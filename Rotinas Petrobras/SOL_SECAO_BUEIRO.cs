using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using SOLIDOS;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using CivSampleLine = Autodesk.Civil.DatabaseServices.SampleLine;
using CivSampleLineGroup = Autodesk.Civil.DatabaseServices.SampleLineGroup;
using CivSectionView = Autodesk.Civil.DatabaseServices.SectionView;

namespace AutomacoesCivil3D
{
    /// <summary>
    /// SOL_SECAO_BUEIRO  (1 bueiro: pick do tubo + seleção do que projetar)
    /// SOL_SECAO_BUEIROS (rede toda: acha cada bueiro = tubo ligado a BOCA, projeta ele + conectados)
    ///
    /// Todas as sample lines vão p/ UM SampleLineGroup por alinhamento (edição em lote).
    /// Elevação = altura fixa (ALTURA) centrada na média dos dados (emula "from mean elevations").
    /// Offset = Automatic (user-specified desloca o conteúdo); largura vem da sample line.
    /// </summary>
    public class SecaoBueiro
    {
        private const double HALF_SAMPLE = 30.0;   // meia-largura da sample line (m) -> offset auto ~ ±30
        private const double ALTURA = 30.0;        // altura da section view (m)
        private const double DESLOC_X = 50.0;      // posição da SV = tubo + (DESLOC_X, DESLOC_Y)
        private const double DESLOC_Y = 50.0;
        private const double PROX_DISSIPADOR = 15.0; // raio p/ anexar dispositivo nao-conectado (dissipador)

        private class Job
        {
            public ObjectId Tubo;
            public Point3d Ini, Fim;
            public ObjectId Align;
            public HashSet<ObjectId> Set;
            public double RefZ;
            public ObjectId SvId;
        }

        // ==================================================================
        // 1 BUEIRO (manual)
        // ==================================================================
        [CommandMethod("SOL_SECAO_BUEIRO")]
        public static void SolSecaoBueiro()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var peo = new PromptEntityOptions("\n[SECAO] Selecione o CORPO do bueiro (tubo SOLIDOS): ");
            peo.SetRejectMessage("\n[SECAO] Selecione um dispositivo.");
            PromptEntityResult rTubo = ed.GetEntity(peo);
            if (rTubo.Status != PromptStatus.OK) return;

            var pso = new PromptSelectionOptions
            {
                MessageForAdding = "\n[SECAO] Selecione TUDO que sera projetado (bueiro, bocas/alas, dissipador) e ENTER: "
            };
            PromptSelectionResult rSel = ed.GetSelection(pso);
            if (rSel.Status != PromptStatus.OK) return;
            ObjectId[] alvos = rSel.Value.GetObjectIds();
            if (alvos.Length == 0) { ed.WriteMessage("\n[SECAO] Nada selecionado p/ projetar."); return; }

            if (!LerEixoBueiro(ed, rTubo.ObjectId, out Point3d ini, out Point3d fim, out ObjectId align))
            {
                ed.WriteMessage("\n[SECAO] Nao consegui ler StartPoint/EndPoint/RefAlign. Selecione o CORPO (tubo).");
                return;
            }
            if (!SecaoOpcoesDialog.Mostrar(out OpcoesSecao op)) { ed.WriteMessage("\n[SECAO] Cancelado."); return; }

            var jobs = new List<Job>
            {
                new Job { Tubo = rTubo.ObjectId, Ini = ini, Fim = fim, Align = align,
                          Set = new HashSet<ObjectId>(alvos), RefZ = Math.Min(ini.Z, fim.Z) }
            };
            ProcessarLote(doc, ed, db, jobs, op);
        }

        // ==================================================================
        // REDE TODA (massa)
        // ==================================================================
        [CommandMethod("SOL_SECAO_BUEIROS")]
        public static void SolSecaoBueiros()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            ed.WriteMessage("\n[SECAO] Varrendo a rede atras de bueiros...");
            List<ObjectId> candidatos = EnumInserts(db);

            var jobs = new List<Job>();
            var claimed = new HashSet<ObjectId>();
            foreach (ObjectId id in candidatos)
            {
                if (LerStr(id, "Family") == null) continue;        // nao é SOLIDOS
                Point3d? ini = LerPonto(id, "StartPoint");
                Point3d? fim = LerPonto(id, "EndPoint");
                if (ini == null || fim == null) continue;          // nao é linear
                ObjectId inP = LerObjId(id, "InPart");
                ObjectId outP = LerObjId(id, "OutPart");
                if (!EhBoca(inP) && !EhBoca(outP)) continue;       // nao é bueiro
                ObjectId align = LerObjId(id, "RefAlign");
                if (align.IsNull) continue;

                var set = new HashSet<ObjectId> { id };
                if (!inP.IsNull) set.Add(inP);
                if (!outP.IsNull) set.Add(outP);
                AddObjIdList(inP, "ConnectedDevices", set);
                AddObjIdList(outP, "ConnectedDevices", set);

                jobs.Add(new Job { Tubo = id, Ini = ini.Value, Fim = fim.Value, Align = align,
                                   Set = set, RefZ = Math.Min(ini.Value.Z, fim.Value.Z) });
                foreach (ObjectId s in set) claimed.Add(s);
            }

