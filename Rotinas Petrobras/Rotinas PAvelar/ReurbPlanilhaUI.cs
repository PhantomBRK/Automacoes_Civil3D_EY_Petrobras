// Referências necessárias no projeto:
// - Autodesk.AutoCAD.ApplicationServices
// - Autodesk.AutoCAD.DatabaseServices
// - Autodesk.AutoCAD.EditorInput
// - Autodesk.AutoCAD.Runtime
// - Autodesk.Civil.ApplicationServices
// - Autodesk.Civil.DatabaseServices
// - Autodesk.Aec.PropertyData
// - Autodesk.Aec.PropertyData.DatabaseServices
// - System.Windows.Forms
// - OfficeOpenXml (EPPlus - via NuGet)
// - HtmlAgilityPack (via NuGet)
// Aliases obrigatórios:
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Autodesk.Aec.PropertyData;
using Autodesk.Aec.PropertyData.DatabaseServices;

using OfficeOpenXml;

using HtmlAgilityPack;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using DataTable = System.Data.DataTable;

using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DataType = Autodesk.Aec.PropertyData.DataType;

namespace AutomacoesCivil3D
{
    public class ReurbPlanilhaUI
    {
        private const string PSET_NAME = "RELATORIO DE REGULARIZAÇÃO FUNDIÁRIA";

        [CommandMethod("REURB_XLS_APLICAR")]
        public static async void LerPlanilhaEscolherLinhaAplicar()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database db = Manager.DocData;

            try
            {
                // 1) Seleciona arquivo Excel
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.Filter = "Planilhas Excel (*.xlsx)|*.xlsx";
                ofd.Title = "Selecione a planilha Olympus";
                DialogResult dr = ofd.ShowDialog();
                if (dr != DialogResult.OK) return;
                string caminho = ofd.FileName;

                // 2) Carrega DataTable com colunas desejadas
                DataTable dt = CarregarTabelaBasica(caminho);
                if (dt.Rows.Count == 0)
                {
                    docEditor.WriteMessage("\nPlanilha sem linhas válidas.");
                    return;
                }

                // 3) Abre UI para escolher a linha
                LinhaSelecionadaForm form = new LinhaSelecionadaForm(dt);
                DialogResult ds = Application.ShowModalDialog(form);
                if (ds != DialogResult.OK) return;

                DataRow linha = form.LinhaSelecionada;
                if (linha == null)
                {
                    docEditor.WriteMessage("\nNenhuma linha selecionada.");
                    return;
                }

                string url = linha["Diagnostico"].ToString();
                if (string.IsNullOrWhiteSpace(url))
                {
                    docEditor.WriteMessage("\nURL (Diagnóstico) vazia na linha escolhida.");
                    return;
                }

                // 4) Baixa e extrai HTML -> dicionário PSET
                Dictionary<string, string> dados = await BaixarEExtrair(url);
                if (dados.Count == 0)
                {
                    docEditor.WriteMessage("\nNada extraído do link.");
                    return;
                }

                // 5) Seleciona objeto alvo no desenho
                PromptEntityOptions peo = new PromptEntityOptions("\nSelecione o objeto destino:");
                PromptEntityResult per = docEditor.GetEntity(peo);
                if (per.Status != PromptStatus.OK) return;
                ObjectId alvoId = per.ObjectId;

                // 6) Garante Pset, cria props que faltarem e grava valores
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    //PropertySetDefinition psetDef = GarantirPsetDefinition(db, tr, PSET_NAME);
                    PropertySetDefinition psetDef = new PropertySetDefinition();
                    //HashSet<string> existentes = ObterNomesPropriedades(psetDef, tr, db);
                    HashSet<string> existentes = new HashSet<string>();
                    foreach (KeyValuePair<string, string> kv in dados)
                    {
                        
                        string titulo = kv.Key;
                        if (existentes.Contains(titulo)) continue;

                        PropertyDefinition pdNew = new PropertyDefinition();
                        pdNew.SetToStandard(db);
                        pdNew.SubSetDatabaseDefaults(db);
                        pdNew.Name = titulo;
                        pdNew.Description = titulo;
                        pdNew.DataType = DataType.Text;
                        pdNew.DefaultData = " - ";
                        psetDef.Definitions.Add(pdNew);
                    }

                    Entity br = (Entity)tr.GetObject(alvoId, OpenMode.ForRead);
                    ObjectId psId = PropertyDataServices.GetPropertySet(br, psetDef.ObjectId);
                    if (psId.IsNull)
                    {
                        PropertyDataServices.AddPropertySet(br, psetDef.ObjectId);
                        psId = PropertyDataServices.GetPropertySet(br, psetDef.ObjectId);
                    }

                    PropertySet ps = (PropertySet)tr.GetObject(psId, OpenMode.ForWrite);
                    foreach (KeyValuePair<string, string> kv in dados)
                    {
                        string titulo = kv.Key;
                        string valor = kv.Value ?? string.Empty;
                        int pid = ps.PropertyNameToId(titulo);
                        if (pid >= 0) { ps.SetAt(pid, valor); }
                    }

                    tr.Commit();
                }

