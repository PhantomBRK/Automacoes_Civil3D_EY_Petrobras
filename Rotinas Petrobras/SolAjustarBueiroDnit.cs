using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using SOLIDOS;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace RotinasPetrobras.Diagnostics
{
    /// <summary>
    /// SOL_AJUSTAR_BUEIRO_DNIT — adequa as PONTAS do bueiro tubular (BSTC) ao talude de aterro.
    ///
    /// Caminho C combinado com o device:
    ///   - HORIZONTAL: escorrega StartPoint/EndPoint ao longo do eixo até o PÉ-DE-TALUDE
    ///     (onde a superfície do ATERRO encontra a do TERRENO natural). Amostra as duas
    ///     superfícies ao longo do eixo ESTENDIDO além das pontas atuais (logo, alonga ou
    ///     encurta), achando o pé de cada lado.
    ///   - VERTICAL (ancoragem decidida): ancora StartTopElevation/EndTopElevation no talude.
    ///     Como no device EndTopElevation = EndPoint.Z + Altura/2, escreve-se
    ///     EndPoint.Z = S_aterro − Altura/2 (o topo interno do tubo encosta na superfície no pé).
    ///     A declividade resultante cai do próprio terreno → é só VERIFICADA (i >= 0,5%), nunca forçada.
    ///
    /// É MUTAÇÃO TOPOLÓGICA. Faz preview e PEDE CONFIRMAÇÃO antes de gravar. Só move a ponta
    /// cujo pé-de-talude foi encontrado dentro das duas superfícies; a outra fica intacta.
    /// O diagnóstico SOL_DIAG_BUEIRO_DNIT continua sendo o dry-run (não escreve nada).
    /// </summary>
    public class SolAjustarBueiroDnit
    {
        private const double DeclividadeMin = 0.005;  // 0,5% (autolimpeza DNIT)
        private const double PassoAmostra   = 0.25;   // m entre amostras ao longo do eixo
        private const double TolToe         = 0.08;   // m: |aterro-terreno| <= tol => candidato a pé
        private const double FolgaExtensao  = 30.0;   // m: quanto estende além de cada ponta na busca

        private sealed class Plano
        {
            public ObjectId Id;
            public string Nome;
            public double Altura;
            public GeometryPoint Sp, Ep;
            public bool MoverStart, MoverJus;
            public GeometryPoint NovoStart, NovoJus;
            public double CompAtual, CompNovo;
            public double Declive;
            public bool DecliveOk;
            public string Obs;
        }

        [CommandMethod("SOL_AJUSTAR_BUEIRO_DNIT", CommandFlags.Modal)]
        public void Run()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            // Superfícies por LISTA (não pick direto): coleta as TinSurface do desenho e mostra dropdowns.
            var superficies = ColetarSuperficies(db);
            if (superficies.Count == 0)
            {
                ed.WriteMessage("\nNenhuma superfície TIN no desenho.");
                return;
            }

            ObjectId atId, terrId;
            using (var dlg = new SurfacePickerForm(superficies))
            {
                if (Application.ShowModalDialog(dlg) != System.Windows.Forms.DialogResult.OK)
                {
                    ed.WriteMessage("\nCancelado.");
                    return;
                }
                atId = dlg.AterroId;
                terrId = dlg.TerrenoId;
            }
            if (atId.IsNull || terrId.IsNull) { ed.WriteMessage("\nSeleção de superfície inválida."); return; }
            if (atId == terrId)
            {
                ed.WriteMessage("\nAterro e terreno são a mesma superfície — escolha superfícies distintas.");
                return;
            }

            var pso = new PromptSelectionOptions { MessageForAdding = "\nSelecione os bueiros (tubos): " };
            PromptSelectionResult psr = ed.GetSelection(pso);
            if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nCancelado."); return; }

            var planos = new List<Plano>();

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

                    double altura = GetDouble(id, "Altura", double.NaN);
                    if (double.IsNaN(altura)) altura = GetDouble(id, "Diametro", double.NaN);
                    if (double.IsNaN(altura)) altura = GetDouble(id, "Diameter", double.NaN);

                    string nome = GetString(id, "Code");
                    if (string.IsNullOrWhiteSpace(nome)) nome = GetString(id, "Name");
                    if (string.IsNullOrWhiteSpace(nome)) nome = "(sem código)";

                    var p = new Plano { Id = id, Nome = nome, Altura = altura, Sp = sp, Ep = ep };

                    if (double.IsNaN(altura))
                    {
                        p.Obs = "sem 'Altura'/'Diametro' legível — pulado";
                        planos.Add(p);
                        continue;
                    }

                    AvaliarBueiro(p, atSurf, terrSurf);
                    planos.Add(p);
                }

                tr.Commit();
            }

            // ---- Preview ----
            ed.WriteMessage("\n" + new string('-', 74));
            ed.WriteMessage("\nPREVIEW SOL_AJUSTAR_BUEIRO_DNIT (ancora topo no talude; declividade só verificada)");
            int alvos = 0;
            foreach (var p in planos)
            {
                ed.WriteMessage($"\n[{p.Nome}]");
                if (!string.IsNullOrEmpty(p.Obs) && !(p.MoverStart || p.MoverJus))
                {
                    ed.WriteMessage($"   -> {p.Obs}");
                    continue;
                }
                ed.WriteMessage($"   Comprimento: {p.CompAtual,7:0.00} m -> {p.CompNovo,7:0.00} m");
                ed.WriteMessage($"   Montante: {(p.MoverStart ? $"cota topo {p.NovoStart.Z + p.Altura/2:0.000}  (move)" : "pé não encontrado — mantém")}");
                ed.WriteMessage($"   Jusante : {(p.MoverJus ? $"cota topo {p.NovoJus.Z + p.Altura/2:0.000}  (move)" : "pé não encontrado — mantém")}");
                ed.WriteMessage($"   Declividade resultante: {p.Declive*100,6:0.00}%  => {(p.DecliveOk ? "ok" : "<< ABAIXO de 0,5%")}");
                if (!string.IsNullOrEmpty(p.Obs)) ed.WriteMessage($"   Obs: {p.Obs}");
                if (p.MoverStart || p.MoverJus) alvos++;
            }
            ed.WriteMessage("\n" + new string('-', 74));

            if (alvos == 0)
            {
                ed.WriteMessage("\nNenhuma ponta para ajustar (pé-de-talude não encontrado nas superfícies). Nada gravado.");
                return;
            }

            // ---- Confirmação ----
            var pko = new PromptKeywordOptions($"\nAplicar ajuste em {alvos} bueiro(s)? Isto MOVE as pontas. ")
            { AllowNone = false };
            pko.Keywords.Add("Sim");
            pko.Keywords.Add("Nao");
            pko.Keywords.Default = "Nao";
            PromptResult pkr = ed.GetKeywords(pko);
            if (pkr.Status != PromptStatus.OK || pkr.StringResult != "Sim")
            {
                ed.WriteMessage("\nAbortado — nada foi alterado.");
                return;
            }

            // ---- Aplicação ----
            int gravados = 0;
            try
            {
                foreach (var p in planos)
                {
                    if (!(p.MoverStart || p.MoverJus)) continue;
                    var dic = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    if (p.MoverStart) dic["StartPoint"] = p.NovoStart;
                    if (p.MoverJus)   dic["EndPoint"]   = p.NovoJus;
                    if (dic.Count == 0) continue;
                    SolidosAPI.SetNodeParams(p.Id, dic);
                    gravados++;
                }
                SolidosAPI.DocCommit();
            }
            catch (SolidosException sx)
            {
                ed.WriteMessage($"\n[SOLIDOS] {sx.Message}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[ERRO] {ex.Message}");
            }

            ed.WriteMessage($"\nConcluído. Bueiros ajustados: {gravados}. Reabra/regen se a geometria não atualizar.");
        }

        /// <summary>
        /// Amostra ATERRO e TERRENO ao longo do eixo estendido, acha o pé-de-talude de cada
        /// lado (a partir do coroamento, primeiro ponto onde aterro≈terreno) e monta as novas
        /// pontas ancorando o TOPO no aterro: Z = S_aterro − Altura/2.
        /// </summary>
        private static void AvaliarBueiro(Plano p, TinSurface atSurf, TinSurface terrSurf)
        {
            double dx = p.Ep.X - p.Sp.X, dy = p.Ep.Y - p.Sp.Y;
            double planLen = Math.Sqrt(dx * dx + dy * dy);
            p.CompAtual = planLen;
            p.CompNovo = planLen;
            if (planLen < 1e-6) { p.Obs = "tubo degenerado (len~0)"; return; }

            double ux = dx / planLen, uy = dy / planLen;

            double sMin = -FolgaExtensao;
            double sMax = planLen + FolgaExtensao;
            int n = (int)Math.Ceiling((sMax - sMin) / PassoAmostra) + 1;

            var s = new double[n];
            var diff = new double[n];      // aterro - terreno
            var aterro = new double[n];
            var ok = new bool[n];
            int crown = -1; double crownEl = double.NegativeInfinity;

            for (int k = 0; k < n; k++)
            {
                s[k] = sMin + k * PassoAmostra;
                double x = p.Sp.X + ux * s[k];
                double y = p.Sp.Y + uy * s[k];
                double a = SafeElev(atSurf, x, y, out bool okA);
                double t = SafeElev(terrSurf, x, y, out bool okT);
                ok[k] = okA && okT;
                aterro[k] = a;
                diff[k] = ok[k] ? (a - t) : double.NaN;
                if (ok[k] && a > crownEl) { crownEl = a; crown = k; }
            }

            if (crown < 0) { p.Obs = "eixo fora das superfícies (sem coroamento)"; return; }

            // Pé-de-talude montante: do coroamento p/ trás (s decrescente)
            double sMont; bool foundMont = AcharPe(s, diff, ok, crown, -1, out sMont);
            // Pé-de-talude jusante: do coroamento p/ frente (s crescente)
            double sJus; bool foundJus = AcharPe(s, diff, ok, crown, +1, out sJus);

            if (foundMont)
            {
                double x = p.Sp.X + ux * sMont, y = p.Sp.Y + uy * sMont;
                double sa = SafeElev(atSurf, x, y, out bool okA);
                if (okA)
                {
                    p.NovoStart = new GeometryPoint(x, y, sa - p.Altura / 2.0);
                    p.MoverStart = true;
                }
            }
            if (foundJus)
            {
                double x = p.Sp.X + ux * sJus, y = p.Sp.Y + uy * sJus;
                double sa = SafeElev(atSurf, x, y, out bool okA);
                if (okA)
                {
                    p.NovoJus = new GeometryPoint(x, y, sa - p.Altura / 2.0);
                    p.MoverJus = true;
                }
            }

            // Pontas efetivas p/ comprimento e declividade (usa nova quando movida, atual quando não)
            GeometryPoint a0 = p.MoverStart ? p.NovoStart : p.Sp;
            GeometryPoint a1 = p.MoverJus ? p.NovoJus : p.Ep;
            double ndx = a1.X - a0.X, ndy = a1.Y - a0.Y;
            p.CompNovo = Math.Sqrt(ndx * ndx + ndy * ndy);
            p.Declive = p.CompNovo > 1e-6 ? Math.Abs(a0.Z - a1.Z) / p.CompNovo : 0.0;
            p.DecliveOk = p.Declive >= DeclividadeMin;
            if (!(p.MoverStart || p.MoverJus))
                p.Obs = "pé-de-talude não encontrado nas superfícies (verifique extensão das TINs)";
        }

        /// <summary>
        /// A partir de 'crown', caminha na direção 'dir' (+1 ou -1) até a 1ª amostra onde a
        /// diferença aterro-terreno cai a ~0 (pé-de-talude). Interpola entre a amostra interna
        /// (diff>0) e essa para estimar onde diff cruza 0. Para na borda da superfície.
        /// </summary>
        private static bool AcharPe(double[] s, double[] diff, bool[] ok, int crown, int dir, out double sPe)
        {
            sPe = 0;
            int prev = crown;
            for (int k = crown + dir; k >= 0 && k < s.Length; k += dir)
            {
                if (!ok[k]) return false; // saiu da superfície antes de achar o pé
                if (diff[k] <= TolToe)
                {
                    double dPrev = diff[prev]; // > tol (mais interno, ainda dentro do aterro)
                    double dCur = diff[k];     // <= tol
                    double denom = dPrev - dCur;
                    if (denom > 1e-9)
                    {
                        double frac = dPrev / denom; // interpola p/ diff = 0
                        if (frac < 0) frac = 0; if (frac > 1) frac = 1;
                        sPe = s[prev] + frac * (s[k] - s[prev]);
                    }
                    else sPe = s[k];
                    return true;
                }
                prev = k;
            }
            return false;
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

        private static double SafeElev(TinSurface ss, double x, double y, out bool ok)
        {
            try { double e = ss.FindElevationAtXY(x, y); ok = true; return e; }
            catch { ok = false; return double.NaN; }
        }

        /// <summary>Coleta todas as TinSurface do desenho como pares (nome, ObjectId), ordenados por nome.</summary>
        private static List<KeyValuePair<string, ObjectId>> ColetarSuperficies(Database db)
        {
            var lista = new List<KeyValuePair<string, ObjectId>>();
            var civ = AutomacoesCivil3D.Manager.DocCivil;
            if (civ == null) return lista;

            ObjectIdCollection ids;
            try { ids = civ.GetSurfaceIds(); }
            catch { return lista; }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is TinSurface s)
                        lista.Add(new KeyValuePair<string, ObjectId>(s.Name, id));
                }
                tr.Commit();
            }
            lista.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.CurrentCultureIgnoreCase));
            return lista;
        }
    }
}