            if (jobs.Count == 0) { ed.WriteMessage("\n[SECAO] Nenhum bueiro (tubo ligado a BOCA BUEIRO) na rede."); return; }

            // dispositivos nao-conectados (dissipador) -> bueiro mais proximo dentro do raio
            foreach (ObjectId id in candidatos)
            {
                if (claimed.Contains(id)) continue;
                if (LerStr(id, "Family") == null) continue;
                Point3d? loc = LerPonto(id, "Location");
                if (loc == null) continue;
                int best = -1; double bd = double.MaxValue;
                for (int i = 0; i < jobs.Count; i++)
                {
                    double d = DistPontoSeg(loc.Value, jobs[i].Ini, jobs[i].Fim);
                    if (d < bd) { bd = d; best = i; }
                }
                if (best >= 0 && bd <= PROX_DISSIPADOR) { jobs[best].Set.Add(id); claimed.Add(id); }
            }

            ed.WriteMessage($"\n[SECAO] {jobs.Count} bueiro(s) encontrado(s).");
            if (!SecaoOpcoesDialog.Mostrar(out OpcoesSecao op)) { ed.WriteMessage("\n[SECAO] Cancelado."); return; }

            ProcessarLote(doc, ed, db, jobs, op);
        }

        // ==================================================================
        // Lote: 1 SLG por alinhamento -> sample lines + section views + elevacao + projecao
        // ==================================================================
        private static void ProcessarLote(AcDocument doc, Editor ed, Database db, List<Job> jobs, OpcoesSecao op)
        {
            if (jobs.Count == 0) { ed.WriteMessage("\n[SECAO] Nada a processar."); return; }

            // 1) um SLG por alinhamento (+ superficies escolhidas)
            var slgPorAlign = new Dictionary<ObjectId, ObjectId>();
            string ts = DateTime.Now.ToString("HHmmss");
            int gi = 0;
            foreach (Job j in jobs)
            {
                if (j.Align.IsNull || slgPorAlign.ContainsKey(j.Align)) continue;
                try
                {
                    using (doc.LockDocument())
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        ObjectId slgId = CivSampleLineGroup.Create($"SLG-BUEIROS-{ts}-{gi++}", j.Align);
                        var slg = (CivSampleLineGroup)tr.GetObject(slgId, OpenMode.ForWrite);
                        AplicarSuperficies(slg, op);
                        slgPorAlign[j.Align] = slgId;
                        tr.Commit();
                    }
                }
                catch (System.Exception ex) { ed.WriteMessage($"\n[SECAO] FALHA criando SLG: {ex.Message}"); }
            }

            // 2) sample line + section view (estilo, offset auto) por bueiro, todas no SLG do alinhamento
            foreach (Job j in jobs)
            {
                j.SvId = ObjectId.Null;
                if (!slgPorAlign.TryGetValue(j.Align, out ObjectId slgId)) continue;
                Vector3d raw = new Vector3d(j.Fim.X - j.Ini.X, j.Fim.Y - j.Ini.Y, 0);
                if (raw.Length < 1e-6) continue;
                Vector3d dir = raw.GetNormal();
                Point3d c = new Point3d((j.Ini.X + j.Fim.X) / 2.0, (j.Ini.Y + j.Fim.Y) / 2.0, 0);
                Point3d a = c - dir * HALF_SAMPLE, b = c + dir * HALF_SAMPLE;
                Point3d localSV = new Point3d(j.Ini.X + DESLOC_X, j.Ini.Y + DESLOC_Y, 0);

                try
                {
                    using (doc.LockDocument())
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        var pts = new Point2dCollection { new Point2d(a.X, a.Y), new Point2d(b.X, b.Y) };
                        ObjectId slId = CivSampleLine.Create("SL-" + DateTime.Now.ToString("HHmmssfff"), slgId, pts);
                        ObjectId svId = CivSectionView.Create("SV-" + DateTime.Now.ToString("HHmmssfff"), slId, localSV);

                        var sv = (CivSectionView)tr.GetObject(svId, OpenMode.ForWrite);
                        if (!op.EstiloSV.IsNull) { try { sv.StyleId = op.EstiloSV; } catch { } }
                        try { sv.IsOffsetRangeAutomatic = true; } catch { }
                        j.SvId = svId;
                        tr.Commit();
                    }
                }
                catch (System.Exception ex) { ed.WriteMessage($"\n[SECAO] FALHA SL/SV: {ex.Message}"); }
            }

            // 3) elevacao: altura ALTURA centrada na media dos dados (pos-amostragem)
            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (Job j in jobs)
                    {
                        if (j.SvId.IsNull) continue;
                        try
                        {
                            var sv = (CivSectionView)tr.GetObject(j.SvId, OpenMode.ForWrite);
                            double centro;
                            try
                            {
                                double aMin = sv.ElevationMin, aMax = sv.ElevationMax;
                                centro = (aMax > aMin + 1e-6) ? (aMin + aMax) / 2.0 : j.RefZ;
                            }
                            catch { centro = j.RefZ; }
                            sv.IsElevationRangeAutomatic = false;
                            sv.ElevationMin = centro - ALTURA / 2.0;
                            sv.ElevationMax = centro + ALTURA / 2.0;
                        }
                        catch { }
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex) { ed.WriteMessage($"\n[SECAO] (aviso) elevacao: {ex.Message}"); }

            // 4) projecoes (fora de transacao)
            int ok = 0;
            foreach (Job j in jobs)
            {
                if (j.SvId.IsNull) continue;
                try
                {
                    var arr = new ObjectId[j.Set.Count];
                    j.Set.CopyTo(arr);
                    SelectionSet ssA = SelectionSet.FromObjectIds(arr);
                    SelectionSet ssV = SelectionSet.FromObjectIds(new[] { j.SvId });
                    ed.Command("_SPROJECTION", ssA, "", ssV, "");
                    ok++;
                }
                catch (System.Exception ex) { ed.WriteMessage($"\n[SECAO] projecao FALHOU: {ex.Message}"); }
            }
            ed.WriteMessage($"\n[SECAO] CONCLUIDO: {ok}/{jobs.Count} secoes em {slgPorAlign.Count} grupo(s).");
        }

        private static void AplicarSuperficies(CivSampleLineGroup slg, OpcoesSecao op)
        {
            try
            {
                foreach (SectionSource s in slg.GetSectionSources())
                {
                    try
                    {
                        if (op.Superficies.Contains(s.SourceId))
                        {
                            s.IsSampled = true;
                            if (!op.EstiloSecao.IsNull) { try { s.StyleId = op.EstiloSecao; } catch { } }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ==================================================================
        // Helpers SOLIDOS / geometria
        // ==================================================================
        private static bool LerEixoBueiro(Editor ed, ObjectId deviceId,
            out Point3d pIni, out Point3d pFim, out ObjectId alignId)
        {
            pIni = Point3d.Origin; pFim = Point3d.Origin; alignId = ObjectId.Null;
            Point3d? gpIni = LerPonto(deviceId, "StartPoint");
            Point3d? gpFim = LerPonto(deviceId, "EndPoint");
            if (gpIni == null || gpFim == null) return false;
            pIni = gpIni.Value; pFim = gpFim.Value;
            alignId = LerObjId(deviceId, "RefAlign");
            return !alignId.IsNull;
        }

        private static Point3d? LerPonto(ObjectId deviceId, string prop)
        {
            try
            {
                Type t = null;
                object v = SolidosAPI.GetNodeParam(deviceId, prop, null, ref t);
                if (v is GeometryPoint gp) return new Point3d(gp.X, gp.Y, gp.Z);
            }
            catch { }
            return null;
        }

        private static ObjectId LerObjId(ObjectId deviceId, string prop)
        {
            try
            {
                Type t = null;
                object v = SolidosAPI.GetNodeParam(deviceId, prop, null, ref t);
                if (v is ObjectId o) return o;
            }
            catch { }
            return ObjectId.Null;
        }

        private static string LerStr(ObjectId deviceId, string prop)
        {
            try
            {
                Type t = null;
                object v = SolidosAPI.GetNodeParam(deviceId, prop, null, ref t);
                return v?.ToString();
            }
            catch { return null; }
        }

        private static void AddObjIdList(ObjectId deviceId, string prop, HashSet<ObjectId> into)
        {
            if (deviceId.IsNull) return;
            try
            {
                Type t = null;
                object v = SolidosAPI.GetNodeParam(deviceId, prop, null, ref t);
                if (v is System.Collections.IEnumerable en && !(v is string))
                    foreach (object o in en) if (o is ObjectId oo && !oo.IsNull) into.Add(oo);
            }
            catch { }
        }

        private static bool EhBoca(ObjectId deviceId)
        {
            if (deviceId.IsNull) return false;
            string fam = LerStr(deviceId, "Family");
            return fam != null && fam.ToUpperInvariant().Contains("BOCA");
        }

        private static List<ObjectId> EnumInserts(Database db)
        {
            var res = new List<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                    if (id.ObjectClass != null && id.ObjectClass.DxfName == "INSERT") res.Add(id);
                tr.Commit();
            }
            return res;
        }

        private static double DistPontoSeg(Point3d p, Point3d a, Point3d b)
        {
            double vx = b.X - a.X, vy = b.Y - a.Y;
            double wx = p.X - a.X, wy = p.Y - a.Y;
            double c2 = vx * vx + vy * vy;
            double tt = c2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, (vx * wx + vy * wy) / c2));
            double dx = p.X - (a.X + tt * vx), dy = p.Y - (a.Y + tt * vy);
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