                docEditor.WriteMessage("\nAplicado a partir da linha selecionada.");
            }
            catch (Exception ex)
            {
                Editor ed = Manager.DocEditor;
                ed.WriteMessage($"\nErro: {ex.Message}");
            }
        }

        // ======== Leitura da planilha (EPPlus) ========
        private static DataTable CarregarTabelaBasica(string caminhoXlsx)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("COD");        // A
            dt.Columns.Add("Referencia"); // B
            dt.Columns.Add("Quadra");     // I
            dt.Columns.Add("Lote");       // J
            dt.Columns.Add("Diagnostico");// L

            try
            {
                ExcelPackage.License.SetNonCommercialPersonal("Gleison Bruno da Costa");

                // abre com compartilhamento para evitar lock do Excel/Explorer
                using (System.IO.FileStream fs = new System.IO.FileStream(
                    caminhoXlsx, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                using (ExcelPackage pkg = new ExcelPackage(fs))
                {
                    ExcelWorksheet ws = pkg.Workbook.Worksheets.FirstOrDefault();
                    if (ws == null || ws.Dimension == null) return dt;

                    int lastRow = ws.Dimension.End.Row;
                    int lastCol = ws.Dimension.End.Column;

                    // garante que as colunas A,B,I,J,L existem
                    int colA = 1, colB = 2, colI = 9, colJ = 10, colL = 12;
                    if (lastCol < Math.Max(Math.Max(colJ, colI), colL)) return dt;

                    for (int r = 2; r <= lastRow; r++) // pula cabeçalho
                    {
                        string cod = ws.Cells[r, colA].Text?.Trim();
                        string referencia = ws.Cells[r, colB].Text?.Trim();
                        string quadra = ws.Cells[r, colI].Text?.Trim();
                        string lote = ws.Cells[r, colJ].Text?.Trim();
                        string diagnostico = ws.Cells[r, colL].Text?.Trim();

                        // pula linhas vazias
                        if (string.IsNullOrWhiteSpace(cod) && string.IsNullOrWhiteSpace(referencia)
                            && string.IsNullOrWhiteSpace(quadra) && string.IsNullOrWhiteSpace(lote)
                            && string.IsNullOrWhiteSpace(diagnostico)) continue;

                        DataRow row = dt.NewRow();
                        row["COD"] = cod ?? string.Empty;
                        row["Referencia"] = referencia ?? string.Empty;
                        row["Quadra"] = quadra ?? string.Empty;
                        row["Lote"] = lote ?? string.Empty;
                        row["Diagnostico"] = diagnostico ?? string.Empty;
                        dt.Rows.Add(row);
                    }
                }
            }
            catch (System.Exception ex)
            {
                // Mostra o motivo real no Editor. Evita fatal error por exceção não tratada.
                Editor ed = Manager.DocEditor;
                ed.WriteMessage($"\nFalha lendo Excel: {ex.GetType().Name}: {ex.Message}");
                return dt; // retorna vazio para não quebrar o fluxo
            }

            return dt;
        }


        // ======== UI seleção de linha ========
        private class LinhaSelecionadaForm : Form
        {
            private readonly DataGridView _grid;
            private readonly Button _ok;
            private readonly Button _cancel;
            private readonly DataTable _data;
            public DataRow LinhaSelecionada { get; private set; }

            public LinhaSelecionadaForm(DataTable dt)
            {
                _data = dt;

                this.Text = "Selecione a linha (COD, Referência, Quadra, Lote)";
                this.StartPosition = FormStartPosition.CenterScreen;
                this.Width = 900;
                this.Height = 500;

                _grid = new DataGridView();
                _grid.Dock = DockStyle.Top;
                _grid.Height = 400;
                _grid.ReadOnly = true;
                _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                _grid.MultiSelect = false;
                _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                _grid.DataSource = _data;

                // Oculta a coluna Diagnostico
                _grid.Columns["Diagnostico"].Visible = false;

                _ok = new Button();
                _ok.Text = "OK";
                _ok.Width = 100;
                _ok.Top = 410;
                _ok.Left = 680;
                _ok.Click += Ok_Click;

                _cancel = new Button();
                _cancel.Text = "Cancelar";
                _cancel.Width = 100;
                _cancel.Top = 410;
                _cancel.Left = 790;
                _cancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

                this.Controls.Add(_grid);
                this.Controls.Add(_ok);
                this.Controls.Add(_cancel);
            }

            private void Ok_Click(object sender, EventArgs e)
            {
                if (_grid.CurrentRow == null)
                {
                    MessageBox.Show("Selecione uma linha.");
                    return;
                }
                DataRowView drv = (DataRowView)_grid.CurrentRow.DataBoundItem;
                LinhaSelecionada = drv.Row;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }

        // ======== HTML -> dicionário com chaves do PSET ========
        private static async Task<Dictionary<string, string>> BaixarEExtrair(string url)
        {
            Dictionary<string, string> saida = new Dictionary<string, string>();

            using (HttpClient http = new HttpClient())
            {
                string html = await http.GetStringAsync(url);
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(html);

                IEnumerable<HtmlNode> tds = doc.DocumentNode.SelectNodes("//td|//th") ?? Enumerable.Empty<HtmlNode>();
                List<string> cel = tds.Select(n => Limpar(n.InnerText)).Where(s => s.Length > 0).ToList();

                Dictionary<string, string> bruto = new Dictionary<string, string>();
                for (int i = 0; i + 1 < cel.Count; i++)
                {
                    string a = cel[i];
                    string b = cel[i + 1];
                    bool rotulo = a.EndsWith(":") || (a.Length <= 30 && a.ToUpperInvariant() == a);
                    if (rotulo && Valido(b))
                    {
                        string chave = a.TrimEnd(':').Trim();
                        if (!bruto.ContainsKey(chave)) bruto.Add(chave, b);
                    }
                }

                IEnumerable<HtmlNode> blocks = doc.DocumentNode.SelectNodes("//div[strong]") ?? Enumerable.Empty<HtmlNode>();
                foreach (HtmlNode n in blocks)
                {
                    string rot = Limpar(n.SelectSingleNode(".//strong")?.InnerText ?? "");
                    string val = Limpar(n.InnerText.Replace(rot, ""));
                    if (rot.Length > 0 && Valido(val) && !bruto.ContainsKey(rot))
                    {
                        bruto.Add(rot, val);
                    }
                }

                Dictionary<string, string> mapa = new Dictionary<string, string>
                {
                    { "CÓD", "COD" },
                    { "REFERÊNCIA", "Referencia" },
                    { "PREFEITURA", "Prefeitura" },
                    { "BAIRRO", "Bairro" },
                    { "QUADRA", "Quadra" },
                    { "LOTE", "Lote" },
                    { "Data", "Visita_Data" },
                    { "Hora", "Visita_Hora" },
                    { "Status", "Visita_Status" },
                    { "MUNICÍPIO", "Municipio" },
                    { "LOTEAMENTO", "Loteamento" },
                    { "NOME", "Nome_Titular" },
                    { "CPF", "CPF_Titular" },
                    { "RG", "RG_Titular" },
                    { "UF", "UF_RG" },
                    { "DATA EXPEDIÇÃO", "Data_Emissao_RG" },
                    { "DATA NASCIMENTO", "Data_Nascimento" },
                    { "SEXO", "Sexo" },
                    { "ESTADO CIVIL", "Estado_Civil" },
                    { "NACIONALIDADE", "Nacionalidade" },
                    { "PROFISSÃO", "Profissao" },
                    { "RENDA COMPROVADA", "Renda_Comprovada" },
                    { "RENDA NÃO COMPROVADA", "Renda_Nao_Comprovada" }
                };

                foreach (KeyValuePair<string, string> kv in mapa)
                {
                    KeyValuePair<string, string>? hit = bruto.FirstOrDefault(p => Normalizar(p.Key) == Normalizar(kv.Key));
                    if (hit.HasValue) { saida[kv.Value] = hit.Value.Value; }
                }
            }

            return saida;
        }

        // ===== util =====
        private static PropertySetDefinition GarantirPsetDefinition(Database db, Transaction tr, string nome)
        {

            DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);

            if (!dictionary.Has(nome, tr))
            {
                ObjectId novoId = dictionary.GetAt(nome);
                PropertySetDefinition defExistente = (PropertySetDefinition)tr.GetObject(novoId, OpenMode.ForRead);
                if (defExistente != null && defExistente.Name == nome) return defExistente;
            }


            PropertySetDefinition defNovo = new PropertySetDefinition();
            defNovo.SetToStandard(db);
            defNovo.AlternateName = nome;

            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
            string chave = nome;
            int i = 1;
            while (nod.Contains(chave)) { chave = nome + "_" + i.ToString(); i++; }
            nod.SetAt(chave, defNovo);
            tr.AddNewlyCreatedDBObject(defNovo, true);
            return defNovo;
        }

        private static HashSet<string> ObterNomesPropriedades(PropertySetDefinition def, Transaction tr, Database db)
        {

            HashSet<string> nomes = new HashSet<string>();
            for (int i = 0; i < def.Definitions.Count; i++)
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                ObjectId novoId = nod.GetAt(def.Definitions[i].Name);
                PropertySet pdExistente = (PropertySet)tr.GetObject(novoId, OpenMode.ForWrite);
                nomes.Add(pdExistente.Name);
            }
            return nomes;
        }


        private static string Limpar(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            string r = HtmlEntity.DeEntitize(s).Trim();
            r = Regex.Replace(r, @"\s+", " ");
            return r;
        }
        private static bool Valido(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (s == "-" || s.Equals("N/A", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
        private static string Normalizar(string s)
        {
            string t = s.ToUpperInvariant().Trim().Replace(":", "");
            t = t.Replace("Ç", "C").Replace("Ã", "A").Replace("Â", "A").Replace("Á", "A").Replace("À", "A")
                 .Replace("É", "E").Replace("Ê", "E").Replace("Í", "I").Replace("Ó", "O").Replace("Ô", "O")
                 .Replace("Ú", "U").Replace("Ü", "U");
            return t;
        }
    }
}
