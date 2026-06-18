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
    // SOL_DIMENSIONAR_DRENAGEM — dimensionamento de rede de drenagem por gravidade
    // replicando a LÓGICA NATIVA do SOLIDOS (decompilação: Flow Analysis / FormQCap),
    // com auto-seleção por MENOR ESCAVAÇÃO e dois regimes (pluvial + incêndio).
    //
    // O SOLIDOS não tem otimizador: o "dimensionamento" é a matriz DN×declividade do
    // FormQCap, onde cada célula mostra V e lâmina% calculadas por Manning, e o
    // engenheiro escolhe. Esta rotina AUTOMATIZA essa escolha.
    //
    // Princípios (todos validados na decompilação):
    //  • Vazão de projeto = a que o SOLIDOS já calculou no tubo. NÃO recalcula nem
    //    muta vazão (foi o que quebrou a rotina anterior).
    //      - Pluvial  (Qini) = tubo.HCalcIni.Qesc   (recorrência Tr)
    //      - Incêndio (Qfim) = tubo.HCalcFim.Qesc    (recorrência Trv / regime crítico)
    //    Sem Qfim → dimensiona só pelo pluvial.
    //  • Hidráulica = Manning circular (HidraulicaCircularDren), idêntico ao motor
    //    do SOLIDOS: Q = A·(A/P)^(2/3)·√i/n; resolve a lâmina; V = Q/A; Y/D = h/D.
    //  • Critérios (o MESMO i tem que passar nos dois regimes):
    //      Pluvial : Vmin ≤ V ≤ Vmax , Y/D ≤ YDmaxP
    //      Incêndio: V ≤ VmaxI        , Y/D ≤ YDmaxI   (sem Vmin)
    //      Ambos dentro da faixa de declividade [SlopeMin, SlopeMax].
    //  • Seleção = MENOR ESCAVAÇÃO: o menor DN viável e, nele, a menor declividade
    //    válida (DN menor exige menos cobertura e i menor desce menos — ambos reduzem
    //    profundidade; e DN menor admite i mais baixo mantendo V).
    //  • Cotas (subir tudo, sem exutório fixo): cabeceira ancorada o mais raso
    //    possível (invert = terreno − recobrimento − D_externo, D_externo = D+2·Parede),
    //    limitada por MaxElevAllowed do nó. Desce o mínimo: zJus = zMont − i·L.
    //    Nas junções/caixas a propagação segue o PONTO BAIXO (invert de saída = menor
    //    invert que chega) e o builder Petrobras recalcula sozinho o fundo da caixa
    //    (SumpElevation) e a cota das conexões (ptTuboZ) no rebuild.
    //  • Junções: seta SetarReferencia="PONTO BAIXO" (configurável) — alinha ao tubo
    //    mais baixo, dispensando a correção manual de conexões.
    // ============================================================================
    public class SolDimensionarDrenagem
    {
        public const int GuardMax = 20000;

        public static readonly string[] FamilyNameCandidates =
            { "FamilyName", "Family", "Familia", "NomeFamilia", "PartFamilyName" };
        public static readonly string[] SurfaceElevCandidates =
            { "SurfaceElevation", "RimElevation", "TopElevation", "GradeElevation" };
        public static readonly string[] ManningCandidates =
            { "ACMan", "Manning", "Roughness", "ChannelRoughness", "FrictionFactor" };
        public static readonly string[] ParedeCandidates =
            { "Parede", "EspessuraParede", "WallThickness", "Espessura" };

        [CommandMethod("SOL_DIMENSIONAR_DRENAGEM")]
        public void DimensionarDrenagem()
        {
            Editor ed = Manager.DocEditor;

            ObjectId anchorId = SelecionarAncora(ed);
            if (anchorId.IsNull) { ed.WriteMessage("\nNada selecionado."); return; }

            DimensionamentoConfig cfg = DimensionamentoConfig.Carregar();

            // --- Janela de parâmetros (regimes + faixa dinâmica de DNs) ---
            var cfgWin = new DimensionamentoDrenagemConfigWindow(cfg);
            try { Application.ShowModalWindow(cfgWin); }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[UI] Falha ao abrir janela de regras: {ex.Message}");
                return;
            }
            if (!cfgWin.Confirmado) { ed.WriteMessage("\nCancelado pelo usuário."); return; }
            cfg = cfgWin.Config;

            // Catálogo efetivo (interseção do configurado com o suportado pelo .sbd).
            List<double> dnsM = cfg.CatalogoDNsMm
                .Where(dn => CatalogoTuboPadrao.DNsMm.Contains(dn))
                .OrderBy(dn => dn).Select(dn => dn / 1000.0).ToList();
            if (dnsM.Count == 0)
            {
                ed.WriteMessage("\n[CFG] Nenhum DN do catálogo é suportado pelo .sbd Petrobras.");
                return;
            }

            EcoarRegras(ed, cfg, dnsM);

            var avisos = new List<string>();
            var diag = new Diagnostico();
            var linhas = new List<DimensionamentoLinhaRelatorio>();

            ProgressMeter pm = null;
            try
            {
                pm = new ProgressMeter();
                pm.Start("SOLIDOS: Dimensionando drenagem a montante do âncora...");
                pm.SetLimit(3);

                // CRÍTICO: força a rede a recalcular COM verificação antes de ler as
                // vazões. HCalcFim (Qfim/incêndio) só acumula quando CalcVerification=true
                // e o SOLIDOS recalcula; GetNodeParam apenas LÊ o valor gravado, não
                // recalcula. Sem isso, o tronco lê vazão local (não acumulada) e
                // subdimensiona — ex.: lê 206 L/s onde o acumulado real é 699 L/s.
                ForcarRecalculoRede(anchorId, ed);

                var grafo = new Grafo();
                grafo.MapearViaBFS(anchorId, avisos, diag);
                pm.MeterProgress();

                var ordem = grafo.OrdenarMontanteParaJusante(avisos);
                pm.MeterProgress();

                int ok = grafo.Dimensionar(ordem, cfg, dnsM, avisos, diag, linhas);
                SolidosAPI.DocCommit();
                pm.MeterProgress();

                ed.WriteMessage($"\nOK. Tubos dimensionados: {ok} de {grafo.QtdTubos}.");
                diag.Imprimir(ed);
                foreach (string a in avisos.Take(40)) ed.WriteMessage("\n" + a);
                if (avisos.Count > 40) ed.WriteMessage($"\n... (+{avisos.Count - 40} avisos)");

                try { pm.Stop(); pm = null; } catch { }

                string header = $"Drenagem — dimensionados {ok} de {grafo.QtdTubos}.  " +
                                $"Nós={diag.NosTotal}  Tubos={diag.TubosTotal}.  " +
                                $"Pluvial V[{cfg.Vmin:0.00}-{cfg.Vmax:0.00}] Y/D≤{cfg.YDmax*100:0}%  " +
                                (cfg.IncendioAtivo ? $"Incêndio V≤{cfg.IncendioVmax:0.0} Y/D≤{cfg.IncendioYDmax*100:0}%" : "Incêndio OFF");
                var relWin = new DimensionamentoRelatorioWindow(linhas, header);
                try { Application.ShowModalWindow(relWin); }
                catch (System.Exception ex) { ed.WriteMessage($"\n[UI] Falha relatório: {ex.Message}"); }
            }
            catch (SolidosException sx) { ed.WriteMessage($"\n[SOLIDOS] {sx.Message}"); }
            catch (System.Exception ex) { ed.WriteMessage($"\n[ERRO] {ex.Message}"); }
            finally { try { pm?.Stop(); } catch { } }
        }

        private static void EcoarRegras(Editor ed, DimensionamentoConfig cfg, List<double> dnsM)
        {
            ed.WriteMessage("\n--- Regras (de dimensionamento_regras.json) ---");
            ed.WriteMessage($"\n  Pluvial : V {cfg.Vmin:0.00}–{cfg.Vmax:0.00} m/s, lâmina ≤ {cfg.YDmax*100:0}%");
            ed.WriteMessage(cfg.IncendioAtivo
                ? $"\n  Incêndio: V ≤ {cfg.IncendioVmax:0.0} m/s, lâmina ≤ {cfg.IncendioYDmax*100:0}% (Qfim=HCalcFim.Qesc)"
                : "\n  Incêndio: desativado");
            ed.WriteMessage($"\n  Declividade: {cfg.DuraSlopeMin*100:0.0}%–{cfg.DuraSlopeMax*100:0.0}% (passo {cfg.SlopeStep*100:0.00}%)");
            ed.WriteMessage($"\n  Recobrimento mín.: {cfg.RecobrimentoMinDrenM:0.00} m (D_ext = D+2·Parede)");
            ed.WriteMessage($"\n  Junção PONTO BAIXO: {(cfg.JuncaoPontoBaixo ? "sim" : "não")}");
            if (cfg.ManterCotasDeclividade)
                ed.WriteMessage("\n  >> MODO: MANTER COTAS E DECLIVIDADE — altera SÓ o diâmetro (ignora i mín/máx, recobrimento e PONTO BAIXO).");
            ed.WriteMessage($"\n  Catálogo (mm): {string.Join(";", dnsM.Select(d => (int)Math.Round(d*1000)))}");
        }

        public static ObjectId SelecionarAncora(Editor ed)
        {
            var peo = new PromptEntityOptions("\nSelecione o DISPOSITIVO DE JUSANTE (âncora): ");
            peo.SetRejectMessage("\nSelecione um nó SOLIDOS.");
            PromptEntityResult per = ed.GetEntity(peo);
            return per.Status == PromptStatus.OK ? per.ObjectId : ObjectId.Null;
        }

        // Liga CalcVerification na rede (Root do âncora) e força o recálculo, para que
        // HCalcIni.Qesc (pluvial) e HCalcFim.Qesc (incêndio) reflitam as vazões
        // ACUMULADAS, e não os valores locais gravados. Setar "CalcVerification" dispara
        // SolGravityNetwork.ProcessaKey → ToCalcAll(); DocCommit() faz o flush do cálculo.
        private static void ForcarRecalculoRede(ObjectId anchorId, Editor ed)
        {
            try
            {
                ObjectId rootId = Grafo.GetParam<ObjectId>(anchorId, "RootId");
                if (rootId.IsNull)
                {
                    ed.WriteMessage("\n[CALC] Não achei o Root da rede; vazões podem estar desatualizadas.");
                    return;
                }
                var dic = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CalcVerification"] = true
                };
                SolidosAPI.SetNodeParams(rootId, dic);
                SolidosAPI.DocCommit();
                ed.WriteMessage("\n[CALC] Rede recalculada com verificação (HCalcFim acumulado).");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[CALC] Falha ao forçar recálculo: {ex.Message}");
            }
        }

        // ============================================================== SELEÇÃO HID.
        public sealed class Escolha
        {
            public double D;        // m
            public double Slope;    // m/m
            public HidraulicaCircularDren.Estado Pluvial;
            public HidraulicaCircularDren.Estado Incendio;
            public bool TemIncendio;
            public string RegimeGov;
        }

        // Acha (DN, i) por MENOR ESCAVAÇÃO que passa nos dois regimes ativos.
        // Itera DN crescente; para cada DN varre i e pega o MENOR i válido; retorna o
        // primeiro DN (menor) com janela não-vazia.
        public static Escolha SelecionarTubo(
            double qIni, double qFim, double n, DimensionamentoConfig cfg, List<double> dnsM)
        {
            bool incendio = cfg.IncendioAtivo && qFim > 0;
            double iMin = cfg.DuraSlopeMin, iMax = cfg.DuraSlopeMax, passo = Math.Max(cfg.SlopeStep, 1e-5);

            foreach (double D in dnsM)
            {
                for (double i = iMin; i <= iMax + 1e-12; i += passo)
                {
                    // Pluvial (sempre exigido quando há Qini).
                    HidraulicaCircularDren.Estado ep = default;
                    if (qIni > 0)
                    {
                        ep = HidraulicaCircularDren.ResolverLamina(qIni, D, n, i);
                        if (!ep.Ok) continue;
                        if (ep.YD > cfg.YDmax) continue;             // lâmina alta → precisa mais i
                        if (ep.V < cfg.Vmin) continue;              // V baixa → precisa mais i
                        if (ep.V > cfg.Vmax) break;                 // V alta → i só piora; abandona DN
                    }

                    // Incêndio (verificação, se houver Qfim).
                    HidraulicaCircularDren.Estado ei = default;
                    if (incendio)
                    {
                        ei = HidraulicaCircularDren.ResolverLamina(qFim, D, n, i);
                        if (!ei.Ok) continue;
                        if (ei.YD > cfg.IncendioYDmax) continue;
                        if (ei.V > cfg.IncendioVmax) break;         // V alta no incêndio; abandona DN
                    }

                    return new Escolha
                    {
                        D = D, Slope = i, Pluvial = ep, Incendio = ei, TemIncendio = incendio,
                        RegimeGov = (incendio && qFim >= qIni) ? "Incêndio" : "Pluvial"
                    };
                }
            }
            return null;
        }

        // Modo "manter cotas": declividade FIXA (= a atual do tubo); varia SÓ o DN.
        // Com i fixo e D crescente, Y/D e V caem (monotônicos) — logo o MENOR DN viável
        // é o primeiro que põe lâmina ≤ máx e V ≤ Vmáx nos dois regimes. A Vmín do
        // pluvial vira AVISO (vminFalha): com i fixo, aumentar D só REDUZ V; não dá p/
        // corrigir baixa velocidade pelo diâmetro — o que importa aqui é a capacidade.
        public static Escolha SelecionarSoDiametro(
            double qIni, double qFim, double n, double slope, DimensionamentoConfig cfg,
            List<double> dnsM, out bool vminFalha)
        {
            vminFalha = false;
            bool incendio = cfg.IncendioAtivo && qFim > 0;
            foreach (double D in dnsM)
            {
                HidraulicaCircularDren.Estado ep = default;
                bool vminLocal = false;
                if (qIni > 0)
                {
                    ep = HidraulicaCircularDren.ResolverLamina(qIni, D, n, slope);
                    if (!ep.Ok) continue;
                    if (ep.YD > cfg.YDmax) continue;        // muito cheio → DN maior
                    if (ep.V > cfg.Vmax) continue;           // muito rápido → DN maior
                    if (ep.V < cfg.Vmin) vminLocal = true;   // lento: DN maior não corrige
                }

                HidraulicaCircularDren.Estado ei = default;
                if (incendio)
                {
                    ei = HidraulicaCircularDren.ResolverLamina(qFim, D, n, slope);
                    if (!ei.Ok) continue;
                    if (ei.YD > cfg.IncendioYDmax) continue;
                    if (ei.V > cfg.IncendioVmax) continue;
                }

                vminFalha = vminLocal;
                return new Escolha
                {
                    D = D, Slope = slope, Pluvial = ep, Incendio = ei, TemIncendio = incendio,
                    RegimeGov = (incendio && qFim >= qIni) ? "Incêndio" : "Pluvial"
                };
            }
            return null;
        }

        // ============================================================== DIAGNÓSTICO
        public class Diagnostico
        {
            public int NosTotal, TubosTotal, TubosSemVazao, TubosSemSolucao,
                       TubosLenZero, TubosFalhaGravacao, TubosOK, JuncoesAjustadas,
                       ConectoresMantidos;

            public void Imprimir(Editor ed)
            {
                ed.WriteMessage("\n--- Diagnóstico ---");
                ed.WriteMessage($"\n  Nós: {NosTotal}   Tubos: {TubosTotal}");
                ed.WriteMessage($"\n    sem vazão (Qini e Qfim = 0):  {TubosSemVazao}");
                ed.WriteMessage($"\n    sem (D,i) viável:             {TubosSemSolucao}");
                ed.WriteMessage($"\n    comprimento ~0:               {TubosLenZero}");
                ed.WriteMessage($"\n    falha ao gravar catálogo:     {TubosFalhaGravacao}");
                ed.WriteMessage($"\n    OK (dimensionados):           {TubosOK}");
                ed.WriteMessage($"\n    conectores DN mantido:        {ConectoresMantidos}");
                ed.WriteMessage($"\n    junções p/ PONTO BAIXO:       {JuncoesAjustadas}");
            }
        }

        // ================================================================== MODELO
        public class Tubo
        {
            public ObjectId Id, InPart, OutPart;
            public GeometryPoint StartPoint, EndPoint;
            public double? QIni;   // m³/s, HCalcIni.Qesc (pluvial)
            public double? QFim;   // m³/s, HCalcFim.Qesc (incêndio)
            public int? DNmmAnterior;
        }

        public class No
        {
            public ObjectId Id;
            public GeometryPoint Location;
            public double? SurfaceElevation;   // terreno
            public double? MaxElevAllowed;     // teto do invert nesse nó
            public string FamilyName, SubType;
            public List<Tubo> Entrando = new List<Tubo>();
            public List<Tubo> Saindo = new List<Tubo>();
        }

        // =================================================================== GRAFO
        public class Grafo
        {
            public Dictionary<ObjectId, No> Nos = new Dictionary<ObjectId, No>();
            public Dictionary<ObjectId, Tubo> Tubos = new Dictionary<ObjectId, Tubo>();
            public int QtdTubos => Tubos.Count;

            // invert "disponível" por nó = menor invert que chega (PONTO BAIXO).
            private readonly Dictionary<ObjectId, double> _invertDisp = new Dictionary<ObjectId, double>();
            private readonly HashSet<ObjectId> _juncoesSetadas = new HashSet<ObjectId>();

            public void MapearViaBFS(ObjectId anchorId, List<string> avisos, Diagnostico diag)
            {
                No ancora = LerOuCriarNo(anchorId);
                if (ancora.Location == null) { avisos.Add($"[BFS] Âncora {anchorId.Handle} sem Location."); return; }

                var fila = new Queue<ObjectId>();
                var visit = new HashSet<ObjectId> { anchorId };
                fila.Enqueue(anchorId);

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

            public List<Tubo> OrdenarMontanteParaJusante(List<string> avisos)
            {
                var inDeg = new Dictionary<ObjectId, int>();
                foreach (var kv in Nos)
                {
                    // Conta só arestas de entrada que SERÃO emitidas: tubo com InPart
                    // válido e mapeado. Tubo de entrada com InPart nulo/fantasma nunca
                    // entra em Saindo de ninguém → nunca decrementa → travaria o nó e
                    // TODO o trecho de jusante (ex.: troncos a jusante de uma CANALETA
                    // SolGravityLong cuja ligação InPart não é lida). Ignorá-las aqui
                    // libera o resto da rede; só o tubo realmente quebrado fica de fora.
                    int deg = 0;
                    foreach (Tubo t in kv.Value.Entrando)
                        if (!t.InPart.IsNull && Nos.ContainsKey(t.InPart)) deg++;
                    inDeg[kv.Key] = deg;
                }

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
                {
                    // Lista os handles travados (ciclo/desconexão) — sem eles o usuário
                    // não consegue achar o tubo virado que cria o laço. Esses tubos NÃO
                    // são dimensionados (Kahn não os ordena).
                    var faltam = new List<string>();
                    foreach (var kv in Tubos)
                        if (!emitidos.Contains(kv.Key)) faltam.Add(kv.Key.Handle.ToString());
                    avisos.Add($"[TOPO] {Tubos.Count - ordem.Count} tubo(s) fora da ordem (ciclo/desconexão) " +
                               $"— NÃO dimensionados: {string.Join(" ", faltam.GetRange(0, Math.Min(40, faltam.Count)))}");
                }
                return ordem;
            }

            public int Dimensionar(
                List<Tubo> ordem, DimensionamentoConfig cfg, List<double> dnsM,
                List<string> avisos, Diagnostico diag, List<DimensionamentoLinhaRelatorio> linhas)
            {
                int ok = 0;
                foreach (Tubo t in ordem)
                {
                    No noMont = !t.InPart.IsNull && Nos.TryGetValue(t.InPart, out var nm) ? nm : null;
                    No noJus = !t.OutPart.IsNull && Nos.TryGetValue(t.OutPart, out var nj) ? nj : null;

                    DimensionamentoLinhaRelatorio Linha(string status, Escolha e,
                        double? zM, double? zJ, int? dnAplic, double? recob)
                    {
                        return new DimensionamentoLinhaRelatorio
                        {
                            Handle = t.Id.Handle.ToString(),
                            NoMontanteFamilia = noMont?.FamilyName ?? "",
                            NoMontanteHandle = noMont != null ? noMont.Id.Handle.ToString() : "",
                            NoJusanteFamilia = noJus?.FamilyName ?? "",
                            NoJusanteHandle = noJus != null ? noJus.Id.Handle.ToString() : "",
                            QLs = t.QIni.HasValue ? t.QIni.Value * 1000.0 : (double?)null,
                            QLsIncendio = t.QFim.HasValue ? t.QFim.Value * 1000.0 : (double?)null,
                            DNmmAnterior = t.DNmmAnterior,
                            DNmm = dnAplic ?? (e != null ? (int?)CatalogoTuboPadrao.DnMmMaisProximo(e.D) : null),
                            SlopePct = e != null ? e.Slope * 100.0 : (double?)null,
                            VMs = e != null && e.Pluvial.Ok ? e.Pluvial.V : (double?)null,
                            YDPct = e != null && e.Pluvial.Ok ? e.Pluvial.YD * 100.0 : (double?)null,
                            VMsIncendio = e != null && e.TemIncendio && e.Incendio.Ok ? e.Incendio.V : (double?)null,
                            YDPctIncendio = e != null && e.TemIncendio && e.Incendio.Ok ? e.Incendio.YD * 100.0 : (double?)null,
                            RegimeGov = e?.RegimeGov,
                            ZMontante = zM,
                            ZJusante = zJ,
                            RecobrimentoM = recob,
                            ComprimentoM = PlanLen(t.StartPoint, t.EndPoint),
                            Status = status
                        };
                    }

                    double qIni = t.QIni ?? 0.0, qFim = t.QFim ?? 0.0;
                    if (qIni <= 0 && qFim <= 0)
                    {
                        // Conector/dreno passivo (CURVA / FUNIL-TÊ — IfcPipeFitting.JUNCTION):
                        // o SOLIDOS calcula Qesc=0 com razão, pois a carga de incêndio nasce
                        // nas CAIXAS, não nestes fittings de cabeceira (ficam a montante da
                        // injeção). NÃO inventamos vazão (a regra "funil" antiga mutava a rede
                        // e a corrompeu). Em vez de deixar o trecho "cru/pulado" no relatório,
                        // CONFIRMAMOS o DN que o tubo já tem (continuidade geométrica). NUNCA
                        // herdar o DN do tronco de jusante: isso o subiria além do necessário,
                        // contrariando a regra de MENOR ESCAVAÇÃO desta rotina.
                        if (EhConectorPassivo(noMont) && t.DNmmAnterior is int dnExist && dnExist > 0)
                        {
                            diag.ConectoresMantidos++; ok++;
                            linhas.Add(Linha($"OK (conector sem vazão — DN {dnExist} mantido)",
                                             null, null, null, dnExist, null));
                            continue;
                        }
                        diag.TubosSemVazao++;
                        linhas.Add(Linha("pulado: sem vazão (Qini e Qfim = 0)", null, null, null, null, null));
                        continue;
                    }

                    double n = LerManning(t.Id, cfg.ManningDefault);

                    // ===== MODO "MANTER COTAS E DECLIVIDADE" — altera SÓ o diâmetro =====
                    // Não recalcula cota nem declividade: lê a declividade ATUAL do tubo
                    // (das cotas existentes) e procura o menor DN que atende lâmina/V. Grava
                    // só o Catalogo (geometria intacta). Para troncos de cota fixa.
                    if (cfg.ManterCotasDeclividade)
                    {
                        double planK = PlanLen(t.StartPoint, t.EndPoint);
                        if (planK < 1e-6)
                        {
                            diag.TubosLenZero++;
                            linhas.Add(Linha("pulado: comprimento ~0", null, null, null, null, null));
                            continue;
                        }
                        bool startMontK = EhMontante(t.StartPoint, t.EndPoint, noMont);
                        double zMK = startMontK ? t.StartPoint.Z : t.EndPoint.Z;
                        double zJK = startMontK ? t.EndPoint.Z : t.StartPoint.Z;
                        double iK = (zMK - zJK) / planK;
                        if (iK <= 1e-6)
                        {
                            diag.TubosSemSolucao++;
                            avisos.Add($"[DIM] Tubo {t.Id.Handle}: declividade atual {iK*100:F3}% ≤ 0 — Manning não resolve (cota mantida).");
                            linhas.Add(Linha($"pulado: i atual ≤ 0 ({iK*100:F3}%) — cota mantida", null, null, null, null, null));
                            continue;
                        }
                        Escolha escK = SelecionarSoDiametro(qIni, qFim, n, iK, cfg, dnsM, out bool vminFalha);
                        if (escK == null)
                        {
                            diag.TubosSemSolucao++;
                            avisos.Add($"[DIM] Tubo {t.Id.Handle}: nenhum DN atende Y/D e Vmáx na i={iK*100:F2}% mantida (Qini={qIni*1000:F1} Qfim={qFim*1000:F1} L/s).");
                            linhas.Add(Linha($"pulado: sem DN viável c/ cota mantida (i={iK*100:F2}%)", null, null, null, null, null));
                            continue;
                        }
                        int dnK = CatalogoTuboPadrao.DnMmMaisProximo(escK.D);
                        SolidosAPI.SetNodeParams(t.Id, new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Catalogo"] = dnK.ToString(CultureInfo.InvariantCulture)   // SÓ o diâmetro; cotas intactas
                        });
                        int? lidoK = CatalogoTuboPadrao.LerCatalogoMm(t.Id);
                        if (lidoK != dnK)
                        {
                            diag.TubosFalhaGravacao++;
                            avisos.Add($"[DIM] Tubo {t.Id.Handle}: catálogo pedido {dnK}, lido {(lidoK?.ToString() ?? "?")}.");
                            linhas.Add(Linha($"falha ao gravar DN {dnK} (cota mantida)", escK, zMK, zJK, dnK, null));
                            continue;
                        }
                        double? recobK = null;
                        if (noMont?.SurfaceElevation is double terrK && !double.IsNaN(terrK))
                            recobK = terrK - (zMK + escK.D + 2.0 * LerParede(t.Id));
                        if (vminFalha)
                            avisos.Add($"[DIM] Tubo {t.Id.Handle}: V pluvial < Vmín na i={iK*100:F2}% mantida — autolimpeza não atendida só com diâmetro.");
                        diag.TubosOK++; ok++;
                        linhas.Add(Linha(vminFalha
                            ? "OK (só DN — V pluvial < Vmín na cota mantida)"
                            : "OK (só DN — cota e declividade mantidas)", escK, zMK, zJK, dnK, recobK));
                        continue;
                    }
                    // ===================================================================

                    Escolha esc = SelecionarTubo(qIni, qFim, n, cfg, dnsM);
                    if (esc == null)
                    {
                        diag.TubosSemSolucao++;
                        avisos.Add($"[DIM] Tubo {t.Id.Handle}: sem (D,i) p/ Qini={qIni*1000:F1} Qfim={qFim*1000:F1} L/s, n={n:F3}.");
                        linhas.Add(Linha($"pulado: sem (D,i) viável (n={n:F3})", null, null, null, null, null));
                        continue;
                    }

                    double planLen = PlanLen(t.StartPoint, t.EndPoint);
                    if (planLen < 1e-6)
                    {
                        diag.TubosLenZero++;
                        linhas.Add(Linha("pulado: comprimento ~0", esc, null, null, null, null));
                        continue;
                    }

                    // ---- cota de montante ----
                    double dExt = esc.D + 2.0 * LerParede(t.Id);
                    double zMont;
                    if (_invertDisp.TryGetValue(t.InPart, out double disp))
                    {
                        zMont = disp;                                   // segue PONTO BAIXO do nó
                    }
                    else
                    {
                        // cabeceira: o mais raso possível (subir tudo). Ancora pelo
                        // terreno e o recobrimento. Sem terreno conhecido, NÃO enterra:
                        // mantém a ponta de montante atual do tubo.
                        double? terreno = noMont?.SurfaceElevation ?? noMont?.Location?.Z;
                        zMont = terreno.HasValue
                            ? terreno.Value - cfg.RecobrimentoMinDrenM - dExt
                            : AltoDoTubo(t);
                    }
                    // teto do nó (MaxElevAllowed), se houver.
                    if (noMont?.MaxElevAllowed is double tetoM && !double.IsNaN(tetoM))
                        zMont = Math.Min(zMont, tetoM);

                    double zJus = zMont - esc.Slope * planLen;

                    // propaga PONTO BAIXO para o nó de jusante.
                    if (!t.OutPart.IsNull)
                    {
                        if (!_invertDisp.TryGetValue(t.OutPart, out double atual) || zJus < atual)
                            _invertDisp[t.OutPart] = zJus;
                    }

                    // recobrimento resultante na montante (p/ relatório/aviso).
                    double? recob = null;
                    if (noMont?.SurfaceElevation is double terrM && !double.IsNaN(terrM))
                    {
                        recob = terrM - (zMont + dExt);
                        if (recob.Value < cfg.RecobrimentoMinDrenM - 1e-6)
                            avisos.Add($"[DIM] Tubo {t.Id.Handle}: recobrimento {recob.Value:F2} < {cfg.RecobrimentoMinDrenM:F2} m (terreno sobe a jusante?).");
                    }

                    // ---- grava DN + cotas (montante na ponta mais perto do nó InPart) ----
                    bool startIsMont = EhMontante(t.StartPoint, t.EndPoint, noMont);
                    int dnMm = CatalogoTuboPadrao.DnMmMaisProximo(esc.D);
                    var dic = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Catalogo"] = dnMm.ToString(CultureInfo.InvariantCulture)
                    };
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

                    // junção a jusante → PONTO BAIXO.
                    if (cfg.JuncaoPontoBaixo && noJus != null) SetarJuncaoPontoBaixo(noJus, diag);

                    diag.TubosOK++; ok++;
                    linhas.Add(Linha("OK", esc, zMont, zJus, dnMm, recob));
                }
                return ok;
            }

            private void SetarJuncaoPontoBaixo(No no, Diagnostico diag)
            {
                if (_juncoesSetadas.Contains(no.Id)) return;
                string fam = (no.FamilyName ?? "").ToUpperInvariant();
                string sub = (no.SubType ?? "").ToUpperInvariant();
                bool ehJuncao = fam.Contains("CONEX") && (sub.Contains("JUN") || sub.Contains("TE"));
                if (!ehJuncao) return;
                _juncoesSetadas.Add(no.Id);
                try
                {
                    var dic = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["SetarReferencia"] = "PONTO BAIXO"
                    };
                    SolidosAPI.SetNodeParams(no.Id, dic);
                    diag.JuncoesAjustadas++;
                }
                catch { /* best-effort */ }
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
                    MaxElevAllowed = LerDouble(id, "MaxElevAllowed"),
                    FamilyName = LerPrimeiroString(id, FamilyNameCandidates),
                    SubType = LerPrimeiroString(id, new[] { "SubType", "Code" })
                };
                Nos[id] = no;
                return no;
            }

            public Tubo LerOuCriarTubo(ObjectId id)
            {
                if (Tubos.TryGetValue(id, out Tubo e)) return e;
                GeometryPoint sp = GetParam<GeometryPoint>(id, "StartPoint");
                GeometryPoint ep = GetParam<GeometryPoint>(id, "EndPoint");
                if (sp == null || ep == null) return null;

                var t = new Tubo
                {
                    Id = id,
                    InPart = GetParam<ObjectId>(id, "InPart"),
                    OutPart = GetParam<ObjectId>(id, "OutPart"),
                    StartPoint = sp,
                    EndPoint = ep,
                    QIni = LerDoublePos(id, "HCalcIni.Qesc"),
                    QFim = LerDoublePos(id, "HCalcFim.Qesc"),
                    DNmmAnterior = CatalogoTuboPadrao.LerCatalogoMm(id)
                };
                Tubos[id] = t;
                return t;
            }

            // Heurística de orientação: a ponta de montante é a mais próxima (XY) do
            // nó InPart. Fallback: ponta de maior Z (água desce).
            private static bool EhMontante(GeometryPoint sp, GeometryPoint ep, No noMont)
            {
                if (noMont?.Location != null)
                {
                    double ds = Dist2(sp, noMont.Location);
                    double de = Dist2(ep, noMont.Location);
                    if (Math.Abs(ds - de) > 1e-9) return ds < de;
                }
                return sp.Z >= ep.Z;
            }

            // Conector/dreno passivo = fitting de transição/captação sem carga própria
            // (família CONEXÕES = curvas; DRENO DE EQUIPAMENTOS = funil-tê). Esses tubos
            // leem Qesc=0 por design (a vazão de incêndio nasce nas CAIXAS). NÃO inclui
            // CAIXAS: caixa com Q=0 é erro de projeto real, não algo a "manter".
            private static bool EhConectorPassivo(No noMont)
            {
                string fam = (noMont?.FamilyName ?? "").ToUpperInvariant();
                return fam.Contains("CONEX") || fam.Contains("DRENO");
            }

            private static double AltoDoTubo(Tubo t) => Math.Max(t.StartPoint.Z, t.EndPoint.Z);

            public static double LerManning(ObjectId tuboId, double padrao)
            {
                double? n = LerPrimeiroDouble(tuboId, ManningCandidates);
                return n.HasValue && n.Value > 0 ? n.Value : padrao;
            }

            public static double LerParede(ObjectId tuboId)
            {
                double? p = LerPrimeiroDouble(tuboId, ParedeCandidates);
                return p.HasValue && p.Value >= 0 ? p.Value : 0.0;
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

            public static double? LerDoublePos(ObjectId id, string nome)
            {
                double? d = LerDouble(id, nome);
                return (d.HasValue && d.Value > 0) ? d : null;
            }

            public static double? LerPrimeiroDouble(ObjectId id, IEnumerable<string> nomes)
            {
                foreach (string nome in nomes)
                {
                    double? d = LerDouble(id, nome);
                    if (d.HasValue) return d;
                }
                return null;
            }

            public static double? LerDouble(ObjectId id, string nome)
            {
                try
                {
                    Type t = null;
                    object v = SolidosAPI.GetNodeParam(id, nome, null, ref t);
                    if (v == null) return null;
                    if (v is double d) return double.IsNaN(d) ? (double?)null : d;
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

            private static double Dist2(GeometryPoint a, GeometryPoint b)
            {
                double dx = a.X - b.X, dy = a.Y - b.Y;
                return dx * dx + dy * dy;
            }
        }

        public static double PlanLen(GeometryPoint a, GeometryPoint b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
