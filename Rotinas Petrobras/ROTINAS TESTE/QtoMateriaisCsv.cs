using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Label = Autodesk.Civil.DatabaseServices.Label;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace AutomacoesCivil3D.ROTINAS_TESTE

{
    public class QtoMateriaisCsv
    {
        private class QtoItem
        {
            public string Servico { get; set; }
            public string Unidade { get; set; }
            public double Quantidade { get; set; }
        }

        [CommandMethod("QTO_MATERIAIS_CSV")]
        public void ExportarMateriaisQtoCsv()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            CivilDocument civilDb = Manager.DocCivil;
            Database db = civilDoc.Database;

            try
            {
                // Caminho do CSV de saída
                PromptSaveFileOptions pso = new PromptSaveFileOptions("\nInforme o arquivo CSV de saída:");
                pso.Filter = "Arquivo CSV (*.csv)|*.csv";
                PromptFileNameResult pfr = docEditor.GetFileNameForSave(pso);
                if (pfr.Status != PromptStatus.OK)
                {
                    return;
                }

                string csvPath = pfr.ToString();

                // Monta definição do relatório QTO (resumo, desenho inteiro)
                QTOGenerateDetail reportDetail = new QTOGenerateDetail();
                reportDetail.ReportType = QTOReportType.SummaryReport;
                reportDetail.ReportExtent = QTOReportExtent.DrawingExtent;
                reportDetail.ReportSelectedPayItemsOnly = false;
                reportDetail.ReportSheetOnly = false;

                string xmlPath = Path.Combine(
                    Path.GetTempPath(),
                    "QTO_" + Guid.NewGuid().ToString("N") + ".xml"
                );

                string[] generatedPayItemIds = new string[0];

                bool ok = QTOUtility.GenerateXMLReport(
                    reportDetail,
                    ref xmlPath,
                    ref generatedPayItemIds
                );

                if (!ok || !File.Exists(xmlPath))
                {
                    docEditor.WriteMessage(
                        "\nNão foi possível gerar o relatório QTO em XML. Verifique se o Quantity Takeoff está configurado."
                    );
                    return;
                }

                // Lê o XML e extrai os itens
                XDocument xdoc = XDocument.Load(xmlPath);

                IEnumerable<XElement> payItemElements = xdoc.Descendants("PayItem");
                if (!payItemElements.Any())
                {
                    // fallback genérico – em alguns templates o nó é "Item"
                    payItemElements = xdoc.Descendants("Item");
                }

                List<QtoItem> items = new List<QtoItem>();

                foreach (XElement el in payItemElements)
                {
                    string descricao = ObterTexto(el,
                        "Description",
                        "Name",
                        "ItemDescription"
                    );

                    string unidade = ObterTexto(el,
                        "Unit",
                        "Units",
                        "uom"
                    );

                    double quantidade = ObterNumero(el,
                        "TotalQuantity",
                        "Quantity",
                        "Qty",
                        "TotalQty"
                    );

                    if (string.IsNullOrWhiteSpace(descricao) ||
                        string.IsNullOrWhiteSpace(unidade))
                    {
                        continue;
                    }

                    if (quantidade <= 0.0)
                    {
                        continue;
                    }

                    QtoItem item = new QtoItem
                    {
                        Servico = descricao.Trim(),
                        Unidade = unidade.Trim(),
                        Quantidade = quantidade
                    };

                    items.Add(item);
                }

                if (items.Count == 0)
                {
                    docEditor.WriteMessage(
                        "\nNenhum item foi encontrado no XML. Confira os nomes dos nós/atributos no XML gerado."
                    );
                    return;
                }

                // Agrupa por Serviço + Unidade e soma as quantidades
                List<QtoItem> agrupados = items
                    .GroupBy(i => new { i.Servico, i.Unidade })
                    .Select(g => new QtoItem
                    {
                        Servico = g.Key.Servico,
                        Unidade = g.Key.Unidade,
                        Quantidade = g.Sum(x => x.Quantidade)
                    })
                    .OrderBy(i => i.Servico)
                    .ToList();

                // Gera o CSV (separador ";", quantidades em pt-BR)
                CultureInfo culturaBr = CultureInfo.GetCultureInfo("pt-BR");

                using (StreamWriter writer = new StreamWriter(
                    csvPath,
                    false,
                    System.Text.Encoding.UTF8))
                {
                    writer.WriteLine("Serviço;Unidade de Medida;Quantidade");

                    foreach (QtoItem item in agrupados)
                    {
                        string quantidadeStr = item.Quantidade.ToString("0.###", culturaBr);
                        writer.WriteLine(
                            string.Format(
                                "{0};{1};{2}",
                                item.Servico,
                                item.Unidade,
                                quantidadeStr
                            )
                        );
                    }
                }

                docEditor.WriteMessage("\nCSV criado em: " + csvPath);
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage("\nErro na exportação QTO: " + ex.Message);
            }
        }

        private static string ObterTexto(XElement element, params string[] nomes)
        {
            foreach (string nome in nomes)
            {
                XAttribute attr = element.Attribute(nome);
                if (attr != null && !string.IsNullOrWhiteSpace(attr.Value))
                {
                    return attr.Value;
                }

                XElement child = element.Element(nome);
                if (child != null && !string.IsNullOrWhiteSpace(child.Value))
                {
                    return child.Value;
                }
            }

            return string.Empty;
        }

        private static double ObterNumero(XElement element, params string[] nomes)
        {
            string texto = ObterTexto(element, nomes);
            if (string.IsNullOrWhiteSpace(texto))
            {
                return 0.0;
            }

            double valor;

            // tenta primeiro com ponto (Invariante), depois com vírgula (pt-BR)
            if (!double.TryParse(
                    texto,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out valor))
            {
                if (!double.TryParse(
                        texto,
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.GetCultureInfo("pt-BR"),
                        out valor))
                {
                    return 0.0;
                }
            }

            return valor;
        }
    }
}
