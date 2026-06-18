using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Autodesk.Aec.PropertyData;
using Autodesk.Aec.PropertyData.DatabaseServices;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Label = Autodesk.Civil.DatabaseServices.Label;
using Color = Autodesk.AutoCAD.Colors.Color;
using DataType = Autodesk.Aec.PropertyData.DataType;
using FlowDirection = System.Windows.Forms.FlowDirection;

namespace AutomacoesCivil3D
{
    public class SinalizacaoPsetMapper
    {
        [CommandMethod("SINAL_PSET_MAPEAR")]
        public void MapearESalvarAplicar()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database db = civilDoc.Database;

            PromptEntityOptions peo = new PromptEntityOptions("\nSelecione um bloco de sinalização: ");
            peo.SetRejectMessage("\nSelecione apenas BLOCK REFERENCE.");
            peo.AddAllowedClass(typeof(BlockReference), true);

            PromptEntityResult per = docEditor.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
                return;

            using (Transaction trans = db.TransactionManager.StartTransaction())
            {
                BlockReference br = (BlockReference)trans.GetObject(per.ObjectId, OpenMode.ForRead);
                string blockKey = BlockUtils.GetEffectiveBlockName(trans, br);

                List<BlockSourceOption> sources = BlockSourceProvider.BuildSourceOptions(trans, br);
                List<PsetFieldInfo> psetFields = PsetProvider.GetAttachedPsetFields(trans, br);

                if (psetFields.Count == 0)
                {
                    docEditor.WriteMessage("\nO bloco selecionado não possui Property Sets anexados. Anexe os PSETS primeiro (ou via estilo) e rode de novo.");
                    return;
                }

                string defaultPath = BlockPsetMappingStorage.GetDefaultFilePath(blockKey);
                BlockPsetMappingFile existing = null;

                if (File.Exists(defaultPath))
                {
                    existing = BlockPsetMappingStorage.TryLoad(defaultPath);
                }

                using (BlockPsetMapperForm form = new BlockPsetMapperForm(blockKey, sources, psetFields, existing))
                {
                    DialogResult dr = Application.ShowModalDialog(form);
                    if (dr != DialogResult.OK)
                        return;

                    BlockPsetMappingFile mapFile = form.GetMappingFile();
                    mapFile.LayerName = br.Layer;
                    mapFile.UseLayerFilter = true;

                    Directory.CreateDirectory(Path.GetDirectoryName(defaultPath));
                    BlockPsetMappingStorage.Save(mapFile, defaultPath);

                    trans.Commit();
                }
            }

            // Aplicar depois de salvar (transação separada)
            AplicarMappingNoDesenho();
        }

        [CommandMethod("SINAL_PSET_APLICAR")]
        public void AplicarMappingNoDesenho()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database db = civilDoc.Database;

            PromptEntityOptions peo = new PromptEntityOptions("\nSelecione um bloco (do tipo que você quer aplicar): ");
            peo.SetRejectMessage("\nSelecione apenas BLOCK REFERENCE.");
            peo.AddAllowedClass(typeof(BlockReference), true);

            PromptEntityResult per = docEditor.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
                return;

            string blockKey = null;
            using (Transaction trans = db.TransactionManager.StartTransaction())
            {
                BlockReference br = (BlockReference)trans.GetObject(per.ObjectId, OpenMode.ForRead);
                blockKey = BlockUtils.GetEffectiveBlockName(trans, br);
                trans.Commit();
            }

            string defaultPath = BlockPsetMappingStorage.GetDefaultFilePath(blockKey);
            if (!File.Exists(defaultPath))
            {
                docEditor.WriteMessage("\nMapping não encontrado: " + defaultPath);
                return;
            }

            BlockPsetMappingFile mapFileLoaded = BlockPsetMappingStorage.TryLoad(defaultPath);
            if (mapFileLoaded == null || mapFileLoaded.Maps == null || mapFileLoaded.Maps.Count == 0)
            {
                docEditor.WriteMessage("\nMapping inválido/vazio.");
                return;
            }

            int total = 0;
            int updated = 0;
            int errors = 0;

