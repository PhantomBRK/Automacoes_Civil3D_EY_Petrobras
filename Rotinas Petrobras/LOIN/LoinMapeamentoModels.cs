using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using OfficeOpenXml;

namespace AutomacoesCivil3D
{
    // VM para cada linha da tabela LOIN (ISO 7817)
    internal sealed class LoinLinha : ObservableObject
    {
        private string _id = "";
        private string _elemento = "";
        private string _disciplina = "";
        private string _fase = "";
        private string _log = "";
        private string _loi = "";
        private string _atributosObrigatorios = "";
        private string _atributosOpcionais = "";
        private string _observacao = "";
        private string _cor = "";
        private string _ifcClass = "";
        private string _predefinedType = "";
        // Chave única para correlacionar com LoinElementDefinition da rotina
        // de exportação (sheet+row da planilha LOIN original)
        private string _sourceSheet = "";
        private int    _sourceRow;

        public string Id
        {
            get => _id;
            set { SetProperty(ref _id, value); OnPropertyChanged(nameof(DisplayLabel)); }
        }

        public string Elemento
        {
            get => _elemento;
            set { SetProperty(ref _elemento, value); OnPropertyChanged(nameof(DisplayLabel)); }
        }

        public string Disciplina
        {
            get => _disciplina;
            set => SetProperty(ref _disciplina, value);
        }

        public string Fase
        {
            get => _fase;
            set { SetProperty(ref _fase, value); OnPropertyChanged(nameof(DisplayLabel)); }
        }

        public string LoG
        {
            get => _log;
            set { SetProperty(ref _log, value); OnPropertyChanged(nameof(DisplayLabel)); }
        }

        public string LoI
        {
            get => _loi;
            set { SetProperty(ref _loi, value); OnPropertyChanged(nameof(DisplayLabel)); }
        }

        public string AtributosObrigatorios
        {
            get => _atributosObrigatorios;
            set => SetProperty(ref _atributosObrigatorios, value);
        }

        public string AtributosOpcionais
        {
            get => _atributosOpcionais;
            set => SetProperty(ref _atributosOpcionais, value);
        }

        public string Observacao
        {
            get => _observacao;
            set => SetProperty(ref _observacao, value);
        }

        // Cor da matriz LOIN (coluna COR). Aceita RGB "255,255,0", nome "AMARELO",
        // hexa "#FFFF00" ou ACI "50". Consumida pelo LoinCodeSetStyleCorredores
        // para Shape / Link / Marker / Material Area Fill.
        public string Cor
        {
            get => _cor;
            set => SetProperty(ref _cor, value);
        }

        // Classe IFC do elemento (ex: "IfcDistributionChamberElement"). Coluna IFC
        // da matriz LOIN. Consumida pelo LoinIfcExportMappingLinker para preencher
        // IfcExportAs no IfcInfraExportMapping-IFC4X3_ADD2.xlsx do Civil 3D.
        public string IfcClass
        {
            get => _ifcClass;
            set => SetProperty(ref _ifcClass, value);
        }

        // PredefinedType IFC (ex: "MANHOLE", "CULVERT") ou USERDEFINED para custom.
        // Coluna TYPE da matriz LOIN. Concatenado em IfcExportAs como "IfcClass.Type".
        public string PredefinedType
        {
            get => _predefinedType;
            set => SetProperty(ref _predefinedType, value);
        }

        // Aba da planilha LOIN original (ex: "TOPOGRAFIA"). Vazio = entrada manual.
        public string SourceSheet
        {
            get => _sourceSheet;
            set => SetProperty(ref _sourceSheet, value);
        }

        // Número da linha (1-based) na aba SourceSheet. Zero = entrada manual.
        public int SourceRow
        {
            get => _sourceRow;
            set => SetProperty(ref _sourceRow, value);
        }

        // Rótulo exibido no ComboBox do grid de mapeamento
        public string DisplayLabel =>
            string.IsNullOrWhiteSpace(Id)
                ? "(sem Id)"
                : $"[{Id}] {Elemento}" +
                  (string.IsNullOrWhiteSpace(Fase) ? "" : $" — {Fase}") +
                  $" | LoG:{(string.IsNullOrWhiteSpace(LoG) ? "—" : LoG)}" +
                  $" LoI:{(string.IsNullOrWhiteSpace(LoI) ? "—" : LoI)}";
    }

    // VM para cada item do grid de mapeamento (camada/code → linha LOIN)
    internal sealed class LoinItemMapeamento : ObservableObject
    {
        private string _camada = "";
        private string _origem = "";
        private LoinLinha? _loinLinhaSelecionada;

