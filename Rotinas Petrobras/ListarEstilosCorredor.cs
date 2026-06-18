using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutomacoesCivil3D
{
    public sealed class ListarEstilosCorredor
    {
        private sealed class CodigoEstiloInfo
        {
            public string Tipo { get; set; } = string.Empty;
            public string Codigo { get; set; } = string.Empty;
            public string NomeEstilo { get; set; } = string.Empty;
            public string HandleEstilo { get; set; } = string.Empty;
            public string Classificacao { get; set; } = string.Empty;
            public string Descricao { get; set; } = string.Empty;
            public string OrigemMapeamento { get; set; } = string.Empty;
            public bool MapeamentoEncontrado { get; set; }
        }

        private sealed class ResultadoCodeSetItem
        {
            public CodeSetStyleItem Item { get; set; }
            public bool MapeamentoExplicito { get; set; }
            public string Origem { get; set; } = string.Empty;
        }

        private sealed class RelatorioEstilosCorredor
        {
            public string NomeCorredor { get; set; } = string.Empty;
            public string NomeCodeSetStyle { get; set; } = string.Empty;
            public string HandleCodeSetStyle { get; set; } = string.Empty;
            public List<CodigoEstiloInfo> ShapeStyles { get; } = new List<CodigoEstiloInfo>();
            public List<CodigoEstiloInfo> LinkStyles { get; } = new List<CodigoEstiloInfo>();
            public List<string> Avisos { get; } = new List<string>();
        }

        [CommandMethod("LISTAR_ESTILOS_CORREDOR")]
        public void Executar()
        {
            Document doc = Manager.DocCad;
            Editor editor = Manager.DocEditor;
            Database db = Manager.DocData;

            PromptEntityOptions options = new PromptEntityOptions("\nSelecione o corredor: ");
            options.SetRejectMessage("\nO objeto selecionado nao e um corredor.");
            options.AddAllowedClass(typeof(Corridor), exactMatch: false);

            PromptEntityResult result = editor.GetEntity(options);
            if (result.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nSelecao cancelada.");
                return;
            }

            RelatorioEstilosCorredor relatorio;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Corridor corridor = tr.GetObject(result.ObjectId, OpenMode.ForRead, false) as Corridor;
                if (corridor == null)
                {
                    editor.WriteMessage("\nNao foi possivel abrir o corredor selecionado.");
                    return;
                }

                relatorio = ColetarEstilos(corridor, tr);
            }

            string textoCompleto = FormatarRelatorio(relatorio);
            editor.WriteMessage("\n" + textoCompleto.Replace("\r\n", "\n"));
            AcadApp.ShowAlertDialog(FormatarResumo(relatorio));
        }

        private static RelatorioEstilosCorredor ColetarEstilos(Corridor corridor, Transaction tr)
        {
            RelatorioEstilosCorredor relatorio = new RelatorioEstilosCorredor
            {
                NomeCorredor = SafeString(() => corridor.Name),
                NomeCodeSetStyle = SafeString(() => corridor.CodeSetStyleName)
            };

            string[] shapeCodes = SafeGetCodes(() => corridor.GetShapeCodes());
            string[] linkCodes = SafeGetCodes(() => corridor.GetLinkCodes());

            ObjectId codeSetStyleId = SafeObjectId(() => corridor.CodeSetStyleId);
            if (codeSetStyleId.IsNull)
            {
                relatorio.Avisos.Add("O corredor nao possui Code Set Style associado.");
                relatorio.ShapeStyles.AddRange(CriarItensSemMapeamento("Shape", shapeCodes));
                relatorio.LinkStyles.AddRange(CriarItensSemMapeamento("Link", linkCodes));
                return relatorio;
            }

            relatorio.HandleCodeSetStyle = SafeHandle(codeSetStyleId);

            CodeSetStyle codeSetStyle = tr.GetObject(codeSetStyleId, OpenMode.ForWrite, false) as CodeSetStyle;
            if (codeSetStyle == null)
            {
                relatorio.Avisos.Add("Nao foi possivel abrir o Code Set Style do corredor.");
                relatorio.ShapeStyles.AddRange(CriarItensSemMapeamento("Shape", shapeCodes));
                relatorio.LinkStyles.AddRange(CriarItensSemMapeamento("Link", linkCodes));
                return relatorio;
            }

            string nomeStyle = SafeString(() => codeSetStyle.Name);
            if (!string.IsNullOrWhiteSpace(nomeStyle))
            {
                relatorio.NomeCodeSetStyle = nomeStyle;
            }

            relatorio.ShapeStyles.AddRange(ColetarPorTipo(
                codeSetStyle,
                tr,
                shapeCodes,
                "Shape",
                SubassemblySubentityStyleType.ShapeType,
                relatorio.Avisos));

            relatorio.LinkStyles.AddRange(ColetarPorTipo(
                codeSetStyle,
                tr,
                linkCodes,
                "Link",
                SubassemblySubentityStyleType.LinkType,
                relatorio.Avisos));

            return relatorio;
        }

        private static IEnumerable<CodigoEstiloInfo> ColetarPorTipo(
            CodeSetStyle codeSetStyle,
            Transaction tr,
            IEnumerable<string> codigos,
            string tipo,
            SubassemblySubentityStyleType styleType,
            ICollection<string> avisos)
        {
            List<CodigoEstiloInfo> itens = new List<CodigoEstiloInfo>();
            string[] codigosValidos = NormalizarCodigos(codigos);

            if (codigosValidos.Length == 0)
            {
                return itens;
            }

            if (!TentarSelecionarTipo(codeSetStyle, styleType, avisos))
            {
                return CriarItensSemMapeamento(tipo, codigosValidos);
            }

            foreach (string codigo in codigosValidos)
            {
                ResultadoCodeSetItem resultado = TentarObterItem(codeSetStyle, codigo, styleType);
                if (resultado == null)
                {
                    itens.Add(new CodigoEstiloInfo
                    {
                        Tipo = tipo,
                        Codigo = codigo,
                        NomeEstilo = "(sem mapeamento explicito no Code Set Style)",
                        MapeamentoEncontrado = false
                    });
                    continue;
                }

                CodeSetStyleItem item = resultado.Item;
                ObjectId styleId = SafeObjectId(() => item.CodeStyleId);
                string nomeEstilo = SafeString(() => item.CodeStyleName);
                if (string.IsNullOrWhiteSpace(nomeEstilo))
                {
                    nomeEstilo = ResolverNomeDBObject(tr, styleId);
                }

                itens.Add(new CodigoEstiloInfo
                {
                    Tipo = tipo,
                    Codigo = codigo,
                    NomeEstilo = string.IsNullOrWhiteSpace(nomeEstilo) ? "(style nao resolvido)" : nomeEstilo,
                    HandleEstilo = SafeHandle(styleId),
                    Classificacao = SafeString(() => item.Classification),
                    Descricao = SafeString(() => item.Description),
                    OrigemMapeamento = resultado.Origem,
                    MapeamentoEncontrado = resultado.MapeamentoExplicito
                });
            }

            return itens;
        }

        private static bool TentarSelecionarTipo(
            CodeSetStyle codeSetStyle,
            SubassemblySubentityStyleType styleType,
            ICollection<string> avisos)
        {
            try
            {
                codeSetStyle.SubentityStyleType = styleType;
                return true;
            }
            catch (System.Exception ex)
            {
                avisos.Add($"Nao foi possivel alternar o Code Set Style para {styleType}: {ex.Message}");
                return false;
            }
        }

        private static ResultadoCodeSetItem TentarObterItem(
            CodeSetStyle codeSetStyle,
            string codigo,
            SubassemblySubentityStyleType styleType)
        {
            try
            {
                CodeSetStyleItem item = codeSetStyle.GetItemBy(CodeSetStyleItemType.NormalItemType, codigo);
                if (ItemValido(item, codigo, styleType))
                {
                    return new ResultadoCodeSetItem
                    {
                        Item = item,
                        MapeamentoExplicito = true,
                        Origem = "codigo explicito"
                    };
                }
            }
            catch
            {
            }

            try
            {
                foreach (CodeSetStyleItem item in codeSetStyle)
                {
                    if (ItemValido(item, codigo, styleType))
                    {
                        return new ResultadoCodeSetItem
                        {
                            Item = item,
                            MapeamentoExplicito = true,
                            Origem = "codigo explicito"
                        };
                    }
                }
            }
            catch
            {
            }

            CodeSetStyleItem defaultItem = TentarObterItemEspecial(
                codeSetStyle,
                CodeSetStyleItemType.DefaultItemType,
                styleType);

            if (defaultItem != null)
            {
                return new ResultadoCodeSetItem
                {
                    Item = defaultItem,
                    MapeamentoExplicito = false,
                    Origem = "item default do Code Set Style"
                };
            }

            return null;
        }

        private static CodeSetStyleItem TentarObterItemEspecial(
            CodeSetStyle codeSetStyle,
            CodeSetStyleItemType itemType,
            SubassemblySubentityStyleType styleType)
        {
            try
            {
                CodeSetStyleItem item = codeSetStyle.GetItemBy(itemType, string.Empty);
                if (ItemTemTipo(item, styleType))
                {
                    return item;
                }
            }
            catch
            {
            }

            try
            {
                foreach (CodeSetStyleItem item in codeSetStyle)
                {
                    CodeSetStyleItemType currentType = SafeEnum(() => item.ItemType, CodeSetStyleItemType.NormalItemType);
                    if (currentType == itemType && ItemTemTipo(item, styleType))
                    {
                        return item;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool ItemValido(
            CodeSetStyleItem item,
            string codigo,
            SubassemblySubentityStyleType styleType)
        {
            if (item == null)
            {
                return false;
            }

            CodeSetStyleItemType itemType = SafeEnum(() => item.ItemType, CodeSetStyleItemType.NoCodeItemType);
            if (itemType != CodeSetStyleItemType.NormalItemType)
            {
                return false;
            }

            SubassemblySubentityStyleType itemStyleType = SafeEnum(() => item.StyleType, styleType);
            if (itemStyleType != styleType)
            {
                return false;
            }

            string itemCode = SafeString(() => item.Code);
            return string.Equals(itemCode, codigo, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ItemTemTipo(CodeSetStyleItem item, SubassemblySubentityStyleType styleType)
        {
            if (item == null)
            {
                return false;
            }

            SubassemblySubentityStyleType itemStyleType = SafeEnum(() => item.StyleType, styleType);
            return itemStyleType == styleType;
        }

        private static IEnumerable<CodigoEstiloInfo> CriarItensSemMapeamento(string tipo, IEnumerable<string> codigos)
        {
            foreach (string codigo in NormalizarCodigos(codigos))
            {
                yield return new CodigoEstiloInfo
                {
                    Tipo = tipo,
                    Codigo = codigo,
                    NomeEstilo = "(style nao resolvido)",
                    MapeamentoEncontrado = false
                };
            }
        }

        private static string FormatarRelatorio(RelatorioEstilosCorredor relatorio)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== ESTILOS DO CORREDOR ===");
            sb.AppendLine($"Corredor: {ValorOuVazio(relatorio.NomeCorredor)}");
            sb.AppendLine($"Code Set Style: {ValorOuVazio(relatorio.NomeCodeSetStyle)}{FormatarHandle(relatorio.HandleCodeSetStyle)}");
            sb.AppendLine();

            AppendResumoPorEstilo(sb, "SHAPE STYLES", relatorio.ShapeStyles);
            sb.AppendLine();
            AppendDetalhes(sb, "SHAPE CODES", relatorio.ShapeStyles);
            sb.AppendLine();
            AppendResumoPorEstilo(sb, "LINK STYLES", relatorio.LinkStyles);
            sb.AppendLine();
            AppendDetalhes(sb, "LINK CODES", relatorio.LinkStyles);

            if (relatorio.Avisos.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("AVISOS:");
                foreach (string aviso in relatorio.Avisos.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"  - {aviso}");
                }
            }

            return sb.ToString();
        }

        private static string FormatarResumo(RelatorioEstilosCorredor relatorio)
        {
            int shapeStyles = ContarEstilosUnicos(relatorio.ShapeStyles);
            int linkStyles = ContarEstilosUnicos(relatorio.LinkStyles);

            return
                "Estilos do corredor lidos.\n\n" +
                $"Corredor: {ValorOuVazio(relatorio.NomeCorredor)}\n" +
                $"Shape codes: {relatorio.ShapeStyles.Count}\n" +
                $"Shape styles unicos: {shapeStyles}\n" +
                $"Link codes: {relatorio.LinkStyles.Count}\n" +
                $"Link styles unicos: {linkStyles}\n\n" +
                "A lista completa foi escrita na linha de comando do AutoCAD.";
        }

        private static void AppendResumoPorEstilo(
            StringBuilder sb,
            string titulo,
            IEnumerable<CodigoEstiloInfo> itens)
        {
            List<CodigoEstiloInfo> lista = itens.ToList();
            sb.AppendLine(titulo);
            if (lista.Count == 0)
            {
                sb.AppendLine("  (nenhum codigo encontrado)");
                return;
            }

            foreach (IGrouping<string, CodigoEstiloInfo> grupo in lista
                         .GroupBy(i => NormalizarChaveEstilo(i.NomeEstilo), StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.First().NomeEstilo, StringComparer.OrdinalIgnoreCase))
            {
                CodigoEstiloInfo primeiro = grupo.First();
                string codigos = string.Join(", ", grupo
                    .Select(i => i.Codigo)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase));

                sb.AppendLine($"  - {ValorOuVazio(primeiro.NomeEstilo)}{FormatarHandle(primeiro.HandleEstilo)}");
                sb.AppendLine($"    Codigos: {codigos}");
            }
        }

        private static void AppendDetalhes(
            StringBuilder sb,
            string titulo,
            IEnumerable<CodigoEstiloInfo> itens)
        {
            List<CodigoEstiloInfo> lista = itens.ToList();
            sb.AppendLine(titulo);
            if (lista.Count == 0)
            {
                sb.AppendLine("  (nenhum codigo encontrado)");
                return;
            }

            foreach (CodigoEstiloInfo item in lista.OrderBy(i => i.Codigo, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"  - {item.Codigo} -> {ValorOuVazio(item.NomeEstilo)}{FormatarHandle(item.HandleEstilo)}");

                List<string> detalhes = new List<string>();
                if (!item.MapeamentoEncontrado)
                {
                    string origem = string.IsNullOrWhiteSpace(item.OrigemMapeamento)
                        ? "sem mapeamento explicito"
                        : $"sem mapeamento explicito; origem: {item.OrigemMapeamento}";
                    detalhes.Add(origem);
                }
                if (!string.IsNullOrWhiteSpace(item.Classificacao))
                {
                    detalhes.Add($"classificacao: {item.Classificacao}");
                }
                if (!string.IsNullOrWhiteSpace(item.Descricao))
                {
                    detalhes.Add($"descricao: {item.Descricao}");
                }

                if (detalhes.Count > 0)
                {
                    sb.AppendLine("    " + string.Join("; ", detalhes));
                }
            }
        }

        private static int ContarEstilosUnicos(IEnumerable<CodigoEstiloInfo> itens)
        {
            return itens
                .Where(i => i.MapeamentoEncontrado && !string.IsNullOrWhiteSpace(i.NomeEstilo))
                .Select(i => NormalizarChaveEstilo(i.NomeEstilo))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        private static string[] SafeGetCodes(Func<string[]> getter)
        {
            try
            {
                return NormalizarCodigos(getter() ?? Array.Empty<string>());
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string[] NormalizarCodigos(IEnumerable<string> codigos)
        {
            return (codigos ?? Array.Empty<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string ResolverNomeDBObject(Transaction tr, ObjectId objectId)
        {
            if (objectId.IsNull)
            {
                return string.Empty;
            }

            try
            {
                Autodesk.AutoCAD.DatabaseServices.DBObject dbObject = tr.GetObject(objectId, OpenMode.ForRead, false);
                PropertyInfo prop = dbObject.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public);
                object value = prop?.GetValue(dbObject);
                return value?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeString(Func<string> getter)
        {
            try
            {
                return getter() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static ObjectId SafeObjectId(Func<ObjectId> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return ObjectId.Null;
            }
        }

        private static T SafeEnum<T>(Func<T> getter, T fallback)
        {
            try
            {
                return getter();
            }
            catch
            {
                return fallback;
            }
        }

        private static string SafeHandle(ObjectId objectId)
        {
            if (objectId.IsNull)
            {
                return string.Empty;
            }

            try
            {
                return objectId.Handle.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string FormatarHandle(string handle)
        {
            return string.IsNullOrWhiteSpace(handle) ? string.Empty : $" [handle {handle}]";
        }

        private static string ValorOuVazio(string valor)
        {
            return string.IsNullOrWhiteSpace(valor) ? "(vazio)" : valor;
        }

        private static string NormalizarChaveEstilo(string valor)
        {
            return string.IsNullOrWhiteSpace(valor) ? "(vazio)" : valor.Trim();
        }
    }
}
