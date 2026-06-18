using System;
using System.Collections.Generic;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Label = Autodesk.Civil.DatabaseServices.Label;
using Color = Autodesk.AutoCAD.Colors.Color;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace AutomacoesCivil3D
{
    public class CorridorSurfaceTargets
    {
        [CommandMethod("SET_SURFTARGET_ALL_CORRIDORS")]
        public void SetSurfaceTargetForAllCorridors()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            CivilDocument civilDb = Manager.DocCivil;
            Database db = civilDoc.Database;

            ObjectId selectedSurfaceId = ObjectId.Null;
            string selectedSurfaceName = string.Empty;

            // 1) Carrega superfícies e monta lista para a UI
            List<SurfaceItem> surfaces = new List<SurfaceItem>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectIdCollection surfIds = civilDb.GetSurfaceIds();
                if (surfIds == null || surfIds.Count == 0)
                {
                    docEditor.WriteMessage("\nNão há superfícies no desenho.");
                    return;
                }

                foreach (ObjectId sid in surfIds)
                {
                    CivilSurface surface = (CivilSurface)tr.GetObject(sid, OpenMode.ForRead);
                    surfaces.Add(new SurfaceItem(sid, surface.Name));
                }

                tr.Commit();
            }

            // 2) Form de seleção
            using (SurfaceSelectionForm form = new SurfaceSelectionForm(surfaces))
            {
                DialogResult result = Application.ShowModalDialog(form);
                if (result != DialogResult.OK || form.SelectedItem == null)
                {
                    docEditor.WriteMessage("\nComando cancelado.");
                    return;
                }

                selectedSurfaceId = form.SelectedItem.Id;
                selectedSurfaceName = form.SelectedItem.Name;
            }

            if (!selectedSurfaceId.IsValid)
            {
                docEditor.WriteMessage("\nSuperfície selecionada inválida.");
                return;
            }

            // 3) Aplica o surface target nos corredores
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                int corridorsUpdated = 0;
                int corridorsSkipped = 0;

                foreach (ObjectId corrId in civilDb.CorridorCollection)
                {
                    Corridor corridor = (Corridor)tr.GetObject(corrId, OpenMode.ForWrite);

                    SubassemblyTargetInfoCollection targets = corridor.GetTargets();
                    if (targets == null || targets.Count == 0)
                    {
                        corridorsSkipped++;
                        continue;
                    }

                    bool hasSurfaceTargetAlready = false;

                    // verifica se já existe algum target de superfície preenchido
                    for (int i = 0; i < targets.Count; i++)
                    {
                        SubassemblyTargetInfo info = targets[i];
                        if (info.TargetType == SubassemblyLogicalNameType.Surface)
                        {
                            ObjectIdCollection currentIds = info.TargetIds;
                            if (currentIds != null && currentIds.Count > 0)
                            {
                                hasSurfaceTargetAlready = true;
                                break;
                            }
                        }
                    }

                    if (hasSurfaceTargetAlready)
                    {
                        corridorsSkipped++;
                        continue;
                    }

                    ObjectIdCollection newIds = new ObjectIdCollection();
                    newIds.Add(selectedSurfaceId);

                    bool anySet = false;

                    // seta a superfície escolhida em todos os targets do tipo Surface
                    for (int i = 0; i < targets.Count; i++)
                    {
                        SubassemblyTargetInfo info = targets[i];
                        if (info.TargetType == SubassemblyLogicalNameType.Surface)
                        {
                            info.TargetIds = newIds;
                            anySet = true;
                        }
                    }

                    if (anySet)
                    {
                        corridor.SetTargets(targets);
                        corridor.Rebuild();
                        corridorsUpdated++;
                    }
                    else
                    {
                        corridorsSkipped++;
                    }
                }

                tr.Commit();

                docEditor.WriteMessage(
                    $"\nSuperfície '{selectedSurfaceName}' aplicada como alvo em {corridorsUpdated} corredor(es). " +
                    $"{corridorsSkipped} corredor(es) mantidos sem alteração.");
            }

            docEditor.Regen();
        }

        // ----------------- tipos auxiliares -----------------

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
