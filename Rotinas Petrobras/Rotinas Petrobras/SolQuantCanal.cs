using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutomacoesCivil3D;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using OfficeOpenXml;
using SOLIDOS;

namespace RotinasPetrobras.Quantitativos
{
    /// <summary>
    /// SOL_QUANT_CANAL: memória de cálculo das CANALETAS (dispositivo linear).
    /// Lê os valores já calculados no construtor (Opção A) e preenche as abas
    /// CANALET_Pluv / CANALET_Cont / CANALET_Oleo (1 por sistema), começando na linha 6.
    /// </summary>
    public partial class SolQuantTubos
    {
        private static readonly string[] SHEETS_CANAL_TEMPLATE = { "CANALET_Pluv", "CANALET_Cont", "CANALET_Oleo" };
        private const string SHEET_CANAL_DEFAULT = "CANALET_Oleo";
        private const int CANAL_LINHA_INICIO = 6;   // 1-5 = cabeçalho
        private const int CANAL_COL_FIM = 34;        // AH

        [CommandMethod("SOL_QUANT_CANAL")]
        public void ExecutarCanal()
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
                    ed.WriteMessage("\n[SOL_QUANT_CANAL] Salve o DWG antes de rodar.");
                    return;
                }
                string dwgNome = Path.GetFileNameWithoutExtension(dwgPath);
                string dwgDir = Path.GetDirectoryName(dwgPath) ?? Environment.CurrentDirectory;

                string template = GarantirTemplateLocal(dwgDir, ed);
                if (template == null) return;
                ed.WriteMessage($"\n[SOL_QUANT_CANAL] Template: {template}");

                ed.WriteMessage("\n[SOL_QUANT_CANAL] Lendo canaletas...");
                var porSistema = new Dictionary<string, List<CanalQuantData>>(StringComparer.OrdinalIgnoreCase);
                int total = 0, fantasmas = 0;
                var zeros = new HashSet<string>();

