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
    /// SOL_QUANT_CANAL_SMEC: aglutina os quantitativos de TODAS as canaletas (global)
    /// e classifica em itens SMEC da família CANAIS E CANALETAS, preenchendo o
    /// FORMULÁRIO DE SOLICITAÇÕES (col G Família, H Item, J Quantidade).
    /// </summary>
    public partial class SolQuantTubos
    {
        private const string FAMILIA_CANAL = "CANAIS E CANALETAS";
        // Itens da família CANAIS E CANALETAS (escavação/apiloam/reaterro/bota-fora reusam
        // as constantes de TUBULAÇÕES, que têm a mesma grafia).
        private const string IT_CN_CONC_EST   = "CONCRETO ESTRUTURAL - FCK= 30 MPA";
        private const string IT_CN_CONC_MAGRO = "CONCRETO MAGRO - FCK = 10MPA";
        private const string IT_CN_ACO        = "ARMADURA DE AÇO CA-50";
        private const string IT_CN_FORMAS     = "FÔRMAS DE MADEIRA COMPENSADA ATÉ 3 UTILIZAÇÕES";
        private const string IT_CN_GRELHA_FMT = "GRELHA EM FERRO FUNDIDO LARGURA {0} cm"; // unidade m
        private const string IT_CN_TAMPA_CONC = "TAMPA EM CONCRETO";                       // unidade m²

        private static readonly int[] GRELHA_LARGURAS_CM = { 20, 25, 30, 40, 50, 60, 80, 100 };

        /// <summary>Largura (m) da canaleta -> largura de grelha disponível mais próxima (cm).</summary>
        private static int SnapGrelhaCm(double larguraM)
        {
            double cm = larguraM * 100.0;
            int best = GRELHA_LARGURAS_CM[0];
            double bd = double.MaxValue;
            foreach (var o in GRELHA_LARGURAS_CM)
            {
                double d = Math.Abs(o - cm);
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        [CommandMethod("SOL_QUANT_CANAL_SMEC")]
        public void ExecutarCanalSmec()
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
                    ed.WriteMessage("\n[CANAL-SMEC] Salve o DWG antes de rodar.");
                    return;
                }
                string dwgNome = Path.GetFileNameWithoutExtension(dwgPath);
                string dwgDir = Path.GetDirectoryName(dwgPath) ?? Environment.CurrentDirectory;

                string destino = GarantirFormularioLocal(dwgDir, dwgNome + "_CANAL", ed);
                if (destino == null) return;

                var canais = new List<CanalQuantData>();
                int fantasmas = 0;
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
                        canais.Add(c);
                    }
                    t.Commit();
                }

                if (canais.Count == 0)
                {
                    ed.WriteMessage("\n[CANAL-SMEC] Nenhuma canaleta encontrada.");
                    return;
                }
                ed.WriteMessage($"\n[CANAL-SMEC] {canais.Count} canaletas"
                    + (fantasmas > 0 ? $", {fantasmas} fantasma(s)." : "."));

                var linhas = AgregarSmecCanal(canais, dwgNome);
                EscreverFormularioFamilia(destino, linhas, FAMILIA_CANAL, ed);
                ed.WriteMessage($"\n[CANAL-SMEC] OK -> {destino}");
                ed.WriteMessage($"\n[CANAL-SMEC] {linhas.Count} linha(s) no FORMULÁRIO.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[CANAL-SMEC] ERRO: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static List<SmecLinha> AgregarSmecCanal(List<CanalQuantData> canais, string docRef)
        {
            var linhas = new List<SmecLinha>();

            // Insumos (somatórios globais)
            AddSe(linhas, FAMILIA_CANAL, IT_CN_CONC_EST,   canais.Sum(x => x.VolConcCanal), docRef);
            AddSe(linhas, FAMILIA_CANAL, IT_CN_CONC_MAGRO, canais.Sum(x => x.VolConcMagro), docRef);
            AddSe(linhas, FAMILIA_CANAL, IT_CN_ACO,        canais.Sum(x => x.MassaAco),     docRef);
            AddSe(linhas, FAMILIA_CANAL, IT_CN_FORMAS,     canais.Sum(x => x.AreaForma),    docRef);
            AddSe(linhas, FAMILIA_CANAL, IT_APILOAMENTO,   canais.Sum(x => x.AreaApiloam),  docRef);
            AddSe(linhas, FAMILIA_CANAL, IT_REATERRO,      canais.Sum(x => x.VolReaterro),  docRef);
            AddSe(linhas, FAMILIA_CANAL, IT_BOTAFORA,      canais.Sum(x => x.VolBotaFora),  docRef);

            // Escavação em 3 faixas por profundidade média da vala
            double e1 = 0, e2 = 0, e3 = 0;
            foreach (var x in canais)
            {
                double prof = (x.ProfValaMont + x.ProfValaJus) / 2.0;
                if (prof <= 1.5) e1 += x.VolEscav;
                else if (prof <= 1.75) e2 += x.VolEscav;
                else e3 += x.VolEscav;
            }
            AddSe(linhas, FAMILIA_CANAL, IT_ESC_ATE15,  e1, docRef);
            AddSe(linhas, FAMILIA_CANAL, IT_ESC_15_175, e2, docRef);
            AddSe(linhas, FAMILIA_CANAL, IT_ESC_ESCOR,  e3, docRef);

            // Fechamento:
            //  - GRELHA  -> GRELHA EM FERRO FUNDIDO LARGURA {N}cm (unidade m -> qtd = comprimento)
            //  - TAMPA   -> TAMPA EM CONCRETO (unidade m² -> qtd = área da tampa)
            //  - ÁREA CONTIDA / PUMP OUT -> sem fechamento
            var comGrelha = canais.Where(x =>
                (x.TipoFechamento ?? "").IndexOf("GRELHA", StringComparison.OrdinalIgnoreCase) >= 0);
            foreach (var g in comGrelha.GroupBy(x => SnapGrelhaCm(x.LarguraInt)).OrderBy(g => g.Key))
                AddSe(linhas, FAMILIA_CANAL, string.Format(IT_CN_GRELHA_FMT, g.Key),
                      g.Sum(x => x.Comprimento), docRef);

            double areaTampaConc = canais
                .Where(x => (x.TipoFechamento ?? "").IndexOf("TAMPA", StringComparison.OrdinalIgnoreCase) >= 0)
                .Sum(x => x.AreaTampa);
            AddSe(linhas, FAMILIA_CANAL, IT_CN_TAMPA_CONC, areaTampaConc, docRef);

            return linhas;
        }

        /// <summary>
        /// Versão genérica do escritor de FORMULÁRIO: valida os itens contra a família
        /// informada (não fixa em TUBULAÇÕES). Reaproveita LerItensValidos/Normalizar.
        /// </summary>
        private static void EscreverFormularioFamilia(string destino, List<SmecLinha> linhas, string familia, Editor ed)
        {
            ExcelPackage.License.SetNonCommercialPersonal("Gleison Bruno da Costa");
            using var pkg = new ExcelPackage(new FileInfo(destino));
            var wb = pkg.Workbook;

            var form = wb.Worksheets.FirstOrDefault(
                w => w.Name.IndexOf("FORMUL", StringComparison.OrdinalIgnoreCase) >= 0);
            if (form == null)
                throw new InvalidOperationException("Aba 'FORMULÁRIO DE SOLICITAÇÕES' não encontrada.");

            var validos = LerItensValidos(wb, familia);

            int linha = FORM_LINHA_INICIO;
            int naoEnc = 0;
            foreach (var l in linhas)
            {
                string itemFinal = l.Item;
                if (validos.Count > 0 && validos.TryGetValue(Normalizar(l.Item), out string exato))
                    itemFinal = exato;
                else
                {
                    naoEnc++;
                    ed.WriteMessage($"\n[CANAL-SMEC][AVISO] item sem match exato (linha {linha} ficará laranja): {l.Item}");
                }

                form.Cells[linha, COL_DOCREF].Value = l.DocRef;
                form.Cells[linha, COL_FAMILIA].Value = l.Familia;
                form.Cells[linha, COL_ITEM].Value = itemFinal;
                form.Cells[linha, COL_QTD].Value = Math.Round(l.Quantidade, 3);
                linha++;
            }

            if (naoEnc > 0)
                ed.WriteMessage($"\n[CANAL-SMEC] {naoEnc} item(ns) sem correspondência (ver avisos).");

            pkg.Save();
        }
    }
}
