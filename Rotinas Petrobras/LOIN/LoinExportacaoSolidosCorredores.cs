using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES;
using AutomacoesCivil3D.loin;
using AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Body = Autodesk.AutoCAD.DatabaseServices.Body;
using Color = Autodesk.AutoCAD.Colors.Color;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace AutomacoesCivil3D
{
    // Existem duas classes "PropertySets" no assembly (legacy em
    // PastaSolidosCorredoresNovaInterfaceLogicaAntiga e ativa em
    // PastaSolidosCorredores). O 'PropertySets' nu neste arquivo resolve para a
    // LEGACY por causa do using acima — o ctor PropertySets(CodeNameMappingCatalog)
    // só existe lá. Para chamar a ATIVA usamos LoinSolidValuesBridge (arquivo
    // separado neste mesmo namespace) que isola o acesso de tipos.
    public sealed class LoinExportacaoSolidosCorredoresCommands
    {
        [CommandMethod("EXSOLIDOSCORR_LOIN", CommandFlags.Modal)]
        public void ExportarSolidosCorredoresLoin()
        {
            Executar();
        }

        [CommandMethod("EXSOLIDOSCORR_NI_LOIN", CommandFlags.Modal)]
        public void ExportarSolidosCorredoresNiLoin()
        {
            Executar();
        }

        private static void Executar()
        {
            Editor editor = Manager.DocEditor;

            try
            {
                string loinPath = SelectOpenFile(
                    "Selecionar JSON ou planilha LOIN",
                    "LOIN JSON ou Excel (*.json;*.xlsx)|*.json;*.xlsx|JSON (*.json)|*.json|Planilha Excel (*.xlsx)|*.xlsx");

                if (string.IsNullOrWhiteSpace(loinPath))
                    return;

                LoinConfiguration config = LoadConfig(loinPath, out string generatedJsonPath);

                ExportacaoSolidosCorredoresService baseService = new ExportacaoSolidosCorredoresService();
                ExportacaoSolidosCorredoresDialogData dialogData = baseService.BuildDialogData();

                if (dialogData.Corridors.Count == 0)
                {
                    AcadApp.ShowAlertDialog(dialogData.BlockingIssue);
                    return;
                }

                ExportacaoSolidosCorredoresDialogViewModel viewModel = new ExportacaoSolidosCorredoresDialogViewModel(dialogData);
                ExportacaoSolidosCorredoresWindow window = new ExportacaoSolidosCorredoresWindow(viewModel);

                bool? confirmed = AutoCadWpfDialogHost.ShowModal(window);
                if (confirmed != true)
                {
                    editor.WriteMessage("\nExportacao LOIN cancelada pelo usuario.");
                    return;
                }

                // Tenta carregar o mapeamento explícito gerado pela interface
                // LOIN_MAPEAMENTO (loin_mapeamento.json). Se existir, será usado
                // como override do resolver — codes batendo exatamente vão direto
                // para a linha LOIN apontada, sem cair na heurística fuzzy.
                LoinMapeamentoConfig? mapeamento = TentarCarregarMapeamento(editor);

                LoinExportacaoSolidosCorredoresService service =
                    new LoinExportacaoSolidosCorredoresService(config, generatedJsonPath, mapeamento);

                ExportacaoSolidosCorredoresResult result = service.Execute(window.BuildRequest());
                AcadApp.ShowAlertDialog(result.BuildSummary());
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                editor.WriteMessage("\nErro AutoCAD na exportacao LOIN: " + ex.Message);
                AcadApp.ShowAlertDialog("Erro AutoCAD na exportacao LOIN de solidos:\n" + ex.Message);
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage("\nErro geral na exportacao LOIN: " + ex.Message);
                AcadApp.ShowAlertDialog("Erro na exportacao LOIN de solidos:\n" + ex.Message);
            }
        }

        // Carrega o loin_mapeamento.json (gerado pelo comando LOIN_MAPEAMENTO)
        // — ao lado do DWG ou, em fallback, no AppData do bundle.
        private static LoinMapeamentoConfig? TentarCarregarMapeamento(Editor editor)
        {
            try
            {
                string? drawingPath = Manager.DocCad?.Name;
                string caminho = LoinMapeamentoService.ResolverCaminhoConfig(drawingPath);

                if (!File.Exists(caminho))
                {
                    editor.WriteMessage("\n[LOIN] Mapeamento explícito não encontrado em " + caminho
                                        + " — resolver usará apenas heurística da planilha.");
                    return null;
                }

                LoinMapeamentoConfig mapeamento = LoinMapeamentoService.Carregar(caminho);
                int totalMap = mapeamento.Mapeamentos?.Count ?? 0;
                int totalLin = mapeamento.TabelaLoin?.Count ?? 0;
                editor.WriteMessage($"\n[LOIN] Mapeamento explícito carregado: {totalMap} camada(s) → {totalLin} linha(s) LOIN.");
                return mapeamento;
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage("\n[LOIN] Falha ao carregar mapeamento explícito: " + ex.Message);
                return null;
            }
        }

        private static string SelectOpenFile(string title, string filter)
        {
            using OpenFileDialog dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter,
                CheckFileExists = true,
                Multiselect = false
            };

            string downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");

            if (Directory.Exists(downloads))
                dialog.InitialDirectory = downloads;

            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : string.Empty;
        }

        private static LoinConfiguration LoadConfig(string inputPath, out string generatedJsonPath)
        {
            generatedJsonPath = string.Empty;
            string extension = Path.GetExtension(inputPath).ToLowerInvariant();

            if (extension == ".xlsx")
            {
                LoinConfiguration config = LoinWorkbookReader.Read(inputPath);
                generatedJsonPath = GetDefaultJsonPath(inputPath);
                SaveConfig(config, generatedJsonPath);
                return config;
            }

            if (extension == ".json")
            {
                string json = File.ReadAllText(inputPath, Encoding.UTF8);
                LoinConfiguration config = JsonSerializer.Deserialize<LoinConfiguration>(json, JsonOptions);
                if (config == null)
                    throw new InvalidOperationException("JSON LOIN invalido ou vazio.");

                return config;
            }

            throw new InvalidOperationException("Formato nao suportado. Use .xlsx ou .json.");
        }

        private static void SaveConfig(LoinConfiguration config, string jsonPath)
        {
            string json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(jsonPath, json, Encoding.UTF8);
        }

        private static string GetDefaultJsonPath(string inputPath)
        {
            string folder = Path.GetDirectoryName(inputPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string name = Path.GetFileNameWithoutExtension(inputPath);
            return Path.Combine(folder, name + "_AutomacoesCivil3D_LOIN.json");
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    public sealed class LoinExportacaoSolidosCorredoresService
    {
        private const string FallbackLayerName = "Pset_SEM_MAPEAMENTO_SOLIDOS_CORREDOR";

        private readonly LoinConfiguration _config;
        private readonly string _generatedJsonPath;
        private readonly LoinSolidElementResolver _resolver;

        // Lookup das linhas LOIN do mapeamento por (SourceSheet, SourceRow),
        // que é a mesma chave usada para resolver LoinResolvedElement. Permite
        // recuperar a LoinLinhaDto (com Cor/IfcClass/PredefinedType tipados)
        // a partir do resolved sem refazer a heurística.
        private readonly Dictionary<(string sheet, int row), LoinLinhaDto> _linhaPorOrigem;

        // Dados de projeto (Pset_A): preenchidos via janela LOIN_DADOS_PROJETO
        // (LOIN_PROJ) e persistidos em loin_projeto.json ao lado do DWG.
        private LoinProjetoDto _loinProjeto = new LoinProjetoDto();

        public LoinExportacaoSolidosCorredoresService(LoinConfiguration config, string generatedJsonPath)
            : this(config, generatedJsonPath, null)
        {
        }

        // Overload que aceita o mapeamento explícito gerado pela interface
        // LOIN_MAPEAMENTO. Quando informado, codes batendo exatamente com
        // alguma entrada Mapeamentos[].Camada resolvem direto para a linha
        // LOIN apontada — sem cair na heurística fuzzy.
        // Internal pois LoinMapeamentoConfig é internal — uso interno ao assembly.
        internal LoinExportacaoSolidosCorredoresService(
            LoinConfiguration config,
            string generatedJsonPath,
            LoinMapeamentoConfig? mapeamento)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _generatedJsonPath = generatedJsonPath ?? string.Empty;
            _resolver = new LoinSolidElementResolver(config, mapeamento);
            _linhaPorOrigem = BuildLinhaPorOrigem(mapeamento);
        }

        private static Dictionary<(string, int), LoinLinhaDto> BuildLinhaPorOrigem(LoinMapeamentoConfig? m)
        {
            Dictionary<(string, int), LoinLinhaDto> lookup =
                new Dictionary<(string, int), LoinLinhaDto>(TupleSheetRowComparer.Instance);

            if (m?.TabelaLoin == null) return lookup;
            foreach (LoinLinhaDto linha in m.TabelaLoin)
            {
                if (string.IsNullOrWhiteSpace(linha.SourceSheet) || linha.SourceRow <= 0) continue;
                lookup[(linha.SourceSheet, linha.SourceRow)] = linha;
            }
            return lookup;
        }

        private sealed class TupleSheetRowComparer : IEqualityComparer<(string, int)>
        {
            public static readonly TupleSheetRowComparer Instance = new TupleSheetRowComparer();
            public bool Equals((string, int) x, (string, int) y) =>
                string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) && x.Item2 == y.Item2;
            public int GetHashCode((string, int) obj) =>
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1 ?? string.Empty) ^ obj.Item2;
        }

        public ExportacaoSolidosCorredoresResult Execute(ExportacaoSolidosCorredoresRequest request)
        {
            ExportacaoSolidosCorredoresService.ValidateRequest(request);

            Editor editor = Manager.DocEditor;
            Database db = Manager.DocData;
            CodeNameMappingCatalog codeNameCatalog = TrySynchronizeCatalog(out string catalogWarning);

            // Carrega dados de projeto (Pset_A). Se não existir, abre vazio —
            // os campos do Pset_A ficam em branco e o usuário deve rodar LOIN_PROJ
            // para preencher.
            try
            {
                string? drawingPath = Manager.DocCad?.Name;
                string projetoPath = LoinProjetoService.ResolverCaminhoConfig(drawingPath);
                _loinProjeto = LoinProjetoService.Carregar(projetoPath);
            }
            catch
            {
                _loinProjeto = new LoinProjetoDto();
            }

            ExportacaoSolidosCorredoresResult result = new ExportacaoSolidosCorredoresResult
            {
                DestinationPath = Path.GetFullPath(request.DestinationPath),
                ReportPath = request.GenerateReport ? Path.GetFullPath(request.ReportPath) : string.Empty
            };

            string destinationDirectory = Path.GetDirectoryName(result.DestinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            if (!string.IsNullOrWhiteSpace(catalogWarning))
                result.Warnings.Add(catalogWarning);

            if (!string.IsNullOrWhiteSpace(_generatedJsonPath))
                result.Warnings.Add("JSON LOIN gerado: " + _generatedJsonPath);

            ObjectIdCollection exportedIds = new ObjectIdCollection();
            List<ExportacaoSolidosCorredoresService.ReportRow> rows = new List<ExportacaoSolidosCorredoresService.ReportRow>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LoinCivil3DApplier.ResourceSummary resourceSummary =
                    LoinCivil3DApplier.EnsureResources(db, tr, editor, _config);

                EnsureFallbackLayer(db, tr);

                DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
                List<ExportacaoSolidosCorredoresService.PropertySetBinding> legacyBindings =
                    ExportacaoSolidosCorredoresService.ResolvePropertySets(dictionary, tr);

                List<ExportacaoSolidosCorredoresService.PropertySetBinding> loinBindings =
                    ResolveLoinPropertySets(dictionary, tr);

                PropertySets legacyValueCollector = new PropertySets(codeNameCatalog);

                // Resolve UMA vez o ObjectId do PSet "Propriedades Físicas" pra reusar em todas as entidades.
                // Se não existir no desenho origem (raro — LoinCivil3DApplier.EnsureResources cria), psetCId fica Null
                // e o passo de auto-preenchimento será pulado sem quebrar o pipeline.
                string psetCName = PropertySets2.ResolverNomePsetC(dictionary, tr, db, editor);
                ObjectId psetCId = (psetCName != null) ? dictionary.GetAt(psetCName) : ObjectId.Null;
                if (psetCId == ObjectId.Null)
                    result.Warnings.Add("PSet 'Propriedades Físicas' não encontrado no desenho — auto-preenchimento de Pset_C foi pulado.");

                foreach (ObjectId corridorId in request.CorridorIds)
                {
                    DBObject obj = tr.GetObject(corridorId, OpenMode.ForRead, false);
                    Corridor corridor = obj as Corridor;
                    if (corridor == null || corridor.IsReferenceObject)
                        continue;

                    result.ProcessedCorridors++;
                    string[] includedCodes = ExportacaoSolidosCorredoresService.BuildIncludedCodes(corridor, request);
                    if (includedCodes.Length == 0)
                    {
                        result.Warnings.Add("O corredor '" + corridor.Name + "' nao possui codigos compativeis com as opcoes selecionadas.");
                        continue;
                    }

                    ExportCorridorSolidsParams exportParams = new ExportCorridorSolidsParams
                    {
                        IncludedCodes = includedCodes,
                        ExportLinks = request.ExportLinks,
                        ExportShapes = request.ExportShapes
                    };

                    foreach (ObjectId exportedId in corridor.ExportSolids(exportParams, db))
                    {
                        if (!exportedId.IsValid || exportedId.IsNull || exportedId.ObjectClass == null)
                            continue;

                        Entity entity = tr.GetObject(exportedId, OpenMode.ForWrite, false) as Entity;
                        if (entity == null)
                            continue;

                        bool processed = ProcessExportedEntity(
                            entity,
                            db,
                            tr,
                            legacyBindings,
                            loinBindings,
                            legacyValueCollector,
                            corridor.Name,
                            dictionary,
                            psetCId,
                            editor,
                            result.Warnings);

                        if (!processed)
                            continue;

                        exportedIds.Add(entity.ObjectId);

                        if (exportedId.ObjectClass.Name == "AcDb3dSolid")
                            result.ExportedSolids++;
                        else if (exportedId.ObjectClass.Name == "AcDbBody")
                            result.ExportedBodies++;

                        if (request.GenerateReport)
                        {
                            ExportacaoSolidosCorredoresService.TryAddReportRow(
                                rows,
                                tr,
                                entity,
                                corridor.Name,
                                exportedId.ObjectClass.Name == "AcDbBody" ? "BODY" : "3DSOLID",
                                loinBindings.Count > 0 ? loinBindings : legacyBindings,
                                result.Warnings);
                        }
                    }
                }

                result.Warnings.Add(
                    "Recursos LOIN: layers criados/atualizados " +
                    resourceSummary.CreatedLayers + "/" + resourceSummary.UpdatedLayers +
                    ", PSets criados/atualizados " +
                    resourceSummary.CreatedPsets + "/" + resourceSummary.UpdatedPsets + ".");

                tr.Commit();
            }

            if (exportedIds.Count == 0)
            {
                result.Warnings.Add("Nenhum solido ou body foi gerado com a selecao atual.");
                return result;
            }

            if (request.GenerateReport)
                ExportacaoSolidosCorredoresService.ExportReportToCsv(result.ReportPath, rows);

            new Civil3DObjectCopier2().CopyObjectsBetweenDrawings(exportedIds, result.DestinationPath, null, db);
            TryEnsureDestinationLoinPsets(result, request);

            // Limpa os PSets legacy SÓ no DWG destino — o DWG origem continua intocado
            // (o PSetSolid legacy precisa rodar lá pra alimentar o Pset_C novo do LOIN).
            StripLegacyPsetsFromDestination(result.DestinationPath, result.Warnings);

            // Aplica os Psets LOIN per-disciplina nas entidades já copiadas para o
            // DWG destino. ApplyToSelection sabe filtrar por disciplina (sólido de
            // PAV só recebe Pset_B_PAV/C_PAV/Requisitos_PAV; não recebe os de outras
            // disciplinas mesmo se a definição existir no destino). Sem este passo,
            // os sólidos ficariam só com os Psets que vieram anexados na cópia —
            // o que pode estar incompleto se a copy não trouxe todos os PSet bindings.
            ApplyNewLoinPsetsInDestination(result.DestinationPath, result.Warnings);

            if (request.RemoveSourceSolidsAfterCopy)
            {
                using Transaction deleteTr = db.TransactionManager.StartTransaction();
                new ExclusaoObjetos().ApagarSolid3d(exportedIds, deleteTr);
                deleteTr.Commit();
            }

            editor.WriteMessage(
                "\nExportacao LOIN finalizada. " +
                result.TotalEntities +
                " entidades enviadas para '" +
                result.DestinationPath +
                "'.");

            return result;
        }

        private bool ProcessExportedEntity(
            Entity entity,
            Database db,
            Transaction tr,
            IReadOnlyList<ExportacaoSolidosCorredoresService.PropertySetBinding> legacyBindings,
            IReadOnlyList<ExportacaoSolidosCorredoresService.PropertySetBinding> loinBindings,
            PropertySets legacyValueCollector,
            string corridorName,
            DictionaryPropertySetDefinitions dictionary,
            ObjectId psetCId,
            Editor editor,
            ICollection<string> warnings)
        {
            ExportacaoSolidosCorredoresService.ApplyPropertySets(entity, legacyBindings);

            if (entity is Solid3d solid)
                legacyValueCollector.PSetSolid(solid, db, tr);
            else if (entity is Body body)
                legacyValueCollector.PSetBody(body, db, tr);
            else
                return false;

            LegacySolidMetadata legacyMetadata = LegacySolidMetadata.Read(entity, tr, legacyBindings, warnings);
            string codeName = FirstNonEmpty(legacyMetadata.CodeName, entity.Layer, corridorName);

            LoinResolvedElement resolved = _resolver.Resolve(codeName);
            ApplyLoinPackage(entity, tr, loinBindings, resolved, legacyMetadata, warnings);

            // Auto-preenchimento do Pset_C — substitui a necessidade de rodar AplicarPsetTodos
            // manualmente após a exportação. Lê PSets nativos (Corridor Shape/Identity/Model
            // Information), varre subassembly e escreve Largura/Altura/Inclinação/Volume/Comprimento/Area.
            if (psetCId != ObjectId.Null)
            {
                /*try
                {
                    PropertySets2.ProcessarEntidadeCorredor(
                        entity, psetCId, db, editor, tr, dictionary, verbose: false);
                }
                catch (System.Exception ex)
                {
                    warnings.Add($"Auto-preenchimento Pset_C falhou para handle {entity.Handle}: {ex.Message}");
                }*/
            }

            return true;
        }

        private void ApplyLoinPackage(
            Entity entity,
            Transaction tr,
            IReadOnlyList<ExportacaoSolidosCorredoresService.PropertySetBinding> loinBindings,
            LoinResolvedElement resolved,
            LegacySolidMetadata legacyMetadata,
            ICollection<string> warnings)
        {
            foreach (ExportacaoSolidosCorredoresService.PropertySetBinding binding in loinBindings)
            {
                bool ok = ExportacaoSolidosCorredoresService.EnsurePropertySet(entity, binding.DefinitionId);
                if (!ok)
                {
                    // Não-fatal: PSet pode estar com AppliesToFilter restrito demais
                    // (não inclui o tipo desta entidade — ex.: AcDbBody). Reporta
                    // no relatório para o user investigar — não bloqueia a exportação.
                    warnings.Add(
                        "PSet '" + binding.Name + "' não pôde ser anexado em " +
                        ((object)entity).GetType().Name + " (Handle " + entity.Handle +
                        "). Verifique o AppliesToFilter desse Pset no template.");
                }
            }

            string layerName = SanitizeLayerName(resolved.LayerName);
            if (string.IsNullOrWhiteSpace(layerName))
                layerName = FallbackLayerName;

            try
            {
                entity.Layer = layerName;
            }
            catch (System.Exception ex)
            {
                warnings.Add("Falha ao aplicar layer LOIN na entidade " + entity.Handle + ": " + ex.Message);
            }

            Dictionary<string, string> metadataValues = BuildLoinMetadataValues(resolved, legacyMetadata, layerName);
            // requirementValues / ifcValues não são mais escritos — ABCD são suficientes.
            // BuildLoin*Values são mantidos invocados por enquanto caso outros consumers leiam.
            _ = BuildLoinRequirementValues(resolved);
            _ = BuildLoinIfcValues(resolved, legacyMetadata, layerName, entity);

            // ABCD unificado — sem split per-disciplina, sem Pset_Requisitos, sem IfcObject.
            SetPsetValues(entity, tr, loinBindings, LoinCivil3DApplier.PsetDName, metadataValues);
            SetPsetValues(entity, tr, loinBindings, LoinCivil3DApplier.PsetCName, legacyMetadata.PhysicalValues);
            SetPsetValues(entity, tr, loinBindings, LoinCivil3DApplier.PsetBName, legacyMetadata.ElementValues);
            SetPsetValues(entity, tr, loinBindings, LoinCivil3DApplier.PsetBName, resolved.RowValues);

            // Pset_A / Pset_B / Pset_C com os nomes de campo do template Petrobras
            // (AUTOR, EIXO, KM, ALTURA, ÁREA, etc). Os valores fisicos/geometricos
            // reusam o pipeline ja calculado em PropertySets.ExtractValues.
            try
            {
                LoinSolidValues v = LoinSolidValuesBridge.Extract(entity, Manager.DocData, tr);
                LoinLinhaDto linha = ResolveLinhaLoin(resolved);

                WriteLoinPsetA(entity, tr, loinBindings, _loinProjeto);
                WriteLoinPsetB(entity, tr, loinBindings, v, resolved, linha);
                WriteLoinPsetC(entity, tr, loinBindings, v, resolved, linha);
            }
            catch (System.Exception ex)
            {
                warnings.Add("Falha ao preencher Pset_A/B/C LOIN na entidade " + entity.Handle + ": " + ex.Message);
            }

            IfcResolvedMetadata metadata = new IfcResolvedMetadata
            {
                IfcClass = resolved.IfcClass,
                PredefinedType = resolved.PredefinedType,
                ObjectType = resolved.ElementName,
                Name = FirstNonEmpty(resolved.ElementName, legacyMetadata.CodeName),
                Tag = FirstNonEmpty(legacyMetadata.CodeName, entity.Handle.ToString()),
                Description = FirstNonEmpty(resolved.ElementName, legacyMetadata.CodeName),
                Layer = layerName,
                System = resolved.Discipline,
                Subsystem = legacyMetadata.SubassemblyName
            };

            try
            {
                IfcAplicarMapeamentoJson.WriteMetadataToObject(entity, metadata, tr);
            }
            catch (System.Exception ex)
            {
                warnings.Add("Falha ao gravar metadados IFC LOIN na entidade " + entity.Handle + ": " + ex.Message);
            }
        }

        private static Dictionary<string, string> BuildLoinMetadataValues(
            LoinResolvedElement resolved,
            LegacySolidMetadata legacy,
            string layerName)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DISCIPLINA"] = resolved.Discipline,
                ["ELEMENTO"] = FirstNonEmpty(resolved.ElementName, legacy.CodeName),
                ["IFC_CLASS"] = resolved.IfcClass,
                ["PREDEFINED_TYPE"] = resolved.PredefinedType,
                ["CLASSIFICATION_CODE"] = resolved.ClassificationCode,
                ["LAYER"] = layerName,
                ["COLOR_RAW"] = resolved.Color?.Raw ?? string.Empty,
                ["COLOR_RGB"] = FormatRgb(resolved.Color),
                ["Pset_SOURCE_SHEET"] = resolved.SourceSheet,
                ["Pset_SOURCE_ROW"] = resolved.SourceRow > 0 ? resolved.SourceRow.ToString(CultureInfo.InvariantCulture) : string.Empty
            };
        }

        private static Dictionary<string, string> BuildLoinRequirementValues(LoinResolvedElement resolved)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Pset_DISCIPLINA"] = resolved.Discipline,
                ["Pset_ELEMENTO"] = resolved.ElementName,
                ["Pset_CAMPOS_B_OBRIGATORIOS"] = string.Join("; ", resolved.RequiredElementProperties),
                ["Pset_CAMPOS_C_OBRIGATORIOS"] = string.Join("; ", resolved.RequiredPhysicalProperties),
                ["Pset_CAMPOS_NA"] = string.Join("; ", resolved.NotApplicableProperties),
                ["Pset_ORIGEM"] = resolved.SourceRow > 0 ? resolved.SourceSheet + ":" + resolved.SourceRow : string.Empty
            };
        }

        private static Dictionary<string, string> BuildLoinIfcValues(
            LoinResolvedElement resolved,
            LegacySolidMetadata legacy,
            string layerName,
            Entity entity)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["IFC::IfcExportAs"] = resolved.IfcClass,
                ["IFC::PredefinedType"] = resolved.PredefinedType,
                ["IFC::IfcPredefinedType"] = resolved.PredefinedType,
                ["IFC::ObjectType"] = FirstNonEmpty(resolved.ElementName, legacy.CodeName),
                ["IFC::Layer"] = layerName,
                ["IFC::ClassificationCode"] = resolved.ClassificationCode,
                ["IfcGlobalId"] = entity.Handle.ToString()
            };
        }

        // ------------------------------ Pset_A / B / C ------------------------------

        // Pset_A é o mesmo em todos os sólidos do desenho — vem do loin_projeto.json
        // preenchido na janela LOIN_DADOS_PROJETO (LOIN_PROJ).
        private static void WriteLoinPsetA(
            Entity entity,
            Transaction tr,
            IReadOnlyList<ExportacaoSolidosCorredoresService.PropertySetBinding> loinBindings,
            LoinProjetoDto projeto)
        {
            if (projeto == null) return;

            // Variantes do nome do campo: com/sem acento, espaço/underscore.
            Dictionary<string, string> v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AUTOR"]                = projeto.Autor,
                ["CONTRATANTE"]          = projeto.Contratante,
                ["DATA"]                 = projeto.Data,
                ["DISCIPLINA"]           = projeto.Disciplina,
                ["FASE DE PROJETO"]      = projeto.FaseProjeto,
                ["FASE_DE_PROJETO"]      = projeto.FaseProjeto,
                ["FASE PROJETO"]         = projeto.FaseProjeto,
                ["LOCALIZAÇÃO"]          = projeto.Localizacao,
                ["LOCALIZACAO"]          = projeto.Localizacao,
                ["NOME DO PROJETO"]      = projeto.NomeProjeto,
                ["NOME_DO_PROJETO"]      = projeto.NomeProjeto,
                ["NOME PROJETO"]         = string.IsNullOrWhiteSpace(projeto.NomeProjetoAlt)
                                                ? projeto.NomeProjeto : projeto.NomeProjetoAlt,
                ["NOMEPROJETO"]          = projeto.NomeProjeto,
                ["SISTEMA DE COORDENADA"] = projeto.SistemaCoordenada,
                ["SISTEMA_DE_COORDENADA"] = projeto.SistemaCoordenada,
                ["SISTEMA COORDENADA"]   = projeto.SistemaCoordenada
            };

            SetPsetValues(entity, tr, loinBindings, LoinCivil3DApplier.PsetAName, v);
        }

        // Pset_B é por elemento. As coordenadas X/Y ficam vazias para pavimento
        // (pedido explicito do usuario); os demais campos derivam do corredor +
        // linha LOIN mapeada.
        private static void WriteLoinPsetB(
            Entity entity,
            Transaction tr,
            IReadOnlyList<ExportacaoSolidosCorredoresService.PropertySetBinding> loinBindings,
            LoinSolidValues v,
            LoinResolvedElement resolved,
            LoinLinhaDto linhaLoin)
        {
            string kmInicial = FormatStationDnit(v.StartStation);
            string kmFinal   = FormatStationDnit(v.EndStation);
            string km        = !string.IsNullOrWhiteSpace(kmInicial) ? kmInicial : kmFinal;

            string nomeElemento = FirstNonEmpty(
                linhaLoin?.Elemento,
                resolved?.ElementName,
                v.SubassemblyName,
                v.CodeName);

            string tipo = FirstNonEmpty(
                linhaLoin?.PredefinedType,
                resolved?.PredefinedType);

            string subCategoria = FirstNonEmpty(
                linhaLoin?.Disciplina,
                resolved?.Discipline,
                v.Disciplina);

            string material = FirstNonEmpty(v.Material, linhaLoin?.Elemento);

            string situacao = FirstNonEmpty(v.Situacao);

            Dictionary<string, string> vals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // PAV não preenche coordenadas (conforme decisão do projeto).
                ["COORDENADA X"]      = string.Empty,
                ["COORDENADA Y"]      = string.Empty,

                ["EIXO"]              = v.CorridorName,
                ["KM"]                = km,
                ["KM INICIAL"]        = kmInicial,
                ["KM_INICIAL"]        = kmInicial,
                ["KM FINAL"]          = kmFinal,
                ["KM_FINAL"]          = kmFinal,
                ["LADO DA PISTA"]     = v.Lado,
                ["LADO_DA_PISTA"]     = v.Lado,
                ["MATERIAL"]          = material,
                ["NOME DO ELEMENTO"]  = nomeElemento,
                ["NOME_DO_ELEMENTO"]  = nomeElemento,
                ["SITUAÇÃO"]          = situacao,
                ["SITUACAO"]          = situacao,
                ["SUB-CATEGORIA"]     = subCategoria,
                ["SUB_CATEGORIA"]     = subCategoria,
                ["SUBCATEGORIA"]      = subCategoria,
                ["TIPO"]              = tipo
            };

            // Per-disciplina: Pset_B_<CODE> a partir de resolved.Discipline.
            // Pset_B unificado — ABCD bastam.
            SetPsetValues(entity, tr, loinBindings, LoinCivil3DApplier.PsetBName, vals);
        }

        // Pset_C novo — recebe os valores já calculados pelo PSetSolid no Pset_C
        // legacy (lido pelo bridge) + Cotas do bbox + COR do LoinLinha mapeado.
        // Campos sem fonte (CADENCIA, JUSANTE, MONTANTE, ESPESSURA, LAMINAS,
        // PROFUNDIDADE, QUANTIDADE, TAXA DE AÇO) ficam vazios por design — são
        // específicos de drenagem/estruturas e não derivam de sólido de pavimento.
        private static void WriteLoinPsetC(
            Entity entity,
            Transaction tr,
            IReadOnlyList<ExportacaoSolidosCorredoresService.PropertySetBinding> loinBindings,
            LoinSolidValues v,
            LoinResolvedElement resolved,
            LoinLinhaDto linhaLoin)
        {
            string cor = NormalizarRgb(linhaLoin?.Cor, resolved?.Color);

            Dictionary<string, string> vals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Vem do Pset_C legacy (calculado por PSetSolid → subassembly)
                ["ALTURA"]         = Fmt(v.Altura),
                ["LARGURA"]        = Fmt(v.Width),
                ["COMPRIMENTO"]    = Fmt(v.LengthMeters),
                ["ÁREA"]           = Fmt(v.Area),
                ["AREA"]           = Fmt(v.Area),
                ["VOLUME"]         = Fmt(v.Volume),
                ["INCLINAÇÃO"]     = Fmt(v.Slope),
                ["INCLINACAO"]     = Fmt(v.Slope),
                ["DIÂMETRO"]       = Fmt(v.Diametro),
                ["DIAMETRO"]       = Fmt(v.Diametro),
                ["COTA DE FUND"]   = Fmt(v.CotaFundo),
                ["COTA_DE_FUND"]   = Fmt(v.CotaFundo),
                ["COTA DE FUNDO"]  = Fmt(v.CotaFundo),
                ["COTA DE TOPO"]   = Fmt(v.CotaTopo),
                ["COTA_DE_TOPO"]   = Fmt(v.CotaTopo),

                // Vem do LoinLinha (mapeamento manual)
                ["COR"]            = cor,

                // ESPESSURA é sinônimo de Altura para pavimento (mesma medida)
                ["ESPESSURA"]      = Fmt(v.Altura),

                // Sem fonte automática para pavimento — atributos de drenagem/estrutura
                ["CADENCIA"]       = string.Empty,
                ["COTA JUSANTE"]   = string.Empty,
                ["COTA_JUSANTE"]   = string.Empty,
                ["COTA MONTANTE"]  = string.Empty,
                ["COTA_MONTANTE"]  = string.Empty,
                ["LAMINAS"]        = string.Empty,
                ["PROFUNDIDADE"]   = string.Empty,
                ["QUANTIDADE"]     = string.Empty,
                ["TAXA DE AÇO"]    = string.Empty,
                ["TAXA_DE_AÇO"]    = string.Empty,
                ["TAXA DE ACO"]    = string.Empty,
                ["TAXA_DE_ACO"]    = string.Empty
            };

            // Per-disciplina: Pset_C_<CODE> a partir de resolved.Discipline.
            // Pset_C unificado — ABCD bastam.
            SetPsetValues(entity, tr, loinBindings, LoinCivil3DApplier.PsetCName, vals);
        }

        // Resolve a LoinLinhaDto correspondente ao LoinResolvedElement via
        // (SourceSheet, SourceRow) — chave compartilhada entre os dois sistemas.
        private LoinLinhaDto ResolveLinhaLoin(LoinResolvedElement resolved)
        {
            if (resolved == null) return null;
            if (string.IsNullOrWhiteSpace(resolved.SourceSheet) || resolved.SourceRow <= 0)
                return null;

            return _linhaPorOrigem.TryGetValue((resolved.SourceSheet, resolved.SourceRow), out LoinLinhaDto linha)
                ? linha
                : null;
        }

        private static string Fmt(double v) => v.ToString("F2", CultureInfo.InvariantCulture);

        // KM estilo DNIT: "3+875.50" (km inteiro + metros restantes).
        private static string FormatStationDnit(double stationMeters)
        {
            if (double.IsNaN(stationMeters) || stationMeters < 0.0)
                return string.Empty;

            int km = (int)Math.Floor(stationMeters / 1000.0);
            double resto = stationMeters - km * 1000.0;
            return km.ToString(CultureInfo.InvariantCulture) + "+" + resto.ToString("F2", CultureInfo.InvariantCulture);
        }

        // Normaliza a cor para "R,G,B". Prefere o que o usuario digitou na
        // LoinLinha.Cor (parser comum). Fallback: cor do LoinResolvedElement.
        private static string NormalizarRgb(string corLoinLinha, LoinColorDefinition corResolved)
        {
            // 1) Cor digitada pelo user na linha LOIN (RGB / nome / hexa / ACI)
            if (!string.IsNullOrWhiteSpace(corLoinLinha))
            {
                LoinColorDefinition parsed = LoinWorkbookReader.ParseColor(corLoinLinha);
                if (parsed.HasRgb)
                    return parsed.Red.Value + "," + parsed.Green.Value + "," + parsed.Blue.Value;
            }

            // 2) Fallback: cor já resolvida no LoinResolvedElement (vem da config)
            if (corResolved != null && corResolved.HasRgb)
                return corResolved.Red.Value + "," + corResolved.Green.Value + "," + corResolved.Blue.Value;

            return string.Empty;
        }

        // ------------------------------ infra ------------------------------

        private static void SetPsetValues(
            Entity entity,
            Transaction tr,
            IReadOnlyList<ExportacaoSolidosCorredoresService.PropertySetBinding> bindings,
            string psetName,
            IDictionary<string, string> values)
        {
            if (values == null || values.Count == 0)
                return;

            ExportacaoSolidosCorredoresService.PropertySetBinding binding = bindings.FirstOrDefault(
                b => string.Equals(b.Name, psetName, StringComparison.OrdinalIgnoreCase));

            if (binding == null)
                return;

            PropertySet? pset = TryOpenAttachedPropertySetForWrite(entity, tr, binding);
            if (pset == null)
                return;

            foreach (KeyValuePair<string, string> value in values)
            {
                try
                {
                    int id = pset.PropertyNameToId(value.Key);
                    if (id != -1)
                        pset.SetAt(id, value.Value ?? string.Empty);
                }
                catch
                {
                }
            }
        }

        private static PropertySet? TryOpenAttachedPropertySetForWrite(
            Entity entity,
            Transaction tr,
            ExportacaoSolidosCorredoresService.PropertySetBinding binding)
        {
            try
            {
                ObjectId propertySetId = PropertyDataServices.GetPropertySet((DBObject)(object)entity, binding.DefinitionId);
                if (propertySetId == ObjectId.Null || !propertySetId.IsValid)
                    return null;

                DBObject dbObject = tr.GetObject(propertySetId, OpenMode.ForWrite, false);
                return dbObject as PropertySet;
            }
            catch
            {
                return null;
            }
        }

        private List<ExportacaoSolidosCorredoresService.PropertySetBinding> ResolveLoinPropertySets(
            DictionaryPropertySetDefinitions dictionary,
            Transaction tr)
        {
            List<ExportacaoSolidosCorredoresService.PropertySetBinding> resolved =
                new List<ExportacaoSolidosCorredoresService.PropertySetBinding>();

            foreach (string name in (_config.PropertySetDefinitions ?? new List<LoinPsetDefinition>())
                         .Select(p => p.Name)
                         .Where(n => !string.IsNullOrWhiteSpace(n))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ObjectId id = ExportacaoSolidosCorredoresService.TryGetPsetDefinitionId(dictionary, tr, name);
                if (id == ObjectId.Null)
                    continue;

                PropertySetDefinition definition = tr.GetObject(id, OpenMode.ForRead, false) as PropertySetDefinition;
                if (definition != null)
                    resolved.Add(new ExportacaoSolidosCorredoresService.PropertySetBinding(name, id, definition));
            }

            return resolved;
        }

        private void TryEnsureDestinationLoinPsets(ExportacaoSolidosCorredoresResult result, ExportacaoSolidosCorredoresRequest request)
        {
            try
            {
                IEnumerable<string> names = (_config.PropertySetDefinitions ?? new List<LoinPsetDefinition>())
                    .Select(p => p.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n));

                ExportacaoSolidosCorredoresService.EnsureDestinationPropertySetDefinitions(result.DestinationPath, names);
            }
            catch (System.Exception ex)
            {
                result.Warnings.Add("Nao foi possivel sincronizar os PSets LOIN no DWG destino: " + ex.Message);
            }
        }

        // Etapa final do fluxo de exportação: roda LoinCivil3DApplier.ApplyToSelection
        // no DWG destino para anexar + preencher os Psets per-disciplina nas entidades
        // já copiadas. Equivalente a abrir o destino e rodar manualmente LOIN_APLICAR_SELECAO
        // com a mesma planilha LOIN — agora automatizado no final do export.
        //
        // Filtro de elegibilidade: itera ModelSpace pegando Solid3d/Body/Surface.
        // ApplyToSelection já filtra internamente quem não tem layer LOIN
        // (elementsByLayer.TryGetValue) — entidades estranhas não pegam Pset.
        // O filtro per-disciplina de AttachConfiguredPsets garante que cada sólido
        // só recebe os Psets da SUA disciplina (Pset_B_PAV não vai em sólido de TER).
        private void ApplyNewLoinPsetsInDestination(string destinationPath, ICollection<string> warnings)
        {
            Document destDoc = null;
            try
            {
                destDoc = ExportacaoSolidosCorredoresService.EnsureDocumentOpen(destinationPath);
                if (destDoc == null)
                {
                    warnings.Add("Nao foi possivel abrir o DWG destino para aplicar Psets LOIN.");
                    return;
                }

                Database destDb = destDoc.Database;
                Editor destEd = destDoc.Editor;

                using (DocumentLock docLock = destDoc.LockDocument())
                using (Transaction tr = destDb.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(destDb.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    List<ObjectId> ids = new List<ObjectId>();
                    foreach (ObjectId id in ms)
                    {
                        string cls = id.ObjectClass.Name;
                        if (cls == "AcDb3dSolid" || cls == "AcDbBody" || cls == "AcDbSurface")
                            ids.Add(id);
                    }

                    if (ids.Count == 0)
                    {
                        warnings.Add("Nenhuma entidade elegivel encontrada no DWG destino para receber Psets LOIN.");
                        tr.Commit();
                        return;
                    }

                    // Re-garante recursos (cria/atualiza Psets, atualiza AppliesToFilter).
                    // Idempotente — se EnsureDestinationPropertySetDefinitions já tinha
                    // criado, este passo só revalida.
                    LoinCivil3DApplier.ResourceSummary resSummary =
                        LoinCivil3DApplier.EnsureResources(destDb, tr, destEd, _config);

                    // Anexa Psets per-disciplina + preenche Pset_D/Requisitos/IfcObject.
                    LoinCivil3DApplier.SelectionApplySummary applySummary =
                        LoinCivil3DApplier.ApplyToSelection(destDb, tr, destEd, _config, ids);

                    tr.Commit();

                    warnings.Add(
                        "Aplicacao LOIN no destino: " + applySummary.Applied + "/" + applySummary.Selected +
                        " entidades aplicadas, " + applySummary.WithoutLoinLayer + " sem layer LOIN, " +
                        applySummary.Errors + " erros. Psets criados/atualizados no destino: " +
                        resSummary.CreatedPsets + "/" + resSummary.UpdatedPsets +
                        " (+" + resSummary.AddedProperties + " campos).");
                }
            }
            catch (System.Exception ex)
            {
                warnings.Add("Falha ao aplicar Psets LOIN no destino: " + ex.Message);
            }
        }

        // PSets antigos do template Petrobras (e variantes com/sem acento e com/sem
        // prefixo Pset_) que NÃO devem aparecer no DWG destino — só os PSets novos
        // do LOIN viajam pro IFC. Os PSets nativos do Civil (Corridor Identity,
        // Corridor Model Information, Corridor Shape Information) NÃO entram nessa
        // lista: o IFC Export Extension usa eles. Idem PSets IFC (Pset_CivilElementCommon,
        // Pset_CourseCommon etc) que viram propriedades IFC standard.
        private static readonly string[] LegacyPsetsToStrip = new[]
        {
            // Templates Petrobras antigos (com prefixo Pset_)
            "Pset_A - Dados do Projeto",
            "Pset_B - Informações dos Objetos e Elementos",
            "Pset_B - Informacoes dos Objetos e Elementos",
            // Nomes ANTIGOS de Pset_C — agora obsoletos após renomeação para
            // "Pset_C - Propriedades Fisicas dos Objetos" (canonical novo).
            // O LoinSolidValuesBridge lê de QUALQUER um (canonical novo + legacy)
            // ANTES desta etapa de strip, então é seguro apagar os antigos aqui —
            // os valores já foram migrados para o novo canonical.
            "Pset_C - Propriedades Fisicas dos Objetos e Elementos",
            "Pset_C - Propriedades Físicas dos Objetos e Elementos",
            "Pset_D - Propriedades Geográficas",
            "Pset_D - Propriedades Geograficas",
            "Pset_COORDENAÇÃO",
            "Pset_COORDENACAO",
            // Mesmos templates sem o prefixo Pset_ (formato ainda mais antigo)
            "A - Dados do Projeto",
            "B - Informações dos Objetos e Elementos",
            "B - Informacoes dos Objetos e Elementos",
            "C - Propriedades Físicas dos Objetos e Elementos",
            "C - Propriedades Fisicas dos Objetos e Elementos",
            "D - Propriedades Geográficas",
            "D - Propriedades Geograficas",
            "COORDENAÇÃO",
            "COORDENACAO",
            "E - Requisitos Específicos de Projeto",
            "E - Requisitos Especificos de Projeto"
        };

        // Limpa os PSets legacy do DWG destino após a cópia: desanexa de cada
        // entidade exportada (Solid3d/Body do ModelSpace) e apaga a própria
        // PropertySetDefinition do dicionário. Idempotente; PSets que não existem
        // ou já não estavam anexados são silenciosamente ignorados.
        private static void StripLegacyPsetsFromDestination(string destinationPath, ICollection<string> warnings)
        {
            Document destDoc = null;
            try
            {
                destDoc = ExportacaoSolidosCorredoresService.EnsureDocumentOpen(destinationPath);
                if (destDoc == null)
                {
                    warnings.Add("Nao foi possivel abrir o DWG destino para limpar PSets legacy: " + destinationPath);
                    return;
                }

                Database destDb = destDoc.Database;
                int totalDetach = 0;
                int totalErased = 0;

                using (DocumentLock docLock = destDoc.LockDocument())
                using (Transaction tr = destDb.TransactionManager.StartTransaction())
                {
                    DictionaryPropertySetDefinitions dict = new DictionaryPropertySetDefinitions(destDb);

                    // Resolve as ObjectIds dos PSets legacy presentes no DWG destino.
                    List<KeyValuePair<string, ObjectId>> legacyDefs =
                        new List<KeyValuePair<string, ObjectId>>();
                    foreach (string name in LegacyPsetsToStrip)
                    {
                        try
                        {
                            if (dict.Has(name, tr))
                                legacyDefs.Add(new KeyValuePair<string, ObjectId>(name, dict.GetAt(name)));
                        }
                        catch { }
                    }

                    if (legacyDefs.Count == 0)
                    {
                        tr.Commit();
                        return;
                    }

                    // Desanexa de toda entidade do ModelSpace.
                    BlockTable bt = (BlockTable)tr.GetObject(destDb.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId entId in ms)
                    {
                        if (entId.IsNull || entId.IsErased) continue;

                        DBObject obj = null;
                        try { obj = tr.GetObject(entId, OpenMode.ForRead, false); }
                        catch { continue; }

                        if (obj == null) continue;

                        foreach (KeyValuePair<string, ObjectId> pair in legacyDefs)
                        {
                            try
                            {
                                // RemovePropertySet exige a entidade aberta para escrita.
                                // Upgrade pontual; se a remoção falhar (não estava anexado),
                                // o catch swallow garante que seguimos para a próxima.
                                if (!obj.IsWriteEnabled)
                                {
                                    try { obj.UpgradeOpen(); } catch { continue; }
                                }
                                PropertyDataServices.RemovePropertySet(obj, pair.Value);
                                totalDetach++;
                            }
                            catch
                            {
                                // PSet não anexado nessa entidade — ok.
                            }
                        }
                    }

                    // Apaga as PropertySetDefinitions do dicionário do DWG destino.
                    foreach (KeyValuePair<string, ObjectId> pair in legacyDefs)
                    {
                        try
                        {
                            PropertySetDefinition psd = tr.GetObject(pair.Value, OpenMode.ForWrite)
                                as PropertySetDefinition;
                            if (psd != null && !psd.IsErased)
                            {
                                psd.Erase(true);
                                totalErased++;
                            }
                        }
                        catch { }
                    }

                    tr.Commit();
                }

                // Persiste as alterações no DWG destino.
                try { destDb.SaveAs(destinationPath, DwgVersion.Current); } catch { }

                warnings.Add("PSets legacy removidos no DWG destino: " +
                             totalDetach + " desanexações, " +
                             totalErased + " definições apagadas.");
            }
            catch (System.Exception ex)
            {
                warnings.Add("Falha ao limpar PSets legacy no DWG destino: " + ex.Message);
            }
        }

        private CodeNameMappingCatalog TrySynchronizeCatalog(out string warning)
        {
            warning = string.Empty;

            try
            {
                CodeNameMappingCatalog catalog = new ExportacaoSolidosCorredoresService().SynchronizeCodeNameCatalog();
                if (catalog.UnmappedCount > 0)
                {
                    string sample = string.Join(", ", catalog.GetUnmappedCodeNames(5));
                    warning = "Catalogo JSON de CodeNames sincronizado em '" + catalog.SourcePath + "'. Existem " +
                              catalog.UnmappedCount +
                              " codigos sem categoria definida" +
                              (string.IsNullOrWhiteSpace(sample) ? "." : ": " + sample + ".");
                }

                return catalog;
            }
            catch (System.Exception ex)
            {
                warning = "Nao foi possivel sincronizar o catalogo JSON de CodeNames. A exportacao seguiu com a logica interna atual. Detalhe: " + ex.Message;
                return null;
            }
        }

        private static void EnsureFallbackLayer(Database db, Transaction tr)
        {
            LayerTable layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (layerTable.Has(FallbackLayerName))
                return;

            layerTable.UpgradeOpen();
            LayerTableRecord record = new LayerTableRecord
            {
                Name = FallbackLayerName,
                Color = Color.FromColorIndex(ColorMethod.ByAci, 7),
                IsPlottable = true,
                LinetypeObjectId = db.ContinuousLinetype,
                LineWeight = LineWeight.ByLineWeightDefault
            };

            layerTable.Add(record);
            tr.AddNewlyCreatedDBObject(record, true);
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

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        private sealed class LegacySolidMetadata
        {
            public string CodeName { get; set; } = string.Empty;
            public string CorridorName { get; set; } = string.Empty;
            public string SubassemblyName { get; set; } = string.Empty;
            public Dictionary<string, string> ElementValues { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> PhysicalValues { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public static LegacySolidMetadata Read(
                Entity entity,
                Transaction tr,
                IEnumerable<ExportacaoSolidosCorredoresService.PropertySetBinding> legacyBindings,
                ICollection<string> warnings)
            {
                LegacySolidMetadata metadata = new LegacySolidMetadata();

                foreach (ExportacaoSolidosCorredoresService.PropertySetBinding binding in legacyBindings)
                {
                    PropertySet pset = ExportacaoSolidosCorredoresService.TryOpenReportPropertySet(tr, entity, binding, warnings);
                    if (pset == null)
                        continue;

                    Dictionary<string, string> values = ReadAllValues(entity, pset, binding, warnings);

                    if (binding.Name.Equals("Corridor Shape Information", StringComparison.OrdinalIgnoreCase))
                    {
                        metadata.CodeName = FirstNonEmpty(GetValue(values, "CodeName"), metadata.CodeName);          
                    }
                    else if (binding.Name.Equals("Corridor Identity", StringComparison.OrdinalIgnoreCase))
                    {
                        metadata.SubassemblyName = FirstNonEmpty(GetValue(values, "SubassemblyName"), metadata.SubassemblyName);
                    }
                    else if (binding.Name.Equals("Corridor Model Information", StringComparison.OrdinalIgnoreCase))
                    {

                        metadata.CorridorName = FirstNonEmpty(GetValue(values, "CorridorName"), metadata.SubassemblyName);
                    }


                    
                }

                return metadata;
            }

            private static Dictionary<string, string> ReadAllValues(
                Entity entity,
                PropertySet pset,
                ExportacaoSolidosCorredoresService.PropertySetBinding binding,
                ICollection<string> warnings)
            {
                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (string propertyName in ExportacaoSolidosCorredoresService.GetReportPropertyNames(binding, warnings))
                {
                    if (!string.IsNullOrWhiteSpace(propertyName))
                        values[propertyName] = ExportacaoSolidosCorredoresService.TryReadPropertyValue(pset, entity, propertyName);
                }

                return values;
            }

            private static void Merge(Dictionary<string, string> target, Dictionary<string, string> source)
            {
                foreach (KeyValuePair<string, string> item in source)
                {
                    if (!target.ContainsKey(item.Key))
                        target[item.Key] = item.Value;
                }
            }

            private static string GetValue(Dictionary<string, string> values, string key)
            {
                return values.TryGetValue(key, out string value) ? value : string.Empty;
            }
        }

        private sealed class LoinResolvedElement
        {
            public string LayerName { get; set; } = FallbackLayerName;
            public string Discipline { get; set; } = string.Empty;
            public string ElementName { get; set; } = string.Empty;
            public string IfcClass { get; set; } = "IfcBuildingElementProxy";
            public string PredefinedType { get; set; } = "USERDEFINED";
            public string ClassificationCode { get; set; } = string.Empty;
            public string SourceSheet { get; set; } = string.Empty;
            public int SourceRow { get; set; }
            public LoinColorDefinition Color { get; set; } = new LoinColorDefinition { FallbackAci = 7 };
            public List<string> RequiredElementProperties { get; set; } = new List<string>();
            public List<string> RequiredPhysicalProperties { get; set; } = new List<string>();
            public List<string> NotApplicableProperties { get; set; } = new List<string>();
            public Dictionary<string, string> RowValues { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class LoinSolidElementResolver
        {
            private readonly List<(LoinResolvedElement Element, HashSet<string> Keys)> _items;

            // Override explícito vindo de loin_mapeamento.json:
            // codeName normalizado → LoinResolvedElement já pronto.
            private readonly Dictionary<string, LoinResolvedElement> _overrides =
                new Dictionary<string, LoinResolvedElement>(StringComparer.Ordinal);

            public LoinSolidElementResolver(LoinConfiguration config)
                : this(config, null)
            {
            }

            public LoinSolidElementResolver(LoinConfiguration config, LoinMapeamentoConfig? mapeamento)
            {
                _items = BuildItems(config);
                if (mapeamento != null)
                    BuildOverrides(config, mapeamento);
            }

            public LoinResolvedElement Resolve(string codeName)
            {
                string key = NormalizeKey(codeName);
                if (string.IsNullOrWhiteSpace(key))
                    return BuildFallback(codeName);

                // 1) Override explícito do mapeamento do usuário (tem precedência)
                if (_overrides.TryGetValue(key, out LoinResolvedElement? mapped))
                    return mapped;

                (LoinResolvedElement Element, HashSet<string> Keys) exact = _items.FirstOrDefault(item => item.Keys.Contains(key));
                if (exact.Element != null)
                    return exact.Element;

                (LoinResolvedElement Element, HashSet<string> Keys) contains = _items
                    .Where(item => item.Keys.Any(k => IsMeaningfulContains(key, k)))
                    .OrderByDescending(item => item.Keys.Max(k => CommonLengthScore(key, k)))
                    .FirstOrDefault();

                return contains.Element ?? BuildFallback(codeName);
            }

            // Constrói o dicionário de overrides correlacionando cada
            // Mapeamentos[].Camada com o LoinElementDefinition apontado pela
            // LoinLinha (via SourceSheet+SourceRow ou via fallback por Disciplina+Elemento).
            private void BuildOverrides(LoinConfiguration config, LoinMapeamentoConfig mapeamento)
            {
                if (mapeamento.Mapeamentos == null || mapeamento.Mapeamentos.Count == 0) return;
                if (mapeamento.TabelaLoin == null || mapeamento.TabelaLoin.Count == 0) return;
                if (config.Elements == null || config.Elements.Count == 0) return;

                Dictionary<string, LoinLinhaDto> linhaById =
                    mapeamento.TabelaLoin.ToDictionary(l => l.Id, l => l, StringComparer.OrdinalIgnoreCase);

                foreach (LoinItemMapeamentoDto m in mapeamento.Mapeamentos)
                {
                    if (string.IsNullOrWhiteSpace(m.Camada) || string.IsNullOrWhiteSpace(m.LoinLinhaId)) continue;
                    if (!linhaById.TryGetValue(m.LoinLinhaId, out LoinLinhaDto? linha)) continue;

                    LoinElementDefinition? alvo = LocalizarElementDef(config, linha);
                    if (alvo == null) continue;

                    LoinResolvedElement resolved = BuildResolvedFromElement(alvo);
                    _overrides[NormalizeKey(m.Camada)] = resolved;
                }
            }

            private static LoinElementDefinition? LocalizarElementDef(LoinConfiguration config, LoinLinhaDto linha)
            {
                // 1) Match exato por (SourceSheet, SourceRow)
                if (!string.IsNullOrWhiteSpace(linha.SourceSheet) && linha.SourceRow > 0)
                {
                    LoinElementDefinition? bySheetRow = config.Elements.FirstOrDefault(e =>
                        string.Equals(e.SourceSheet, linha.SourceSheet, StringComparison.OrdinalIgnoreCase) &&
                        e.SourceRow == linha.SourceRow);
                    if (bySheetRow != null) return bySheetRow;
                }

                // 2) Fallback: match por (Disciplina, Elemento)
                if (!string.IsNullOrWhiteSpace(linha.Elemento))
                {
                    return config.Elements.FirstOrDefault(e =>
                        string.Equals(e.Element, linha.Elemento, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrWhiteSpace(linha.Disciplina) ||
                         string.Equals(e.Discipline, linha.Disciplina, StringComparison.OrdinalIgnoreCase)));
                }

                return null;
            }

            private static LoinResolvedElement BuildResolvedFromElement(LoinElementDefinition e)
            {
                return new LoinResolvedElement
                {
                    LayerName                  = SanitizeLayerName(e.Layer),
                    Discipline                 = e.Discipline,
                    ElementName                = e.Element,
                    IfcClass                   = FirstNonEmpty(e.IfcClass, "IfcBuildingElementProxy"),
                    PredefinedType             = FirstNonEmpty(e.PredefinedType, "USERDEFINED"),
                    ClassificationCode         = e.ClassificationCode,
                    SourceSheet                = e.SourceSheet,
                    SourceRow                  = e.SourceRow,
                    Color                      = e.Color ?? new LoinColorDefinition { FallbackAci = 7 },
                    RequiredElementProperties  = e.RequiredElementProperties?.ToList() ?? new List<string>(),
                    RequiredPhysicalProperties = e.RequiredPhysicalProperties?.ToList() ?? new List<string>(),
                    NotApplicableProperties    = e.NotApplicableProperties?.ToList()    ?? new List<string>(),
                    RowValues = e.RowValues != null
                        ? new Dictionary<string, string>(e.RowValues, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                };
            }

            private static List<(LoinResolvedElement Element, HashSet<string> Keys)> BuildItems(LoinConfiguration config)
            {
                List<(LoinResolvedElement Element, HashSet<string> Keys)> items = new List<(LoinResolvedElement Element, HashSet<string> Keys)>();

                foreach (LoinElementDefinition element in (config.Elements ?? new List<LoinElementDefinition>())
                             .Where(e => !string.IsNullOrWhiteSpace(e.Layer)))
                {
                    LoinResolvedElement resolved = new LoinResolvedElement
                    {
                        LayerName = SanitizeLayerName(element.Layer),
                        Discipline = element.Discipline,
                        ElementName = element.Element,
                        IfcClass = FirstNonEmpty(element.IfcClass, "IfcBuildingElementProxy"),
                        PredefinedType = FirstNonEmpty(element.PredefinedType, "USERDEFINED"),
                        ClassificationCode = element.ClassificationCode,
                        SourceSheet = element.SourceSheet,
                        SourceRow = element.SourceRow,
                        Color = element.Color ?? new LoinColorDefinition { FallbackAci = 7 },
                        RequiredElementProperties = element.RequiredElementProperties?.ToList() ?? new List<string>(),
                        RequiredPhysicalProperties = element.RequiredPhysicalProperties?.ToList() ?? new List<string>(),
                        NotApplicableProperties = element.NotApplicableProperties?.ToList() ?? new List<string>(),
                        RowValues = element.RowValues != null
                            ? new Dictionary<string, string>(element.RowValues, StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    };

                    HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    AddKey(keys, element.Layer);
                    AddKey(keys, element.Element);
                    AddKey(keys, element.ClassificationCode);
                    AddKey(keys, element.SourceSheet);
                    foreach (string value in element.RowValues?.Values ?? Enumerable.Empty<string>())
                        AddRowValueKey(keys, value);

                    items.Add((resolved, keys));
                }

                return items;
            }

            private static void AddKey(ICollection<string> keys, string value)
            {
                string key = NormalizeKey(value);
                if (string.IsNullOrWhiteSpace(key))
                    return;

                keys.Add(key);
                foreach (string part in Regex.Split(key, @"[\s_\-\.]+"))
                {
                    if (part.Length >= 3)
                        keys.Add(part);
                }
            }

            private static void AddRowValueKey(ICollection<string> keys, string value)
            {
                string key = NormalizeKey(value);
                if (key.Length < 3 || key is "S" or "N A" or "NA")
                    return;

                AddKey(keys, value);
            }

            private static bool IsMeaningfulContains(string codeKey, string candidateKey)
            {
                if (candidateKey.Length < 4 || codeKey.Length < 4)
                    return false;

                return codeKey.Contains(candidateKey, StringComparison.OrdinalIgnoreCase) ||
                       candidateKey.Contains(codeKey, StringComparison.OrdinalIgnoreCase);
            }

            private static int CommonLengthScore(string codeKey, string candidateKey)
            {
                if (codeKey.Contains(candidateKey, StringComparison.OrdinalIgnoreCase) ||
                    candidateKey.Contains(codeKey, StringComparison.OrdinalIgnoreCase))
                    return Math.Min(codeKey.Length, candidateKey.Length);

                return 0;
            }

            private static LoinResolvedElement BuildFallback(string codeName)
            {
                return new LoinResolvedElement
                {
                    LayerName = FallbackLayerName,
                    ElementName = codeName,
                    Color = new LoinColorDefinition { FallbackAci = 7 }
                };
            }

            private static string NormalizeKey(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return string.Empty;

                string normalized = value.Trim().Normalize(NormalizationForm.FormD);
                StringBuilder sb = new StringBuilder(normalized.Length);

                foreach (char c in normalized)
                {
                    UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
                    if (category != UnicodeCategory.NonSpacingMark)
                        sb.Append(c);
                }

                return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), @"[^A-Z0-9]+", " ")
                    .Trim()
                    .ToUpperInvariant();
            }
        }
    }
}
