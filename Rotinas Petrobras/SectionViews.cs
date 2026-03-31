using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Autodesk.Civil.Settings;
using System;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using SectionViewStyle = Autodesk.Civil.DatabaseServices.Styles.SectionViewStyle;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using System;
using System.Collections.Generic;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using System.Linq;
using Section = Autodesk.Civil.DatabaseServices.Section;
using Autodesk.Civil;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.Aec.Modeler;

namespace AutomacoesCivil3D
{
    public class SectionViews
    {
        [CommandMethod("CriarSampleLinesSectionViews")]
        public static void CriarSampleLinesSectionViews()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            CivilDocument civilDoc = Manager.DocCivil;

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // 1. Selecionar o alinhamento
                    PromptEntityOptions opt = new PromptEntityOptions("\nSelecione um alinhamento: ");
                    opt.SetRejectMessage("\nEntidade não é um alinhamento!");
                    opt.AddAllowedClass(typeof(Alignment), true);

                    PromptEntityResult res = ed.GetEntity(opt);
                    if (res.Status != PromptStatus.OK) return;

                    ObjectId alignmentId = res.ObjectId;

                    // 2. Criar o grupo de sample lines
                    string groupName = "SLG-" + DateTime.Now.ToString("HHmmss");
                    ObjectId sampleLineGroupId = SampleLineGroup.Create(groupName, alignmentId);
                    SampleLineGroup slg = (SampleLineGroup)tr.GetObject(sampleLineGroupId, OpenMode.ForWrite);
                    AddSurfaceToSections(tr, slg, "01-PRIMITIVO");
                    AddCorridorToSections(tr, slg, "VIA-01");
                    //slg.UpgradeFromNotify();
                    SectionSourceCollection ss = slg.GetSectionSources();
                    //slg.GetMaterialSectionSources();


                    foreach ( SectionSource s in ss)
                    {
                       SectionSourceType st = SectionSourceType.TinSurface;
                      
                        SectionSourceCollection sources = slg.GetSectionSources();
                        

                    }




                    // 3. Adicionar sample lines a cada 20 metros
                    Alignment alignment = (Alignment)tr.GetObject(alignmentId, OpenMode.ForRead);
                    double startStation = alignment.StartingStation;
                    double endStation = alignment.EndingStation;
                    double interval = 20.0;

                    List<double> stations = new List<double>();
                    for (double st = startStation; st <= endStation; st += interval)
                    {
                        stations.Add(st);
                    }

                    // Adicionar a estação final se não for múltiplo do intervalo
                    if (stations[stations.Count - 1] < endStation)
                    {
                        stations.Add(endStation);
                    }

                    // 4. Criar as seções
                    foreach (double station in stations)
                    {
                        try
                        {
                            // Criação da sample line (exemplo simplificado)
                            ObjectId sampleId = SampleLine.Create(station.ToString(), sampleLineGroupId, station);
                            SampleLine sampleLine = (SampleLine)tr.GetObject(sampleId, OpenMode.ForWrite);




                        }
                        catch (Exception ex)
                        {
                            ed.WriteMessage($"\nErro na estação {station}: {ex.Message}");
                        }
                    }

                    // 5. Configurar estilos
                    //ObjectId styleId = CivilApplication.ActiveDocument.Styles.SampleLineStyles["Padrão"];
                    //ObjectId labelStyleId = civilDoc.Styles.LabelStyles.SampleLineLabelStyles(;

                    //slg.DefaultSamplineStyleId = styleId;
                    //slg.DefaultSamplineLabelStyleId = labelStyleId;

                    // 6. Criar Section Views
                    if (slg.SectionViewGroups.Count == 0)
                    {
                        // Criar novo grupo de section views
                        CreateSectionViews(tr, civilDoc, slg);


                    }

                    tr.Commit();

                    ed.WriteMessage($"\nCriadas {stations.Count} sample lines e seções com sucesso!");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nErro: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // Método auxiliar para exibir mensagens
        private static void WriteMessage(string message)
        {
            Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage(message);
        }

        private static void CreateSectionViews(Transaction tr, CivilDocument civilDoc, SampleLineGroup slg)
        {

            Point3d point3D = new Point3d(726710.1046, 8827150.9174, 0);
            SectionViewGroup svg = slg.SectionViewGroups.Add(point3D);
            

            double dist = 0;
            Point3d point3D1 = new Point3d(point3D.X, point3D.Y + dist, point3D.Z);
            int cont = 0;

            

            foreach (ObjectId sampleLineId in slg.GetSampleLineIds())
            {
                SampleLine sampleLine = (SampleLine)tr.GetObject(sampleLineId, OpenMode.ForRead);
                
                ObjectId svId = SectionView.Create($"SV {cont} ", sampleLineId, point3D1);
                SectionView sv = (SectionView)tr.GetObject(svId, OpenMode.ForRead);
                dist = dist + 100;
                cont++;

            }
        }

