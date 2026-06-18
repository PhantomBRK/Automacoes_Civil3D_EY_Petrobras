using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivSurface = Autodesk.Civil.DatabaseServices.Surface;
using StyleBase = Autodesk.Civil.DatabaseServices.Styles.StyleBase;

namespace AutomacoesCivil3D
{
    /// <summary>Escolhas do usuário p/ a seção: superfícies amostradas + estilos.</summary>
    public class OpcoesSecao
    {
        public HashSet<ObjectId> Superficies = new HashSet<ObjectId>();
        public ObjectId EstiloSV = ObjectId.Null;     // estilo da Section View
        public ObjectId EstiloSecao = ObjectId.Null;  // estilo aplicado às superfícies amostradas
    }

    /// <summary>
    /// Diálogo único (antes de criar as seções) p/ escolher quais superfícies aparecem
    /// e quais estilos (Section View + estilo de seção das superfícies) usar.
    /// </summary>
    public class SecaoOpcoesDialog : Form
    {
        private sealed class Item
        {
            public readonly string Nome;
            public readonly ObjectId Id;
            public Item(string nome, ObjectId id) { Nome = nome; Id = id; }
            public override string ToString() => Nome;
        }

        private readonly CheckedListBox _clbSup;
        private readonly ComboBox _cboSV;
        private readonly ComboBox _cboSecao;

        public HashSet<ObjectId> SupSel { get; private set; } = new HashSet<ObjectId>();
        public ObjectId EstiloSV { get; private set; } = ObjectId.Null;
        public ObjectId EstiloSecao { get; private set; } = ObjectId.Null;

        private SecaoOpcoesDialog(List<Item> sup, List<Item> svS, List<Item> secS)
        {
            Text = "Seção de bueiros — superfícies e estilos";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(444, 392);

            var lbl1 = new Label { Text = "Superfícies presentes na seção:", Location = new Point(12, 10), AutoSize = true };
            _clbSup = new CheckedListBox { Location = new Point(12, 30), Size = new Size(420, 170), CheckOnClick = true };
            foreach (var it in sup) _clbSup.Items.Add(it, true);

            var lbl2 = new Label { Text = "Estilo da Section View:", Location = new Point(12, 210), AutoSize = true };
            _cboSV = new ComboBox { Location = new Point(12, 230), Size = new Size(420, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var it in svS) _cboSV.Items.Add(it);
            if (_cboSV.Items.Count > 0) _cboSV.SelectedIndex = 0;

            var lbl3 = new Label { Text = "Estilo da seção (superfícies):", Location = new Point(12, 262), AutoSize = true };
            _cboSecao = new ComboBox { Location = new Point(12, 282), Size = new Size(420, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            _cboSecao.Items.Add(new Item("(padrão)", ObjectId.Null));
            foreach (var it in secS) _cboSecao.Items.Add(it);
            _cboSecao.SelectedIndex = 0;

            var btnOk = new Button { Text = "OK", Location = new Point(276, 350), Size = new Size(75, 28), DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "Cancelar", Location = new Point(357, 350), Size = new Size(75, 28), DialogResult = DialogResult.Cancel };
            btnOk.Click += (s, e) =>
            {
                foreach (var o in _clbSup.CheckedItems) if (o is Item it) SupSel.Add(it.Id);
                EstiloSV = (_cboSV.SelectedItem as Item)?.Id ?? ObjectId.Null;
                EstiloSecao = (_cboSecao.SelectedItem as Item)?.Id ?? ObjectId.Null;
            };

            Controls.Add(lbl1); Controls.Add(_clbSup);
            Controls.Add(lbl2); Controls.Add(_cboSV);
            Controls.Add(lbl3); Controls.Add(_cboSecao);
            Controls.Add(btnOk); Controls.Add(btnCancel);
            AcceptButton = btnOk; CancelButton = btnCancel;
        }

        /// <summary>Lê superfícies/estilos do desenho, mostra o diálogo. false = cancelado.</summary>
        public static bool Mostrar(out OpcoesSecao op)
        {
            op = null;
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            Database db = doc.Database;
            CivilDocument civ = CivilApplication.ActiveDocument;

            var sup = new List<Item>();
            var svS = new List<Item>();
            var secS = new List<Item>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in civ.GetSurfaceIds())
                {
                    try { if (tr.GetObject(id, OpenMode.ForRead) is CivSurface s) sup.Add(new Item(s.Name, id)); }
                    catch { }
                }
                AddStyles(tr, civ.Styles.SectionViewStyles, svS);
                AddStyles(tr, civ.Styles.SectionStyles, secS);
                tr.Commit();
            }

            using (var dlg = new SecaoOpcoesDialog(sup, svS, secS))
            {
                if (AcAp.ShowModalDialog(dlg) != DialogResult.OK) return false;
                op = new OpcoesSecao
                {
                    Superficies = dlg.SupSel,
                    EstiloSV = dlg.EstiloSV,
                    EstiloSecao = dlg.EstiloSecao
                };
            }
            return true;
        }

        private static void AddStyles(Transaction tr, IEnumerable coll, List<Item> into)
        {
            if (coll == null) return;
            foreach (ObjectId id in coll)
            {
                try { if (tr.GetObject(id, OpenMode.ForRead) is StyleBase s) into.Add(new Item(s.Name, id)); }
                catch { }
            }
        }
    }
}
