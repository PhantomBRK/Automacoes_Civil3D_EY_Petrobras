using System;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace RotinasPetrobras.Diagnostics
{
    public class DumpSolidosXRecord
    {
        // Dumpa um .sbd / .dwg ESCOLHIDO (side database). Se o arquivo estiver aberto no AutoCAD/Civil
        // dá eFileSharingViolation -> use DumpSolidosXmlAtivo no DWG aberto, ou DumpSolidosXmlForcado
        // que tenta FileShare.ReadWrite (funciona em alguns casos com sessão concorrente).
        [CommandMethod("DumpSolidosXml", CommandFlags.Session)]
        public void Dump() => DumpSide(FileShare.Read);

        [CommandMethod("DumpSolidosXmlForcado", CommandFlags.Session)]
        public void DumpForcado() => DumpSide(FileShare.ReadWrite);

        // Dumpa o DWG ATUALMENTE ABERTO. Sem file picker, sem conflito de lock.
        // É o que você quer no fluxo dos Relatórios (DWG já carregado com a config do SOLIDOS).
        [CommandMethod("DumpSolidosXmlAtivo")]
        public void DumpAtivo()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            string sourcePath = string.IsNullOrEmpty(doc.Name) ? "(DWG ativo - sem path)" : doc.Name;
            string outPath = BuildOutPath();

            var sb = new StringBuilder();
            HeaderInto(sb, sourcePath);

            int totalXrecords = 0, totalDicts = 0;
            try
            {
                using (doc.LockDocument())
                using (var t = db.TransactionManager.StartTransaction())
                {
                    var nod = (DBDictionary)t.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                    WalkDict(t, nod, "NOD", sb, 0, ref totalDicts, ref totalXrecords);
                    t.Commit();
                }
                FinishOk(sb, outPath, ed, totalDicts, totalXrecords);
            }
            catch (System.Exception ex)
            {
                FinishErr(sb, outPath, ed, ex);
            }
        }

        private void DumpSide(FileShare share)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            var pfo = new PromptOpenFileOptions("\nSelecione o arquivo .sbd/.dwg do SOLIDOS Builder")
            {
                Filter = "SOLIDOS Builder (*.sbd;*.dwg)|*.sbd;*.dwg|Todos (*.*)|*.*",
                DialogCaption = "Selecionar arquivo SOLIDOS Builder"
            };
            var fr = ed.GetFileNameForOpen(pfo);
            if (fr.Status != PromptStatus.OK) return;
            string sourcePath = fr.StringResult;

            string outPath = BuildOutPath();
            var sb = new StringBuilder();
            HeaderInto(sb, sourcePath);

            int totalXrecords = 0, totalDicts = 0;
            try
            {
                using (var db = new Database(false, true))
                {
                    db.ReadDwgFile(sourcePath, share, true, "");
                    db.CloseInput(true);

                    using (var t = db.TransactionManager.StartTransaction())
                    {
                        var nod = (DBDictionary)t.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                        WalkDict(t, nod, "NOD", sb, 0, ref totalDicts, ref totalXrecords);
                        t.Commit();
                    }
                }
                FinishOk(sb, outPath, ed, totalDicts, totalXrecords);
            }
            catch (System.Exception ex)
            {
                FinishErr(sb, outPath, ed, ex);
            }
        }

        private static string BuildOutPath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "solidos_xrecord_dump.txt");

        private static void HeaderInto(StringBuilder sb, string sourcePath)
        {
            sb.AppendLine("=== DUMP SOLIDOS XRecord ===");
            sb.AppendLine($"Source: {sourcePath}");
            sb.AppendLine($"Time:   {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
        }

        private static void FinishOk(StringBuilder sb, string outPath, Editor ed, int totalDicts, int totalXrecords)
        {
            sb.AppendLine();
            sb.AppendLine("=== RESUMO ===");
            sb.AppendLine($"Dictionaries percorridos: {totalDicts}");
            sb.AppendLine($"XRecords encontrados:     {totalXrecords}");
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            ed.WriteMessage($"\nDump OK -> {outPath}");
            ed.WriteMessage($"\n  Dicts: {totalDicts}  XRecords: {totalXrecords}  Chars: {sb.Length}");
        }

        private static void FinishErr(StringBuilder sb, string outPath, Editor ed, System.Exception ex)
        {
            sb.AppendLine();
            sb.AppendLine("=== ERRO ===");
            sb.AppendLine(ex.ToString());
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            ed.WriteMessage($"\nERRO: {ex.Message}");
            ed.WriteMessage($"\nDump parcial salvo em: {outPath}");
        }

        private void WalkDict(Transaction t, DBDictionary dict, string path, StringBuilder sb, int depth,
            ref int totalDicts, ref int totalXrecords)
        {
            totalDicts++;
            if (depth > 30)
            {
                sb.AppendLine($"[!] Profundidade max atingida em {path}");
                return;
            }

            foreach (DBDictionaryEntry entry in dict)
            {
                DBObject obj;
                try { obj = t.GetObject(entry.Value, OpenMode.ForRead); }
                catch (System.Exception ex)
                {
                    sb.AppendLine($"[ERR] {path}/{entry.Key} -> {ex.Message}");
                    continue;
                }

                string newPath = $"{path}/{entry.Key}";

                if (obj is DBDictionary subDict)
                {
                    sb.AppendLine($"[DICT] {newPath}");
                    WalkDict(t, subDict, newPath, sb, depth + 1, ref totalDicts, ref totalXrecords);
                }
                else if (obj is Xrecord xrec)
                {
                    totalXrecords++;
                    sb.AppendLine();
                    sb.AppendLine($"[XREC] {newPath}");
                    sb.AppendLine($"  Handle: {xrec.Handle}");
                    var rb = xrec.Data;
                    if (rb == null)
                    {
                        sb.AppendLine("  <Data null>");
                    }
                    else
                    {
                        int i = 0;
                        foreach (TypedValue tv in rb)
                        {
                            string val = tv.Value?.ToString() ?? "<null>";
                            string typeName = ((DxfCode)tv.TypeCode).ToString();
                            sb.AppendLine($"  [{i,3}] code={tv.TypeCode} ({typeName})  value={val}");
                            i++;
                        }
                    }
                }
                else
                {
                    sb.AppendLine($"[?]    {newPath} -> {obj.GetType().Name}");
                }
            }
        }
    }
}
