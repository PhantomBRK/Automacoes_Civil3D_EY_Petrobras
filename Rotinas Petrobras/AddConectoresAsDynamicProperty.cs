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
    public class AddConectoresAsDynamicProperty
    {
        // 20 DynamicProperty: Double/Distance, defaults equidistantes pra Largura=Comprimento=1.3m
        // Largura/3 = 0.4333, Largura/6 = 0.2167
        private const double L_3 = 0.4333333333;
        private const double L_6 = 0.2166666667;

        private static readonly (string Name, double Default)[] NewProps = new (string, double)[]
        {
            ("YCxL0", -L_3), ("YCxL1", -L_6), ("YCxL2", 0), ("YCxL3", L_6), ("YCxL4", L_3),
            ("YCxO0", -L_3), ("YCxO1", -L_6), ("YCxO2", 0), ("YCxO3", L_6), ("YCxO4", L_3),
            ("XCxS0", -L_3), ("XCxS1", -L_6), ("XCxS2", 0), ("XCxS3", L_6), ("XCxS4", L_3),
            ("XCxN0", -L_3), ("XCxN1", -L_6), ("XCxN2", 0), ("XCxN3", L_6), ("XCxN4", L_3),
        };

        [CommandMethod("AddConectoresAsDynamicProperty", CommandFlags.Session)]
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

                    string constructorKey;
                    int injected = 0, skipped = 0;

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

                        // Acha bloco "Properties|SOLIDOS.ListDynamicProperty"
                        int propMarker = -1;
                        for (int i = 0; i < values.Count - 1; i++)
                        {
                            if (values[i].TypeCode == 1000 &&
                                (values[i].Value?.ToString() ?? "") == "Properties|SOLIDOS.ListDynamicProperty")
                            { propMarker = i; break; }
                        }
                        if (propMarker < 0) throw new System.Exception("Properties marker nao encontrado");

                        int listStart = propMarker + 1;
                        if (values[listStart].TypeCode != 102 ||
                            (values[listStart].Value?.ToString() ?? "") != "{")
                            throw new System.Exception($"Esperado '{{' apos Properties marker");

                        // Acha fechamento da lista
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
                        if (listEnd < 0) throw new System.Exception("Fim de Properties nao encontrado");

                        // Coleta nomes ja existentes pra evitar duplicar
                        var existingNames = new HashSet<string>();
                        for (int i = listStart; i < listEnd - 1; i++)
                        {
                            if (values[i].TypeCode == 1000 &&
                                (values[i].Value?.ToString() ?? "") == "Name|System.String" &&
                                values[i + 1].TypeCode == 1)
                            {
                                existingNames.Add(values[i + 1].Value?.ToString() ?? "");
                            }
                        }

                        var inject = new List<TypedValue>();
                        foreach (var p in NewProps)
                        {
                            if (existingNames.Contains(p.Name))
                            {
                                skipped++;
                                continue;
                            }
                            string category = "Conectores";
                            string desc = $"Posicao do conector {p.Name}";

                            inject.Add(new TypedValue(102, "{SOLIDOS.DynamicProperty"));
                            inject.Add(new TypedValue(1000, "IsDefaultDescriptor"));
                            inject.Add(new TypedValue(290, (short)0));
                            inject.Add(new TypedValue(1000, "Name|System.String"));
                            inject.Add(new TypedValue(1, p.Name));
                            inject.Add(new TypedValue(1000, "VarType|System.RuntimeType"));
                            inject.Add(new TypedValue(1, "System.Double"));
                            inject.Add(new TypedValue(1000, "Direction"));
                            inject.Add(new TypedValue(90, 1));
                            inject.Add(new TypedValue(1000, "TypeConverter|System.RuntimeType"));
                            inject.Add(new TypedValue(1, "SOLIDOS.UnidadeDistancia"));
                            inject.Add(new TypedValue(1000, "ValueProvider"));
                            inject.Add(new TypedValue(90, 0));
                            inject.Add(new TypedValue(1000, "Description|System.String"));
                            inject.Add(new TypedValue(1, desc));
                            inject.Add(new TypedValue(1000, "DisplayName|System.String"));
                            inject.Add(new TypedValue(1, p.Name));
                            inject.Add(new TypedValue(1000, "Category|System.String"));
                            inject.Add(new TypedValue(1, category));
                            inject.Add(new TypedValue(1000, "DefValue"));
                            inject.Add(new TypedValue(40, p.Default));
                            inject.Add(new TypedValue(1000, "Macro|System.String"));
                            inject.Add(new TypedValue(1, "[(T1|U9|P3|D0|N0|M1|Z0)]"));
                            inject.Add(new TypedValue(1000, "Visible"));
                            inject.Add(new TypedValue(290, (short)1));
                            inject.Add(new TypedValue(1000, "KeepOnChange"));
                            inject.Add(new TypedValue(290, (short)0));
                            inject.Add(new TypedValue(102, "}"));
                            injected++;
                        }

                        if (injected == 0)
                        {
                            ed.WriteMessage($"\nNenhuma propriedade nova - todas {skipped} ja existem");
                            return;
                        }

                        values.InsertRange(listEnd, inject);

                        constructor.Data = new ResultBuffer(values.ToArray());
                        t.Commit();
                    }

                    db.SaveAs(sourcePath, db.OriginalFileVersion);

                    ed.WriteMessage($"\nOK! {injected} DynamicProperty adicionadas (skipped {skipped} existentes)");
                    ed.WriteMessage($"\nXRecord: {constructorKey}");
                    ed.WriteMessage($"\nArquivo salvo: {sourcePath}");
                    ed.WriteMessage($"\nDefaults assumindo Largura=Comprimento=1.3m");
                    ed.WriteMessage($"\nSe a caixa tiver outro tamanho, ajuste cada YCx*/XCx* no painel");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nERRO: {ex.Message}");
                ed.WriteMessage($"\nRestaure do backup se necessario: {backupPath}");
            }
        }
    }
}
