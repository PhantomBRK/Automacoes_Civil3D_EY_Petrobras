
using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using System.Net;
using Surface = Autodesk.Civil.DatabaseServices.Surface;
using Autodesk.Civil.DatabaseServices.Styles;
using Autodesk.Aec.Modeler;
using System.Windows.Forms.Design;

namespace AutomacoesCivil3D
{
    public class CriaFeatureCaixasDRE
    {

        [CommandMethod("CriarFeatureLinePorRede")]
        public void CriarFeatureLinePorRede()
        {
            Document docCad = Manager.DocCad;
            CivilDocument docCivil = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database docData = Manager.DocData;

            // Solicita seleção da rede
            PromptEntityOptions peo = new PromptEntityOptions("\nSelecione a rede de drenagem (PipeNetwork):");
            peo.SetRejectMessage("\nPor favor, selecione apenas Tubos da Rede.");
            peo.AddAllowedClass(typeof(Pipe), false);

            PromptEntityResult per = docEditor.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
            {
                docEditor.WriteMessage("\nNão foi possível selecionar uma rede de drenagem.");
                return;
            }




            using (Transaction tr = docData.TransactionManager.StartTransaction())
            {
                Pipe tuboSelect = (Pipe)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                ObjectId networkId = tuboSelect.NetworkId;

                Network net = (Network)tr.GetObject(networkId, OpenMode.ForRead);
                ObjectId surfaceBaseId = net.ReferenceSurfaceId;
                TinSurface superficieBase = (TinSurface)surfaceBaseId.GetObject(OpenMode.ForRead);



                ObjectId surfaceStyleId = ObjectId.Null;
                //Percorre todos os estilos de superficies do template
                foreach (ObjectId styleId in docCivil.Styles.SurfaceStyles)
                {
                    //Abre o estilo de acordo com o ObjectId
                    SurfaceStyle surfaceStyle = (SurfaceStyle)styleId.GetObject(OpenMode.ForRead);
                    //Filtra o estilo aberto por nome
                    if (surfaceStyle.Name == "TRIANGULOS VERMELHO")
                    {
                        //Define o id do estilo ao ObjectId criado anteriormente se o nome dele for o escolhido no filtro
                        surfaceStyleId = surfaceStyle.Id;
                    }
                }


                //Cria um objectId de superficie com o nome e estiloSelecionados
                ObjectId surface1 = TinSurface.Create("EscavaçãoDrenagemTubos", surfaceStyleId);
                //Adciona essa superficie ao projeto
                TinSurface superficie = (TinSurface)surface1.GetObject(OpenMode.ForRead);
                //Rebuida a superfie
                superficie.AutoRebuild = true;

                //Cria um objectId de superficie com o nome e estiloSelecionados
                ObjectId surfaceCaixasId = TinSurface.Create("EscavaçãoDrenagemCaixas", surfaceStyleId);
                //Adciona essa superficie ao projeto
                TinSurface superficieCaixas = (TinSurface)surface1.GetObject(OpenMode.ForRead);
                //Rebuida a superfie
                superficie.AutoRebuild = true;


                // Coleta todos os tubos da rede (sequência bruta, para refinar, ver nota*)
                List<Point3d> verticesLinhaCentral = new List<Point3d>();
                List<Point3d> verticesLinhaInfDir = new List<Point3d>();
                List<Point3d> verticesLinhaInfEsq = new List<Point3d>();
                List<Point3d> verticesLinhaSupDir = new List<Point3d>();
                List<Point3d> verticesLinhaSupEsq = new List<Point3d>();
                List<Point3d> verticesLinhaMedDir = new List<Point3d>();
                List<Point3d> verticesLinhaMedEsq = new List<Point3d>();
                List<Point3d> cantos = new List<Point3d>();
                List<Point3d> cantosAbaixo = new List<Point3d>();
                string nomeEstrutura = "";

                foreach (ObjectId structureId in net.GetStructureIds())
                {
                    Structure structure = (Structure)tr.GetObject(structureId, OpenMode.ForRead);
                    if(structure.BoundingShape == BoundingShapeType.Undefined) 
                        continue;
                    if (structure.BoundingShape == BoundingShapeType.Cylinder)
                        continue;

                    Point3d centro = structure.Location;
                    double largura = structure.DiameterOrWidth;
                    double comprimento = structure.Length;
                    double larguraBase = structure.DiameterOrWidth;
                    double comprimentoBase = structure.Length;
                    double thetaRad = structure.Rotation;

                    // Vetor unitário ao longo do COMPRIMENTO (direção principal da caixa)
                    Vector3d dirComp = new Vector3d(Math.Cos(thetaRad), Math.Sin(thetaRad), 0).GetNormal();
                    // Vetor unitário ao longo da LARGURA (perpendicular ao comprimento, sentido anti-horário)
                    Vector3d dirLarg = dirComp.CrossProduct(Vector3d.ZAxis).GetNormal();

                    // Metade de cada lado
                    Vector3d metadeComp = dirComp * (comprimento / 2);
                    Vector3d metadeLarg = dirLarg * (largura / 2);

                    // Cantos em relação ao centro, sentido anti-horário
                    Point3d canto1 = centro + metadeComp + metadeLarg;     // Canto superior direito
                    Point3d canto2 = centro + metadeComp - metadeLarg;     // Canto inferior direito
                    Point3d canto3 = centro - metadeComp - metadeLarg;     // Canto inferior esquerdo
                    Point3d canto4 = centro - metadeComp + metadeLarg;     // Canto superior esquerdo

                    // Se quiser uma lista:
                    cantos = new List<Point3d> { canto1, canto2, canto3, canto4 };

                    

                    //Adiciona as featureLines no topo da Caixa
                    AdcionaBreakLines(superficieCaixas, CriaFeatureLine(cantos, $"Caixa {structure.Name} Topo", "", surfaceBaseId));

                  


                }



                /*foreach (ObjectId pipeId in net.GetPipeIds())
                {
                    Pipe tubo = (Pipe)tr.GetObject(pipeId, OpenMode.ForRead);

                    double afastamento = CalcularLarguraVala(tubo.OuterDiameterOrWidth);

                    double startX = tubo.StartPoint.X;
                    double startY = tubo.StartPoint.Y;
                    double startZ = tubo.StartPoint.Z;
                    double endX = tubo.EndPoint.X;
                    double endY = tubo.EndPoint.Y;
                    double endZ = tubo.EndPoint.Z;

                    double profundidadeInicio = superficieBase.FindElevationAtXY(startX, startY) - (startZ + tubo.OuterDiameterOrWidth / 2 + 0.05);
                    double profundidadeFim = superficieBase.FindElevationAtXY(endX, endY) - endZ - (startZ + tubo.OuterDiameterOrWidth / 2 + 0.05);
                    double profundidade = (profundidadeInicio + profundidadeFim) / 2;

                    Point3d startLID = new Point3d(startX + afastamento, startY, (startZ - (tubo.OuterDiameterOrWidth / 2) - 0.05));
                    Point3d endLID = new Point3d(endX + afastamento, endY, (endZ - (tubo.OuterDiameterOrWidth / 2) - 0.05));
                    Point3d startLIE = new Point3d(startX - afastamento, startY, (startZ - (tubo.OuterDiameterOrWidth / 2) - 0.05));
                    Point3d endLIE = new Point3d(endX - afastamento, endY, (endZ - (tubo.OuterDiameterOrWidth / 2) - 0.05));

                    //Adiciona os vertices a direita inferior do tubo
                    verticesLinhaInfDir.Add(startLID);
                    verticesLinhaInfDir.Add(endLID);
                    //Adiciona os vertices a direita inferior do tubo
                    verticesLinhaInfEsq.Add(startLIE);
                    verticesLinhaInfEsq.Add(endLIE);

                    if (profundidade < 1.25 || profundidade > 1.75)
                    {
                        Point3d startLSD = new Point3d(startX + afastamento + 0.05, startY, superficieBase.FindElevationAtXY((startX + afastamento + 0.01), startY));
                        Point3d endLSD = new Point3d(endX + afastamento + 0.05, endY, superficieBase.FindElevationAtXY((endX + afastamento + 0.01), endY));
                        Point3d startLSE = new Point3d(startX - afastamento - 0.05, startY, superficieBase.FindElevationAtXY((startX - afastamento - 0.01), startY));
                        Point3d endLSE = new Point3d(endX - afastamento - 0.05, endY, superficieBase.FindElevationAtXY((endX - afastamento - 0.01), endY));
                        //Adiciona os vertices a direita superior do tubo
                        verticesLinhaSupDir.Add(startLSD);
                        verticesLinhaSupDir.Add(endLSD);
                        //Adiciona os vertices a direita superior do tubo
                        verticesLinhaSupEsq.Add(startLSE);
                        verticesLinhaSupEsq.Add(endLSE);

                    }
                    else
                    {
                        if (profundidade > 1.25 && profundidade < 1.75)
                        {
                            Point3d startLMD = new Point3d(startX + afastamento + 0.01, startY, (startZ - (tubo.OuterDiameterOrWidth / 2) - 0.05) + 1.25);
                            Point3d endLMD = new Point3d(endX + afastamento + 0.01, endY, (endZ - (tubo.OuterDiameterOrWidth / 2) - 0.05) + 1.25);
                            Point3d startLME = new Point3d(startX - afastamento - 0.01, startY, (startZ - (tubo.OuterDiameterOrWidth / 2) + 1.25));
                            Point3d endLME = new Point3d(endX - afastamento - 0.01, endY, (endZ - (tubo.OuterDiameterOrWidth / 2) - 0.05) + 1.25);
                            //Adiciona os vertices a direita superior do tubo
                            verticesLinhaMedDir.Add(startLMD);
                            verticesLinhaMedDir.Add(endLMD);
                            //Adiciona os vertices a direita superior do tubo
                            verticesLinhaMedEsq.Add(startLME);
                            verticesLinhaMedEsq.Add(endLME);


                            Point3d startLSD = new Point3d(startX + afastamento + 0.5, startY, superficieBase.FindElevationAtXY((startX + afastamento + 0.01), startY));
                            Point3d endLSD = new Point3d(endX + afastamento + 0.5, endY, superficieBase.FindElevationAtXY((endX + afastamento + 0.01), endY));
                            Point3d startLSE = new Point3d(startX - afastamento - 0.5, startY, superficieBase.FindElevationAtXY((startX - afastamento - 0.01), startY));
                            Point3d endLSE = new Point3d(endX - afastamento - 0.5, endY, superficieBase.FindElevationAtXY((endX - afastamento - 0.01), endY));
                            //Adiciona os vertices a direita superior do tubo
                            verticesLinhaSupDir.Add(startLSD);
                            verticesLinhaSupDir.Add(endLSD);
                            //Adiciona os vertices a direita superior do tubo
                            verticesLinhaSupEsq.Add(startLSE);
                            verticesLinhaSupEsq.Add(endLSE);
                        }



                    }









                }*/










                /*/Adiciona as featureLines no fundo da vala
                AdcionaBreakLines(superficie, AdionaVerticesNaSuperficie(verticesLinhaInfDir, "InfDireita", "", surfaceBaseId));
                AdcionaBreakLines(superficie, AdionaVerticesNaSuperficie(verticesLinhaInfEsq, "InfEsquerda", "", surfaceBaseId));
                
                //Adiciona featureLines como breaklines centrais caso a vala tenha mais de 1.25m de altura
                if (verticesLinhaMedDir.Count > 0)
                {
                    AdcionaBreakLines(superficie, AdionaVerticesNaSuperficie(verticesLinhaMedDir, "MedDireita", "", surfaceBaseId));
                    AdcionaBreakLines(superficie, AdionaVerticesNaSuperficie(verticesLinhaMedEsq, "MedEsquerda", "", surfaceBaseId));
                }

                //Adiciona as featureLines no topo da vala
                AdcionaBreakLines(superficie, AdionaVerticesNaSuperficie(verticesLinhaSupDir, "SupDireita", "Superior", surfaceBaseId));
                AdcionaBreakLines(superficie, AdionaVerticesNaSuperficie(verticesLinhaSupEsq, "SupEsquerda", "Superior", surfaceBaseId));*/




                tr.Commit();
            }

        }

