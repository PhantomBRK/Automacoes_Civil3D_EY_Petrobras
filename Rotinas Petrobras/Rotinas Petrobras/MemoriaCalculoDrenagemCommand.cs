using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using SOLIDOS;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Exception = System.Exception;

namespace AutomacoesCivil3D
{
    /// <summary>
    /// Comando MEMORIA_CALCULO_DRENAGEM — gera planilha XLSX com a memória de cálculo
    /// hidráulica/hidrológica dos dispositivos SOLIDOS da rede de drenagem, no formato
    /// HDT (Hydraulic Design Table) usado pela Petrobras (REPAR_NOVO HDT.XLS).
    ///
    /// Os parâmetros do SOLIDOS são lidos pelos nomes ingleses oficiais
    /// (ACMan, Diameter, Width, Depth, Slope, ArcLength, StartPoint, EndPoint,
    /// Location, RimElevation, FilledDepth, CalcVelocity, etc.).
    /// </summary>
    public class MemoriaCalculoDrenagemCommand
    {
        private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
        private static readonly CultureInfo Inv  = CultureInfo.InvariantCulture;

        // ───────────────────────────────────────────────────────────────────────────
        // COMANDO
        // ───────────────────────────────────────────────────────────────────────────

        [CommandMethod("MEMORIA_CALCULO_DRENAGEM")]
        public void GerarMemoriaCalculo()
        {
            Editor ed = Manager.DocEditor;
            Database db = Manager.DocCad.Database;

            try
            {
                // 1) Seleção de UM dispositivo (qualquer caixa, PV, tubo ou canaleta da rede)
                ObjectId semente = SelecionarSemente(ed);
                if (semente.IsNull)
                {
                    ed.WriteMessage("\n[MC_DRE] Nenhum dispositivo selecionado.");
                    return;
                }

                // 2) Varre a rede inteira (BFS) a partir da semente usando a API SOLIDOS
                HashSet<ObjectId> idsRede = ColetarRedeCompleta(semente, ed);
                if (idsRede.Count == 0)
                {
                    ed.WriteMessage("\n[MC_DRE] Não foi possível identificar a rede a partir do dispositivo selecionado.");
                    return;
                }
                ed.WriteMessage($"\n[MC_DRE] Rede identificada: {idsRede.Count} elementos (dispositivos + tubos).");

                string saida = ObterCaminhoSaida(ed);
                if (string.IsNullOrWhiteSpace(saida)) return;

                List<DadosDispositivo> lista;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    lista = LerDispositivosPorIds(idsRede, tr, ed);
                    tr.Commit();
                }

                if (lista.Count == 0)
                {
                    ed.WriteMessage("\n[MC_DRE] Nenhum elemento SOLIDOS reconhecido na rede.");
                    return;
                }

                GerarExcel(lista, saida, ed);
                ed.WriteMessage($"\n[MC_DRE] Concluído. {lista.Count} dispositivos → {saida}");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                Manager.DocEditor.WriteMessage($"\n[AutoCAD] {ex.Message}");
            }
            catch (Exception ex)
            {
                Manager.DocEditor.WriteMessage($"\n[.NET] {ex.Message}");
            }
        }

        // ───────────────────────────────────────────────────────────────────────────
        // SELEÇÃO E TRAVERSAL DA REDE
        // ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Seleciona UM dispositivo SOLIDOS qualquer da rede (semente para o BFS).
        /// </summary>
        private static ObjectId SelecionarSemente(Editor ed)
        {
            var peo = new PromptEntityOptions(
                "\nSelecione um dispositivo da rede SOLIDOS (caixa, PV, tubo ou canaleta): ");
            peo.SetRejectMessage("\nSelecione um objeto SOLIDOS.");
            peo.AllowNone = false;

            PromptEntityResult per = ed.GetEntity(peo);
            return per.Status == PromptStatus.OK ? per.ObjectId : ObjectId.Null;
        }

        /// <summary>
        /// A partir de UMA semente (dispositivo ou tubo), percorre a rede inteira via
        /// API SOLIDOS (ConnectedDevices / InPart / OutPart) e retorna todos os ObjectIds
        /// que pertencem ao componente conexo: dispositivos (nós) + tubos (arcos).
        ///
        /// Algoritmo:
        ///   - Se a semente é um dispositivo: enfileira como nó
        ///   - Se a semente é um tubo: lê InPart/OutPart e enfileira ambos
        ///   - Para cada dispositivo da fila: lê ConnectedDevices (lista de tubos)
        ///     e adiciona os tubos ao resultado; cada tubo expõe InPart/OutPart, que
        ///     são novos dispositivos a serem enfileirados
        ///   - Termina quando a fila esvazia (componente conexo completo)
        /// </summary>
        private static HashSet<ObjectId> ColetarRedeCompleta(ObjectId semente, Editor ed)
        {
            var visitadosDispositivos = new HashSet<ObjectId>();
            var visitadosTubos        = new HashSet<ObjectId>();
            var fila                  = new Queue<ObjectId>();
            const int LimiteGuarda = 10000;

            void EnfileirarDispositivo(ObjectId devId)
            {
                if (devId.IsNull || visitadosDispositivos.Contains(devId)) return;
                visitadosDispositivos.Add(devId);
                fila.Enqueue(devId);
            }

            // Identifica se a semente é um tubo (tem InPart/OutPart) ou um dispositivo
            ObjectId inPart  = LerObjectId(semente, "InPart");
            ObjectId outPart = LerObjectId(semente, "OutPart");
            bool sementeETubo = !inPart.IsNull && !outPart.IsNull;

            if (sementeETubo)
            {
                visitadosTubos.Add(semente);
                EnfileirarDispositivo(inPart);
                EnfileirarDispositivo(outPart);
            }
            else
            {
                EnfileirarDispositivo(semente);
            }

            int contador = 0;
            while (fila.Count > 0)
            {
                if (++contador > LimiteGuarda)
                {
                    ed.WriteMessage($"\n[MC_DRE] Aviso: limite de {LimiteGuarda} dispositivos atingido — varredura interrompida.");
                    break;
                }

                ObjectId devId = fila.Dequeue();
                List<ObjectId> tubos = LerListaObjectIds(devId, "ConnectedDevices");

                foreach (ObjectId tuboId in tubos)
                {
                    if (tuboId.IsNull || visitadosTubos.Contains(tuboId)) continue;
                    visitadosTubos.Add(tuboId);

                    ObjectId tIn  = LerObjectId(tuboId, "InPart");
                    ObjectId tOut = LerObjectId(tuboId, "OutPart");
                    EnfileirarDispositivo(tIn);
                    EnfileirarDispositivo(tOut);
                }
            }

            var resultado = new HashSet<ObjectId>(visitadosDispositivos);
            foreach (ObjectId t in visitadosTubos) resultado.Add(t);
            return resultado;
        }

        private static ObjectId LerObjectId(ObjectId id, string nome)
        {
            try
            {
                Type tipo = null;
                object val = SolidosAPI.GetNodeParam(id, nome, null, ref tipo);
                if (val is ObjectId oid) return oid;
            }
            catch { }
            return ObjectId.Null;
        }

        private static List<ObjectId> LerListaObjectIds(ObjectId id, string nome)
        {
            var lista = new List<ObjectId>();
            try
            {
                Type tipo = null;
                object raw = SolidosAPI.GetNodeParam(id, nome, null, ref tipo);
                if (raw == null) return lista;

                switch (raw)
                {
                    case List<ObjectId> l:        lista.AddRange(l); break;
                    case ObjectId[] arr:          lista.AddRange(arr); break;
                    case ObjectIdCollection col:  foreach (ObjectId o in col) lista.Add(o); break;
                    case System.Collections.IEnumerable en:
                        foreach (object o in en) if (o is ObjectId oid) lista.Add(oid);
                        break;
                }
            }
            catch { }
            return lista;
        }

