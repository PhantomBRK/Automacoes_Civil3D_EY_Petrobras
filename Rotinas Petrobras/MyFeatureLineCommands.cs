using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Table = Autodesk.AutoCAD.DatabaseServices.Table;
using Entity = Autodesk.Civil.DatabaseServices.Entity;

namespace AutomacoesCivil3D
{
    public class FeatureLineTable
    {
        [CommandMethod("TabelaPIPointsComCogoPointsAtualizavel")]
        public void TabelaPIPointsComCogoPointsAtualizavel()
        {
            Document docCad = Manager.DocCad;
            CivilDocument docCivil = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database docData = Manager.DocData;

            string nomeLayerAuto = "PLATOR_PI_AUTOMATICO";
            string nomeEstiloPonto = "T-RN-TOPOGRAFICO_8mm";

            PromptSelectionOptions selOpts = new PromptSelectionOptions();
            selOpts.MessageForAdding = "\nSelecione as Feature Lines desejadas:";

            TypedValue[] filter = new TypedValue[]
            {
                new TypedValue((int)DxfCode.Start, "AECC_FEATURE_LINE")
            };
            SelectionFilter selFilter = new SelectionFilter(filter);
            PromptSelectionResult selRes = docEditor.GetSelection(selOpts, selFilter);

            if (selRes.Status != PromptStatus.OK)
            {
                docEditor.WriteMessage("\nNenhuma Feature Line selecionada.");
                return;
            }

            PromptPointOptions ppo = new PromptPointOptions("\nSelecione o ponto de inserção da tabela:");
            PromptPointResult ppr = docEditor.GetPoint(ppo);
            if (ppr.Status != PromptStatus.OK)
            {
                docEditor.WriteMessage("\nComando cancelado: ponto de inserção não informado.");
                return;
            }
            Point3d posTabela = ppr.Value;

            ObjectId pontoStyleId = ObterEstiloPontoId(docCivil, nomeEstiloPonto);
            using (Transaction tr = docData.TransactionManager.StartTransaction())
            {
                ObjectId layerId = CriarOuObterLayer(docData, nomeLayerAuto, tr);

                // --- APAGAR TUDO DO LAYER ---
                ApagarObjetosDoLayer(docData, tr, nomeLayerAuto, docCivil);

                List<Tuple<string, Point3d>> listaPontos = new List<Tuple<string, Point3d>>();
                int contador = 1;
                foreach (SelectedObject selObj in selRes.Value)
                {
                    if (selObj == null) continue;
                    FeatureLine fline = (FeatureLine)tr.GetObject(selObj.ObjectId, OpenMode.ForRead);

                    int piCount = fline.PIPointsCount;
                    for (int i = 0; i < piCount; i++)
                    {
                        Point3d vertice = fline.GetPointAtParameter(i);
                        string nomePonto = $"P{contador++}";
                        listaPontos.Add(Tuple.Create(nomePonto, vertice));
                    }
                }
                if (listaPontos.Count == 0)
                {
                    docEditor.WriteMessage("\nNenhum PI Point coletado.");
                    return;
                }

                // Criação da tabela
                CriarTabelaPI(tr, docData, listaPontos, posTabela, layerId);

                // Inserção dos rótulos nos vértices
                InserirTagsNosVertices(tr, docData, listaPontos, layerId);

                // Criação dos marcadores de ponto Civil 3D (CogoPoints)
                //CogoPoint pt = (CogoPoint)docCivil.CogoPoints.Add(vertices, desc, true).GetObject(OpenMode.ForWrite);
               // pt.StyleId = pointStyleId;
               // pt.RawDescription = desc;
               // pt.DescriptionFormat = desc;

                tr.Commit();
            }
        }

        private ObjectId CriarOuObterLayer(Database docData, string nomeLayer, Transaction tr)
        {
            LayerTable lt = (LayerTable)tr.GetObject(docData.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(nomeLayer))
            {
                LayerTableRecord ltr = new LayerTableRecord
                {
                    Name = nomeLayer
                };
                lt.UpgradeOpen();
                ObjectId layerId = lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
                return layerId;
            }
            else
            {
                return lt[nomeLayer];
            }
        }