        private static void AddSurfaceToSections(Transaction tr, SampleLineGroup slg, string nomeSuperficie)
        {
            // Buscar a superfície pelo nome
            ObjectId surfaceId = GetSurfaceId(nomeSuperficie);
            if (surfaceId.IsNull) return;

            
            // Adicionar à coleção de fontes da seção
            SectionSourceCollection sources = slg.GetSectionSources();
            

            

            foreach (SectionSource source in sources)
            {
             
                source.IsSampled = true;
                source.GetSectionIds().Add(surfaceId);
                source.IsSampled = true;
                SectionSourceType surfaceType = source.SourceType;
                



            }

        }

        private static void AddCorridorToSections(Transaction tr, SampleLineGroup slg, string nomeCorredor)
        {
            // Buscar o corredor pelo nome
            ObjectId corridorId = GetCorridorId(nomeCorredor);
            if (corridorId.IsNull) return;

            // Adicionar à coleção de fontes da seção
          
        }

        private static ObjectId GetSurfaceId(string nomeSuperficie)
        {
            foreach (ObjectId id in CivilApplication.ActiveDocument.GetSurfaceIds())
            {
                if (id.GetObject(OpenMode.ForRead) is TinSurface surface && surface.Name.Equals(nomeSuperficie))
                {
                    return id;
                }
            }
            return ObjectId.Null;
        }

        private static ObjectId GetCorridorId(string nomeCorredor)
        {
            foreach (ObjectId id in CivilApplication.ActiveDocument.CorridorCollection)
            {
                if (id.GetObject(OpenMode.ForRead) is Corridor corridor && corridor.Name.Equals(nomeCorredor))
                {
                    return id;
                }
            }
            return ObjectId.Null;
        }



        [CommandMethod("CriarSectionViewEixo")]
        public void CriarSectionViewEixo()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // 1. Selecionar o alinhamento "Eixo-01"
                Alignment alin = null;
                foreach (ObjectId alinId in civilDoc.GetAlignmentIds())
                {
                    Alignment a = (Alignment)tr.GetObject(alinId, OpenMode.ForRead);
                    if (a.Name == "EIXO-01") // ajuste o nome conforme seu caso
                    {
                        alin = a;
                        break;
                    }
                }
                if (alin == null)
                {
                    ed.WriteMessage("\nAlinhamento não encontrado.");
                    return;
                }

                // 2. Achar as superfícies: "TN" e "Terraplenagem"
                TinSurface surfTN = null, surfTerraplenagem = null;
                foreach (ObjectId surfId in civilDoc.GetSurfaceIds())
                {
                    TinSurface s = (TinSurface)tr.GetObject(surfId, OpenMode.ForRead);
                    if (s != null)
                    {
                        if (s.Name == "TN")
                            surfTN = s;
                        else if (s.Name == "Terraplenagem")
                            surfTerraplenagem = s;
                    }
                }
                if (surfTN == null || surfTerraplenagem == null)
                {
                    ed.WriteMessage("\nSuperfícies TN ou Terraplenagem não encontradas.");
                    return;
                }

                // 3. Criar Sample Line Group (se não existe)
                ObjectId sampleLineGroupId;
                if (alin.GetSampleLineGroupIds().Count > 0)
                    sampleLineGroupId = alin.GetSampleLineGroupIds()[0];
                else
                    sampleLineGroupId = SampleLineGroup.Create("Secoes Eixo-01", alin.ObjectId);

                SampleLineGroup slg = (SampleLineGroup)tr.GetObject(sampleLineGroupId, OpenMode.ForWrite);

                // 4. Sample Lines (um exemplo simples: só no PI -- para mais, pode amostrar em cada 20m, etc)
                double startSta = alin.StartingStation;
                double endSta = alin.EndingStation;
                double interval = 20.0; // metros

                List<double> stations = new List<double>();
                for (double s = startSta; s <= endSta; s += interval)
                    stations.Add(s);

                // Checa se já existem amostras na posição (opcional)
                foreach (double sta in stations)
                {
                    // Largura padrão (exemplo 50m em cada lado)
                    
                }

                // 5. Vincular superfícies ao grupo de sample lines
                ObjectIdCollection surfaces = new ObjectIdCollection();
                
                    surfaces.Add(surfTN.ObjectId);
                
                    surfaces.Add(surfTerraplenagem.ObjectId);
                
                    SectionSourceCollection sS = slg.GetSectionSources();
//-----------------------ATENÇÃO AQUI-------------------------------------------------------------------------
                foreach (SectionSource s in sS)
                {

                    s.IsSampled = true;
                    s.GetSectionIds().Add(surfTN.ObjectId);
                    s.GetSectionIds().Add(surfTerraplenagem.ObjectId);
                }

                // 6. Criar Section View
                // Solicita um ponto na tela para inserção
                PromptPointResult ppr = ed.GetPoint("\nClique para posicionar a Section View:");
                if (ppr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nComando cancelado ao pedir ponto.");
                    return;
                }

                // Cria o Section View
                ObjectId sectionView = SectionView.Create("SectionViews",slg.ObjectId, ppr.Value);
                SectionView sv = (SectionView)sectionView.GetObject(OpenMode.ForRead);
                sv.Visible = true;  

                ed.WriteMessage("\nSection View criada com superfícies TN e Terraplenagem!");
                tr.Commit();
            }
        }




    }
}