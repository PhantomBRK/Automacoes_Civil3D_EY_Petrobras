using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices.Styles;
using Autodesk.Civil;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Table = Autodesk.AutoCAD.DatabaseServices.Table;

namespace AutomacoesCivil3D
{
    public class FeatureLineToPoints
    {
        [CommandMethod("ExtractFeatureLinePoints")]
        public void ExtractFeatureLinePointsCommand()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

          

            try
            {
                // Obter o documento ativo do Civil 3D
                CivilDocument civilDoc = CivilApplication.ActiveDocument;

                // Solicitar ao usuário para selecionar Feature Lines
                PromptEntityOptions peo = new PromptEntityOptions("\nSelecione uma Feature Line: ");
                peo.SetRejectMessage("\nPor favor, selecione apenas Feature Lines.");
                peo.AddAllowedClass(typeof(FeatureLine), false);

                List<FeatureLine> featureLines = new List<FeatureLine>();
                while (true)
                {
                    PromptEntityResult per = ed.GetEntity(peo);
                    if (per.Status != PromptStatus.OK)
                        break;

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        FeatureLine fl = (FeatureLine)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                        if (fl != null)
                        {
                            featureLines.Add(fl);
                        }
                        tr.Commit();
                    }
                }

                if (featureLines.Count == 0)
                {
                    ed.WriteMessage("\nNenhuma Feature Line selecionada.");
                    return;
                }

                // Criar uma lista para armazenar as coordenadas dos pontos
                List<PointData> pointsData = new List<PointData>();
                int pointNumber = 1;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Iterar sobre cada Feature Line selecionada
                    foreach (FeatureLine fl in featureLines)
                    {
                        // Obter os pontos de vértice da Feature Line
                        Point3dCollection points = fl.GetPoints(FeatureLinePointType.AllPoints);
                        foreach (Point3d point in points)
                        {
                            pointsData.Add(new PointData
                            {
                                PointName = $"P{pointNumber}",
                                X = point.X,
                                Y = point.Y,
                                Z = point.Z
                            });
                            pointNumber++;
                        }
                    }

                    // Criar Cogo Points e associar ao estilo especificado
                    CogoPointCollection cogoPoints = civilDoc.CogoPoints;

                    // Tentar obter o estilo de ponto especificado; se não existir, usar um estilo padrão
                    ObjectId pointStyle = civilDoc.Styles.PointStyles["T-RN-TOPOGRAFICO_8mm"];
                    if (pointStyle == null)
                    {
                        ed.WriteMessage("\nEstilo de ponto 'T-RN-TOPOGRAFICO_8mm' não encontrado. Usando estilo padrão.");
                        // Use o estilo padrão ou crie um novo se necessário
                        // Exemplo de uso de estilo padrão:
                        pointStyle = civilDoc.Styles.PointStyles["Standard"]; // Garanta que "Standard" exista
                    }

                    foreach (PointData pd in pointsData)
                    {
                        ObjectId pointId = cogoPoints.Add(new Point3d(pd.X, pd.Y, pd.Z), "", false);
                        CogoPoint cogoPoint = (CogoPoint)tr.GetObject(pointId, OpenMode.ForWrite);
                        if (cogoPoint != null)
                        {
                            cogoPoint.PointName = pd.PointName;
                            cogoPoint.StyleId = pointStyle.ConvertToRedirectedId();
                        }
                    }

                    // Criar tabela com as coordenadas
                    CreateTableWithCoordinates(db, ed, pointsData);

                    tr.Commit();
                }

                ed.WriteMessage($"\n{pointsData.Count} pontos criados com sucesso.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nErro: {ex.Message}");
            }
        }

        private void CreateTableWithCoordinates(Database db, Editor ed, List<PointData> pointsData)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                // Criar uma tabela
                Table table = new Table();
                table.SetSize(pointsData.Count + 1, 4); // Linhas (pontos + cabeçalho), Colunas (Nome, X, Y, Z)
                table.SetRowHeight(0, 3.0);
                
                
                for (int i = 1; i <= pointsData.Count; i++)
                {
                    table.SetRowHeight(i, 2.5);
                }
                table.SetColumnWidth(0, 15.0); // Coluna Nome do Ponto
                table.SetColumnWidth(1, 20.0); // Coluna X
                table.SetColumnWidth(2, 20.0); // Coluna Y
                table.SetColumnWidth(3, 20.0); // Coluna Z

                // Definir cabeçalhos
                table.Cells[0, 0].TextString = "Ponto";
                table.Cells[0, 1].TextString = "X (East)";
                table.Cells[0, 2].TextString = "Y (North)";
                table.Cells[0, 3].TextString = "Z (Elevação)";

                // Preencher tabela com dados
                for (int i = 0; i < pointsData.Count; i++)
                {
                    table.Cells[i + 1, 0].TextString = pointsData[i].PointName;
                    table.Cells[i + 1, 1].TextString = pointsData[i].X.ToString("F3");
                    table.Cells[i + 1, 2].TextString = pointsData[i].Y.ToString("F3");
                    table.Cells[i + 1, 3].TextString = pointsData[i].Z.ToString("F3");
                    table.Cells[i + 1, 0].TextHeight = 2;
                    table.Cells[i + 1, 1].TextHeight = 2;
                    table.Cells[i + 1, 2].TextHeight = 2;
                    table.Cells[i + 1, 3].TextHeight = 2;
                }

                // Solicitar posição para inserir a tabela
                PromptPointResult ppr = ed.GetPoint("\nSelecione o ponto de inserção da tabela: ");
                if (ppr.Status == PromptStatus.OK)
                {
                    table.Position = ppr.Value;
                    btr.AppendEntity(table);
                    tr.AddNewlyCreatedDBObject(table, true);
                }

                // Definir estilo da tabela (opcional)
                table.TableStyle = db.Tablestyle; // Usar o estilo de tabela padrão do desenho

                tr.Commit();
            }
        }

        private class PointData
        {
            public string PointName { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
        }
    }
}