        public string Camada
        {
            get => _camada;
            set => SetProperty(ref _camada, value);
        }

        public string Origem
        {
            get => _origem;
            set => SetProperty(ref _origem, value);
        }

        // Referência à coleção compartilhada — permite binding direto no DataGrid sem RelativeSource
        public ObservableCollection<LoinLinha>? LinhasDisponiveis { get; set; }

        public LoinLinha? LoinLinhaSelecionada
        {
            get => _loinLinhaSelecionada;
            set
            {
                SetProperty(ref _loinLinhaSelecionada, value);
                OnPropertyChanged(nameof(LoinLinhaId));
                OnPropertyChanged(nameof(LoGDisplay));
                OnPropertyChanged(nameof(LoIDisplay));
                OnPropertyChanged(nameof(Mapeado));
            }
        }

        public string LoinLinhaId  => _loinLinhaSelecionada?.Id  ?? "";
        public string LoGDisplay   => _loinLinhaSelecionada?.LoG ?? "—";
        public string LoIDisplay   => _loinLinhaSelecionada?.LoI ?? "—";
        public bool   Mapeado      => _loinLinhaSelecionada != null;
    }

    // -------- DTOs para serialização JSON --------

    internal sealed class LoinMapeamentoConfig
    {
        public string   Versao           { get; set; } = "1.0";
        public DateTime UltimaAlteracao  { get; set; } = DateTime.Now;
        public List<LoinLinhaDto>          TabelaLoin   { get; set; } = new();
        public List<LoinItemMapeamentoDto> Mapeamentos  { get; set; } = new();
    }

    internal sealed class LoinLinhaDto
    {
        public string Id                    { get; set; } = "";
        public string Elemento              { get; set; } = "";
        public string Disciplina            { get; set; } = "";
        public string Fase                  { get; set; } = "";
        public string LoG                   { get; set; } = "";
        public string LoI                   { get; set; } = "";
        public string AtributosObrigatorios { get; set; } = "";
        public string AtributosOpcionais    { get; set; } = "";
        public string Observacao            { get; set; } = "";
        // Cor da matriz LOIN (RGB / nome / hexa / ACI). Usada pelos estilos
        // Shape/Link/Marker do Code Set dos corredores. Vazia = ACI 7.
        public string Cor                   { get; set; } = "";
        // Classe IFC + PredefinedType da matriz LOIN. Usadas pelo
        // LoinIfcExportMappingLinker para preencher IfcExportAs no
        // IfcInfraExportMapping-*.xlsx do IFC Export Extension do Civil 3D.
        public string IfcClass              { get; set; } = "";
        public string PredefinedType        { get; set; } = "";
        // Coordenadas na planilha LOIN original — chave para correlacionar
        // com LoinElementDefinition durante a exportação de sólidos.
        public string SourceSheet           { get; set; } = "";
        public int    SourceRow             { get; set; }
    }

    internal sealed class LoinItemMapeamentoDto
    {
        public string Camada       { get; set; } = "";
        public string Origem       { get; set; } = "";
        public string LoinLinhaId  { get; set; } = "";
    }

    // Resultado da importação Excel/CSV: linhas LOIN + mapeamentos auto-detectados
    internal sealed class LoinImportResult
    {
        public List<LoinLinhaDto>          Linhas       { get; set; } = new();
        public List<LoinItemMapeamentoDto> Mapeamentos  { get; set; } = new();
    }

    // -------- Serviço de persistência / importação --------

    internal static class LoinMapeamentoService
    {
        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string ResolverCaminhoConfig(string? caminhoDrawing = null)
        {
            string pasta = string.IsNullOrEmpty(caminhoDrawing)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                               "Autodesk", "ApplicationPlugins", "AutomacoesPetrobras.bundle")
                : (Path.GetDirectoryName(caminhoDrawing) ?? ".");

            Directory.CreateDirectory(pasta);
            return Path.Combine(pasta, "loin_mapeamento.json");
        }

        public static LoinMapeamentoConfig Carregar(string caminho)
        {
            if (!File.Exists(caminho)) return new LoinMapeamentoConfig();
            string json = File.ReadAllText(caminho, Encoding.UTF8);
            return JsonSerializer.Deserialize<LoinMapeamentoConfig>(json, _jsonOpts) ?? new LoinMapeamentoConfig();
        }