            using (Transaction trans = db.TransactionManager.StartTransaction())
            {
                Dictionary<string, ObjectId> psdIdCache = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
                Dictionary<ObjectId, Dictionary<string, DataType>> psdFieldTypeCache = new Dictionary<ObjectId, Dictionary<string, DataType>>();

                IEnumerable<ObjectId> allBrIds = BlockUtils.FindAllBlockReferencesInLayouts(trans, db);

                foreach (ObjectId id in allBrIds)
                {
                    Entity ent = (Entity)trans.GetObject(id, OpenMode.ForRead);
                    BlockReference br = ent as BlockReference;
                    if (br == null)
                        continue;

                    if (mapFileLoaded.UseLayerFilter)
                    {
                        if (string.IsNullOrWhiteSpace(mapFileLoaded.LayerName))
                            continue;

                        if (!string.Equals(br.Layer, mapFileLoaded.LayerName, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                    else
                    {
                        string effName = BlockUtils.GetEffectiveBlockName(trans, br);
                        if (!string.Equals(effName, mapFileLoaded.BlockName, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    total++;

                    try
                    {
                        Entity entW = (Entity)trans.GetObject(id, OpenMode.ForWrite);
                        BlockReference brW = entW as BlockReference;
                        if (brW == null)
                            continue;

                        if (mapFileLoaded.UseLayerFilter && string.IsNullOrWhiteSpace(mapFileLoaded.LayerName))
                        {
                            // fallback: se não tiver layer salvo, volta a filtrar por nome
                            mapFileLoaded.UseLayerFilter = false;
                        }

                        ApplyMappingToBlock(trans, db, brW, mapFileLoaded, psdIdCache, psdFieldTypeCache);
                        updated++;
                    }
                    catch (System.Exception ex)
                    {
                        errors++;
                        docEditor.WriteMessage("\nErro em um bloco: " + ex.Message);
                    }
                }

                trans.Commit();
            }

            docEditor.WriteMessage($"\nFinalizado. Encontrados={total}, Atualizados={updated}, Erros={errors}");
        }

        private static void ApplyMappingToBlock(
            Transaction trans,
            Database db,
            BlockReference brW,
            BlockPsetMappingFile mapFile,
            Dictionary<string, ObjectId> psdIdCache,
            Dictionary<ObjectId, Dictionary<string, DataType>> psdFieldTypeCache)
        {
            Dictionary<string, List<PsetFieldMap>> byPset =
                mapFile.Maps
                    .Where(m => m != null && !string.IsNullOrWhiteSpace(m.PsetName) && !string.IsNullOrWhiteSpace(m.FieldName))
                    .GroupBy(m => m.PsetName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, List<PsetFieldMap>> kv in byPset)
            {
                string psetName = kv.Key;

                ObjectId psdId = PsetProvider.GetPsetDefinitionIdByName(trans, db, psetName, psdIdCache);
                if (psdId == ObjectId.Null)
                    continue;

                // cache tipos por campo
                Dictionary<string, DataType> fieldTypes = PsetProvider.GetPsetFieldTypes(trans, psdId, psdFieldTypeCache);

                // garante PropertySet anexado
                ObjectId psetId = PsetProvider.EnsurePropertySetOnObject(trans, brW, psdId);
                if (psetId == ObjectId.Null)
                    continue;

                PropertySet pset = (PropertySet)trans.GetObject(psetId, OpenMode.ForWrite);
                if (!pset.IsWriteEnabled)
                    continue;

                List<PsetFieldMap> maps = kv.Value;

                foreach (PsetFieldMap map in maps)
                {
                    if (map == null)
                        continue;

                    if (string.IsNullOrWhiteSpace(map.SourceCode) || string.Equals(map.SourceCode, "NONE", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // não tenta escrever em automático (a maioria é calculada)
                    bool isAutomatic = PsetProvider.IsFieldAutomatic(trans, psdId, map.FieldName);
                    if (isAutomatic)
                        continue;

                    object rawValue = BlockSourceProvider.TryGetSourceValue(trans, brW, map.SourceCode, map.ConstantValue);

                    DataType dt = DataType.Text;
                    if (fieldTypes.ContainsKey(map.FieldName))
                        dt = fieldTypes[map.FieldName];

                    object convertedValue = ValueConverter.ConvertToAecDataType(rawValue, dt);

                    try
                    {
                        int pid = pset.PropertyNameToId(map.FieldName);
                        pset.SetAt(pid, convertedValue);
                    }
                    catch (Exception)
                    {
                        // eKeyNotFound / read-only / fórmula etc.
                    }
                }
            }
        }
    }

    internal static class BlockUtils
    {
        public static string GetEffectiveBlockName(Transaction trans, BlockReference br)
        {
            // Civil 3D/AutoCAD modernos têm EffectiveName
            try
            {
                string eff = br.Layer;
                if (!string.IsNullOrWhiteSpace(eff))
                    return eff;
            }
            catch
            {
            }

            BlockTableRecord btr = (BlockTableRecord)trans.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            return btr.Name;
        }

        public static IEnumerable<ObjectId> FindAllBlockReferencesInLayouts(Transaction trans, Database db)
        {
            List<ObjectId> result = new List<ObjectId>();

            BlockTable bt = (BlockTable)trans.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId btrId in bt)
            {
                BlockTableRecord btr = (BlockTableRecord)trans.GetObject(btrId, OpenMode.ForRead);
                if (!btr.IsLayout)
                    continue;

                foreach (ObjectId entId in btr)
                {
                    result.Add(entId);
                }
            }

            return result;
        }
    }

    internal static class BlockSourceProvider
    {
        public static List<BlockSourceOption> BuildSourceOptions(Transaction trans, BlockReference br)
        {
            Dictionary<string, BlockSourceOption> dict = new Dictionary<string, BlockSourceOption>(StringComparer.OrdinalIgnoreCase);

            // NONE / CONST
            dict["NONE"] = new BlockSourceOption("NONE", "[Nenhum]", "");
            dict["CONST"] = new BlockSourceOption("CONST", "[Constante]", "");

            // Built-ins
            Add(dict, new BlockSourceOption("BUI:Layer", "[Built-in] Layer", br.Layer));
            Add(dict, new BlockSourceOption("BUI:Rotation", "[Built-in] Rotation", br.Rotation.ToString(CultureInfo.InvariantCulture)));
            Add(dict, new BlockSourceOption("BUI:PosX", "[Built-in] Position.X", br.Position.X.ToString(CultureInfo.InvariantCulture)));
            Add(dict, new BlockSourceOption("BUI:PosY", "[Built-in] Position.Y", br.Position.Y.ToString(CultureInfo.InvariantCulture)));
            Add(dict, new BlockSourceOption("BUI:PosZ", "[Built-in] Position.Z", br.Position.Z.ToString(CultureInfo.InvariantCulture)));
            Add(dict, new BlockSourceOption("BUI:ScaleX", "[Built-in] ScaleX", br.ScaleFactors.X.ToString(CultureInfo.InvariantCulture)));
            Add(dict, new BlockSourceOption("BUI:ScaleY", "[Built-in] ScaleY", br.ScaleFactors.Y.ToString(CultureInfo.InvariantCulture)));
            Add(dict, new BlockSourceOption("BUI:ScaleZ", "[Built-in] ScaleZ", br.ScaleFactors.Z.ToString(CultureInfo.InvariantCulture)));

            // Attributes
            if (br.AttributeCollection != null)
            {
                foreach (ObjectId attId in br.AttributeCollection)
                {
                    if (attId == ObjectId.Null)
                        continue;

                    AttributeReference att = (AttributeReference)trans.GetObject(attId, OpenMode.ForRead);
                    if (att == null)
                        continue;

                    string tag = att.Tag ?? "";
                    string val = att.TextString ?? "";

                    if (string.IsNullOrWhiteSpace(tag))
                        continue;

                    Add(dict, new BlockSourceOption("ATTR:" + tag, "[Atributo] " + tag, val));
                }
            }

            // Dynamic properties
            try
            {
                DynamicBlockReferencePropertyCollection props = br.DynamicBlockReferencePropertyCollection;
                if (props != null)
                {
                    foreach (DynamicBlockReferenceProperty p in props)
                    {
                        if (p == null)
                            continue;

                        string name = p.PropertyName ?? "";
                        object v = p.Value;

                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        Add(dict, new BlockSourceOption("DYN:" + name, "[Dinâmico] " + name, (v == null) ? "" : v.ToString()));
                    }
                }
            }
            catch
            {
            }

            return dict.Values.OrderBy(o => o.Display, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void Add(Dictionary<string, BlockSourceOption> dict, BlockSourceOption opt)
        {
            if (!dict.ContainsKey(opt.Code))
                dict[opt.Code] = opt;
        }

        public static object TryGetSourceValue(Transaction trans, BlockReference br, string sourceCode, string constantValue)
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
                return null;

            if (string.Equals(sourceCode, "CONST", StringComparison.OrdinalIgnoreCase))
                return constantValue ?? "";

            if (string.Equals(sourceCode, "NONE", StringComparison.OrdinalIgnoreCase))
                return null;

            if (sourceCode.StartsWith("BUI:", StringComparison.OrdinalIgnoreCase))
            {
                string key = sourceCode.Substring(4);
                if (string.Equals(key, "Layer", StringComparison.OrdinalIgnoreCase)) return br.Layer;
                if (string.Equals(key, "Rotation", StringComparison.OrdinalIgnoreCase)) return br.Rotation;
                if (string.Equals(key, "PosX", StringComparison.OrdinalIgnoreCase)) return br.Position.X;
                if (string.Equals(key, "PosY", StringComparison.OrdinalIgnoreCase)) return br.Position.Y;
                if (string.Equals(key, "PosZ", StringComparison.OrdinalIgnoreCase)) return br.Position.Z;
                if (string.Equals(key, "ScaleX", StringComparison.OrdinalIgnoreCase)) return br.ScaleFactors.X;
                if (string.Equals(key, "ScaleY", StringComparison.OrdinalIgnoreCase)) return br.ScaleFactors.Y;
                if (string.Equals(key, "ScaleZ", StringComparison.OrdinalIgnoreCase)) return br.ScaleFactors.Z;
                return null;
            }

            if (sourceCode.StartsWith("ATTR:", StringComparison.OrdinalIgnoreCase))
            {
                string tag = sourceCode.Substring(5);

                if (br.AttributeCollection == null)
                    return null;

                foreach (ObjectId attId in br.AttributeCollection)
                {
                    AttributeReference att = (AttributeReference)trans.GetObject(attId, OpenMode.ForRead);
                    if (att == null)
                        continue;

                    if (string.Equals(att.Tag, tag, StringComparison.OrdinalIgnoreCase))
                        return att.TextString ?? "";
                }

                return null;
            }

            if (sourceCode.StartsWith("DYN:", StringComparison.OrdinalIgnoreCase))
            {
                string name = sourceCode.Substring(4);

                try
                {
                    DynamicBlockReferencePropertyCollection props = br.DynamicBlockReferencePropertyCollection;
                    if (props == null)
                        return null;

                    foreach (DynamicBlockReferenceProperty p in props)
                    {
                        if (p == null)
                            continue;

                        if (string.Equals(p.PropertyName, name, StringComparison.OrdinalIgnoreCase))
                            return p.Value;
                    }
                }
                catch
                {
                }

                return null;
            }

            return null;
        }
    }

    internal static class PsetProvider
    {
        public static List<PsetFieldInfo> GetAttachedPsetFields(Transaction trans, BlockReference br)
        {
            List<PsetFieldInfo> result = new List<PsetFieldInfo>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ObjectIdCollection setIds = PropertyDataServices.GetPropertySets((DBObject)br);
            foreach (ObjectId psId in setIds)
            {
                PropertySet ps = (PropertySet)trans.GetObject(psId, OpenMode.ForRead);
                if (ps == null)
                    continue;

                ObjectId psdId = ps.PropertySetDefinition;
                if (psdId == ObjectId.Null)
                    continue;

                PropertySetDefinition psd = (PropertySetDefinition)trans.GetObject(psdId, OpenMode.ForRead);
                if (psd == null)
                    continue;

                foreach (PropertyDefinition def in psd.Definitions)
                {
                    if (def == null)
                        continue;

                    string key = psd.Name + "|" + def.Name;
                    if (seen.Contains(key))
                        continue;

                    seen.Add(key);

                    PsetFieldInfo info = new PsetFieldInfo();
                    info.PsetName = psd.Name;
                    info.FieldName = def.Name;
                    info.DataType = def.DataType;
                    info.IsAutomatic = def.Automatic;

                    result.Add(info);
                }
            }

            return result.OrderBy(r => r.PsetName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(r => r.FieldName, StringComparer.OrdinalIgnoreCase)
                         .ToList();
        }

        public static ObjectId GetPsetDefinitionIdByName(Transaction trans, Database db, string psetName, Dictionary<string, ObjectId> cache)
        {
            if (cache.ContainsKey(psetName))
                return cache[psetName];

            ObjectId psdId = ObjectId.Null;

            DictionaryPropertySetDefinitions psdDict = new DictionaryPropertySetDefinitions(db);
            if (psdDict.Has(psetName, trans))
            {
                psdId = psdDict.GetAt(psetName);
            }

            cache[psetName] = psdId;
            return psdId;
        }

        public static Dictionary<string, DataType> GetPsetFieldTypes(Transaction trans, ObjectId psdId, Dictionary<ObjectId, Dictionary<string, DataType>> cache)
        {
            if (cache.ContainsKey(psdId))
                return cache[psdId];

            Dictionary<string, DataType> dict = new Dictionary<string, DataType>(StringComparer.OrdinalIgnoreCase);

            PropertySetDefinition psd = (PropertySetDefinition)trans.GetObject(psdId, OpenMode.ForRead);
            foreach (PropertyDefinition def in psd.Definitions)
            {
                if (def == null)
                    continue;

                if (!dict.ContainsKey(def.Name))
                    dict[def.Name] = def.DataType;
            }

            cache[psdId] = dict;
            return dict;
        }

        public static bool IsFieldAutomatic(Transaction trans, ObjectId psdId, string fieldName)
        {
            PropertySetDefinition psd = (PropertySetDefinition)trans.GetObject(psdId, OpenMode.ForRead);
            foreach (PropertyDefinition def in psd.Definitions)
            {
                if (def == null)
                    continue;

                if (string.Equals(def.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                    return def.Automatic;
            }

            return false;
        }

        public static ObjectId EnsurePropertySetOnObject(Transaction trans, DBObject dbobjW, ObjectId psdId)
        {
            ObjectIdCollection setIds = PropertyDataServices.GetPropertySets(dbobjW);
            ObjectId psetId = FindPropertySetIdByDefinition(trans, setIds, psdId);
            if (psetId != ObjectId.Null)
                return psetId;

            // anexa
            PropertyDataServices.AddPropertySet(dbobjW, psdId);

            // busca de novo
            ObjectIdCollection setIds2 = PropertyDataServices.GetPropertySets(dbobjW);
            return FindPropertySetIdByDefinition(trans, setIds2, psdId);
        }

        private static ObjectId FindPropertySetIdByDefinition(Transaction trans, ObjectIdCollection setIds, ObjectId psdId)
        {
            foreach (ObjectId id in setIds)
            {
                PropertySet ps = (PropertySet)trans.GetObject(id, OpenMode.ForRead);
                if (ps == null)
                    continue;

                if (ps.PropertySetDefinition == psdId)
                    return id;
            }

            return ObjectId.Null;
        }
    }

    internal static class ValueConverter
    {
        public static object ConvertToAecDataType(object rawValue, DataType dt)
        {
            if (rawValue == null)
                return GetDefault(dt);

            // Se já veio do tipo certo, deixa
            if (dt == DataType.Text)
                return rawValue.ToString();

            if (dt == DataType.TrueFalse)
            {
                bool b;
                if (rawValue is bool)
                    return rawValue;

                if (bool.TryParse(rawValue.ToString(), out b))
                    return b;

                string s = rawValue.ToString().Trim();
                if (string.Equals(s, "SIM", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(s, "NÃO", StringComparison.OrdinalIgnoreCase)) return false;
                if (string.Equals(s, "NAO", StringComparison.OrdinalIgnoreCase)) return false;
                if (string.Equals(s, "1", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(s, "0", StringComparison.OrdinalIgnoreCase)) return false;

                return false;
            }

            if (dt == DataType.Integer)
            {
                int i;
                if (rawValue is int)
                    return rawValue;

                if (int.TryParse(rawValue.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out i))
                    return i;

                if (int.TryParse(rawValue.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out i))
                    return i;

                double d;
                if (double.TryParse(rawValue.ToString().Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                    return (int)Math.Round(d);

                return 0;
            }

            if (dt == DataType.Real)
            {
                double d;
                if (rawValue is double)
                    return rawValue;

                if (double.TryParse(rawValue.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                    return d;

                if (double.TryParse(rawValue.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out d))
                    return d;

                if (double.TryParse(rawValue.ToString().Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                    return d;

                return 0.0;
            }

            // fallback: texto
            return rawValue.ToString();
        }

        private static object GetDefault(DataType dt)
        {
            if (dt == DataType.Text) return "";
            if (dt == DataType.Integer) return 0;
            if (dt == DataType.Real) return 0.0;
            if (dt == DataType.TrueFalse) return false;
            return "";
        }
    }

    [DataContract]
    internal class BlockPsetMappingFile
    {
        [DataMember] public int Version;
        [DataMember] public string BlockName;
        [DataMember] public List<PsetFieldMap> Maps;
        [DataMember] public string LayerName;
        [DataMember] public bool UseLayerFilter;


        public BlockPsetMappingFile()
        {
            Version = 1;
            BlockName = "";
            LayerName = "";
            UseLayerFilter = true;
            Maps = new List<PsetFieldMap>();
        }

    }

    [DataContract]
    internal class PsetFieldMap
    {
        [DataMember] public string PsetName;
        [DataMember] public string FieldName;

        // "NONE" | "CONST" | "ATTR:TAG" | "DYN:Name" | "BUI:Layer" etc
        [DataMember] public string SourceCode;

        // usado quando SourceCode == CONST
        [DataMember] public string ConstantValue;

        public PsetFieldMap()
        {
            PsetName = "";
            FieldName = "";
            SourceCode = "NONE";
            ConstantValue = "";
        }
    }

    internal class PsetFieldInfo
    {
        public string PsetName;
        public string FieldName;
        public DataType DataType;
        public bool IsAutomatic;
    }

    internal class BlockSourceOption
    {
        public string Code;
        public string Display;
        public string Preview;

        public BlockSourceOption(string code, string display, string preview)
        {
            Code = code;
            Display = display;
            Preview = preview;
        }
    }

    internal static class BlockPsetMappingStorage
    {
        public static string GetDefaultFolder()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "AutomacoesCivil3D", "PsetMappings");
        }

        public static string GetDefaultFilePath(string blockName)
        {
            string safe = MakeSafeFileName(blockName);
            return Path.Combine(GetDefaultFolder(), safe + ".json");
        }

        public static void Save(BlockPsetMappingFile file, string path)
        {
            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(BlockPsetMappingFile));
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                ser.WriteObject(fs, file);
            }
        }

        public static BlockPsetMappingFile TryLoad(string path)
        {
            try
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(BlockPsetMappingFile));
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    BlockPsetMappingFile file = (BlockPsetMappingFile)ser.ReadObject(fs);
                    return file;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }

    internal class BlockPsetMapperRow
    {
        public string PsetName { get; set; }
        public string FieldName { get; set; }
        public string DataType { get; set; }
        public string Automatico { get; set; }
        public string SourceCode { get; set; }
        public string Constante { get; set; }
        public string Preview { get; set; }
    }

    internal class BlockPsetMapperForm : Form
    {
        private readonly string _blockName;
        private readonly List<BlockSourceOption> _sources;
        private readonly List<PsetFieldInfo> _fields;

        private DataGridView _grid;
        private ListView _lvSources;
        private Button _btnOk;
        private Button _btnCancel;

        private BindingSource _binding;

        public BlockPsetMapperForm(
            string blockName,
            List<BlockSourceOption> sources,
            List<PsetFieldInfo> fields,
            BlockPsetMappingFile existing)
        {
            MinimumSize = new Size(900, 550);
            KeyPreview = true;
            _blockName = blockName;
            _sources = sources;
            _fields = fields;

            Text = "Mapeamento Bloco -> Property Sets | " + blockName;
            Width = 1100;
            Height = 650;
            StartPosition = FormStartPosition.CenterScreen;

            BuildUi(existing);
        }

        public BlockPsetMappingFile GetMappingFile()
        {
            BlockPsetMappingFile file = new BlockPsetMappingFile();
            file.Version = 1;
            file.BlockName = _blockName;

            List<BlockPsetMapperRow> rows = _binding.List.Cast<BlockPsetMapperRow>().ToList();
            foreach (BlockPsetMapperRow r in rows)
            {
                PsetFieldMap map = new PsetFieldMap();
                map.PsetName = r.PsetName;
                map.FieldName = r.FieldName;
                map.SourceCode = string.IsNullOrWhiteSpace(r.SourceCode) ? "NONE" : r.SourceCode;
                map.ConstantValue = r.Constante ?? "";

                file.Maps.Add(map);
            }

            return file;
        }

        private void BuildUi(BlockPsetMappingFile existing)
        {
            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Vertical;
            split.SplitterDistance = 380;

            _lvSources = new ListView();
            _lvSources.Dock = DockStyle.Fill;
            _lvSources.View = View.Details;
            _lvSources.FullRowSelect = true;
            _lvSources.Columns.Add("Fonte", 250);
            _lvSources.Columns.Add("Valor (bloco selecionado)", 300);

            foreach (BlockSourceOption s in _sources)
            {
                ListViewItem it = new ListViewItem(s.Display);
                it.SubItems.Add(s.Preview ?? "");
                it.Tag = s.Code;
                _lvSources.Items.Add(it);
            }

            split.Panel1.Controls.Add(_lvSources);

            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AutoGenerateColumns = false;

            DataGridViewTextBoxColumn cPset = new DataGridViewTextBoxColumn();
            cPset.HeaderText = "PSet";
            cPset.DataPropertyName = "PsetName";
            cPset.ReadOnly = true;
            cPset.Width = 180;

            DataGridViewTextBoxColumn cField = new DataGridViewTextBoxColumn();
            cField.HeaderText = "Campo";
            cField.DataPropertyName = "FieldName";
            cField.ReadOnly = true;
            cField.Width = 220;

            DataGridViewTextBoxColumn cType = new DataGridViewTextBoxColumn();
            cType.HeaderText = "Tipo";
            cType.DataPropertyName = "DataType";
            cType.ReadOnly = true;
            cType.Width = 80;

            DataGridViewTextBoxColumn cAuto = new DataGridViewTextBoxColumn();
            cAuto.HeaderText = "Auto";
            cAuto.DataPropertyName = "Automatico";
            cAuto.ReadOnly = true;
            cAuto.Width = 60;

            DataGridViewComboBoxColumn cSource = new DataGridViewComboBoxColumn();
            cSource.HeaderText = "Origem";
            cSource.DataPropertyName = "SourceCode";
            cSource.Width = 260;
            cSource.DisplayMember = "Display";
            cSource.ValueMember = "Code";
            cSource.DataSource = _sources.Select(s => new { s.Code, s.Display }).ToList();

            DataGridViewTextBoxColumn cConst = new DataGridViewTextBoxColumn();
            cConst.HeaderText = "Constante (se Origem=Constante)";
            cConst.DataPropertyName = "Constante";
            cConst.Width = 220;

            DataGridViewTextBoxColumn cPrev = new DataGridViewTextBoxColumn();
            cPrev.HeaderText = "Preview";
            cPrev.DataPropertyName = "Preview";
            cPrev.ReadOnly = true;
            cPrev.Width = 200;

            _grid.Columns.AddRange(new DataGridViewColumn[] { cPset, cField, cType, cAuto, cSource, cConst, cPrev });

            _binding = new BindingSource();
            _binding.DataSource = BuildRows(existing);
            _grid.DataSource = _binding;

            _grid.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex < 0)
                    return;

                UpdatePreviewForRow(e.RowIndex);
            };
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_grid.IsCurrentCellDirty)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            split.Panel2.Controls.Add(_grid);

            FlowLayoutPanel bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 52;
            bottom.FlowDirection = FlowDirection.RightToLeft;
            bottom.WrapContents = false;
            bottom.Padding = new Padding(8);

            Button btnOk = new Button();
            btnOk.Text = "Salvar e Aplicar";
            btnOk.Width = 160;
            btnOk.Height = 30;
            btnOk.DialogResult = DialogResult.OK;

            Button btnCancel = new Button();
            btnCancel.Text = "Cancelar";
            btnCancel.Width = 120;
            btnCancel.Height = 30;
            btnCancel.DialogResult = DialogResult.Cancel;

            bottom.Controls.Add(btnCancel);
            bottom.Controls.Add(btnOk);

            // atalhos padrão
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            // IMPORTANTE: adicionar o bottom ANTES do split para não sumir
            Controls.Add(bottom);
            Controls.Add(split);

            // garante que o rodapé fica visível mesmo com layout/DPI zoado
            bottom.BringToFront();


            // calcula preview inicial
            for (int i = 0; i < _grid.Rows.Count; i++)
                UpdatePreviewForRow(i);
        }

        private List<BlockPsetMapperRow> BuildRows(BlockPsetMappingFile existing)
        {
            Dictionary<string, string> existingMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> existingConst = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (existing != null && existing.Maps != null)
            {
                foreach (PsetFieldMap m in existing.Maps)
                {
                    string key = (m.PsetName ?? "") + "|" + (m.FieldName ?? "");
                    if (!existingMap.ContainsKey(key))
                        existingMap[key] = m.SourceCode ?? "NONE";

                    if (!existingConst.ContainsKey(key))
                        existingConst[key] = m.ConstantValue ?? "";
                }
            }

            List<BlockPsetMapperRow> rows = new List<BlockPsetMapperRow>();

            foreach (PsetFieldInfo f in _fields)
            {
                string key = (f.PsetName ?? "") + "|" + (f.FieldName ?? "");
                string src = existingMap.ContainsKey(key) ? existingMap[key] : "NONE";
                string con = existingConst.ContainsKey(key) ? existingConst[key] : "";

                BlockPsetMapperRow r = new BlockPsetMapperRow();
                r.PsetName = f.PsetName;
                r.FieldName = f.FieldName;
                r.DataType = f.DataType.ToString();
                r.Automatico = f.IsAutomatic ? "SIM" : "NÃO";
                r.SourceCode = string.IsNullOrWhiteSpace(src) ? "NONE" : src;
                r.Constante = con;
                r.Preview = "";

                rows.Add(r);
            }

            return rows;
        }

        private void UpdatePreviewForRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _binding.Count)
                return;

            BlockPsetMapperRow r = (BlockPsetMapperRow)_binding[rowIndex];
            if (r == null)
                return;

            BlockSourceOption opt = _sources.FirstOrDefault(o => string.Equals(o.Code, r.SourceCode, StringComparison.OrdinalIgnoreCase));
            if (opt == null)
            {
                r.Preview = "";
                _binding.ResetItem(rowIndex);
                return;
            }

            if (string.Equals(r.SourceCode, "CONST", StringComparison.OrdinalIgnoreCase))
            {
                r.Preview = r.Constante ?? "";
                _binding.ResetItem(rowIndex);
                return;
            }

            r.Preview = opt.Preview ?? "";
            _binding.ResetItem(rowIndex);
        }
    }
}
