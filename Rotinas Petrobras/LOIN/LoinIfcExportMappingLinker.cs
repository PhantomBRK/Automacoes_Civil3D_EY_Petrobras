using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutomacoesCivil3D
{
    // Liga o mapeamento LOIN (loin_mapeamento.json) à planilha de configuração do
    // IFC Export Extension do Civil 3D (IfcInfraExportMapping-IFC4X3_ADD2.xlsx).
    // Preenche a coluna IfcExportAs ("IfcClass" ou "IfcClass.PredefinedType") para
    // cada entrada mapeada, distribuindo por tipo (CADLayerName / ShapeCode /
    // LinkCode / PointCode / *StyleName) conforme a Origem do item LOIN.
    //
    // Saídas: a própria XLSX (master) + o IfcInfraExportMapping.json correspondente.
    public sealed class LoinIfcExportMappingLinker
    {
        private const string MappingSheetName = "Mapping";
        private const string LoinSource = "LOIN Mapping";

        // Prefixos usados pelo LoinCodeSetStyleCorredores ao criar os styles.
        // Devem casar com BuildStyleName naquele arquivo.
        private const string ShapeStyleNamePrefix = "LOIN - SHAPE - ";
        private const string LinkStyleNamePrefix  = "LOIN - LINK - ";
        private const string MarkerStyleNamePrefix = "LOIN - MARKER - ";

        // Padrões usados pelo Civil 3D ao distribuir mapeamentos. Os Codes
        // (Shape/Link/Point) vão na chave JSON "Code"; os demais em "Name".
        private static readonly HashSet<string> CodeBasedTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "ShapeCode", "LinkCode", "PointCode"
        };

        [CommandMethod("LOIN_LINKAR_IFCEXPORT", CommandFlags.Modal)]
        public void Executar()
        {
            Editor ed = Manager.DocEditor;
            Document doc = Manager.DocCad;

            try
            {
                string drawingPath = string.Empty;
                try { drawingPath = doc?.Name ?? string.Empty; }
                catch { drawingPath = string.Empty; }

                string mappingPath = LoinMapeamentoService.ResolverCaminhoConfig(drawingPath);
                if (!File.Exists(mappingPath))
                {
                    string msg = "loin_mapeamento.json não encontrado em:\n  " + mappingPath +
                                 "\n\nRode LOINMAP primeiro e salve o mapeamento.";
                    ed.WriteMessage("\n[LOIN] " + msg.Replace("\n", "\n[LOIN] "));
                    AcadApp.ShowAlertDialog(msg);
                    return;
                }

                LoinMapeamentoConfig config = LoinMapeamentoService.Carregar(mappingPath);
                if (config.Mapeamentos.Count == 0)
                {
                    ed.WriteMessage("\n[LOIN] Mapeamento vazio. Nada para escrever.");
                    return;
                }

                string xlsxPath = ResolverCaminhoXlsx(drawingPath);
                if (string.IsNullOrWhiteSpace(xlsxPath))
                {
                    ed.WriteMessage("\n[LOIN] Operação cancelada — XLSX não selecionada.");
                    return;
                }
                if (!File.Exists(xlsxPath))
                {
                    AcadApp.ShowAlertDialog("XLSX não encontrada:\n" + xlsxPath);
                    return;
                }

                List<LinkEntry> entradas = BuildEntradas(config);
                if (entradas.Count == 0)
                {
                    AcadApp.ShowAlertDialog(
                        "Nenhuma linha LOIN tem IfcClass preenchida — não há nada para linkar.\n" +
                        "Preencha a coluna IFC no LOINMAP (ou na coluna IFC da matriz Excel original).");
                    return;
                }

                ApplyResult resXlsx = AtualizarXlsx(xlsxPath, entradas);
                string jsonPath = Path.Combine(
                    Path.GetDirectoryName(xlsxPath) ?? ".",
                    "IfcInfraExportMapping.json");
                ApplyResult resJson = File.Exists(jsonPath)
                    ? AtualizarJson(jsonPath, entradas)
                    : new ApplyResult { Skipped = "IfcInfraExportMapping.json não encontrado ao lado da XLSX — JSON não atualizado." };

                string relatorio = MontarRelatorio(xlsxPath, jsonPath, entradas, resXlsx, resJson);
                ed.WriteMessage("\n" + relatorio.Replace("\r\n", "\n"));
                AcadApp.ShowAlertDialog(MontarAlerta(entradas, resXlsx, resJson));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n[LOIN] Erro ao linkar IfcExportMapping: " + ex.Message);
                MessageBox.Show(ex.Message, "LOIN - Linkar IfcExport", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ------------------------------ Resolução de paths ------------------------------

        // 1) ao lado do DWG: "{stem}-IfcInfraExportMapping-*.xlsx" → "IfcInfraExportMapping-*.xlsx"
        // 2) fallback: OpenFileDialog
        private static string ResolverCaminhoXlsx(string drawingPath)
        {
            string folder = string.IsNullOrWhiteSpace(drawingPath)
                ? string.Empty
                : Path.GetDirectoryName(drawingPath) ?? string.Empty;

            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                string stem = Path.GetFileNameWithoutExtension(drawingPath);

                // 1a — IFC-output-folder pattern: "{stem}-IfcInfraExportMapping-*.xlsx"
                if (!string.IsNullOrWhiteSpace(stem))
                {
                    string padraoComStem = stem + "-IfcInfraExportMapping-*.xlsx";
                    string achado = Directory.GetFiles(folder, padraoComStem).FirstOrDefault();
                    if (achado != null) return achado;
                }

                // 1b — drawing-folder pattern: "IfcInfraExportMapping-*.xlsx"
                string padraoBase = "IfcInfraExportMapping-*.xlsx";
                string baseAchado = Directory.GetFiles(folder, padraoBase).FirstOrDefault();
                if (baseAchado != null) return baseAchado;
            }

            using OpenFileDialog dlg = new OpenFileDialog
            {
                Title = "Selecione o IfcInfraExportMapping-IFC4X3_ADD2.xlsx",
                Filter = "Planilha IFC Export Mapping (*.xlsx)|*.xlsx|Todos os arquivos|*.*",
                CheckFileExists = true,
                Multiselect = false,
                InitialDirectory = string.IsNullOrEmpty(folder)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                    : folder
            };
            return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : string.Empty;
        }

        // ------------------------------ Plan de mapeamento ------------------------------

        // Para cada item do mapeamento LOIN, expande em 1..N entradas (Tipo,Name,IfcExportAs)
        // dependendo da Origem. Itens sem IfcClass na linha LOIN são descartados.
        private static List<LinkEntry> BuildEntradas(LoinMapeamentoConfig config)
        {
            Dictionary<string, LoinLinhaDto> linhas = config.TabelaLoin
                .Where(l => !string.IsNullOrWhiteSpace(l.Id))
                .ToDictionary(l => l.Id, StringComparer.OrdinalIgnoreCase);

            List<LinkEntry> result = new List<LinkEntry>();

            foreach (LoinItemMapeamentoDto item in config.Mapeamentos)
            {
                if (string.IsNullOrWhiteSpace(item.Camada) ||
                    string.IsNullOrWhiteSpace(item.LoinLinhaId))
                    continue;

                if (!linhas.TryGetValue(item.LoinLinhaId, out LoinLinhaDto linha))
                    continue;

                string ifcClass = (linha.IfcClass ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(ifcClass))
                    continue;

                string predef = (linha.PredefinedType ?? string.Empty).Trim();
                string ifcExportAs = string.IsNullOrWhiteSpace(predef)
                    ? ifcClass
                    : ifcClass + "." + predef;

                string camada = item.Camada.Trim();
                string elemento = (linha.Elemento ?? string.Empty).Trim();
                string origem = (item.Origem ?? string.Empty).Trim();

                switch (origem)
                {
                    case "Corridor-Shape":
                        result.Add(new LinkEntry("ShapeCode", camada, ifcExportAs));
                        if (!string.IsNullOrWhiteSpace(elemento))
                            result.Add(new LinkEntry("ShapeStyleName", SanitizeStyleName(ShapeStyleNamePrefix + elemento), ifcExportAs));
                        break;

                    case "Corridor-Link":
                        result.Add(new LinkEntry("LinkCode", camada, ifcExportAs));
                        if (!string.IsNullOrWhiteSpace(elemento))
                            result.Add(new LinkEntry("LinkStyleName", SanitizeStyleName(LinkStyleNamePrefix + elemento), ifcExportAs));
                        break;

                    case "Corridor-Point":
                        result.Add(new LinkEntry("PointCode", camada, ifcExportAs));
                        if (!string.IsNullOrWhiteSpace(elemento))
                            result.Add(new LinkEntry("PointStyleName", SanitizeStyleName(MarkerStyleNamePrefix + elemento), ifcExportAs));
                        break;

                    case "Layer":
                    case "LOIN-XLSX":
                        result.Add(new LinkEntry("CADLayerName", camada, ifcExportAs));
                        break;

                    case "Code Set Style":
                        // Code Set Style traz códigos sem distinção Shape/Link/Point
                        // — duplica para os três tipos; quem não casar no Civil 3D fica inerte.
                        result.Add(new LinkEntry("ShapeCode", camada, ifcExportAs));
                        result.Add(new LinkEntry("LinkCode", camada, ifcExportAs));
                        result.Add(new LinkEntry("PointCode", camada, ifcExportAs));
                        break;

                    case "Manual":
                    default:
                        // Sem indicação clara — assume layer (caso mais comum em entrada manual).
                        result.Add(new LinkEntry("CADLayerName", camada, ifcExportAs));
                        break;
                }
            }

            return result
                .GroupBy(e => (e.Tipo, e.Name), comparer: TupleIgnoreCaseComparer.Instance)
                .Select(g => g.First())
                .ToList();
        }

        // ------------------------------ XLSX ------------------------------

        private static ApplyResult AtualizarXlsx(string xlsxPath, List<LinkEntry> entradas)
        {
            ApplyResult result = new ApplyResult();

            // ClosedXML usa licenciamento em 0.105+; segue o mesmo padrão do LoinMapeamentoModels.
            // Como não há licensing API obrigatório em ClosedXML, basta abrir.
            using XLWorkbook wb = new XLWorkbook(xlsxPath);
            IXLWorksheet ws = wb.Worksheets
                .FirstOrDefault(w => string.Equals(w.Name, MappingSheetName, StringComparison.OrdinalIgnoreCase));
            if (ws == null)
                throw new InvalidOperationException("Aba '" + MappingSheetName + "' não encontrada na XLSX.");

            // Indexa todas as linhas existentes por (Tipo, Name) — tanto na forma literal
            // quanto na forma com regex escapado, porque o Civil 3D armazena com escape.
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            Dictionary<(string, string), int> indice =
                new Dictionary<(string, string), int>(TupleIgnoreCaseComparer.Instance);

            for (int r = 2; r <= lastRow; r++)
            {
                string tipo = ws.Cell(r, 1).GetString().Trim();
                string name = ws.Cell(r, 2).GetString().Trim();
                if (tipo.Length == 0) continue;

                indice[(tipo, name)] = r;
                string descape = Unescape(name);
                if (descape != name)
                    indice[(tipo, descape)] = r;
            }

            int nextRow = lastRow + 1;

            foreach (LinkEntry e in entradas)
            {
                string nameEscaped = NeedsEscape(e.Name) ? Regex.Escape(e.Name) : e.Name;

                int targetRow;
                if (indice.TryGetValue((e.Tipo, e.Name), out targetRow) ||
                    indice.TryGetValue((e.Tipo, nameEscaped), out targetRow))
                {
                    // Update
                    string atual = ws.Cell(targetRow, 4).GetString().Trim();
                    if (string.Equals(atual, e.IfcExportAs, StringComparison.Ordinal))
                    {
                        result.Inalterados++;
                        continue;
                    }
                    ws.Cell(targetRow, 4).Value = e.IfcExportAs;     // IfcExportAs
                    ws.Cell(targetRow, 3).Value = 1;                  // Export = true
                    ws.Cell(targetRow, 8).Value = LoinSource;         // Source
                    result.Atualizados++;
                }
                else
                {
                    // Insert
                    ws.Cell(nextRow, 1).Value = e.Tipo;
                    ws.Cell(nextRow, 2).Value = nameEscaped;
                    ws.Cell(nextRow, 3).Value = 1;
                    ws.Cell(nextRow, 4).Value = e.IfcExportAs;
                    ws.Cell(nextRow, 8).Value = LoinSource;
                    indice[(e.Tipo, e.Name)] = nextRow;
                    indice[(e.Tipo, nameEscaped)] = nextRow;
                    nextRow++;
                    result.Adicionados++;
                }
            }

            wb.Save();
            return result;
        }

        // ------------------------------ JSON ------------------------------

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static ApplyResult AtualizarJson(string jsonPath, List<LinkEntry> entradas)
        {
            ApplyResult result = new ApplyResult();

            string raw = File.ReadAllText(jsonPath, Encoding.UTF8);
            JsonNode root = JsonNode.Parse(raw) ?? new JsonObject();

            foreach (LinkEntry e in entradas)
            {
                string secao = "Map" + e.Tipo; // ShapeCode → MapShapeCode, CADLayerName → MapCADLayerName
                JsonArray arr;
                if (root[secao] is JsonArray existente)
                {
                    arr = existente;
                }
                else
                {
                    arr = new JsonArray();
                    root[secao] = arr;
                }

                string keyName = CodeBasedTypes.Contains(e.Tipo) ? "Code" : "Name";
                string nameEscaped = NeedsEscape(e.Name) ? Regex.Escape(e.Name) : e.Name;

                JsonNode match = arr.FirstOrDefault(n =>
                {
                    if (n is not JsonObject obj) return false;
                    string val = obj[keyName]?.GetValue<string>() ?? string.Empty;
                    return string.Equals(val, e.Name, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(val, nameEscaped, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(Unescape(val), e.Name, StringComparison.OrdinalIgnoreCase);
                });

                if (match is JsonObject mo)
                {
                    string atual = mo["IfcExportAs"]?.GetValue<string>() ?? string.Empty;
                    if (string.Equals(atual, e.IfcExportAs, StringComparison.Ordinal))
                    {
                        result.Inalterados++;
                        continue;
                    }
                    mo["IfcExportAs"] = e.IfcExportAs;
                    mo["Export"] = true;
                    result.Atualizados++;
                }
                else
                {
                    JsonObject novo = new JsonObject
                    {
                        [keyName] = nameEscaped,
                        ["IfcExportAs"] = e.IfcExportAs,
                        ["Export"] = true
                    };
                    arr.Add(novo);
                    result.Adicionados++;
                }
            }

            File.WriteAllText(jsonPath, root.ToJsonString(JsonOpts), Encoding.UTF8);
            return result;
        }

        // ------------------------------ Helpers ------------------------------

        private static bool NeedsEscape(string s)
        {
            // Caracteres regex relevantes para o Civil 3D — listados no ReadME da xlsx.
            foreach (char c in s)
            {
                if (".*?+{}|()[]^$\\".IndexOf(c) >= 0)
                    return true;
            }
            return false;
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
            // Reverte Regex.Escape: \X → X para os caracteres relevantes.
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length && ".*?+{}|()[]^$\\ ".IndexOf(s[i + 1]) >= 0)
                {
                    sb.Append(s[i + 1]);
                    i++;
                }
                else
                {
                    sb.Append(s[i]);
                }
            }
            return sb.ToString();
        }

        private static string SanitizeStyleName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "LOIN";
            string sanitized = Regex.Replace(name.Trim(), @"\s+", " ");
            char[] invalid = { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', '=' };
            foreach (char c in invalid)
                sanitized = sanitized.Replace(c, '-');
            return sanitized.Length <= 240 ? sanitized : sanitized.Substring(0, 240);
        }

        private static string MontarRelatorio(
            string xlsxPath, string jsonPath, List<LinkEntry> entradas,
            ApplyResult xlsx, ApplyResult json)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[LOIN] Link com IFC Export Mapping concluído.");
            sb.AppendLine("XLSX: " + xlsxPath);
            sb.AppendLine("JSON: " + jsonPath);
            sb.AppendLine("Entradas LOIN preparadas: " + entradas.Count);

            sb.AppendLine();
            sb.AppendLine("Distribuição por tipo:");
            foreach (var grp in entradas.GroupBy(e => e.Tipo).OrderBy(g => g.Key))
                sb.AppendLine("  - " + grp.Key + ": " + grp.Count());

            sb.AppendLine();
            sb.AppendLine("XLSX  → atualizados " + xlsx.Atualizados + ", adicionados " + xlsx.Adicionados + ", inalterados " + xlsx.Inalterados);
            sb.AppendLine("JSON  → " + (json.Skipped ?? ("atualizados " + json.Atualizados + ", adicionados " + json.Adicionados + ", inalterados " + json.Inalterados)));
            return sb.ToString();
        }

        private static string MontarAlerta(List<LinkEntry> entradas, ApplyResult xlsx, ApplyResult json)
        {
            return
                "IfcInfraExportMapping atualizado.\n\n" +
                "Entradas LOIN: " + entradas.Count + "\n" +
                "XLSX: +" + xlsx.Adicionados + " / ↻" + xlsx.Atualizados + " / =" + xlsx.Inalterados + "\n" +
                "JSON: " + (json.Skipped ?? ("+" + json.Adicionados + " / ↻" + json.Atualizados + " / =" + json.Inalterados)) + "\n\n" +
                "Detalhes na linha de comando.";
        }

        private sealed class LinkEntry
        {
            public string Tipo { get; }
            public string Name { get; }
            public string IfcExportAs { get; }

            public LinkEntry(string tipo, string name, string ifcExportAs)
            {
                Tipo = tipo;
                Name = name;
                IfcExportAs = ifcExportAs;
            }
        }

        private sealed class ApplyResult
        {
            public int Adicionados { get; set; }
            public int Atualizados { get; set; }
            public int Inalterados { get; set; }
            public string Skipped { get; set; }
        }

        private sealed class TupleIgnoreCaseComparer : IEqualityComparer<(string, string)>
        {
            public static readonly TupleIgnoreCaseComparer Instance = new TupleIgnoreCaseComparer();

            public bool Equals((string, string) x, (string, string) y) =>
                string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode((string, string) obj) =>
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1 ?? string.Empty) ^
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2 ?? string.Empty);
        }
    }
}