        public static void Salvar(string caminho, LoinMapeamentoConfig config)
        {
            config.UltimaAlteracao = DateTime.Now;
            File.WriteAllText(caminho, JsonSerializer.Serialize(config, _jsonOpts), Encoding.UTF8);
        }

        // Importa de CSV (separador ; ou ,).
        // Cabeçalho esperado: Id;Elemento;Disciplina;Fase;LoG;LoI;AtributosObrigatorios;AtributosOpcionais;Observacao
        public static LoinImportResult ImportarCsv(string caminho)
        {
            var resultado = new LoinImportResult();
            string[] linhas = File.ReadAllLines(caminho, Encoding.UTF8);
            if (linhas.Length < 2) return resultado;

            char sep = linhas[0].Contains(';') ? ';' : ',';
            string[] cab = linhas[0].Split(sep);

            int iId = Col(cab, "Id", "ID");
            int iEl = Col(cab, "Elemento", "Element");
            int iDi = Col(cab, "Disciplina");
            int iFa = Col(cab, "Fase", "Phase");
            int iG  = Col(cab, "LoG", "LOG", "Geometria");
            int iI  = Col(cab, "LoI", "LOI", "Informacao");
            int iAO = Col(cab, "AtributosObrigatorios", "Obrigatorios", "Required");
            int iAP = Col(cab, "AtributosOpcionais", "Opcionais", "Optional");
            int iOb = Col(cab, "Observacao", "Obs", "Notes");

            for (int r = 1; r < linhas.Length; r++)
            {
                if (string.IsNullOrWhiteSpace(linhas[r])) continue;
                string[] c = linhas[r].Split(sep);
                resultado.Linhas.Add(new LoinLinhaDto
                {
                    Id = Get(c, iId), Elemento = Get(c, iEl), Disciplina = Get(c, iDi),
                    Fase = Get(c, iFa), LoG = Get(c, iG), LoI = Get(c, iI),
                    AtributosObrigatorios = Get(c, iAO), AtributosOpcionais = Get(c, iAP),
                    Observacao = Get(c, iOb)
                });
            }
            return resultado;
        }

        // Importa de Excel. Suporta dois formatos:
        //  1) Genérico: cabeçalho simples na linha 1 (Id;Elemento;Disciplina;Fase;LoG;LoI;...)
        //  2) Matriz LOIN Petrobras: múltiplas abas (disciplinas), cabeçalho técnico
        //     numa linha que contém "ELEMENTO OU STATUS" e "LAYER" — auto-cria mapeamentos
        public static LoinImportResult ImportarExcel(string caminho)
        {
            ExcelPackage.License.SetNonCommercialPersonal("Gleison Bruno da Costa");
            var resultado = new LoinImportResult();
            using var pkg = new ExcelPackage(new FileInfo(caminho));

            foreach (var ws in pkg.Workbook.Worksheets)
            {
                if (ws?.Dimension == null) continue;

                int totalRows = ws.Dimension.End.Row;
                int totalCols = ws.Dimension.End.Column;

                // Tenta localizar uma linha header que tenha "ELEMENTO" (formato matriz)
                int headerRow = LocalizarHeaderMatriz(ws, totalRows, totalCols);
                if (headerRow > 0)
                {
                    ImportarAbaMatriz(ws, headerRow, totalRows, totalCols, resultado);
                }
                else
                {
                    // Cai no formato genérico (cabeçalho linha 1)
                    ImportarAbaGenerica(ws, totalRows, totalCols, resultado);
                }
            }

            return resultado;
        }

        // Localiza a linha que tem "ELEMENTO OU STATUS" (ou similar) na primeira coluna —
        // tipicamente entre as linhas 1 e 12.
        private static int LocalizarHeaderMatriz(ExcelWorksheet ws, int totalRows, int totalCols)
        {
            int limite = Math.Min(15, totalRows);
            for (int r = 1; r <= limite; r++)
            {
                for (int c = 1; c <= Math.Min(6, totalCols); c++)
                {
                    string txt = (ws.Cells[r, c].Text ?? "").Trim().ToUpperInvariant();
                    if (txt.StartsWith("ELEMENTO") || txt.Contains("ELEMENTO OU STATUS"))
                        return r;
                }
            }
            return -1;
        }