        //Metodo que adiciona as featureLines como BreakLines a Superficie
        public void AdcionaBreakLines(TinSurface superficie, ObjectIdCollection featLineColl)
        {
            //superficie.BreaklinesDefinition.AddStandardBreaklines(featLineColl, 1.0, 0, 0, 0);

        }

        //Metodo que cria as featureLines a partir da lista de pontos3d e nome
        public ObjectIdCollection CriaFeatureLine(List<Point3d> vertices, string nome, string posicao, ObjectId superficie)
        {
            Document docCad = Manager.DocCad;
            CivilDocument docCivil = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database docData = Manager.DocData;
            //Cria uma coleção de objectIds para as featureslines que serão usadas como breakLines
            ObjectIdCollection featLineColl = new ObjectIdCollection();
            // Evita pontos repetidos seguidos
            List<Point3d> verticesDistinct = new List<Point3d>();
            foreach (Point3d pt in vertices)
            {
                if (verticesDistinct.Count == 0 || !pt.IsEqualTo(verticesDistinct[verticesDistinct.Count - 1], new Tolerance(0.001, 0.001)))
                    verticesDistinct.Add(pt);
            }

            Point3dCollection pontosCollectionCentral = new Point3dCollection();
            foreach (Point3d pt in verticesDistinct)
                pontosCollectionCentral.Add(pt);

            bool estado = false;

            if (nome.Contains("Caixa"))
            {


                estado = true;
            }

            Polyline3d poly3d = new Polyline3d(Poly3dType.SimplePoly, pontosCollectionCentral, estado);

            using (Transaction tr = docData.TransactionManager.StartTransaction())
            {


                ObjectId siteId = ObjectId.Null;
                ObjectId siteId2 = ObjectId.Null;


                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(docData.CurrentSpaceId, OpenMode.ForWrite);
                btr.AppendEntity(poly3d);
                tr.AddNewlyCreatedDBObject(poly3d, true);

                ObjectIdCollection sites = docCivil.GetSiteIds();
                foreach (ObjectId siteIds in sites)
                {


                    Site site = (Site)tr.GetObject(siteIds, OpenMode.ForRead);
                    if (site.Name == "PROJETADO")
                    {

                        siteId = site.Id;

                    }
                    else
                    {

                        siteId2 = site.Id;

                    }



                }

                //Define os pontos na elevação da superficie caso sejam linhas superiores
                if (nome.Contains("Caixa"))
                {
                    siteId = siteId2;

                }


                //Cria um objectId de uma featureLine nova
                ObjectId flId = FeatureLine.Create(nome, poly3d.ObjectId, siteId);
                //Cria essa feature atraves desse ObjectId
                FeatureLine featureLine = (FeatureLine)tr.GetObject(flId, OpenMode.ForRead);

                if (posicao == "Superior")
                {
                    featureLine.AssignElevationsFromSurface(superficie, true);


                }

                featLineColl.Add(flId);

                poly3d.Erase();

                tr.Commit();
            }
            return featLineColl;
        }


        // 🔹 Cálculo da largura da vala (baseado no diâmetro do tubo)
        private double CalcularLarguraVala(double D)
        {
            if (D <= 0.40) return 0.40;
            if (D > 0.40 && D <= 0.80) return D / 2 + 0.30;
            return D / 2 + 0.20;
        }

        // 🔹 Cálculo da profundidade da vala (H)
        private double CalcularProfundidadeVala(double D, double ZInicio, double ZFim)
        {
            double profundidadeMedia = Math.Abs(ZInicio - ZFim); // Média das profundidades

            if (profundidadeMedia <= 1.25) return 1.25; // Profundidade mínima
            if (profundidadeMedia <= 1.75) return 1.75; // Segundo caso
            return profundidadeMedia; // Se for mais profunda, adicionar margem de segurança
        }
    }
}
