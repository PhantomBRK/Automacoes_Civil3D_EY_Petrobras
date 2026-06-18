using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using SOLIDOS;

namespace AutomacoesCivil3D
{
    // ============================================================================
    // SOL_DIAG_CONECTORES — diagnóstico READ-ONLY de por que tubos de CONEXÕES
    // (CURVA) e DRENO DE EQUIPAMENTOS (FUNIL-TÊ) — e quaisquer tubos com Qesc=0 —
    // ficam "pulado: sem vazão" no SOL_DIMENSIONAR_DRENAGEM.
    //
    // Reaproveita o grafo/leitores de SolDimensionarDrenagem (mesmo escopo: a
    // montante do âncora). Para cada conector/dreno (ou tubo sem vazão) lê e imprime:
    //   • StartClosed ("Ponta Seca") — se true, o SOLIDOS bloqueia a vazão de propósito.
    //   • RootId vs RootId do âncora — se DIFERENTE, o ForcarRecalculoRede do
    //     dimensionamento (que só liga CalcVerification no Root do âncora) NUNCA
    //     recalculou esse trecho → é BUG de recálculo, não "trecho sem carga".
    //   • HCalcIni.Qesc (pluvial) e HCalcFim.Qesc/Qin/QMon (incêndio) do tubo.
    //   • CTop (contribuição local) do nó de jusante + Qesc do tronco que sai dele —
    //     se o nó-caixa tem CTop/Qesc>0 e o conector tem Qin=Qesc=0, fica CONFIRMADO
    //     que a carga entra no NÓ, não no conector (= comportamento correto).
    //
    // NÃO grava nada na rede, EXCETO (opcional, default Sim) o MESMO recálculo
    // CalcVerification que o dimensionamento já faz, para ler o estado que ele enxerga.
    // ============================================================================
    public class SolDiagnosticoConectores
    {
        [CommandMethod("SOL_DIAG_CONECTORES")]
        public void DiagnosticarConectores()
        {
            Editor ed = Manager.DocEditor;

            ObjectId anchorId = SolDimensionarDrenagem.SelecionarAncora(ed);
            if (anchorId.IsNull) { ed.WriteMessage("\nNada selecionado."); return; }

            // Recalcular antes de ler? (espelha ForcarRecalculoRede do dimensionamento)
            bool recalc = true;
            var pko = new PromptKeywordOptions(
                "\nRecalcular a rede (CalcVerification) antes de ler, como faz o dimensionamento? ");
            pko.Keywords.Add("Sim");
            pko.Keywords.Add("Nao");
            pko.Keywords.Default = "Sim";
            pko.AllowNone = true;
            PromptResult pkr = ed.GetKeywords(pko);
            if (pkr.Status == PromptStatus.Cancel) { ed.WriteMessage("\nCancelado."); return; }
            if (pkr.Status == PromptStatus.OK && pkr.StringResult == "Nao") recalc = false;

            ObjectId anchorRoot = SolDimensionarDrenagem.Grafo.GetParam<ObjectId>(anchorId, "RootId");

            if (recalc)
            {
                try
                {
                    if (!anchorRoot.IsNull)
                    {
                        var dic = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        { ["CalcVerification"] = true };
                        SolidosAPI.SetNodeParams(anchorRoot, dic);
                        SolidosAPI.DocCommit();
                        ed.WriteMessage("\n[CALC] Rede recalculada (CalcVerification) — mesmo passo do dimensionamento.");
                    }
                    else ed.WriteMessage("\n[CALC] Root do âncora não encontrado; lendo estado atual sem recalcular.");
                }
                catch (System.Exception ex) { ed.WriteMessage($"\n[CALC] Falha ao recalcular: {ex.Message}"); }
            }

            // Mapear a montante do âncora (mesmo escopo do dimensionamento).
            var avisos = new List<string>();
            var diag = new SolDimensionarDrenagem.Diagnostico();
            var grafo = new SolDimensionarDrenagem.Grafo();
            grafo.MapearViaBFS(anchorId, avisos, diag);

            ed.WriteMessage($"\n=== SOL_DIAG_CONECTORES === Nós={grafo.Nos.Count}  Tubos={grafo.Tubos.Count}  " +
                            $"Âncora Root={H(anchorRoot)}  Recálculo={(recalc ? "sim" : "não")}");

            var csv = new List<string>
            {
                "Handle;FamMont;FamJus;StartClosed;RootId;RootIgualAncora;Qplv_Ls;Qinc_Ls;Qin_Ls;QMon_Ls;" +
                "NoJusHandle;NoJusFam;CTopInc_Ls;CTopPlv_Ls;TroncoSaidaHandle;TroncoQinc_Ls;Veredito"
            };

            int nAnalisados = 0, nRootDif = 0, nPontaSeca = 0, nCorreto = 0, nSemCarga = 0, tubosComQplv = 0;

            foreach (var t in grafo.Tubos.Values)
            {
                var noMont = (!t.InPart.IsNull && grafo.Nos.TryGetValue(t.InPart, out var nm)) ? nm : null;
                var noJus  = (!t.OutPart.IsNull && grafo.Nos.TryGetValue(t.OutPart, out var nj)) ? nj : null;

                double qPlv = Num(t.Id, "HCalcIni.Qesc");
                double qInc = Num(t.Id, "HCalcFim.Qesc");
                if (qPlv > 0) tubosComQplv++;

                bool ehConector = Fam(noMont).Contains("CONEX") || Fam(noMont).Contains("DRENO")
                               || Fam(noJus).Contains("CONEX")  || Fam(noJus).Contains("DRENO");
                bool semVazao = qPlv <= 0 && qInc <= 0;
                if (!ehConector && !semVazao) continue;   // só conectores/drenos e tubos sem vazão
                nAnalisados++;

                bool? sc = BoolVal(t.Id, "StartClosed");
                string scDisp = sc.HasValue ? (sc.Value ? "true" : "false") : "(ausente/n.d.)";
                ObjectId root = SolDimensionarDrenagem.Grafo.GetParam<ObjectId>(t.Id, "RootId");
                bool rootIgual = !root.IsNull && !anchorRoot.IsNull && root == anchorRoot;
                double qin  = Num(t.Id, "HCalcFim.Qin");
                double qmon = Num(t.Id, "HCalcFim.QMon");

                // nó de jusante: contribuição local + tronco de saída.
                double cTopInc = noJus != null ? Num(noJus.Id, "HCalcFim.CTop") : 0.0;
                double cTopPlv = noJus != null ? Num(noJus.Id, "HCalcIni.CTop") : 0.0;
                ObjectId troncoId = ObjectId.Null; double troncoQinc = 0.0;
                if (noJus?.Saindo != null && noJus.Saindo.Count > 0)
                {
                    var tr = noJus.Saindo[0];
                    troncoId = tr.Id;
                    troncoQinc = Num(tr.Id, "HCalcFim.Qesc");
                }

                // veredito heurístico (ordem importa: descarta bug antes de "correto").
                string veredito;
                if (!root.IsNull && !rootIgual)
                { veredito = "BUG PROVÁVEL: Root != âncora — não recalculado pelo dimensionamento."; nRootDif++; }
                else if (sc == true)
                { veredito = "Ponta Seca LIGADA: SOLIDOS bloqueia vazão de propósito (intencional?)."; nPontaSeca++; }
                else if ((cTopInc > 0 || troncoQinc > 0) && qin <= 0 && qInc <= 0)
                { veredito = "CORRETO: carga entra no nó-caixa de jusante; conector sem vazão própria."; nCorreto++; }
                else
                { veredito = "Sem carga acumulada: conferir se recálculo rodou / se falta lançar contribuição."; nSemCarga++; }

                ed.WriteMessage($"\n\nTubo {t.Id.Handle}  [{Fam(noMont)} -> {Fam(noJus)}]");
                ed.WriteMessage($"\n   StartClosed={scDisp}   Root={H(root)} ({(rootIgual ? "== âncora" : "≠ âncora")})");
                ed.WriteMessage($"\n   Q_plv(HCalcIni.Qesc)={Ls(qPlv)}  Q_inc(HCalcFim.Qesc)={Ls(qInc)}  " +
                                $"Qin={Ls(qin)}  QMon={Ls(qmon)}  [L/s]");
                if (noJus != null)
                    ed.WriteMessage($"\n   Nó jus. {noJus.Id.Handle} [{Fam(noJus)}]: CTop_inc={Ls(cTopInc)}  " +
                                    $"CTop_plv={Ls(cTopPlv)}  | tronco saída {(troncoId.IsNull ? "(nenhum)" : troncoId.Handle.ToString())} Qesc_inc={Ls(troncoQinc)}");
                ed.WriteMessage($"\n   => {veredito}");

                csv.Add(string.Join(";", new[]
                {
                    t.Id.Handle.ToString(), Fam(noMont), Fam(noJus), scDisp, H(root), rootIgual ? "sim" : "nao",
                    Ls(qPlv), Ls(qInc), Ls(qin), Ls(qmon),
                    noJus?.Id.Handle.ToString() ?? "", Fam(noJus), Ls(cTopInc), Ls(cTopPlv),
                    troncoId.IsNull ? "" : troncoId.Handle.ToString(), Ls(troncoQinc), veredito
                }));
            }

            // --- Resumo ---
            ed.WriteMessage("\n\n--- Resumo ---");
            ed.WriteMessage($"\n  Conectores/sem-vazão analisados:           {nAnalisados}");
            ed.WriteMessage($"\n    Root diferente do âncora (BUG provável): {nRootDif}");
            ed.WriteMessage($"\n    Ponta Seca ligada:                       {nPontaSeca}");
            ed.WriteMessage($"\n    Confirmado correto (carga no nó-caixa):  {nCorreto}");
            ed.WriteMessage($"\n    Sem carga / recálculo duvidoso:          {nSemCarga}");
            ed.WriteMessage($"\n  Tubos com Q_plv>0 na rede: {tubosComQplv} de {grafo.Tubos.Count}" +
                (tubosComQplv == 0 ? "  << PLUVIAL ZERADO na rede inteira (regime off ou contribuição não lançada)" : ""));

            // CSV no Desktop (best-effort).
            try
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string path = Path.Combine(dir, $"diag_conectores_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllText(path, string.Join("\r\n", csv), new UTF8Encoding(true));
                ed.WriteMessage($"\n  CSV: {path}");
            }
            catch (System.Exception ex) { ed.WriteMessage($"\n  (CSV não gravado: {ex.Message})"); }

            foreach (string a in avisos.Take(10)) ed.WriteMessage("\n" + a);
        }

        // ----------------------------- helpers read-only -----------------------------
        private static string Fam(SolDimensionarDrenagem.No no) => (no?.FamilyName ?? "?").ToUpperInvariant();

        private static double Num(ObjectId id, string nome)
            => SolDimensionarDrenagem.Grafo.LerDouble(id, nome) ?? 0.0;

        // vazões do SOLIDOS estão em m³/s; relatório em L/s (×1000), como no dimensionamento.
        private static string Ls(double m3s) => (m3s * 1000.0).ToString("F1", CultureInfo.InvariantCulture);

        private static string H(ObjectId id) => id.IsNull ? "(null)" : id.Handle.ToString();

        private static bool? BoolVal(ObjectId id, string nome)
        {
            try
            {
                Type t = null;
                object v = SolidosAPI.GetNodeParam(id, nome, null, ref t);
                if (v == null) return null;
                if (v is bool b) return b;
                if (v is int i) return i != 0;
                if (v is string s)
                {
                    if (bool.TryParse(s, out bool bs)) return bs;
                    if (int.TryParse(s, out int iv)) return iv != 0;
                }
                return null;
            }
            catch { return null; }
        }
    }
}
