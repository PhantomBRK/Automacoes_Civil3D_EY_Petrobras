using System;
using System.Collections.Generic;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.Runtime;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Color = Autodesk.AutoCAD.Colors.Color;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Label = Autodesk.Civil.DatabaseServices.Label;
using Point = Autodesk.Civil.DatabaseServices.Point;
using Subassembly = Autodesk.Civil.DatabaseServices.Subassembly;
using WinForms = System.Windows.Forms;

namespace AutomacoesCivil3D.Rotinas_Houer
{
    public class ReplaceTaludeSP
    {
        [CommandMethod("CONFIGURAR_PROJETOS_EPL")]
        public static void SubstituirTaludePorPacote()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            CivilDocument civilDb = Manager.DocCivil;
            Database db = civilDoc.Database;

            ShoulderExtendAll_Fix shoulderExtendAll_Fix = new ShoulderExtendAll_Fix();

            try
            {
                shoulderExtendAll_Fix.SetDaylightSlope_Epsilon_For_SEA();
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\nErro ao configurar ShoulderExtendAll: {ex.Message}");
            }

            // 1) Usuário escolhe a subassembly que será substituída
            PromptEntityOptions peo = new PromptEntityOptions("\nSelecione a subassembly que será substituída:");
            peo.SetRejectMessage("\nSelecione apenas uma subassembly.");
            peo.AddAllowedClass(typeof(Subassembly), true);

            PromptEntityResult per = docEditor.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
            {
                return;
            }

            string sourceSubName;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Subassembly sourceSub = (Subassembly)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                sourceSubName = sourceSub.Name;
                docEditor.WriteMessage($"\nSubassembly selecionada: {sourceSubName}");
                tr.Commit();
            }

            if (string.IsNullOrWhiteSpace(sourceSubName))
            {
                docEditor.WriteMessage("\nNome da subassembly selecionada inválido.");
                return;
            }

            // 2) (Opcional) usuário seleciona no desenho as subassemblies que serão usadas na substituição
            HashSet<string> nomesSelecionados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding =
                "\nSelecione as subassemblies que serão usadas na substituição (Enter para usar todas as definições): ";

