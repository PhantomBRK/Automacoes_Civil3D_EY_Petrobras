using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using SOLIDOS;

namespace AutomacoesCivil3D
{
    // Diagnóstico de CONECTIVIDADE da rede SOLIDOS: encontra os componentes
    // desconexos (sub-redes não ligadas por tubo) e, para cada par de componentes,
    // o par de nós mais próximos no plano — candidatos a "religação" no DWG.
    //
    // Use quando o dimensionamento por jusante deixa de fora parte da rede: cada
    // componente só é dimensionado a partir de um âncora DENTRO dele. Se a espinha
    // coletora está num componente separado dos ramos, há uma quebra física aqui.
    //
    // Saída: relatório em TXT no Desktop + marcação opcional (zoom) no maior gap.
    public class SolidosDiagnosticarConectividade
    {
        private const int GuardMax = 20000;

        [CommandMethod("SOL_DIAGNOSTICAR_CONECTIVIDADE")]
        public void Diagnosticar()
        {
            Document doc = Manager.DocCad;
            Editor ed = Manager.DocEditor;

            // 1) Coleta todos os tubos (InPart/OutPart) e nós (Location) do desenho.
            var tubos = new List<TuboLink>();
            var locById = new Dictionary<ObjectId, GeometryPoint>();
            var familiaById = new Dictionary<ObjectId, string>();

            ObjectId[] ids = GetAllEntityIds(doc.Database);
            ed.WriteMessage($"\nVarrendo {ids.Length} entidades...");

            foreach (ObjectId id in ids)
            {
                // É tubo? tem InPart+OutPart.
                ObjectId inPart = TryGetObjId(id, "InPart");
                ObjectId outPart = TryGetObjId(id, "OutPart");
                if (!inPart.IsNull && !outPart.IsNull)
                {
                    tubos.Add(new TuboLink { Id = id, In = inPart, Out = outPart });
                    continue;
                }
                // É nó? tem Location.
                var loc = TryGetParam<GeometryPoint>(id, "Location");
                if (loc != null)
                {
                    locById[id] = loc;
                    familiaById[id] = TryGetString(id, "FamilyName") ?? "";
                }
            }

            if (tubos.Count == 0)
            {
                ed.WriteMessage("\nNenhum tubo (InPart/OutPart) encontrado.");
                return;
            }

            // 2) Componentes conexos (união não-direcionada por tubo).
            var adj = new Dictionary<ObjectId, List<ObjectId>>();
            void AddAdj(ObjectId a, ObjectId b)
            {
                if (!adj.TryGetValue(a, out var l)) { l = new List<ObjectId>(); adj[a] = l; }
                l.Add(b);
            }
            foreach (var t in tubos) { AddAdj(t.In, t.Out); AddAdj(t.Out, t.In); }

            var compById = new Dictionary<ObjectId, int>();
            var componentes = new List<List<ObjectId>>();
            foreach (var start in adj.Keys)
            {
                if (compById.ContainsKey(start)) continue;
                var comp = new List<ObjectId>();
                int cidx = componentes.Count;
                var stack = new Stack<ObjectId>();
                stack.Push(start); compById[start] = cidx;
                int guard = 0;
                while (stack.Count > 0 && guard++ < GuardMax)
                {
                    var x = stack.Pop(); comp.Add(x);
                    foreach (var y in adj[x])
                        if (!compById.ContainsKey(y)) { compById[y] = cidx; stack.Push(y); }
                }
                componentes.Add(comp);
            }

            componentes = componentes.OrderByDescending(c => c.Count).ToList();
            // Reindexa compById conforme nova ordem.
            compById.Clear();
            for (int i = 0; i < componentes.Count; i++)
                foreach (var nd in componentes[i]) compById[nd] = i;

            // 3) Para cada componente: exutório (nó mais baixo) e nº de caixas.
            var sb = new StringBuilder();
            sb.AppendLine("=== DIAGNÓSTICO DE CONECTIVIDADE SOLIDOS ===");
            sb.AppendLine($"Hora: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Tubos: {tubos.Count}   Nós com Location: {locById.Count}");
            sb.AppendLine($"Componentes desconexos: {componentes.Count}");
            sb.AppendLine();

            for (int i = 0; i < componentes.Count; i++)
            {
                var comp = componentes[i];
                int nCaixas = comp.Count(nd => (familiaById.TryGetValue(nd, out var f) ? f : "").ToUpperInvariant().Contains("CAIXA"));
                ObjectId exut = ObjectId.Null; double zmin = double.MaxValue;
                foreach (var nd in comp)
                    if (locById.TryGetValue(nd, out var p) && p.Z < zmin) { zmin = p.Z; exut = nd; }

                sb.AppendLine($"--- Componente #{i + 1}: {comp.Count} nós, {nCaixas} caixas ---");
                if (!exut.IsNull)
                    sb.AppendLine($"    Exutório (mais baixo): Handle={exut.Handle}  Z={zmin:F3}  [{(familiaById.TryGetValue(exut, out var ef) ? ef : "")}]");
            }
            sb.AppendLine();

            // 4) Pares de componentes: par de nós mais próximos no plano (candidato a religar).
            sb.AppendLine("=== PONTOS DE QUEBRA (par de nós mais próximos entre componentes) ===");
            sb.AppendLine("Religue estes pares no SOLIDOS/Civil 3D para unir a rede.");
            sb.AppendLine();

            // Limita a comparação aos N maiores componentes para não explodir.
            int maxComp = Math.Min(componentes.Count, 8);
            ObjectId melhorA = ObjectId.Null, melhorB = ObjectId.Null;
            double melhorGap = double.MaxValue;

            for (int a = 0; a < maxComp; a++)
            {
                for (int b = a + 1; b < maxComp; b++)
                {
                    double bestD = double.MaxValue;
                    ObjectId bestI = ObjectId.Null, bestJ = ObjectId.Null;
                    foreach (var na in componentes[a])
                    {
                        if (!locById.TryGetValue(na, out var pa)) continue;
                        foreach (var nb in componentes[b])
                        {
                            if (!locById.TryGetValue(nb, out var pb)) continue;
                            double dx = pa.X - pb.X, dy = pa.Y - pb.Y;
                            double d = Math.Sqrt(dx * dx + dy * dy);
                            if (d < bestD) { bestD = d; bestI = na; bestJ = nb; }
                        }
                    }
                    if (!bestI.IsNull)
                    {
                        sb.AppendLine($"Comp#{a + 1} <-> Comp#{b + 1}: gap mínimo {bestD:F2} m");
                        sb.AppendLine($"    {bestI.Handle} [{Fam(familiaById, bestI)}]  <->  {bestJ.Handle} [{Fam(familiaById, bestJ)}]");
                        if (bestD < melhorGap) { melhorGap = bestD; melhorA = bestI; melhorB = bestJ; }
                    }
                }
            }

            string outPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"solidos_conectividade_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            try { File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false)); }
            catch { /* ignora */ }

            ed.WriteMessage($"\n{componentes.Count} componente(s) desconexo(s).");
            for (int i = 0; i < Math.Min(componentes.Count, 6); i++)
            {
                int nCaixas = componentes[i].Count(nd => Fam(familiaById, nd).ToUpperInvariant().Contains("CAIXA"));
                ed.WriteMessage($"\n  Comp#{i + 1}: {componentes[i].Count} nós, {nCaixas} caixas");
            }
            ed.WriteMessage($"\nRelatório: {outPath}");

            // 5) Oferece dar zoom no menor gap (a quebra mais provável de ser ligação faltante).
            if (!melhorA.IsNull && !melhorB.IsNull)
            {
                ed.WriteMessage($"\nMenor gap: {melhorGap:F2} m entre {melhorA.Handle} e {melhorB.Handle}.");
                PromptKeywordOptions pko = new PromptKeywordOptions("\nDar zoom nesse ponto de quebra? [Sim/Nao] <Sim>: ");
                pko.Keywords.Add("Sim"); pko.Keywords.Add("Nao"); pko.Keywords.Default = "Sim"; pko.AllowNone = true;
                PromptResult pr = ed.GetKeywords(pko);
                if (pr.Status != PromptStatus.OK || pr.StringResult != "Nao")
                {
                    ZoomNoPar(ed, locById, melhorA, melhorB);
                }
            }
        }

