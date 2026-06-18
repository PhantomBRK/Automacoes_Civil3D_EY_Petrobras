// Usings essenciais e aliases para evitar conflitos:
using AutocadMAP;
using Autodesk.Aec.Modeler;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using DocumentFormat.OpenXml.Office2016.Drawing.Charts;
using System.Windows.Forms.Design;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Color = Autodesk.AutoCAD.Colors.Color;
using Label = Autodesk.Civil.DatabaseServices.Label;

namespace AutomacoesCivil3D
{
    public class AjustaTextosPorSuperficie
    {
        [CommandMethod("AJUSTA_TEXTOS_SUP")]
        public static void AjustarTextosNaCotaDaSuperficie()
        {
            Document doc = Manager.DocCad;
            CivilDocument civDoc = Manager.DocCivil;
            Editor ed = Manager.DocEditor;
            Database db = doc.Database;

            // superfície
            PromptEntityOptions peoSup = new PromptEntityOptions("\nSelecione a superfície TIN:");
            peoSup.SetRejectMessage("\nSomente TinSurface.");
            peoSup.AddAllowedClass(typeof(TinSurface), true);
            PromptEntityResult perSup = ed.GetEntity(peoSup);
            if (perSup.Status != PromptStatus.OK) return;
            ObjectId supId = perSup.ObjectId;

            // textos
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nSelecione TEXT/MTEXT/BLOCOS/LINHAS/POLYLINES:";

            TypedValue[] f =
            {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "TEXT"),       // DBText
                new TypedValue((int)DxfCode.Start, "MTEXT"),
                new TypedValue((int)DxfCode.Start, "INSERT"),     // BlockReference
                new TypedValue((int)DxfCode.Start, "LINE"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"), // Polyline 2D “leve”
                new TypedValue((int)DxfCode.Start, "POLYLINE"),   // Polyline pesada e Polyline3d
                new TypedValue((int)DxfCode.Operator, "OR>")
            };

            SelectionFilter sf = new SelectionFilter(f);
            PromptSelectionResult psr = ed.GetSelection(pso, sf);
            if (psr.Status != PromptStatus.OK) return;
            

            int ok = 0, skipExt = 0, skipInsideHole = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                TinSurface sup = (TinSurface)tr.GetObject(supId, OpenMode.ForRead);
                Extents3d ext = sup.GeometricExtents;
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                int gruposProcessados = 0, labelsProcessadas = 0, entidadesCriadas = 0, ignorados = 0;

                foreach (SelectedObject so in psr.Value)
                {
                    Entity src = (Entity)tr.GetObject(so.ObjectId, OpenMode.ForWrite);

                    string herdaLayer = src.Layer;
                    string herdaLtype = src.Linetype;
                    Color herdaColor = src.Color;

                    var primitivos = new System.Collections.Generic.List<Entity>();
                    

                    foreach (Entity prim in primitivos)
                    {
                        // Ajusta/Converte
                        Entity entFinal = AjustarParaSuperficieOuConverter(prim, sup, tr, db);

                        // Garante objeto “novo” antes de anexar
                        Entity toAppend = entFinal;
                        if (!toAppend.IsNewObject || toAppend.Database != null)
                        {
                            toAppend = (Entity)toAppend.Clone();
                            toAppend.SetDatabaseDefaults(db);
                        }

                        // Herda propriedades visuais
                        toAppend.Layer = herdaLayer;
                        toAppend.Linetype = herdaLtype;
                        toAppend.Color = herdaColor;

                        ObjectId nid = ms.AppendEntity(toAppend);
                        tr.AddNewlyCreatedDBObject(toAppend, true);
                        entidadesCriadas++;

                        // limpeza
                        if (entFinal != toAppend && entFinal.IsNewObject) entFinal.Dispose();
                        if (prim.IsNewObject) prim.Dispose();
                    }

                    if (src is Label) labelsProcessadas++;
                    else if (src is LabelGroup) gruposProcessados++;
                    else ignorados++;

                    
                }

               

               

                   
                    
                

                tr.Commit();
            }

            ed.WriteMessage($"\nAjustados: {ok}. Fora do extents: {skipExt}. Fora da superfície: {skipInsideHole}.");
        }


        private static Entity AjustarParaSuperficieOuConverter(Entity e, TinSurface s, Transaction tr, Database db)
        {
            if (e is DBText)
            {
                DBText t = (DBText)e;
                Point3d p = t.Position;
                double z = AmostraZ(s, p.X, p.Y);
                t.Position = new Point3d(p.X, p.Y, z);
                return t;
            }

            if (e is MText)
            {
                MText m = (MText)e;
                Point3d p = m.Location;
                double z = AmostraZ(s, p.X, p.Y);
                m.Location = new Point3d(p.X, p.Y, z);
                return m;
            }

            if (e is Line)
            {
                Line ln = (Line)e;
                Point3d p1 = ln.StartPoint; Point3d p2 = ln.EndPoint;
                ln.StartPoint = new Point3d(p1.X, p1.Y, AmostraZ(s, p1.X, p1.Y));
                ln.EndPoint = new Point3d(p2.X, p2.Y, AmostraZ(s, p2.X, p2.Y));
                return ln;
            }

            if (e is Polyline)
            {
                Polyline pl = (Polyline)e;
                Point3dCollection pts = new Point3dCollection();
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    Point2d p2 = pl.GetPoint2dAt(i);
                    pts.Add(new Point3d(p2.X, p2.Y, AmostraZ(s, p2.X, p2.Y)));
                }
                Polyline3d pl3 = new Polyline3d(Poly3dType.SimplePoly, pts, pl.Closed);
                pl3.Layer = pl.Layer; pl3.Linetype = pl.Linetype; pl3.Color = pl.Color;
                return pl3;
            }

