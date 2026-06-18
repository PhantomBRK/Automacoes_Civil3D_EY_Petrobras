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
    /// <summary>
    /// AddVariaveisGlobaisQtoSmecCaixa: cria as VARIÁVEIS GLOBAIS (DynamicProperty de
    /// saída, categoria "QTO SMEC") que o fluxograma da caixa JÁ CALCULA via
    /// ActivitySetOutPutParam mas que NÃO estão declaradas como propriedade.
    ///
    /// Sintoma original ("CAIXA COLETORA CONTAMINADO"): o fluxograma seta LargVala1,
    /// LargVala2, VolEscav, AreaApiloamento, VolReaterro, VolBotaFora, MassaEspAdotada
    /// e MassaBotaFora, mas sem a DynamicProperty correspondente esses valores não
    /// aparecem no painel nem são exportados pelo QTO (um SetOutPutParam sem propriedade
    /// declarada é calculado mas não exposto).
    ///
    /// Esta rotina é IDEMPOTENTE: só adiciona a propriedade se ela ainda não existir.
    /// NÃO mexe em Activities (os cálculos já estão no fluxograma). Faz backup antes.
    ///
    /// Template replicado EXATAMENTE do que o próprio dispositivo usa nas saídas QTO SMEC
    /// existentes (ver VolCA / AreaFormas no dump): IsDefaultDescriptor=0, ValueProvider=3
    /// (valor provido pelo fluxograma), Direction=1, Category="QTO SMEC", macro com P2,
    /// sem ComponentType.
    /// </summary>
    public class AddVariaveisGlobaisQtoSmecCaixa
    {
        // Unidades (TypeConverter) e macros de display — P2 para casar com o padrão do device
        private const string CvDist = "SOLIDOS.UnidadeDistancia";
        private const string CvArea = "SOLIDOS.UnidadeArea";
        private const string CvVol  = "SOLIDOS.UnidadeVolume";
        private const string CvMass = "SOLIDOS.UnidadeMassa";
        private const string CvDens = "SOLIDOS.UnidadeDensidade";

        private const string MDist = "[(T1|U9|P2|D0|N0|M1|Z0)]";
        private const string MArea = "[(T1|U38|P2|D0|N0|M1|Z0)]";
        private const string MVol  = "[(T1|U15|P2|D0|N0|M1|Z0)]";
        private const string MMass = "[(T1|U126|P2|D0|N0|M1|Z0)]";
        private const string MDens = "[(T1|U71|P2|D0|N0|M1|Z0)]";

        // As 8 variáveis globais faltantes (nome no fluxograma -> rótulo / unidade).
        // A ordem segue a sequência de cálculo do fluxograma (parentid=50).
        private static readonly (string Name, string DisplayName, string Converter, string Macro)[] OutputProps = new[]
        {
            ("LargVala1",       "Largura Vala 1",            CvDist, MDist),
            ("LargVala2",       "Largura Vala 2",            CvDist, MDist),
            ("VolEscav",        "Volume de Escavação",       CvVol,  MVol),
            ("AreaApiloamento", "Área de Apiloamento",       CvArea, MArea),
            ("VolReaterro",     "Volume de Reaterro",        CvVol,  MVol),
            ("VolBotaFora",     "Volume Bota-Fora",          CvVol,  MVol),
            ("MassaEspAdotada", "Massa Específica Adotada",  CvDens, MDens),
            ("MassaBotaFora",   "Massa Bota-Fora",           CvMass, MMass),
        };

        [CommandMethod("AddVariaveisGlobaisQtoSmecCaixa", CommandFlags.Session)]
        public void Inject()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            var pfo = new PromptOpenFileOptions("\nSelecione o .sbd da caixa (CAIXA COLETORA CONTAMINADO)")
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

                    int added = 0, skipped = 0;
                    var addedNames = new List<string>();

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
                        if (solidos == null) throw new System.Exception("Dict NOD/SOLIDOS_ não encontrado");

                        Xrecord constructor = null;
                        foreach (DBDictionaryEntry e in solidos)
                        {
                            if (e.Key.Contains("Constructor"))
                            {
                                var obj = t.GetObject(e.Value, OpenMode.ForWrite);
                                if (obj is Xrecord xr) { constructor = xr; break; }
                            }
                        }
                        if (constructor == null) throw new System.Exception("Constructor XRecord não encontrado");

                        var values = new List<TypedValue>();
                        foreach (TypedValue tv in constructor.Data) values.Add(tv);

                        // Localiza a lista de Properties e o fim dela
                        int propMarker = FindMarker(values, "Properties|SOLIDOS.ListDynamicProperty");
                        if (propMarker < 0) throw new System.Exception("Marcador Properties não encontrado");
                        int propListEnd = FindListEnd(values, propMarker + 1);

                        // Nomes de propriedades já existentes (pra não duplicar)
                        var existing = CollectExistingNames(values, propMarker + 2, propListEnd, "Name|System.String");

                        // Aviso: confere se a variável realmente é calculada no fluxograma
                        var calcTargets = CollectSetOutPutParamTargets(values);

                        var newTVs = new List<TypedValue>();
                        foreach (var p in OutputProps)
                        {
                            if (existing.Contains(p.Name)) { skipped++; continue; }
                            if (!calcTargets.Contains(p.Name))
                                ed.WriteMessage($"\n[AVISO] '{p.Name}' não é setada por nenhum ActivitySetOutPutParam "
                                    + "no fluxograma — propriedade será criada mesmo assim (valor ficará no DefValue).");
                            newTVs.AddRange(BuildOutputProperty(p));
                            added++;
                            addedNames.Add(p.Name);
                        }

                        if (newTVs.Count > 0)
                        {
                            values.InsertRange(propListEnd, newTVs);
                            constructor.Data = new ResultBuffer(values.ToArray());
                        }
                        t.Commit();
                    }

                    if (added > 0)
                        db.SaveAs(sourcePath, db.OriginalFileVersion);

                    ed.WriteMessage($"\nOK!");
                    ed.WriteMessage($"\n  Variáveis globais adicionadas: {added} ({string.Join(", ", addedNames)})");
                    ed.WriteMessage($"\n  Já existentes (puladas): {skipped}");
                    if (added > 0)
                        ed.WriteMessage($"\nArquivo salvo: {sourcePath}");
                    else
                        ed.WriteMessage($"\nNada a fazer — todas as 8 variáveis já estavam declaradas. (.sbd não foi reescrito)");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nERRO: {ex.Message}");
                ed.WriteMessage($"\n{ex.StackTrace}");
                ed.WriteMessage($"\nRestaure do backup: {backupPath}");
            }
        }

        // ============= BUILDER =============

        // Replica o template de saída QTO SMEC do próprio device (ver VolCA / AreaFormas no dump).
        private List<TypedValue> BuildOutputProperty(
            (string Name, string DisplayName, string Converter, string Macro) p)
        {
            return new List<TypedValue>
            {
                new TypedValue(102, "{SOLIDOS.DynamicProperty"),
                new TypedValue(1000, "IsDefaultDescriptor"),          new TypedValue(290, (short)0),
                new TypedValue(1000, "Name|System.String"),           new TypedValue(1, p.Name),
                new TypedValue(1000, "ValueProvider"),                new TypedValue(90, 3),
                new TypedValue(1000, "VarType|System.RuntimeType"),   new TypedValue(1, "System.Double"),
                new TypedValue(1000, "TypeConverter|System.RuntimeType"), new TypedValue(1, p.Converter),
                new TypedValue(1000, "Direction"),                    new TypedValue(90, 1),
                new TypedValue(1000, "Macro|System.String"),          new TypedValue(1, p.Macro),
                new TypedValue(1000, "DisplayName|System.String"),    new TypedValue(1, p.DisplayName),
                new TypedValue(1000, "Category|System.String"),       new TypedValue(1, "QTO SMEC"),
                new TypedValue(1000, "DefValue"),                     new TypedValue(40, 0.0),
                new TypedValue(1000, "Visible"),                      new TypedValue(290, (short)1),
                new TypedValue(1000, "KeepOnChange"),                 new TypedValue(290, (short)0),
                new TypedValue(102, "}"),
            };
        }

        // ============= HELPERS (mesma lógica de AddQuantitativosCaixa) =============

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
            throw new System.Exception("Fim de lista não encontrado");
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

        // PropName de todos os ActivitySetOutPutParam (alvos calculados no fluxograma)
        private HashSet<string> CollectSetOutPutParamTargets(List<TypedValue> values)
        {
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
    }
}
