using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Color = Autodesk.AutoCAD.Colors.Color;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Label = Autodesk.Civil.DatabaseServices.Label;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace AutomacoesCivil3D
{
    public class CropSurfaceExport
    {
        [CommandMethod("CROPSURF2DWG")]
        public static void CropSurfaceToOtherDrawing()
        {
            Document docSrc = Manager.DocCad;
            Editor ed = Manager.DocEditor;
            CivilDocument civilDb = Manager.DocCivil;
            Database dbSrc = docSrc.Database;

            // 1) Selecionar TinSurface origem via formulário (mesma UI do Surface_Target)
            ObjectId surfIdSrc = ObjectId.Null;
            string surfNameSrc = string.Empty;

            List<SurfaceItem> surfaces = new List<SurfaceItem>();

            using (Transaction tr = dbSrc.TransactionManager.StartTransaction())
            {
                ObjectIdCollection surfIds = civilDb.GetSurfaceIds();
                if (surfIds == null || surfIds.Count == 0)
                {
                    ed.WriteMessage("\nNão há superfícies no desenho.");
                    return;
                }

                foreach (ObjectId sid in surfIds)
                {
                    CivilSurface surface = (CivilSurface)tr.GetObject(sid, OpenMode.ForRead);
                    if (surface is TinSurface)
                    {
                        surfaces.Add(new SurfaceItem(sid, surface.Name));
                    }
                }

                tr.Commit();
            }

            if (surfaces.Count == 0)
            {
                ed.WriteMessage("\nNão há superfícies TIN no desenho.");
                return;
            }

            using (SurfaceSelectionForm form = new SurfaceSelectionForm(surfaces))
            {
                DialogResult dlgResult = Application.ShowModalDialog(form);
                if (dlgResult != DialogResult.OK || form.SelectedItem == null)
                {
                    ed.WriteMessage("\nComando cancelado.");
                    return;
                }

                surfIdSrc = form.SelectedItem.Id;
                surfNameSrc = form.SelectedItem.Name;
            }

            if (!surfIdSrc.IsValid)
            {
                ed.WriteMessage("\nSuperfície selecionada inválida.");
                return;
            }

            // 2) Selecionar objetos de recorte
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nSelecione os objetos que definem a área de recorte (fechada):";

            // tipos suportados pelo CreateByCropping (Polyline, Polyline2d, Polyline3d, ParcelSegment,
            // FeatureLine, Circle, Arc, Ellipse, Line). Aqui filtro os DXF básicos.
            TypedValue[] tv = new TypedValue[]
            {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start, "POLYLINE"),
                new TypedValue((int)DxfCode.Start, "CIRCLE"),
                new TypedValue((int)DxfCode.Start, "ARC"),
                new TypedValue((int)DxfCode.Start, "ELLIPSE"),
                new TypedValue((int)DxfCode.Start, "LINE"),
                new TypedValue((int)DxfCode.Operator, "OR>")
            };
            SelectionFilter sf = new SelectionFilter(tv);

            PromptSelectionResult psr = ed.GetSelection(pso, sf);
            if (psr.Status != PromptStatus.OK)
            {
                return;
            }

            ObjectIdCollection recorteObjs = new ObjectIdCollection();
            foreach (SelectedObject so in psr.Value)
            {
                if (so != null && !so.ObjectId.IsNull && so.ObjectId.IsValid)
                {
                    recorteObjs.Add(so.ObjectId);
                }
            }

            if (recorteObjs.Count == 0)
            {
                ed.WriteMessage("\nNenhum objeto de recorte selecionado.");
                return;
            }

            // 3) Ponto interno para indicar o lado a manter
            PromptPointResult ppr = ed.GetPoint("\nEspecifique um ponto dentro da área a manter:");
            if (ppr.Status != PromptStatus.OK)
            {
                return;
            }

            Point3d ptSeed3d = ppr.Value;
            Point2d ptSeed2d = new Point2d(ptSeed3d.X, ptSeed3d.Y);

            // 4) Escolher desenho de destino (abre se não estiver aberto)
            string destPath = GetDestinationDwgPath();
            if (string.IsNullOrEmpty(destPath))
            {
                ed.WriteMessage("\nOperação cancelada (nenhum DWG de destino selecionado).");
                return;
            }

            string destFullPath = Path.GetFullPath(destPath);

            // Impedir usar o mesmo DWG
            string srcFile = dbSrc.Filename;
            if (!string.IsNullOrEmpty(srcFile))
            {
                string srcFullPath = Path.GetFullPath(srcFile);
                if (string.Equals(srcFullPath, destFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    ed.WriteMessage("\nO desenho de destino deve ser diferente do desenho atual.");
                    return;
                }
            }

            DocumentCollection docs = Application.DocumentManager;
            Document docDest = null;

            foreach (Document d in docs)
            {
                try
                {
                    string fn = d.Database.Filename;
                    if (!string.IsNullOrEmpty(fn))
                    {
                        string openFull = Path.GetFullPath(fn);
                        if (string.Equals(openFull, destFullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            docDest = d;
                            break;
                        }
                    }
                }
                catch
                {
                    // ignora problemas de caminho
                }
            }

            if (docDest == null)
            {
                try
                {
                    docDest = docs.Open(destFullPath, false); // readOnly=false
                }
                catch (System.Exception exOpen)
                {
                    ed.WriteMessage("\nErro ao abrir desenho de destino: " + exOpen.Message);
                    return;
                }
            }

            Database dbDest = docDest.Database;

            // 5) Gerar nome único para a nova superfície no DWG destino
            string baseName = "CROP_" + surfNameSrc;
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = "CROP_SURFACE";
            }

            string newSurfName = GetUniqueSurfaceName(dbDest, baseName);

            // 6) Executar o CreateByCropping
            try
            {
                ObjectId newSurfId = TinSurface.CreateByCropping(
                    dbDest,
                    newSurfName,
                    surfIdSrc,
                    recorteObjs,
                    ptSeed2d);

                ed.WriteMessage("\nSuperfície recortada '" + newSurfName + "' criada em: " + destFullPath);
            }
            catch (System.Exception exCrop)
            {
                ed.WriteMessage("\nErro ao criar superfície recortada: " + exCrop.Message);
            }
        }

        // ---------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------

        private static string GetDestinationDwgPath()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "Desenhos do AutoCAD (*.dwg)|*.dwg|Todos os arquivos (*.*)|*.*";
                dlg.Title = "Selecione o desenho de destino para receber a superfície recortada";
                dlg.CheckFileExists = true;
                dlg.Multiselect = false;

                DialogResult dr = dlg.ShowDialog();
                if (dr != DialogResult.OK)
                {
                    return null;
                }

                return dlg.FileName;
            }
        }

        private static string GetSurfaceName(Database db, ObjectId surfId)
        {
            string nome = null;
            CivilDocument civilDb = CivilDocument.GetCivilDocument(db);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBObject dbo = (DBObject)tr.GetObject(surfId, OpenMode.ForRead);
                if (dbo is TinSurface)
                {
                    TinSurface s = (TinSurface)dbo;
                    nome = s.Name;
                }
                tr.Commit();
            }

            return nome;
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


        private static string GetUniqueSurfaceName(Database dbDest, string baseName)
        {
            CivilDocument civDest = CivilDocument.GetCivilDocument(dbDest);
            if (civDest == null)
            {
                return baseName;
            }

            HashSet<string> nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (Transaction tr = dbDest.TransactionManager.StartTransaction())
            {
                CivilDocument civilDb = CivilDocument.GetCivilDocument(dbDest);
                ObjectId surfStyleId = ObjectId.Null;
                ObjectIdCollection surfIds = civDest.GetSurfaceIds();

                foreach (ObjectId id in surfIds)
                {
                    TinSurface s = (TinSurface)tr.GetObject(id, OpenMode.ForRead);
                    nomes.Add(s.Name);

                    // Tentar aplicar um estilo "padrão" às superfícies existentes no DWG destino.
                    // 1º tenta "CURVAS - 1 & 5(GEOMETRIA)"; se não existir, pega o primeiro estilo disponível.
                    try
                    {
                        SurfaceStyleCollection surfStyles1 = civilDb.Styles.SurfaceStyles;
                        surfStyleId = ObjectId.Null;

                        try
                        {
                            surfStyleId = surfStyles1["CURVAS - 1 & 5(GEOMETRIA)"];
                        }
                        catch
                        {
                            foreach (ObjectId idStyle in surfStyles1)
                            {
                                surfStyleId = idStyle;
                                break;
                            }
                        }

                        if (!surfStyleId.IsNull)
                        {
                            s.UpgradeOpen();
                            s.StyleId = surfStyleId;
                        }
                    }
                    catch
                    {
                    }
                }

                tr.Commit();
            }

            string candidato = baseName;
            int idx = 1;
            while (nomes.Contains(candidato))
            {
                candidato = baseName + "_" + idx.ToString();
                idx++;
            }

            return candidato;
        }

    }
}