            if (e is Polyline2d)
            {
                Polyline2d pl2 = (Polyline2d)e;
                // se não estiver no BD, a enumeração por vértice pode não existir; tratar via extents como fallback
                try
                {
                    var pts = new System.Collections.Generic.List<Point3d>();
                    foreach (ObjectId vId in pl2)
                    {
                        Vertex2d v = (Vertex2d)tr.GetObject(vId, OpenMode.ForRead);
                        pts.Add(new Point3d(v.Position.X, v.Position.Y, AmostraZ(s, v.Position.X, v.Position.Y)));
                    }
                    if (pts.Count > 1)
                    {
                        Polyline3d pl3 = new Polyline3d(Poly3dType.SimplePoly, new Point3dCollection(pts.ToArray()), pl2.Closed);
                        pl3.Layer = pl2.Layer; pl3.Linetype = pl2.Linetype; pl3.Color = pl2.Color;
                        return pl3;
                    }
                }
                catch { }
                AjusteUniformePorCentro(e, s);
                return e;
            }

            if (e is Polyline3d || e is Arc || e is Circle || e is Ellipse)
            {
                AjusteUniformePorCentro(e, s);
                return e;
            }

            AjusteUniformePorCentro(e, s);
            return e;
        }



        private static void AjusteUniformePorCentro(Entity e, TinSurface s)
        {
            try
            {
                Extents3d ex = e.GeometricExtents;
                Point3d c = new Point3d((ex.MinPoint.X + ex.MaxPoint.X) * 0.5, (ex.MinPoint.Y + ex.MaxPoint.Y) * 0.5, (ex.MinPoint.Z + ex.MaxPoint.Z) * 0.5);
                double z = AmostraZ(s, c.X, c.Y);
                double dz = z - c.Z;
                if (System.Math.Abs(dz) > 1e-6)
                    e.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, dz)));
            }
            catch { }
        }

        private static double AmostraZ(TinSurface s, double x, double y)
        {
            try { return (s.FindElevationAtXY(x, y) + 0.4); }
            catch { return 0.0; }
        }


        private static void ConvertPolylineTo3dOnSurface(Polyline pl, TinSurface surface)
        {
            Database db = pl.Database;
            Transaction tr = db.TransactionManager.TopTransaction;

            // Construir pontos 3D
            Point3dCollection pts = new Point3dCollection();
            int n = pl.NumberOfVertices;
            for (int i = 0; i < n; i++)
            {
                Point2d p2 = pl.GetPoint2dAt(i);
                double z = Elevacao(surface, p2.X, p2.Y);
                Point3d p3 = new Point3d(p2.X, p2.Y, z);
                pts.Add(p3);
            }

            // Criar Polyline3d e substituir
            Polyline3d pl3 = new Polyline3d(Poly3dType.SimplePoly, pts, pl.Closed);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            ObjectId newId = ms.AppendEntity(pl3);
            tr.AddNewlyCreatedDBObject(pl3, true);

            // Estilos
            pl3.Layer = pl.Layer;
            pl3.Linetype = pl.Linetype;
            pl3.Color = pl.Color;

            pl.Erase();
        }

        private static void ConvertLineTo3dOnSurface(Line pl, TinSurface surface)
        {
            Database db = pl.Database;
            Transaction tr = db.TransactionManager.TopTransaction;

            // Construir pontos 3D
            Point3dCollection pts = new Point3dCollection();
            
                Point3d p2 = pl.GeometricExtents.MinPoint;
                Point3d p1 = pl.GeometricExtents.MaxPoint;
                double z = Elevacao(surface, p2.X, p2.Y);
                Point3d p3 = new Point3d(p2.X, p2.Y, z);
                double z1 = Elevacao(surface, p1.X, p1.Y);
                Point3d p4 = new Point3d(p1.X, p1.Y, z1);
                pts.Add(p3);
                pts.Add(p4);
            

            // Criar Polyline3d e substituir
            Polyline3d pl3 = new Polyline3d(Poly3dType.SimplePoly, pts, pl.Closed);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            ObjectId newId = ms.AppendEntity(pl3);
            tr.AddNewlyCreatedDBObject(pl3, true);

            // Estilos
            pl3.Layer = pl.Layer;
            pl3.Linetype = pl.Linetype;
            pl3.Color = pl.Color;

            pl.Erase();
        }

        private static void ConvertPolyline2dTo3dOnSurface(Polyline2d pl2, TinSurface surface)
        {
            Database db = pl2.Database;
            Transaction tr = db.TransactionManager.TopTransaction;

            // Coletar vértices 2D
            Point3dCollection pts = new Point3dCollection();
            foreach (ObjectId vId in pl2)
            {
                Vertex2d v2 = (Vertex2d)tr.GetObject(vId, OpenMode.ForRead);
                double z = Elevacao(surface, v2.Position.X, v2.Position.Y);
                Point3d p3 = new Point3d(v2.Position.X, v2.Position.Y, z);
                pts.Add(p3);
            }

            Polyline3d pl3 = new Polyline3d(Poly3dType.SimplePoly, pts, pl2.Closed);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            ObjectId newId = ms.AppendEntity(pl3);
            tr.AddNewlyCreatedDBObject(pl3, true);

            pl3.Layer = pl2.Layer;
            pl3.Linetype = pl2.Linetype;
            pl3.Color = pl2.Color;

            pl2.Erase();
        }

        private static double Elevacao(TinSurface s, double x, double y)
        {
            try { return s.FindElevationAtXY(x, y); }
            catch { return 0.0; }
        }
    }
}

