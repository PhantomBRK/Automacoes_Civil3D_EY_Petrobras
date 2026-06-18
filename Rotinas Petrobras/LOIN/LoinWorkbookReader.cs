using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AutomacoesCivil3D
{
    public static class LoinWorkbookReader
    {
        private static readonly HashSet<string> IncludedSheetKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "TOPOGRAFIA",
            "TERRAPLENAGEM",
            "PAVIMENTACAO",
            "DRENAGEM",
            "SINALIZACAO",
            "ILUMINACAO"
        };

        private static readonly HashSet<string> IgnoredSheetKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "OAE",
            "OBRAS COMPLEMENTARES",
            "PAISAGISMO"
        };

        public static LoinConfiguration Read(string xlsxPath)
        {
            if (string.IsNullOrWhiteSpace(xlsxPath))
                throw new ArgumentException("Caminho da planilha nao informado.", nameof(xlsxPath));

            if (!File.Exists(xlsxPath))
                throw new FileNotFoundException("Planilha LOIN nao encontrada.", xlsxPath);

            using FileStream fs = new FileStream(xlsxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using XLWorkbook workbook = new XLWorkbook(fs);

            LoinConfiguration config = new LoinConfiguration
            {
                GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                SourceFile = xlsxPath
            };

            Dictionary<string, string> projectFields = new(StringComparer.OrdinalIgnoreCase);

            // Pset_B (cols 7-18) e Pset_C (cols 19+) são UNIFICADOS — único conjunto
            // de campos agregado das abas/disciplinas processadas. Decisão Petrobras:
            // ABCD são suficientes (sem split per-disciplina, sem Pset_Requisitos,
            // sem IfcObject Properties separado).
            Dictionary<string, LoinPsetProperty> elementFields = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, LoinPsetProperty> physicalFields = new(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, LoinLayerDefinition> layers = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, LoinElementDefinition> firstElementByLayer = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> layerOccurrenceCount = new(StringComparer.OrdinalIgnoreCase);

            foreach (IXLWorksheet ws in workbook.Worksheets)
            {
                string sheetName = Clean(ws.Name);
                string sheetKey = NormalizeKey(sheetName);

                if (IgnoredSheetKeys.Contains(sheetKey))
                {
                    config.IgnoredSheets.Add(sheetName);
                    continue;
                }

                if (!IncludedSheetKeys.Contains(sheetKey))
                {
                    config.Diagnostics.Add(new LoinDiagnostic
                    {
                        Severity = "info",
                        Sheet = sheetName,
                        Message = "Aba ignorada por nao fazer parte do escopo LOIN selecionado."
                    });
                    continue;
                }

                int headerRow = FindHeaderRow(ws);
                if (headerRow == 0)
                {
                    config.Diagnostics.Add(new LoinDiagnostic
                    {
                        Severity = "warning",
                        Sheet = sheetName,
                        Message = "Cabecalho tecnico nao localizado."
                    });
                    continue;
                }

                config.IncludedSheets.Add(sheetName);
                CollectProjectFields(ws, projectFields);

                int lastColumn = Math.Max(ws.LastColumnUsed()?.ColumnNumber() ?? 0, 36);
                int lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
                Dictionary<int, string> headers = BuildHeaders(ws, headerRow, lastColumn);

                // Acumula fields no balde unificado (única coleção para todas as disciplinas).
                // Dedup por chave normalizada — se a mesma coluna aparece em mais de uma
                // aba, fica a primeira ocorrência.
                CollectPsetFields(headers, 7, 18, elementFields);
                CollectPsetFields(headers, 19, lastColumn, physicalFields);

                for (int row = headerRow + 1; row <= lastRow; row++)
                {
                    string elementName = CellText(ws, row, 1);
                    if (string.IsNullOrWhiteSpace(elementName))
                        continue;

                    LoinElementDefinition element = BuildElement(ws, sheetName, row, headers, lastColumn);
                    config.Elements.Add(element);

                    if (string.IsNullOrWhiteSpace(element.Layer))
                    {
                        config.Diagnostics.Add(new LoinDiagnostic
                        {
                            Severity = "warning",
                            Sheet = sheetName,
                            Row = row,
                            Message = "Elemento sem layer definido."
                        });
                    }
                    else
                    {
                        AddLayer(layers, element);
                        layerOccurrenceCount[element.Layer] = layerOccurrenceCount.TryGetValue(element.Layer, out int count) ? count + 1 : 1;

                        if (!firstElementByLayer.ContainsKey(element.Layer))
                            firstElementByLayer[element.Layer] = element;
                    }

                    if (string.IsNullOrWhiteSpace(element.IfcClass))
                    {
                        config.Diagnostics.Add(new LoinDiagnostic
                        {
                            Severity = "warning",
                            Sheet = sheetName,
                            Row = row,
                            Message = "Elemento sem classe IFC definida."
                        });
                    }

                    if (string.IsNullOrWhiteSpace(element.PredefinedType))
                    {
                        config.Diagnostics.Add(new LoinDiagnostic
                        {
                            Severity = "info",
                            Sheet = sheetName,
                            Row = row,
                            Message = "Elemento sem TYPE/PredefinedType definido."
                        });
                    }

                    if (!element.Color.HasRgb && !string.IsNullOrWhiteSpace(element.Color.Note))
                    {
                        config.Diagnostics.Add(new LoinDiagnostic
                        {
                            Severity = "info",
                            Sheet = sheetName,
                            Row = row,
                            Message = element.Color.Note
                        });
                    }
                }
            }

            foreach (KeyValuePair<string, int> item in layerOccurrenceCount.Where(kv => kv.Value > 1))
            {
                config.Diagnostics.Add(new LoinDiagnostic
                {
                    Severity = "warning",
                    Message = $"Layer '{item.Key}' aparece em {item.Value} elementos. O mapeamento por layer usara a primeira ocorrencia."
                });
            }

            config.Layers = layers.Values.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
            config.PropertySetDefinitions = BuildPsets(projectFields, elementFields, physicalFields);
            config.Mappings = BuildMappings(firstElementByLayer.Values);

            return config;
        }

        // Código curto (3 letras) usado nos nomes dos Psets por disciplina.
        // Espelha LoinMapeamentoModels.PrefixoDisciplina — duplicado aqui para
        // não tornar aquela rotina pública (escopo mais restrito).
        // Público porque LoinCivil3DApplier e LoinExportacaoSolidosCorredores
        // precisam derivar o mesmo código a partir do nome da disciplina/aba
        // ao escrever/anexar Psets.
        public static string CodigoDisciplina(string disciplinaOuAba)
        {
            string d = (disciplinaOuAba ?? string.Empty).ToUpperInvariant();
            if (d.StartsWith("TOPO"))   return "TOP";
            if (d.StartsWith("TERRA"))  return "TER";
            if (d.StartsWith("PAVIM"))  return "PAV";
            if (d.StartsWith("DREN"))   return "DRE";
            if (d.StartsWith("OBRAS"))  return "OBC";
            if (d.StartsWith("SINAL"))  return "SIN";
            if (d.StartsWith("PAISAG")) return "PAI";
            if (d.StartsWith("OAE"))    return "OAE";
            if (d.StartsWith("ILUM"))   return "ILU";

            // Fallback: 3 primeiras letras do nome (sem espaços/acentos)
            string letras = new string((disciplinaOuAba ?? "L")
                .Where(char.IsLetter).ToArray());
            return letras.Length >= 3
                ? letras.Substring(0, 3).ToUpperInvariant()
                : "L";
        }

        private static int FindHeaderRow(IXLWorksheet ws)
        {
            int lastRow = Math.Min(ws.LastRowUsed()?.RowNumber() ?? 15, 15);
            int lastColumn = Math.Max(ws.LastColumnUsed()?.ColumnNumber() ?? 0, 36);

            for (int row = 1; row <= lastRow; row++)
            {
                string first = NormalizeKey(CellText(ws, row, 1));
                if (!first.Contains("ELEMENTO"))
                    continue;

                bool hasIfc = false;
                bool hasLayer = false;
                bool hasColor = false;

                for (int col = 1; col <= lastColumn; col++)
                {
                    string header = NormalizeKey(CellText(ws, row, col));
                    if (header is "IFC" or "CLASSE IFC")
                        hasIfc = true;
                    if (header == "LAYER")
                        hasLayer = true;
                    if (header == "COR")
                        hasColor = true;
                }

                if (hasIfc && hasLayer && hasColor)
                    return row;
            }

            return 0;
        }

        private static void CollectProjectFields(IXLWorksheet ws, Dictionary<string, string> projectFields)
        {
            for (int col = 1; col <= 36; col++)
            {
                string value = CellText(ws, 2, col);
                string key = NormalizeKey(value);
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                if (key.StartsWith("A - DADOS", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!projectFields.ContainsKey(key))
                    projectFields[key] = value;
            }
        }

        private static Dictionary<int, string> BuildHeaders(IXLWorksheet ws, int headerRow, int lastColumn)
        {
            Dictionary<int, string> headers = new();
            for (int col = 1; col <= lastColumn; col++)
            {
                string header = Clean(CellText(ws, headerRow, col));
                if (!string.IsNullOrWhiteSpace(header))
                    headers[col] = header;
            }

            return headers;
        }

        private static void CollectPsetFields(
            Dictionary<int, string> headers,
            int startColumn,
            int endColumn,
            Dictionary<string, LoinPsetProperty> target)
        {
            foreach (KeyValuePair<int, string> header in headers)
            {
                if (header.Key < startColumn || header.Key > endColumn)
                    continue;

                string key = NormalizeKey(header.Value);
                if (string.IsNullOrWhiteSpace(key) || target.ContainsKey(key))
                    continue;

                target[key] = new LoinPsetProperty
                {
                    Name = header.Value,
                    Description = header.Value,
                    DataType = "Text",
                    SourceColumn = ColumnName(header.Key)
                };
            }
        }

        private static LoinElementDefinition BuildElement(
            IXLWorksheet ws,
            string sheetName,
            int row,
            Dictionary<int, string> headers,
            int lastColumn)
        {
            LoinElementDefinition element = new LoinElementDefinition
            {
                Discipline = sheetName,
                SourceSheet = sheetName,
                SourceRow = row,
                Element = CellText(ws, row, 1),
                IfcClass = CellText(ws, row, 2),
                PredefinedType = CellText(ws, row, 3),
                ClassificationCode = CellText(ws, row, 4),
                Layer = CellText(ws, row, 5),
                Color = ParseColor(CellText(ws, row, 6))
            };

            for (int col = 1; col <= lastColumn; col++)
            {
                if (!headers.TryGetValue(col, out string header))
                    continue;

                string value = CellText(ws, row, col);
                if (!string.IsNullOrWhiteSpace(value))
                    element.RowValues[header] = value;

                string marker = NormalizeKey(value);
                if (marker == "S")
                {
                    if (col >= 7 && col <= 18)
                        element.RequiredElementProperties.Add(header);
                    else if (col >= 19)
                        element.RequiredPhysicalProperties.Add(header);
                }
                else if (marker is "N/A" or "NA")
                {
                    element.NotApplicableProperties.Add(header);
                }
            }

            return element;
        }

        private static void AddLayer(Dictionary<string, LoinLayerDefinition> layers, LoinElementDefinition element)
        {
            if (!layers.TryGetValue(element.Layer, out LoinLayerDefinition layer))
            {
                layer = new LoinLayerDefinition
                {
                    Name = element.Layer,
                    Color = element.Color
                };
                layers[element.Layer] = layer;
            }
            else if (!layer.Color.HasRgb && element.Color.HasRgb)
            {
                layer.Color = element.Color;
            }

            if (!layer.Disciplines.Contains(element.Discipline, StringComparer.OrdinalIgnoreCase))
                layer.Disciplines.Add(element.Discipline);

            if (!layer.Elements.Contains(element.Element, StringComparer.OrdinalIgnoreCase))
                layer.Elements.Add(element.Element);
        }

        // Produz a coleção de PSets a serem materializados no DWG.
        //
        // Estrutura (refatorada para per-disciplina em Pset_B/C/Requisitos):
        //   1× Pset_A             (Dados de Projeto — transversal, dados do projeto inteiro)
        //   1× Pset_D             (Layer IFC + Classificação — metadado por-elemento)
        //   1× IfcObject Props    (IFC override fields — transversal)
        //   N× Pset_B_<CODE>      (campos de elemento, um Pset por disciplina contribuinte)
        //   1× Pset_C unificado   (props físicas — único para todas as disciplinas;
        //                          nome = PsetCUnifiedName, esperado por APLICARPSETTODOS)
        //   N× Pset_Requisitos_<CODE>  (estrutura fixa, um Pset por disciplina)
        //
        // CODE é o código curto (TER/PAV/DRE/...) derivado do nome da aba via
        // CodigoDisciplina(). Se uma disciplina tem fields de elemento mas não
        // de físicas (ou vice-versa), só o Pset correspondente é gerado para ela.
        // Pset_Requisitos_<CODE> é sempre gerado para cada disciplina com aba
        // processada (estrutura fixa; valores variam por elemento na aplicação).
        // Emite EXATAMENTE 4 PSets unificados — Pset_A, Pset_B, Pset_C, Pset_D.
        // Decisão Petrobras: ABCD são suficientes. Não emite Pset_Requisitos
        // (em nenhuma variante) nem IfcObject Properties — a sobreposição
        // de classe IFC pode ser feita via Pset_D (IFC_CLASS / PREDEFINED_TYPE)
        // se o IFC Export Extension for configurado para lê-los.
        private static List<LoinPsetDefinition> BuildPsets(
            Dictionary<string, string> projectFields,
            Dictionary<string, LoinPsetProperty> elementFields,
            Dictionary<string, LoinPsetProperty> physicalFields)
        {
            List<LoinPsetDefinition> psets = new();

            psets.Add(new LoinPsetDefinition
            {
                Name = LoinCivil3DApplier.PsetAName,
                Description = "Dados gerais do projeto conforme LOIN.",
                Group = "A",
                Properties = projectFields.Values
                    .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                    .Select(v => new LoinPsetProperty { Name = v, Description = v, DataType = "Text", SourceColumn = "row 2" })
                    .ToList()
            });

            psets.Add(new LoinPsetDefinition
            {
                Name = LoinCivil3DApplier.PsetBName,
                Description = "Informacoes dos elementos (grupo B da LOIN, unificado entre disciplinas).",
                Group = "B",
                Properties = elementFields.Values
                    .OrderBy(p => p.SourceColumn, StringComparer.OrdinalIgnoreCase).ToList()
            });

            psets.Add(new LoinPsetDefinition
            {
                Name = LoinCivil3DApplier.PsetCName,
                Description = "Propriedades físicas dos objetos e elementos (unificado entre disciplinas).",
                Group = "C",
                Properties = physicalFields.Values
                    .OrderBy(p => p.SourceColumn, StringComparer.OrdinalIgnoreCase).ToList()
            });

            psets.Add(new LoinPsetDefinition
            {
                Name = LoinCivil3DApplier.PsetDName,
                Description = "Informacoes de layer, classificacao e IFC conforme LOIN.",
                Group = "D",
                Properties = new List<LoinPsetProperty>
                {
                    Prop("DISCIPLINA"),
                    Prop("ELEMENTO"),
                    Prop("IFC_CLASS"),
                    Prop("PREDEFINED_TYPE"),
                    Prop("CLASSIFICATION_CODE"),
                    Prop("LAYER"),
                    Prop("COLOR_RAW"),
                    Prop("COLOR_RGB"),
                    Prop("Pset_SOURCE_SHEET"),
                    Prop("Pset_SOURCE_ROW")
                }
            });

            return psets;
        }

        private static List<IfcMappingRule> BuildMappings(IEnumerable<LoinElementDefinition> elements)
        {
            List<IfcMappingRule> mappings = new();

            foreach (LoinElementDefinition element in elements
                .Where(e => !string.IsNullOrWhiteSpace(e.Layer))
                .OrderBy(e => e.Layer, StringComparer.OrdinalIgnoreCase))
            {
                mappings.Add(new IfcMappingRule
                {
                    LayerPattern = "^" + Regex.Escape(element.Layer) + "$",
                    IfcClass = element.IfcClass,
                    PredefinedType = element.PredefinedType,
                    ObjectType = element.Element,
                    NameSourcePriority = new List<string> { "SolidosParam:Nome", "SolidosParam:Codigo", "Layer", "Handle" },
                    TagSourcePriority = new List<string> { "SolidosParam:Codigo", "SolidosParam:Identificacao", "Handle" },
                    DescriptionSourcePriority = new List<string> { "Template:" + element.Element },
                    SystemSourcePriority = new List<string> { "SolidosParam:Sistema", "SolidosParam:Rede", "SolidosParam:NomeRede" },
                    SubsystemSourcePriority = new List<string> { "SolidosParam:Subsistema", "SolidosParam:Ramal" }
                });
            }

            return mappings;
        }

        private static LoinPsetProperty Prop(string name)
        {
            return new LoinPsetProperty
            {
                Name = name,
                Description = name,
                DataType = "Text"
            };
        }

        public static LoinColorDefinition ParseColor(string raw)
        {
            string value = Clean(raw);
            LoinColorDefinition color = new LoinColorDefinition
            {
                Raw = value,
                FallbackAci = 7
            };

            if (string.IsNullOrWhiteSpace(value))
            {
                color.Note = "Cor vazia na planilha; layer usara ACI 7 como fallback.";
                return color;
            }

            Match rgbMatch = Regex.Match(value, @"^\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*$");
            if (rgbMatch.Success)
            {
                int r = ClampColor(rgbMatch.Groups[1].Value);
                int g = ClampColor(rgbMatch.Groups[2].Value);
                int b = ClampColor(rgbMatch.Groups[3].Value);
                color.Red = r;
                color.Green = g;
                color.Blue = b;
                return color;
            }

            string key = NormalizeKey(value);
            Dictionary<string, (int R, int G, int B)> namedColors = new(StringComparer.OrdinalIgnoreCase)
            {
                ["YELLOW"] = (255, 255, 0),
                ["GREEN"] = (0, 255, 0),
                ["CYAN"] = (0, 255, 255),
                ["BLUE"] = (0, 0, 255),
                ["RED"] = (255, 0, 0),
                ["BLACK"] = (0, 0, 0),
                ["WHITE"] = (255, 255, 255),
                ["GRAY"] = (150, 150, 150),
                ["GREY"] = (150, 150, 150),
                ["ORANGE"] = (255, 127, 0),
                ["MAGENTA"] = (255, 0, 255)
            };

            if (namedColors.TryGetValue(key, out (int R, int G, int B) rgb))
            {
                color.Red = rgb.R;
                color.Green = rgb.G;
                color.Blue = rgb.B;
                return color;
            }

            if (key.Contains("COR DO ELEMENTO"))
            {
                color.Note = "Cor marcada como 'Cor do elemento'; layer usara ACI 7 como fallback.";
                return color;
            }

            color.Note = $"Cor '{value}' nao reconhecida; layer usara ACI 7 como fallback.";
            return color;
        }

        private static int ClampColor(string value)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                return 0;

            return Math.Max(0, Math.Min(255, parsed));
        }

        private static string CellText(IXLWorksheet ws, int row, int col)
        {
            try
            {
                return Clean(ws.Cell(row, col).GetString());
            }
            catch
            {
                return Clean(ws.Cell(row, col).Value.ToString());
            }
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty)
                .Replace("\r\n", " ")
                .Replace('\n', ' ')
                .Replace('\r', ' ')
                .Trim();
        }

        private static string NormalizeKey(string value)
        {
            string clean = Clean(value).Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder(clean.Length);

            foreach (char c in clean)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ")
                .Trim()
                .ToUpperInvariant();
        }

        private static string ColumnName(int columnNumber)
        {
            string name = string.Empty;
            int dividend = columnNumber;

            while (dividend > 0)
            {
                int modulo = (dividend - 1) % 26;
                name = Convert.ToChar('A' + modulo) + name;
                dividend = (dividend - modulo) / 26;
            }

            return name;
        }
    }
}
