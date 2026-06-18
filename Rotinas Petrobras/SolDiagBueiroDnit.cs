using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using SOLIDOS;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace RotinasPetrobras.Diagnostics
{
    /// <summary>
    /// SOL_DIAG_BUEIRO_DNIT — diagnóstico DNIT de bueiros tubulares (BSTC) contra o terreno.
    ///
    /// Para cada bueiro selecionado, amostra a superfície do ATERRO/greide e a do TERRENO
    /// natural ao longo do eixo do tubo e calcula, conforme critérios DNIT:
    ///  - Comprimento sugerido = pé-de-talude a pé-de-talude (onde aterro ≈ terreno);
    ///  - Recobrimento sob a pista (no ponto de maior cota do aterro = coroamento);
    ///  - ENVELOPAR = recobrimento < 0,60 m (gatilho de envelopamento em concreto);
    ///  - Declividade e verificação i >= 0,5%;
    ///  - Altura/cota das alas (= terreno em cada boca);
    ///  - Classe NBR 8890 sugerida pela altura de aterro (TABELA PLACEHOLDER — ajustar).
    ///
    /// NÃO altera geometria (só relatório .txt + mensagens). Premissas combinadas:
    /// talude de aterro 1V:1,5H (m=1,5), recobrimento mínimo 0,60 m.
    /// O ajuste das ALAS ao terreno já é feito pela fórmula do device
    /// (REGRAS_DNIT_BUEIRO_SOLIDOS.xml: AlturaAla/CotaTopoAla = terreno na ponta).
    /// </summary>
    public class SolDiagBueiroDnit
    {
        private const double RecobrimentoMin = 0.60;   // m (DNIT)
        private const double DeclividadeMin  = 0.005;  // 0,5%
        private const double TaludeM         = 1.5;    // 1V:1,5H (informativo p/ comprimento teórico)
        private const int    Amostras        = 81;     // pontos ao longo do eixo
        private const double TolPe           = 0.05;   // m: |aterro-terreno| <= tol => pé-de-talude

        [CommandMethod("SOL_DIAG_BUEIRO_DNIT", CommandFlags.Modal)]
        public void Run()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            // 1) Superfícies (aterro/greide e terreno natural)
            ObjectId atId = PromptSurface(ed, "\nSelecione a superfície do ATERRO/greide (estrada): ");
            if (atId.IsNull) { ed.WriteMessage("\nCancelado."); return; }
            ObjectId terrId = PromptSurface(ed, "\nSelecione a superfície do TERRENO natural: ");
            if (terrId.IsNull) { ed.WriteMessage("\nCancelado."); return; }

            // 2) Bueiros (seleção livre; filtro por StartPoint/EndPoint/Diametro)
            var pso = new PromptSelectionOptions { MessageForAdding = "\nSelecione os bueiros (tubos): " };
            PromptSelectionResult psr = ed.GetSelection(pso);
            if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nCancelado."); return; }

            var log = new List<string>();
            log.Add($"DIAGNÓSTICO DNIT BUEIROS  -  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            log.Add($"Critérios: recob.min={RecobrimentoMin:0.00} m | i.min={DeclividadeMin*100:0.0}% | talude 1V:{TaludeM:0.0}H");
            log.Add(new string('-', 78));

            int nBueiro = 0, nEnvelopar = 0, nDeclivBaixa = 0, nCompDif = 0, nForaSup = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var atSurf = tr.GetObject(atId, OpenMode.ForRead) as TinSurface;
                var terrSurf = tr.GetObject(terrId, OpenMode.ForRead) as TinSurface;
                if (atSurf == null || terrSurf == null)
                {
                    ed.WriteMessage("\nUma das seleções não é TinSurface.");
                    return;
                }

                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null) continue;
                    ObjectId id = so.ObjectId;

                    if (!TryGetPoint(id, "StartPoint", out var sp) || !TryGetPoint(id, "EndPoint", out var ep))
                        continue; // não é tubo/bueiro SOLIDOS
                    double diam = GetDouble(id, "Diametro", double.NaN);
                    if (double.IsNaN(diam)) diam = GetDouble(id, "Diameter", 1.0);
                    double parede = GetDouble(id, "Parede", 0.08);
                    string catalogo = GetString(id, "Catalogo");
                    string nome = GetString(id, "Code");
                    if (string.IsNullOrWhiteSpace(nome)) nome = GetString(id, "Name");
                    nBueiro++;

                    double planLen = Hyp(sp.X - ep.X, sp.Y - ep.Y);
                    if (planLen < 1e-6) { log.Add($"[{nBueiro}] {nome}: tubo degenerado (len~0), pulado."); continue; }

                    // amostra eixo
                    double crownAt = double.NegativeInfinity; int crownIdx = -1;
                    int peMontIdx = -1, peJusIdx = -1; bool foraSup = false;
                    var at = new double[Amostras]; var te = new double[Amostras];
                    var inv = new double[Amostras];
                    for (int i = 0; i < Amostras; i++)
                    {
                        double t = (double)i / (Amostras - 1);
                        double x = sp.X + (ep.X - sp.X) * t;
                        double y = sp.Y + (ep.Y - sp.Y) * t;
                        inv[i] = sp.Z + (ep.Z - sp.Z) * t;
                        at[i] = SafeElev(atSurf, x, y, out bool okA);
                        te[i] = SafeElev(terrSurf, x, y, out bool okT);
                        if (!okA || !okT) { foraSup = true; continue; }
                        if (at[i] > crownAt) { crownAt = at[i]; crownIdx = i; }
                    }
                    if (foraSup) nForaSup++;

                    // pé-de-talude: a partir do centro p/ cada lado, 1º ponto onde aterro~terreno
                    int mid = crownIdx >= 0 ? crownIdx : Amostras / 2;
                    for (int i = mid; i >= 0; i--) { if (Math.Abs(at[i] - te[i]) <= TolPe) { peMontIdx = i; break; } }
                    for (int i = mid; i < Amostras; i++) { if (Math.Abs(at[i] - te[i]) <= TolPe) { peJusIdx = i; break; } }

                    double compSug = double.NaN;
                    if (peMontIdx >= 0 && peJusIdx >= 0)
                        compSug = planLen * (double)(peJusIdx - peMontIdx) / (Amostras - 1);

                    // recobrimento sob a pista (no coroamento)
                    double recobPista = double.NaN, hAterro = double.NaN;
                    if (crownIdx >= 0)
                    {
                        double topo = inv[crownIdx] + diam + 2 * parede;
                        recobPista = at[crownIdx] - topo;
                        hAterro = at[crownIdx] - inv[crownIdx];
                    }
                    bool envelopar = !double.IsNaN(recobPista) && recobPista < RecobrimentoMin;
                    if (envelopar) nEnvelopar++;

                    double declive = Math.Abs(sp.Z - ep.Z) / planLen;
                    bool decliveOk = declive >= DeclividadeMin;
                    if (!decliveOk) nDeclivBaixa++;

                    // alas (cota topo = terreno na ponta; altura = topo - invert)
                    double alaMont = at[0] - inv[0];
                    double alaJus = at[Amostras - 1] - inv[Amostras - 1];

                    bool compDif = !double.IsNaN(compSug) && Math.Abs(compSug - planLen) > 0.30;
                    if (compDif) nCompDif++;

                    string classe = SugereClasse(hAterro);

                    log.Add($"[{nBueiro}] {(string.IsNullOrWhiteSpace(nome) ? "(sem código)" : nome)}  DN{catalogo}");
                    log.Add($"      Comprimento atual : {planLen,7:0.00} m" +
                            (double.IsNaN(compSug) ? "   (pé-de-talude não encontrado nas superfícies)" :
                             $"   | sugerido (pé-a-pé): {compSug,7:0.00} m  {(compDif ? "<< AJUSTAR" : "ok")}"));
                    log.Add($"      Recobr. sob pista : {Fmt(recobPista)} m   => {(envelopar ? "ENVELOPAR (recob<0,60)" : "ok")}");
                    log.Add($"      Declividade       : {declive*100,6:0.00}%   => {(decliveOk ? "ok" : "<< ABAIXO de 0,5%")}");
                    log.Add($"      Altura aterro     : {Fmt(hAterro)} m   | Classe sugerida: {classe}");
                    log.Add($"      Ala montante      : cota topo {at[0]:0.00}  altura {alaMont:0.00} m");
                    log.Add($"      Ala jusante       : cota topo {at[Amostras-1]:0.00}  altura {alaJus:0.00} m");
                    log.Add("");
                }

                tr.Commit();
            }

            log.Add(new string('-', 78));
            log.Add($"TOTAL: {nBueiro} bueiro(s) | {nEnvelopar} p/ ENVELOPAR | {nDeclivBaixa} com i<0,5% | " +
                    $"{nCompDif} com comprimento divergente | {nForaSup} com ponto fora das superfícies.");
            log.Add("Classe = TABELA PLACEHOLDER por altura de aterro — substituir pela do fabricante/álbum DNIT.");

            string outPath = Path.Combine(
                Path.GetDirectoryName(db.Filename) ?? Path.GetTempPath(),
                Path.GetFileNameWithoutExtension(db.Filename) + ".dnit_bueiros.txt");
            try { File.WriteAllLines(outPath, log, Encoding.UTF8); } catch { }

            foreach (var l in log) ed.WriteMessage("\n" + l);
            ed.WriteMessage($"\n\nRelatório salvo em: {outPath}");
        }

        // ===== Classe NBR 8890 sugerida pela altura de aterro (PLACEHOLDER — ajustar à tabela real) =====
        private static string SugereClasse(double hAterro)
        {
            if (double.IsNaN(hAterro)) return "?";
            if (hAterro <= 4.0) return "PA-1";
            if (hAterro <= 6.0) return "PA-2";
            if (hAterro <= 8.0) return "PA-3";
            return "PA-4";
        }

        // ===== Helpers SOLIDOS =====
        private static bool TryGetPoint(ObjectId id, string prop, out GeometryPoint pt)
        {
            pt = default;
            try
            {
                Type ty = null;
                object raw = SolidosAPI.GetNodeParam(id, prop, null, ref ty);
                if (raw is GeometryPoint gp) { pt = gp; return true; }
                return false;
            }
            catch { return false; }
        }

        private static double GetDouble(ObjectId id, string prop, double def)
        {
            try
            {
                Type ty = null;
                object raw = SolidosAPI.GetNodeParam(id, prop, null, ref ty);
                switch (raw)
                {
                    case double d: return d;
                    case float f: return f;
                    case int i: return i;
                    case long l: return l;
                    default:
                        if (raw != null && double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) return v;
                        return def;
                }
            }
            catch { return def; }
        }

        private static string GetString(ObjectId id, string prop)
        {
            try
            {
                Type ty = null;
                object raw = SolidosAPI.GetNodeParam(id, prop, null, ref ty);
                return raw == null ? string.Empty : (Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty).Trim();
            }
            catch { return string.Empty; }
        }

        // ===== Helpers gerais =====
        private static double SafeElev(TinSurface s, double x, double y, out bool ok)
        {
            try { double e = s.FindElevationAtXY(x, y); ok = true; return e; }
            catch { ok = false; return double.NaN; }
        }

        private static double Hyp(double a, double b) => Math.Sqrt(a * a + b * b);
        private static string Fmt(double v) => double.IsNaN(v) ? "  n/d " : v.ToString("0.00", CultureInfo.InvariantCulture);

        private static ObjectId PromptSurface(Editor ed, string msg)
        {
            var peo = new PromptEntityOptions(msg);
            peo.SetRejectMessage("\nNão é uma superfície TIN.");
            peo.AddAllowedClass(typeof(TinSurface), exactMatch: false);
            PromptEntityResult per = ed.GetEntity(peo);
            return per.Status == PromptStatus.OK ? per.ObjectId : ObjectId.Null;
        }
    }
}
