using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AutomacoesCivil3D;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using OfficeOpenXml;
using SOLIDOS;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace RotinasPetrobras.Quantitativos
{
    /// <summary>
    /// SOL_QUANT_TUBOS: extrai todos os tubos PETROBRAS do DWG ativo, agrupa por rede
    /// (RootId -> Name) e gera um único XLSX com uma aba por rede.
    /// As fórmulas de cálculo já são executadas pelo construtor SolGravityLinear
    /// (sequence "QTO TUBULACAO"); essa rotina só LÊ os valores e GRAVA estáticos.
    /// </summary>
    public partial class SolQuantTubos
    {
        // Nome do arquivo local copiado pra pasta do DWG.
        private const string TEMPLATE_NOME_LOCAL =
            "PLANILHA DE CALCULO DE QUANTITATIVOS LEANDRO COM OTIMIZAÇÃO DA ABA RESUMO.xlsx";

        // Template-fonte, instalado JUNTO do plugin (Resources\Quantitativos do bundle).
        // Copiado pra pasta do DWG na 1ª execução (não mexe no original).
        private static string TEMPLATE_ORIGEM =>
            BundlePaths.Resource("Quantitativos", TEMPLATE_NOME_LOCAL);

        // 3 sheets-modelo de tubo. Cada uma tem título próprio ("DRENAGEM PLUVIAL LIMPA",
        // "DRENAGEM CONTAMINADA", "DRENAGEM OLEOSA"). A escolha é feita por substring no
        // nome da rede; fallback TUB_Oleo. As 3 são apagadas depois de gerar as cópias.
        private static readonly string[] SHEETS_TUBO_TEMPLATE = { "TUB_Pluv", "TUB_Cont", "TUB_Oleo" };
        private const string SHEET_MODELO_DEFAULT = "TUB_Oleo";

        // Primeira linha de dados na planilha (linhas 1-5 = header).
        private const int LINHA_DADOS_INICIO = 6;

        [CommandMethod("SOL_QUANT_TUBOS")]
        public void Executar()
        {
            var doc = Manager.DocCad;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            try
            {
                string dwgPath = doc.Name;
                if (string.IsNullOrWhiteSpace(dwgPath) || !File.Exists(dwgPath))
                {
                    ed.WriteMessage("\n[SOL_QUANT_TUBOS] Salve o DWG antes de rodar este comando.");
                    return;
                }
                string dwgNome = Path.GetFileNameWithoutExtension(dwgPath);

                // 0) Demolição/recomposição de piso? (greenfield = Não -> não preenche col AB)
                bool incluirDemolicao = PerguntarDemolicao(ed);
                ed.WriteMessage($"\n[SOL_QUANT_TUBOS] Demolição/recomposição: {(incluirDemolicao ? "SIM" : "NÃO")}");

                // 1) Template: garante cópia local na pasta do DWG
                string dwgDir = Path.GetDirectoryName(dwgPath) ?? Environment.CurrentDirectory;
                string template = GarantirTemplateLocal(dwgDir, ed);
                if (template == null) return;
                ed.WriteMessage($"\n[SOL_QUANT_TUBOS] Template: {template}");

                // 2) Coletar tubos agrupados por rede
                ed.WriteMessage("\n[SOL_QUANT_TUBOS] Lendo tubos...");
                var tubosPorRede = new Dictionary<string, List<TuboQuantData>>(StringComparer.OrdinalIgnoreCase);
                int totalTubos = 0, totalIgnorados = 0, totalFantasmas = 0;

                using (doc.LockDocument())
                using (var t = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)t.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)t.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId id in ms)
                    {
                        if (!EhTuboPetrobras(id))
                        {
                            continue;
                        }

                        var dados = LerTubo(id, dwgNome);
                        if (dados == null)
                        {
                            totalIgnorados++;
                            continue;
                        }

                        // Descarta tubos-fantasma: entidades existem no model space mas não
                        // foram construídas (Axis3d.Length=0 -> Comprimento=0) nem conectadas
                        // (InPart/OutPart nulos -> Trecho="? a ?"). Aparecem por copia-cola
                        // sem reconectar, criação cancelada, etc.
                        if (EhFantasma(dados))
                        {
                            totalFantasmas++;
                            continue;
                        }
                        totalTubos++;

                        if (!tubosPorRede.TryGetValue(dados.NomeRede, out var lista))
                        {
                            lista = new List<TuboQuantData>();
                            tubosPorRede[dados.NomeRede] = lista;
                        }
                        lista.Add(dados);
                    }
                    t.Commit();
                }

                if (tubosPorRede.Count == 0)
                {
                    ed.WriteMessage(
                        $"\n[SOL_QUANT_TUBOS] Nenhum tubo PETROBRAS válido encontrado no DWG."
                        + (totalFantasmas > 0 ? $" ({totalFantasmas} tubo(s)-fantasma descartado(s).)" : ""));
                    return;
                }
                ed.WriteMessage(
                    $"\n[SOL_QUANT_TUBOS] {totalTubos} tubos em {tubosPorRede.Count} rede(s).");
                if (totalFantasmas > 0)
                    ed.WriteMessage($"\n[SOL_QUANT_TUBOS] {totalFantasmas} tubo(s)-fantasma descartado(s) (Comprimento=0 ou desconectados).");
                if (totalIgnorados > 0)
                    ed.WriteMessage($"\n[SOL_QUANT_TUBOS] {totalIgnorados} ignorado(s) por erro de leitura.");

                // Avisa tubos com Catálogo desatualizado vs geometria (cadastro a corrigir no DWG)
                foreach (var dv in tubosPorRede.Values.SelectMany(l => l).Where(CatalogoDivergeDaGeometria))
                    ed.WriteMessage($"\n[SOL_QUANT_TUBOS][AVISO] Catálogo desatualizado em '{dv.Trecho}': "
                        + $"Catálogo={dv.Catalogo} vs DN real (geometria)={dv.DnMm}mm (usando geometria).");

                // 3) Gerar XLSX
                string outPath = Path.Combine(dwgDir, $"{dwgNome}_QUANT_TUBOS.xlsx");

                GerarXlsx(template, outPath, tubosPorRede, incluirDemolicao, ed);
                ed.WriteMessage($"\n[SOL_QUANT_TUBOS] OK -> {outPath}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[SOL_QUANT_TUBOS] ERRO: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // ----------------------------------------------------------------------
        // Identificação de tubo
        // ----------------------------------------------------------------------

        /// <summary>
        /// Tubo PETROBRAS = dispositivo LINEAR (não pontual) da família PETROBRAS.
        /// Filtro em 2 camadas:
        /// 1. RXClass: rejeita classes que contém "Point" (conexões pontuais — curvas,
        ///    junções, tês, caixas).
        /// 2. Propriedades: tem que ter StartInvertElevation E EndInvertElevation
        ///    (exclusivas de dispositivo linear — pontuais têm uma só elevação) +
        ///    Catalogo + Diametro (família PETROBRAS).
        /// </summary>
        private static bool EhTuboPetrobras(ObjectId id)
        {
            if (id.IsNull) return false;

            // Camada 1: classe ARX — rejeita dispositivos pontuais
            var cls = id.ObjectClass;
            if (cls != null)
            {
                string clsName = cls.Name ?? string.Empty;
                if (clsName.IndexOf("Point", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false; // Conexão pontual: curva, junção, tê, caixa, etc.
            }

            // Camada 2: propriedades exclusivas de dispositivo linear PETROBRAS
            try
            {
                // Linear tem DUAS elevações de invert (Start + End); pontual tem só uma.
                var tStart = SolidosAPI.GetPropertyType(id, "StartInvertElevation");
                if (tStart == null) return false;
                var tEnd = SolidosAPI.GetPropertyType(id, "EndInvertElevation");
                if (tEnd == null) return false;
                // PETROBRAS = Catalogo (DN como string) + Diametro
                var tCatalogo = SolidosAPI.GetPropertyType(id, "Catalogo");
                if (tCatalogo == null) return false;
                var tDiametro = SolidosAPI.GetPropertyType(id, "Diametro");
                if (tDiametro == null) return false;

                // Camada 3: rejeita válvulas. Foram criadas a partir do template de tubo,
                // então passam nos filtros 1+2. Classifica pelo SubType (fonte canônica),
                // com o Name como rede de segurança secundária.
                string subType = LerString(id, "SubType");
                if (!string.IsNullOrEmpty(subType)
                    && subType.IndexOf("VALV", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
                string nome = LerString(id, "Name");
                if (!string.IsNullOrEmpty(nome)
                    && nome.IndexOf("VALV", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Tubo-fantasma = passou no filtro EhTuboPetrobras (tem as propriedades definidas
        /// no construtor) mas a instância não foi construída/conectada. Sintomas:
        /// - Comprimento = 0 (Axis3d.Length sem geometria)
        /// - InPart e OutPart ambos não-resolvidos (Trecho = "? a ?")
        /// Aparecem por copia-cola sem reconectar, criação cancelada, etc.
        /// </summary>
        private static bool EhFantasma(TuboQuantData d)
        {
            if (d.Comprimento <= 0.001) return true; // Axis3d sem geometria
            // Trecho "? a ?" = InPart e OutPart ambos nulos -> tubo solto
            if (d.Trecho == "? a ?") return true;
            return false;
        }

        /// <summary>
        /// Variante "leve" do filtro de fantasma: lê apenas Comprimento + InPart/OutPart
        /// (sem montar o DTO completo). Usado por SOL_LISTAR_TUBOS_FANTASMAS.
        /// </summary>
        private static bool EhTuboFantasmaPorId(ObjectId id)
        {
            if (!EhTuboPetrobras(id)) return false;

            // Check 1: sem geometria
            if (LerDouble(id, "Comprimento") <= 0.001) return true;

            // Check 2: ambas as pontas desconectadas
            bool inNull = ObjetoEhNuloOuVazio(LerObj(id, "InPart"));
            bool outNull = ObjetoEhNuloOuVazio(LerObj(id, "OutPart"));
            return inNull && outNull;
        }

        private static bool ObjetoEhNuloOuVazio(object v)
        {
            if (v == null) return true;
            if (v is ObjectId oid) return oid.IsNull;
            string s = v.ToString();
            return string.IsNullOrWhiteSpace(s) || s == "(0)" || s == "0";
        }

        // ----------------------------------------------------------------------
        // Comando auxiliar: selecionar todos os tubos-fantasma p/ apagar manualmente
        // ----------------------------------------------------------------------

        /// <summary>
        /// SOL_LISTAR_TUBOS_FANTASMAS: marca todos os tubos-fantasma como seleção
        /// implícita (PICKFIRST). Depois rode ERASE/APAGAR ou pressione Delete pra apagar.
        /// </summary>
        [CommandMethod("SOL_LISTAR_TUBOS_FANTASMAS")]
        public void ListarFantasmas()
        {
            var doc = Manager.DocCad;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            try
            {
                var fantasmas = new List<ObjectId>();

                using (doc.LockDocument())
                using (var t = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)t.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)t.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId id in ms)
                    {
                        if (EhTuboFantasmaPorId(id))
                            fantasmas.Add(id);
                    }
                    t.Commit();
                }

                if (fantasmas.Count == 0)
                {
                    ed.WriteMessage("\n[SOL_LISTAR_TUBOS_FANTASMAS] Nenhum tubo-fantasma encontrado.");
                    return;
                }

                ed.SetImpliedSelection(fantasmas.ToArray());
                ed.WriteMessage(
                    $"\n[SOL_LISTAR_TUBOS_FANTASMAS] {fantasmas.Count} tubo(s)-fantasma selecionado(s).");
                ed.WriteMessage(
                    "\n  -> Pressione DELETE (ou rode ERASE) para apagar a seleção.");
                ed.WriteMessage(
                    "\n  -> Confira no viewport antes; se algum estiver errado, ESC limpa a seleção.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[SOL_LISTAR_TUBOS_FANTASMAS] ERRO: {ex.Message}");
            }
        }

        // ----------------------------------------------------------------------
        // Leitura de um tubo
        // ----------------------------------------------------------------------

        private static TuboQuantData LerTubo(ObjectId id, string docRef)
        {
            try
            {
                var d = new TuboQuantData
                {
                    DocReferencia       = docRef,
                    // Classificação: SubType + código do Catálogo (NUNCA o Name, que pode
                    // ter rótulo errado). DiametroMm é geometria (fallback / conferência).
                    Catalogo            = LerString(id, "Catalogo"),
                    SubType             = LerString(id, "SubType"),
                    DiametroMm          = LerDouble(id, "Diametro") * 1000.0,
                    Material            = LerString(id, "Material"),
                    Comprimento         = LerDouble(id, "Comprimento"),
                    StartSurfElev       = LerDouble(id, "StartSurfElevation"),
                    StartInvert         = LerDouble(id, "StartInvertElevation"),
                    EndSurfElev         = LerDouble(id, "EndSurfElevation1"),
                    EndInvert           = LerDouble(id, "EndInvertElevation"),
                    // Calculados pelo construtor (sequence QTO TUBULACAO)
                    BercoAreia          = LerDouble(id, "BercoAreia"),
                    LargValaEscav       = LerDouble(id, "LargValaEscav"),
                    ProfValaMont        = LerDouble(id, "ProfValaMont"),
                    ProfValaJus         = LerDouble(id, "ProfValaJus"),
                    SecValaMont         = LerDouble(id, "SecValaMont"),
                    SecValaJus          = LerDouble(id, "SecValaJus"),
                    AlturaReatAreia     = LerDouble(id, "AlturaReaterroAreia"),
                    AreaEscoramento     = LerDouble(id, "AreaEscoramento"),
                    AreaApiloamento     = LerDouble(id, "AreaApiloamento"),
                    VolEscav            = LerDouble(id, "VolEscav"),
                    VolTubo             = LerDouble(id, "VolTubo"),
                    VolLastroAreia      = LerDouble(id, "VolLastroAreia"),
                    VolReatAreia        = LerDouble(id, "VolReatAreia"),
                    VolReaterro         = LerDouble(id, "VolReaterro"),
                    VolBotaFora         = LerDouble(id, "VolBotaFora"),
                    MassaEspAdotada     = LerDouble(id, "MassaEspAdotada"),
                    MassaBotaFora       = LerDouble(id, "MassaBotaFora"),
                    DemolRecomp         = LerDouble(id, "DemolicaoRecomposPiso"),
                };

                // Trecho = InPart.Name + " a " + OutPart.Name
                string inName  = LerNomeReferenciado(id, "InPart");
                string outName = LerNomeReferenciado(id, "OutPart");
                d.Trecho = $"{inName} a {outName}";

                // Rede = RootId -> Name
                d.NomeRede = LerNomeReferenciado(id, "RootId");
                if (string.IsNullOrWhiteSpace(d.NomeRede)) d.NomeRede = "SEM REDE";

                // DN (mm): código do Catálogo é a fonte primária; geometria (Diametro) é
                // fallback quando o Catálogo está vazio/ilegível.
                d.DnMm = ResolverDn(d.Catalogo, d.DiametroMm);

                return d;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// DN em mm. A GEOMETRIA (Diametro*1000) é a fonte da verdade — o Catálogo/Name
        /// podem estar desatualizados (ex.: tubo de 150mm cadastrado como "100"). O código
        /// do Catálogo só é usado como fallback quando a geometria está ausente (0).
        /// </summary>
        private static int ResolverDn(string catalogo, double diametroMm)
        {
            if (diametroMm >= 1.0) // qualquer tubo real tem >= 80mm; geometria válida
                return (int)Math.Round(diametroMm);

            // Fallback: código do Catálogo (tolera "DN150", "150 mm", "150").
            if (!string.IsNullOrWhiteSpace(catalogo))
            {
                string digitos = new string(catalogo.Where(char.IsDigit).ToArray());
                if (int.TryParse(digitos, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dn) && dn > 0)
                    return dn;
            }
            return (int)Math.Round(diametroMm);
        }

        /// <summary>
        /// True se o código do Catálogo diverge do DN real (geometria) em &gt; 1mm —
        /// ou seja, o cadastro do tubo está desatualizado e precisa ser corrigido no DWG.
        /// </summary>
        private static bool CatalogoDivergeDaGeometria(TuboQuantData d)
        {
            if (string.IsNullOrWhiteSpace(d.Catalogo)) return false;
            string digitos = new string(d.Catalogo.Where(char.IsDigit).ToArray());
            if (int.TryParse(digitos, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cat) && cat > 0)
                return Math.Abs(cat - d.DnMm) > 1;
            return false;
        }

        // ----------------------------------------------------------------------
        // Helpers de leitura tipada via SolidosAPI
        // ----------------------------------------------------------------------

        private static object LerObj(ObjectId id, string prop)
        {
            try
            {
                Type pt = null;
                return SolidosAPI.GetNodeParam(id, prop, null, ref pt);
            }
            catch
            {
                return null;
            }
        }

        private static double LerDouble(ObjectId id, string prop)
        {
            var v = LerObj(id, prop);
            if (v == null) return 0.0;
            switch (v)
            {
                case double d: return d;
                case float f:  return f;
                case int i:    return i;
                case long l:   return l;
                case decimal m: return (double)m;
            }
            string s = v.ToString();
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double r1)) return r1;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out double r2)) return r2;
            return 0.0;
        }

        private static string LerString(ObjectId id, string prop)
        {
            var v = LerObj(id, prop);
            return v?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Resolve uma propriedade que aponta pra outro nó SOLIDOS (ObjectId)
        /// e retorna o Name desse nó. Tolera string (já é o nome) ou null.
        /// </summary>
        private static string LerNomeReferenciado(ObjectId origemId, string propRef)
        {
            var v = LerObj(origemId, propRef);
            if (v == null) return "?";
            if (v is ObjectId refId)
            {
                if (refId.IsNull) return "?";
                var nome = LerObj(refId, "Name");
                if (nome != null) return nome.ToString();
                return refId.Handle.ToString();
            }
            return v.ToString();
        }

        // ----------------------------------------------------------------------
        // Geração do XLSX
        // ----------------------------------------------------------------------

        /// <summary>
        /// Garante que o template existe na pasta do DWG. Copia de TEMPLATE_ORIGEM se
        /// ainda não tiver cópia local. Se o origem também sumiu, abre file dialog.
        /// </summary>
        private string GarantirTemplateLocal(string dwgDir, Editor ed)
        {
            string localPath = Path.Combine(dwgDir, TEMPLATE_NOME_LOCAL);
            if (File.Exists(localPath))
            {
                ed.WriteMessage("\n[SOL_QUANT_TUBOS] Template local já existe — reutilizando.");
                return localPath;
            }

            if (File.Exists(TEMPLATE_ORIGEM))
            {
                try
                {
                    File.Copy(TEMPLATE_ORIGEM, localPath);
                    ed.WriteMessage($"\n[SOL_QUANT_TUBOS] Template copiado p/ pasta do DWG: {localPath}");
                    return localPath;
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[SOL_QUANT_TUBOS] Falha copiando template: {ex.Message}");
                }
            }

            // Fallback: usuário escolhe manualmente
            var pfo = new PromptOpenFileOptions(
                "\nSelecione 'PLANILHA DE CALCULO DE QUANTITATIVOS LEANDRO...xlsx'")
            {
                Filter = "Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|Todos (*.*)|*.*"
            };
            var r = ed.GetFileNameForOpen(pfo);
            if (r.Status != PromptStatus.OK) return null;

            // Se o usuário escolheu um arquivo fora da pasta, copia-o pra cá
            string escolhido = r.StringResult;
            if (!string.Equals(Path.GetDirectoryName(escolhido), dwgDir,
                    StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Copy(escolhido, localPath, overwrite: true);
                    return localPath;
                }
                catch
                {
                    return escolhido;
                }
            }
            return escolhido;
        }

        /// <summary>
        /// Heurística: escolhe o sheet-modelo (Pluv/Cont/Oleo) baseado em substring
        /// no nome da rede. Cada modelo tem título B2 próprio (DRENAGEM PLUVIAL/CONTAMINADA/OLEOSA).
        /// </summary>
        private static string EscolherSheetModelo(string nomeRede)
        {
            if (string.IsNullOrWhiteSpace(nomeRede)) return SHEET_MODELO_DEFAULT;
            string n = nomeRede.ToUpperInvariant();
            if (n.Contains("OLEOS"))  return "TUB_Oleo";
            if (n.Contains("CONTAM")) return "TUB_Cont";
            if (n.Contains("PLUV"))   return "TUB_Pluv";
            return SHEET_MODELO_DEFAULT;
        }

        private static void GerarXlsx(
            string template,
            string outPath,
            Dictionary<string, List<TuboQuantData>> dados,
            bool incluirDemolicao,
            Editor ed)
        {
            ExcelPackage.License.SetNonCommercialPersonal("Gleison Bruno da Costa");

            // Copia template -> outPath (mais simples que abrir template e dar SaveAs).
            if (File.Exists(outPath)) File.Delete(outPath);
            File.Copy(template, outPath);

            using var pkg = new ExcelPackage(new FileInfo(outPath));
            var wb = pkg.Workbook;
            LimparReferenciasExternas(wb);
            PreencherTubosNoWb(wb, dados, incluirDemolicao, ed);
            pkg.Save();
        }

        /// <summary>
        /// Preenche TUB_Pluv/Cont/Oleo num workbook JÁ ABERTO (não salva). Reutilizável
        /// pelo comando individual e pelo unificado.
        /// </summary>
        private static void PreencherTubosNoWb(
            ExcelWorkbook wb,
            Dictionary<string, List<TuboQuantData>> dados,
            bool incluirDemolicao,
            Editor ed)
        {
            // Agrupa as redes por SISTEMA -> sheet original (TUB_Pluv/Cont/Oleo).
            var porSistema = new Dictionary<string, List<TuboQuantData>>(StringComparer.OrdinalIgnoreCase);
            foreach (var par in dados)
            {
                string sheet = EscolherSheetModelo(par.Key);
                if (!porSistema.TryGetValue(sheet, out var lst))
                {
                    lst = new List<TuboQuantData>();
                    porSistema[sheet] = lst;
                }
                lst.AddRange(par.Value);
            }

            foreach (var nomeSheet in SHEETS_TUBO_TEMPLATE)
            {
                var sh = wb.Worksheets[nomeSheet];
                if (sh == null)
                    throw new InvalidOperationException($"Sheet '{nomeSheet}' não encontrada.");

                if (!porSistema.TryGetValue(nomeSheet, out var tubos) || tubos.Count == 0)
                {
                    ed.WriteMessage($"\n  {nomeSheet}: sem tubos (mantida como template).");
                    continue;
                }

                var ordenados = tubos
                    .OrderBy(x => x.NomeRede, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Trecho, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int linhaTotalModelo = AcharLinhaTotais(sh);
                if (linhaTotalModelo > LINHA_DADOS_INICIO)
                {
                    var colunasSoma = DetectarColunasSoma(sh, linhaTotalModelo);
                    int disponiveis = linhaTotalModelo - LINHA_DADOS_INICIO;
                    int desejado = ordenados.Count + 1;
                    int diff = desejado - disponiveis;
                    if (diff > 0) sh.InsertRow(linhaTotalModelo, diff, linhaTotalModelo - 1);
                    else if (diff < 0) sh.DeleteRow(LINHA_DADOS_INICIO, -diff);

                    int linhaTotal = LINHA_DADOS_INICIO + desejado;
                    int linhaBranca = linhaTotal - 1;
                    var branca = sh.Cells[linhaBranca, 2, linhaBranca, 28];
                    branca.Formula = "";
                    branca.Value = null;
                    foreach (int col in colunasSoma)
                    {
                        string rng = sh.Cells[LINHA_DADOS_INICIO, col, linhaBranca, col].Address;
                        sh.Cells[linhaTotal, col].Formula = $"SUM({rng})";
                    }
                }

                int linha = LINHA_DADOS_INICIO;
                foreach (var d in ordenados)
                {
                    PreencherLinha(sh, linha, d, incluirDemolicao);
                    linha++;
                }
                ed.WriteMessage($"\n  {nomeSheet}: {ordenados.Count} tubos.");
            }
        }

        private static void PreencherLinha(ExcelWorksheet sh, int linha, TuboQuantData d, bool incluirDemolicao)
        {
            // Sobrescreve valores (limpa fórmulas das células). Ordem:
            // B Trecho | C DocRef | D Ø(mm) | E Material | F Compr | G ElevTerrMont | H FITMont
            // I ElevTerrJus | J FITJus | K Berço | L LargVala | M ProfMont | N ProfJus
            // O SecMont | P SecJus | Q AltReatAreia | R AreaEscor | S AreaApiloam
            // T VolEscav | U VolTubo | V VolLastroAreia | W VolReatAreia | X VolReat
            // Y VolBF | Z MassaEsp | AA MassaBF | AB DemolRecomp
            sh.Cells[linha, 2].Value  = d.Trecho;
            sh.Cells[linha, 3].Value  = d.DocReferencia;
            sh.Cells[linha, 4].Value  = d.DnMm; // Ø pelo código do Catálogo (não pelo Name)
            sh.Cells[linha, 5].Value  = d.Material;
            sh.Cells[linha, 6].Value  = d.Comprimento;
            sh.Cells[linha, 7].Value  = d.StartSurfElev;
            sh.Cells[linha, 8].Value  = d.StartInvert;
            sh.Cells[linha, 9].Value  = d.EndSurfElev;
            sh.Cells[linha, 10].Value = d.EndInvert;
            sh.Cells[linha, 11].Value = d.BercoAreia;
            sh.Cells[linha, 12].Value = d.LargValaEscav;
            sh.Cells[linha, 13].Value = d.ProfValaMont;
            sh.Cells[linha, 14].Value = d.ProfValaJus;
            sh.Cells[linha, 15].Value = d.SecValaMont;
            sh.Cells[linha, 16].Value = d.SecValaJus;
            sh.Cells[linha, 17].Value = d.AlturaReatAreia;
            sh.Cells[linha, 18].Value = d.AreaEscoramento;
            sh.Cells[linha, 19].Value = d.AreaApiloamento;
            sh.Cells[linha, 20].Value = d.VolEscav;
            sh.Cells[linha, 21].Value = d.VolTubo;
            sh.Cells[linha, 22].Value = d.VolLastroAreia;
            sh.Cells[linha, 23].Value = d.VolReatAreia;
            sh.Cells[linha, 24].Value = d.VolReaterro;
            sh.Cells[linha, 25].Value = d.VolBotaFora;
            sh.Cells[linha, 26].Value = d.MassaEspAdotada;
            sh.Cells[linha, 27].Value = d.MassaBotaFora;
            // AB (Demolição/Recomposição do piso): só preenche se houver demolição.
            // Greenfield (Não) -> célula limpa (sem fórmula herdada nem valor).
            if (incluirDemolicao)
                sh.Cells[linha, 28].Value = d.DemolRecomp;
            else
            {
                sh.Cells[linha, 28].Formula = "";
                sh.Cells[linha, 28].Value = null;
            }
        }

        /// <summary>
        /// Procura a linha que contém "Total" na coluna B (linha de somatório no rodapé).
        /// Retorna -1 se não achar.
        /// </summary>
        private static int AcharLinhaTotais(ExcelWorksheet sh)
        {
            var dim = sh.Dimension;
            if (dim == null) return -1;
            int maxRow = dim.End.Row;
            for (int row = LINHA_DADOS_INICIO; row <= maxRow + 10; row++)
            {
                var val = sh.Cells[row, 2].Value;
                if (val is string s && s.TrimStart()
                    .StartsWith("Total", StringComparison.OrdinalIgnoreCase))
                {
                    return row;
                }
            }
            return -1;
        }

        /// <summary>
        /// Índices de coluna cuja célula na linha de Total contém um somatório
        /// (fórmula SUM/SUBTOTAL) no modelo. Fallback p/ lista fixa se nada detectado.
        /// Colunas (fallback): F=6, R=18, S=19, T=20, V=22, W=23, X=24, Y=25, AA=27, AB=28.
        /// </summary>
        private static List<int> DetectarColunasSoma(ExcelWorksheet sh, int linhaTotal)
        {
            var cols = new List<int>();
            int maxCol = sh.Dimension?.End.Column ?? 28;
            for (int c = 1; c <= maxCol; c++)
            {
                string f = sh.Cells[linhaTotal, c].Formula ?? string.Empty;
                if (f.IndexOf("SUM", StringComparison.OrdinalIgnoreCase) >= 0
                    || f.IndexOf("SUBTOTAL", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    cols.Add(c);
                }
            }
            if (cols.Count == 0)
                cols.AddRange(new[] { 6, 18, 19, 20, 22, 23, 24, 25, 27, 28 });
            return cols;
        }

        /// <summary>
        /// Remove referências externas ao workbook [1] e nomes definidos que apontam pra
        /// #REF! ou outro workbook. Sem isso, o Excel mostra prompt "Atualizar links"
        /// toda vez que o arquivo abre.
        /// </summary>
        private static void LimparReferenciasExternas(ExcelWorkbook wb)
        {
            // 1) Defined names que referenciam workbook externo ou #REF!
            try
            {
                var paraRemover = new List<string>();
                foreach (var nm in wb.Names)
                {
                    string formula = nm.Formula ?? string.Empty;
                    string addr = nm.Address ?? string.Empty;
                    string fullAddr = nm.FullAddress ?? string.Empty;
                    if (formula.Contains("[1]") || formula.Contains("[2]")
                        || formula.Contains("#REF") || addr.Contains("#REF")
                        || fullAddr.Contains("[1]") || fullAddr.Contains("[2]")
                        || fullAddr.Contains("#REF"))
                    {
                        paraRemover.Add(nm.Name);
                    }
                }
                foreach (var n in paraRemover) wb.Names.Remove(n);
            }
            catch { /* coleção pode não suportar enum/remoção em algumas versões */ }

            // 2) External links / external references no workbook
            try
            {
                var ext = wb.ExternalLinks;
                if (ext != null)
                {
                    while (ext.Count > 0) ext.RemoveAt(0);
                }
            }
            catch { /* API pode variar entre versões do EPPlus */ }
        }

        private static string SanitizarNomeAba(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "Rede";
            // Excel: max 31 chars, sem : \ / ? * [ ]
            var invalidos = new HashSet<char>(new[] { ':', '\\', '/', '?', '*', '[', ']' });
            var limpo = new string(s.Select(c => invalidos.Contains(c) ? '_' : c).ToArray()).Trim();
            if (limpo.Length == 0) return "Rede";
            return limpo.Length > 31 ? limpo.Substring(0, 31) : limpo;
        }
    }

    /// <summary>
    /// DTO com os 27 campos que vão pra UMA linha da planilha de tubos.
    /// Inputs primários (DiametroMm..EndInvert) + valores calculados pelo construtor
    /// (BercoAreia..DemolRecomp).
    /// </summary>
    internal class TuboQuantData
    {
        public string Trecho;
        public string DocReferencia;
        public string Catalogo;   // código do catálogo (DN como string: "150", "200"...)
        public string SubType;    // ex.: "TUBO - FERRO FUNDIDO" / "VÁLVULA ..."
        public int DnMm;          // DN resolvido (mm) — Catálogo primário, Diametro fallback
        public double DiametroMm; // geometria (Diametro*1000), p/ conferência/fallback
        public string Material;
        public double Comprimento;
        public double StartSurfElev;
        public double StartInvert;
        public double EndSurfElev;
        public double EndInvert;
        public double BercoAreia;
        public double LargValaEscav;
        public double ProfValaMont;
        public double ProfValaJus;
        public double SecValaMont;
        public double SecValaJus;
        public double AlturaReatAreia;
        public double AreaEscoramento;
        public double AreaApiloamento;
        public double VolEscav;
        public double VolTubo;
        public double VolLastroAreia;
        public double VolReatAreia;
        public double VolReaterro;
        public double VolBotaFora;
        public double MassaEspAdotada;
        public double MassaBotaFora;
        public double DemolRecomp;
        public string NomeRede;
    }
}
