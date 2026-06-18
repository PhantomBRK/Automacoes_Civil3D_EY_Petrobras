using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutomacoesCivil3D;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using OfficeOpenXml;

namespace RotinasPetrobras.Quantitativos
{
    /// <summary>
    /// SOL_QUANT_CAIXAS: memória de cálculo das caixas. Preenche a aba CAIXAS do template
    /// (2 conjuntos de 3 blocos: escavação PLUVIAL/CONTAMINADA/OLEOSA + material idem),
    /// com valores estáticos do construtor (batem com o SMEC).
    /// </summary>
    public partial class SolQuantTubos
    {
        private const int CX_COL_FIM = 25; // até Y

        [CommandMethod("SOL_QUANT_CAIXAS")]
        public void ExecutarCaixas()
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
                    ed.WriteMessage("\n[SOL_QUANT_CAIXAS] Salve o DWG antes de rodar.");
                    return;
                }
                string dwgNome = Path.GetFileNameWithoutExtension(dwgPath);
                string dwgDir = Path.GetDirectoryName(dwgPath) ?? Environment.CurrentDirectory;

                string template = GarantirTemplateLocal(dwgDir, ed);
                if (template == null) return;

                // Coleta caixas
                var caixas = new List<CaixaQuantData>();
                using (doc.LockDocument())
                using (var t = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)t.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)t.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        string fam = LerString(id, "Family");
                        if (string.IsNullOrEmpty(fam)
                            || fam.IndexOf("CAIXA", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var c = LerCaixa(id, dwgNome);
                        if (c != null && c.VolCA > 1e-6) caixas.Add(c);
                    }
                    t.Commit();
                }

                if (caixas.Count == 0)
                {
                    ed.WriteMessage("\n[SOL_QUANT_CAIXAS] Nenhuma caixa válida encontrada.");
                    return;
                }
                ed.WriteMessage($"\n[SOL_QUANT_CAIXAS] {caixas.Count} caixas.");

                string outPath = Path.Combine(dwgDir, $"{dwgNome}_QUANT_CAIXAS.xlsx");
                GerarXlsxCaixas(template, outPath, caixas, ed);
                ed.WriteMessage($"\n[SOL_QUANT_CAIXAS] OK -> {outPath}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[SOL_QUANT_CAIXAS] ERRO: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private enum SistemaRede { Pluvial, Contaminada, Oleosa }

        private static SistemaRede ClassificarSistema(string nomeRede)
        {
            string n = (nomeRede ?? string.Empty).ToUpperInvariant();
            if (n.Contains("OLEOS")) return SistemaRede.Oleosa;
            if (n.Contains("CONTAM")) return SistemaRede.Contaminada;
            return SistemaRede.Pluvial; // default
        }

        private class BlocoCaixa
        {
            public int HeaderRow;
            public int TotalRow;
            public int DataStart;
            public bool Material;       // false = escavação
            public SistemaRede Sistema;
        }

        private static void GerarXlsxCaixas(
            string template, string outPath, List<CaixaQuantData> caixas, Editor ed)
        {
            ExcelPackage.License.SetNonCommercialPersonal("Gleison Bruno da Costa");
            if (File.Exists(outPath)) File.Delete(outPath);
            File.Copy(template, outPath);

            using var pkg = new ExcelPackage(new FileInfo(outPath));
            var wb = pkg.Workbook;
            LimparReferenciasExternas(wb);
            PreencherCaixasNoWb(wb, caixas, ed);
            pkg.Save();
        }

        /// <summary>Preenche a aba CAIXAS num workbook JÁ ABERTO (não salva).</summary>
        private static void PreencherCaixasNoWb(ExcelWorkbook wb, List<CaixaQuantData> caixas, Editor ed)
        {
            var sh = wb.Worksheets.FirstOrDefault(
                w => w.Name.Equals("CAIXAS", StringComparison.OrdinalIgnoreCase));
            if (sh == null)
                throw new InvalidOperationException("Aba 'CAIXAS' não encontrada no template.");

            // 1) Detecta os 6 blocos (cabeçalhos "DRENAGEM ... - CAIXAS")
            var blocos = DetectarBlocosCaixa(sh);
            if (blocos.Count == 0)
                throw new InvalidOperationException("Não encontrei os blocos da aba CAIXAS.");

            // 2) Agrupa caixas por sistema
            var porSistema = caixas
                .GroupBy(c => ClassificarSistema(c.NomeRede))
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Nome, StringComparer.OrdinalIgnoreCase).ToList());

            // 3) Processa BAIXO->CIMA (insere sem invalidar posições dos blocos acima)
            foreach (var b in blocos.OrderByDescending(x => x.HeaderRow))
            {
                if (!porSistema.TryGetValue(b.Sistema, out var lista) || lista.Count == 0)
                {
                    ed.WriteMessage($"\n  {(b.Material ? "MATERIAL" : "ESCAV")} {b.Sistema}: sem caixas.");
                    continue;
                }

                int totalRow = b.TotalRow;
                var colunasSoma = DetectarColunasSoma(sh, totalRow);

                int disponiveis = totalRow - b.DataStart;
                int desejado = lista.Count + 1; // N dados + 1 linha EM BRANCO antes do Total
                int diff = desejado - disponiveis;
                if (diff > 0) sh.InsertRow(totalRow, diff, totalRow - 1);
                else if (diff < 0) sh.DeleteRow(b.DataStart, -diff);

                int novoTotal = b.DataStart + desejado;
                int linhaBranca = novoTotal - 1; // entra no SUM

                // Garante linha em branco realmente vazia (sem fórmulas herdadas)
                var branca = sh.Cells[linhaBranca, 2, linhaBranca, CX_COL_FIM];
                branca.Formula = "";
                branca.Value = null;

                // Reescreve somatórios da linha de Total (inclui a linha em branco)
                foreach (int col in colunasSoma)
                {
                    if (col > CX_COL_FIM + 5) continue;
                    string rng = sh.Cells[b.DataStart, col, linhaBranca, col].Address;
                    sh.Cells[novoTotal, col].Formula = $"SUM({rng})";
                }

                int linha = b.DataStart;
                foreach (var c in lista)
                {
                    if (b.Material) PreencherCaixaMaterial(sh, linha, c);
                    else PreencherCaixaEscav(sh, linha, c);
                    linha++;
                }

                ed.WriteMessage($"\n  {(b.Material ? "MATERIAL" : "ESCAV")} {b.Sistema}: {lista.Count} caixas.");
            }

            ed.WriteMessage("\n[SOL_QUANT_CAIXAS] Confira a linha 'TOTAL GERAL' (pode precisar reajustar manualmente).");
        }

        private static List<BlocoCaixa> DetectarBlocosCaixa(ExcelWorksheet sh)
        {
            var blocos = new List<BlocoCaixa>();
            int maxRow = sh.Dimension?.End.Row ?? 0;

            var headers = new List<int>();
            for (int r = 1; r <= maxRow; r++)
            {
                string b = sh.Cells[r, 2].Text ?? string.Empty;
                if (b.IndexOf("DRENAGEM", StringComparison.OrdinalIgnoreCase) >= 0
                    && b.IndexOf("CAIXAS", StringComparison.OrdinalIgnoreCase) >= 0)
                    headers.Add(r);
            }
            headers.Sort();

            for (int i = 0; i < headers.Count; i++)
            {
                int h = headers[i];
                string txt = (sh.Cells[h, 2].Text ?? string.Empty).ToUpperInvariant();
                bool material = i >= headers.Count / 2; // 1a metade = escavação, 2a = material

                // Total = primeira "Total" em col C abaixo do header
                int total = -1;
                for (int r = h + 1; r <= maxRow; r++)
                {
                    string c = sh.Cells[r, 3].Text ?? string.Empty;
                    if (c.TrimStart().StartsWith("Total", StringComparison.OrdinalIgnoreCase)) { total = r; break; }
                }
                if (total < 0) continue;

                blocos.Add(new BlocoCaixa
                {
                    HeaderRow = h,
                    TotalRow = total,
                    DataStart = h + (material ? 3 : 4),
                    Material = material,
                    Sistema = txt.Contains("OLEOS") ? SistemaRede.Oleosa
                            : txt.Contains("CONTAM") ? SistemaRede.Contaminada
                            : SistemaRede.Pluvial
                });
            }
            return blocos;
        }

        // Bloco ESCAVAÇÃO: B..Y
        private static void PreencherCaixaEscav(ExcelWorksheet sh, int linha, CaixaQuantData c)
        {
            sh.Cells[linha, 2].Value  = c.SubType;
            sh.Cells[linha, 3].Value  = c.Nome;
            sh.Cells[linha, 4].Value  = c.DocRef;
            sh.Cells[linha, 5].Value  = c.CotaTopo;
            sh.Cells[linha, 6].Value  = c.CotaFundo;
            sh.Cells[linha, 7].Value  = c.Parede;
            sh.Cells[linha, 8].Value  = c.LajeFundo;
            sh.Cells[linha, 9].Value  = c.LajeTopo;
            sh.Cells[linha, 10].Value = c.Di1;
            sh.Cells[linha, 11].Value = c.Di2;
            sh.Cells[linha, 12].Value = c.De1;
            sh.Cells[linha, 13].Value = c.De2;
            sh.Cells[linha, 14].Value = c.AlturaInterna;
            sh.Cells[linha, 15].Value = c.EspMagro;
            sh.Cells[linha, 16].Value = c.LargVala1;
            sh.Cells[linha, 17].Value = c.LargVala2;
            sh.Cells[linha, 18].Value = c.AlturaEscav;
            sh.Cells[linha, 19].Value = c.VolEscav;
            sh.Cells[linha, 20].Value = c.AreaApiloamento;
            sh.Cells[linha, 21].Value = c.VolCM;
            sh.Cells[linha, 22].Value = c.VolReaterro;
            sh.Cells[linha, 23].Value = c.VolBotaFora;
            sh.Cells[linha, 24].Value = c.MassaEspAdotada;
            sh.Cells[linha, 25].Value = c.MassaBotaFora;
        }

        // Bloco MATERIAL: B..S
        private static void PreencherCaixaMaterial(ExcelWorksheet sh, int linha, CaixaQuantData c)
        {
            sh.Cells[linha, 2].Value  = c.SubType;
            sh.Cells[linha, 3].Value  = c.Nome;
            sh.Cells[linha, 4].Value  = c.DocRef;
            sh.Cells[linha, 5].Value  = c.Parede;
            sh.Cells[linha, 6].Value  = c.LajeFundo;
            sh.Cells[linha, 7].Value  = c.LajeTopo;
            sh.Cells[linha, 8].Value  = c.Di1;
            sh.Cells[linha, 9].Value  = c.Di2;
            sh.Cells[linha, 10].Value = c.De1;
            sh.Cells[linha, 11].Value = c.De2;
            sh.Cells[linha, 12].Value = c.AlturaInterna;
            sh.Cells[linha, 13].Value = c.VolLajes;     // M Vol Lajes (lajes + piso)
            sh.Cells[linha, 14].Value = c.VolParedes;   // N Vol Paredes (corpo + septo)
            sh.Cells[linha, 15].Value = c.VolCA;        // O Vol Total Concreto (= M+N)
            sh.Cells[linha, 16].Value = c.TaxaArmadura; // P Taxa aço (construtor)
            sh.Cells[linha, 17].Value = c.QuantAco;     // Q Massa armadura
            sh.Cells[linha, 18].Value = c.AreaFormas;   // R Fôrmas
            sh.Cells[linha, 19].Value = c.VolCA > 1e-6 ? c.AreaFormas / c.VolCA : 0.0; // S Taxa Fôrmas
        }
    }
}
