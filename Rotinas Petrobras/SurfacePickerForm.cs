using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;

namespace RotinasPetrobras.Diagnostics
{
    /// <summary>
    /// Diálogo simples para escolher, numa LISTA, as superfícies de ATERRO/greide e de
    /// TERRENO natural usadas por SOL_AJUSTAR_BUEIRO_DNIT — em vez de pick direto no desenho.
    /// Recebe pares (nome, ObjectId) já coletados do CivilDocument.
    /// </summary>
    internal sealed class SurfacePickerForm : Form
    {
        private readonly List<KeyValuePair<string, ObjectId>> _itens;
        private readonly ComboBox _cboAterro = new ComboBox();
        private readonly ComboBox _cboTerreno = new ComboBox();

        public ObjectId AterroId { get; private set; } = ObjectId.Null;
        public ObjectId TerrenoId { get; private set; } = ObjectId.Null;

        public SurfacePickerForm(List<KeyValuePair<string, ObjectId>> itens)
        {
            _itens = itens;

            Text = "Adequar bueiro ao talude — escolha as superfícies";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(430, 165);

            var lbl1 = new Label { Text = "Aterro / greide (estrada):", Left = 12, Top = 14, AutoSize = true };
            _cboAterro.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboAterro.Left = 12; _cboAterro.Top = 34; _cboAterro.Width = 406;

            var lbl2 = new Label { Text = "Terreno natural:", Left = 12, Top = 70, AutoSize = true };
            _cboTerreno.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboTerreno.Left = 12; _cboTerreno.Top = 90; _cboTerreno.Width = 406;

            foreach (var it in itens)
            {
                _cboAterro.Items.Add(it.Key);
                _cboTerreno.Items.Add(it.Key);
            }
            if (itens.Count > 0) _cboAterro.SelectedIndex = 0;
            if (itens.Count > 1) _cboTerreno.SelectedIndex = 1;
            else if (itens.Count > 0) _cboTerreno.SelectedIndex = 0;

            var btnOk = new Button { Text = "OK", Left = 262, Top = 128, Width = 75, DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "Cancelar", Left = 343, Top = 128, Width = 75, DialogResult = DialogResult.Cancel };
            btnOk.Click += (s, e) =>
            {
                if (_cboAterro.SelectedIndex >= 0) AterroId = _itens[_cboAterro.SelectedIndex].Value;
                if (_cboTerreno.SelectedIndex >= 0) TerrenoId = _itens[_cboTerreno.SelectedIndex].Value;
            };

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            Controls.Add(lbl1);
            Controls.Add(_cboAterro);
            Controls.Add(lbl2);
            Controls.Add(_cboTerreno);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
        }
    }
}