            PromptSelectionResult psr = docEditor.GetSelection(pso);
            if (psr.Status == PromptStatus.OK && psr.Value != null)
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in psr.Value.GetObjectIds())
                    {
                        DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                        Subassembly subSel = obj as Subassembly;
                        if (subSel != null)
                        {
                            if (!nomesSelecionados.Contains(subSel.Name))
                            {
                                nomesSelecionados.Add(subSel.Name);
                            }
                        }
                    }

                    tr.Commit();
                }

                if (nomesSelecionados.Count > 0)
                {
                    docEditor.WriteMessage(
                        $"\nForam selecionadas {nomesSelecionados.Count} definições de subassembly para o pacote.");
                }
            }

            // 3) Monta lista de definições disponíveis (filtrando pelos nomes selecionados, se houver)
            List<SubassemblyInfo> definicoes = new List<SubassemblyInfo>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId subDefId in civilDb.SubassemblyCollection)
                {
                    Subassembly def = (Subassembly)tr.GetObject(subDefId, OpenMode.ForRead);

                    if (nomesSelecionados.Count > 0 && !nomesSelecionados.Contains(def.Name))
                    {
                        continue;
                    }

                    SubassemblyInfo info = new SubassemblyInfo(subDefId, def.Name);
                    definicoes.Add(info);
                }

                tr.Commit();
            }

            if (definicoes.Count == 0)
            {
                docEditor.WriteMessage("\nNenhuma definição de subassembly encontrada com os critérios atuais.");
                return;
            }

            // 4) Form para o usuário montar o pacote (ordem de conexão)
            SubassemblyPackageForm form = new SubassemblyPackageForm(sourceSubName, definicoes);
            WinForms.DialogResult dr = Application.ShowModalDialog(form);

            if (dr != WinForms.DialogResult.OK || form.SelectedSubassemblies.Count == 0)
            {
                docEditor.WriteMessage("\nComando cancelado pelo usuário.");
                return;
            }

            List<SubassemblyInfo> pacote = form.SelectedSubassemblies;

            // 5) Form para configurar o padrão de conexão (âncoras)
            SubassemblyConnectionForm conexaoForm = new SubassemblyConnectionForm(pacote);
            WinForms.DialogResult dr2 = Application.ShowModalDialog(conexaoForm);

            if (dr2 != WinForms.DialogResult.OK)
            {
                docEditor.WriteMessage("\nConfiguração de conexão cancelada pelo usuário.");
                return;
            }

            int[] anchorIndices = conexaoForm.AnchorIndices;
            if (anchorIndices == null || anchorIndices.Length != pacote.Count)
            {
                docEditor.WriteMessage("\nConfiguração de conexão inválida.");
                return;
            }

            // 6) Converte lista em arrays (usados na cópia)
            int countBases = pacote.Count;
            ObjectId[] assemblyBase = new ObjectId[countBases];
            string[] subNames = new string[countBases];

            for (int i = 0; i < countBases; i++)
            {
                assemblyBase[i] = pacote[i].Id;
                subNames[i] = pacote[i].Name;
            }

            // 7) Percorre assemblies e substitui TODAS as instâncias com o mesmo Name
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms =
                    (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId entId in ms)
                {
                    Entity ent = (Entity)tr.GetObject(entId, OpenMode.ForRead);
                    if (!(ent is Assembly))
                    {
                        continue;
                    }

                    Assembly asm = (Assembly)tr.GetObject(entId, OpenMode.ForWrite);
                    AssemblyGroupCollection grupos = asm.Groups;

                    foreach (AssemblyGroup g in grupos)
                    {
                        ObjectIdCollection subIds = g.GetSubassemblyIds();
                        ObjectId[] current = new ObjectId[subIds.Count];
                        subIds.CopyTo(current, 0);

                        foreach (ObjectId sid in current)
                        {
                            Subassembly sub1 =
                                (Subassembly)tr.GetObject(sid, OpenMode.ForRead);

                            if (!string.Equals(sub1.Name, sourceSubName,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            int side = GetSide(sub1);

                            // Âncora inicial: P1, fallback 1º ponto
                            Point initialAnchor = GetPointByName(sub1, "P1");
                            if (initialAnchor == null)
                            {
                                initialAnchor = GetPointByIndex(sub1, 1);
                            }

                            if (initialAnchor == null)
                            {
                                docEditor.WriteMessage(
                                    $"\nSubassembly '{sub1.Name}' sem ponto de ancoragem. Ignorada.");
                                continue;
                            }

                            // Lista das subassemblies recém-inseridas, para suportar conexões em árvore
                            List<Subassembly> insertedSubs = new List<Subassembly>();

                            for (int baseIndex = 0; baseIndex < assemblyBase.Length; baseIndex++)
                            {
                                if (assemblyBase[baseIndex].IsNull)
                                {
                                    docEditor.WriteMessage(
                                        $"\nBase[{baseIndex}] '{subNames[baseIndex]}' não encontrada. Pulando.");
                                    continue;
                                }

                                int anchorRef = anchorIndices[baseIndex];

                                Point currentAnchor;

                                if (anchorRef < 0)
                                {
                                    // -1 = âncora original (P1 da sub que está sendo substituída)
                                    currentAnchor = initialAnchor;
                                }
                                else
                                {
                                    if (anchorRef >= insertedSubs.Count)
                                    {
                                        // Se o índice for inválido, usar o último inserido como fallback
                                        if (insertedSubs.Count > 0)
                                        {
                                            Point pTailFallback =
                                                GetLastPoint(insertedSubs[insertedSubs.Count - 1]);
                                            currentAnchor = pTailFallback ?? initialAnchor;
                                        }
                                        else
                                        {
                                            currentAnchor = initialAnchor;
                                        }
                                    }
                                    else
                                    {
                                        Subassembly anchorSub = insertedSubs[anchorRef];
                                        Point anchorPoint = GetLastPoint(anchorSub);
                                        currentAnchor = anchorPoint ?? initialAnchor;
                                    }
                                }

                                ObjectIdCollection idsBefore = g.GetSubassemblyIds();
                                int countBefore = idsBefore.Count;

                                try
                                {
                                    asm.CopySubassembly(assemblyBase[baseIndex], currentAnchor);
                                }
                                catch (System.Exception ex)
                                {
                                    docEditor.WriteMessage(
                                        $"\nErro ao copiar '{subNames[baseIndex]}': {ex.Message}");
                                    break;
                                }

                                ObjectIdCollection idsAfter = g.GetSubassemblyIds();
                                if (idsAfter.Count <= countBefore)
                                {
                                    docEditor.WriteMessage(
                                        $"\nNão consegui identificar a subassembly '{subNames[baseIndex]}' recém-copiada.");
                                    break;
                                }

                                ObjectId newSubId = idsAfter[idsAfter.Count - 1];

                                Subassembly newSub =
                                    (Subassembly)tr.GetObject(newSubId, OpenMode.ForWrite);
                                SetSide(newSub, side);

                                insertedSubs.Add(newSub);
                            }

                            // Apaga a subassembly antiga depois de criar a sequência/árvore
                            Subassembly sub1W =
                                (Subassembly)tr.GetObject(sid, OpenMode.ForWrite);
                            sub1W.Erase();
                        }
                    }
                }

                tr.Commit();
            }

            // Se quiser, pode chamar aqui depois:
            // RebuildAllCorridors();
            // ClonarSurfaceTargetParaNovosTaludes();
        }

        private static void SetSide(Subassembly sub, int side)
        {
            try
            {
                sub.UpgradeOpen();
                if (side == 0)
                {
                    sub.Side = SubassemblySideType.Right;
                }
                else if (side == 1)
                {
                    sub.Side = SubassemblySideType.Left;
                }
            }
            catch
            {
            }
        }

        private static Point GetLastPoint(Subassembly sub)
        {
            try
            {
                PointCollection pts = sub.Points;
                if (pts == null || pts.Count == 0)
                {
                    return null;
                }

                Point p = pts[pts.Count - 1];
                return p;
            }
            catch
            {
                return null;
            }
        }

        private static int GetSide(Subassembly sub)
        {
            try
            {
                ParamLongCollection pL = sub.ParamsLong;
                if (pL != null)
                {
                    // 0 = Right, 1 = Left
                    return (int)pL.Value("Side");
                }
            }
            catch
            {
            }

            return 0;
        }

        private static Point GetPointByName(Subassembly sub, string name)
        {
            try
            {
                PointCollection pts = sub.Points;
                if (pts == null || pts.Count == 0)
                {
                    return null;
                }

                foreach (Point p in pts)
                {
                    foreach (string code in p.Codes)
                    {
                        if (string.Equals(code, name, StringComparison.OrdinalIgnoreCase))
                        {
                            return p;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static Point GetPointByIndex(Subassembly sub, int index1Based)
        {
            try
            {
                PointCollection pts = sub.Points;
                if (pts == null || pts.Count == 0)
                {
                    return null;
                }

                if (index1Based < 1 || index1Based > pts.Count)
                {
                    return null;
                }

                Point p = pts[index1Based - 1];
                return p;
            }
            catch
            {
                return null;
            }
        }

        private static void RebuildAllCorridors()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Database db = civilDoc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId cid in civilDb.CorridorCollection)
                {
                    Corridor cor = (Corridor)tr.GetObject(cid, OpenMode.ForWrite);
                    cor.Rebuild();
                }

                tr.Commit();
            }

            Application.DocumentManager.MdiActiveDocument.Editor.Regen();
        }

        private static void ClonarSurfaceTargetParaNovosTaludes()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Database db = civilDoc.Database;
            Editor ed = civilDoc.Editor;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId cid in civilDb.CorridorCollection)
                {
                    Corridor cor = (Corridor)tr.GetObject(cid, OpenMode.ForWrite);

                    SubassemblyTargetInfoCollection targets = cor.GetTargets();

                    ObjectIdCollection surfaceTemplate = null;

                    for (int i = 0; i < targets.Count; i++)
                    {
                        SubassemblyTargetInfo info = targets[i];

                        if (info.TargetType != SubassemblyLogicalNameType.Surface)
                        {
                            continue;
                        }

                        ObjectIdCollection ids = info.TargetIds;
                        if (ids != null && ids.Count > 0)
                        {
                            surfaceTemplate = new ObjectIdCollection();
                            foreach (ObjectId sid in ids)
                            {
                                surfaceTemplate.Add(sid);
                            }

                            break;
                        }
                    }

                    if (surfaceTemplate == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < targets.Count; i++)
                    {
                        SubassemblyTargetInfo info = targets[i];

                        if (info.TargetType != SubassemblyLogicalNameType.Surface)
                        {
                            continue;
                        }

                        ObjectIdCollection ids = info.TargetIds;
                        if (ids == null || ids.Count == 0)
                        {
                            info.TargetIds = surfaceTemplate;
                        }
                    }

                    cor.SetTargets(targets);
                    cor.Rebuild();
                }

                tr.Commit();
            }

            ed.Regen();
        }
    }

    internal class SubassemblyInfo
    {
        internal ObjectId Id { get; }
        internal string Name { get; }

        internal SubassemblyInfo(ObjectId id, string name)
        {
            Id = id;
            Name = name;
        }

        public override string ToString()
        {
            return Name;
        }
    }

    internal class SubassemblyPackageForm : WinForms.Form
    {
        internal List<SubassemblyInfo> SelectedSubassemblies { get; }

        private WinForms.ListBox _lstAvailable;
        private WinForms.ListBox _lstSelected;
        private WinForms.Button _btnAdd;
        private WinForms.Button _btnRemove;
        private WinForms.Button _btnUp;
        private WinForms.Button _btnDown;
        private WinForms.Button _btnOk;
        private WinForms.Button _btnCancel;

        internal SubassemblyPackageForm(string sourceSubName, List<SubassemblyInfo> definicoes)
        {
            SelectedSubassemblies = new List<SubassemblyInfo>();

            Text = "Pacote de Subassemblies";
            FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            StartPosition = WinForms.FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            Width = 640;
            Height = 420;

            WinForms.Label lblOrig = new WinForms.Label();
            lblOrig.Text = "Subassembly original: " + sourceSubName;
            lblOrig.AutoSize = true;
            lblOrig.Left = 10;
            lblOrig.Top = 10;
            Controls.Add(lblOrig);

            _lstAvailable = new WinForms.ListBox();
            _lstAvailable.Left = 10;
            _lstAvailable.Top = 40;
            _lstAvailable.Width = 240;
            _lstAvailable.Height = 260;

            _lstSelected = new WinForms.ListBox();
            _lstSelected.Left = 370;
            _lstSelected.Top = 40;
            _lstSelected.Width = 240;
            _lstSelected.Height = 260;

            foreach (SubassemblyInfo info in definicoes)
            {
                _lstAvailable.Items.Add(info);
            }

            _btnAdd = new WinForms.Button();
            _btnAdd.Text = "Adicionar >>";
            _btnAdd.Left = 270;
            _btnAdd.Top = 80;
            _btnAdd.Width = 90;
            _btnAdd.Click += OnAddClick;

            _btnRemove = new WinForms.Button();
            _btnRemove.Text = "<< Remover";
            _btnRemove.Left = 270;
            _btnRemove.Top = 120;
            _btnRemove.Width = 90;
            _btnRemove.Click += OnRemoveClick;

            _btnUp = new WinForms.Button();
            _btnUp.Text = "↑";
            _btnUp.Left = 370;
            _btnUp.Top = 310;
            _btnUp.Width = 40;
            _btnUp.Click += OnUpClick;

            _btnDown = new WinForms.Button();
            _btnDown.Text = "↓";
            _btnDown.Left = 420;
            _btnDown.Top = 310;
            _btnDown.Width = 40;
            _btnDown.Click += OnDownClick;

            _btnOk = new WinForms.Button();
            _btnOk.Text = "OK";
            _btnOk.Left = 430;
            _btnOk.Top = 340;
            _btnOk.Width = 80;
            _btnOk.Click += OnOkClick;

            _btnCancel = new WinForms.Button();
            _btnCancel.Text = "Cancelar";
            _btnCancel.Left = 520;
            _btnCancel.Top = 340;
            _btnCancel.Width = 80;
            _btnCancel.Click += OnCancelClick;

            Controls.Add(_lstAvailable);
            Controls.Add(_lstSelected);
            Controls.Add(_btnAdd);
            Controls.Add(_btnRemove);
            Controls.Add(_btnUp);
            Controls.Add(_btnDown);
            Controls.Add(_btnOk);
            Controls.Add(_btnCancel);
        }

        private void OnAddClick(object sender, EventArgs e)
        {
            if (_lstAvailable.SelectedItem == null)
            {
                return;
            }

            SubassemblyInfo info = (SubassemblyInfo)_lstAvailable.SelectedItem;
            _lstSelected.Items.Add(info);
        }

        private void OnRemoveClick(object sender, EventArgs e)
        {
            if (_lstSelected.SelectedItem == null)
            {
                return;
            }

            int idx = _lstSelected.SelectedIndex;
            _lstSelected.Items.RemoveAt(idx);
        }

        private void OnUpClick(object sender, EventArgs e)
        {
            if (_lstSelected.SelectedItem == null)
            {
                return;
            }

            int idx = _lstSelected.SelectedIndex;
            if (idx <= 0)
            {
                return;
            }

            object item = _lstSelected.Items[idx];
            _lstSelected.Items.RemoveAt(idx);
            _lstSelected.Items.Insert(idx - 1, item);
            _lstSelected.SelectedIndex = idx - 1;
        }

        private void OnDownClick(object sender, EventArgs e)
        {
            if (_lstSelected.SelectedItem == null)
            {
                return;
            }

            int idx = _lstSelected.SelectedIndex;
            if (idx < 0 || idx >= _lstSelected.Items.Count - 1)
            {
                return;
            }

            object item = _lstSelected.Items[idx];
            _lstSelected.Items.RemoveAt(idx);
            _lstSelected.Items.Insert(idx + 1, item);
            _lstSelected.SelectedIndex = idx + 1;
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            if (_lstSelected.Items.Count == 0)
            {
                WinForms.MessageBox.Show(
                    "Selecione pelo menos uma subassembly para formar o pacote.",
                    "Atenção",
                    WinForms.MessageBoxButtons.OK,
                    WinForms.MessageBoxIcon.Warning);
                return;
            }

            SelectedSubassemblies.Clear();
            foreach (object obj in _lstSelected.Items)
            {
                SubassemblyInfo info = (SubassemblyInfo)obj;
                SelectedSubassemblies.Add(info);
            }

            DialogResult = WinForms.DialogResult.OK;
            Close();
        }

        private void OnCancelClick(object sender, EventArgs e)
        {
            DialogResult = WinForms.DialogResult.Cancel;
            Close();
        }
    }

    internal class SubassemblyConnectionForm : WinForms.Form
    {
        internal int[] AnchorIndices { get; private set; }

        private WinForms.NumericUpDown[] _anchorControls;

        internal SubassemblyConnectionForm(List<SubassemblyInfo> subs)
        {
            int count = subs.Count;

            Text = "Configuração de Conexão das Subassemblies";
            FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            StartPosition = WinForms.FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            Width = 520;
            Height = 420;

            AnchorIndices = new int[count];
            _anchorControls = new WinForms.NumericUpDown[count];

            WinForms.Label lblInfo = new WinForms.Label();
            lblInfo.Text =
                "Para cada subassembly, informe o índice de ancoragem:\n" +
                "-1 = origem (ponto da sub original)\n" +
                "0..n-1 = índice da sub já inserida (permite ramificações).";
            lblInfo.AutoSize = true;
            lblInfo.Left = 10;
            lblInfo.Top = 10;
            Controls.Add(lblInfo);

            WinForms.Panel panel = new WinForms.Panel();
            panel.Left = 10;
            panel.Top = 60;
            panel.Width = 480;
            panel.Height = 280;
            panel.AutoScroll = true;
            Controls.Add(panel);

            int y = 10;
            for (int i = 0; i < count; i++)
            {
                WinForms.Label lbl = new WinForms.Label();
                lbl.Text = $"{i}: {subs[i].Name}";
                lbl.Left = 10;
                lbl.Top = y + 4;
                lbl.Width = 340;

                WinForms.NumericUpDown nud = new WinForms.NumericUpDown();
                nud.Left = 360;
                nud.Top = y;
                nud.Width = 80;
                nud.Minimum = -1;
                nud.Maximum = count - 1;

                // padrão: primeira ancora na origem, demais em cadeia
                if (i == 0)
                {
                    nud.Value = -1;
                }
                else
                {
                    nud.Value = i - 1;
                }

                panel.Controls.Add(lbl);
                panel.Controls.Add(nud);

                _anchorControls[i] = nud;

                y += 28;
            }

            WinForms.Button btnOk = new WinForms.Button();
            btnOk.Text = "OK";
            btnOk.Left = 320;
            btnOk.Top = 350;
            btnOk.Width = 80;
            btnOk.Click += OnOkClick;

            WinForms.Button btnCancel = new WinForms.Button();
            btnCancel.Text = "Cancelar";
            btnCancel.Left = 410;
            btnCancel.Top = 350;
            btnCancel.Width = 80;
            btnCancel.Click += OnCancelClick;

            Controls.Add(btnOk);
            Controls.Add(btnCancel);
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            int n = _anchorControls.Length;

            for (int i = 0; i < n; i++)
            {
                int v = (int)_anchorControls[i].Value;

                if (v < -1 || v >= i)
                {
                    WinForms.MessageBox.Show(
                        $"Índice de ancoragem inválido para a sub {i}. Use -1 ou um valor entre 0 e {i - 1}.",
                        "Erro",
                        WinForms.MessageBoxButtons.OK,
                        WinForms.MessageBoxIcon.Error);
                    return;
                }

                AnchorIndices[i] = v;
            }

            DialogResult = WinForms.DialogResult.OK;
            Close();
        }

        private void OnCancelClick(object sender, EventArgs e)
        {
            DialogResult = WinForms.DialogResult.Cancel;
            Close();
        }
    }
}
