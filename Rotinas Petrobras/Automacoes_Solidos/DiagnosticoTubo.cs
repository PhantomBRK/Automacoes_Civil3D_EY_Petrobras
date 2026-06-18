using System;
using System.Collections.Generic;
using System.Globalization;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using SOLIDOS;

namespace AutomacoesCivil3D
{
    // ============================================================================
    // SOL_DIAG_TUBO — por que um tubo específico (ex.: tronco Ø500/600 "sem TQ")
    // NÃO aparece no relatório do SOL_DIMENSIONAR_DRENAGEM. READ-ONLY.
    //
    // O dimensionamento só enxerga tubos A MONTANTE do âncora (BFS que sobe por
    // OutPart==nó). Tubo a jusante do âncora, em outro Root, ou com InPart/OutPart
    // invertido fica fora e some do relatório.
    //
    // Este comando pede o âncora (dispositivo de jusante) e um tubo-alvo, roda o
    // MESMO BFS e diz se o alvo foi alcançado. Se NÃO, percorre de jusante do alvo
    // em direção ao âncora e mostra ONDE a cadeia quebra:
    //   • PONTA MORTA  → não há tubo de jusante (rede interrompida / âncora a montante);
    //   • Root muda    → o caminho passa para outra rede;
    //   • chega no âncora → o caminho existe, então a falha é orientação invertida
    //                       em algum tubo (o BFS sobe por OutPart e não atravessa).
    // ============================================================================
    public class SolDiagnosticoTubo
    {
        [CommandMethod("SOL_DIAG_TUBO")]
        public void DiagnosticarTubo()
        {
            Editor ed = Manager.DocEditor;

            ObjectId anchorId = SolDimensionarDrenagem.SelecionarAncora(ed);
            if (anchorId.IsNull) { ed.WriteMessage("\nÂncora não selecionada."); return; }

            var peo = new PromptEntityOptions("\nSelecione o TUBO que NÃO está sendo dimensionado (tronco): ");
            peo.SetRejectMessage("\nSelecione um tubo SOLIDOS.");
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) { ed.WriteMessage("\nTubo não selecionado."); return; }
            ObjectId alvo = per.ObjectId;

            ObjectId anchorRoot = G(anchorId, "RootId");
            ObjectId alvoRoot   = G(alvo, "RootId");
            ObjectId inPart  = G(alvo, "InPart");
            ObjectId outPart = G(alvo, "OutPart");

            ed.WriteMessage("\n=== SOL_DIAG_TUBO ===");
            ed.WriteMessage($"\nÂncora    {anchorId.Handle}  [{Fam(anchorId)}]  Root={H(anchorRoot)}");
            ed.WriteMessage($"\nTubo-alvo {alvo.Handle}  DN={(CatalogoTuboPadrao.LerCatalogoMm(alvo)?.ToString() ?? "?")}  Root={H(alvoRoot)}");
            ed.WriteMessage($"\n   InPart(mont)={H(inPart)} [{Fam(inPart)}]   OutPart(jus)={H(outPart)} [{Fam(outPart)}]");
            ed.WriteMessage($"\n   StartClosed={Boolish(alvo, "StartClosed")}  " +
                            $"Q_plv(HCalcIni.Qesc)={Ls(Num(alvo, "HCalcIni.Qesc"))}  " +
                            $"Q_inc(HCalcFim.Qesc)={Ls(Num(alvo, "HCalcFim.Qesc"))}  " +
                            $"QMon_inc={Ls(Num(alvo, "HCalcFim.QMon"))}  [L/s]");

            // Causa imediata por Root.
            if (!alvoRoot.IsNull && !anchorRoot.IsNull && alvoRoot != anchorRoot)
                ed.WriteMessage("\n>> CAUSA: tubo em OUTRO Root (outra rede). O dimensionamento ancorado nessa " +
                                "rede NUNCA o alcança. Ancore na rede do tubo, ou una as redes no SOLIDOS.");

            // BFS do âncora — alvo foi alcançado?
            var avisos = new List<string>();
            var diag = new SolDimensionarDrenagem.Diagnostico();
            var grafo = new SolDimensionarDrenagem.Grafo();
            grafo.MapearViaBFS(anchorId, avisos, diag);
            bool alcancado = grafo.Tubos.ContainsKey(alvo);
            ed.WriteMessage($"\nBFS a montante do âncora: {grafo.Tubos.Count} tubos, {grafo.Nos.Count} nós. " +
                            $"Alvo alcançado? {(alcancado ? "SIM (está no escopo)" : "NÃO")}");

            // O dimensionamento só processa tubos que entram na ORDEM topológica (Kahn).
            // Um ciclo trava o tubo e tudo abaixo dele → some do relatório, mesmo no escopo.
            var ordem = grafo.OrdenarMontanteParaJusante(new List<string>());
            var emit = new HashSet<ObjectId>();
            foreach (var t in ordem) emit.Add(t.Id);
            bool naOrdem = emit.Contains(alvo);
            ed.WriteMessage($"\nOrdem topológica: {ordem.Count}/{grafo.Tubos.Count} tubos ordenados. " +
                            $"Alvo na ordem? {(naOrdem ? "SIM" : "NÃO")}");