        private static void ImportarAbaMatriz(
            ExcelWorksheet ws, int headerRow, int totalRows, int totalCols, LoinImportResult resultado)
        {
            // Indexa o cabeçalho técnico
            string[] cab = new string[totalCols + 1];
            for (int c = 1; c <= totalCols; c++)
                cab[c] = (ws.Cells[headerRow, c].Text ?? "").Trim();

            int iElem  = Col(cab, "ELEMENTO OU STATUS", "ELEMENTO");
            int iIfc   = Col(cab, "IFC");
            int iType  = Col(cab, "TYPE", "PREDEFINEDTYPE");
            int iLayer = Col(cab, "LAYER", "CAMADA");
            int iCor   = Col(cab, "COR", "COLOR");

            // Disciplina = nome da aba
            string disciplina = (ws.Name ?? "").Trim();
            string prefixo    = PrefixoDisciplina(disciplina);

            // Mantém-se contador local pra gerar Ids únicos
            int seq = resultado.Linhas.Count(l =>
                string.Equals(l.Disciplina, disciplina, StringComparison.OrdinalIgnoreCase));

            // Colunas que não são identificadoras viram potenciais "Atributos"
            // (qualquer coluna cujo cabeçalho exista e que não seja Elemento/IFC/Type/Layer/Cor)
            var colunasAtributo = new List<(int idx, string nome)>();
            for (int c = 1; c <= totalCols; c++)
            {
                if (c == iElem || c == iIfc || c == iType || c == iLayer || c == iCor) continue;
                if (string.IsNullOrWhiteSpace(cab[c])) continue;
                colunasAtributo.Add((c, cab[c]));
            }

            int linhasVaziasSeguidas = 0;
            for (int r = headerRow + 1; r <= totalRows; r++)
            {
                string elemento = iElem > 0 ? (ws.Cells[r, iElem].Text ?? "").Trim() : "";
                if (string.IsNullOrWhiteSpace(elemento))
                {
                    if (++linhasVaziasSeguidas >= 3) break;
                    continue;
                }
                linhasVaziasSeguidas = 0;

                // Pula linhas que aparentam ser sub-cabeçalho (sem IFC nem LAYER)
                string ifc   = iIfc   > 0 ? (ws.Cells[r, iIfc].Text   ?? "").Trim() : "";
                string type  = iType  > 0 ? (ws.Cells[r, iType].Text  ?? "").Trim() : "";
                string layer = iLayer > 0 ? (ws.Cells[r, iLayer].Text ?? "").Trim() : "";
                string cor   = iCor   > 0 ? (ws.Cells[r, iCor].Text   ?? "").Trim() : "";

                seq++;
                string id = $"{prefixo}-{seq:D3}";

                // Compila atributos obrigatórios (células = "S") e opcionais ("O")
                var obrig = new List<string>();
                var opc   = new List<string>();
                foreach (var (cidx, nome) in colunasAtributo)
                {
                    string v = (ws.Cells[r, cidx].Text ?? "").Trim().ToUpperInvariant();
                    if (v == "S" || v == "SIM" || v == "X") obrig.Add(nome);
                    else if (v == "O" || v == "OPCIONAL")   opc.Add(nome);
                }

                var linha = new LoinLinhaDto
                {
                    Id                    = id,
                    Elemento              = elemento,
                    Disciplina            = disciplina,
                    Fase                  = "",
                    LoG                   = "",
                    LoI                   = "",
                    AtributosObrigatorios = string.Join(", ", obrig),
                    AtributosOpcionais    = string.Join(", ", opc),
                    Observacao            = MontarObservacao(ifc, type, layer, cor),
                    Cor                   = cor,
                    IfcClass              = ifc,
                    PredefinedType        = type,
                    // Coordenadas originais — usado pela rotina de exportação para
                    // localizar o LoinElementDefinition correspondente
                    SourceSheet           = ws.Name ?? "",
                    SourceRow             = r
                };
                resultado.Linhas.Add(linha);

                // Auto-mapeamento: se a planilha define o LAYER, já cria a associação
                if (!string.IsNullOrWhiteSpace(layer))
                {
                    resultado.Mapeamentos.Add(new LoinItemMapeamentoDto
                    {
                        Camada      = layer,
                        Origem      = "LOIN-XLSX",
                        LoinLinhaId = id
                    });
                }
            }
        }

