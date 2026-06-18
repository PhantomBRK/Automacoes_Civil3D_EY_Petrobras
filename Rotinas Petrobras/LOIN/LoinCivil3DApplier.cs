using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using AecDataType = Autodesk.Aec.PropertyData.DataType;
using Color = Autodesk.AutoCAD.Colors.Color;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace AutomacoesCivil3D
{
    public static class LoinCivil3DApplier
    {
        // 4 Psets unificados — decisão Petrobras: ABCD são suficientes.
        // Sem split per-disciplina, sem Pset_Requisitos, sem IfcObject Properties.
        public const string PsetAName = "Pset_A - Dados de Projeto";
        public const string PsetBName = "Pset_B - Informacoes dos Elementos";
        public const string PsetCName = "Pset_C - Propriedades Fisicas dos Objetos";
        public const string PsetDName = "Pset_D - Layer IFC e Classificacao";

        // Alias mantido para compat (resolvers antigos buscavam por este nome).
        public const string PsetCUnifiedName = PsetCName;

        // Constantes/prefixos legacy — mantidos apenas para que código antigo
        // que ainda as referencia continue compilando. NÃO geram Psets novos.
        // PsetIfcName e PsetRequirementsName apontam para nomes que NÃO são
        // criados pelo BuildPsets atual; SetPsetValues nesses Psets vira no-op
        // (o Pset não existe → SetPsetValues ignora silenciosamente).
        public const string PsetIfcName = "IfcObject Properties";
        public const string PsetRequirementsName = "Pset_Requisitos por Elemento";
        public const string PsetBPrefix = "Pset_B_";
        public const string PsetCPrefix = "Pset_C_";
        public const string PsetRequirementsPrefix = "Pset_Requisitos_";

        // Helpers per-disciplina REVERTIDOS — retornam o nome UNIFICADO,
        // ignorando o código. Mantidos com a mesma assinatura para não quebrar
        // call sites em LoinExportacaoSolidosCorredores.
        public static string PsetBNameFor(string codigoDisciplina) => PsetBName;
        public static string PsetCNameFor(string codigoDisciplina) => PsetCName;
        public static string PsetRequirementsNameFor(string codigoDisciplina) => PsetRequirementsName;

        public sealed class ResourceSummary
        {
            public int CreatedLayers { get; set; }
            public int UpdatedLayers { get; set; }
            public int CreatedPsets { get; set; }
            public int UpdatedPsets { get; set; }
            public int AddedProperties { get; set; }
            public int Errors { get; set; }
        }

        public sealed class SelectionApplySummary
        {
            public int Selected { get; set; }
            public int Applied { get; set; }
            public int WithoutLoinLayer { get; set; }
            public int Errors { get; set; }
        }

        public static ResourceSummary EnsureResources(
            Database db,
            Transaction tr,
            Editor ed,
            LoinConfiguration config)
        {
            NormalizePsetPrefixes(config);

            ResourceSummary summary = new ResourceSummary();

            EnsurePropertySets(db, tr, ed, config, summary);
            EnsureLayers(db, tr, ed, config, summary);

            return summary;
        }

        private static void NormalizePsetPrefixes(LoinConfiguration config)
        {
            if (config?.PropertySetDefinitions == null)
                return;

            foreach (LoinPsetDefinition pset in config.PropertySetDefinitions)
            {
                pset.Name = ReplaceLoinPsetPrefix(pset.Name);

                foreach (LoinPsetProperty property in pset.Properties ?? new List<LoinPsetProperty>())
                {
                    property.Name = ReplaceLoinPsetPrefix(property.Name);
                    property.Description = ReplaceLoinPsetPrefix(property.Description);
                }
            }
        }

        private static string ReplaceLoinPsetPrefix(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? value
                : value.Replace("LOIN_", "Pset_");
        }

        public static SelectionApplySummary ApplyToSelection(
            Database db,
            Transaction tr,
            Editor ed,
            LoinConfiguration config,
            IEnumerable<ObjectId> selectedIds)
        {
            SelectionApplySummary summary = new SelectionApplySummary();
            Dictionary<string, LoinElementDefinition> elementsByLayer = config.Elements
                .Where(e => !string.IsNullOrWhiteSpace(e.Layer))
                .GroupBy(e => e.Layer, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            Dictionary<string, ObjectId> psetIds = GetPsetIds(db, tr, config);

            foreach (ObjectId id in selectedIds)
            {
                summary.Selected++;

                try
                {
                    if (id.IsNull || id.IsErased)
                    {
                        summary.WithoutLoinLayer++;
                        continue;
                    }

                    Entity ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;
                    if (ent == null || string.IsNullOrWhiteSpace(ent.Layer))
                    {
                        summary.WithoutLoinLayer++;
                        continue;
                    }

                    if (!elementsByLayer.TryGetValue(ent.Layer, out LoinElementDefinition element))
                    {
                        summary.WithoutLoinLayer++;
                        continue;
                    }

                    string codigoDisc = LoinWorkbookReader.CodigoDisciplina(element.Discipline);
                    AttachConfiguredPsets(ent, tr, psetIds, codigoDisc);
                    FillElementMetadata(ent, tr, psetIds, element, codigoDisc);
                    WriteIfcExtensionMetadata(ent, tr, element);
                    summary.Applied++;
                }
                catch (System.Exception ex)
                {
                    summary.Errors++;
                    ed.WriteMessage("\n[LOIN] Falha ao aplicar em objeto " + id + ": " + ex.Message);
                }
            }

            return summary;
        }

        private static void EnsurePropertySets(
            Database db,
            Transaction tr,
            Editor ed,
            LoinConfiguration config,
            ResourceSummary summary)
        {
            DictionaryPropertySetDefinitions dict = new DictionaryPropertySetDefinitions(db);

            foreach (LoinPsetDefinition pset in config.PropertySetDefinitions)
            {
                try
                {
                    EnsurePropertySetDefinition(db, tr, dict, pset, summary);
                }
                catch (System.Exception ex)
                {
                    summary.Errors++;
                    ed.WriteMessage("\n[LOIN] Erro ao criar/atualizar Pset '" + pset.Name + "': " + ex.Message);
                }
            }

            // PSets canônicos UNIFICADOS do template Petrobras (Pset_A, Pset_C, Pset_D, IfcObject).
            // Podem vir do template SEM estar no LoinConfiguration — garantimos o
            // AppliesToFilter para cobrir AcDb3dSolid/Body/Surface, sem mexer em propriedades.
            //
            // Os Psets per-disciplina (Pset_B_<CODE>, Pset_Requisitos_<CODE>)
            // são SEMPRE criados a partir do config.PropertySetDefinitions — não dependem
            // de template. Por isso não entram nesta lista.
            // Pset_C é unificado (PsetCUnifiedName), entra junto com Pset_A/D.
            // ABCD — únicos Psets canônicos. Garantia de AppliesToFilter cobrir
            // Solid3d/Body/Surface mesmo se já existirem no template.
            string[] canonicalLoinPsets = { PsetAName, PsetBName, PsetCName, PsetDName };
            HashSet<string> jaProcessados = new HashSet<string>(
                config.PropertySetDefinitions?.Select(p => p.Name) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (string name in canonicalLoinPsets)
            {
                if (jaProcessados.Contains(name)) continue;
                if (!dict.Has(name, tr)) continue;

                try
                {
                    ObjectId id = dict.GetAt(name);
                    PropertySetDefinition psd = tr.GetObject(id, OpenMode.ForWrite) as PropertySetDefinition;
                    EnsureAppliesToCoversExportedTypes(psd);
                }
                catch (System.Exception ex)
                {
                    summary.Errors++;
                    ed.WriteMessage("\n[LOIN] Erro ao ajustar AppliesToFilter do Pset '" + name + "': " + ex.Message);
                }
            }
        }

        private static void EnsurePropertySetDefinition(
            Database db,
            Transaction tr,
            DictionaryPropertySetDefinitions dict,
            LoinPsetDefinition pset,
            ResourceSummary summary)
        {
            if (string.IsNullOrWhiteSpace(pset.Name))
                return;

            if (dict.Has(pset.Name, tr))
            {
                ObjectId existingId = dict.GetAt(pset.Name);
                PropertySetDefinition existing = (PropertySetDefinition)tr.GetObject(existingId, OpenMode.ForWrite);
                int added = AddMissingProperties(db, existing, pset.Properties);

                // Garante que o AppliesToFilter cobre AcDbBody/Surface etc
                // mesmo em PSets que já existiam no template (criados sem esses
                // tipos por uma versão antiga ou pelo Civil 3D).
                EnsureAppliesToCoversExportedTypes(existing);

                if (added > 0)
                {
                    summary.UpdatedPsets++;
                    summary.AddedProperties += added;
                }

                return;
            }

            PropertySetDefinition psd = new PropertySetDefinition();
            psd.SetToStandard(db);
            psd.SubSetDatabaseDefaults(db);
            psd.AppliesToAll = true;
            psd.AlternateName = pset.Name;
            psd.Description = pset.Description ?? string.Empty;

            // Cobre todos os tipos que corridor.ExportSolids() produz, incluindo
            // AcDbBody — caso contrário o AddPropertySet falha silenciosamente em
            // junções/conexões complexas e o sólido sai sem Pset_A/B/C anexado.
            StringCollection appliesTo = new StringCollection();
            foreach (string t in ExportedTypeNames) appliesTo.Add(t);
            TrySetAppliesToFilter(psd, appliesTo);

            foreach (LoinPsetProperty property in pset.Properties)
            {
                PropertyDefinition prop = BuildPropertyDefinition(db, property);
                psd.Definitions.Add(prop);
            }

            dict.AddNewRecord(pset.Name, psd);
            tr.AddNewlyCreatedDBObject(psd, true);
            summary.CreatedPsets++;
        }

        private static int AddMissingProperties(
            Database db,
            PropertySetDefinition psd,
            IEnumerable<LoinPsetProperty> incoming)
        {
            HashSet<string> existingNames = new HashSet<string>(
                psd.Definitions.OfType<PropertyDefinition>()
                    .Select(p => p.Name ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            int added = 0;

            foreach (LoinPsetProperty property in incoming)
            {
                if (string.IsNullOrWhiteSpace(property.Name) || existingNames.Contains(property.Name))
                    continue;

                PropertyDefinition prop = BuildPropertyDefinition(db, property);
                psd.Definitions.Add(prop);
                existingNames.Add(property.Name);
                added++;
            }

            return added;
        }

        private static PropertyDefinition BuildPropertyDefinition(Database db, LoinPsetProperty property)
        {
            PropertyDefinition prop = new PropertyDefinition();
            prop.SetToStandard(db);
            prop.SubSetDatabaseDefaults(db);
            prop.Name = property.Name;
            prop.Description = property.Description ?? property.Name;
            prop.DataType = ResolveDataType(property.DataType);
            prop.DefaultData = string.Empty;
            return prop;
        }

        private static AecDataType ResolveDataType(string dataType)
        {
            if (Enum.TryParse(dataType, true, out AecDataType parsed))
                return parsed;

            return AecDataType.Text;
        }

        private static void TrySetAppliesToFilter(PropertySetDefinition psd, StringCollection appliesTo)
        {
            try
            {
                psd.SetAppliesToFilter(appliesTo, false);
            }
            catch
            {
                psd.AppliesToAll = true;
            }
        }

        // Tipos que corridor.ExportSolids() pode gerar — qualquer PSet usado por
        // sólidos exportados precisa cobrir todos eles, senão AddPropertySet
        // falha silenciosamente e o sólido fica sem o PSet anexado.
        private static readonly string[] ExportedTypeNames = new[]
        {
            "AcDbEntity",
            "AcDb3dSolid",
            "AcDbBody",
            "AcDbSurface",
            "AcDbBlockReference",
            "AcDbPolyline",
            "AcDb3dPolyline",
            "AcDbLine",
            "AcDbArc",
            "AcDbCircle",
            "AeccDbFeatureLine"
        };

        // Garante que o AppliesToFilter do PSet cobre todos os tipos que o
        // ExportSolids pode produzir. Idempotente — agrega ao filter existente
        // em vez de sobrescrever (preserva o que o template já tinha).
        private static void EnsureAppliesToCoversExportedTypes(PropertySetDefinition psd)
        {
            if (psd == null) return;

            try
            {
                if (psd.AppliesToAll)
                    return; // já cobre tudo; nada a fazer
            }
            catch { }

            StringCollection current = null;
            try { current = psd.AppliesToFilter; }
            catch { current = null; }

            HashSet<string> existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (current != null)
                foreach (string c in current)
                    if (!string.IsNullOrWhiteSpace(c))
                        existing.Add(c.Trim());

            bool changed = false;
            foreach (string t in ExportedTypeNames)
            {
                if (existing.Add(t)) changed = true;
            }

            if (!changed) return;

            StringCollection updated = new StringCollection();
            foreach (string e in existing) updated.Add(e);

            try { psd.SetAppliesToFilter(updated, false); }
            catch
            {
                // Fallback: se SetAppliesToFilter falhar (versão do template estranha),
                // libera tudo pra não bloquear o attach.
                try { psd.AppliesToAll = true; } catch { }
            }
        }

        private static void EnsureLayers(
            Database db,
            Transaction tr,
            Editor ed,
            LoinConfiguration config,
            ResourceSummary summary)
        {
            LayerTable layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            foreach (LoinLayerDefinition layer in config.Layers)
            {
                string layerName = SanitizeLayerName(layer.Name);
                if (string.IsNullOrWhiteSpace(layerName))
                    continue;

                try
                {
                    if (layerTable.Has(layerName))
                    {
                        LayerTableRecord existing = (LayerTableRecord)tr.GetObject(layerTable[layerName], OpenMode.ForWrite);
                        ApplyLayerColor(existing, layer.Color);
                        summary.UpdatedLayers++;
                        continue;
                    }

                    if (!layerTable.IsWriteEnabled)
                        layerTable.UpgradeOpen();

                    LayerTableRecord record = new LayerTableRecord
                    {
                        Name = layerName,
                        IsPlottable = true,
                        LinetypeObjectId = db.ContinuousLinetype,
                        LineWeight = LineWeight.ByLineWeightDefault
                    };

                    ApplyLayerColor(record, layer.Color);

                    ObjectId newLayerId = layerTable.Add(record);
                    tr.AddNewlyCreatedDBObject(record, true);
                    summary.CreatedLayers++;
                }
                catch (System.Exception ex)
                {
                    summary.Errors++;
                    ed.WriteMessage("\n[LOIN] Erro ao criar/atualizar layer '" + layerName + "': " + ex.Message);
                }
            }
        }

        private static void ApplyLayerColor(LayerTableRecord record, LoinColorDefinition color)
        {
            if (color != null && color.HasRgb)
            {
                record.Color = Color.FromRgb(
                    Convert.ToByte(color.Red.Value),
                    Convert.ToByte(color.Green.Value),
                    Convert.ToByte(color.Blue.Value));
                return;
            }

            short aci = color?.FallbackAci ?? 7;
            record.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
        }

        private static Dictionary<string, ObjectId> GetPsetIds(Database db, Transaction tr, LoinConfiguration config)
        {
            Dictionary<string, ObjectId> ids = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
            DictionaryPropertySetDefinitions dict = new DictionaryPropertySetDefinitions(db);

            foreach (LoinPsetDefinition pset in config.PropertySetDefinitions)
            {
                if (dict.Has(pset.Name, tr))
                    ids[pset.Name] = dict.GetAt(pset.Name);
            }

            return ids;
        }

        // Anexa Psets ao elemento filtrando por disciplina:
        //   - Sempre: Pset_A, Pset_C unificado, Pset_D, IfcObject (transversais)
        //   - Apenas se bater com a disciplina do elemento: Pset_B_<CODE>,
        //     Pset_Requisitos_<CODE>
        // Sem o filtro, um sólido de PAV teria Pset_B_TER, Pset_B_DRE etc. anexados
        // (todos vazios), poluindo a árvore IFC. O filtro mantém só os relevantes.
        private static void AttachConfiguredPsets(
            Entity ent,
            Transaction tr,
            Dictionary<string, ObjectId> psetIds,
            string codigoDisciplina)
        {
            // Pset unificado (ABCD) — anexa todos os Psets gerados em config.
            // O parâmetro codigoDisciplina é mantido na assinatura para compat com
            // os call sites, mas não é mais usado para filtrar (não há mais Psets
            // per-disciplina). Filtra apenas Psets per-disciplina LEGACY que possam
            // existir em DWGs já preparados antes da reversão — esses não anexam,
            // poupando ruído na árvore IFC.
            foreach (KeyValuePair<string, ObjectId> kvp in psetIds)
            {
                if (kvp.Value.IsNull || kvp.Value.IsErased)
                    continue;

                string name = kvp.Key;

                // Filtra legacy per-disciplina (Pset_B_PAV, Pset_C_TER,
                // Pset_Requisitos_DRE, etc.) — apenas se eles existirem
                // no dict por terem sido criados em runs anteriores.
                bool isLegacyPerDisc =
                    (name.StartsWith(PsetBPrefix,            StringComparison.OrdinalIgnoreCase) && !name.Equals(PsetBName, StringComparison.OrdinalIgnoreCase)) ||
                    (name.StartsWith(PsetCPrefix,            StringComparison.OrdinalIgnoreCase) && !name.Equals(PsetCName, StringComparison.OrdinalIgnoreCase)) ||
                    (name.StartsWith(PsetRequirementsPrefix, StringComparison.OrdinalIgnoreCase));

                if (isLegacyPerDisc) continue;

                try
                {
                    PropertyDataServices.AddPropertySet(ent, kvp.Value);
                }
                catch
                {
                    // Ja anexado ou entidade nao aceita o Pset. A leitura seguinte decide.
                }
            }
        }

        private static void FillElementMetadata(
            Entity ent,
            Transaction tr,
            Dictionary<string, ObjectId> psetIds,
            LoinElementDefinition element,
            string codigoDisciplina)
        {
            SetPsetValues(ent, tr, psetIds, PsetDName, new Dictionary<string, string>
            {
                ["DISCIPLINA"] = element.Discipline,
                ["ELEMENTO"] = element.Element,
                ["IFC_CLASS"] = element.IfcClass,
                ["PREDEFINED_TYPE"] = element.PredefinedType,
                ["CLASSIFICATION_CODE"] = element.ClassificationCode,
                ["LAYER"] = element.Layer,
                ["COLOR_RAW"] = element.Color.Raw,
                ["COLOR_RGB"] = FormatRgb(element.Color),
                ["Pset_SOURCE_SHEET"] = element.SourceSheet,
                ["Pset_SOURCE_ROW"] = element.SourceRow.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

            // Pset_Requisitos e IfcObject Properties REMOVIDOS da geração — ABCD bastam.
            // Se o template antigo ainda contém esses Psets, SetPsetValues neles seria
            // no-op porque eles não estão em psetIds (não foram ensured por EnsureResources).
            // codigoDisciplina mantido na assinatura por compat de call site.
            _ = codigoDisciplina;
        }

        private static void SetPsetValues(
            Entity ent,
            Transaction tr,
            Dictionary<string, ObjectId> psetIds,
            string psetName,
            Dictionary<string, string> values)
        {
            if (!psetIds.TryGetValue(psetName, out ObjectId defId) || defId.IsNull)
                return;

            ObjectId psetId;
            try
            {
                psetId = PropertyDataServices.GetPropertySet(ent, defId);
            }
            catch
            {
                return;
            }

            if (psetId.IsNull || psetId.IsErased)
                return;

            PropertySet pset = tr.GetObject(psetId, OpenMode.ForWrite, false) as PropertySet;
            if (pset == null)
                return;

            foreach (KeyValuePair<string, string> item in values)
            {
                try
                {
                    int propertyId = pset.PropertyNameToId(item.Key);
                    if (propertyId != -1)
                        pset.SetAt(propertyId, item.Value ?? string.Empty);
                }
                catch
                {
                }
            }
        }

        private static void WriteIfcExtensionMetadata(
            Entity ent,
            Transaction tr,
            LoinElementDefinition element)
        {
            IfcResolvedMetadata metadata = new IfcResolvedMetadata
            {
                IfcClass = element.IfcClass,
                PredefinedType = element.PredefinedType,
                ObjectType = element.Element,
                Name = element.Element,
                Tag = ent.Handle.ToString(),
                Description = element.Element,
                Layer = ent.Layer,
                System = element.Discipline,
                Subsystem = string.Empty
            };

            IfcAplicarMapeamentoJson.WriteMetadataToObject(ent, metadata, tr);
        }

        private static string FormatRgb(LoinColorDefinition color)
        {
            if (color == null || !color.HasRgb)
                return string.Empty;

            return color.Red + "," + color.Green + "," + color.Blue;
        }

        private static string SanitizeLayerName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            char[] invalid = { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', '=' };
            string sanitized = name.Trim();
            foreach (char c in invalid)
                sanitized = sanitized.Replace(c, '-');

            return sanitized;
        }
    }
}
