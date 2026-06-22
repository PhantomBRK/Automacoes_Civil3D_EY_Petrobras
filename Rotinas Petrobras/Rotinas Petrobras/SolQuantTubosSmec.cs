using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AutomacoesCivil3D;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using OfficeOpenXml;
using SOLIDOS;

namespace RotinasPetrobras.Quantitativos
{
    /// <summary>
    /// SOL_QUANT_TUBOS_SMEC: aglutina os quantitativos de TODOS os tubos do DWG (global)
    /// e classifica em itens SMEC, preenchendo o FORMULÁRIO DE SOLICITAÇÕES.
    ///
    /// Lê os valores já calculados no construtor (Opção A — auditável), agrega por item
    /// SMEC e escreve Família (col G) + Item (col H) + Quantidade (col J). A Unidade de
    /// Medida (col I) é fórmula automática do formulário — não é tocada.
    ///
    /// Conexões (Family contém "CONEXÕES") são contadas por SubType e classificadas
    /// como Família "Outros" (sem classe SMEC ainda → check de consistência fica laranja).
    /// </summary>
    public partial class SolQuantTubos
    {
        // ---- Template de destino (FORMULÁRIO + TABELAS_AUXILIARES) ----
        // Instalado JUNTO do plugin (Resources\Quantitativos do bundle).
        private static string TEMPLATE_DRENAGEM_ORIGEM =>
            BundlePaths.Resource("Quantitativos", "Drenagem_1 2.xlsx");

        // ---- Famílias SMEC ----
        private const string FAMILIA_TUBULACOES = "TUBULAÇÕES";
        private const string FAMILIA_OUTROS = "Outros";

        // ---- Itens SMEC (grafia confirmada na TABELAS_AUXILIARES, coluna TUBULAÇÕES) ----
        private const string IT_TUBO_FMT   = "TUBO DE FERRO FUNDIDO PONTA-BOLSA CLASSE K-7 (D = {0}mm)";
        private const string IT_ESC_ATE15  = "ESCAVAÇÃO DE VALA NÃO ESCORADA ATÉ 1,5m";
        private const string IT_ESC_15_175 = "ESCAVAÇÃO DE VALA NÃO ESCORADA DE 1,5m ATÉ 1,75m";
        private const string IT_ESC_ESCOR  = "ESCAVAÇÃO DE VALA ESCORADA MAIOR QUE 1,75m";
        private const string IT_APILOAMENTO = "APILOAMENTO DE FUNDO DE CAVA";
        private const string IT_BOTAFORA   = "BOTA-FORA";
        private const string IT_LASTRO     = "LASTRO DE AREIA COMERCIAL - ESPALHAMENTO MANUAL";
        private const string IT_EMBASAMENTO = "EMBASAMENTO DE TUBULAÇÃO COM AREIA";
        private const string IT_REATERRO   = "REATERRO";
        private const string IT_DEMOLICAO  = "DEMOLIÇÃO DE CONCRETO / ALVENARIA";

        // ---- FORMULÁRIO: colunas (1-based) ----
        private const int COL_DOCREF = 5;   // E - Documento de Referência
        private const int COL_FAMILIA = 7;  // G - Família (lista)
        private const int COL_ITEM = 8;     // H - Item (lista)
        // I (9) = Unidade de Medida (fórmula automática) — NÃO TOCAR
        private const int COL_QTD = 10;     // J - Quantidade
        private const int FORM_LINHA_INICIO = 11; // 1-10 = legenda/cabeçalho