            if (alcancado && naOrdem)
            {
                ed.WriteMessage("\n>> O tubo está no escopo E na ordem. Se não sai como OK, o motivo é a coluna " +
                                $"Status: 'sem (D,i) viável' = conflito de regime/catálogo. Aqui Q_inc=" +
                                $"{Ls(Num(alvo, "HCalcFim.Qesc"))} L/s, Q_plv={Ls(Num(alvo, "HCalcIni.Qesc"))} L/s " +
                                "— confira se o catálogo alcança o DN necessário e o Vmax pluvial.");
                return;
            }

            if (alcancado && !naOrdem)
            {
                ed.WriteMessage("\n>> CAUSA: o tubo está no grafo MAS FORA da ordem topológica — há um CICLO a " +
                                "montante. O dimensionamento só processa tubos ordenáveis; o laço e tudo abaixo " +
                                "dele (inclusive este tronco) são descartados ('[TOPO] ... fora da ordem').");
                var stuck = new List<ObjectId>();
                foreach (var t in grafo.Tubos.Values) if (!emit.Contains(t.Id)) stuck.Add(t.Id);
                var sh = new List<string>();
                for (int i = 0; i < stuck.Count && i < 30; i++) sh.Add(stuck[i].Handle.ToString());
                ed.WriteMessage($"\n   {stuck.Count} tubo(s) fora da ordem: {string.Join(" ", sh)}");

                List<ObjectId> ciclo = DetectarCiclo(grafo, emit);
                if (ciclo != null && ciclo.Count > 0)
                {
                    var ch = new List<string>();
                    foreach (var n in ciclo) ch.Add(n.Handle.ToString());
                    ed.WriteMessage($"\n   CICLO (nós): {string.Join(" -> ", ch)} -> {ciclo[0].Handle}");
                    ed.WriteMessage("\n   >> Verifique a ORIENTAÇÃO (InPart/OutPart, ou Start/EndPoint) dos tubos " +
                                    "entre esses nós — provavelmente um está virado e fecha o laço. Corrija e rode de novo.");
                    return;
                }

                // Sem laço → STALL: alguma aresta de entrada não drena (InPart nulo/fantasma).
                // Lista cada travado com suas pontas e marca o(s) quebrado(s) = a CAUSA real.
                ed.WriteMessage("\n   (sem laço — é STALL: aresta de entrada que não drena. Pontas dos travados:)");
                foreach (ObjectId h in stuck)
                {
                    ObjectId ip = G(h, "InPart");
                    ObjectId op = G(h, "OutPart");
                    var flags = new List<string>();
                    if (ip.IsNull) flags.Add("InPart NULO");
                    else if (!grafo.Nos.ContainsKey(ip)) flags.Add("InPart fantasma");
                    if (op.IsNull) flags.Add("OutPart NULO");
                    else if (!grafo.Nos.ContainsKey(op)) flags.Add("OutPart fantasma");
                    if (!ip.IsNull && ip == op) flags.Add("self-loop");
                    string fl = flags.Count > 0 ? "   <<< " + string.Join(", ", flags) : "";
                    ed.WriteMessage($"\n     {h.Handle} [{Fam(h)}]  {H(ip)} -> {H(op)}{fl}");
                }
                ed.WriteMessage("\n   >> Os marcados com <<< travam a ordem (ligação de montante quebrada — típico " +
                                "de CANALETA/SolGravityLong). A ordenação robusta já ignora essas arestas e " +
                                "dimensiona o resto; para o tubo sair do limbo, reconecte o InPart no SOLIDOS.");
                return;
            }

            // Trilha de jusante: do alvo em direção ao âncora, achando a quebra.
            ed.WriteMessage("\n--- Caminho de JUSANTE do alvo (segue InPart→OutPart) até o âncora ---");
            if (outPart.IsNull)
            {
                ed.WriteMessage("\n   O alvo não tem OutPart (nó de jusante). É ponta solta — não há caminho até o âncora.");
                return;
            }