        private static string ObterCaminhoSaida(Editor ed)
        {
            string dwg = Path.GetFileNameWithoutExtension(Manager.DocCad.Name);
            var opts = new PromptSaveFileOptions("\nSalvar memória de cálculo (.xlsx):")
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                InitialFileName = $"MC_DRE_{dwg}_HDT.xlsx"
            };
            PromptFileNameResult r = ed.GetFileNameForSave(opts);
            return r.Status == PromptStatus.OK ? r.StringResult : string.Empty;
        }

        // ───────────────────────────────────────────────────────────────────────────
        // LEITURA DOS PARÂMETROS DO SOLIDOS
        // ───────────────────────────────────────────────────────────────────────────

        private static List<DadosDispositivo> LerDispositivosPorIds(
            HashSet<ObjectId> idsRede, Transaction tr, Editor ed)
        {
            var lista = new List<DadosDispositivo>();

            foreach (ObjectId objId in idsRede)
            {
                if (objId.IsNull) continue;
                try
                {
                    var ent = tr.GetObject(objId, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    ObjectId id = ent.ObjectId;
                    string familia = LerString(id, "FamilyName", "Family", "Familia", "NomeFamilia");
                    if (string.IsNullOrWhiteSpace(familia)) continue;

                    var dev = new DadosDispositivo
                    {
                        Handle  = ent.Handle.ToString(),
                        Familia = familia,
                        Nome    = LerString(id, "Nome", "Name") ?? ent.Handle.ToString(),
                        Sistema = LerString(id, "Sistema", "System", "Rede", "Network", "NomeRede",
                                                "NetworkName") ?? "",
                        SubSistema = LerString(id, "Subsistema", "SubRede", "Subsystem",
                                                   "Subnetwork", "Ramal") ?? "",
                        Material = LerString(id, "Material") ?? "",
                        Catalogo = LerString(id, "Catalogo") ?? "",
                        Tag      = LerString(id, "Tag", "TAG") ?? "",
                    };

                    // Geometria (pontos retornados pelo SOLIDOS)
                    var pStart = LerPonto(id, "StartPoint");
                    var pEnd   = LerPonto(id, "EndPoint");
                    var pLoc   = LerPonto(id, "Location");
                    dev.StartZ    = pStart?.Z;
                    dev.EndZ      = pEnd?.Z;
                    dev.LocationZ = pLoc?.Z;
                    dev.PlanLength = (pStart != null && pEnd != null)
                        ? Math.Sqrt(Math.Pow(pStart.X - pEnd.X, 2) + Math.Pow(pStart.Y - pEnd.Y, 2))
                        : (double?)null;

                    // Dimensões da seção (parâmetros oficiais SOLIDOS)
                    dev.Diametro = LerDouble(id, "Diametro", "Diameter", "DN");
                    dev.Largura  = LerDouble(id, "Largura", "Width", "BaseWidth", "ShapeWidth",
                                                 "TopWidth");
                    dev.Altura   = LerDouble(id, "Altura", "Depth", "Height", "BaseHeight",
                                                 "ShapeHeight");
                    dev.Base     = LerDouble(id, "Base");

                    // Hidráulica
                    dev.Declividade = LerDouble(id, "Declividade", "Gradient", "Slope", "AverageSlope");
                    dev.ManningN    = LerDouble(id, "ACMan", "Manning", "Roughness", "ChannelRoughness",
                                                    "FrictionFactor");
                    dev.HazenW      = LerDouble(id, "ACHW", "HazenWilliams");
                    dev.Darcy       = LerDouble(id, "ACDW", "DarcyWeisbach");

                    // Comprimento (várias fontes possíveis no SOLIDOS)
                    dev.Comprimento = LerDouble(id, "Comprimento", "Length", "ArcLength",
                                                    "Length2D", "Length3D", "L", "L1",
                                                    "LengtheningStep", "TrenchLength")
                                      ?? dev.PlanLength;

                    // Cotas — SOLIDOS expõe via geometria ou parâmetros nomeados
                    double? sumpZ      = LerDouble(id, "SumpElevation", "InvertElevation",
                                                       "InvertAttr", "BottomElev");
                    double? rimZ       = LerDouble(id, "RimElevation", "TopElevation",
                                                       "SurfaceElevation", "GradeElevation",
                                                       "ConnectionGradeElevation");
                    double? startInvZ  = LerDouble(id, "StartGradeElevation", "InletElevation",
                                                       "StartInvert");
                    double? endInvZ    = LerDouble(id, "EndGradeElevation", "OutletElevation",
                                                       "EndInvert");

                    dev.CotaFundoMont = startInvZ
                                        ?? (pStart != null && pEnd != null
                                            ? Math.Max(pStart.Z, pEnd.Z)
                                            : (double?)null)
                                        ?? sumpZ;

                    dev.CotaFundoJus  = endInvZ
                                        ?? (pStart != null && pEnd != null
                                            ? Math.Min(pStart.Z, pEnd.Z)
                                            : (double?)null)
                                        ?? sumpZ;

                    dev.CotaTerreno   = rimZ
                                        ?? (sumpZ.HasValue && dev.Altura.HasValue
                                            ? sumpZ.Value + dev.Altura.Value
                                            : (double?)null)
                                        ?? dev.LocationZ;

                    // Vazão / velocidade / lâmina (saída do SOLIDOS quando dimensionado)
                    dev.QDim    = LerDouble(id, "FlowRate", "Q", "Vazao", "VazaoProjeto",
                                                "QDesign", "RunoffAttr", "RunoffRateAttr",
                                                "CalcEquivalentWidth", "InflowRate", "OutFlowRate");
                    dev.VCalc   = LerDouble(id, "CalcVelocity", "VelocityAttr", "V",
                                                "Velocidade");
                    dev.YLamina = LerDouble(id, "FilledDepth", "FlowHeight", "Y", "SurfWaterDepth");
                    dev.YD      = LerDouble(id, "YD", "Y/D", "TiranteRelativo");
                    dev.Folga   = LerDouble(id, "Folga", "FolgaTampa", "FolgaLamina", "Freeboard");

                    // Conectividade (montante / jusante) — exibição
                    dev.DispMontante = LerString(id, "StartConnectorId", "StartDevice",
                                                     "StartConnector", "DISP. MONTANTE",
                                                     "DispMontante", "Montante", "NodeUp") ?? "";
                    dev.DispJusante  = LerString(id, "EndConnectorId", "EndDevice",
                                                     "EndConnector", "DISP. JUSANTE",
                                                     "DispJusante", "Jusante", "NodeDown") ?? "";

                    // Handles dos nós conectados (preenchidos apenas para tubos via InPart/OutPart).
                    // Necessários para o encadeamento nó↔tubo no formato HDT.
                    ObjectId montanteOid = LerObjectId(id, "InPart");
                    ObjectId jusanteOid  = LerObjectId(id, "OutPart");
                    if (!montanteOid.IsNull)
                    {
                        var entM = tr.GetObject(montanteOid, OpenMode.ForRead) as Entity;
                        if (entM != null) dev.MontanteHandle = entM.Handle.ToString();
                    }
                    if (!jusanteOid.IsNull)
                    {
                        var entJ = tr.GetObject(jusanteOid, OpenMode.ForRead) as Entity;
                        if (entJ != null) dev.JusanteHandle = entJ.Handle.ToString();
                    }
                    dev.MontanteHandle ??= "";
                    dev.JusanteHandle  ??= "";

                    // Classificação e padrões default
                    dev.Tipo = ClassificarSecao(familia);
                    if (dev.ManningN == null || dev.ManningN <= 0)
                        dev.ManningN = ManningPadrao(dev.Tipo, familia);

                    (dev.QPlena, dev.VPlena) = CalcularManningPlena(dev);

                    // Y/D estimado se SOLIDOS não trouxe explicitamente
                    if (!dev.YD.HasValue)
                    {
                        if (dev.YLamina.HasValue && dev.Diametro.HasValue && dev.Diametro > 0)
                            dev.YD = dev.YLamina.Value / dev.Diametro.Value;
                        else if (dev.QDim.HasValue && dev.QPlena.HasValue && dev.QPlena > 0 &&
                                 dev.Tipo == TipoSecao.Circular)
                            dev.YD = YsobreD_Circular(dev.QDim.Value / dev.QPlena.Value);
                    }

                    lista.Add(dev);
                }
                catch (Exception ex)
                {
                    ed.WriteMessage($"\n[MC_DRE] Falha (objId {objId}): {ex.Message}");
                }
            }

            return lista;
        }

        // ───────────────────────────────────────────────────────────────────────────
        // CLASSIFICAÇÃO E FÓRMULAS HIDRÁULICAS
        // ───────────────────────────────────────────────────────────────────────────

        private static TipoSecao ClassificarSecao(string familia)
        {
            string f = familia.ToUpperInvariant();
            if (f.Contains("CANALETA") || f.Contains("VALETA") || f.Contains("GALERIA") ||
                f.Contains("GUTTER")   || f.Contains("CONDUITO RETANGULAR"))
                return TipoSecao.Retangular;
            if (f.Contains("CAIXA") || f.Contains("PV") || f.Contains("POCO") ||
                f.Contains("CHAMBER") || f.Contains("MANHOLE") || f.Contains("SUMP") ||
                f.Contains("RALO") || f.Contains("BOCA") || f.Contains("BL") ||
                f.Contains("FUNIL") || f.Contains("INSPECAO"))
                return TipoSecao.Estrutura;
            return TipoSecao.Circular;
        }

        private static double ManningPadrao(TipoSecao tipo, string familia)
        {
            string f = familia?.ToUpperInvariant() ?? "";
            if (f.Contains("CONCRETO")) return 0.013;
            if (f.Contains("PVC") || f.Contains("PEAD") || f.Contains("HDPE")) return 0.011;
            if (f.Contains("METAL") || f.Contains("ACO")) return 0.012;
            if (tipo == TipoSecao.Retangular) return 0.015;
            return 0.013;
        }

        private static (double? qPlena, double? vPlena) CalcularManningPlena(DadosDispositivo dev)
        {
            double n = dev.ManningN ?? 0.013;
            double i = dev.Declividade ?? 0;
            if (i <= 0 || n <= 0) return (null, null);

            if (dev.Tipo == TipoSecao.Circular)
            {
                double d = dev.Diametro ?? 0;
                if (d <= 0) return (null, null);
                double area = Math.PI * d * d / 4.0;
                double rh   = d / 4.0;
                double v    = (1.0 / n) * Math.Pow(rh, 2.0 / 3.0) * Math.Sqrt(i);
                return (v * area * 1000.0, v); // l/s, m/s
            }

            if (dev.Tipo == TipoSecao.Retangular)
            {
                double b = dev.Largura ?? 0;
                double h = dev.Altura  ?? 0;
                if (b <= 0 || h <= 0) return (null, null);
                double area = b * h;
                double rh   = area / (b + 2 * h);
                double v    = (1.0 / n) * Math.Pow(rh, 2.0 / 3.0) * Math.Sqrt(i);
                return (v * area * 1000.0, v);
            }

            return (null, null);
        }

        // Inversão da função de seção plena para encontrar Y/D em tubo circular
        // dado a razão q/Qp (busca binária).
        private static double? YsobreD_Circular(double qQp)
        {
            if (qQp <= 0) return 0.0;
            if (qQp >= 1.0) return 1.0;

            double lo = 0, hi = 1.0;
            for (int k = 0; k < 60; k++)
            {
                double mid   = (lo + hi) / 2;
                double theta = 2 * Math.Acos(Math.Max(-1, Math.Min(1, 1 - 2 * mid)));
                double aRat  = (theta - Math.Sin(theta)) / (2 * Math.PI);
                double rhRat = theta > 0 ? (theta - Math.Sin(theta)) / theta : 0;
                double ratio = aRat * Math.Pow(rhRat, 2.0 / 3.0);
                if (Math.Abs(ratio - qQp) < 1e-7) return mid;
                if (ratio < qQp) lo = mid; else hi = mid;
            }
            return (lo + hi) / 2;
        }

        // ───────────────────────────────────────────────────────────────────────────
        // ORDENAÇÃO HIERÁRQUICA DAS ESTACAS (A4.11.1.1 < A4.11.2 < A5)
        // ───────────────────────────────────────────────────────────────────────────

        private static int CompararEstacas(string a, string b)
        {
            int[] ka = ChaveEstaca(a), kb = ChaveEstaca(b);
            int len = Math.Max(ka.Length, kb.Length);
            for (int k = 0; k < len; k++)
            {
                int va = k < ka.Length ? ka[k] : 0;
                int vb = k < kb.Length ? kb[k] : 0;
                if (va != vb) return va.CompareTo(vb);
            }
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Comparador "trunk-priority" usado no HDT (Petrobras): emite primeiro a
        /// CABECEIRA DO TRONCO PRINCIPAL e segue jusante, intercalando tributários
        /// apenas após processar o nó da confluência.
        ///
        /// Regras (testadas contra o REPAR_NOVO HDT.XLS):
        ///   - Se uma chave é prefixo da outra → a mais curta primeiro
        ///     (A14 antes de A14.1; A14 é tronco, A14.1 é ramal saindo dele)
        ///   - Diferença no PRIMEIRO nível → maior número primeiro
        ///     (A16, A15, A14 nessa ordem — montante→jusante do tronco)
        ///   - Diferença em níveis subsequentes → menor número primeiro
        ///     (A4.11.1 antes de A4.11.2; processa o ramal .1 inteiro antes do .2)
        /// </summary>
        private static int CompararEstacasHDT(string a, string b)
        {
            int[] ka = ChaveEstaca(a), kb = ChaveEstaca(b);
            int minLen = Math.Min(ka.Length, kb.Length);

            int i = 0;
            while (i < minLen && ka[i] == kb[i]) i++;

            // Prefixo: o mais curto vem primeiro (tronco antes do ramal)
            if (i == minLen) return ka.Length.CompareTo(kb.Length);

            // Primeiro nível diferente: maior é mais a montante → vem antes
            if (i == 0) return kb[i].CompareTo(ka[i]);

            // Nível subsequente: menor número de ramal vem primeiro
            return ka[i].CompareTo(kb[i]);
        }

        private static int[] ChaveEstaca(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome)) return new[] { int.MaxValue };
            int pos = 0;
            while (pos < nome.Length && (char.IsLetter(nome[pos]) || nome[pos] == ' ')) pos++;
            string[] partes = nome.Substring(pos).Split('.');
            var res = new List<int>();
            foreach (string p in partes)
                res.Add(int.TryParse(p.Trim(), out int n) ? n : 0);
            return res.Count > 0 ? res.ToArray() : new[] { 0 };
        }

        // ───────────────────────────────────────────────────────────────────────────
        // GERAÇÃO DO EXCEL — FORMATO HDT (REPAR_NOVO HDT.XLS)
        // ───────────────────────────────────────────────────────────────────────────
        //
        // Estrutura da planilha (uma aba por rede / sistema):
        //   Linhas 1–14 : Bloco de título (projeto, rede, n Manning, Tr, pluviógrafo)
        //   Linhas 15–18: Cabeçalhos multi-nível (HIDROLOGIA / HIDRÁULICA + sub-níveis)
        //   Linhas 19+  : Dados (uma linha por dispositivo, ordenado por estaca)
        //   Bloco final : Tabela auxiliar Manning (Y/D, F, S/D², Rh/D — referência)
        //
        // ───────────────────────────────────────────────────────────────────────────

        private static void GerarExcel(List<DadosDispositivo> lista, string caminho, Editor ed)
        {
            try { ExcelPackage.License.SetNonCommercialPersonal("Gleison Bruno da Costa"); }
            catch { /* já configurado */ }

            using var pkg = new ExcelPackage();

            var grupos = lista
                .GroupBy(d => string.IsNullOrWhiteSpace(d.Sistema) ? "REDE" : d.Sistema.ToUpperInvariant())
                .OrderBy(g => g.Key);

            foreach (var grupo in grupos)
            {
                // A ordenação interna real (trunk-priority + intercalação nó/tubo)
                // é feita dentro de CriarAbaHDT; aqui só agrupamos por rede.
                var devs = grupo.ToList();
                CriarAbaHDT(pkg, grupo.Key, devs);
            }

            // Aba "Modelo" — tabela de cálculo Manning para seção parcial (referência)
            CriarAbaTabelaManning(pkg);

            pkg.SaveAs(new FileInfo(caminho));
        }

        // Paleta HDT (espelha as cores do REPAR_NOVO HDT.XLS)
        private static readonly Color CorBordaTitulo  = Color.FromArgb(  0,   0,   0);
        private static readonly Color CorTituloBg     = Color.FromArgb(255, 255, 255);
        private static readonly Color CorSecaoBg      = Color.FromArgb(217, 217, 217);
        private static readonly Color CorHidrologia   = Color.FromArgb(255, 242, 204); // amarelo claro
        private static readonly Color CorHidraulica   = Color.FromArgb(217, 234, 211); // verde claro
        private static readonly Color CorIdentif      = Color.FromArgb(252, 228, 214); // pêssego
        private static readonly Color CorCotas        = Color.FromArgb(221, 235, 247); // azul claro
        private static readonly Color CorQDim         = Color.FromArgb(255, 199,  77); // âmbar HDT
        private static readonly Color CorEstaca       = Color.FromArgb(226, 239, 218); // verde HDT
        private static readonly Color CorEstacaNo     = Color.FromArgb(255, 242, 204); // amarelo nó

        // ───────────────────────────────────────────────────────────────────────────
        // ABA HDT — uma rede de drenagem
        // ───────────────────────────────────────────────────────────────────────────

        private static void CriarAbaHDT(ExcelPackage pkg, string nomeRede, List<DadosDispositivo> devs)
        {
            string nomeAba = nomeRede.Length > 31 ? nomeRede.Substring(0, 31) : nomeRede;
            ExcelWorksheet ws = pkg.Workbook.Worksheets.Add(nomeAba);
            ws.View.ShowGridLines = false;

            // ── ESTRUTURA DE COLUNAS (51 colunas, A:AY, replicando o HDT) ──────────
            //
            // A   : PR (Estaca/identificação do dispositivo)
            // B-D : metadados (Sistema, Tipo, Família)
            // E   : COTAS Terreno (m)
            // F   : COTAS Fundo (m)
            // G   : Nível d'água (m)
            // H-O : HIDROLOGIA — BACIA LOCAL + CONTRIBUIÇÃO LOCAL
            // P-T : VAZÕES — Combate, Equipamento, Hidrologia + Equip
            // U   : Q DIM (vazão de dimensionamento)
            // V-X : (espaço para fórmulas intermediárias)
            // Y-AB: HIDRÁULICA — Declividade, Seção, Altura d'água, Y/D
            // AC-AH: Profundidades, Velocidades, Comprimento, tempo
            // AI-AN: Verificação (Q plena, q/Q, Rh, etc.)
            // AO  : FOLGA CANALETA
            // AP-AY: Conectividade (Montante/Jusante PV/PR, Cota Ter., Cota Fundo)

            // Larguras de coluna (HDT compacto)
            ws.Column(1).Width  = 14;                       // PR
            ws.Column(2).Width  = 14;                       // Sistema
            ws.Column(3).Width  = 11;                       // Tipo
            ws.Column(4).Width  = 18;                       // Família
            ws.Column(5).Width  =  9;                       // COTAS Terreno
            ws.Column(6).Width  =  9;                       // COTAS Fundo
            ws.Column(7).Width  =  9;                       // Nível d'água
            for (int c = 8; c <= 20; c++) ws.Column(c).Width = 8.5;   // Hidrologia
            ws.Column(21).Width = 11;                       // Q DIM
            for (int c = 22; c <= 28; c++) ws.Column(c).Width = 9;    // Hidráulica
            ws.Column(29).Width = 11;                       // Comprimento
            for (int c = 30; c <= 41; c++) ws.Column(c).Width = 8.5;  // Auxiliares
            ws.Column(42).Width = 11;                       // FOLGA CANALETA
            for (int c = 43; c <= 51; c++) ws.Column(c).Width = 9;    // Conectividade

            // ── BLOCO DE TÍTULO (rows 1–14) — espelha REPAR_NOVO HDT.XLS ───────────
            EscreverTituloHDT(ws, 1, 1, 51, "REPAR – Refinaria Presidente Getúlio Vargas", 14, true);
            EscreverTituloHDT(ws, 2, 1, 51, "MEMÓRIA DE CÁLCULO – HDT", 12, true);
            EscreverTituloHDT(ws, 3, 1, 51, $"{nomeRede}", 11, true);

            ws.Cells[5, 1].Value = "REV. 0";
            ws.Cells[5, 1].Style.Font.Bold = true;
            ws.Cells[5, 5].Value = $"DATA: {DateTime.Now:dd/MM/yyyy}";
            ws.Cells[5, 5].Style.Font.Bold = true;

            ws.Cells[5, 40].Value = "PLUVIÓGRAFO:";
            ws.Cells[5, 40].Style.Font.Bold = true;
            ws.Cells[5, 41].Value = "CUBATÃO";
            ws.Cells[6, 40].Value = "COEFICIENTE MANNING";
            ws.Cells[6, 40].Style.Font.Bold = true;
            ws.Cells[6, 47].Value = "TUBO n =";
            ws.Cells[6, 48].Value = 0.013;
            ws.Cells[6, 48].Style.Numberformat.Format = "0.000";
            ws.Cells[7, 40].Value = "TEMPO DE RECORRÊNCIA";
            ws.Cells[7, 40].Style.Font.Bold = true;
            ws.Cells[7, 47].Value = "Tr =";
            ws.Cells[7, 48].Value = 10;
            ws.Cells[7, 48].Style.Numberformat.Format = "0";

            // ── CABEÇALHOS MULTI-NÍVEL (rows 15–18) ────────────────────────────────
            int hr1 = 15, hr2 = 16, hr3 = 17, hr4 = 18;

            // Nível 1 — grandes seções
            MesclarCabecalho(ws, hr1, 1, 4,   "IDENTIFICAÇÃO",        CorIdentif);
            MesclarCabecalho(ws, hr1, 5, 7,   "COTAS",                CorCotas);
            MesclarCabecalho(ws, hr1, 8, 21,  "HIDROLOGIA",           CorHidrologia);
            MesclarCabecalho(ws, hr1, 22, 41, "HIDRÁULICA",           CorHidraulica);
            MesclarCabecalho(ws, hr1, 42, 42, "FOLGA",                CorHidrologia);
            MesclarCabecalho(ws, hr1, 43, 47, "MONTANTE",             CorCotas);
            MesclarCabecalho(ws, hr1, 48, 51, "JUSANTE",              CorCotas);

            // Nível 2 — sub-seções
            MesclarCabecalho(ws, hr2, 8, 9,   "BACIA LOCAL",          CorHidrologia);
            MesclarCabecalho(ws, hr2, 10, 14, "CONTRIBUIÇÃO LOCAL",   CorHidrologia);
            MesclarCabecalho(ws, hr2, 15, 16, "VAZÃO COMBATE INC.",   CorHidrologia);
            MesclarCabecalho(ws, hr2, 17, 18, "VAZÃO EQUIPAMENTO",    CorHidrologia);
            MesclarCabecalho(ws, hr2, 19, 20, "VAZÃO HIDRO + EQUIP",  CorHidrologia);
            MesclarCabecalho(ws, hr2, 21, 21, "VAZÃO DIM.",           CorQDim);

            MesclarCabecalho(ws, hr2, 22, 25, "DIMENSIONAMENTO",      CorHidraulica);
            MesclarCabecalho(ws, hr2, 26, 28, "LÂMINA / V",           CorHidraulica);
            MesclarCabecalho(ws, hr2, 29, 30, "COMPRIMENTO / t",      CorHidraulica);
            MesclarCabecalho(ws, hr2, 31, 35, "VERIFICAÇÃO PLENA",    CorHidraulica);
            MesclarCabecalho(ws, hr2, 36, 41, "AUXILIARES",           CorHidraulica);

            // Nível 3 — cabeçalhos individuais
            string[] heads = new string[51];
            heads[0]  = "PR";                heads[1]  = "SISTEMA";
            heads[2]  = "TIPO";              heads[3]  = "FAMÍLIA";

            heads[4]  = "COTAS\nTerreno (m)";
            heads[5]  = "COTAS\nFundo (m)";
            heads[6]  = "Nível\nd'água (m)";

            heads[7]  = "ÁREA\n(ha)";        heads[8]  = "Coef.\nImper.";
            heads[9]  = "Área Total\nAcum. (ha)";
            heads[10] = "Coef.\nMédio";      heads[11] = "Tempo\nConc.\n(min)";
            heads[12] = "Intens.\nPluvial\n(mm/h)";
            heads[13] = "Deflúvio\n(l/s)";
            heads[14] = "Vazão\nCombate\nIncêndio\n(l/s)";
            heads[15] = "Vazão Total\nCombate\nIncêndio\n(l/s)";
            heads[16] = "Vazão do\nEquipamento\n(l/s)";
            heads[17] = "Vazão do\nDreno do\nEquipam. (l/s)";
            heads[18] = "Vazão Hidro\n+ Equip.\n(l/s)";
            heads[19] = "Vazão Dim.\nPluv + Equip\n+ Comb (l/s)";
            heads[20] = "Q DIM\n(l/s)";

            heads[21] = "Declividade\n(m/m)";
            heads[22] = "Dimensões\nSeção D (m)";
            heads[23] = "Altura\nd'água\nY (m)";
            heads[24] = "Y/D\n(%)";
            heads[25] = "Profund.\nMont. (m)";
            heads[26] = "Profund.\nJus. (m)";
            heads[27] = "V real\n(m/s)";
            heads[28] = "L\n(m)";
            heads[29] = "t\n(min)";
            heads[30] = "Q plena\n(l/s)";
            heads[31] = "V plena\n(m/s)";
            heads[32] = "q/Qp";
            heads[33] = "Área\nseção\n(m²)";
            heads[34] = "Rh\n(m)";
            heads[35] = "n\nManning";
            heads[36] = "HW";                heads[37] = "DW";
            heads[38] = "Base\n(m)";         heads[39] = "Largura\n(m)";
            heads[40] = "Altura\n(m)";
            heads[41] = "Folga\nCanaleta\n(%)";
            heads[42] = "PV/PR";             heads[43] = "Cota Ter.\n(m)";
            heads[44] = "Cota Fundo\n(m)";   heads[45] = "Compr.\n(m)";
            heads[46] = "Decl.\n(m/m)";
            heads[47] = "PV/PR";             heads[48] = "Cota Ter.\n(m)";
            heads[49] = "Cota Fundo\n(m)";   heads[50] = "Mat.";

            for (int c = 0; c < 51; c++)
            {
                Color bg = c < 4 ? CorIdentif
                         : c < 7 ? CorCotas
                         : c < 20 ? CorHidrologia
                         : c == 20 ? CorQDim
                         : c < 41 ? CorHidraulica
                         : c == 41 ? CorHidrologia
                         : CorCotas;
                MesclarCabecalho(ws, hr3, c + 1, c + 1, heads[c], bg);
            }

            ws.Row(hr1).Height = 18;
            ws.Row(hr2).Height = 18;
            ws.Row(hr3).Height = 48;

            // ── DADOS — padrão HDT: par de linhas (nó amarelo + tubo branco) ───────
            //
            // Separamos os dispositivos em NÓS (estruturas) e TUBOS, depois ordenamos
            // os nós por trunk-priority. Para cada nó emitimos:
            //   • LINHA AMARELA: identificação + Cota Terreno; e, se for nó
            //     intermediário (tem tubo chegando), a Cota Fundo Jusante daquele
            //     tubo chegando (= cota de saída do nó).
            //   • LINHAS BRANCAS: uma por tubo saindo do nó — Cota Fundo Montante
            //     do tubo + hidrologia + hidráulica.

            var nos    = devs.Where(d => d.Tipo == TipoSecao.Estrutura).ToList();
            var tubos  = devs.Where(d => d.Tipo != TipoSecao.Estrutura).ToList();

            // Índices para descobrir tubos entrando/saindo de cada nó
            var tubosPorMontante = tubos
                .Where(t => !string.IsNullOrEmpty(t.MontanteHandle))
                .GroupBy(t => t.MontanteHandle)
                .ToDictionary(g => g.Key, g => g.ToList());
            var tubosPorJusante = tubos
                .Where(t => !string.IsNullOrEmpty(t.JusanteHandle))
                .GroupBy(t => t.JusanteHandle)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Tubos sem nó associado (BFS pegou só pelo Sistema) ficam órfãos —
            // emitimos no final como linha simples para não perder dado.
            var tubosOrfaos = tubos
                .Where(t => string.IsNullOrEmpty(t.MontanteHandle) &&
                            string.IsNullOrEmpty(t.JusanteHandle))
                .ToList();

            // Ordenação trunk-priority dos nós
            nos.Sort((a, b) => CompararEstacasHDT(a.Nome ?? "", b.Nome ?? ""));

            int row = hr3 + 1;

            if (nos.Count == 0)
            {
                // Sem nós identificáveis (rede só de tubos sem caixas): emite
                // os tubos como linhas simples (fallback equivalente ao layout antigo).
                foreach (DadosDispositivo t in tubos)
                {
                    EmitirLinhaTuboHDT(ws, row, t);
                    row++;
                }
            }
            else
            {
                foreach (DadosDispositivo no in nos)
                {
                    tubosPorJusante.TryGetValue(no.Handle, out var entrando);
                    tubosPorMontante.TryGetValue(no.Handle, out var saindo);

                    DadosDispositivo tuboEntrando = entrando?.FirstOrDefault();
                    bool ehCabeceira = entrando == null || entrando.Count == 0;

                    EmitirLinhaNoHDT(ws, row, no, tuboEntrando, ehCabeceira);
                    row++;

                    if (saindo != null)
                    {
                        foreach (DadosDispositivo tubo in saindo)
                        {
                            EmitirLinhaTuboHDT(ws, row, tubo);
                            row++;
                        }
                    }
                }

                foreach (DadosDispositivo t in tubosOrfaos)
                {
                    EmitirLinhaTuboHDT(ws, row, t);
                    row++;
                }
            }

            // Borda externa da tabela
            int endRow = row - 1;
            if (endRow >= hr1)
            {
                var tabela = ws.Cells[hr1, 1, endRow, 51];
                tabela.Style.Border.Left.Style   = ExcelBorderStyle.Medium;
                tabela.Style.Border.Right.Style  = ExcelBorderStyle.Medium;
                tabela.Style.Border.Top.Style    = ExcelBorderStyle.Medium;
                tabela.Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
            }

            // Congela cabeçalho e a coluna A (estaca)
            ws.View.FreezePanes(hr3 + 1, 2);

            // Auto-filtro na linha de cabeçalhos individuais
            ws.Cells[hr3, 1, hr3, 51].AutoFilter = true;

            // Rodapé de legenda
            row += 2;
            ws.Cells[row, 1].Value = $"Total: {devs.Count} dispositivos";
            ws.Cells[row, 1, row, 4].Merge = true;
            ws.Cells[row, 1].Style.Font.Italic = true;
            ws.Cells[row, 1].Style.Font.Size = 8;

            ws.Cells[row, 8].Value  = "← Hidrologia: campos amarelos a preencher (área, C, tc, I, vazões)";
            ws.Cells[row, 8, row, 21].Merge = true;
            ws.Cells[row, 8].Style.Font.Italic = true;
            ws.Cells[row, 8].Style.Font.Size = 8;

            ws.Cells[row, 22].Value = "← Hidráulica: derivada dos parâmetros SOLIDOS (Diameter, Width, Depth, Slope, ACMan, ArcLength, StartPoint/EndPoint)";
            ws.Cells[row, 22, row, 51].Merge = true;
            ws.Cells[row, 22].Style.Font.Italic = true;
            ws.Cells[row, 22].Style.Font.Size = 8;
        }

        // ───────────────────────────────────────────────────────────────────────────
        // EMISSÃO DAS LINHAS DO HDT — par (nó amarelo + tubo branco)
        // ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Linha amarela: identifica o NÓ (caixa, PV).
        /// Cabeceira → só Cota Terreno; intermediário → Cota Terreno + Cota Fundo
        /// Jusante do tubo chegando (= cota de saída do nó) + NA.
        /// </summary>
        private static void EmitirLinhaNoHDT(ExcelWorksheet ws, int row,
            DadosDispositivo no, DadosDispositivo tuboEntrando, bool ehCabeceira)
        {
            Color bg = ehCabeceira ? CorEstacaNo : CorEstaca;

            Cel  (ws, row, 1, no.Nome,            bg, fontBold: true);
            Cel  (ws, row, 2, no.Sistema,         bg);
            Cel  (ws, row, 3, TipoStr(no.Tipo),   bg);
            Cel  (ws, row, 4, no.Familia,         bg, fontSize: 8);

            CelNum(ws, row, 5, no.CotaTerreno,    "0.000", bg);

            if (!ehCabeceira && tuboEntrando != null)
            {
                // Nó intermediário: mostra a cota de fundo da chegada (= cota
                // de saída do nó para o próximo tubo a jusante) e o nível d'água.
                CelNum(ws, row, 6, tuboEntrando.CotaFundoJus, "0.000", bg);
                CelNum(ws, row, 7, null,                       "0.000", bg);
            }
            else
            {
                // Cabeceira ou outlet sem tubo chegando → fundo e NA em branco.
                Cel(ws, row, 6, "", bg);
                Cel(ws, row, 7, "", bg);
            }

            // Linha do nó: hidrologia e hidráulica ficam em branco — esses dados
            // pertencem ao TUBO logo abaixo (e a possíveis tributários).
            for (int c = 8; c <= 51; c++)
                Cel(ws, row, c, "", bg);

            ws.Row(row).Height = 22;
        }

        /// <summary>
        /// Linha branca: contém os dados do TUBO saindo do nó imediatamente acima.
        /// PR fica vazio (o nome do tubo é implícito pelo nó-pai); Cota Fundo
        /// Montante é a cota inicial do tubo (= cota de saída do nó). Depois vêm
        /// todas as colunas de hidrologia (inputs amarelos) e hidráulica.
        /// </summary>
        private static void EmitirLinhaTuboHDT(ExcelWorksheet ws, int row, DadosDispositivo t)
        {
            Color bg = Color.White;

            Cel  (ws, row, 1, "",                bg);
            Cel  (ws, row, 2, t.Sistema,         bg);
            Cel  (ws, row, 3, TipoStr(t.Tipo),   bg);
            Cel  (ws, row, 4, t.Familia,         bg, fontSize: 8);

            Cel  (ws, row, 5, "",                bg);   // Terreno fica no nó acima
            CelNum(ws, row, 6, t.CotaFundoMont,  "0.000", bg);
            CelNum(ws, row, 7, null,             "0.000", bg);

            // Hidrologia — campos para o projetista preencher (amarelos)
            CelInput(ws, row,  8, null, CorHidrologia);
            CelInput(ws, row,  9, null, CorHidrologia);
            CelInput(ws, row, 10, null, CorHidrologia);
            CelInput(ws, row, 11, null, CorHidrologia);
            CelInput(ws, row, 12, null, CorHidrologia);
            CelInput(ws, row, 13, null, CorHidrologia);
            CelInput(ws, row, 14, null, CorHidrologia);
            CelInput(ws, row, 15, null, CorHidrologia);
            CelInput(ws, row, 16, null, CorHidrologia);
            CelNum  (ws, row, 17, t.QDim,  "0.00", CorHidrologia);
            CelInput(ws, row, 18, null, CorHidrologia);
            CelInput(ws, row, 19, null, CorHidrologia);
            CelInput(ws, row, 20, null, CorHidrologia);
            CelNum  (ws, row, 21, t.QDim,  "0.00", CorQDim);   // Q DIM

            // Hidráulica derivada dos parâmetros SOLIDOS
            CelNum(ws, row, 22, t.Declividade, "0.00000", bg);
            Cel  (ws, row, 23, FormatSecao(t), bg);
            CelNum(ws, row, 24, t.YLamina,     "0.000",   bg);
            CelNum(ws, row, 25, t.YD * 100,    "0.0",     bg);

            double? profMont = (t.CotaTerreno.HasValue && t.CotaFundoMont.HasValue)
                                ? t.CotaTerreno.Value - t.CotaFundoMont.Value : (double?)null;
            double? profJus  = (t.CotaTerreno.HasValue && t.CotaFundoJus.HasValue)
                                ? t.CotaTerreno.Value - t.CotaFundoJus.Value  : (double?)null;
            CelNum(ws, row, 26, profMont, "0.00", bg);
            CelNum(ws, row, 27, profJus,  "0.00", bg);
            CelNum(ws, row, 28, t.VCalc ?? t.VPlena, "0.000", bg);
            CelNum(ws, row, 29, t.Comprimento, "0.00", bg);

            double? tPercurso = (t.Comprimento.HasValue
                                 && (t.VCalc ?? t.VPlena).HasValue
                                 && (t.VCalc ?? t.VPlena) > 0)
                ? t.Comprimento.Value / (t.VCalc ?? t.VPlena).Value / 60.0
                : (double?)null;
            CelNum(ws, row, 30, tPercurso, "0.00", bg);

            CelNum(ws, row, 31, t.QPlena, "0.00", bg);
            CelNum(ws, row, 32, t.VPlena, "0.000", bg);
            double? qQp = (t.QDim.HasValue && t.QPlena.HasValue && t.QPlena > 0)
                ? t.QDim.Value / t.QPlena.Value : (double?)null;
            CelNum(ws, row, 33, qQp, "0.000", bg);

            CelNum(ws, row, 34, AreaSecao(t),      "0.0000", bg);
            CelNum(ws, row, 35, RaioHidraulico(t), "0.0000", bg);
            CelNum(ws, row, 36, t.ManningN, "0.000", bg);
            CelNum(ws, row, 37, t.HazenW,   "0",     bg);
            CelNum(ws, row, 38, t.Darcy,    "0.000", bg);

            CelNum(ws, row, 39, t.Base,    "0.000", bg);
            CelNum(ws, row, 40, t.Largura, "0.000", bg);
            CelNum(ws, row, 41, t.Altura,  "0.000", bg);

            CelNum(ws, row, 42, t.Folga.HasValue ? t.Folga * 100 : null, "0.0", CorHidrologia);

            // Conectividade — Montante / Jusante (referências do tubo)
            Cel  (ws, row, 43, t.DispMontante, bg);
            CelNum(ws, row, 44, t.CotaTerreno, "0.000", bg);
            CelNum(ws, row, 45, t.CotaFundoMont, "0.000", bg);
            CelNum(ws, row, 46, t.Comprimento, "0.00", bg);
            CelNum(ws, row, 47, t.Declividade, "0.00000", bg);

            Cel  (ws, row, 48, t.DispJusante,  bg);
            CelNum(ws, row, 49, t.CotaTerreno, "0.000", bg);
            CelNum(ws, row, 50, t.CotaFundoJus, "0.000", bg);
            Cel  (ws, row, 51, t.Material,     bg, fontSize: 8);

            ws.Row(row).Height = 22;
        }

        // ───────────────────────────────────────────────────────────────────────────
        // ABA AUXILIAR — TABELA DE MANNING (seção parcial circular)
        // ───────────────────────────────────────────────────────────────────────────

        private static void CriarAbaTabelaManning(ExcelPackage pkg)
        {
            ExcelWorksheet ws = pkg.Workbook.Worksheets.Add("Tabela Manning");
            ws.View.ShowGridLines = false;

            ws.Column(1).Width = 10;
            ws.Column(2).Width = 12;
            ws.Column(3).Width = 12;
            ws.Column(4).Width = 12;
            ws.Column(5).Width = 12;

            MesclarCabecalho(ws, 1, 1, 5, "TABELA AUXILIAR — SEÇÃO CIRCULAR PARCIALMENTE CHEIA (MANNING)", CorHidraulica);
            ws.Row(1).Height = 22;

            MesclarCabecalho(ws, 3, 1, 1, "Y/D",       CorHidraulica);
            MesclarCabecalho(ws, 3, 2, 2, "F = q/Qp",  CorHidraulica);
            MesclarCabecalho(ws, 3, 3, 3, "v/Vp",      CorHidraulica);
            MesclarCabecalho(ws, 3, 4, 4, "Rh/D",      CorHidraulica);
            MesclarCabecalho(ws, 3, 5, 5, "A/D²",      CorHidraulica);
            ws.Row(3).Height = 22;

            int row = 4;
            for (int i = 1; i <= 100; i++)
            {
                double yd    = i / 100.0;
                double theta = 2 * Math.Acos(Math.Max(-1, Math.Min(1, 1 - 2 * yd)));
                double aRel  = (theta - Math.Sin(theta)) / 8.0;      // A/D²
                double rhRel = theta > 0
                    ? (1 - Math.Sin(theta) / theta) / 4.0 : 0;       // Rh/D
                double fRel  = (theta - Math.Sin(theta)) / (2 * Math.PI)
                              * Math.Pow(theta > 0 ? (theta - Math.Sin(theta)) / theta : 0, 2.0 / 3.0);
                double vRel  = Math.Pow(rhRel * 4, 2.0 / 3.0);       // (4·Rh/D)^(2/3) = (Rh/Rh_p)^(2/3)

                CelNum(ws, row, 1, yd,    "0.00",   Color.White);
                CelNum(ws, row, 2, fRel,  "0.0000", Color.White);
                CelNum(ws, row, 3, vRel,  "0.0000", Color.White);
                CelNum(ws, row, 4, rhRel, "0.0000", Color.White);
                CelNum(ws, row, 5, aRel,  "0.0000", Color.White);
                ws.Row(row).Height = 14;
                row++;
            }
        }

        // ───────────────────────────────────────────────────────────────────────────
        // HELPERS — FORMATAÇÃO EPPlus
        // ───────────────────────────────────────────────────────────────────────────

        private static void EscreverTituloHDT(ExcelWorksheet ws, int row, int c1, int c2,
            string texto, float fontSize, bool bold)
        {
            var range = ws.Cells[row, c1, row, c2];
            range.Merge = true;
            range.Value = texto;
            range.Style.Font.Bold = bold;
            range.Style.Font.Size = fontSize;
            range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            range.Style.VerticalAlignment   = ExcelVerticalAlignment.Center;
            range.Style.Border.BorderAround(ExcelBorderStyle.Medium);
            ws.Row(row).Height = fontSize + 8;
        }

        private static void MesclarCabecalho(ExcelWorksheet ws, int row, int c1, int c2,
            string texto, Color bg)
        {
            var range = ws.Cells[row, c1, row, c2];
            if (c1 != c2) range.Merge = true;
            range.Value = texto;
            range.Style.Font.Bold = true;
            range.Style.Font.Size = 8;
            range.Style.WrapText = true;
            range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            range.Style.VerticalAlignment   = ExcelVerticalAlignment.Center;
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(bg);
            range.Style.Border.Left.Style   = ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style  = ExcelBorderStyle.Thin;
            range.Style.Border.Top.Style    = ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
        }

        private static void Cel(ExcelWorksheet ws, int row, int col, string value,
            Color bg, float fontSize = 9, bool fontBold = false)
        {
            var c = ws.Cells[row, col];
            c.Value = value ?? "";
            c.Style.Font.Size = fontSize;
            c.Style.Font.Bold = fontBold;
            c.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            c.Style.VerticalAlignment   = ExcelVerticalAlignment.Center;
            c.Style.Fill.PatternType = ExcelFillStyle.Solid;
            c.Style.Fill.BackgroundColor.SetColor(bg);
            c.Style.Border.Left.Style   = ExcelBorderStyle.Hair;
            c.Style.Border.Right.Style  = ExcelBorderStyle.Hair;
            c.Style.Border.Top.Style    = ExcelBorderStyle.Hair;
            c.Style.Border.Bottom.Style = ExcelBorderStyle.Hair;
        }

        private static void CelNum(ExcelWorksheet ws, int row, int col, double? value,
            string fmt, Color bg)
        {
            var c = ws.Cells[row, col];
            c.Style.Font.Size = 9;
            c.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            c.Style.VerticalAlignment   = ExcelVerticalAlignment.Center;
            c.Style.Fill.PatternType = ExcelFillStyle.Solid;
            c.Style.Fill.BackgroundColor.SetColor(bg);
            c.Style.Border.Left.Style   = ExcelBorderStyle.Hair;
            c.Style.Border.Right.Style  = ExcelBorderStyle.Hair;
            c.Style.Border.Top.Style    = ExcelBorderStyle.Hair;
            c.Style.Border.Bottom.Style = ExcelBorderStyle.Hair;
            if (value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value))
            {
                c.Value = value.Value;
                c.Style.Numberformat.Format = fmt;
            }
        }

        private static void CelInput(ExcelWorksheet ws, int row, int col, double? value, Color bg)
        {
            var c = ws.Cells[row, col];
            c.Style.Font.Size = 9;
            c.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            c.Style.Fill.PatternType = ExcelFillStyle.Solid;
            c.Style.Fill.BackgroundColor.SetColor(bg);
            c.Style.Border.Left.Style   = ExcelBorderStyle.Hair;
            c.Style.Border.Right.Style  = ExcelBorderStyle.Hair;
            c.Style.Border.Top.Style    = ExcelBorderStyle.Hair;
            c.Style.Border.Bottom.Style = ExcelBorderStyle.Hair;
            if (value.HasValue)
            {
                c.Value = value.Value;
                c.Style.Numberformat.Format = "0.00";
            }
        }

        private static string TipoStr(TipoSecao tipo) => tipo switch
        {
            TipoSecao.Circular   => "TUBO",
            TipoSecao.Retangular => "CANALETA",
            _                    => "ESTRUTURA"
        };

        private static string FormatSecao(DadosDispositivo d)
        {
            if (d.Tipo == TipoSecao.Circular && d.Diametro.HasValue)
                return $"DN {d.Diametro.Value * 1000:0}";
            if (d.Tipo == TipoSecao.Retangular && d.Largura.HasValue && d.Altura.HasValue)
                return $"{d.Largura.Value:0.000} × {d.Altura.Value:0.000}";
            return "";
        }

        private static double? AreaSecao(DadosDispositivo d)
        {
            if (d.Tipo == TipoSecao.Circular && d.Diametro.HasValue)
                return Math.PI * d.Diametro.Value * d.Diametro.Value / 4.0;
            if (d.Tipo == TipoSecao.Retangular && d.Largura.HasValue && d.Altura.HasValue)
                return d.Largura.Value * d.Altura.Value;
            return null;
        }

        private static double? RaioHidraulico(DadosDispositivo d)
        {
            if (d.Tipo == TipoSecao.Circular && d.Diametro.HasValue)
                return d.Diametro.Value / 4.0;
            if (d.Tipo == TipoSecao.Retangular && d.Largura.HasValue && d.Altura.HasValue)
            {
                double a = d.Largura.Value * d.Altura.Value;
                double p = d.Largura.Value + 2 * d.Altura.Value;
                return p > 0 ? a / p : (double?)null;
            }
            return null;
        }

        // ───────────────────────────────────────────────────────────────────────────
        // LEITURA SOLIDOS — strings, doubles e pontos de geometria
        // ───────────────────────────────────────────────────────────────────────────

        private static string LerString(ObjectId id, params string[] nomes)
        {
            foreach (string nome in nomes)
            {
                try
                {
                    Type tipo = null;
                    object val = SolidosAPI.GetNodeParam(id, nome, null, ref tipo);
                    if (val == null) continue;
                    string s = Convert.ToString(val, Inv)?.Trim();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
                catch { }
            }
            return null;
        }

        private static double? LerDouble(ObjectId id, params string[] nomes)
        {
            foreach (string nome in nomes)
            {
                try
                {
                    Type tipo = null;
                    object val = SolidosAPI.GetNodeParam(id, nome, null, ref tipo);
                    if (val == null) continue;
                    if (val is double d && !double.IsNaN(d)) return d;
                    if (val is float  f && !float.IsNaN(f))  return (double)f;
                    if (val is int i)  return i;
                    if (val is long l) return l;
                    string s = Convert.ToString(val, Inv);
                    if (double.TryParse(s, NumberStyles.Any, Inv, out double r) && !double.IsNaN(r))
                        return r;
                    if (double.TryParse(s, NumberStyles.Any, PtBr, out double r2) && !double.IsNaN(r2))
                        return r2;
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Lê um parâmetro do SOLIDOS que represente um ponto 3D (StartPoint, EndPoint, Location).
        /// O SOLIDOS retorna um objeto cujo tipo expõe propriedades X/Y/Z — leitura via reflexão
        /// para evitar dependência forte do tipo concreto (GeometryPoint na DLL do SOLIDOS).
        /// </summary>
        private static Ponto3D LerPonto(ObjectId id, string nome)
        {
            try
            {
                Type tipo = null;
                object val = SolidosAPI.GetNodeParam(id, nome, null, ref tipo);
                if (val == null) return null;

                double? x = LerPropDouble(val, "X");
                double? y = LerPropDouble(val, "Y");
                double? z = LerPropDouble(val, "Z");
                if (!x.HasValue && !y.HasValue && !z.HasValue) return null;

                return new Ponto3D { X = x ?? 0, Y = y ?? 0, Z = z ?? 0 };
            }
            catch { return null; }
        }

        private static double? LerPropDouble(object obj, string nome)
        {
            try
            {
                PropertyInfo pi = obj.GetType().GetProperty(nome,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (pi == null) return null;
                object v = pi.GetValue(obj);
                if (v == null) return null;
                if (v is double d) return d;
                if (v is float f)  return (double)f;
                if (v is int i)    return i;
                if (double.TryParse(Convert.ToString(v, Inv), NumberStyles.Any, Inv, out double r))
                    return r;
            }
            catch { }
            return null;
        }

        // ───────────────────────────────────────────────────────────────────────────
        // TIPOS DE DADOS
        // ───────────────────────────────────────────────────────────────────────────

        private enum TipoSecao { Circular, Retangular, Estrutura }

        private sealed class Ponto3D
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
        }

        private sealed class DadosDispositivo
        {
            // Identificação
            public string Handle       { get; set; }
            public string Nome         { get; set; }
            public string Familia      { get; set; }
            public string Sistema      { get; set; }
            public string SubSistema   { get; set; }
            public string Material     { get; set; }
            public string Catalogo     { get; set; }
            public string Tag          { get; set; }
            public string DispMontante { get; set; }
            public string DispJusante  { get; set; }
            public TipoSecao Tipo      { get; set; }

            // Conectividade — handles dos nós (estruturas) ligados pelo tubo.
            // Para nós (CAIXA/PV), ambos ficam vazios — descobrimos os tubos
            // que entram/saem pela varredura de todos os tubos da rede.
            public string MontanteHandle { get; set; }
            public string JusanteHandle  { get; set; }

            // Geometria
            public double? StartZ      { get; set; }
            public double? EndZ        { get; set; }
            public double? LocationZ   { get; set; }
            public double? PlanLength  { get; set; }

            // Cotas derivadas
            public double? CotaTerreno   { get; set; }
            public double? CotaFundoMont { get; set; }
            public double? CotaFundoJus  { get; set; }

            // Dimensões da seção
            public double? Diametro { get; set; }
            public double? Largura  { get; set; }
            public double? Altura   { get; set; }
            public double? Base     { get; set; }

            // Hidráulica
            public double? Declividade { get; set; }
            public double? Comprimento { get; set; }
            public double? ManningN    { get; set; }
            public double? HazenW      { get; set; }
            public double? Darcy       { get; set; }

            // Resultados do dimensionamento
            public double? QDim    { get; set; }
            public double? VCalc   { get; set; }
            public double? YLamina { get; set; }
            public double? YD      { get; set; }
            public double? Folga   { get; set; }

            // Verificação (seção plena)
            public double? QPlena  { get; set; }
            public double? VPlena  { get; set; }
        }
    }
}