        private static string Fam(Dictionary<ObjectId, string> map, ObjectId id)
            => map.TryGetValue(id, out var f) ? f : "";

        private static void ZoomNoPar(Editor ed, Dictionary<ObjectId, GeometryPoint> loc, ObjectId a, ObjectId b)
        {
            if (!loc.TryGetValue(a, out var pa) || !loc.TryGetValue(b, out var pb)) return;
            double minX = Math.Min(pa.X, pb.X), maxX = Math.Max(pa.X, pb.X);
            double minY = Math.Min(pa.Y, pb.Y), maxY = Math.Max(pa.Y, pb.Y);
            double mrg = Math.Max(10.0, Math.Max(maxX - minX, maxY - minY));
            try
            {
                using (ViewTableRecord view = ed.GetCurrentView())
                {
                    view.CenterPoint = new Autodesk.AutoCAD.Geometry.Point2d((minX + maxX) / 2, (minY + maxY) / 2);
                    view.Width = (maxX - minX) + 2 * mrg;
                    view.Height = (maxY - minY) + 2 * mrg;
                    ed.SetCurrentView(view);
                }
            }
            catch { /* ignora */ }
        }

        // -------- Helpers SOLIDOS --------

        private sealed class TuboLink { public ObjectId Id; public ObjectId In; public ObjectId Out; }

        private static ObjectId TryGetObjId(ObjectId nodeId, string prop)
        {
            var v = TryGetParam<ObjectId>(nodeId, prop);
            return v;
        }

        private static string TryGetString(ObjectId id, string prop)
        {
            try
            {
                Type t = null;
                object v = SolidosAPI.GetNodeParam(id, prop, null, ref t);
                if (v == null) return null;
                string s = v as string ?? v.ToString();
                return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            }
            catch { return null; }
        }

        private static T TryGetParam<T>(ObjectId nodeId, string propName)
        {
            try
            {
                Type t = null;
                object v = SolidosAPI.GetNodeParam(nodeId, propName, null, ref t);
                if (v == null) return default;
                if (v is T tv) return tv;
                return default;
            }
            catch { return default; }
        }

        private static ObjectId[] GetAllEntityIds(Database db)
        {
            var ids = new List<ObjectId>();
            using (Transaction t = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)t.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)t.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId oid in ms) ids.Add(oid);
                t.Commit();
            }
            return ids.ToArray();
        }
    }
}
