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
    /// SOL_QUANT_GERAL: comando ÚNICO. Numa passada só, coleta tubos, caixas, canaletas
    /// e conexões e gera DOIS arquivos:
    ///   1) &lt;dwg&gt;_QUANTITATIVO.xlsx  — memória de cálculo (todas as abas: TUB_*, CAIXAS, CANALET_*)
    ///   2) &lt;dwg&gt;_FORMULARIO_SMEC.xlsx — FORMULÁRIO DE SOLICITAÇÕES com tudo agregado
    ///      (TUBULAÇÕES, CAIXAS, CANAIS E CANALETAS, Outros).
    /// </summary>
    public partial class SolQuantTubos
    {
        private const string FAMILIA_VALVULAS = "VÁLVULAS";
        private const string IT_VALV_FMT = "VÁLVULA GAVETA EM FERRO FUNDIDO {0}mm, CLASSE 10 (JUNTA ELÁSTICA)";

        /// <summary>Sufixa o rótulo com o DN da peça (lido de Catalogo/Diametro). Sem DN, mantém o rótulo.</summary>
        private static string RotuloComDn(string baseLabel, ObjectId id)
        {
            int dn = ResolverDn(LerString(id, "Catalogo"), LerDouble(id, "Diametro") * 1000.0);
            return dn > 0 ? $"{baseLabel} - DN {dn}" : baseLabel;
        }

        [CommandMethod("SOL_QUANT_GERAL")]
        public void ExecutarGeral()
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
                    ed.WriteMessage("\n[GERAL] Salve o DWG antes de rodar.");
                    return;
                }
                string dwgNome = Path.GetFileNameWithoutExtension(dwgPath);
                string dwgDir = Path.GetDirectoryName(dwgPath) ?? Environment.CurrentDirectory;

                bool incluirDemolicao = PerguntarDemolicao(ed);
                ed.WriteMessage($"\n[GERAL] Demolição/recomposição: {(incluirDemolicao ? "SIM" : "NÃO")}");

                // Templates
                string templateMemoria = GarantirTemplateLocal(dwgDir, ed);
                if (templateMemoria == null) return;

                // --- 1) Coleta única de todos os dispositivos ---
                var tubosPorRede = new Dictionary<string, List<TuboQuantData>>(StringComparer.OrdinalIgnoreCase);
                var tubosFlat = new List<TuboQuantData>();
                var caixas = new List<CaixaQuantData>();
                var canaisFlat = new List<CanalQuantData>();
                var canalPorSistema = new Dictionary<string, List<CanalQuantData>>(StringComparer.OrdinalIgnoreCase);
                var conexoes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var valvulas = new Dictionary<int, int>();  // DN(mm) -> contagem
                int tubFant = 0, canFant = 0, funisDecomp = 0;
                var zeros = new HashSet<string>();

                using (doc.LockDocument())
                using (var t = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)t.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)t.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    // Classe dos sólidos 3D (gerados pelos dispositivos) — nunca são dispositivos.
                    var rxSolid3d = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Solid3d));

                    foreach (ObjectId id in ms)
                    {
                        // Pula sólidos 3D gerados (categoria mais numerosa) sem nem chamar a SolidosAPI.
                        if (id.ObjectClass == rxSolid3d) continue;

                        // OTIMIZAÇÃO: todo dispositivo SOLIDOS tem "Family". Entidades comuns
                        // (linhas, textos, sólidos gerados, blocos não-SOLIDOS) retornam vazio
                        // -> pula imediatamente, evitando ~8 chamadas caras da SolidosAPI por entidade.
                        string fam = LerString(id, "Family");
                        if (string.IsNullOrEmpty(fam)) continue;

                        // Conexões -> Outros
                        if (!string.IsNullOrEmpty(fam)
                            && fam.IndexOf("CONEX", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string sub = LerString(id, "SubType");
                            if (string.IsNullOrWhiteSpace(sub)) sub = LerString(id, "Name");
                            if (string.IsNullOrWhiteSpace(sub)) sub = "CONEXÃO";
                            sub = RotuloComDn(sub, id); // separa por DN
                            conexoes[sub] = conexoes.TryGetValue(sub, out int n) ? n + 1 : 1;
                            continue;
                        }

                        // Caixas
                        if (!string.IsNullOrEmpty(fam)
                            && fam.IndexOf("CAIXA", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var c = LerCaixa(id, dwgNome);
                            if (c != null && c.VolCA > 1e-6) caixas.Add(c);
                            continue;
                        }

                        // Canaletas
                        if (!string.IsNullOrEmpty(fam)
                            && fam.IndexOf("CANALET", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var cn = LerCanal(id, dwgNome, zeros);
                            if (cn == null) continue;
                            if (EhCanaletaIgnorar(cn)) { canFant++; continue; }
                            canaisFlat.Add(cn);
                            string sheet = EscolherSheetCanal(cn.NomeRede);
                            if (!canalPorSistema.TryGetValue(sheet, out var lc))
                            { lc = new List<CanalQuantData>(); canalPorSistema[sheet] = lc; }
                            lc.Add(cn);
                            continue;
                        }

                        // Funil (Family "PETROBRAS - DRENO DE EQUIPAMENTOS"): DECOMPÕE em
                        //  (1) tubo vertical de FF -> soma ao TUBO (TUBULAÇÕES) do mesmo DN;
                        //  (2) a peça do fundo (curva 90° ou tê) -> conta com as conexões, por DN.
                        // Comprimento vertical = Altura - RaioCurva (trecho reto; a curva entra como peça).
                        if (!string.IsNullOrEmpty(fam)
                            && fam.IndexOf("DRENO DE EQUIP", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            int dnF = ResolverDn(LerString(id, "Catalogo"), LerDouble(id, "Diametro") * 1000.0);
                            double alturaF = LerDouble(id, "Altura");
                            double raioF = LerDouble(id, "RaioCurva");
                            double compVert = alturaF - raioF;

                            if (dnF > 0 && compVert > 0.001)
                            {
                                // (1) tubo vertical FF -> tubosFlat sintético (SÓ comprimento;
                                //     não entra na memória nem na escavação — demais campos = 0)
                                tubosFlat.Add(new TuboQuantData
                                {
                                    DnMm = dnF, DiametroMm = dnF, Comprimento = compVert,
                                    Material = "Ferro Fundido", NomeRede = "FUNIL",
                                    Trecho = "FUNIL", DocReferencia = dwgNome
                                });

                                // (2) peça do fundo: TÊ se "FUNIL TE", senão CURVA 90°.
                                string nomeF = (LerString(id, "SubType") + " " + LerString(id, "Name")).ToUpperInvariant();
                                string peca = nomeF.Contains(" TE") ? "TE - 90°" : "CURVA - 90°";
                                string keyF = $"{peca} - DN {dnF}";
                                conexoes[keyF] = conexoes.TryGetValue(keyF, out int nf) ? nf + 1 : 1;
                                funisDecomp++;
                            }
                            continue;
                        }

                        // Válvulas (criadas a partir de tubos; têm VALV no Name/SubType).
                        // Conta por DN; vão pro SMEC como unidade (família VÁLVULAS).
                        string subt = LerString(id, "SubType");
                        string nm = LerString(id, "Name");
                        string sn = (subt + " " + nm).ToUpperInvariant();
                        if (sn.Contains("VALV") || sn.Contains("VÁLV"))
                        {
                            int dnv = ResolverDn(LerString(id, "Catalogo"), LerDouble(id, "Diametro") * 1000.0);
                            valvulas[dnv] = valvulas.TryGetValue(dnv, out int nv) ? nv + 1 : 1;
                            continue;
                        }

                        // Tubos (só família TUBO -> evita GetPropertyType×4 nas demais famílias)
                        if (fam.IndexOf("TUBO", StringComparison.OrdinalIgnoreCase) >= 0
                            && EhTuboPetrobras(id))
                        {
                            var tt = LerTubo(id, dwgNome);
                            if (tt == null) continue;
                            if (EhFantasma(tt)) { tubFant++; continue; }
                            tubosFlat.Add(tt);
                            if (!tubosPorRede.TryGetValue(tt.NomeRede, out var lt))
                            { lt = new List<TuboQuantData>(); tubosPorRede[tt.NomeRede] = lt; }
                            lt.Add(tt);
                        }
                    }
                    t.Commit();
                }

                ed.WriteMessage($"\n[GERAL] {tubosFlat.Count} tubos (inclui {funisDecomp} tubos verticais de funil), "
                    + $"{caixas.Count} caixas, {canaisFlat.Count} canaletas, {conexoes.Values.Sum()} conexões "
                    + $"(inclui {funisDecomp} curvas de funil), {valvulas.Values.Sum()} válvulas.");
                if (tubFant + canFant > 0)
                    ed.WriteMessage($"\n[GERAL] Descartados (fantasma): {tubFant} tubos, {canFant} canaletas.");
                if (zeros.Count > 0)
                    ed.WriteMessage($"\n[GERAL][AVISO] propriedades 0/nulas (canaleta): {string.Join(", ", zeros)}");

                // --- 2) Memória de cálculo (1 arquivo, todas as abas) ---
                string outMemoria = Path.Combine(dwgDir, $"{dwgNome}_QUANTITATIVO.xlsx");
                ExcelPackage.License.SetNonCommercialPersonal("Gleison Bruno da Costa");
                if (File.Exists(outMemoria)) File.Delete(outMemoria);
                File.Copy(templateMemoria, outMemoria);
                using (var pkg = new ExcelPackage(new FileInfo(outMemoria)))
                {
                    var wb = pkg.Workbook;
                    LimparReferenciasExternas(wb);
                    ed.WriteMessage("\n[GERAL] Memória — tubos:");
                    PreencherTubosNoWb(wb, tubosPorRede, incluirDemolicao, ed);
                    ed.WriteMessage("\n[GERAL] Memória — caixas:");
                    if (caixas.Count > 0) PreencherCaixasNoWb(wb, caixas, ed);
                    ed.WriteMessage("\n[GERAL] Memória — canaletas:");
                    PreencherCanalNoWb(wb, canalPorSistema, ed);
                    pkg.Save();
                }
                ed.WriteMessage($"\n[GERAL] Memória -> {outMemoria}");

                // --- 3) FORMULÁRIO SMEC (1 arquivo, tudo agregado) ---
                string outSmec = GarantirFormularioLocal(dwgDir, dwgNome, ed);
                if (outSmec == null) { ed.WriteMessage("\n[GERAL] FORMULÁRIO não gerado (template ausente)."); return; }

                var linhas = new List<SmecLinha>();
                linhas.AddRange(AgregarSmec(tubosFlat, conexoes, incluirDemolicao, dwgNome));
                linhas.AddRange(AgregarSmecCaixas(caixas, incluirDemolicao, dwgNome));
                linhas.AddRange(AgregarSmecCanal(canaisFlat, dwgNome));
                linhas.AddRange(AgregarSmecValvulas(valvulas, dwgNome));
                // (funis já decompostos acima: tubo vertical -> tubosFlat; curva -> conexoes)

                EscreverFormularioMulti(outSmec, linhas, ed);
                ed.WriteMessage($"\n[GERAL] FORMULÁRIO -> {outSmec}  ({linhas.Count} linhas)");
                ed.WriteMessage("\n[GERAL] Concluído.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[GERAL] ERRO: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>Válvulas por DN -> linha SMEC (família VÁLVULAS, quantidade = unidades).</summary>
        private static List<SmecLinha> AgregarSmecValvulas(Dictionary<int, int> valvulas, string docRef)
        {
            var linhas = new List<SmecLinha>();
            foreach (var kv in valvulas.OrderBy(k => k.Key))
                linhas.Add(new SmecLinha(FAMILIA_VALVULAS,
                    string.Format(IT_VALV_FMT, kv.Key), kv.Value, docRef));
            return linhas;
        }

        /// <summary>
        /// Escreve linhas SMEC de MÚLTIPLAS famílias no FORMULÁRIO, validando cada item
        /// contra a coluna da SUA família na TABELAS_AUXILIARES (cache por família).
        /// </summary>
        private static void EscreverFormularioMulti(string destino, List<SmecLinha> linhas, Editor ed)
        {
            ExcelPackage.License.SetNonCommercialPersonal("Gleison Bruno da Costa");
            using var pkg = new ExcelPackage(new FileInfo(destino));
            var wb = pkg.Workbook;

            var form = wb.Worksheets.FirstOrDefault(
                w => w.Name.IndexOf("FORMUL", StringComparison.OrdinalIgnoreCase) >= 0);
            if (form == null)
                throw new InvalidOperationException("Aba 'FORMULÁRIO DE SOLICITAÇÕES' não encontrada.");

            var cacheValidos = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> ValidosDe(string familia)
            {
                if (!cacheValidos.TryGetValue(familia, out var d))
                {
                    d = LerItensValidos(wb, familia);
                    cacheValidos[familia] = d;
                }
                return d;
            }

            int linha = FORM_LINHA_INICIO;
            int naoEnc = 0;
            foreach (var l in linhas)
            {
                string itemFinal = l.Item;
                var validos = ValidosDe(l.Familia);
                if (validos.Count > 0 && validos.TryGetValue(Normalizar(l.Item), out string exato))
                    itemFinal = exato;
                else
                {
                    naoEnc++;
                    ed.WriteMessage($"\n[GERAL][AVISO] item sem match (linha {linha}, {l.Familia}): {l.Item}");
                }

                form.Cells[linha, COL_DOCREF].Value = l.DocRef;
                form.Cells[linha, COL_FAMILIA].Value = l.Familia;
                form.Cells[linha, COL_ITEM].Value = itemFinal;
                form.Cells[linha, COL_QTD].Value = Math.Round(l.Quantidade, 3);
                linha++;
            }

            if (naoEnc > 0)
                ed.WriteMessage($"\n[GERAL] {naoEnc} item(ns) sem correspondência exata (ficam laranja).");

            pkg.Save();
        }
    }
}
