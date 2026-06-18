using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Label = Autodesk.Civil.DatabaseServices.Label;
using Color = Autodesk.AutoCAD.Colors.Color;

using System.Collections.Generic;
using System.Windows.Forms;
using DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace AutomacoesCivil3D
{
    public class ExplodirLabelGroupParaSuperficie
    {
        [CommandMethod("EXPLODIRLABELS")]
        public static void ExplodirLabelsELabelGroupsParaSuperficie()
        {
            Document docCad = Manager.DocCad;
            CivilDocument docCivil = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database db = Manager.DocData;

            // Seleção do alinhamento (mantida)
            PromptEntityOptions peoAl = new PromptEntityOptions("\nSelecione o Alinhamento:");
            peoAl.SetRejectMessage("\nNão é Alinhamento.");
            peoAl.AddAllowedClass(typeof(Alignment), true);

            PromptEntityResult perAl = docEditor.GetEntity(peoAl);
            if (perAl.Status != PromptStatus.OK)
            {
                docEditor.WriteMessage("\nCancelado.");
                return;
            }
            ObjectId alignId = perAl.ObjectId;

            // -------------------------------
            // Seleção da superfície TIN via formulário (mesmo padrão do Surface_Target)
            // -------------------------------
            ObjectId surfId = ObjectId.Null;
            string surfName = string.Empty;

            List<SurfaceItem> surfaces = new List<SurfaceItem>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectIdCollection surfIds = docCivil.GetSurfaceIds();
                if (surfIds == null || surfIds.Count == 0)
                {
                    docEditor.WriteMessage("\nNão há superfícies no desenho.");
                    return;
                }

                foreach (ObjectId sid in surfIds)
                {
                    Autodesk.Civil.DatabaseServices.Surface surface =
                        (Autodesk.Civil.DatabaseServices.Surface)tr.GetObject(sid, OpenMode.ForRead);

                    if (surface is TinSurface)
                    {
                        surfaces.Add(new SurfaceItem(sid, surface.Name));
                    }
                }

                tr.Commit();
            }

            if (surfaces.Count == 0)
            {
                docEditor.WriteMessage("\nNão há superfícies TIN no desenho.");
                return;
            }

            using (SurfaceSelectionForm form = new SurfaceSelectionForm(surfaces))
            {
                DialogResult dlgResult = Application.ShowModalDialog(form);
                if (dlgResult != DialogResult.OK || form.SelectedItem == null)
                {
                    docEditor.WriteMessage("\nCancelado.");
                    return;
                }

                surfId = form.SelectedItem.Id;
                surfName = form.SelectedItem.Name;
            }

            if (!surfId.IsValid)
            {
                docEditor.WriteMessage("\nSuperfície selecionada inválida.");
                return;
            }

            // -------------------------------
            // Processamento dos Labels / LabelGroups
            // -------------------------------
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                TinSurface superficie = (TinSurface)tr.GetObject(surfId, OpenMode.ForRead);

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                List<ObjectId> alvos = new List<ObjectId>();

                foreach (ObjectId id in ms)
                {
                    // Label
                    if (id.ObjectClass.IsDerivedFrom(RXClass.GetClass(typeof(Label))))
                    {
                        Label lbl = (Label)tr.GetObject(id, OpenMode.ForRead);
                        alvos.Add(id);
                        continue;
                    }
                    // LabelGroup
                    if (id.ObjectClass.IsDerivedFrom(RXClass.GetClass(typeof(LabelGroup))))
                    {
                        LabelGroup lg = (LabelGroup)tr.GetObject(id, OpenMode.ForRead);
                        try
                        {
                            alvos.Add(id);
                        }
                        catch
                        {
                        }
                    }
                }

                int gruposProcessados = 0;
                int labelsProcessadas = 0;
                int entidadesCriadas = 0;
                int ignorados = 0;

                foreach (ObjectId alvoId in alvos)
                {
                    Entity src = (Entity)tr.GetObject(alvoId, OpenMode.ForWrite);

                    string herdaLayer = src.Layer;
                    string herdaLtype = src.Linetype;
                    Color herdaColor = src.Color;

                    List<Entity> primitivos = new List<Entity>();
                    ExplodeRecursivoParaPrimitivos(src, primitivos, 0);

                    foreach (Entity prim in primitivos)
                    {
                        // Ajusta/Converte
                        Entity entFinal = AjustarParaSuperficieOuConverter(prim, superficie, tr, db);

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
                        if (entFinal != toAppend && entFinal.IsNewObject)
                        {
                            entFinal.Dispose();
                        }
                        if (prim.IsNewObject)
                        {
                            prim.Dispose();
                        }
                    }

                    if (src is Label)
                    {
                        labelsProcessadas++;
                    }
                    else if (src is LabelGroup)
                    {
                        gruposProcessados++;
                    }
                    else
                    {
                        ignorados++;
                    }

                    // src.Erase(); // se quiser apagar os originais
                }

                tr.Commit();
                docEditor.WriteMessage(
                    "\nSuperfície: " + surfName +
                    " | LabelGroups: " + gruposProcessados +
                    ", Labels: " + labelsProcessadas +
                    ", Entidades criadas: " + entidadesCriadas +
                    ", Ignorados: " + ignorados + ".");
            }
        }

        private static void ExplodeRecursivoParaPrimitivos(
            Entity fonte,
            List<Entity> saida,
            int profundidade)
        {
            if (profundidade > 10)
            {
                return;
            }

            // Se já for primitivo, devolver um NEW: clonar se estiver no BD
            if (EhPrimitivo(fonte))
            {
                if (!fonte.IsNewObject || fonte.Database != null)
                {
                    saida.Add((Entity)fonte.Clone());
                }
                else
                {
                    saida.Add(fonte);
                }
                return;
            }

            DBObjectCollection partes = new DBObjectCollection();
            try
            {
                fonte.Explode(partes);
            }
            catch
            {
                return;
            }

            if (partes.Count == 0)
            {
                return;
            }

            foreach (DBObject dbo in partes)
            {
                Entity ent = dbo as Entity;
                if (ent == null)
                {
                    dbo.Dispose();
                    continue;
                }

                if (EhPrimitivo(ent))
                {
                    saida.Add(ent); // é NEW
                }
                else
                {
                    ExplodeRecursivoParaPrimitivos(ent, saida, profundidade + 1);
                    if (ent.IsNewObject)
                    {
                        ent.Dispose();
                    }
                }
            }
        }

        private static bool EhPrimitivo(Entity e)
        {
            return (e is DBText) ||
                   (e is MText) ||
                   (e is Line) ||
                   (e is Polyline) ||
                   (e is Polyline2d) ||
                   (e is Polyline3d) ||
                   (e is Arc) ||
                   (e is Circle) ||
                   (e is Ellipse);
        }

        private static Entity AjustarParaSuperficieOuConverter(
            Entity e,
            TinSurface s,
            Transaction tr,
            Database db)
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
                Point3d p1 = ln.StartPoint;
                Point3d p2 = ln.EndPoint;

                ln.StartPoint = new Point3d(p1.X, p1.Y, AmostraZ(s, p1.X, p1.Y));
                ln.EndPoint = new Point3d(p2.X, p2.Y, AmostraZ(s, p2.X, p2.Y));

                if (AmostraZ(s, p1.X, p1.Y) > AmostraZ(s, p2.X, p2.Y))
                {
                    ln.StartPoint = new Point3d(p1.X, p1.Y, AmostraZ(s, p1.X, p1.Y));
                    ln.EndPoint = new Point3d(p2.X, p2.Y, AmostraZ(s, p1.X, p1.Y));
                }
                else
                {
                    ln.StartPoint = new Point3d(p1.X, p1.Y, AmostraZ(s, p2.X, p2.Y));
                    ln.EndPoint = new Point3d(p2.X, p2.Y, AmostraZ(s, p2.X, p2.Y));
                }

                Point3dCollection pts = new Point3dCollection();
                pts.Add(ln.StartPoint);
                pts.Add(ln.EndPoint);

                Polyline3d pl3 = new Polyline3d(Poly3dType.SimplePoly, pts, false);
                pl3.Layer = ln.Layer;
                pl3.Linetype = ln.Linetype;
                pl3.Color = ln.Color;

                return pl3;
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
                pl3.Layer = pl.Layer;
                pl3.Linetype = pl.Linetype;
                pl3.Color = pl.Color;

                return pl3;
            }

            if (e is Polyline2d)
            {
                Polyline2d pl2 = (Polyline2d)e;

                // se não estiver no BD, a enumeração por vértice pode não existir; tratar via extents como fallback
                try
                {
                    List<Point3d> pts = new List<Point3d>();
                    foreach (ObjectId vId in pl2)
                    {
                        Vertex2d v = (Vertex2d)tr.GetObject(vId, OpenMode.ForRead);
                        pts.Add(new Point3d(
                            v.Position.X,
                            v.Position.Y,
                            AmostraZ(s, v.Position.X, v.Position.Y)));
                    }

                    if (pts.Count > 1)
                    {
                        Polyline3d pl3 = new Polyline3d(
                            Poly3dType.SimplePoly,
                            new Point3dCollection(pts.ToArray()),
                            pl2.Closed);

                        pl3.Layer = pl2.Layer;
                        pl3.Linetype = pl2.Linetype;
                        pl3.Color = pl2.Color;
                        return pl3;
                    }
                }
                catch
                {
                }

                AjusteUniformePorCentro(e, s);
                return e;
            }

            // Arc / Circle convertidos em Polyline3d na superfície
            if (e is Arc)
            {
                Arc a = (Arc)e;
                Polyline3d pl3 = ArcOuCircleParaPolyline3d(
                    a.Center,
                    a.Radius,
                    a.StartAngle,
                    a.EndAngle,
                    s);

                pl3.Layer = a.Layer;
                pl3.Linetype = a.Linetype;
                pl3.Color = a.Color;
                return pl3;
            }

            if (e is Circle)
            {
                Circle c = (Circle)e;
                Polyline3d pl3 = ArcOuCircleParaPolyline3d(
                    c.Center,
                    c.Radius,
                    0.0,
                    2.0 * System.Math.PI,
                    s);

                pl3.Layer = c.Layer;
                pl3.Linetype = c.Linetype;
                pl3.Color = c.Color;
                return pl3;
            }

            // Ellipse: fallback simples (ou implemente tesselagem similar)
            if (e is Polyline3d || e is Ellipse)
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
                Point3d c = new Point3d(
                    (ex.MinPoint.X + ex.MaxPoint.X) * 0.5,
                    (ex.MinPoint.Y + ex.MaxPoint.Y) * 0.5,
                    (ex.MinPoint.Z + ex.MaxPoint.Z) * 0.5);

                double z = AmostraZ(s, c.X, c.Y);
                double dz = z - c.Z;

                if (System.Math.Abs(dz) > 1e-6)
                {
                    e.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, dz)));
                }
            }
            catch
            {
            }
        }

        private static Polyline3d ArcOuCircleParaPolyline3d(
            Point3d centro,
            double raio,
            double angIni,
            double angFim,
            TinSurface s)
        {
            // normaliza varredura
            double sweep = angFim - angIni;
            if (sweep <= 0)
            {
                sweep += 2.0 * System.Math.PI;
            }

            // passo angular ~10° (ajuste se quiser mais suavidade)
            double passo = 10.0 * System.Math.PI / 180.0;
            int n = System.Math.Max(8, (int)System.Math.Ceiling(sweep / passo));

            Point3dCollection pts = new Point3dCollection();
            for (int i = 0; i <= n; i++)
            {
                double t = angIni + sweep * (double)i / (double)n;
                double x = centro.X + raio * System.Math.Cos(t);
                double y = centro.Y + raio * System.Math.Sin(t);
                double z = AmostraZ(s, x, y);

                Point3d p = new Point3d(x, y, z);
                pts.Add(p);
            }

            Polyline3d pl3 = new Polyline3d(Poly3dType.SimplePoly, pts, true);
            return pl3;
        }

        private static double AmostraZ(TinSurface s, double x, double y)
        {
            try
            {
                return (s.FindElevationAtXY(x, y) + 0.4);
            }
            catch
            {
                return 0.0;
            }
        }

        private class SurfaceItem
        {
            public ObjectId Id { get; }
            public string Name { get; }

            public SurfaceItem(ObjectId id, string name)
            {
                Id = id;
                Name = name;
            }

            public override string ToString()
            {
                return Name;
            }
        }

        private class SurfaceSelectionForm : Form
        {
            private readonly ListBox listBox;
            private readonly Button okButton;
            private readonly Button cancelButton;

            public SurfaceItem SelectedItem
            {
                get
                {
                    return (SurfaceItem)listBox.SelectedItem;
                }
            }

            public SurfaceSelectionForm(List<SurfaceItem> surfaces)
            {
                Text = "Seleção de superfície";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterScreen;
                MinimizeBox = false;
                MaximizeBox = false;
                ShowInTaskbar = false;
                Width = 400;
                Height = 300;

                listBox = new ListBox();
                listBox.Dock = DockStyle.Top;
                listBox.Height = 220;

                foreach (SurfaceItem item in surfaces)
                {
                    listBox.Items.Add(item);
                }

                okButton = new Button();
                okButton.Text = "OK";
                okButton.Width = 80;
                okButton.Top = 230;
                okButton.Left = 200;
                okButton.Click += OkButton_Click;

                cancelButton = new Button();
                cancelButton.Text = "Cancelar";
                cancelButton.Width = 80;
                cancelButton.Top = 230;
                cancelButton.Left = 290;
                cancelButton.Click += CancelButton_Click;

                Controls.Add(listBox);
                Controls.Add(okButton);
                Controls.Add(cancelButton);
            }

            private void OkButton_Click(object sender, EventArgs e)
            {
                if (listBox.SelectedItem == null)
                {
                    MessageBox.Show(
                        "Selecione uma superfície.",
                        "Aviso",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                DialogResult = DialogResult.OK;
                Close();
            }

            private void CancelButton_Click(object sender, EventArgs e)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }
    }
}