        private static void ImportarAbaGenerica(
            ExcelWorksheet ws, int totalRows, int totalCols, LoinImportResult resultado)
        {
            string[] cab = new string[totalCols + 1];
            for (int c = 1; c <= totalCols; c++)
                cab[c] = (ws.Cells[1, c].Text ?? "").Trim();

            int iId = Col(cab, "Id", "ID");
            int iEl = Col(cab, "Elemento", "Element");
            int iDi = Col(cab, "Disciplina");
            int iFa = Col(cab, "Fase", "Phase");
            int iG  = Col(cab, "LoG", "LOG", "Geometria");
            int iI  = Col(cab, "LoI", "LOI", "Informacao");
            int iAO = Col(cab, "AtributosObrigatorios", "Obrigatorios", "Required");
            int iAP = Col(cab, "AtributosOpcionais", "Opcionais", "Optional");
            int iOb = Col(cab, "Observacao", "Obs", "Notes");

            for (int r = 2; r <= totalRows; r++)
            {
                string id = iId > 0 ? (ws.Cells[r, iId].Text ?? "").Trim() : "";
                if (string.IsNullOrWhiteSpace(id)) continue;
                resultado.Linhas.Add(new LoinLinhaDto
                {
                    Id                    = id,
                    Elemento              = iEl > 0 ? (ws.Cells[r, iEl].Text ?? "").Trim() : "",
                    Disciplina            = iDi > 0 ? (ws.Cells[r, iDi].Text ?? "").Trim() : "",
                    Fase                  = iFa > 0 ? (ws.Cells[r, iFa].Text ?? "").Trim() : "",
                    LoG                   = iG  > 0 ? (ws.Cells[r, iG ].Text ?? "").Trim() : "",
                    LoI                   = iI  > 0 ? (ws.Cells[r, iI ].Text ?? "").Trim() : "",
                    AtributosObrigatorios = iAO > 0 ? (ws.Cells[r, iAO].Text ?? "").Trim() : "",
                    AtributosOpcionais    = iAP > 0 ? (ws.Cells[r, iAP].Text ?? "").Trim() : "",
                    Observacao            = iOb > 0 ? (ws.Cells[r, iOb].Text ?? "").Trim() : ""
                });
            }
        }

        private static string MontarObservacao(string ifc, string type, string layer, string cor)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ifc))   parts.Add($"IFC={ifc}");
            if (!string.IsNullOrWhiteSpace(type))  parts.Add($"TYPE={type}");
            if (!string.IsNullOrWhiteSpace(layer)) parts.Add($"LAYER={layer}");
            if (!string.IsNullOrWhiteSpace(cor))   parts.Add($"COR={cor}");
            return string.Join("; ", parts);
        }

        private static string PrefixoDisciplina(string disciplina)
        {
            string d = (disciplina ?? "").ToUpperInvariant();
            if (d.StartsWith("TOPO"))    return "TOP";
            if (d.StartsWith("TERRA"))   return "TER";
            if (d.StartsWith("PAVIM"))   return "PAV";
            if (d.StartsWith("DREN"))    return "DRE";
            if (d.StartsWith("OBRAS"))   return "OBC";
            if (d.StartsWith("SINAL"))   return "SIN";
            if (d.StartsWith("PAISAG"))  return "PAI";
            if (d.StartsWith("OAE"))     return "OAE";
            if (d.StartsWith("ILUM"))    return "ILU";
            // fallback: 3 primeiras letras
            var letras = new string((disciplina ?? "L").Where(char.IsLetter).ToArray());
            return letras.Length >= 3 ? letras.Substring(0, 3).ToUpperInvariant() : "L";
        }

        public static void ExportarCsvMapeamento(string caminho, IEnumerable<LoinItemMapeamento> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Camada;Origem;LoinLinhaId;Elemento;Disciplina;Fase;LoG;LoI;AtributosObrigatorios");
            foreach (var item in items)
            {
                var l = item.LoinLinhaSelecionada;
                sb.AppendLine(
                    $"{Esc(item.Camada)};{item.Origem};{item.LoinLinhaId};" +
                    $"{Esc(l?.Elemento)};{Esc(l?.Disciplina)};{Esc(l?.Fase)};" +
                    $"{l?.LoG ?? ""};{l?.LoI ?? ""};{Esc(l?.AtributosObrigatorios)}");
            }
            File.WriteAllText(caminho, sb.ToString(), Encoding.UTF8);
        }

        // -------- helpers privados --------

        private static int Col(string[] arr, params string[] names)
        {
            // Tolerante a entradas null (índice 0 dos arrays "1-based" usados no Excel)
            foreach (string n in names)
                for (int i = 0; i < arr.Length; i++)
                {
                    string s = (arr[i] ?? "").Trim();
                    if (string.Equals(s, n, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            return -1;
        }

        private static string Get(string[] cols, int idx)
            => idx >= 0 && idx < cols.Length ? cols[idx].Trim() : "";

        private static string Esc(string? s)
            => (s ?? "").Contains(';') ? $"\"{s}\"" : (s ?? "");
    }
}
