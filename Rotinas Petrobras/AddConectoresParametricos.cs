using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace RotinasPetrobras.Diagnostics
{
    public class AddConectoresParametricos
    {
        // 20 variaveis: 5 por parede, defaults equidistantes
        private static readonly (string Name, string Default)[] NewVars = new (string, string)[]
        {
            ("YCxL0", "-Largura/3"),  ("YCxL1", "-Largura/6"),  ("YCxL2", "0"),
            ("YCxL3", "Largura/6"),   ("YCxL4", "Largura/3"),
            ("YCxO0", "-Largura/3"),  ("YCxO1", "-Largura/6"),  ("YCxO2", "0"),
            ("YCxO3", "Largura/6"),   ("YCxO4", "Largura/3"),
            ("XCxS0", "-Comprimento/3"), ("XCxS1", "-Comprimento/6"), ("XCxS2", "0"),
            ("XCxS3", "Comprimento/6"),  ("XCxS4", "Comprimento/3"),
            ("XCxN0", "-Comprimento/3"), ("XCxN1", "-Comprimento/6"), ("XCxN2", "0"),
            ("XCxN3", "Comprimento/6"),  ("XCxN4", "Comprimento/3"),
        };

        [CommandMethod("AddConectoresParametricos", CommandFlags.Session)]
        public void Inject()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            var pfo = new PromptOpenFileOptions("\nSelecione o .sbd")
            {
                Filter = "SOLIDOS Builder (*.sbd;*.dwg)|*.sbd;*.dwg|Todos (*.*)|*.*",
                DialogCaption = "Selecionar SOLIDOS Builder"
            };
            var fr = ed.GetFileNameForOpen(pfo);
            if (fr.Status != PromptStatus.OK) return;
            string sourcePath = fr.StringResult;

            string backupPath = sourcePath + $".backup_{DateTime.Now:yyyyMMdd_HHmmss}";
            try { File.Copy(sourcePath, backupPath, overwrite: false); }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nERRO criando backup: {ex.Message}");
                return;
            }
            ed.WriteMessage($"\nBackup: {backupPath}");

            try
            {
                using (var db = new Database(false, true))
                {
                    db.ReadDwgFile(sourcePath, FileShare.Read, true, "");
                    db.CloseInput(true);

                    int parentVariaveisId;
                    string constructorKey;
                    int injected;

                    using (var t = db.TransactionManager.StartTransaction())
                    {
                        var nod = (DBDictionary)t.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

                        DBDictionary solidos = null;
                        foreach (DBDictionaryEntry e in nod)
                        {
                            if (e.Key.StartsWith("SOLIDOS"))
                            {
                                var obj = t.GetObject(e.Value, OpenMode.ForRead);
                                if (obj is DBDictionary d) { solidos = d; break; }
                            }
                        }
                        if (solidos == null) throw new System.Exception("Dict NOD/SOLIDOS_ nao encontrado");

                        Xrecord constructor = null;
                        constructorKey = null;
                        foreach (DBDictionaryEntry e in solidos)
                        {
                            if (e.Key.Contains("Constructor"))
                            {
                                var obj = t.GetObject(e.Value, OpenMode.ForWrite);
                                if (obj is Xrecord xr) { constructor = xr; constructorKey = e.Key; break; }
                            }
                        }
                        if (constructor == null) throw new System.Exception("Constructor XRecord nao encontrado");

                        var rb = constructor.Data;
                        if (rb == null) throw new System.Exception("Constructor sem Data");

                        var values = new List<TypedValue>();
                        foreach (TypedValue tv in rb) values.Add(tv);

                        // Acha bloco "Activities|SOLIDOS.ListActivity"
                        int actMarker = -1;
                        for (int i = 0; i < values.Count - 1; i++)
                        {
                            if (values[i].TypeCode == 1000 &&
                                (values[i].Value?.ToString() ?? "") == "Activities|SOLIDOS.ListActivity")
                            { actMarker = i; break; }
                        }
                        if (actMarker < 0) throw new System.Exception("Activities marker nao encontrado");

                        // Proximo TV deve ser code 102 "{"
                        int listStart = actMarker + 1;
                        if (values[listStart].TypeCode != 102 ||
                            (values[listStart].Value?.ToString() ?? "") != "{")
                            throw new System.Exception($"Esperado '{{' apos Activities marker; achou: {values[listStart].TypeCode}/{values[listStart].Value}");

                        // Acha closing '}' do nivel da lista (depth 0 -> 1 com listStart, fecha quando volta a 0)
                        int depth = 1;
                        int listEnd = -1;
                        for (int i = listStart + 1; i < values.Count; i++)
                        {
                            if (values[i].TypeCode == 102)
                            {
                                string s = values[i].Value?.ToString() ?? "";
                                if (s.StartsWith("{")) depth++;
                                else if (s == "}")
                                {
                                    depth--;
                                    if (depth == 0) { listEnd = i; break; }
                                }
                            }
                        }
                        if (listEnd < 0) throw new System.Exception("Fim da lista Activities nao encontrado");

                        // Acha id maximo existente pra evitar colisao
                        int maxId = 0;
                        for (int i = 0; i < values.Count - 1; i++)
                        {
                            if (values[i].TypeCode == 1000 &&
                                (values[i].Value?.ToString() ?? "") == "id" &&
                                values[i + 1].TypeCode == 90)
                            {
                                int idv = Convert.ToInt32(values[i + 1].Value);
                                if (idv > maxId) maxId = idv;
                            }
                        }

                        // Confirma parentid Variaveis = 4 (ou descobre)
                        parentVariaveisId = FindSequenceId(values, "Variaveis");
                        if (parentVariaveisId < 0) throw new System.Exception("Sequence 'Variaveis' nao encontrada");

                        // Acha proximo sequenceindex livre dentro de Variaveis
                        int maxSeq = -1;
                        for (int i = 0; i < values.Count - 1; i++)
                        {
                            if (values[i].TypeCode == 1000 &&
                                (values[i].Value?.ToString() ?? "") == "parentid" &&
                                values[i + 1].TypeCode == 90 &&
                                Convert.ToInt32(values[i + 1].Value) == parentVariaveisId)
                            {
                                // Procura sequenceindex no mesmo bloco
                                for (int j = i; j < Math.Min(i + 40, values.Count - 1); j++)
                                {
                                    if (values[j].TypeCode == 1000 &&
                                        (values[j].Value?.ToString() ?? "") == "sequenceindex" &&
                                        values[j + 1].TypeCode == 90)
                                    {
                                        int sv = Convert.ToInt32(values[j + 1].Value);
                                        if (sv > maxSeq) maxSeq = sv;
                                        break;
                                    }
                                }
                            }
                        }

                        int idCursor = maxId + 1;
                        int seqCursor = maxSeq + 1;
                        int locY = 510;

                        var inject = new List<TypedValue>();
                        for (int i = 0; i < NewVars.Length; i++)
                        {
                            var v = NewVars[i];
                            inject.Add(new TypedValue(102, "{SOLIDOS.ActivityDefineVariable"));
                            inject.Add(new TypedValue(1000, "VarType|System.String"));
                            inject.Add(new TypedValue(1, "Double"));
                            inject.Add(new TypedValue(1000, "Value|System.String"));
                            inject.Add(new TypedValue(1, v.Default));
                            inject.Add(new TypedValue(1000, "Visible"));
                            inject.Add(new TypedValue(290, (short)1));
                            inject.Add(new TypedValue(1000, "DisplayName|System.String"));
                            inject.Add(new TypedValue(1, v.Name));
                            inject.Add(new TypedValue(1000, "parentid"));
                            inject.Add(new TypedValue(90, parentVariaveisId));
                            inject.Add(new TypedValue(1000, "id"));
                            inject.Add(new TypedValue(90, idCursor++));
                            inject.Add(new TypedValue(1000, "sequenceindex"));
                            inject.Add(new TypedValue(90, seqCursor++));
                            inject.Add(new TypedValue(1000, "location|System.String"));
                            inject.Add(new TypedValue(1, $"20,{locY + i * 30}"));
                            inject.Add(new TypedValue(102, "}"));
                        }
                        injected = NewVars.Length;

                        values.InsertRange(listEnd, inject);

                        constructor.Data = new ResultBuffer(values.ToArray());
                        t.Commit();
                    }

                    db.SaveAs(sourcePath, db.OriginalFileVersion);

                    ed.WriteMessage($"\nOK! {injected} variaveis adicionadas em {constructorKey}");
                    ed.WriteMessage($"\nVariaveis sequence id = {parentVariaveisId}");
                    ed.WriteMessage($"\nArquivo salvo: {sourcePath}");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nERRO: {ex.Message}");
                ed.WriteMessage($"\nRestaure do backup se necessario: {backupPath}");
            }
        }

        private int FindSequenceId(List<TypedValue> values, string displayName)
        {
            // Procura padrao: {SOLIDOS.ActivitySequence ... DisplayName=<displayName>
            // retorna o id do bloco.
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].TypeCode == 102 &&
                    (values[i].Value?.ToString() ?? "").StartsWith("{SOLIDOS.ActivitySequence"))
                {
                    int blockEnd = FindBlockEnd(values, i);
                    int? id = null;
                    string name = null;
                    for (int j = i; j < blockEnd; j++)
                    {
                        if (values[j].TypeCode == 1000 && j + 1 < blockEnd)
                        {
                            string key = values[j].Value?.ToString() ?? "";
                            if (key == "id" && values[j + 1].TypeCode == 90)
                                id = Convert.ToInt32(values[j + 1].Value);
                            else if (key == "DisplayName|System.String" && values[j + 1].TypeCode == 1)
                                name = values[j + 1].Value?.ToString();
                        }
                    }
                    if (name == displayName && id.HasValue) return id.Value;
                }
            }
            return -1;
        }

        private int FindBlockEnd(List<TypedValue> values, int start)
        {
            for (int i = start + 1; i < values.Count; i++)
            {
                if (values[i].TypeCode == 102 && (values[i].Value?.ToString() ?? "") == "}")
                    return i;
            }
            return values.Count - 1;
        }
    }
}
