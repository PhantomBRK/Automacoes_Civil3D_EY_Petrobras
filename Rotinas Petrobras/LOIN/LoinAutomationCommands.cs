using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace AutomacoesCivil3D
{
    public class LoinAutomationCommands
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        [CommandMethod("LOIN_GERAR_JSON", CommandFlags.Modal)]
        public void GerarJson()
        {
            Editor ed = Manager.DocEditor;

            try
            {
                string xlsxPath = SelectOpenFile(
                    "Selecionar planilha LOIN",
                    "Planilha Excel (*.xlsx)|*.xlsx");

                if (string.IsNullOrWhiteSpace(xlsxPath))
                    return;

                LoinConfiguration config = LoinWorkbookReader.Read(xlsxPath);

                string defaultJsonPath = GetDefaultJsonPath(xlsxPath);
                string jsonPath = SelectSaveFile(
                    "Salvar JSON LOIN",
                    "JSON (*.json)|*.json",
                    defaultJsonPath);

                if (string.IsNullOrWhiteSpace(jsonPath))
                    return;

                SaveConfig(config, jsonPath);
                WriteConfigSummary(ed, config, jsonPath);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n[LOIN] Erro ao gerar JSON: " + ex.Message);
                MessageBox.Show(ex.Message, "LOIN - Gerar JSON", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        [CommandMethod("LOIN_PREPARAR_DWG", CommandFlags.Modal)]
        public void PrepararDwg()
        {
            Editor ed = Manager.DocEditor;
            Document doc = Manager.DocCad;
            Database db = doc.Database;

            try
            {
                string inputPath = SelectOpenFile(
                    "Selecionar JSON ou planilha LOIN",
                    "LOIN JSON ou Excel (*.json;*.xlsx)|*.json;*.xlsx|JSON (*.json)|*.json|Planilha Excel (*.xlsx)|*.xlsx");

                if (string.IsNullOrWhiteSpace(inputPath))
                    return;

                LoinConfiguration config = LoadConfig(inputPath, out string generatedJsonPath);

                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    LoinCivil3DApplier.ResourceSummary summary =
                        LoinCivil3DApplier.EnsureResources(db, tr, ed, config);

                    tr.Commit();

                    ed.WriteMessage(
                        "\n[LOIN] DWG preparado." +
                        "\nLayers criados: " + summary.CreatedLayers +
                        " | layers atualizados: " + summary.UpdatedLayers +
                        "\nPsets criados: " + summary.CreatedPsets +
                        " | psets atualizados: " + summary.UpdatedPsets +
                        " | campos adicionados: " + summary.AddedProperties +
                        "\nErros: " + summary.Errors);
                }

                if (!string.IsNullOrWhiteSpace(generatedJsonPath))
                    ed.WriteMessage("\n[LOIN] JSON gerado: " + generatedJsonPath);

                WriteConfigSummary(ed, config, generatedJsonPath);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n[LOIN] Erro ao preparar DWG: " + ex.Message);
                MessageBox.Show(ex.Message, "LOIN - Preparar DWG", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        [CommandMethod("LOIN_APLICAR_SELECAO", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void AplicarSelecao()
        {
            Editor ed = Manager.DocEditor;
            Document doc = Manager.DocCad;
            Database db = doc.Database;

            try
            {
                string inputPath = SelectOpenFile(
                    "Selecionar JSON ou planilha LOIN",
                    "LOIN JSON ou Excel (*.json;*.xlsx)|*.json;*.xlsx|JSON (*.json)|*.json|Planilha Excel (*.xlsx)|*.xlsx");

                if (string.IsNullOrWhiteSpace(inputPath))
                    return;

                LoinConfiguration config = LoadConfig(inputPath, out string generatedJsonPath);

                PromptSelectionOptions selectionOptions = new PromptSelectionOptions
                {
                    MessageForAdding = "\nSelecione os objetos que receberao os PSets/IFC da LOIN: "
                };

                PromptSelectionResult selectionResult = ed.GetSelection(selectionOptions);
                if (selectionResult.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n[LOIN] Nenhum objeto selecionado.");
                    return;
                }

                ObjectId[] ids = selectionResult.Value.GetObjectIds();

                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    LoinCivil3DApplier.ResourceSummary resourceSummary =
                        LoinCivil3DApplier.EnsureResources(db, tr, ed, config);

                    LoinCivil3DApplier.SelectionApplySummary applySummary =
                        LoinCivil3DApplier.ApplyToSelection(db, tr, ed, config, ids);

                    tr.Commit();

                    ed.WriteMessage(
                        "\n[LOIN] Aplicacao concluida." +
                        "\nSelecionados: " + applySummary.Selected +
                        " | aplicados: " + applySummary.Applied +
                        " | sem layer LOIN: " + applySummary.WithoutLoinLayer +
                        " | erros: " + applySummary.Errors +
                        "\nLayers criados/atualizados: " + resourceSummary.CreatedLayers + "/" + resourceSummary.UpdatedLayers +
                        " | Psets criados/atualizados: " + resourceSummary.CreatedPsets + "/" + resourceSummary.UpdatedPsets);
                }

                if (!string.IsNullOrWhiteSpace(generatedJsonPath))
                    ed.WriteMessage("\n[LOIN] JSON gerado: " + generatedJsonPath);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n[LOIN] Erro ao aplicar selecao: " + ex.Message);
                MessageBox.Show(ex.Message, "LOIN - Aplicar Selecao", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

        private static void WriteConfigSummary(Editor ed, LoinConfiguration config, string jsonPath)
        {
            int warnings = config.Diagnostics.Count(d =>
                string.Equals(d.Severity, "warning", StringComparison.OrdinalIgnoreCase));

            ed.WriteMessage(
                "\n[LOIN] Disciplinas incluidas: " + string.Join(", ", config.IncludedSheets) +
                "\n[LOIN] Disciplinas ignoradas: " + string.Join(", ", config.IgnoredSheets) +
                "\n[LOIN] Elementos: " + config.Elements.Count +
                " | layers: " + config.Layers.Count +
                " | mapeamentos IFC: " + config.Mappings.Count +
                " | diagnosticos: " + config.Diagnostics.Count +
                " | warnings: " + warnings);

            if (!string.IsNullOrWhiteSpace(jsonPath))
                ed.WriteMessage("\n[LOIN] Arquivo JSON: " + jsonPath);
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

        private static string SelectSaveFile(string title, string filter, string defaultPath)
        {
            using SaveFileDialog dialog = new SaveFileDialog
            {
                Title = title,
                Filter = filter,
                FileName = Path.GetFileName(defaultPath),
                InitialDirectory = Path.GetDirectoryName(defaultPath),
                AddExtension = true,
                DefaultExt = "json",
                OverwritePrompt = true
            };

            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : string.Empty;
        }

        private static string GetDefaultJsonPath(string sourcePath)
        {
            string folder = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
            string name = Path.GetFileNameWithoutExtension(sourcePath);
            return Path.Combine(folder, name + "_AutomacoesCivil3D_LOIN.json");
        }
    }
}