            var visit = new HashSet<ObjectId>();
            ObjectId prevPipe = alvo;
            ObjectId node = outPart;
            int hop = 0;
            while (!node.IsNull && visit.Add(node) && hop++ < 500)
            {
                if (node == anchorId)
                {
                    ed.WriteMessage($"\n   [{hop}] chegou no ÂNCORA {node.Handle}. O caminho EXISTE → o alvo " +
                                    "deveria ser alcançado. CAUSA provável: ORIENTAÇÃO invertida (InPart/OutPart) " +
                                    "em algum tubo do caminho — o BFS sobe por OutPart e não atravessa o tubo virado.");
                    return;
                }

                // tubo de jusante = conectado ao nó com InPart==nó (e != de onde viemos).
                ObjectId nextPipe = ObjectId.Null;
                foreach (ObjectId p in SolDimensionarDrenagem.Grafo.ConnectedDevices(node))
                {
                    if (p == prevPipe) continue;
                    if (G(p, "InPart") == node) { nextPipe = p; break; }
                }

                if (nextPipe.IsNull)
                {
                    ed.WriteMessage($"\n   [{hop}] PONTA MORTA no nó {node.Handle} [{Fam(node)}]: nenhum tubo de " +
                                    "jusante (InPart==nó). >> CAUSA: o caminho do alvo NÃO chega ao âncora — rede " +
                                    "interrompida aqui, OU o âncora está a MONTANTE do alvo (ancore mais a jusante).");
                    ListarConectados(ed, node);
                    return;
                }

                ObjectId nextRoot = G(nextPipe, "RootId");
                string extra = (!nextRoot.IsNull && !anchorRoot.IsNull && nextRoot != anchorRoot)
                    ? $"  << Root muda p/ {H(nextRoot)} (quebra de rede aqui)" : "";
                ed.WriteMessage($"\n   [{hop}] nó {node.Handle} [{Fam(node)}] --tubo {nextPipe.Handle} " +
                                $"(DN {(CatalogoTuboPadrao.LerCatalogoMm(nextPipe)?.ToString() ?? "?")})-->{extra}");

                prevPipe = nextPipe;
                node = G(nextPipe, "OutPart");
            }
            ed.WriteMessage($"\n   (parou após {hop} saltos sem achar o âncora)");
        }

        private static void ListarConectados(Editor ed, ObjectId node)
        {
            var conn = SolDimensionarDrenagem.Grafo.ConnectedDevices(node);
            ed.WriteMessage($"\n      Tubos conectados ao nó ({conn.Count}):");
            foreach (ObjectId p in conn)
                ed.WriteMessage($"\n        {p.Handle}: InPart={H(G(p, "InPart"))}  OutPart={H(G(p, "OutPart"))}");
        }

        // Acha UM ciclo entre os tubos que NÃO entraram na ordem (DFS com pilha de nós,
        // seguindo só arestas presas = tubos não emitidos). Devolve os nós do laço.
        private static List<ObjectId> DetectarCiclo(SolDimensionarDrenagem.Grafo grafo, HashSet<ObjectId> emitidos)
        {
            var estado = new Dictionary<ObjectId, int>();   // 0=novo, 1=na pilha, 2=fechado
            var pilha = new List<ObjectId>();
            List<ObjectId> ciclo = null;

            bool Dfs(ObjectId node)
            {
                estado[node] = 1; pilha.Add(node);
                if (grafo.Nos.TryGetValue(node, out var no))
                {
                    foreach (var t in no.Saindo)
                    {
                        if (emitidos.Contains(t.Id)) continue;     // só arestas presas
                        if (t.OutPart.IsNull) continue;
                        ObjectId nxt = t.OutPart;
                        int st = estado.TryGetValue(nxt, out var s) ? s : 0;
                        if (st == 1)                               // back-edge → laço
                        {
                            int idx = pilha.IndexOf(nxt);
                            ciclo = pilha.GetRange(idx, pilha.Count - idx);
                            return true;
                        }
                        if (st == 0 && Dfs(nxt)) return true;
                    }
                }
                estado[node] = 2; pilha.RemoveAt(pilha.Count - 1);
                return false;
            }

            foreach (var n in grafo.Nos.Keys)
                if (!estado.ContainsKey(n) && Dfs(n)) break;
            return ciclo;
        }

        // ----------------------------- helpers read-only -----------------------------
        private static ObjectId G(ObjectId id, string prop) => SolDimensionarDrenagem.Grafo.GetParam<ObjectId>(id, prop);

        private static string Fam(ObjectId id)
        {
            if (id.IsNull) return "-";
            string f = SolDimensionarDrenagem.Grafo.LerPrimeiroString(id, SolDimensionarDrenagem.FamilyNameCandidates);
            return string.IsNullOrWhiteSpace(f) ? "?" : f.ToUpperInvariant();
        }

        private static double Num(ObjectId id, string nome) => SolDimensionarDrenagem.Grafo.LerDouble(id, nome) ?? 0.0;
        private static string Ls(double m3s) => (m3s * 1000.0).ToString("F1", CultureInfo.InvariantCulture);
        private static string H(ObjectId id) => id.IsNull ? "(null)" : id.Handle.ToString();

        private static string Boolish(ObjectId id, string nome)
        {
            try
            {
                Type t = null;
                object v = SolidosAPI.GetNodeParam(id, nome, null, ref t);
                if (v == null) return "(ausente)";
                if (v is bool b) return b ? "true" : "false";
                if (v is int i) return i != 0 ? $"true({i})" : "false(0)";
                return v.ToString();
            }
            catch { return "(n.d.)"; }
        }
    }
}
