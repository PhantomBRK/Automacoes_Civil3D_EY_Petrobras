using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using SOLIDOS;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutomacoesCivil3D
{
    // ============================================================================
    // DIMENSIONAMENTO DE REDE DE DRENAGEM POR GRAVIDADE (versão simples e robusta).
    //
    // Filosofia (decisão do usuário após várias iterações): NÃO recombinar vazões,
    // NÃO mexer em CTop/início-fim de plano, NÃO regra de funil dominante/perdedor.
    // Cada tubo é dimensionado pela vazão que o PRÓPRIO SOLIDOS já calculou nele
    // (HydraulicSection.Qcalc, fallback Qc). A rotina só LÊ vazão e ESCREVE
    // diâmetro + declividade + cotas. Idempotente: rodar N vezes dá o mesmo
    // resultado, pois nunca altera a fonte de vazão.
    //
    // Passos:
    //   1. Selecionar âncora (dispositivo de jusante).
    //   2. Janela de regras (faixas de i, V, Y/D, catálogo, recobrimento).
    //   3. BFS a montante do âncora → mapeia tubos e nós.
    //   4. Ordena montante→jusante (Kahn) — garante que o nó de origem de cada tubo
    //      já teve sua cota de saída resolvida antes de o tubo ser processado.
    //   5. Para cada tubo: Q = vazão do SOLIDOS; (D,i) = HidraulicaSolidos;
    //      cota desce do nó montante (zJus = zMont - i*L); grava no SOLIDOS.
    //   6. Relatório.
    // ============================================================================
    public class SolidosDimensionarRedeJusante
    {
        public const int GuardMax = 20000;

        // Vazão de projeto do tubo (m³/s), em GRUPOS de prioridade. A leitura pega o
        // PRIMEIRO grupo cujo valor > 0 (maior entre Ini/Fim dentro do grupo). NÃO pega
        // o maior global — Qc é a vazão CRÍTICA (capacidade), sempre maior que a de
        // escoamento; se entrasse na disputa, todo tubo dimensionaria pra capacidade do
        // DN atual e nada mudaria. Ordem: Qesc (escoamento real) → Qcalc → Q genérico.
        // Qc fica de fora de propósito.
        public static readonly string[][] VazaoTuboGrupos =
        {
            new[] { "HCalcIni.Qesc",  "HCalcFim.Qesc"  },
            new[] { "HCalcIni.Qcalc", "HCalcFim.Qcalc" },
            new[] { "Qesc", "Qcalc" },
            new[] { "FlowRate", "Q", "Vazao" }
        };

        public static readonly string[] FamilyNameCandidates =
        {
            "FamilyName", "Family", "Familia", "NomeFamilia", "PartFamilyName"
        };

        public static readonly string[] SurfaceElevCandidates =
        {
            "SurfaceElevation", "RimElevation", "TopElevation", "GradeElevation"
        };

        public static readonly string[] ManningCandidates =
        {
            "ACMan", "Manning", "Roughness", "ChannelRoughness", "FrictionFactor"
        };

        [CommandMethod("SOL_DIMENSIONAR_REDE_POR_JUSANTE")]
        public void DimensionarRede()
        {
            Editor ed = Manager.DocEditor;

            ObjectId anchorId = SelecionarAncora(ed);
            if (anchorId.IsNull)
            {
                ed.WriteMessage("\nNada selecionado.");
                return;
            }

            // --- Janela de regras (persistida em %APPDATA%) ---
            DimensionamentoConfig cfg = DimensionamentoConfig.Carregar();
            var cfgWin = new DimensionamentoConfigWindow(cfg);
            try { Application.ShowModalWindow(cfgWin); }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[UI] Falha ao abrir janela de regras: {ex.Message}");
                return;
            }
            if (!cfgWin.Confirmado)
            {
                ed.WriteMessage("\nCancelado pelo usuário.");
                return;
            }
            cfg = cfgWin.Config;

            // Catálogo efetivo: interseção do configurado com o suportado pelo .sbd.
            List<double> dnsM = new List<double>();
            foreach (int dn in cfg.CatalogoDNsMm)
                if (CatalogoTuboPadrao.DNsMm.Contains(dn))
                    dnsM.Add(dn / 1000.0);
            if (dnsM.Count == 0)
            {
                ed.WriteMessage("\n[CFG] Nenhum DN do catálogo é suportado pelo .sbd Petrobras.");
                return;
            }

            RegrasDimensionamento regras = cfg.ParaRegras();
            var avisos = new List<string>();
            var diag = new Diagnostico();
            var linhas = new List<DimensionamentoLinhaRelatorio>();

            ProgressMeter pm = null;
            try
            {
                pm = new ProgressMeter();
                pm.Start("SOLIDOS: Dimensionando rede a montante do âncora...");
                pm.SetLimit(3);

                var grafo = new Grafo();
                grafo.MapearViaBFS(anchorId, avisos, diag);
                pm.MeterProgress();

                var ordem = grafo.OrdenarMontanteParaJusante(avisos);
                pm.MeterProgress();

                int ok = grafo.Dimensionar(ordem, regras, dnsM, cfg.ManningDefault,
                                           cfg.RecobrimentoMinM, avisos, diag, linhas);
                SolidosAPI.DocCommit();
                pm.MeterProgress();

                ed.WriteMessage($"\nOK. Tubos dimensionados: {ok} de {grafo.QtdTubos}.");
                diag.Imprimir(ed);

                try { pm.Stop(); pm = null; } catch { }

                string header = $"Dimensionados: {ok} de {grafo.QtdTubos}.  " +
                                $"Nós={diag.NosTotal}  Tubos={diag.TubosTotal}.";
                var relWin = new DimensionamentoRelatorioWindow(linhas, header);
                try { Application.ShowModalWindow(relWin); }
                catch (System.Exception ex) { ed.WriteMessage($"\n[UI] Falha relatório: {ex.Message}"); }
            }
            catch (SolidosException sx) { ed.WriteMessage($"\n[SOLIDOS] {sx.Message}"); }
            catch (System.Exception ex) { ed.WriteMessage($"\n[ERRO] {ex.Message}"); }
            finally { try { pm?.Stop(); } catch { } }
        }

        // ------------------------------------------------------------------ seleção
        public static ObjectId SelecionarAncora(Editor ed)
        {
            var peo = new PromptEntityOptions("\nSelecione o DISPOSITIVO DE JUSANTE (âncora): ");
            peo.SetRejectMessage("\nSelecione um nó SOLIDOS.");
            PromptEntityResult per = ed.GetEntity(peo);
            return per.Status == PromptStatus.OK ? per.ObjectId : ObjectId.Null;
        }

        // ============================================================== DIAGNÓSTICO
        public class Diagnostico
        {
            public int NosTotal;
            public int TubosTotal;
            public int TubosSemVazao;
            public int TubosSemSolucao;
            public int TubosLenZero;
            public int TubosFalhaGravacao;
            public int TubosOK;

            public void Imprimir(Editor ed)
            {
                ed.WriteMessage("\n--- Diagnóstico ---");
                ed.WriteMessage($"\n  Nós: {NosTotal}   Tubos: {TubosTotal}");
                ed.WriteMessage($"\n    sem vazão (Q=0 no SOLIDOS):  {TubosSemVazao}");
                ed.WriteMessage($"\n    sem (D,i) viável:            {TubosSemSolucao}");
                ed.WriteMessage($"\n    comprimento ~0:              {TubosLenZero}");
                ed.WriteMessage($"\n    falha ao gravar catálogo:    {TubosFalhaGravacao}");
                ed.WriteMessage($"\n    OK (dimensionados):          {TubosOK}");
            }
        }

        // ================================================================== MODELO
        public class Tubo
        {
            public ObjectId Id;
            public ObjectId InPart;       // nó a montante
            public ObjectId OutPart;      // nó a jusante
            public GeometryPoint StartPoint;
            public GeometryPoint EndPoint;
            public double? Vazao;         // m³/s, lida do SOLIDOS
            public int? DNmmAnterior;
        }

        public class No
        {
            public ObjectId Id;
            public GeometryPoint Location;
            public double? SurfaceElevation;   // terreno (só p/ aviso de recobrimento)
            public string FamilyName;
            public List<Tubo> Entrando = new List<Tubo>();
            public List<Tubo> Saindo = new List<Tubo>();
        }

        // =================================================================== GRAFO
        public class Grafo
        {
            public Dictionary<ObjectId, No> Nos = new Dictionary<ObjectId, No>();
            public Dictionary<ObjectId, Tubo> Tubos = new Dictionary<ObjectId, Tubo>();
            public int QtdTubos => Tubos.Count;

            // BFS a montante do âncora: descobre vizinhos por ConnectedDevices, segue só
            // os tubos cujo OutPart é o nó atual (= tubo está a montante dele).
            public void MapearViaBFS(ObjectId anchorId, List<string> avisos, Diagnostico diag)
            {
                No ancora = LerOuCriarNo(anchorId);
                if (ancora.Location == null)
                {
                    avisos.Add($"[BFS] Âncora {anchorId.Handle} sem Location. Abortando.");
                    return;
                }

                var fila = new Queue<ObjectId>();
                var visit = new HashSet<ObjectId>();
                fila.Enqueue(anchorId);
                visit.Add(anchorId);

                int guard = 0;
                while (fila.Count > 0)
                {
                    if (++guard > GuardMax) { avisos.Add("[BFS] Guard atingido."); break; }
                    ObjectId nodeId = fila.Dequeue();
                    No no = Nos[nodeId];

                    foreach (ObjectId pipeId in ConnectedDevices(nodeId))
                    {
                        Tubo t = LerOuCriarTubo(pipeId);
                        if (t == null) continue;
                        if (t.OutPart != nodeId) continue;   // só tubos a montante deste nó

                        if (!no.Entrando.Contains(t)) no.Entrando.Add(t);

                        ObjectId upId = t.InPart;
                        if (upId.IsNull) continue;
                        No up = LerOuCriarNo(upId);
                        if (!up.Saindo.Contains(t)) up.Saindo.Add(t);

                        if (!visit.Contains(upId)) { visit.Add(upId); fila.Enqueue(upId); }
                    }
                }

                diag.NosTotal = Nos.Count;
                diag.TubosTotal = Tubos.Count;
            }

            // Kahn: cabeceiras (sem tubo entrando) → desce até o âncora.
            public List<Tubo> OrdenarMontanteParaJusante(List<string> avisos)
            {
                var inDeg = new Dictionary<ObjectId, int>();
                foreach (var kv in Nos) inDeg[kv.Key] = kv.Value.Entrando.Count;

                var q = new Queue<ObjectId>();
                foreach (var kv in inDeg) if (kv.Value == 0) q.Enqueue(kv.Key);

                var ordem = new List<Tubo>();
                var emitidos = new HashSet<ObjectId>();
                int guard = 0;
                while (q.Count > 0)
                {
                    if (++guard > GuardMax * 2) { avisos.Add("[TOPO] Guard/ciclo."); break; }
                    No no = Nos[q.Dequeue()];
                    foreach (Tubo t in no.Saindo)
                    {
                        if (emitidos.Add(t.Id)) ordem.Add(t);
                        if (t.OutPart.IsNull || !inDeg.ContainsKey(t.OutPart)) continue;
                        if (--inDeg[t.OutPart] == 0) q.Enqueue(t.OutPart);
                    }
                }

                if (ordem.Count < Tubos.Count)
                    avisos.Add($"[TOPO] {Tubos.Count - ordem.Count} tubo(s) fora da ordem (ciclo/desconexão).");
                return ordem;
            }

            // Dimensiona cada tubo pela vazão do SOLIDOS; propaga COTAS montante→jusante.
            public int Dimensionar(
                List<Tubo> ordem, RegrasDimensionamento regras, List<double> dnsM,
                double manningDefault, double recobrimentoMin,
                List<string> avisos, Diagnostico diag, List<DimensionamentoLinhaRelatorio> linhas)
            {
                int ok = 0;

                foreach (Tubo t in ordem)
                {
                    No noMont = !t.InPart.IsNull && Nos.TryGetValue(t.InPart, out var nm) ? nm : null;
                    No noJus = !t.OutPart.IsNull && Nos.TryGetValue(t.OutPart, out var nj) ? nj : null;

                    // COTA DE PARTIDA (decisão do usuário): vem do Z ATUAL do próprio tubo,
                    // não de propriedade do nó. A montante é a ponta MAIS ALTA do tubo (a
                    // água desce); preserva o nivelamento que o projetista já fez. A rotina
                    // só recalcula a queda pela declividade dimensionada.
                    double zStart = t.StartPoint.Z;
                    double zEnd = t.EndPoint.Z;
                    bool startIsMont = zStart >= zEnd;
                    double zMont = startIsMont ? zStart : zEnd;

                    DimensionamentoLinhaRelatorio Linha(string status, SolucaoTubo sol,
                        double? zM, double? zJ, int? dnAplic)
                    {
                        return new DimensionamentoLinhaRelatorio
                        {
                            Handle = t.Id.Handle.ToString(),
                            NoMontanteFamilia = noMont?.FamilyName ?? "",
                            NoMontanteHandle = noMont != null ? noMont.Id.Handle.ToString() : "",
                            NoJusanteFamilia = noJus?.FamilyName ?? "",
                            NoJusanteHandle = noJus != null ? noJus.Id.Handle.ToString() : "",
                            QLs = t.Vazao.HasValue ? t.Vazao.Value * 1000.0 : (double?)null,
                            DNmmAnterior = t.DNmmAnterior,
                            DNmm = dnAplic ?? (sol != null ? (int?)CatalogoTuboPadrao.DnMmMaisProximo(sol.D) : null),
                            SlopePct = sol != null ? sol.Slope * 100.0 : (double?)null,
                            VMs = sol?.V,
                            YDPct = sol != null ? sol.YD * 100.0 : (double?)null,
                            ZMontante = zM,
                            ZJusante = zJ,
                            ComprimentoM = PlanLen(t.StartPoint, t.EndPoint),
                            FaixaIdeal = sol != null && sol.FaixaIdeal,
                            Status = status
                        };
                    }

                    // Vazão do tubo (lida do SOLIDOS).
                    if (!t.Vazao.HasValue || t.Vazao.Value <= 0)
                    {
                        diag.TubosSemVazao++;
                        linhas.Add(Linha("pulado: vazão = 0 no SOLIDOS", null, null, null, null));
                        continue;
                    }

                    double n = LerManning(t.Id, manningDefault);
                    SolucaoTubo sol2 = HidraulicaSolidos.DimensionarCircular(t.Vazao.Value, n, dnsM, regras);
                    if (sol2 == null)
                    {
                        diag.TubosSemSolucao++;
                        avisos.Add($"[DIM] Tubo {t.Id.Handle}: sem (D,i) viável p/ Q={t.Vazao.Value*1000:F1} L/s, n={n:F3}.");
                        linhas.Add(Linha($"pulado: sem (D,i) viável (n={n:F3})", null, null, null, null));
                        continue;
                    }

                    double planLen = PlanLen(t.StartPoint, t.EndPoint);
                    if (planLen < 1e-6)
                    {
                        diag.TubosLenZero++;
                        linhas.Add(Linha("pulado: comprimento ~0", sol2, null, null, null));
                        continue;
                    }

                    // A ponta de jusante DESCE da montante pela declividade dimensionada.
                    double zJus = zMont - sol2.Slope * planLen;

                    // Recobrimento é só AVISO (não trava).
                    if (noMont != null && noMont.SurfaceElevation.HasValue)
                    {
                        double topo = zMont + sol2.D;
                        double limite = noMont.SurfaceElevation.Value - recobrimentoMin;
                        if (topo > limite)
                            avisos.Add($"[DIM] Tubo {t.Id.Handle}: recobrimento insuf. (topo {topo:F3} > {limite:F3}).");
                    }

                    // Grava Catalogo + Z's numa só chamada. zMont na ponta MAIS ALTA do
                    // tubo (montante), zJus na outra — respeitando a orientação real do
                    // StartPoint/EndPoint deste tubo (nem sempre StartPoint = montante).
                    int dnMm = CatalogoTuboPadrao.DnMmMaisProximo(sol2.D);
                    var dic = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    dic["Catalogo"] = dnMm.ToString(CultureInfo.InvariantCulture);
                    if (startIsMont)
                    {
                        dic["StartPoint"] = new GeometryPoint(t.StartPoint.X, t.StartPoint.Y, zMont);
                        dic["EndPoint"]   = new GeometryPoint(t.EndPoint.X,   t.EndPoint.Y,   zJus);
                    }
                    else
                    {
                        dic["StartPoint"] = new GeometryPoint(t.StartPoint.X, t.StartPoint.Y, zJus);
                        dic["EndPoint"]   = new GeometryPoint(t.EndPoint.X,   t.EndPoint.Y,   zMont);
                    }
                    SolidosAPI.SetNodeParams(t.Id, dic);

                    int? dnLido = CatalogoTuboPadrao.LerCatalogoMm(t.Id);
                    if (dnLido != dnMm)
                    {
                        diag.TubosFalhaGravacao++;
                        avisos.Add($"[DIM] Tubo {t.Id.Handle}: catálogo pedido {dnMm}, lido {(dnLido?.ToString() ?? "?")}.");
                    }

                    diag.TubosOK++;
                    ok++;
                    linhas.Add(Linha("OK", sol2, zMont, zJus, dnMm));
                }
                return ok;
            }

            // ---------------------------------------------------------- leitura nós
            public No LerOuCriarNo(ObjectId id)
            {
                if (Nos.TryGetValue(id, out No e)) return e;
                var no = new No
                {
                    Id = id,
                    Location = GetParam<GeometryPoint>(id, "Location"),
                    SurfaceElevation = LerPrimeiroDouble(id, SurfaceElevCandidates),
                    FamilyName = LerPrimeiroString(id, FamilyNameCandidates)
                };
                Nos[id] = no;
                return no;
            }

            public Tubo LerOuCriarTubo(ObjectId id)
            {
                if (Tubos.TryGetValue(id, out Tubo e)) return e;
                ObjectId inPart = GetParam<ObjectId>(id, "InPart");
                ObjectId outPart = GetParam<ObjectId>(id, "OutPart");
                GeometryPoint sp = GetParam<GeometryPoint>(id, "StartPoint");
                GeometryPoint ep = GetParam<GeometryPoint>(id, "EndPoint");
                if (sp == null || ep == null) return null;

                var t = new Tubo
                {
                    Id = id,
                    InPart = inPart,
                    OutPart = outPart,
                    StartPoint = sp,
                    EndPoint = ep,
                    Vazao = LerVazaoPorGrupos(id, VazaoTuboGrupos),
                    DNmmAnterior = CatalogoTuboPadrao.LerCatalogoMm(id)
                };
                Tubos[id] = t;
                return t;
            }

            public static double LerManning(ObjectId tuboId, double padrao)
            {
                double? n = LerPrimeiroDouble(tuboId, ManningCandidates);
                return n.HasValue && n.Value > 0 ? n.Value : padrao;
            }

            // ------------------------------------------------------- helpers SOLIDOS
            public static List<ObjectId> ConnectedDevices(ObjectId nodeId)
            {
                var result = new List<ObjectId>();
                object raw = GetParam<object>(nodeId, "ConnectedDevices");
                if (raw == null) return result;
                if (raw is List<ObjectId> l) { result.AddRange(l); return result; }
                if (raw is ObjectId[] arr) { result.AddRange(arr); return result; }
                if (raw is ObjectIdCollection col) { foreach (ObjectId o in col) result.Add(o); return result; }
                if (raw is System.Collections.IEnumerable en)
                    foreach (object o in en) if (o is ObjectId oid) result.Add(oid);
                return result;
            }

            public static T GetParam<T>(ObjectId id, string prop)
            {
                try
                {
                    Type t = null;
                    object v = SolidosAPI.GetNodeParam(id, prop, null, ref t);
                    if (v is T tv) return tv;
                    return default;
                }
                catch { return default; }
            }

            // Vazão por GRUPOS de prioridade: retorna o 1º grupo cujo valor > 0 (maior
            // entre os nomes do grupo — cobre Ini/Fim). Garante que Qesc vence Qcalc, e
            // que Qc (capacidade) nunca entra na disputa por estar fora dos grupos.
            public static double? LerVazaoPorGrupos(ObjectId id, string[][] grupos)
            {
                foreach (string[] grupo in grupos)
                {
                    double melhor = 0; bool achou = false;
                    foreach (string nome in grupo)
                    {
                        double? d = LerDouble(id, nome);
                        if (d.HasValue && d.Value > melhor) { melhor = d.Value; achou = true; }
                    }
                    if (achou && melhor > 0) return melhor;
                }
                return null;
            }

            // Primeiro valor não-nulo entre os candidatos.
            public static double? LerPrimeiroDouble(ObjectId id, IEnumerable<string> nomes)
            {
                foreach (string nome in nomes)
                {
                    double? d = LerDouble(id, nome);
                    if (d.HasValue) return d;
                }
                return null;
            }

            private static double? LerDouble(ObjectId id, string nome)
            {
                try
                {
                    Type t = null;
                    object v = SolidosAPI.GetNodeParam(id, nome, null, ref t);
                    if (v == null) return null;
                    if (v is double d && !double.IsNaN(d)) return d;
                    if (v is int i) return i;
                    if (v is string s &&
                        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double dv))
                        return dv;
                }
                catch { }
                return null;
            }

            public static string LerPrimeiroString(ObjectId id, IEnumerable<string> nomes)
            {
                foreach (string nome in nomes)
                {
                    try
                    {
                        Type t = null;
                        object v = SolidosAPI.GetNodeParam(id, nome, null, ref t);
                        if (v == null) continue;
                        string s = v as string ?? v.ToString();
                        if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                    }
                    catch { }
                }
                return null;
            }
        }

        public static double PlanLen(GeometryPoint a, GeometryPoint b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
