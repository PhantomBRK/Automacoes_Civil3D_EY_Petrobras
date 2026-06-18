using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Autodesk.Aec.PropertyData;
using Autodesk.Aec.PropertyData.DatabaseServices;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutomacoesCivil3D
{
    
    public class SlCorredorParametrizado
    {
        // Nome exato do PSet de destino (conforme definição no DWG)
        private const string PSET_TARGET   = "Pset_C - Propriedades Fisicas dos Objetos e Elementos";
        private const string PSET_IDENTITY = "Corridor Identity";
        private const string PSET_SHAPE    = "Corridor Shape Information";
        private const string PSET_COORD    = "COORDENAÇÃO";

        // -----------------------------------------------------------------------
        // Comando principal
        // -----------------------------------------------------------------------
        [CommandMethod("SL_CORREDOR_PARAMETRIZADO")]
        public void ExecuteLocked(Autodesk.AutoCAD.ApplicationServices.Document doc)
        {
            Editor ed = Manager.DocEditor;
            Database db = Manager.DocData;
            CivilDocument civil   = Manager.DocCivil;

            // Verificar se o PSet de destino existe no desenho
            ObjectId targetDefId = FindPSetDefinition(db, PSET_TARGET);
            if (targetDefId.IsNull)
            {
                ed.WriteMessage($"\n[ERRO] PSet '{PSET_TARGET}' não encontrado no desenho.");
                ed.WriteMessage("\nCarregue a definição do PropertySet antes de executar o comando.");
                AcadApp.ShowAlertDialog(
                    $"PSet '{PSET_TARGET}' não encontrado.\n\nCarregue a definição do PropertySet e execute novamente.");
                return;
            }

            int totalCorridors = 0, totalSolids = 0, updatedSolids = 0;
            var log = new List<string>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId corrId in civil.CorridorCollection)
                {
                    var corridor = tr.GetObject(corrId, OpenMode.ForRead) as Corridor;
                    if (corridor == null) continue;
                    totalCorridors++;

                    ed.WriteMessage($"\n  Corredor: {corridor.Name} — exportando sólidos...");

                    // 1. Exportar sólidos 3D do corredor
                    ObjectIdCollection solidIds;
                    try
                    {
                        string[] includedCodes = (corridor.GetShapeCodes() ?? Array.Empty<string>())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();

                        if (includedCodes.Length == 0)
                        {
                            log.Add($"[{corridor.Name}] Nenhum código de shape encontrado para exportar.");
                            continue;
                        }

                        var exportParams = new ExportCorridorSolidsParams
                        {
                            IncludedCodes = includedCodes,
                            ExportLinks = false,
                            ExportShapes = true
                        };

                        solidIds = corridor.ExportSolids(exportParams, db);
                    }
                    catch (System.Exception ex)
                    {
                        log.Add($"[{corridor.Name}] Falha ao exportar sólidos: {ex.Message}");
                        continue;
                    }

                    if (solidIds == null || solidIds.Count == 0)
                    {
                        log.Add($"[{corridor.Name}] Nenhum sólido retornado — verifique se o corredor está reconstruído.");
                        continue;
                    }

                    ed.WriteMessage($" {solidIds.Count} sólido(s).");

                    // 2. Cache de parâmetros de subassemblies do corredor (fallback por nome)
                    var subCache = BuildSubCache(corridor, tr);

                    // 3. Processar cada sólido
                    foreach (ObjectId solidId in solidIds)
                    {
                        totalSolids++;
                        try
                        {
                            ProcessSolid(solidId, tr, db, targetDefId, subCache, log);
                            updatedSolids++;
                        }
                        catch (System.Exception ex)
                        {
                            log.Add($"Sólido {solidId.Handle}: {ex.Message}");
                        }
                    }
                }

                tr.Commit();
            }

            // Relatório final
            var sb = new StringBuilder();
            sb.AppendLine("=== SL_CORREDOR_PARAMETRIZADO ===");
            sb.AppendLine($"Corredores processados : {totalCorridors}");
            sb.AppendLine($"Sólidos encontrados    : {totalSolids}");
            sb.AppendLine($"Sólidos com PSet_C     : {updatedSolids}");
            if (log.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Avisos/Erros ({log.Count}):");
                foreach (string line in log.Take(20))
                    sb.AppendLine("  " + line);
                if (log.Count > 20)
                    sb.AppendLine($"  ... e mais {log.Count - 20} aviso(s).");
            }

            string msg = sb.ToString();
            ed.WriteMessage("\n" + msg);
            AcadApp.ShowAlertDialog(msg);
        }

        // -----------------------------------------------------------------------
        // Lógica por sólido
        // -----------------------------------------------------------------------
        public static void ProcessSolid(
            ObjectId solidId,
            Transaction tr,
            Autodesk.AutoCAD.DatabaseServices.Database db,
            ObjectId targetDefId,
            Dictionary<string, SubData> subCache,
            List<string> log)
        {
            // Lê os PSets que o Civil 3D já colocou no sólido
            var identity = ReadPSetDict(solidId, tr, PSET_IDENTITY);
            var shape    = ReadPSetDict(solidId, tr, PSET_SHAPE);
            var coord    = ReadPSetDict(solidId, tr, PSET_COORD);

            // ── Comprimento ──────────────────────────────────────────────────
            // Prioridade 1: PSet COORDENAÇÃO, propriedade que começa com "COMPRIMENTO"
            double comprimento = GetDoubleByPrefix(coord, "COMPRIMENTO");

            // Prioridade 2: EndStation − StartStation do Corridor Identity
            if (double.IsNaN(comprimento))
            {
                double stStart = ParseStation(GetProp(identity, "StartStation"));
                double stEnd   = ParseStation(GetProp(identity, "EndStation"));
                if (!double.IsNaN(stStart) && !double.IsNaN(stEnd) && stEnd > stStart)
                    comprimento = stEnd - stStart;
            }

            // ── Volume ────────────────────────────────────────────────────────
            double volume = ParseDouble(GetProp(shape, "Volume"));

            // ── Identificação da subassembly ─────────────────────────────────
            string subHandleStr = GetPropStr(identity, "SubassemblyHandle", "SubassemblyHan");
            string subName      = GetPropStr(identity, "SubassemblyName");
            string codeName     = GetPropStr(shape,    "CodeName");
            string side         = GetPropStr(shape,    "Side");

            // ── Parâmetros da subassembly ─────────────────────────────────────
            // Prioridade 1: pelo handle hexadecimal (mais preciso)
            SubData sub = GetSubByHandle(subHandleStr, db, tr);

            // Prioridade 2: pelo nome da subassembly no cache do corredor
            if (sub == null && !string.IsNullOrEmpty(subName))
                subCache.TryGetValue(subName, out sub);

            double largura    = sub?.Width    ?? double.NaN;
            double inclinacao = sub?.Slope    ?? double.NaN;   // valor decimal, ex: -0.03
            double altura     = GetAlturaByCode(sub, codeName);

            // ── Área = comprimento × largura ──────────────────────────────────
            double area = (!double.IsNaN(comprimento) && !double.IsNaN(largura))
                ? comprimento * largura
                : double.NaN;

            // ── Log de valores ausentes (diagnóstico) ─────────────────────────
            var missing = new List<string>();
            if (double.IsNaN(comprimento)) missing.Add("Comprimento");
            if (double.IsNaN(largura))     missing.Add("Largura");
            if (double.IsNaN(inclinacao))  missing.Add("Inclinação");
            if (double.IsNaN(altura))      missing.Add("Altura");
            if (double.IsNaN(volume))      missing.Add("Volume");

            if (missing.Count > 0)
                log.Add($"Handle {solidId.Handle} ({subName}/{codeName}/{side}): sem valor para [{string.Join(", ", missing)}]");

            // ── Gravar no Pset_C ──────────────────────────────────────────────
            // Inclinação: guardada como string percentual ("−3.00%")
            string inclinacaoStr = double.IsNaN(inclinacao)
                ? string.Empty
                : (inclinacao * 100.0).ToString("F2", CultureInfo.InvariantCulture) + "%";

            WritePsetValues(solidId, tr, targetDefId, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Comprimento"] = FormatMeter(comprimento),
                ["Volume"]      = FormatMeter(volume),
                ["Largura"]     = FormatMeter(largura),
                ["Inclinação"]  = inclinacaoStr,
                // Campo sem acento como alias de segurança
                ["Inclinacao"]  = inclinacaoStr,
                ["Altura"]      = FormatMeter(altura),
                // Campo com acento (nome real no PSet)
                ["Área"]        = FormatMeter(area),
                // Alias sem acento
                ["Area"]        = FormatMeter(area),
            });
        }

        // -----------------------------------------------------------------------
        // Leitura de PSets
        // -----------------------------------------------------------------------

        /// <summary>Lê todas as propriedades de um PSet num dicionário nome→valor.</summary>
        public static Dictionary<string, object> ReadPSetDict(ObjectId objId, Transaction tr, string psetName)
        {
            try
            {
                var obj = tr.GetObject(objId, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.DBObject;
                if (obj == null) return null;

                ObjectIdCollection ids = PropertyDataServices.GetPropertySets(obj);
                foreach (ObjectId pid in ids)
                {
                    var ps = tr.GetObject(pid, OpenMode.ForRead) as PropertySet;
                    if (ps == null) continue;

                    if (!ps.PropertySetDefinitionName.Equals(psetName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var def = tr.GetObject(ps.PropertySetDefinition, OpenMode.ForRead) as PropertySetDefinition;
                    if (def == null) continue;

                    var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (PropertyDefinition propDef in def.Definitions)
                    {
                        try
                        {
                            int id2 = ps.PropertyNameToId(propDef.Name);
                            if (id2 >= 0)
                                result[propDef.Name] = ps.GetAt(id2);
                        }
                        catch { }
                    }
                    return result;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Obtém um valor pelo nome exato ou pelo prefixo (case-insensitive).</summary>
        public static object GetProp(Dictionary<string, object> dict, params string[] candidates)
        {
            if (dict == null) return null;
            foreach (string name in candidates)
            {
                if (dict.TryGetValue(name, out object val)) return val;
                // busca por prefixo — cobre nomes truncados como "SubassemblyHan..."
                foreach (var kv in dict)
                    if (kv.Key.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                        return kv.Value;
            }
            return null;
        }

        public static string GetPropStr(Dictionary<string, object> dict, params string[] candidates)
            => GetProp(dict, candidates)?.ToString()?.Trim() ?? string.Empty;

        /// <summary>Retorna o primeiro valor double encontrado cuja chave começa com o prefixo.</summary>
        public static double GetDoubleByPrefix(Dictionary<string, object> dict, string prefix)
        {
            if (dict == null) return double.NaN;
            foreach (var kv in dict)
            {
                if (!kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                double v = ParseDouble(kv.Value);
                if (!double.IsNaN(v) && v > 0) return v;
            }
            return double.NaN;
        }

        // -----------------------------------------------------------------------
        // Escrita no PSet de destino
        // -----------------------------------------------------------------------

        public static ObjectId FindPSetDefinition(Autodesk.AutoCAD.DatabaseServices.Database db, string name)
        {
            try
            {
                var defs = new DictionaryPropertySetDefinitions(db);
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    if (defs.Has(name, tr))
                    {
                        ObjectId id = defs.GetAt(name);
                        tr.Commit();
                        return id;
                    }

                    tr.Commit();
                }
            }
            catch { }
            return ObjectId.Null;
        }

        public static void WritePsetValues(
            ObjectId solidId,
            Transaction tr,
            ObjectId defId,
            Dictionary<string, string> values)
        {
            // Garante que o PSet está associado ao sólido
            var dbObj = tr.GetObject(solidId, OpenMode.ForWrite) as Autodesk.AutoCAD.DatabaseServices.DBObject;
            if (dbObj == null) return;

            ObjectId psetId = ObjectId.Null;
            try
            {
                psetId = PropertyDataServices.GetPropertySet(dbObj, defId);
            }
            catch { }

            if (psetId.IsNull || !psetId.IsValid)
            {
                try
                {
                    PropertyDataServices.AddPropertySet(dbObj, defId);
                    psetId = PropertyDataServices.GetPropertySet(dbObj, defId);
                }
                catch { return; }
            }

            if (psetId.IsNull || !psetId.IsValid) return;

            ObjectIdCollection psetIds = PropertyDataServices.GetPropertySets(dbObj);
            foreach (ObjectId pid in psetIds)
            {
                var ps = tr.GetObject(pid, OpenMode.ForRead) as PropertySet;
                if (ps == null || ps.PropertySetDefinition != defId) continue;

                ps = tr.GetObject(pid, OpenMode.ForWrite) as PropertySet;
                var def = tr.GetObject(defId, OpenMode.ForRead) as PropertySetDefinition;

                foreach (PropertyDefinition propDef in def.Definitions)
                {
                    // Verifica nome exato E nome sem acento (ambos os aliases)
                    string val = null;
                    if (!values.TryGetValue(propDef.Name, out val))
                    {
                        // Tenta normalizado (sem acento) como fallback
                        string normalized = RemoveAccents(propDef.Name);
                        values.TryGetValue(normalized, out val);
                    }

                    if (val == null) continue;
                    try
                    {
                        int pid2 = ps.PropertyNameToId(propDef.Name);
                        if (pid2 >= 0) ps.SetAt(pid2, val);
                    }
                    catch { }
                }
                break;
            }
        }

        // -----------------------------------------------------------------------
        // Subassembly — leitura de parâmetros
        // -----------------------------------------------------------------------

        public class SubData
        {
            public double Width    = double.NaN;  // Largura da pista
            public double Slope    = double.NaN;  // Declividade transversal (decimal, ex: -0.03)
            public double Pave1    = double.NaN;
            public double Pave2    = double.NaN;
            public double Base     = double.NaN;
            public double Subbase  = double.NaN;
            public double Subleito = double.NaN;
        }

        /// <summary>Monta cache nome→SubData iterando pelas subassemblies de todas as regiões do corredor.</summary>
        public static Dictionary<string, SubData> BuildSubCache(Corridor corridor, Transaction tr)
        {
            var cache = new Dictionary<string, SubData>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (Baseline bl in corridor.Baselines)
                {
                    foreach (BaselineRegion region in bl.BaselineRegions)
                    {
                        if (region.AssemblyId.IsNull) continue;
                        var assembly = tr.GetObject(region.AssemblyId, OpenMode.ForRead) as Assembly;
                        if (assembly == null) continue;

                        foreach (AssemblyGroup group in assembly.Groups)
                        {
                            foreach (ObjectId subId in group.GetSubassemblyIds())
                            {
                                try
                                {
                                    var sub = tr.GetObject(subId, OpenMode.ForRead) as Subassembly;
                                    if (sub == null || cache.ContainsKey(sub.Name)) continue;
                                    cache[sub.Name] = ExtractSubData(sub);
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch { }
            return cache;
        }

        /// <summary>Obtém SubData usando o handle hexadecimal gravado no PSet Corridor Identity.</summary>
        public static SubData GetSubByHandle(string handleStr, Autodesk.AutoCAD.DatabaseServices.Database db, Transaction tr)
        {
            if (string.IsNullOrWhiteSpace(handleStr)) return null;
            try
            {
                long handleVal = Convert.ToInt64(handleStr.Trim(), 16);
                ObjectId subId = db.GetObjectId(false, new Handle(handleVal), 0);
                if (subId.IsNull) return null;
                var sub = tr.GetObject(subId, OpenMode.ForRead) as Subassembly;
                return sub == null ? null : ExtractSubData(sub);
            }
            catch { return null; }
        }

        public static SubData ExtractSubData(Subassembly sub)
        {
            var data = new SubData();
            if (sub?.ParamsDouble == null) return data;

            foreach (var p in sub.ParamsDouble)
            {
                string k = NormalizeParamName(p.Key);
                double v = p.Value;

                // Largura
                if (MatchesAny(k, string.Empty, "WIDTH", "LANEWIDTH", "SHOULDERWIDTH", "LARGURA"))
                    data.Width = First(data.Width, v);

                // Declividade (guardada como decimal, ex: -0.03 para -3%)
                else if (MatchesAny(k, string.Empty, "DEFAULTSLOPE", "LANESLOPE", "SHOULDERSLOPE", "LINKSLOPE", "SLOPE", "DEFLECTION", "INCLINACAO"))
                    data.Slope = First(data.Slope, v);

                // Profundidades de camada
                else if (MatchesAny(k, string.Empty, "PAVE1DEPTH", "PAVE1", "PAV1DEPTH", "ESPESSURA1CAMADAPAVIMENTO"))
                    data.Pave1 = First(data.Pave1, v);

                else if (MatchesAny(k, string.Empty, "PAVE2DEPTH", "PAVE2", "PAV2DEPTH", "ESPESSURA2CAMADAPAVIMENTO"))
                    data.Pave2 = First(data.Pave2, v);

                else if (MatchesAny(k, string.Empty, "BASEDEPTH", "ESPESSURABASE"))
                    data.Base = First(data.Base, v);

                else if (MatchesAny(k, string.Empty, "SUBBASEDEPTH", "ESPESSURASUBBASE"))
                    data.Subbase = First(data.Subbase, v);

                else if (MatchesAny(k, string.Empty, "SUBLEITODEPTH", "SUBGRADEDEPTH", "CFTDEPTH", "ESPESSURAREFORCOSUBLEITO"))
                    data.Subleito = First(data.Subleito, v);
            }
            return data;
        }

        /// <summary>Retorna a altura correspondente ao CodeName da camada.</summary>
        public static double GetAlturaByCode(SubData sub, string codeName)
        {
            if (sub == null || string.IsNullOrEmpty(codeName)) return double.NaN;

            string code = RemoveAccents(codeName).ToUpperInvariant()
                          .Replace(" ", "").Replace("_", "").Replace("-", "");

            if (code.StartsWith("PAVE1") || code == "PAV1" || code == "TOP")  return sub.Pave1;
            if (code.StartsWith("PAVE2") || code == "PAV2")                   return sub.Pave2;
            if (code == "BASE")                                                 return sub.Base;
            if (code.StartsWith("SUBBASE"))                                    return sub.Subbase;
            if (code.StartsWith("SUBLEITO") || code.StartsWith("SUBGRADE"))   return sub.Subleito;

            return double.NaN;
        }

        // -----------------------------------------------------------------------
        // Parsing
        // -----------------------------------------------------------------------

        /// <summary>
        /// Converte valores de estação: "2+378.00m" → 2378.00  |  "2662.07" → 2662.07
        /// Formato brasileiro N+XXX.XX = N*1000 + XXX.XX metros.
        /// </summary>
        public static double ParseStation(object raw)
        {
            if (raw == null) return double.NaN;
            if (raw is double d) return d;

            string s = raw.ToString().Trim();
            // Remove sufixo de unidade (m, km, etc.)
            s = Regex.Replace(s, @"\s*[a-zA-Z]+\s*$", "").Trim();
            if (string.IsNullOrEmpty(s)) return double.NaN;

            int plus = s.IndexOf('+');
            if (plus > 0)
            {
                string left  = s.Substring(0, plus).Trim().Replace(",", ".");
                string right = s.Substring(plus + 1).Trim().Replace(",", ".");
                if (double.TryParse(left,  NumberStyles.Any, CultureInfo.InvariantCulture, out double km) &&
                    double.TryParse(right, NumberStyles.Any, CultureInfo.InvariantCulture, out double m))
                    return km * 1000.0 + m;
            }

            s = s.Replace(",", ".");
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double val) ? val : double.NaN;
        }

        /// <summary>Converte qualquer objeto numérico ou string para double.</summary>
        public static double ParseDouble(object raw)
        {
            if (raw == null) return double.NaN;
            if (raw is double d) return d;
            if (raw is float  f) return f;
            if (raw is int    i) return i;
            string s = raw.ToString().Trim().Replace(",", ".");
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
        }

        /// <summary>
        /// Converte parâmetro de subassembly: "-3.00%" → -0.03  |  "3.6" → 3.6
        /// Percentuais são divididos por 100 para retornar valor decimal.
        /// </summary> 
        public static double ParseSubParam(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return double.NaN;
            string s = raw.Trim();
            bool isPct = s.EndsWith("%");
            if (isPct) s = s.TrimEnd('%').Trim();
            s = Regex.Replace(s, @"[a-zA-Z]+$", "").Trim().Replace(",", ".");
            if (!double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) return double.NaN;
            return isPct ? v / 100.0 : v;
        }

        // -----------------------------------------------------------------------
        // Utilitários
        // -----------------------------------------------------------------------

        public static bool MatchesAny(string key, string display, params string[] targets)
        {
            foreach (string t in targets)
                if (key == t || display == t) return true;
            return false;
        }

        public static string NormalizeParamName(string text)
            => RemoveAccents(text ?? string.Empty).ToUpperInvariant().Replace(" ", "").Replace("_", "");

        public static double First(double existing, double candidate)
            => double.IsNaN(existing) ? candidate : existing;

        public static string FormatMeter(double v)
            => double.IsNaN(v) ? string.Empty : v.ToString("F4", CultureInfo.InvariantCulture);

        public static string RemoveAccents(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
