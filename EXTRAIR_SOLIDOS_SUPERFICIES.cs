using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutomacoesCivil3D
{
    public class TinToMesh
    {
        // Comando: seleciona 1+ TinSurface e, opcionalmente, um limite (Polyline),
        // e gera uma PolyFaceMesh rápida com os triângulos do TIN dentro do limite.
        [CommandMethod("TIN_NATIVO_PARA_MESH")]
        public static void TinNativoParaMesh()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            CivilDocument civilDb = Manager.DocCivil;
            Database db = civilDoc.Database;

            try
            {
                // 1) Seleção de superfícies TIN
                PromptSelectionOptions selOpts = new PromptSelectionOptions();
                selOpts.MessageForAdding = "\nSelecione as superfícies TIN:";
                PromptSelectionResult selRes = docEditor.GetSelection(selOpts);
                if (selRes.Status != PromptStatus.OK)
                {
                    docEditor.WriteMessage("\nOperação cancelada (nenhuma superfície selecionada).");
                    return;
                }

                // 2) Limite opcional (Polyline)
                PromptEntityOptions peo = new PromptEntityOptions("\nSelecione um limite (Polyline) ou <Enter> para usar a extensão completa das superfícies: ");
                peo.SetRejectMessage("\nApenas Polyline.");
                peo.AddAllowedClass(typeof(Polyline), exactMatch: true);
                PromptEntityResult per = docEditor.GetEntity(peo);
                ObjectId boundaryId = ObjectId.Null;

                using (Transaction tr0 = db.TransactionManager.StartTransaction())
                {
                    if (per.Status == PromptStatus.OK && per.ObjectId != ObjectId.Null)
                    {
                        Polyline tmp = (Polyline)tr0.GetObject(per.ObjectId, OpenMode.ForRead);
                        boundaryId = per.ObjectId;
                    }
                    tr0.Commit();
                }

                // 3) Construção da Mesh
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btrMs = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    Polyline boundary = null;
                    if (boundaryId != ObjectId.Null)
                    {
                        boundary = (Polyline)tr.GetObject(boundaryId, OpenMode.ForRead);
                    }

                    // Dicionário de vértices (deduplicação) com gradeamento por tolerância
                    double tol = 1e-4; // ~0.1 mm; ajuste conforme necessário
                    Dictionary<(long X, long Y, long Z), int> vertexIndexByKey = new Dictionary<(long, long, long), int>(1_000_000);
                    List<Point3d> vertexBuffer = new List<Point3d>(1_000_000);
                    List<(int A, int B, int C)> faceBuffer = new List<(int A, int B, int C)>(2_000_000);

                    int totalSurfaces = 0;
                    int totalTriangulos = 0;

                    // Pré-carrega limite extents (para poda rápida)
                    Extents3d? boundaryExt = null;
                    if (boundary != null)
                    {
                        boundaryExt = boundary.GeometricExtents;
                    }

                    foreach (SelectedObject so in selRes.Value)
                    {
                        if (so.ObjectId == ObjectId.Null)
                            continue;

                        Autodesk.Civil.DatabaseServices.Surface supBase =
                            (Autodesk.Civil.DatabaseServices.Surface)tr.GetObject(so.ObjectId, OpenMode.ForRead);

                        TinSurface ts = supBase as TinSurface;
                        if (ts == null)
                            continue;

                        totalSurfaces++;

                        // Extents básicos da superfície (para poda por bounding box)
                        Extents3d surfExt = ts.GeometricExtents;

                        // Se há limite, verifica se há interseção de extents
                        if (boundaryExt.HasValue && !ExtentsIntersect(surfExt, boundaryExt.Value))
                            continue;

                        // Enumerar triângulos do TIN
                        // IMPORTANTE: este método depende da sua versão do AeccDBMgd.
                        // Eu deixei o miolo isolado para você plugar as chamadas corretas.
                        foreach ((Point3d A, Point3d B, Point3d C) tri in EnumerarTriangulosTin(ts, tr))
                        {
                            // Poda por extents do limite (rápida)
                            if (boundaryExt.HasValue)
                            {
                                if (!TriangleExtentsIntersect(tri, boundaryExt.Value))
                                    continue;
                            }

                            // Recorte por limite (opcional): testa centróide dentro do limite
                            if (boundary != null)
                            {
                                Point3d centroid = new Point3d(
                                    (tri.A.X + tri.B.X + tri.C.X) / 3.0,
                                    (tri.A.Y + tri.B.Y + tri.C.Y) / 3.0,
                                    (tri.A.Z + tri.B.Z + tri.C.Z) / 3.0
                                );

                                if (!IsPointInsidePolyline(boundary, centroid))
                                    continue;
                            }

                            int ia = GetOrAddVertexIndex(vertexIndexByKey, vertexBuffer, tri.A, tol);
                            int ib = GetOrAddVertexIndex(vertexIndexByKey, vertexBuffer, tri.B, tol);
                            int ic = GetOrAddVertexIndex(vertexIndexByKey, vertexBuffer, tri.C, tol);

                            faceBuffer.Add((ia, ib, ic));
                            totalTriangulos++;
                        }
                    }

                    if (faceBuffer.Count == 0)
                    {
                        docEditor.WriteMessage("\nNenhum triângulo dentro do limite/seleção.");
                        tr.Commit();
                        return;
                    }

                    // Cria PolyFaceMesh
                    PolyFaceMesh pfm = new PolyFaceMesh();
                    btrMs.AppendEntity(pfm);
                    tr.AddNewlyCreatedDBObject(pfm, true);

                    // Adiciona vértices (1-based indexing para FaceRecord)
                    // Observação: PolyFaceMeshVertex é um DBObject “filho” no mesmo BTR.
                    for (int i = 0; i < vertexBuffer.Count; i++)
                    {
                        PolyFaceMeshVertex vtx = new PolyFaceMeshVertex(vertexBuffer[i]);
                        btrMs.AppendEntity(vtx);
                        tr.AddNewlyCreatedDBObject(vtx, true);
                    }

                    // Adiciona faces (triângulos) — FaceRecord usa índices de 1 a N
                    // Para triângulo, repete o 3º índice no 4º parâmetro.
                    for (int i = 0; i < faceBuffer.Count; i++)
                    {
                        (int A, int B, int C) f = faceBuffer[i];
                        short ia = (short)(f.A + 1);
                        short ib = (short)(f.B + 1);
                        short ic = (short)(f.C + 1);

                        FaceRecord fr = new FaceRecord(ia, ib, ic, ic);
                        btrMs.AppendEntity(fr);
                        tr.AddNewlyCreatedDBObject(fr, true);
                    }

                    docEditor.WriteMessage($"\nPolyFaceMesh criada. Superfícies: {totalSurfaces}, Triângulos: {totalTriangulos}, Vértices únicos: {vertexBuffer.Count}.");
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                docEditor.WriteMessage($"\nErro: {ex.Message}");
            }
            catch (System.Exception ex2)
            {
                docEditor.WriteMessage($"\nErro geral: {ex2.Message}");
            }
        }

        // ============================================================
        // PONTO DE INTEGRAÇÃO: Enumeração de triângulos do TIN
        // ============================================================
        // Preencha este método com as chamadas exatas da sua versão do AeccDBMgd.
        // Em algumas versões, há algo como:
        // - ts.Triangles -> coleção que retorna triângulos com 3 vértices (Point3d)
        // - ou métodos utilitários que retornam vértices via índices
        // Se me disser a sua versão do Civil 3D, eu te devolvo esse método pronto.
        private static IEnumerable<(Point3d A, Point3d B, Point3d C)> EnumerarTriangulosTin(TinSurface ts, Transaction tr)
        {

            foreach (var tri in ts.Triangles)
            {

                yield return (tri.Vertex1.Location, tri.Vertex2.Location, tri.Vertex3.Location);
            }
        }
           

        // ============================================================
        // Auxiliares
        // ============================================================
        private static bool ExtentsIntersect(Extents3d a, Extents3d b)
        {
            return !(a.MaxPoint.X < b.MinPoint.X || a.MinPoint.X > b.MaxPoint.X ||
                     a.MaxPoint.Y < b.MinPoint.Y || a.MinPoint.Y > b.MaxPoint.Y);
        }

        private static bool TriangleExtentsIntersect((Point3d A, Point3d B, Point3d C) t, Extents3d e)
        {
            double minX = Math.Min(t.A.X, Math.Min(t.B.X, t.C.X));
            double minY = Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y));
            double maxX = Math.Max(t.A.X, Math.Max(t.B.X, t.C.X));
            double maxY = Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y));
            return !(maxX < e.MinPoint.X || minX > e.MaxPoint.X || maxY < e.MinPoint.Y || minY > e.MaxPoint.Y);
        }

        private static bool IsPointInsidePolyline(Polyline pl, Point3d pt)
        {
            // Usa método nativo da Polyline em 2D (descarta Z)
            return PolylinePointInPolygon.IsInside(pl, new Point2d(pt.X, pt.Y), new Tolerance(1e-8, 1e-8), 0.25) ;
        }

        private static int GetOrAddVertexIndex(
            Dictionary<(long X, long Y, long Z), int> map,
            List<Point3d> store,
            Point3d p,
            double tol)
        {
            (long X, long Y, long Z) key = Quantize(p, tol);
            if (map.TryGetValue(key, out int idx))
                return idx;

            int newIndex = store.Count;
            store.Add(p);
            map[key] = newIndex;
            return newIndex;
        }

        private static (long X, long Y, long Z) Quantize(Point3d p, double tol)
        {
            // Quantização por tolerância para deduplicar vértices quase idênticos
            long qx = (long)Math.Round(p.X / tol);
            long qy = (long)Math.Round(p.Y / tol);
            long qz = (long)Math.Round(p.Z / tol);
            return (qx, qy, qz);
        }
    }
}