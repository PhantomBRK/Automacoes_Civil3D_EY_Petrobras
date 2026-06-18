using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using SOLIDOS;

namespace AutomacoesCivil3D
{
    // Dumpa N dispositivos SOLIDOS do desenho ativo (não de .sbd externo) em TXT
    // formatado, com todas as propriedades via SolidosAPI.ListProperties +
    // GetNodeParam por nome. Saída: Desktop/solidos_dispositivos_<timestamp>.txt
    //
    // Uso: SOL_DUMP_DISPOSITIVOS — pede seleção; tecle [Tab] na seleção pra pegar
    // todos os SOLIDOS do desenho via PromptSelectionOptions.SelectAll.
    public class SolidosDumpDispositivos
    {
        [CommandMethod("SOL_DUMP_DISPOSITIVOS")]
        public void Dump()
        {
            Document doc = Manager.DocCad;
            Editor ed = Manager.DocEditor;

            PromptSelectionOptions pso = new PromptSelectionOptions
            {
                MessageForAdding = "\nSelecione os dispositivos SOLIDOS a despejar (ENTER seleciona tudo): ",
                AllowDuplicates = false
            };
            PromptSelectionResult psr = ed.GetSelection(pso);

            ObjectId[] ids;
            if (psr.Status == PromptStatus.OK)
            {
                ids = psr.Value.GetObjectIds();
            }
            else if (psr.Status == PromptStatus.Error || psr.Status == PromptStatus.None)
            {
                // Vazio = pega todos os objetos do desenho. Vai filtrar depois quem
                // responde a ListProperties (= dispositivo SOLIDOS).
                ids = GetAllEntityIds(doc.Database);
                if (ids.Length == 0)
                {
                    ed.WriteMessage("\nDesenho vazio.");
                    return;
                }
                ed.WriteMessage($"\nSem seleção — varrendo {ids.Length} entidades do desenho.");
            }
            else
            {
                ed.WriteMessage("\nCancelado.");
                return;
            }

            string outPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"solidos_dispositivos_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== DUMP SOLIDOS DISPOSITIVOS ===");
            sb.AppendLine($"Documento: {doc.Name}");
            sb.AppendLine($"Hora:      {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Entidades selecionadas: {ids.Length}");
            sb.AppendLine();

            int totalDisp = 0;
            int totalProps = 0;
            int totalSkipped = 0;
            Dictionary<string, int> familiasCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (ObjectId id in ids)
            {
                List<string> props;
                try
                {
                    props = SolidosAPI.ListProperties(id);
                }
                catch
                {
                    totalSkipped++;
                    continue;
                }
                if (props == null || props.Count == 0)
                {
                    totalSkipped++;
                    continue;
                }

                totalDisp++;
                sb.AppendLine("------------------------------------------------------------");
                sb.AppendLine($"[{totalDisp}] Handle={id.Handle}  ObjectId={id}");

                // Pega FamilyName cedo pra contagem agregada.
                string familyName = TryReadString(id, "FamilyName") ??
                                    TryReadString(id, "Family") ??
                                    TryReadString(id, "Familia") ??
                                    "<sem FamilyName>";
                if (!familiasCount.ContainsKey(familyName)) familiasCount[familyName] = 0;
                familiasCount[familyName]++;

                sb.AppendLine($"     FamilyName: {familyName}");
                sb.AppendLine($"     Propriedades ({props.Count}):");

                // Ordena alfabeticamente pra dump ficar comparável entre dispositivos.
                props.Sort(StringComparer.OrdinalIgnoreCase);

                foreach (string p in props)
                {
                    totalProps++;
                    bool ro = SolidosVazaoCombateIncendioSOL.TryIsReadOnly(id, p);
                    string flag = ro ? "RO" : "RW";

                    object raw;
                    try
                    {
                        Type t = null;
                        raw = SolidosAPI.GetNodeParam(id, p, null, ref t);
                    }
                    catch (System.Exception ex)
                    {
                        sb.AppendLine($"       [{flag}] {p,-40} <EXCEPTION: {ex.GetType().Name}: {ex.Message}>");
                        continue;
                    }

                    sb.AppendLine($"       [{flag}] {p,-40} {FormatValue(raw)}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("=== RESUMO ===");
            sb.AppendLine($"Dispositivos despejados:  {totalDisp}");
            sb.AppendLine($"Entidades sem props (skip):{totalSkipped}");
            sb.AppendLine($"Propriedades totais:      {totalProps}");
            sb.AppendLine();
            sb.AppendLine("Contagem por FamilyName:");
            foreach (var kv in SortDescByValue(familiasCount))
            {
                sb.AppendLine($"  {kv.Value,4}  {kv.Key}");
            }

            try
            {
                File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
                ed.WriteMessage($"\nDump OK ({totalDisp} dispositivos, {totalProps} props)");
                ed.WriteMessage($"\n  -> {outPath}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nERRO ao gravar: {ex.Message}");
            }
        }

        private static string TryReadString(ObjectId id, string prop)
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

        private static string FormatValue(object raw)
        {
            if (raw == null) return "<null>";
            if (raw is string s) return $"\"{s}\"";
            if (raw is double d) return d.ToString("R", CultureInfo.InvariantCulture);
            if (raw is float f)  return f.ToString("R", CultureInfo.InvariantCulture);
            if (raw is bool b)   return b ? "true" : "false";
            if (raw is ObjectId oid) return $"ObjectId(handle={oid.Handle})";
            if (raw is GeometryPoint gp)
                return $"GeometryPoint({gp.X:F3}, {gp.Y:F3}, {gp.Z:F3})";

            // Coleções: lista os elementos.
            if (raw is IEnumerable en && !(raw is string))
            {
                List<string> parts = new List<string>();
                int n = 0;
                foreach (object item in en)
                {
                    if (n++ >= 20) { parts.Add("..."); break; }
                    parts.Add(FormatValue(item));
                }
                return $"[{raw.GetType().Name}: {string.Join(", ", parts)}]";
            }

            return $"({raw.GetType().Name}) {raw}";
        }

        private static IEnumerable<KeyValuePair<string, int>> SortDescByValue(Dictionary<string, int> d)
        {
            var list = new List<KeyValuePair<string, int>>(d);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            return list;
        }

        private static ObjectId[] GetAllEntityIds(Database db)
        {
            List<ObjectId> ids = new List<ObjectId>();
            using (Transaction t = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)t.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)t.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId oid in ms) ids.Add(oid);
                t.Commit();
            }
            return ids.ToArray();
        }
    }
}
