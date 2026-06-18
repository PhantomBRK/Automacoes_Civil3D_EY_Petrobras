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
    public class AddQuantitativosCaixa
    {
        // Macros de display por unidade (extraidos do dump)
        private const string MDist = "[(T1|U9|P3|D0|N0|M1|Z0)]";
        private const string MVol  = "[(T1|U15|P3|D0|N0|M1|Z0)]";
        private const string MArea = "[(T1|U38|P3|D0|N0|M1|Z0)]";
        private const string MDens = "[(T1|U71|P3|D0|N0|M1|Z0)]";
        private const string MMass = "[(T1|U126|P3|D0|N0|M1|Z0)]";

        // 4 inputs (IsDefaultDescriptor=0, editaveis no painel)
        private static readonly (string Name, string DisplayName, string Description, string Category, double DefValue, string Converter, string Macro)[] InputProps = new []
        {
            ("TopElevation",   "Cota Topo",            "Cota do topo da caixa (m)",                                      "Geometria",       0.0, "SOLIDOS.UnidadeDistancia", MDist),
            ("MargemVala",     "Margem da Vala",       "Folga lateral pra escavacao alem das dimensoes externas",        "Quantitativos",   0.4, "SOLIDOS.UnidadeDistancia", MDist),
            ("AfastConcMagro", "Afast. Conc. Magro",   "Afastamento pra concreto magro alem das dimensoes externas",     "Quantitativos",   0.1, "SOLIDOS.UnidadeDistancia", MDist),
            ("Mesp",           "Massa Especifica",     "Massa especifica adotada (ton/m3)",                              "Quantitativos",   1.8, "SOLIDOS.UnidadeDensidade", MDens),
        };

        // 9 outputs (IsDefaultDescriptor=1 + ComponentType, somente leitura no painel)
        private static readonly (string Name, string DisplayName, string Description, string Category, string Converter, string Macro)[] OutputProps = new []
        {
            ("L1",              "L-1 Larg. Vala",      "Largura da vala dimensao 1 (m)",         "Quantitativos", "SOLIDOS.UnidadeDistancia", MDist),
            ("L2",              "L-2 Larg. Vala",      "Largura da vala dimensao 2 (m)",         "Quantitativos", "SOLIDOS.UnidadeDistancia", MDist),
            ("HEscav",          "H Escavacao",         "Altura total de escavacao (m)",          "Quantitativos", "SOLIDOS.UnidadeDistancia", MDist),
            ("VE",              "Vol. Escav. Solo",    "Volume de escavacao do solo (m3)",       "Quantitativos", "SOLIDOS.UnidadeVolume",    MVol),
            ("AreaApiloamento", "Area Apiloamento",    "Area de apiloamento (m2)",               "Quantitativos", "SOLIDOS.UnidadeArea",      MArea),
            ("Vcm",             "Vol. Conc. Magro",    "Volume de concreto magro (m3)",          "Quantitativos", "SOLIDOS.UnidadeVolume",    MVol),
            ("VR",              "Vol. Reaterro",       "Volume de reaterro (m3)",                "Quantitativos", "SOLIDOS.UnidadeVolume",    MVol),
            ("Vbf",             "Vol. Bota-fora",      "Volume de bota-fora (m3)",               "Quantitativos", "SOLIDOS.UnidadeVolume",    MVol),
            ("Mbf",             "Massa Bota-fora",     "Massa de bota-fora (ton)",               "Quantitativos", "SOLIDOS.UnidadeMassa",     MMass),
        };

        // 9 ActivitySetOutPutParam dentro do Variaveis (parentid=4)
        private static readonly (string PropName, string Value)[] Calcs = new []
        {
            ("L1",              "ComprimentoExterno + MargemVala"),
            ("L2",              "LarguraExterna + MargemVala"),
            ("HEscav",          "Altura + AltPiso + EspessuraConcretoMagro"),
            ("VE",              "L1 * L2 * HEscav"),
            ("AreaApiloamento", "L1 * L2"),
            ("Vcm",             "(ComprimentoExterno + AfastConcMagro) * (LarguraExterna + AfastConcMagro) * EspessuraConcretoMagro"),
            ("VR",              "VE - (ComprimentoExterno * LarguraExterna * HEscav) - Vcm"),
            ("Vbf",             "VE - VR"),
            ("Mbf",             "Vbf * Mesp"),
        };

        [CommandMethod("AddQuantitativosCaixa", CommandFlags.Session)]
        public void Inject()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            var pfo = new PromptOpenFileOptions("\nSelecione o .sbd da caixa")
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

                    int propsAdded = 0, propsSkipped = 0;
                    int actsAdded = 0, actsSkipped = 0;
                    int variaveisId;

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
                        foreach (DBDictionaryEntry e in solidos)
                        {
                            if (e.Key.Contains("Constructor"))
                            {
                                var obj = t.GetObject(e.Value, OpenMode.ForWrite);
                                if (obj is Xrecord xr) { constructor = xr; break; }
                            }
                        }
                        if (constructor == null) throw new System.Exception("Constructor XRecord nao encontrado");

                        var values = new List<TypedValue>();
                        foreach (TypedValue tv in constructor.Data) values.Add(tv);

                        // ====== FASE 1: Inserir DynamicProperty em Properties ======
                        int propMarker = FindMarker(values, "Properties|SOLIDOS.ListDynamicProperty");
                        if (propMarker < 0) throw new System.Exception("Properties marker nao encontrado");
                        int propListEnd = FindListEnd(values, propMarker + 1);

                        var existingPropNames = CollectExistingNames(values, propMarker + 2, propListEnd, "Name|System.String");

                        var newPropTVs = new List<TypedValue>();
                        foreach (var p in InputProps)
                        {
                            if (existingPropNames.Contains(p.Name)) { propsSkipped++; continue; }
                            newPropTVs.AddRange(BuildInputProperty(p));
                            propsAdded++;
                        }
                        foreach (var p in OutputProps)
                        {
                            if (existingPropNames.Contains(p.Name)) { propsSkipped++; continue; }
                            newPropTVs.AddRange(BuildOutputProperty(p));
                            propsAdded++;
                        }
                        values.InsertRange(propListEnd, newPropTVs);

                        // ====== FASE 2: Inserir ActivitySetOutPutParam em Activities ======
                        int actMarker = FindMarker(values, "Activities|SOLIDOS.ListActivity");
                        if (actMarker < 0) throw new System.Exception("Activities marker nao encontrado");
                        int actListEnd = FindListEnd(values, actMarker + 1);

                        variaveisId = FindSequenceId(values, "Variaveis");
                        if (variaveisId < 0) throw new System.Exception("Sequence Variaveis nao encontrada");

                        // Maior id global pra evitar colisao
                        int maxId = FindMaxId(values);

                        // Proximo sequenceindex livre dentro de Variaveis
                        int maxSeqInVariaveis = FindMaxSequenceIndex(values, variaveisId);

                        // Coleta PropName ja existentes de ActivitySetOutPutParam pra nao duplicar calculo
                        var existingCalcPropNames = CollectExistingCalcTargets(values);

                        int idCursor = maxId + 1;
                        int seqCursor = maxSeqInVariaveis + 1;
                        int locY = 1500; // posicao y arbitraria no designer pra novos blocos

                        var newActTVs = new List<TypedValue>();
                        foreach (var c in Calcs)
                        {
                            if (existingCalcPropNames.Contains(c.PropName)) { actsSkipped++; continue; }
                            newActTVs.AddRange(BuildSetOutPutParam(c.PropName, c.Value, variaveisId, idCursor++, seqCursor++, $"20,{locY}"));
                            locY += 30;
                            actsAdded++;
                        }
                        values.InsertRange(actListEnd, newActTVs);

                        constructor.Data = new ResultBuffer(values.ToArray());
                        t.Commit();
                    }

                    db.SaveAs(sourcePath, db.OriginalFileVersion);

                    ed.WriteMessage($"\nOK!");
                    ed.WriteMessage($"\n  Properties:  {propsAdded} adicionadas, {propsSkipped} ja existentes");
                    ed.WriteMessage($"\n  Activities:  {actsAdded} adicionadas, {actsSkipped} ja existentes");
                    ed.WriteMessage($"\n  Variaveis seq id: {variaveisId}");
                    ed.WriteMessage($"\nArquivo salvo: {sourcePath}");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nERRO: {ex.Message}");
                ed.WriteMessage($"\n{ex.StackTrace}");
                ed.WriteMessage($"\nRestaure do backup: {backupPath}");
            }
        }

        // ============= BUILDERS =============

        private List<TypedValue> BuildInputProperty(
            (string Name, string DisplayName, string Description, string Category, double DefValue, string Converter, string Macro) p)
        {
            var tvs = new List<TypedValue>
            {
                new TypedValue(102, "{SOLIDOS.DynamicProperty"),
                new TypedValue(1000, "IsDefaultDescriptor"),
                new TypedValue(290, (short)0),
                new TypedValue(1000, "Name|System.String"),       new TypedValue(1, p.Name),
                new TypedValue(1000, "VarType|System.RuntimeType"), new TypedValue(1, "System.Double"),
                new TypedValue(1000, "Direction"),                  new TypedValue(90, 1),
                new TypedValue(1000, "TypeConverter|System.RuntimeType"), new TypedValue(1, p.Converter),
                new TypedValue(1000, "ValueProvider"),              new TypedValue(90, 0),
                new TypedValue(1000, "Description|System.String"), new TypedValue(1, p.Description),
                new TypedValue(1000, "DisplayName|System.String"), new TypedValue(1, p.DisplayName),
                new TypedValue(1000, "Category|System.String"),    new TypedValue(1, p.Category),
                new TypedValue(1000, "DefValue"),                  new TypedValue(40, p.DefValue),
                new TypedValue(1000, "Macro|System.String"),       new TypedValue(1, p.Macro),
                new TypedValue(1000, "Visible"),                   new TypedValue(290, (short)1),
                new TypedValue(1000, "KeepOnChange"),              new TypedValue(290, (short)0),
                new TypedValue(102, "}"),
            };
            return tvs;
        }

        private List<TypedValue> BuildOutputProperty(
            (string Name, string DisplayName, string Description, string Category, string Converter, string Macro) p)
        {
            var tvs = new List<TypedValue>
            {
                new TypedValue(102, "{SOLIDOS.DynamicProperty"),
                new TypedValue(1000, "IsDefaultDescriptor"),
                new TypedValue(290, (short)1),  // marca como output/read-only
                new TypedValue(1000, "Name|System.String"),       new TypedValue(1, p.Name),
                new TypedValue(1000, "VarType|System.RuntimeType"), new TypedValue(1, "System.Double"),
                new TypedValue(1000, "Direction"),                  new TypedValue(90, 1),
                new TypedValue(1000, "TypeConverter|System.RuntimeType"), new TypedValue(1, p.Converter),
                new TypedValue(1000, "ValueProvider"),              new TypedValue(90, 0),
                new TypedValue(1000, "ComponentType|System.RuntimeType"), new TypedValue(1, "SOLIDOS.SolPointDeviceBase"),
                new TypedValue(1000, "Description|System.String"), new TypedValue(1, p.Description),
                new TypedValue(1000, "DisplayName|System.String"), new TypedValue(1, p.DisplayName),
                new TypedValue(1000, "Category|System.String"),    new TypedValue(1, p.Category),
                new TypedValue(1000, "DefValue"),                  new TypedValue(40, 0.0),
                new TypedValue(1000, "Macro|System.String"),       new TypedValue(1, p.Macro),
                new TypedValue(1000, "Visible"),                   new TypedValue(290, (short)1),
                new TypedValue(1000, "KeepOnChange"),              new TypedValue(290, (short)0),
                new TypedValue(102, "}"),
            };
            return tvs;
        }

        private List<TypedValue> BuildSetOutPutParam(string propName, string value, int parentId, int id, int seqIdx, string location)
        {
            var tvs = new List<TypedValue>
            {
                new TypedValue(102, "{SOLIDOS.ActivitySetOutPutParam"),
                new TypedValue(1000, "PropName|System.String"),    new TypedValue(1, propName),
                new TypedValue(1000, "Value|System.String"),       new TypedValue(1, value),
                new TypedValue(1000, "DisplayName|System.String"), new TypedValue(1, $"{propName}={value}"),
                new TypedValue(1000, "parentid"),                  new TypedValue(90, parentId),
                new TypedValue(1000, "id"),                        new TypedValue(90, id),
                new TypedValue(1000, "sequenceindex"),             new TypedValue(90, seqIdx),
                new TypedValue(1000, "location|System.String"),    new TypedValue(1, location),
                new TypedValue(102, "}"),
            };
            return tvs;
        }

        // ============= HELPERS =============

        private int FindMarker(List<TypedValue> values, string marker)
        {
            for (int i = 0; i < values.Count - 1; i++)
                if (values[i].TypeCode == 1000 && (values[i].Value?.ToString() ?? "") == marker)
                    return i;
            return -1;
        }

        private int FindListEnd(List<TypedValue> values, int listStartIdx)
        {
            if (values[listStartIdx].TypeCode != 102 || (values[listStartIdx].Value?.ToString() ?? "") != "{")
                throw new System.Exception("Esperado '{' no listStartIdx");
            int depth = 1;
            for (int i = listStartIdx + 1; i < values.Count; i++)
            {
                if (values[i].TypeCode == 102)
                {
                    string s = values[i].Value?.ToString() ?? "";
                    if (s.StartsWith("{")) depth++;
                    else if (s == "}") { depth--; if (depth == 0) return i; }
                }
            }
            throw new System.Exception("Fim de lista nao encontrado");
        }

        private HashSet<string> CollectExistingNames(List<TypedValue> values, int from, int toExclusive, string fieldMarker)
        {
            var set = new HashSet<string>();
            for (int i = from; i < toExclusive - 1; i++)
            {
                if (values[i].TypeCode == 1000 &&
                    (values[i].Value?.ToString() ?? "") == fieldMarker &&
                    values[i + 1].TypeCode == 1)
                    set.Add(values[i + 1].Value?.ToString() ?? "");
            }
            return set;
        }

        private HashSet<string> CollectExistingCalcTargets(List<TypedValue> values)
        {
            // PropName de ActivitySetOutPutParam que ja existem
            var set = new HashSet<string>();
            for (int i = 0; i < values.Count - 3; i++)
            {
                if (values[i].TypeCode == 102 &&
                    (values[i].Value?.ToString() ?? "") == "{SOLIDOS.ActivitySetOutPutParam" &&
                    values[i + 1].TypeCode == 1000 &&
                    (values[i + 1].Value?.ToString() ?? "") == "PropName|System.String" &&
                    values[i + 2].TypeCode == 1)
                    set.Add(values[i + 2].Value?.ToString() ?? "");
            }
            return set;
        }

        private int FindSequenceId(List<TypedValue> values, string displayName)
        {
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
                if (values[i].TypeCode == 102 && (values[i].Value?.ToString() ?? "") == "}")
                    return i;
            return values.Count - 1;
        }

        private int FindMaxId(List<TypedValue> values)
        {
            int max = 0;
            for (int i = 0; i < values.Count - 1; i++)
            {
                if (values[i].TypeCode == 1000 &&
                    (values[i].Value?.ToString() ?? "") == "id" &&
                    values[i + 1].TypeCode == 90)
                {
                    int idv = Convert.ToInt32(values[i + 1].Value);
                    if (idv > max) max = idv;
                }
            }
            return max;
        }

        private int FindMaxSequenceIndex(List<TypedValue> values, int parentId)
        {
            int max = -1;
            for (int i = 0; i < values.Count - 1; i++)
            {
                if (values[i].TypeCode == 1000 &&
                    (values[i].Value?.ToString() ?? "") == "parentid" &&
                    values[i + 1].TypeCode == 90 &&
                    Convert.ToInt32(values[i + 1].Value) == parentId)
                {
                    for (int j = i; j < Math.Min(i + 40, values.Count - 1); j++)
                    {
                        if (values[j].TypeCode == 1000 &&
                            (values[j].Value?.ToString() ?? "") == "sequenceindex" &&
                            values[j + 1].TypeCode == 90)
                        {
                            int sv = Convert.ToInt32(values[j + 1].Value);
                            if (sv > max) max = sv;
                            break;
                        }
                    }
                }
            }
            return max;
        }
    }
}
