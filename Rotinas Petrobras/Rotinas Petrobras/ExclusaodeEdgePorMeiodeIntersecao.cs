using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using System.Collections.Generic;
using OpenMode = Autodesk.AutoCAD.DatabaseServices.OpenMode;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutomacoesCivil3D
{
    public class ExclusaodeEdgePorMeiodeIntersecao
    {
        [CommandMethod("Intersecao")]
        public void IntesecaoSurfacePoly(Polyline exterior2D, TinSurface superficie)
        {
            Document docCad = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            CivilDocument docCivil = CivilApplication.ActiveDocument;
            Editor docEditor = docCad.Editor;
            Database docData = docCad.Database;

            try
            {
                using (Transaction tr = docData.TransactionManager.StartTransaction())
                {
                    

                    BlockTable bt = (BlockTable)tr.GetObject(docData.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // 1. Coletar todos os pontos de intersecção possíveis
                    var intersecoes = new List<Point2d>();

                    foreach (TinSurfaceTriangle tri in superficie.Triangles)
                    {
                        Point2d[] triPts = new Point2d[] {
                            new Point2d(tri.Vertex1.Location.X, tri.Vertex1.Location.Y),
                            new Point2d(tri.Vertex2.Location.X, tri.Vertex2.Location.Y),
                            new Point2d(tri.Vertex3.Location.X, tri.Vertex3.Location.Y),
                        };

                        // Checa cada edge do triângulo
                        for (int t = 0; t < 3; t++)
                        {
                            Point2d A = triPts[t];
                            Point2d B = triPts[(t + 1) % 3];

                            // Para cada segmento da polyline...
                            int nSegments = exterior2D.NumberOfVertices - (exterior2D.Closed ? 0 : 1);
                            for (int j = 0; j < nSegments; j++)
                            {
                                Point2d C = exterior2D.GetPoint2dAt(j);
                                Point2d D = exterior2D.GetPoint2dAt((j + 1) % exterior2D.NumberOfVertices);

                                // Calcula interseção
                                if (SegmentIntersect(A, B, C, D, out Point2d interPt))
                                {
                                    // Adiciona SE ainda não existe (por causa da tolerância)
                                    bool jaExiste = false;
                                    foreach (Point2d ep in intersecoes)
                                    {
                                        if (ep.GetDistanceTo(interPt) < 0.001)
                                        {
                                            jaExiste = true;
                                            break;
                                        }
                                    }
                                    if (!jaExiste)
                                        intersecoes.Add(interPt);

                                    
                                }
                            }
                        }
                    }

                    // 2. Só agora, apague todas as edges de uma vez
                    int apagadas = 0;
                    foreach (var ponto in intersecoes)
                    {
                        TryDeleteEdge(superficie, ponto, docEditor);
                        apagadas++;
                    }

                    superficie.Rebuild();
                    docEditor.WriteMessage($"\nTotal de edges apagadas: {apagadas}");

                    tr.Commit();
                }
            }
            catch (System.Exception e)
            {
                docEditor.WriteMessage("\nErro: " + e.Message + "\nStack: " + e.StackTrace);
            }
        }

        // Função de interseção entre segmentos 2D
        public bool SegmentIntersect(Point2d A, Point2d B, Point2d C, Point2d D, out Point2d intersection)
        {
            intersection = new Point2d();
            double denom = (B.X - A.X) * (D.Y - C.Y) - (B.Y - A.Y) * (D.X - C.X);
            if (Math.Abs(denom) < 1e-10)
                return false; // Paralelos ou coincidentes

            double num1 = (A.Y - C.Y) * (D.X - C.X) - (A.X - C.X) * (D.Y - C.Y);
            double num2 = (A.Y - C.Y) * (B.X - A.X) - (A.X - C.X) * (B.Y - A.Y);

            double t1 = num1 / denom;
            double t2 = num2 / denom;

            if (t1 < 0 || t1 > 1 || t2 < 0 || t2 > 1)
                return false; // Fora dos segmentos

            // Interseção encontrada
            intersection = new Point2d(
                A.X + t1 * (B.X - A.X),
                A.Y + t1 * (B.Y - A.Y)
            );
            return true;
        }

        private void TryDeleteEdge(TinSurface superficie, Point2d ponto, Editor docEditor, bool rebuild = false)
        {
            try
            {
                var edge = superficie.FindEdgeAtXY(ponto.X, ponto.Y);
                if (edge != null)
                {
                    superficie.DeleteLine(edge);
                    if (rebuild) superficie.Rebuild();
                }
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\nProblema ao apagar edge em {ponto.X},{ponto.Y}: {ex.Message}");
            }
        }

    }
}