        private void ApagarObjetosDoLayer(Database docData, Transaction tr, string nomeLayer, CivilDocument docCivil)
        {
            // Apagar DBText e Table
            BlockTable bt = (BlockTable)tr.GetObject(docData.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(docData.CurrentSpaceId, OpenMode.ForRead);
            List<ObjectId> paraApagar = new List<ObjectId>();

            foreach (ObjectId entId in btr)
            {
                Entity ent = (Entity)tr.GetObject(entId, OpenMode.ForRead);
                if (ent.Layer == nomeLayer &&
                    (ent is DBText || ent is Table))
                {
                    paraApagar.Add(entId);
                }
            }
            foreach (ObjectId delId in paraApagar)
            {
                Entity ent = (Entity)tr.GetObject(delId, OpenMode.ForWrite);
                ent.Erase();
            }

            // Apagar CogoPoints criados (por layer e Description)
            foreach (ObjectId ptId in docCivil.CogoPoints)
            {
                try
                {
                    CogoPoint pt = (CogoPoint)tr.GetObject(ptId, OpenMode.ForRead);
                    if (pt.Layer == nomeLayer &&
                        pt.RawDescription.Contains("AUTOGERADO"))
                    {
                        pt.UpgradeOpen();
                        pt.Erase();
                    }
                }
                catch { /* Pontos podem ter sido apagados em sessões anteriores */ }
            }
        }

        private ObjectId ObterEstiloPontoId(CivilDocument docCivil, string nomeEstilo)
        {
            ObjectId pontoStyleId = ObjectId.Null;
            var estilos = docCivil.Styles.PointStyles;
            foreach (ObjectId estiloId in estilos)
            {
                PointStyle estilo = (PointStyle)estiloId.GetObject(OpenMode.ForRead);
                if (estilo.Name.Equals(nomeEstilo, StringComparison.OrdinalIgnoreCase))
                {
                    pontoStyleId = estiloId;
                    break;
                }
            }
          
            return pontoStyleId;
        }

        private void CriarTabelaPI(Transaction tr, Database docData, List<Tuple<string, Point3d>> listaPontos, Point3d posTabela, ObjectId layerId)
        {
            Table tabela = new Table();
            tabela.TableStyle = docData.Tablestyle;
            tabela.SetSize(listaPontos.Count + 1, 4);
            tabela.SetRowHeight(4);
            tabela.SetColumnWidth(25);
            tabela.LayerId = layerId;

            tabela.Cells[0, 0].TextString = "Ponto";
            tabela.Cells[0, 1].TextString = "X";
            tabela.Cells[0, 2].TextString = "Y";
            tabela.Cells[0, 3].TextString = "Z";

            for (int i = 0; i < listaPontos.Count; i++)
            {
                tabela.Cells[i + 1, 0].TextString = listaPontos[i].Item1;
                tabela.Cells[i + 1, 1].TextString = listaPontos[i].Item2.X.ToString("F3");
                tabela.Cells[i + 1, 2].TextString = listaPontos[i].Item2.Y.ToString("F3");
                tabela.Cells[i + 1, 3].TextString = listaPontos[i].Item2.Z.ToString("F3");
            }

            tabela.Position = posTabela;
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(docData.CurrentSpaceId, OpenMode.ForWrite);
            btr.AppendEntity(tabela);
            tr.AddNewlyCreatedDBObject(tabela, true);
        }

        private void InserirTagsNosVertices(Transaction tr, Database docData, List<Tuple<string, Point3d>> listaPontos, ObjectId layerId)
        {
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(docData.CurrentSpaceId, OpenMode.ForWrite);
            foreach (var ponto in listaPontos)
            {
                DBText dbText = new DBText
                {
                    Position = ponto.Item2,
                    Height = 1.0,
                    TextString = ponto.Item1,
                    LayerId = layerId
                };
                btr.AppendEntity(dbText);
                tr.AddNewlyCreatedDBObject(dbText, true);
            }
        }

        
    }
}