                using (doc.LockDocument())
                using (var t = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)t.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)t.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId id in ms)
                    {
                        string fam = LerString(id, "Family");
                        if (string.IsNullOrEmpty(fam)
                            || fam.IndexOf("CANALET", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        var c = LerCanal(id, dwgNome, zeros);
                        if (c == null) continue;
                        if (EhCanaletaIgnorar(c)) { fantasmas++; continue; }
                        total++;

                        string sheet = EscolherSheetCanal(c.NomeRede);
                        if (!porSistema.TryGetValue(sheet, out var lst))
                        {
                            lst = new List<CanalQuantData>();
                            porSistema[sheet] = lst;
                        }
                        lst.Add(c);
                    }
                    t.Commit();
                }

                if (total == 0)
                {
                    ed.WriteMessage("\n[SOL_QUANT_CANAL] Nenhuma canaleta encontrada.");
                    return;
                }
                ed.WriteMessage($"\n[SOL_QUANT_CANAL] {total} canaletas"
                    + (fantasmas > 0 ? $", {fantasmas} fantasma(s) descartada(s)." : "."));
                if (zeros.Count > 0)
                    ed.WriteMessage($"\n[SOL_QUANT_CANAL][AVISO] propriedades que vieram 0/nulas (confirmar nome): {string.Join(", ", zeros)}");

                string outPath = Path.Combine(dwgDir, $"{dwgNome}_QUANT_CANAL.xlsx");
                GerarXlsxCanal(template, outPath, porSistema, ed);
                ed.WriteMessage($"\n[SOL_QUANT_CANAL] OK -> {outPath}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[SOL_QUANT_CANAL] ERRO: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Canaleta a IGNORAR (não é trecho real): sem geometria (Comprimento≈0) OU
        /// totalmente desconectada (Trecho "? a ?" = InPart e OutPart ambos nulos).
        /// Essas são tipicamente peças de conexão/canto da canaleta — saíam como linhas
        /// em branco (elevações 0, ProfVala 0) na memória.
        /// </summary>
        private static bool EhCanaletaIgnorar(CanalQuantData c)
            => c == null || c.Comprimento <= 0.001 || c.Trecho == "? a ?";

        private static string EscolherSheetCanal(string nomeRede)
        {
            if (string.IsNullOrWhiteSpace(nomeRede)) return SHEET_CANAL_DEFAULT;
            string n = nomeRede.ToUpperInvariant();
            if (n.Contains("OLEOS"))  return "CANALET_Oleo";
            if (n.Contains("CONTAM")) return "CANALET_Cont";
            if (n.Contains("PLUV"))   return "CANALET_Pluv";
            return SHEET_CANAL_DEFAULT;
        }

        /// <summary>Lê double tentando vários nomes; registra em `zeros` se nenhum resolveu.</summary>
        private static double LerDoubleAlt(ObjectId id, HashSet<string> zeros, params string[] nomes)
        {
            foreach (var n in nomes)
            {
                double v = LerDouble(id, n);
                if (Math.Abs(v) > 1e-9) return v;
            }
            if (zeros != null) zeros.Add(nomes[0]);
            return 0.0;
        }

        private static CanalQuantData LerCanal(ObjectId id, string docRef, HashSet<string> zeros)
        {
            try
            {
                var c = new CanalQuantData
                {
                    DocReferencia = docRef,
                    // Inputs
                    ElevTerrenoMont = LerDoubleAlt(id, zeros, "StartSurfElevation", "StartTopElevation"),
                    FitMont         = LerDoubleAlt(id, zeros, "StartInvertElevation"),
                    ElevTerrenoJus  = LerDoubleAlt(id, zeros, "EndSurfElevation1", "EndSurfElevation", "EndTopElevation"),
                    FitJus          = LerDoubleAlt(id, zeros, "EndInvertElevation"),
                    Comprimento     = LerDoubleAlt(id, zeros, "Comprimento"),
                    LarguraInt      = LerDoubleAlt(id, zeros, "Largura"),
                    Parede          = LerDoubleAlt(id, zeros, "Parede"),
                    EspConcMagro    = LerDoubleAlt(id, zeros, "EspConcMagro", "EspessuraConcretoMagro"),
                    // Calculados no construtor (QTO SMEC) — nomes lidos do painel
                    LargExt         = LerDoubleAlt(id, zeros, "LargExt", "LarguraExterna"),
                    LargVala        = LerDoubleAlt(id, zeros, "LargVala"),
                    ProfValaMont    = LerDoubleAlt(id, zeros, "ProfValaM"),
                    ProfValaJus     = LerDoubleAlt(id, zeros, "ProfValaJ"),
                    SecValaMont     = LerDoubleAlt(id, zeros, "SecValaM"),
                    SecValaJus      = LerDoubleAlt(id, zeros, "SecValaJ"),
                    AltMedCanal     = LerDoubleAlt(id, zeros, "AltMedCanal"),
                    AreaApiloam     = LerDoubleAlt(id, zeros, "AreaApiloam"),
                    VolEscav        = LerDoubleAlt(id, zeros, "VolEscav"),
                    VolConcMagro    = LerDoubleAlt(id, zeros, "VolConcMagro", "VolMagro"),
                    VolCanal        = LerDoubleAlt(id, zeros, "VolCanal"),
                    VolReaterro     = LerDoubleAlt(id, zeros, "VolReaterro"),
                    VolBotaFora     = LerDoubleAlt(id, zeros, "VolBotaFora"),
                    MassaEsp        = LerDoubleAlt(id, zeros, "MassaEspBF", "MassaEspAdotada"),
                    MassaBotaFora   = LerDoubleAlt(id, zeros, "MassaBotaFora"),
                    VolConcCanal    = LerDoubleAlt(id, zeros, "VolConcCanal"),
                    TaxaAco         = LerDoubleAlt(id, zeros, "TaxaAco"),
                    MassaAco        = LerDoubleAlt(id, zeros, "MassaAco"),
                    AreaForma       = LerDoubleAlt(id, zeros, "AreaForma"),
                    TaxaForma       = LerDoubleAlt(id, zeros, "TaxaForma"),
                };

                c.Trecho = $"{LerNomeReferenciado(id, "InPart")} a {LerNomeReferenciado(id, "OutPart")}";
                c.NomeRede = LerNomeReferenciado(id, "RootId");
                if (string.IsNullOrWhiteSpace(c.NomeRede)) c.NomeRede = "SEM REDE";

                // Tipo de fechamento: a partir do Family ("PETROBRAS - CANALETAS - GRELHA METALICA")
                string fam = LerString(id, "Family");
                string tipo = fam;
                int ix = fam.LastIndexOf(" - ", StringComparison.Ordinal);
                if (ix >= 0) tipo = fam.Substring(ix + 3).Trim();
                if (string.IsNullOrWhiteSpace(tipo)) tipo = LerString(id, "SubType");
                c.TipoFechamento = tipo;

                // Tampa: derivados simples (não saem do construtor) — espelham a planilha
                c.LarguraTampa = c.LarguraInt + 0.05;
                c.AreaTampa = c.Comprimento * c.LarguraTampa;

                return c;
            }
            catch
            {
                return null;
            }
        }

        // ----------------------------------------------------------------------
        // Geração XLSX (1 aba por sistema, layout idêntico aos tubos)
        // ----------------------------------------------------------------------

        private static void GerarXlsxCanal(
            string template, string outPath,
            Dictionary<string, List<CanalQuantData>> porSistema, Editor ed)
        {
            ExcelPackage.License.SetNonCommercialPersonal("Gleison Bruno da Costa");
            if (File.Exists(outPath)) File.Delete(outPath);
            File.Copy(template, outPath);

            using var pkg = new ExcelPackage(new FileInfo(outPath));
            var wb = pkg.Workbook;
            LimparReferenciasExternas(wb);
            PreencherCanalNoWb(wb, porSistema, ed);
            pkg.Save();
        }

        /// <summary>Preenche CANALET_Pluv/Cont/Oleo num workbook JÁ ABERTO (não salva).</summary>
        private static void PreencherCanalNoWb(
            ExcelWorkbook wb, Dictionary<string, List<CanalQuantData>> porSistema, Editor ed)
        {
            foreach (var nomeSheet in SHEETS_CANAL_TEMPLATE)
            {
                var sh = wb.Worksheets[nomeSheet];
                if (sh == null)
                    throw new InvalidOperationException($"Sheet '{nomeSheet}' não encontrada.");

                if (!porSistema.TryGetValue(nomeSheet, out var lista) || lista.Count == 0)
                {
                    ed.WriteMessage($"\n  {nomeSheet}: sem canaletas (mantida como template).");
                    continue;
                }

                var ordenados = lista
                    .OrderBy(x => x.NomeRede, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Trecho, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int linhaTotalModelo = AcharLinhaTotais(sh);
                if (linhaTotalModelo > CANAL_LINHA_INICIO)
                {
                    var colunasSoma = DetectarColunasSoma(sh, linhaTotalModelo);
                    int disponiveis = linhaTotalModelo - CANAL_LINHA_INICIO;
                    int desejado = ordenados.Count + 1; // dados + 1 branca
                    int diff = desejado - disponiveis;
                    if (diff > 0) sh.InsertRow(linhaTotalModelo, diff, linhaTotalModelo - 1);
                    else if (diff < 0) sh.DeleteRow(CANAL_LINHA_INICIO, -diff);

                    int linhaTotal = CANAL_LINHA_INICIO + desejado;
                    int linhaBranca = linhaTotal - 1;

                    var branca = sh.Cells[linhaBranca, 2, linhaBranca, CANAL_COL_FIM];
                    branca.Formula = "";
                    branca.Value = null;

                    foreach (int col in colunasSoma)
                    {
                        if (col > CANAL_COL_FIM + 5) continue;
                        string rng = sh.Cells[CANAL_LINHA_INICIO, col, linhaBranca, col].Address;
                        sh.Cells[linhaTotal, col].Formula = $"SUM({rng})";
                    }
                }

                int linha = CANAL_LINHA_INICIO;
                foreach (var c in ordenados)
                {
                    PreencherLinhaCanal(sh, linha, c);
                    linha++;
                }
                ed.WriteMessage($"\n  {nomeSheet}: {ordenados.Count} canaletas.");
            }
        }

        private static void PreencherLinhaCanal(ExcelWorksheet sh, int r, CanalQuantData c)
        {
            sh.Cells[r, 2].Value  = c.Trecho;          // B
            sh.Cells[r, 3].Value  = c.DocReferencia;   // C
            sh.Cells[r, 4].Value  = c.ElevTerrenoMont; // D
            sh.Cells[r, 5].Value  = c.FitMont;         // E
            sh.Cells[r, 6].Value  = c.ElevTerrenoJus;  // F
            sh.Cells[r, 7].Value  = c.FitJus;          // G
            sh.Cells[r, 8].Value  = c.Comprimento;     // H Extensão
            sh.Cells[r, 9].Value  = c.TipoFechamento;  // I
            sh.Cells[r, 10].Value = c.LarguraInt;      // J (b)
            sh.Cells[r, 11].Value = c.Parede;          // K (e)
            sh.Cells[r, 12].Value = c.LarguraTampa;    // L
            sh.Cells[r, 13].Value = c.AreaTampa;       // M
            sh.Cells[r, 14].Value = c.LargExt;         // N (B)
            sh.Cells[r, 15].Value = c.LargVala;        // O (L)
            sh.Cells[r, 16].Value = c.EspConcMagro;    // P
            sh.Cells[r, 17].Value = c.ProfValaMont;    // Q (H)
            sh.Cells[r, 18].Value = c.ProfValaJus;     // R
            sh.Cells[r, 19].Value = c.SecValaMont;     // S (S1)
            sh.Cells[r, 20].Value = c.SecValaJus;      // T (S2)
            sh.Cells[r, 21].Value = c.AltMedCanal;     // U (Hmed)
            sh.Cells[r, 22].Value = c.AreaApiloam;     // V
            sh.Cells[r, 23].Value = c.VolEscav;        // W (VE)
            sh.Cells[r, 24].Value = c.VolConcMagro;    // X (Vcm)
            sh.Cells[r, 25].Value = c.VolCanal;        // Y (Vc)
            sh.Cells[r, 26].Value = c.VolReaterro;     // Z (VR)
            sh.Cells[r, 27].Value = c.VolBotaFora;     // AA (Vbf)
            sh.Cells[r, 28].Value = c.MassaEsp;        // AB (M_esp)
            sh.Cells[r, 29].Value = c.MassaBotaFora;   // AC (Mbf)
            sh.Cells[r, 30].Value = c.VolConcCanal;    // AD (V_cc)
            sh.Cells[r, 31].Value = c.TaxaAco;         // AE
            sh.Cells[r, 32].Value = c.MassaAco;        // AF (M_aço)
            sh.Cells[r, 33].Value = c.AreaForma;       // AG (A_form)
            sh.Cells[r, 34].Value = c.TaxaForma;       // AH (Tx_form)
        }
    }

    internal class CanalQuantData
    {
        public string Trecho;
        public string DocReferencia;
        public string NomeRede;
        public string TipoFechamento;
        // Inputs
        public double ElevTerrenoMont, FitMont, ElevTerrenoJus, FitJus;
        public double Comprimento, LarguraInt, Parede, EspConcMagro;
        public double LarguraTampa, AreaTampa;
        // Calculados
        public double LargExt, LargVala, ProfValaMont, ProfValaJus, SecValaMont, SecValaJus;
        public double AltMedCanal, AreaApiloam, VolEscav, VolConcMagro, VolCanal, VolReaterro;
        public double VolBotaFora, MassaEsp, MassaBotaFora, VolConcCanal, TaxaAco, MassaAco;
        public double AreaForma, TaxaForma;
    }
}
