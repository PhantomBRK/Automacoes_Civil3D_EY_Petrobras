using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Color = Autodesk.AutoCAD.Colors.Color;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace AutomacoesCivil3D
{
    // Cria os estilos Shape / Link / Marker (Point) + Material Area Fill do Code Set
    // dos corredores a partir do mapeamento LOIN salvo pela janela LOINMAP
    // (loin_mapeamento.json). A cor vem da linha LOIN associada e a hachura é
    // sempre sólida ("SOLID").
    public sealed class LoinCodeSetStyleCorredores
    {
        private const string CodeSetStyleName = "LOIN - Code Set Corredores";
        private const string UnmappedLayerName = "Pset_SEM_MAPEAMENTO";

        // Origens do LoinItemMapeamento que viram cada tipo de estilo do Code Set.
        // "Manual" é aceito em qualquer tipo — o usuário pode digitar livre na window.
        private static readonly HashSet<string> ShapeOrigins = new(StringComparer.OrdinalIgnoreCase)
            { "Corridor-Shape", "Manual" };
        private static readonly HashSet<string> LinkOrigins = new(StringComparer.OrdinalIgnoreCase)
            { "Corridor-Link", "Manual" };
        private static readonly HashSet<string> PointOrigins = new(StringComparer.OrdinalIgnoreCase)
            { "Corridor-Point", "Manual" };

        private sealed class CorridorCodeInventory
        {
            public int CorridorCount { get; set; }
            public List<ObjectId> CorridorIds { get; } = new List<ObjectId>();
            public SortedSet<string> ShapeCodes { get; } = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            public SortedSet<string> LinkCodes { get; } = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            public SortedSet<string> PointCodes { get; } = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            public List<string> Warnings { get; } = new List<string>();
        }

        private sealed class LoinStyleDefinition
        {
            public string StyleBaseName { get; set; } = string.Empty;
            public string LayerName { get; set; } = string.Empty;
            public LoinColorDefinition Color { get; set; } = new LoinColorDefinition { FallbackAci = 7 };
            public string Discipline { get; set; } = string.Empty;
            public string Element { get; set; } = string.Empty;
            public string ClassificationCode { get; set; } = string.Empty;
            public bool IsFallback { get; set; }
        }

        private sealed class CodeSetBuildSummary
        {
            public int CreatedShapeStyles { get; set; }
            public int UpdatedShapeStyles { get; set; }
            public int CreatedMaterialAreaFillStyles { get; set; }
            public int UpdatedMaterialAreaFillStyles { get; set; }
            public int CreatedLinkStyles { get; set; }
            public int UpdatedLinkStyles { get; set; }
            public int CreatedMarkerStyles { get; set; }
            public int UpdatedMarkerStyles { get; set; }
            public int AddedCodeSetItems { get; set; }
            public int UpdatedCodeSetItems { get; set; }
            public int AppliedCorridors { get; set; }
            public int UnmappedCodes { get; set; }
            public List<string> Warnings { get; } = new List<string>();
        }

        [CommandMethod("LOIN_CODESET_CORREDORES", CommandFlags.Modal)]
        public void Executar()
        {
            Editor ed = Manager.DocEditor;
            Document doc = Manager.DocCad;
            Database db = Manager.DocData;
            CivilDocument civilDoc = Manager.DocCivil;

            try
            {
                string mappingPath = ResolveMappingPath(doc);
                if (string.IsNullOrWhiteSpace(mappingPath) || !File.Exists(mappingPath))
                {
                    string aviso =
                        "Mapeamento LOIN não encontrado.\n\n" +
                        "Esperado em:\n  " + mappingPath + "\n\n" +
                        "Rode primeiro o comando LOINMAP, configure os mapeamentos e clique em 'Salvar Mapeamento'.";
                    ed.WriteMessage("\n[LOIN] " + aviso.Replace("\n", "\n[LOIN] "));
                    AcadApp.ShowAlertDialog(aviso);
                    return;
                }

                LoinMapeamentoConfig mapeamento = LoinMapeamentoService.Carregar(mappingPath);
                if (mapeamento.Mapeamentos.Count == 0)
                {
                    ed.WriteMessage("\n[LOIN] Arquivo carregado, porém sem itens mapeados.");
                    return;
                }

                LoinMappingResolver resolver = new LoinMappingResolver(mapeamento);

                CodeSetBuildSummary summary;
                CorridorCodeInventory inventory;

                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureFallbackLayer(db, tr);

                    inventory = CollectCorridorCodes(civilDoc, tr);
                    if (inventory.CorridorCount == 0)
                    {
                        ed.WriteMessage("\n[LOIN] Nenhum corredor local foi encontrado no desenho.");
                        tr.Commit();
                        return;
                    }

                    summary = BuildAndApplyCodeSetStyle(db, civilDoc, tr, resolver, inventory);
                    summary.Warnings.AddRange(inventory.Warnings);

                    tr.Commit();
                }

                ed.Regen();

                string report = FormatReport(inventory, summary, mappingPath);
                ed.WriteMessage("\n" + report.Replace("\r\n", "\n"));
                AcadApp.ShowAlertDialog(FormatAlert(inventory, summary));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n[LOIN] Erro ao criar Code Set Style dos corredores: " + ex.Message);
                MessageBox.Show(ex.Message, "LOIN - Code Set Corredores", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string ResolveMappingPath(Document doc)
        {
            string drawingPath = string.Empty;
            try { drawingPath = doc?.Name ?? string.Empty; }
            catch { drawingPath = string.Empty; }

            return LoinMapeamentoService.ResolverCaminhoConfig(drawingPath);
        }

        private static CorridorCodeInventory CollectCorridorCodes(CivilDocument civilDoc, Transaction tr)
        {
            CorridorCodeInventory inventory = new CorridorCodeInventory();

            foreach (ObjectId corridorId in civilDoc.CorridorCollection)
            {
                Corridor corridor = tr.GetObject(corridorId, OpenMode.ForRead, false) as Corridor;
                if (corridor == null || corridor.IsReferenceObject)
                    continue;

                inventory.CorridorCount++;
                inventory.CorridorIds.Add(corridorId);

                AddCodes(inventory.ShapeCodes, () => corridor.GetShapeCodes(), "shape", corridor.Name, inventory.Warnings);
                AddCodes(inventory.LinkCodes, () => corridor.GetLinkCodes(), "link", corridor.Name, inventory.Warnings);
                AddCodes(inventory.PointCodes, () => corridor.GetPointCodes(), "point", corridor.Name, inventory.Warnings);
            }

            return inventory;
        }

        private static void AddCodes(
            ICollection<string> target,
            Func<string[]> getter,
            string kind,
            string corridorName,
            ICollection<string> warnings)
        {
            try
            {
                foreach (string code in getter() ?? Array.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(code))
                        target.Add(code.Trim());
                }
            }
            catch (System.Exception ex)
            {
                warnings.Add($"Nao foi possivel ler {kind} codes do corredor '{corridorName}': {ex.Message}");
            }
        }

        private static CodeSetBuildSummary BuildAndApplyCodeSetStyle(
            Database db,
            CivilDocument civilDoc,
            Transaction tr,
            LoinMappingResolver resolver,
            CorridorCodeInventory inventory)
        {
            CodeSetBuildSummary summary = new CodeSetBuildSummary();

            ObjectId codeSetStyleId = GetOrCreateStyleId(civilDoc.Styles.CodeSetStyles, CodeSetStyleName);
            CodeSetStyle codeSetStyle = tr.GetObject(codeSetStyleId, OpenMode.ForWrite, false) as CodeSetStyle;
            if (codeSetStyle == null)
                throw new InvalidOperationException("Nao foi possivel criar ou abrir o Code Set Style LOIN.");

            foreach (string code in inventory.ShapeCodes)
            {
                LoinStyleDefinition definition = resolver.Resolve(code, ShapeOrigins);
                if (definition.IsFallback)
                    summary.UnmappedCodes++;

                ObjectId styleId = GetOrCreateShapeStyle(db, civilDoc, tr, code, definition, summary);
                ObjectId materialAreaFillStyleId = GetOrCreateMaterialAreaFillStyle(db, civilDoc, tr, code, definition, summary);
                UpsertCodeSetItem(
                    codeSetStyle,
                    code,
                    styleId,
                    materialAreaFillStyleId,
                    definition,
                    SubassemblySubentityStyleType.ShapeType,
                    summary);
            }

            foreach (string code in inventory.LinkCodes)
            {
                LoinStyleDefinition definition = resolver.Resolve(code, LinkOrigins);
                if (definition.IsFallback)
                    summary.UnmappedCodes++;

                ObjectId styleId = GetOrCreateLinkStyle(db, civilDoc, tr, code, definition, summary);
                UpsertCodeSetItem(
                    codeSetStyle,
                    code,
                    styleId,
                    ObjectId.Null,
                    definition,
                    SubassemblySubentityStyleType.LinkType,
                    summary);
            }

            // Point codes não cabem no CodeSetStyle (a API só aceita ShapeType e LinkType).
            // Criamos os MarkerStyles isolados — o usuário pode atribuir manualmente onde
            // for necessário (ex.: PointGroup, FeatureLine, etc.).
            foreach (string code in inventory.PointCodes)
            {
                LoinStyleDefinition definition = resolver.Resolve(code, PointOrigins);
                if (definition.IsFallback)
                    summary.UnmappedCodes++;

                GetOrCreateMarkerStyle(db, civilDoc, tr, code, definition, summary);
            }

            foreach (ObjectId corridorId in inventory.CorridorIds)
            {
                try
                {
                    Corridor corridor = tr.GetObject(corridorId, OpenMode.ForWrite, false) as Corridor;
                    if (corridor == null || corridor.IsReferenceObject)
                        continue;

                    corridor.CodeSetStyleId = codeSetStyleId;
                    summary.AppliedCorridors++;
                }
                catch (System.Exception ex)
                {
                    summary.Warnings.Add("Falha ao aplicar Code Set Style no corredor " + corridorId + ": " + ex.Message);
                }
            }

            return summary;
        }

        private static ObjectId GetOrCreateShapeStyle(
            Database db,
            CivilDocument civilDoc,
            Transaction tr,
            string code,
            LoinStyleDefinition definition,
            CodeSetBuildSummary summary)
        {
            string styleName = BuildStyleName("SHAPE", code, definition);
            ShapeStyleCollection collection = civilDoc.Styles.ShapeStyles;
            bool created = !collection.Contains(styleName);
            ObjectId styleId = created ? collection.Add(styleName) : collection[styleName];

            ShapeStyle style = tr.GetObject(styleId, OpenMode.ForWrite, false) as ShapeStyle;
            if (style == null)
                throw new InvalidOperationException("Nao foi possivel abrir o Shape Style '" + styleName + "'.");

            ConfigureShapeStyle(style, definition);

            if (created)
                summary.CreatedShapeStyles++;
            else
                summary.UpdatedShapeStyles++;

            EnsureLayer(db, tr, definition.LayerName, definition.Color);
            return styleId;
        }

        private static ObjectId GetOrCreateMaterialAreaFillStyle(
            Database db,
            CivilDocument civilDoc,
            Transaction tr,
            string code,
            LoinStyleDefinition definition,
            CodeSetBuildSummary summary)
        {
            string styleName = BuildStyleName("MATERIAL AREA FILL", code, definition);
            ShapeStyleCollection collection = civilDoc.Styles.ShapeStyles;
            bool created = !collection.Contains(styleName);
            ObjectId styleId = created ? collection.Add(styleName) : collection[styleName];

            ShapeStyle style = tr.GetObject(styleId, OpenMode.ForWrite, false) as ShapeStyle;
            if (style == null)
                throw new InvalidOperationException("Nao foi possivel abrir o Material Area Fill Style '" + styleName + "'.");

            ConfigureShapeStyle(style, definition);

            if (created)
                summary.CreatedMaterialAreaFillStyles++;
            else
                summary.UpdatedMaterialAreaFillStyles++;

            EnsureLayer(db, tr, definition.LayerName, definition.Color);
            return styleId;
        }

        private static ObjectId GetOrCreateLinkStyle(
            Database db,
            CivilDocument civilDoc,
            Transaction tr,
            string code,
            LoinStyleDefinition definition,
            CodeSetBuildSummary summary)
        {
            string styleName = BuildStyleName("LINK", code, definition);
            LinkStyleCollection collection = civilDoc.Styles.LinkStyles;
            bool created = !collection.Contains(styleName);
            ObjectId styleId = created ? collection.Add(styleName) : collection[styleName];

            LinkStyle style = tr.GetObject(styleId, OpenMode.ForWrite, false) as LinkStyle;
            if (style == null)
                throw new InvalidOperationException("Nao foi possivel abrir o Link Style '" + styleName + "'.");

            ConfigureLinkStyle(style, definition);

            if (created)
                summary.CreatedLinkStyles++;
            else
                summary.UpdatedLinkStyles++;

            EnsureLayer(db, tr, definition.LayerName, definition.Color);
            return styleId;
        }

        private static ObjectId GetOrCreateMarkerStyle(
            Database db,
            CivilDocument civilDoc,
            Transaction tr,
            string code,
            LoinStyleDefinition definition,
            CodeSetBuildSummary summary)
        {
            string styleName = BuildStyleName("MARKER", code, definition);
            MarkerStyleCollection collection = civilDoc.Styles.MarkerStyles;
            bool created = !collection.Contains(styleName);
            ObjectId styleId = created ? collection.Add(styleName) : collection[styleName];

            MarkerStyle style = tr.GetObject(styleId, OpenMode.ForWrite, false) as MarkerStyle;
            if (style == null)
                throw new InvalidOperationException("Nao foi possivel abrir o Marker Style '" + styleName + "'.");

            ConfigureMarkerStyle(style, definition);

            if (created)
                summary.CreatedMarkerStyles++;
            else
                summary.UpdatedMarkerStyles++;

            EnsureLayer(db, tr, definition.LayerName, definition.Color);
            return styleId;
        }

        private static void ConfigureLinkStyle(LinkStyle style, LoinStyleDefinition definition)
        {
            Color color = BuildAcadColor(definition.Color);
            ConfigureDisplay(style.GetDisplayStylePlan(), definition.LayerName, color);
            ConfigureDisplay(style.GetDisplayStyleModel(), definition.LayerName, color);
            ConfigureDisplay(style.GetDisplayStyleSection(), definition.LayerName, color);
        }

        private static void ConfigureShapeStyle(ShapeStyle style, LoinStyleDefinition definition)
        {
            Color color = BuildAcadColor(definition.Color);

            foreach (ShapeDisplayStyleType type in new[] { ShapeDisplayStyleType.Border, ShapeDisplayStyleType.AreaFill })
            {
                ConfigureDisplay(style.GetDisplayStylePlan(type), definition.LayerName, color);
                ConfigureDisplay(style.GetDisplayStyleModel(type), definition.LayerName, color);
                ConfigureDisplay(style.GetDisplayStyleProfile(type), definition.LayerName, color);
                ConfigureDisplay(style.GetDisplayStyleSection(type), definition.LayerName, color);
            }

            // Hachura sólida em todos os contextos (planta, modelo, perfil, seção)
            ConfigureHatch(style.GetHatchDisplayStylePlan());
            ConfigureHatch(style.GetHatchDisplayStyleModel());
            ConfigureHatch(style.GetHatchDisplayStyleProfile());
            ConfigureHatch(style.GetHatchDisplayStyleSection());
        }

        private static void ConfigureMarkerStyle(MarkerStyle style, LoinStyleDefinition definition)
        {
            Color color = BuildAcadColor(definition.Color);
            ConfigureDisplay(style.GetMarkerDisplayStylePlan(), definition.LayerName, color);
            ConfigureDisplay(style.GetMarkerDisplayStyleModel(), definition.LayerName, color);
            ConfigureDisplay(style.GetMarkerDisplayStyleProfile(), definition.LayerName, color);
            ConfigureDisplay(style.GetMarkerDisplayStyleSection(), definition.LayerName, color);
        }

        private static void ConfigureDisplay(DisplayStyle display, string layerName, Color color)
        {
            if (display == null)
                return;

            try { display.Visible = true; } catch { }
            try { display.Layer = SanitizeLayerName(layerName); } catch { }
            try { display.Color = color; } catch { }
        }

        private static void ConfigureHatch(HatchDisplayStyle hatch)
        {
            if (hatch == null)
                return;

            try { hatch.HatchType = HatchType.SolidFill; } catch { }
            try { hatch.Pattern = "SOLID"; } catch { }
            try { hatch.ScaleFactor = 1.0; } catch { }
        }

        private static void UpsertCodeSetItem(
            CodeSetStyle codeSetStyle,
            string code,
            ObjectId styleId,
            ObjectId materialAreaFillStyleId,
            LoinStyleDefinition definition,
            SubassemblySubentityStyleType styleType,
            CodeSetBuildSummary summary)
        {
            codeSetStyle.SubentityStyleType = styleType;

            CodeSetStyleItem item = null;
            try
            {
                item = codeSetStyle.GetItemBy(CodeSetStyleItemType.NormalItemType, code);
            }
            catch
            {
            }

            if (item == null)
            {
                try
                {
                    item = codeSetStyle.Add(code, styleId);
                    summary.AddedCodeSetItems++;
                }
                catch
                {
                    try
                    {
                        codeSetStyle.Remove(code);
                        item = codeSetStyle.Add(code, styleId);
                        summary.UpdatedCodeSetItems++;
                    }
                    catch (System.Exception ex)
                    {
                        summary.Warnings.Add("Falha ao adicionar codigo '" + code + "' ao Code Set Style: " + ex.Message);
                        return;
                    }
                }
            }
            else
            {
                try
                {
                    item.CodeStyleId = styleId;
                    summary.UpdatedCodeSetItems++;
                }
                catch (System.Exception ex)
                {
                    summary.Warnings.Add("Falha ao atualizar codigo '" + code + "' no Code Set Style: " + ex.Message);
                }
            }

            if (item == null)
                return;

            try { item.Description = BuildCodeDescription(definition); } catch { }
            try { item.Classification = definition.ClassificationCode ?? string.Empty; } catch { }

            if (!materialAreaFillStyleId.IsNull)
            {
                try
                {
                    item.MaterialAreaFillStyleId = materialAreaFillStyleId;
                }
                catch (System.Exception ex)
                {
                    summary.Warnings.Add("Falha ao aplicar Material Area Fill Style no codigo '" + code + "': " + ex.Message);
                }
            }
        }

        private static ObjectId GetOrCreateStyleId(CodeSetStyleCollection collection, string name)
        {
            if (collection.Contains(name))
                return collection[name];

            return collection.Add(name);
        }

        private static void EnsureFallbackLayer(Database db, Transaction tr)
        {
            EnsureLayer(db, tr, UnmappedLayerName, new LoinColorDefinition { FallbackAci = 7 });
        }

        private static void EnsureLayer(Database db, Transaction tr, string rawLayerName, LoinColorDefinition color)
        {
            string layerName = SanitizeLayerName(rawLayerName);
            if (string.IsNullOrWhiteSpace(layerName))
                layerName = UnmappedLayerName;

            LayerTable layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (layerTable.Has(layerName))
            {
                LayerTableRecord existing = (LayerTableRecord)tr.GetObject(layerTable[layerName], OpenMode.ForWrite);
                existing.Color = BuildAcadColor(color);
                return;
            }

            if (!layerTable.IsWriteEnabled)
                layerTable.UpgradeOpen();

            LayerTableRecord record = new LayerTableRecord
            {
                Name = layerName,
                IsPlottable = true,
                LinetypeObjectId = db.ContinuousLinetype,
                LineWeight = LineWeight.ByLineWeightDefault,
                Color = BuildAcadColor(color)
            };

            layerTable.Add(record);
            tr.AddNewlyCreatedDBObject(record, true);
        }

        private static Color BuildAcadColor(LoinColorDefinition color)
        {
            if (color != null && color.HasRgb)
            {
                return Color.FromRgb(
                    Convert.ToByte(color.Red.Value),
                    Convert.ToByte(color.Green.Value),
                    Convert.ToByte(color.Blue.Value));
            }

            short aci = color?.FallbackAci ?? 7;
            return Color.FromColorIndex(ColorMethod.ByAci, aci);
        }

        private static string BuildStyleName(string typePrefix, string code, LoinStyleDefinition definition)
        {
            string baseName = definition.IsFallback
                ? code
                : FirstNonEmpty(definition.StyleBaseName, definition.LayerName, code);

            return SanitizeStyleName("LOIN - " + typePrefix + " - " + baseName);
        }

        private static string BuildCodeDescription(LoinStyleDefinition definition)
        {
            if (definition.IsFallback)
                return "Codigo sem correspondencia no mapeamento LOIN.";

            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(definition.Discipline))
                parts.Add(definition.Discipline);
            if (!string.IsNullOrWhiteSpace(definition.Element))
                parts.Add(definition.Element);
            if (!string.IsNullOrWhiteSpace(definition.LayerName))
                parts.Add("Layer: " + definition.LayerName);
            if (!string.IsNullOrWhiteSpace(definition.ClassificationCode))
                parts.Add("LOIN: " + definition.ClassificationCode);

            return string.Join(" | ", parts);
        }

        private static string FormatReport(
            CorridorCodeInventory inventory,
            CodeSetBuildSummary summary,
            string mappingPath)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[LOIN] Code Set Style dos corredores concluido.");
            sb.AppendLine("Mapeamento: " + mappingPath);
            sb.AppendLine("Code Set Style: " + CodeSetStyleName);
            sb.AppendLine("Corredores lidos: " + inventory.CorridorCount);
            sb.AppendLine("Corredores atualizados: " + summary.AppliedCorridors);
            sb.AppendLine("Shape codes unicos: " + inventory.ShapeCodes.Count);
            sb.AppendLine("Link codes unicos: " + inventory.LinkCodes.Count);
            sb.AppendLine("Point codes unicos: " + inventory.PointCodes.Count);
            sb.AppendLine("Shape styles criados/atualizados: " + summary.CreatedShapeStyles + "/" + summary.UpdatedShapeStyles);
            sb.AppendLine("Material Area Fill styles criados/atualizados: " + summary.CreatedMaterialAreaFillStyles + "/" + summary.UpdatedMaterialAreaFillStyles);
            sb.AppendLine("Link styles criados/atualizados: " + summary.CreatedLinkStyles + "/" + summary.UpdatedLinkStyles);
            sb.AppendLine("Marker styles criados/atualizados (isolados, nao vao no CodeSet): " + summary.CreatedMarkerStyles + "/" + summary.UpdatedMarkerStyles);
            sb.AppendLine("Itens do Code Set adicionados/atualizados: " + summary.AddedCodeSetItems + "/" + summary.UpdatedCodeSetItems);
            sb.AppendLine("Codigos sem correspondencia no mapeamento: " + summary.UnmappedCodes);

            if (summary.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Avisos:");
                foreach (string warning in summary.Warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(20))
                    sb.AppendLine("  - " + warning);

                if (summary.Warnings.Count > 20)
                    sb.AppendLine("  ... e mais " + (summary.Warnings.Count - 20) + " aviso(s).");
            }

            return sb.ToString();
        }

        private static string FormatAlert(CorridorCodeInventory inventory, CodeSetBuildSummary summary)
        {
            return
                "Code Set Style LOIN criado/aplicado.\n\n" +
                "Corredores atualizados: " + summary.AppliedCorridors + " de " + inventory.CorridorCount + "\n" +
                "Shape codes: " + inventory.ShapeCodes.Count + "\n" +
                "Link codes:  " + inventory.LinkCodes.Count + "\n" +
                "Point codes: " + inventory.PointCodes.Count + "\n" +
                "Sem correspondencia no mapeamento: " + summary.UnmappedCodes + "\n\n" +
                "Detalhes na linha de comando.";
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

        private static string SanitizeStyleName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "LOIN";

            string sanitized = Regex.Replace(name.Trim(), @"\s+", " ");
            char[] invalid = { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', '=' };
            foreach (char c in invalid)
                sanitized = sanitized.Replace(c, '-');

            return sanitized.Length <= 240 ? sanitized : sanitized.Substring(0, 240);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        // Resolve um code do corredor contra o LoinMapeamentoConfig: encontra o item
        // cuja Camada bate exatamente (case-insensitive) e cuja Origem está no
        // conjunto permitido para o tipo (Shape / Link / Point). Match exato é
        // intencional — o usuário definiu a correspondência manualmente na window.
        private sealed class LoinMappingResolver
        {
            private readonly Dictionary<string, List<ResolvedEntry>> _byCamada;

            public LoinMappingResolver(LoinMapeamentoConfig config)
            {
                _byCamada = new Dictionary<string, List<ResolvedEntry>>(StringComparer.OrdinalIgnoreCase);

                Dictionary<string, LoinLinhaDto> linhasPorId =
                    config.TabelaLoin
                          .Where(l => !string.IsNullOrWhiteSpace(l.Id))
                          .ToDictionary(l => l.Id, StringComparer.OrdinalIgnoreCase);

                foreach (LoinItemMapeamentoDto item in config.Mapeamentos)
                {
                    if (string.IsNullOrWhiteSpace(item.Camada) ||
                        string.IsNullOrWhiteSpace(item.LoinLinhaId))
                        continue;

                    if (!linhasPorId.TryGetValue(item.LoinLinhaId, out LoinLinhaDto linha))
                        continue;

                    string key = item.Camada.Trim();
                    if (!_byCamada.TryGetValue(key, out List<ResolvedEntry> bucket))
                    {
                        bucket = new List<ResolvedEntry>();
                        _byCamada[key] = bucket;
                    }
                    bucket.Add(new ResolvedEntry(item.Origem ?? string.Empty, linha));
                }
            }

            public LoinStyleDefinition Resolve(string code, HashSet<string> allowedOrigins)
            {
                if (string.IsNullOrWhiteSpace(code))
                    return BuildFallback(code);

                if (!_byCamada.TryGetValue(code.Trim(), out List<ResolvedEntry> bucket))
                    return BuildFallback(code);

                ResolvedEntry match = bucket.FirstOrDefault(e => allowedOrigins.Contains(e.Origem))
                                      ?? bucket.FirstOrDefault();

                if (match == null)
                    return BuildFallback(code);

                LoinLinhaDto linha = match.Linha;
                LoinColorDefinition color = ParseLoinColor(linha.Cor, linha.Observacao);

                string layerName = FirstNonEmpty(linha.Elemento, linha.Id, code);

                return new LoinStyleDefinition
                {
                    StyleBaseName = FirstNonEmpty(linha.Elemento, linha.Id, code),
                    LayerName = SanitizeLayerName(layerName),
                    Color = color,
                    Discipline = linha.Disciplina ?? string.Empty,
                    Element = linha.Elemento ?? string.Empty,
                    ClassificationCode = linha.Id ?? string.Empty,
                    IsFallback = false
                };
            }

            private static LoinColorDefinition ParseLoinColor(string corDireta, string observacao)
            {
                if (!string.IsNullOrWhiteSpace(corDireta))
                    return LoinWorkbookReader.ParseColor(corDireta);

                // Retrocompat: JSONs antigos guardavam a cor em Observacao como "COR=AMARELO".
                if (!string.IsNullOrWhiteSpace(observacao))
                {
                    foreach (string part in observacao.Split(';'))
                    {
                        string p = part.Trim();
                        if (p.StartsWith("COR=", StringComparison.OrdinalIgnoreCase))
                            return LoinWorkbookReader.ParseColor(p.Substring(4).Trim());
                    }
                }

                return new LoinColorDefinition { FallbackAci = 7 };
            }

            private static LoinStyleDefinition BuildFallback(string code)
            {
                return new LoinStyleDefinition
                {
                    StyleBaseName = code,
                    LayerName = UnmappedLayerName,
                    Color = new LoinColorDefinition { FallbackAci = 7 },
                    IsFallback = true
                };
            }

            private sealed class ResolvedEntry
            {
                public string Origem { get; }
                public LoinLinhaDto Linha { get; }

                public ResolvedEntry(string origem, LoinLinhaDto linha)
                {
                    Origem = origem;
                    Linha = linha;
                }
            }
        }
    }
}