        [CommandMethod("SOL_QUANT_SMEC")]
        [CommandMethod("SOL_QUANT_TUBOS_SMEC")]
        public void ExecutarSmec()
        {
            var doc = Manager.DocCad;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            try
            {
                string dwgPath = doc.Name;
                if (string.IsNullOrWhiteSpace(dwgPath) || !File.Exists(dwgPath))
                {
                    ed.WriteMessage("\n[SMEC] Salve o DWG antes de rodar.");
                    return;
                }
                string dwgNome = Path.GetFileNameWithoutExtension(dwgPath);
                string dwgDir = Path.GetDirectoryName(dwgPath) ?? Environment.CurrentDirectory;

                // 1) Prompt: haverá demolição/recomposição de piso? (greenfield = Não)
                bool incluirDemolicao = PerguntarDemolicao(ed);
                ed.WriteMessage($"\n[SMEC] Demolição/recomposição: {(incluirDemolicao ? "SIM" : "NÃO")}");

                // 2) Template de destino -> cópia na pasta do DWG
                string destino = GarantirFormularioLocal(dwgDir, dwgNome, ed);
                if (destino == null) return;

                // 3) Coleta tubos (válidos) + caixas + conexões
                var tubos = new List<TuboQuantData>();
                var caixas = new List<CaixaQuantData>();
                var conexoes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int fantasmas = 0;

                using (doc.LockDocument())
                using (var t = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)t.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)t.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId id in ms)
                    {
                        string fam = LerString(id, "Family");

                        // Caixas (Family "PETROBRAS - CAIXAS")
                        if (!string.IsNullOrEmpty(fam)
                            && fam.IndexOf("CAIXA", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var c = LerCaixa(id, dwgNome);
                            if (c != null && c.VolCA > 1e-6) caixas.Add(c);
                            continue;
                        }

                        // Conexões (devices pontuais Family "PETROBRAS - CONEXÕES")
                        if (!string.IsNullOrEmpty(fam)
                            && fam.IndexOf("CONEX", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string sub = LerString(id, "SubType");
                            if (string.IsNullOrWhiteSpace(sub)) sub = LerString(id, "Name");
                            if (string.IsNullOrWhiteSpace(sub)) sub = "CONEXÃO";
                            conexoes[sub] = conexoes.TryGetValue(sub, out int n) ? n + 1 : 1;
                            continue;
                        }

                        // Tubos
                        if (!EhTuboPetrobras(id)) continue;
                        var d = LerTubo(id, dwgNome);
                        if (d == null) continue;
                        if (EhFantasma(d)) { fantasmas++; continue; }
                        tubos.Add(d);
                    }
                    t.Commit();
                }

                if (tubos.Count == 0 && caixas.Count == 0 && conexoes.Count == 0)
                {
                    ed.WriteMessage("\n[SMEC] Nenhum tubo/caixa/conexão encontrado.");
                    return;
                }
                ed.WriteMessage($"\n[SMEC] {tubos.Count} tubos, {caixas.Count} caixas, {conexoes.Values.Sum()} conexões"
                    + (fantasmas > 0 ? $", {fantasmas} fantasma(s) descartado(s)." : "."));

                // Avisa tubos com Catálogo desatualizado vs geometria (cadastro a corrigir no DWG)
                foreach (var dv in tubos.Where(CatalogoDivergeDaGeometria))
                    ed.WriteMessage($"\n[SMEC][AVISO] Catálogo desatualizado em '{dv.Trecho}': "
                        + $"Catálogo={dv.Catalogo} vs DN real (geometria)={dv.DnMm}mm (usando geometria).");

                // 4) Agrega em linhas SMEC (tubos + caixas + conexões)
                var linhas = AgregarSmec(tubos, conexoes, incluirDemolicao, dwgNome);
                linhas.AddRange(AgregarSmecCaixas(caixas, incluirDemolicao, dwgNome));

                // 5) Escreve no FORMULÁRIO
                EscreverFormulario(destino, linhas, ed);
                ed.WriteMessage($"\n[SMEC] OK -> {destino}");
                ed.WriteMessage($"\n[SMEC] {linhas.Count} linha(s) escrita(s) no FORMULÁRIO DE SOLICITAÇÕES.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[SMEC] ERRO: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // ----------------------------------------------------------------------
        // Agregação SMEC
        // ----------------------------------------------------------------------

        private static List<SmecLinha> AgregarSmec(
            List<TuboQuantData> tubos,
            Dictionary<string, int> conexoes,
            bool incluirDemolicao,
            string docRef)
        {
            var linhas = new List<SmecLinha>();

            // --- TUBO por DN (mm) — DN pelo código do Catálogo (ver ResolverDn) ---
            var porDn = tubos
                .GroupBy(x => x.DnMm)
                .OrderBy(g => g.Key);
            foreach (var g in porDn)
            {
                double compr = g.Sum(x => x.Comprimento);
                if (compr <= 0) continue;
                linhas.Add(new SmecLinha(FAMILIA_TUBULACOES,
                    string.Format(IT_TUBO_FMT, g.Key), compr, docRef));
            }

            // --- ESCAVAÇÃO em 3 faixas (por profundidade média da vala) ---
            double escAte15 = 0, esc15_175 = 0, escEscor = 0;
            foreach (var x in tubos)
            {
                double profMedia = (x.ProfValaMont + x.ProfValaJus) / 2.0;
                if (profMedia <= 1.5) escAte15 += x.VolEscav;
                else if (profMedia <= 1.75) esc15_175 += x.VolEscav;
                else escEscor += x.VolEscav;
            }
            AddSe(linhas, FAMILIA_TUBULACOES, IT_ESC_ATE15, escAte15, docRef);
            AddSe(linhas, FAMILIA_TUBULACOES, IT_ESC_15_175, esc15_175, docRef);
            AddSe(linhas, FAMILIA_TUBULACOES, IT_ESC_ESCOR, escEscor, docRef);

            // --- Serviços (somatórios globais) ---
            AddSe(linhas, FAMILIA_TUBULACOES, IT_APILOAMENTO, tubos.Sum(x => x.AreaApiloamento), docRef);
            AddSe(linhas, FAMILIA_TUBULACOES, IT_LASTRO,      tubos.Sum(x => x.VolLastroAreia), docRef);
            AddSe(linhas, FAMILIA_TUBULACOES, IT_EMBASAMENTO, tubos.Sum(x => x.VolReatAreia),   docRef);
            AddSe(linhas, FAMILIA_TUBULACOES, IT_REATERRO,    tubos.Sum(x => x.VolReaterro),    docRef);
            AddSe(linhas, FAMILIA_TUBULACOES, IT_BOTAFORA,    tubos.Sum(x => x.VolBotaFora),    docRef);
            // NOTA: SMEC não tem item de fôrmas/escoramento p/ TUBULAÇÕES — não classifica
            // AreaEscoramento aqui (continua na memória de cálculo, coluna R do QUANT_TUBOS).

            if (incluirDemolicao)
                AddSe(linhas, FAMILIA_TUBULACOES, IT_DEMOLICAO, tubos.Sum(x => x.DemolRecomp), docRef);

            // --- Conexões -> Outros (por tipo) ---
            foreach (var kv in conexoes.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                linhas.Add(new SmecLinha(FAMILIA_OUTROS, kv.Key, kv.Value, docRef));

            return linhas;
        }

        private static void AddSe(List<SmecLinha> lst, string fam, string item, double qtd, string docRef)
        {
            if (qtd > 1e-6)
                lst.Add(new SmecLinha(fam, item, qtd, docRef));
        }

        // ----------------------------------------------------------------------
        // Escrita no FORMULÁRIO
        // ----------------------------------------------------------------------

        private static void EscreverFormulario(string destino, List<SmecLinha> linhas, Editor ed)
        {
            ExcelPackage.License.SetNonCommercialPersonal("Gleison Bruno da Costa");
            using var pkg = new ExcelPackage(new FileInfo(destino));
            var wb = pkg.Workbook;

            var form = wb.Worksheets.FirstOrDefault(
                w => w.Name.IndexOf("FORMUL", StringComparison.OrdinalIgnoreCase) >= 0);
            if (form == null)
                throw new InvalidOperationException("Aba 'FORMULÁRIO DE SOLICITAÇÕES' não encontrada.");

            // Cache de itens válidos POR família (normalizado -> grafia exata).
            var validosPorFamilia = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            int linha = FORM_LINHA_INICIO;
            int naoEncontrados = 0;
            foreach (var l in linhas)
            {
                string itemFinal = l.Item;

                // Valida contra a coluna da PRÓPRIA família (TUBULAÇÕES tem coluna,
                // CAIXAS tem coluna; "Outros" não tem -> escreve como está, fica laranja).
                if (!validosPorFamilia.TryGetValue(l.Familia, out var validos))
                {
                    validos = LerItensValidos(wb, l.Familia);
                    validosPorFamilia[l.Familia] = validos;
                }
                if (validos.Count > 0)
                {
                    if (validos.TryGetValue(Normalizar(l.Item), out string exato))
                        itemFinal = exato;
                    else
                    {
                        naoEncontrados++;
                        ed.WriteMessage($"\n[SMEC][AVISO] Item não encontrado em '{l.Familia}' "
                            + $"(linha {linha} ficará laranja): {l.Item}");
                    }
                }

                form.Cells[linha, COL_DOCREF].Value = l.DocRef;
                form.Cells[linha, COL_FAMILIA].Value = l.Familia;
                form.Cells[linha, COL_ITEM].Value = itemFinal;
                form.Cells[linha, COL_QTD].Value = Math.Round(l.Quantidade, 3);
                linha++;
            }

            if (naoEncontrados > 0)
                ed.WriteMessage($"\n[SMEC] {naoEncontrados} item(ns) sem correspondência exata (ver avisos acima).");

            pkg.Save();
        }

        /// <summary>
        /// Lê a coluna da família indicada na TABELAS_AUXILIARES e devolve um dicionário
        /// (item normalizado -> grafia exata da célula) p/ casar os itens que vamos escrever.
        /// </summary>
        private static Dictionary<string, string> LerItensValidos(ExcelWorkbook wb, string familia)
        {
            var dic = new Dictionary<string, string>(StringComparer.Ordinal);
            var tab = wb.Worksheets.FirstOrDefault(
                w => w.Name.IndexOf("TABELAS_AUX", StringComparison.OrdinalIgnoreCase) >= 0);
            if (tab?.Dimension == null) return dic;

            int maxCol = tab.Dimension.End.Column;
            int maxRow = tab.Dimension.End.Row;

            // Acha a coluna cujo cabeçalho (linha 1) == família
            int colFam = -1;
            for (int c = 1; c <= maxCol; c++)
            {
                string h = tab.Cells[1, c].Text?.Trim();
                if (string.Equals(h, familia, StringComparison.OrdinalIgnoreCase)) { colFam = c; break; }
            }
            if (colFam < 0) return dic;

            for (int r = 2; r <= maxRow; r++)
            {
                string v = tab.Cells[r, colFam].Text;
                if (string.IsNullOrWhiteSpace(v)) continue;
                string norm = Normalizar(v);
                if (!dic.ContainsKey(norm)) dic[norm] = v; // grafia exata (com espaços/acentos)
            }
            return dic;
        }

        private static string Normalizar(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return Regex.Replace(s.Trim(), @"\s+", " ").ToUpperInvariant();
        }

        // ----------------------------------------------------------------------
        // Template local + prompt
        // ----------------------------------------------------------------------

        private static bool PerguntarDemolicao(Editor ed)
        {
            var pko = new PromptKeywordOptions("\nHaverá demolição/recomposição de piso?")
            {
                AllowNone = true
            };
            pko.Keywords.Add("Sim");
            pko.Keywords.Add("Nao");
            pko.Keywords.Default = "Nao";
            var r = ed.GetKeywords(pko);
            return r.Status == PromptStatus.OK
                && r.StringResult.Equals("Sim", StringComparison.OrdinalIgnoreCase);
        }

        private string GarantirFormularioLocal(string dwgDir, string dwgNome, Editor ed)
        {
            string destino = Path.Combine(dwgDir, $"{dwgNome}_FORMULARIO_SMEC.xlsx");

            // Sempre regenera a partir do template (formulário limpo a cada execução)
            string origem = File.Exists(TEMPLATE_DRENAGEM_ORIGEM) ? TEMPLATE_DRENAGEM_ORIGEM : null;
            if (origem == null)
            {
                var pfo = new PromptOpenFileOptions("\nSelecione 'Drenagem_1 2.xlsx' (FORMULÁRIO)")
                {
                    Filter = "Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|Todos (*.*)|*.*"
                };
                var fr = ed.GetFileNameForOpen(pfo);
                if (fr.Status != PromptStatus.OK) return null;
                origem = fr.StringResult;
            }

            try
            {
                if (File.Exists(destino)) File.Delete(destino);
                File.Copy(origem, destino);
                return destino;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[SMEC] Falha copiando template: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>Uma linha do FORMULÁRIO: Família + Item + Quantidade + Documento de Referência.</summary>
    internal class SmecLinha
    {
        public string Familia;
        public string Item;
        public double Quantidade;
        public string DocRef;

        public SmecLinha(string familia, string item, double quantidade, string docRef)
        {
            Familia = familia;
            Item = item;
            Quantidade = quantidade;
            DocRef = docRef;
        }
    }
}
