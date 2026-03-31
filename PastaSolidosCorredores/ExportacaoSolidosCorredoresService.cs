using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using ObjectIdCollection = Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection;

namespace AutomacoesCivil3D
{
    internal sealed class ExportacaoSolidosCorredoresResult
    {
        public int ProcessedCorridors { get; set; }
        public int ExportedSolids { get; set; }
        public int ExportedBodies { get; set; }
        public string DestinationPath { get; set; } = string.Empty;
        public string ReportPath { get; set; } = string.Empty;
        public List<string> Warnings { get; } = new List<string>();

        public int TotalEntities => ExportedSolids + ExportedBodies;

        public string BuildSummary()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Exportação concluída.");
            sb.AppendLine($"Corredores processados: {ProcessedCorridors}");
            sb.AppendLine($"Sólidos exportados: {ExportedSolids}");
            sb.AppendLine($"Bodies exportados: {ExportedBodies}");
            sb.AppendLine($"DWG destino: {DestinationPath}");

            if (!string.IsNullOrWhiteSpace(ReportPath))
            {
                sb.AppendLine($"CSV: {ReportPath}");
            }

            if (Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Avisos:");
                foreach (string warning in Warnings.Take(5))
                {
                    sb.AppendLine("- " + warning);
                }
            }

            return sb.ToString().TrimEnd();
        }
    }

    internal sealed class ExportacaoSolidosCorredoresService
    {
        private static readonly string[] RequiredRoadworksPropertySets =
        {
            "A - Dados do Projeto",
            "B - Informações dos Objetos e Elementos",
            "C - Propriedades Fisicas dos Objetos e Elementos",
            "D - Propriedades Geográficas",
            "COORDENAÇÃO",
            "Corridor Assembly Information",
            "Corridor Shape Information",
            "IfcObject Properties",
            "Pset_Rodoviario",
            "Pset_Pavimentacao",
            "Pset_Terraplenagem"
        };

        private static readonly Dictionary<string, string[]> PropertySetAliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Pset_Rodoviario"] = new[] { "Pset_Rodoviario", "PSET_PETROBRAS_RODOVIARIO" },
            ["Pset_Pavimentacao"] = new[] { "Pset_Pavimentacao", "PSET_PETROBRAS_PAVIMENTACAO" },
            ["Pset_Terraplenagem"] = new[] { "Pset_Terraplenagem", "PSET_PETROBRAS_TERRAPLENAGEM" }
        };

        private static readonly string[] OptionalRoadworksPropertySets =
        {
           
            "Pset_CivilElementCommon",
            "Pset_CourseCommon",
            "Qto_CourseBaseQuantities",
            "Pset_CourseApplicationConditions",
            "Pset_BoundedCourseCommon",
            "Pset_PavementCommon",
            "Qto_PavementBaseQuantities",
            "Pset_PavementSurfaceCommon",
            "Pset_PavementMillingCommon",
            "Pset_TrenchExcavationCommon",
            "Pset_TransitionSectionCommon",
            "Pset_RoadDesignCriteriaCommon",
            "Pset_Superelevation",
            "Pset_Width",
            "Pset_ReferentCommon",
            "Pset_Stationing",
            "Pset_LinearReferencingMethod"
        };

        private static readonly string[] BlockingRoadworksPropertySets =
        {
            "Corridor Shape Information",
            "Corridor Identity",
            "Corridor Model Information"
        };

        private static readonly string[] HiddenRoadworksPropertySets =
        {
            "Corridor Assembly Information"
        };

        private static readonly string[] DisplayRoadworksOptionalPropertySets = RequiredRoadworksPropertySets
            .Except(BlockingRoadworksPropertySets, StringComparer.OrdinalIgnoreCase)
            .Except(HiddenRoadworksPropertySets, StringComparer.OrdinalIgnoreCase)
            .Concat(OptionalRoadworksPropertySets)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        private static readonly string[] ResolvableRoadworksPropertySets = BlockingRoadworksPropertySets
            .Concat(DisplayRoadworksOptionalPropertySets)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        private static readonly string[] RequiredPropertySets =
        {
            "B - Informações dos Objetos e Elementos",
            "C - Propriedades Fisicas dos Objetos e Elementos",
            "Corridor Shape Information"
        };

        private static readonly string[] OptionalPropertySets =
        {
            "A - Dados do Projeto",
            "D - Propriedades Geográficas",
            "COORDENAÇÃO"
        };

        private sealed class PropertySetBinding
        {
            public PropertySetBinding(string name, ObjectId definitionId, PropertySetDefinition definition)
            {
                Name = name;
                DefinitionId = definitionId;
                Definition = definition;
            }

            public string Name { get; }
            public ObjectId DefinitionId { get; }
            public PropertySetDefinition Definition { get; }
        }

        private sealed class ReportRow
        {
            public string Corridor { get; set; } = string.Empty;
            public string EntityType { get; set; } = string.Empty;
            public string Handle { get; set; } = string.Empty;
            public string Layer { get; set; } = string.Empty;
            public Dictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public ExportacaoSolidosCorredoresDialogData BuildDialogData()
        {
            Document doc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Database db = Manager.DocData;

            List<CorridorExportItem> corridors = new List<CorridorExportItem>();
            List<PropertySetStatusInfo> propertySets = new List<PropertySetStatusInfo>();
            string? blockingIssue = null;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureExportPropertySetDefinitions(db, tr, Manager.DocEditor);

                foreach (ObjectId corridorId in civilDb.CorridorCollection)
                {
                    if (!corridorId.IsValid || corridorId.IsNull)
                    {
                        continue;
                    }

                    Corridor? corridor = tr.GetObject(corridorId, OpenMode.ForRead, false) as Corridor;
                    if (corridor == null || corridor.IsReferenceObject)
                    {
                        continue;
                    }

                    int shapeCount = SafeGetCodes(() => corridor.GetShapeCodes()).Length;
                    int linkCount = SafeGetCodes(() => corridor.GetLinkCodes()).Length;
                    corridors.Add(new CorridorExportItem(corridorId, corridor.Name, shapeCount, linkCount, "Local"));
                }

                corridors = corridors.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();

                DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
                propertySets.AddRange(BlockingRoadworksPropertySets.Select(name => new PropertySetStatusInfo(name, true, TryGetPsetDefinitionId(dictionary, tr, name) != ObjectId.Null)));
                propertySets.AddRange(DisplayRoadworksOptionalPropertySets.Select(name => new PropertySetStatusInfo(name, false, TryGetPsetDefinitionId(dictionary, tr, name) != ObjectId.Null)));

                tr.Commit();
            }

            if (corridors.Count == 0)
            {
                blockingIssue = "Nenhum corredor local foi encontrado no desenho ativo.";
            }

            string activeDrawingPath = GetActiveDrawingPath(doc);
            string suggestedDestination = BuildDefaultDestinationPath(activeDrawingPath);
            string suggestedReport = BuildDefaultCsvPath(suggestedDestination);

            return new ExportacaoSolidosCorredoresDialogData(
                corridors,
                propertySets,
                activeDrawingPath,
                suggestedDestination,
                suggestedReport,
                blockingIssue);
        }

        public ExportacaoSolidosCorredoresResult Execute(ExportacaoSolidosCorredoresRequest request)
        {
            ValidateRequest(request);

            Editor editor = Manager.DocEditor;
            Database db = Manager.DocData;

            ExportacaoSolidosCorredoresResult result = new ExportacaoSolidosCorredoresResult
            {
                DestinationPath = Path.GetFullPath(request.DestinationPath),
                ReportPath = request.GenerateReport ? Path.GetFullPath(request.ReportPath) : string.Empty
            };

            ObjectIdCollection exportedIds = new ObjectIdCollection();
            List<ReportRow> reportRows = new List<ReportRow>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureExportPropertySetDefinitions(db, tr, editor);

                DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
                List<PropertySetBinding> availablePropertySets = ResolvePropertySets(dictionary, tr);
                PropertySets propertySets = new PropertySets();

                foreach (ObjectId corridorId in request.CorridorIds)
                {
                    Corridor? corridor = tr.GetObject(corridorId, OpenMode.ForRead, false) as Corridor;
                    if (corridor == null || corridor.IsReferenceObject)
                    {
                        continue;
                    }

                    result.ProcessedCorridors++;

                    string[] includedCodes = BuildIncludedCodes(corridor, request);
                    if (includedCodes.Length == 0)
                    {
                        result.Warnings.Add($"O corredor '{corridor.Name}' não possui códigos compatíveis com as opções selecionadas.");
                        continue;
                    }

                    ExportCorridorSolidsParams parameters = new ExportCorridorSolidsParams
                    {
                        IncludedCodes = includedCodes,
                        ExportLinks = request.ExportLinks,
                        ExportShapes = request.ExportShapes
                    };

                    ObjectIdCollection generated = corridor.ExportSolids(parameters, db);
                    foreach (ObjectId id in generated)
                    {
                        if (!id.IsValid || id.IsNull || id.ObjectClass == null)
                        {
                            continue;
                        }

                        if (id.ObjectClass.Name == "AcDb3dSolid")
                        {
                            Solid3d? solid = tr.GetObject(id, OpenMode.ForWrite, false) as Solid3d;
                            if (solid == null)
                            {
                                continue;
                            }

                            ApplyPropertySets(solid, availablePropertySets);
                            propertySets.PSetSolid(solid, db, tr);
                            exportedIds.Add(solid.ObjectId);
                            result.ExportedSolids++;

                            if (request.GenerateReport)
                            {
                                TryAddReportRow(reportRows, tr, solid, corridor.Name, "3DSOLID", availablePropertySets, result.Warnings);
                            }
                        }
                        else if (id.ObjectClass.Name == "AcDbBody")
                        {
                            Body? body = tr.GetObject(id, OpenMode.ForWrite, false) as Body;
                            if (body == null)
                            {
                                continue;
                            }

                            ApplyPropertySets(body, availablePropertySets);
                            propertySets.PSetBody(body, db, tr);
                            exportedIds.Add(body.ObjectId);
                            result.ExportedBodies++;

                            if (request.GenerateReport)
                            {
                                TryAddReportRow(reportRows, tr, body, corridor.Name, "BODY", availablePropertySets, result.Warnings);
                            }
                        }
                    }
                }

                tr.Commit();
            }

            if (exportedIds.Count == 0)
            {
                result.Warnings.Add("Nenhum sólido ou body foi gerado com a seleção atual.");
                return result;
            }

            if (request.GenerateReport)
            {
                ExportReportToCsv(result.ReportPath, reportRows);
            }

            Civil3DObjectCopier2 copier = new Civil3DObjectCopier2();
            copier.CopyObjectsBetweenDrawings(exportedIds, result.DestinationPath, null, db);

            if (request.RemoveSourceSolidsAfterCopy)
            {
                using (Transaction trErase = db.TransactionManager.StartTransaction())
                {
                    ExclusaoObjetos deletion = new ExclusaoObjetos();
                    deletion.ApagarSolid3d(exportedIds, trErase);
                    trErase.Commit();
                }
            }

            editor.WriteMessage($"\nExportação finalizada. {result.TotalEntities} entidades enviadas para '{result.DestinationPath}'.");
            return result;
        }

        public static string BuildDefaultCsvPath(string destinationPath)
        {
            string safeDestination = string.IsNullOrWhiteSpace(destinationPath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Exportacao_Solidos.dwg")
                : destinationPath;

            string directory = Path.GetDirectoryName(safeDestination) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string fileName = Path.GetFileNameWithoutExtension(safeDestination);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            return Path.Combine(directory, $"{fileName}_RelatorioPSET_{stamp}.csv");
        }

        private static string[] BuildIncludedCodes(Corridor corridor, ExportacaoSolidosCorredoresRequest request)
        {
            IEnumerable<string> codes = Array.Empty<string>();

            if (request.ExportShapes)
            {
                codes = codes.Concat(SafeGetCodes(() => corridor.GetShapeCodes()));
            }

            if (request.ExportLinks)
            {
                codes = codes.Concat(SafeGetCodes(() => corridor.GetLinkCodes()));
            }

            if (request.IncludeInicioTaludeCode)
            {
                codes = codes.Concat(new[] { "INICIO TALUDE" });
            }

            return codes
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void ApplyPropertySets(Entity entity, IEnumerable<PropertySetBinding> propertySets)
        {
            foreach (PropertySetBinding binding in propertySets)
            {
                EnsurePropertySet(entity, binding.DefinitionId);
            }
        }

        private static void EnsureExportPropertySetDefinitions(Database db, Transaction tr, Editor editor)
        {
            IfcPsetFactory.EnsureDefaultPsets(db, tr, editor);
            IfcRoadworksPsetSeeder.EnsureRoadworksPsets(db, tr);
        }

        private static List<PropertySetBinding> ResolvePropertySets(DictionaryPropertySetDefinitions dictionary, Transaction tr)
        {
            List<PropertySetBinding> resolved = new List<PropertySetBinding>();

            foreach (string name in ResolvableRoadworksPropertySets)
            {
                ObjectId definitionId = TryGetPsetDefinitionId(dictionary, tr, name);
                if (definitionId == ObjectId.Null)
                {
                    continue;
                }

                PropertySetDefinition? definition = tr.GetObject(definitionId, OpenMode.ForRead, false) as PropertySetDefinition;
                if (definition != null)
                {
                    resolved.Add(new PropertySetBinding(name, definitionId, definition));
                }
            }

            string[] missingRequired = BlockingRoadworksPropertySets
                .Where(name => resolved.All(r => !string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            if (missingRequired.Length > 0)
            {
                throw new InvalidOperationException("Os Property Sets essenciais não foram encontrados: " + string.Join(", ", missingRequired) + ".");
            }

            return resolved;
        }

        private static void TryAddReportRow(
            ICollection<ReportRow> rows,
            Transaction tr,
            Entity entity,
            string corridorName,
            string entityType,
            IEnumerable<PropertySetBinding> propertySets,
            ICollection<string> warnings)
        {
            try
            {
                rows.Add(BuildReportRow(tr, entity, corridorName, entityType, propertySets, warnings));
            }
            catch (System.Exception ex)
            {
                AddReportWarning(
                    warnings,
                    $"Falha ao gerar uma linha da planilha para a entidade {entityType} ({entity.Handle}). Detalhe: {ex.Message}");
            }
        }

        private static ReportRow BuildReportRow(
            Transaction tr,
            Entity entity,
            string corridorName,
            string entityType,
            IEnumerable<PropertySetBinding> propertySets,
            ICollection<string> warnings)
        {
            ReportRow row = new ReportRow
            {
                Corridor = corridorName,
                EntityType = entityType,
                Handle = entity.Handle.ToString(),
                Layer = entity.Layer
            };

            foreach (PropertySetBinding binding in propertySets)
            {
                PropertySet? propertySet = TryOpenReportPropertySet(tr, entity, binding, warnings);
                if (propertySet == null)
                {
                    continue;
                }

                foreach (string propertyName in GetReportPropertyNames(binding, warnings))
                {
                    if (string.IsNullOrWhiteSpace(propertyName))
                    {
                        continue;
                    }

                    string key = $"{binding.Name}.{propertyName}";
                    if (!row.Values.ContainsKey(key))
                    {
                        row.Values[key] = TryReadPropertyValue(propertySet, entity, propertyName);
                    }
                }
            }

            return row;
        }

        private static PropertySet? TryOpenReportPropertySet(
            Transaction tr,
            Entity entity,
            PropertySetBinding binding,
            ICollection<string> warnings)
        {
            try
            {
                ObjectId propertySetId = PropertyDataServices.GetPropertySet(entity, binding.DefinitionId);
                if (propertySetId == ObjectId.Null)
                {
                    return null;
                }

                return tr.GetObject(propertySetId, OpenMode.ForRead, false) as PropertySet;
            }
            catch (System.Exception ex)
            {
                AddReportWarning(
                    warnings,
                    $"Falha ao acessar o Property Set '{binding.Name}' durante a geração da planilha. Algumas colunas podem ficar vazias. Detalhe: {ex.Message}");
                return null;
            }
        }

        private static List<string> GetReportPropertyNames(PropertySetBinding binding, ICollection<string> warnings)
        {
            List<string> names = new List<string>();

            try
            {
                foreach (PropertyDefinition definition in binding.Definition.Definitions)
                {
                    if (definition == null || string.IsNullOrWhiteSpace(definition.Name))
                    {
                        continue;
                    }

                    if (!names.Any(name => string.Equals(name, definition.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        names.Add(definition.Name);
                    }
                }
            }
            catch (System.Exception ex)
            {
                AddReportWarning(
                    warnings,
                    $"Falha ao listar as propriedades do Property Set '{binding.Name}' durante a geração da planilha. Algumas colunas podem ficar vazias. Detalhe: {ex.Message}");
            }

            return names;
        }

        private static string TryReadPropertyValue(PropertySet propertySet, Entity host, string propertyName)
        {
            try
            {
                int propertyId = propertySet.PropertyNameToId(propertyName);
                object? value;

                try
                {
                    value = propertySet.GetAt(propertyId, host);
                }
                catch
                {
                    value = propertySet.GetAt(propertyId);
                }

                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void AddReportWarning(ICollection<string> warnings, string message)
        {
            if (warnings.Any(existing => string.Equals(existing, message, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            warnings.Add(message);
        }

        private static void ExportReportToCsv(string path, List<ReportRow> rows)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

            List<string> columns = new List<string> { "Corridor", "EntityType", "Handle", "Layer" };
            columns.AddRange(rows.SelectMany(r => r.Values.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.OrdinalIgnoreCase));

            StringBuilder sb = new StringBuilder(1024);
            sb.AppendLine(string.Join(";", columns.Select(EscapeCsv)));

            foreach (ReportRow row in rows)
            {
                List<string> values = new List<string>(columns.Count);
                foreach (string column in columns)
                {
                    values.Add(column switch
                    {
                        "Corridor" => EscapeCsv(row.Corridor),
                        "EntityType" => EscapeCsv(row.EntityType),
                        "Handle" => EscapeCsv(row.Handle),
                        "Layer" => EscapeCsv(row.Layer),
                        _ => EscapeCsv(row.Values.TryGetValue(column, out string? value) ? value : string.Empty)
                    });
                }

                sb.AppendLine(string.Join(";", values));
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static string EscapeCsv(string? value)
        {
            string text = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
            if (text.Contains(';') || text.Contains('"'))
            {
                text = "\"" + text.Replace("\"", "\"\"") + "\"";
            }

            return text;
        }

        private static void EnsurePropertySet(Entity entity, ObjectId propertySetDefinitionId)
        {
            try
            {
                ObjectId currentId = PropertyDataServices.GetPropertySet(entity, propertySetDefinitionId);
                if (currentId == ObjectId.Null)
                {
                    PropertyDataServices.AddPropertySet(entity, propertySetDefinitionId);
                }
            }
            catch
            {
            }
        }

        private static ObjectId TryGetPsetDefinitionId(DictionaryPropertySetDefinitions dictionary, Transaction tr, string name)
        {
            IEnumerable<string> candidateNames = PropertySetAliases.TryGetValue(name, out string[] aliases)
                ? aliases
                : new[] { name };

            foreach (string candidate in candidateNames)
            {
                try
                {
                    Autodesk.Aec.DatabaseServices.Dictionary raw = (Autodesk.Aec.DatabaseServices.Dictionary)dictionary;
                    if (raw.Has(candidate, tr))
                    {
                        return raw.GetAt(candidate);
                    }
                }
                catch
                {
                }
            }

            return ObjectId.Null;
        }

        private static string[] SafeGetCodes(Func<string[]> getter)
        {
            try
            {
                return getter() ?? Array.Empty<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string GetActiveDrawingPath(Document doc)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(doc.Name) && Path.IsPathRooted(doc.Name))
                {
                    return Path.GetFullPath(doc.Name);
                }
            }
            catch
            {
            }

            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, "Desenho_Ativo.dwg");
        }

        private static string BuildDefaultDestinationPath(string activeDrawingPath)
        {
            string directory = Path.GetDirectoryName(activeDrawingPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string fileName = Path.GetFileNameWithoutExtension(activeDrawingPath);
            return Path.Combine(directory, $"{fileName}_SOLIDOS_CORREDORES.dwg");
        }

        private static void ValidateRequest(ExportacaoSolidosCorredoresRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.CorridorIds == null || request.CorridorIds.Count == 0)
            {
                throw new InvalidOperationException("Nenhum corredor foi selecionado para exportação.");
            }

            if (!request.ExportShapes && !request.ExportLinks)
            {
                throw new InvalidOperationException("Selecione ao menos um tipo de geometria para exportação.");
            }

            if (string.IsNullOrWhiteSpace(request.DestinationPath))
            {
                throw new InvalidOperationException("O caminho do DWG de destino não foi informado.");
            }

            if (request.GenerateReport && string.IsNullOrWhiteSpace(request.ReportPath))
            {
                throw new InvalidOperationException("O caminho do relatório CSV não foi informado.");
            }
        }
    }
}
