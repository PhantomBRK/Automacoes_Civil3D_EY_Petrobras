using System;
using System.IO;
using System.Linq;
using System.Text;
using HtmlAgilityPack;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using Microsoft.VisualBasic.ApplicationServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using System.Diagnostics;

namespace AutomacoesCivil3D
{
    public class VolumeReport
    {
        [CommandMethod("GenerateAndConvertReport")]
        public void GenerateAndConvertReport()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            CivilDocument civilDoc = Manager.DocCivil;
            


            try
            {
                // Defina o caminho de saída do relatório HTML
                string reportOutputPath = @"C:/Users/gleis/AppData/Local/Temp/QuantityReportTemp.html";

                // Exclua qualquer arquivo existente no caminho de saída
                if (File.Exists(reportOutputPath))
                {
                    File.Delete(reportOutputPath);
                }

                // Inicia o comando no Civil 3D para gerar o relatório
                ed.WriteMessage("\nExecutando o comando para gerar o relatório...");
                ed.Command("_AeccGenerateQuantitiesReport");
                ed.WriteMessage("\nRelatório gerado no caminho: {0}\n", reportOutputPath);

                // Aguarde até que o HTML seja gerado (adicionamos uma pausa forçada)
                Thread.Sleep(5000); // 5-segundos para garantir a geração

                // Verifique se o arquivo HTML foi gerado
                if (!File.Exists(reportOutputPath))
                {
                    ed.WriteMessage("\nErro: O relatório HTML não foi encontrado no caminho: {0}\n", reportOutputPath);
                    return;
                }

                // Converte o HTML gerado para CSV
                //string csvPath = ConvertHtmlReportToCsv(reportOutputPath);

                // Exibe mensagem informando o caminho do CSV gerado
                /*ed.WriteMessage("\nRelatório CSV gerado com sucesso no seguinte local: {0}\n", csvPath);
                try
                {
                    if (File.Exists(csvPath))
                    {
                        ProcessStartInfo startInfo = new ProcessStartInfo()
                        {
                            FileName = "excel.exe",
                            Arguments = csvPath,
                            UseShellExecute = true
                        };
                        Process.Start(startInfo);
                    }
                }
                catch (Exception ex)
                {
                    ed.WriteMessage($"\nErro ao abrir arquivo CSV: {ex.Message}\n");
                }*/
            }
            catch (Exception ex)
            {
                ed.WriteMessage("\nErro: {0}\n", ex.Message);
            }
        }

        private string ConvertHtmlReportToCsv(string xmlPath)
        {
            // Caminho de saída do arquivo CSV
            string csvPath = Path.ChangeExtension(@"C:\Users\gleis\OneDrive\Documentos\SMEC_TRP-PAV", ".csv");

            // Carregar o HTML usando HtmlAgilityPack
            var doc = new HtmlDocument();
            doc.Load(xmlPath);

            var sb = new StringBuilder();

            // ** Adicionar o cabeçalho conforme solicitado **
            sb.AppendLine("Unidade;Anexo;Item_PPU;Subcontrato;Codigo_Composicao;Documento de Referencia;Familia;Servico;Unidade_de_Medida;Quantidade;Subitem;Condicao_de_Execucao;Transporte Local (Km);Transporte Externo (Km)");

            // Selecionar a tabela de materiais (segunda tabela)
            var tables = doc.DocumentNode.SelectNodes("//table");
            if (tables == null || tables.Count < 2)
            {
                throw new InvalidOperationException("Erro: Tabela de materiais não encontrada no relatório HTML.");
            }

            HtmlNode materialsTable = tables[1];
            CivilDocument civilDoc = Manager.DocCivil;
            Database docData = Manager.DocData; 

            // Processar as linhas da tabela
            foreach (var row in materialsTable.SelectNodes(".//tr"))
            {
                var cells = row.SelectNodes(".//td");
                if (cells != null && cells.Count >= 3) // Certificando-se de que há pelo menos Código, Material e Quantidade
                {
                    string familia = "";
                    string servico = "";
                    string subitem = "";
                    string condicaoExecucao = "";
                    string unidadeMedida = "";
                    switch (cells[1].InnerText.Trim()) // Coluna 8: "Material"
                    {
                        case "CALÇADA DE CONCRETO SIMPLES":
                            familia = "Calcada";
                            servico = "Calcada de Concreto Simples";
                            subitem = "Espessura 10 cm";
                            condicaoExecucao = "Concreto Armado";
                            unidadeMedida = "m²";
                            break;

                        case "PAVIMENTO EM ÁREAS INTERNAS":
                            familia = "Pavimentos";
                            servico = "Pavimento em Areas Internas";
                            subitem = "Pavimento em Areas Internas";
                            condicaoExecucao = "Concreto Armado";
                            unidadeMedida = "m³";
                            break;

                        case "PAVIMENTO EM VIAS DE ACESSO":
                            familia = "Pavimentos";
                            servico = "Pavimento em Vias de Acesso";
                            subitem = "Pavimento em Vias de Acesso";
                            condicaoExecucao = "Concreto Armado";
                            unidadeMedida = "m³";
                            break;

                        case "PAVIMENTO DE BLOCOS INTERTRAVADOS 8 cm":
                            familia = "Pavimentos";
                            servico = "Pavimento de Blocos Intertravados";
                            subitem = "Espessura 8 cm";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            break;

                        case "PAVIMENTO DE BLOCOS INTERTRAVADOS 10 cm":
                            familia = "Pavimentos";
                            servico = "Pavimento de Blocos Intertravados";
                            subitem = "Espessura 10 cm";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            break;

                        case "PAVIMENTO DE BLOCOS INTERTRAVADOS 6,5 cm":
                            familia = "Pavimentos";
                            servico = "Pavimento de Blocos Intertravados";
                            subitem = "Espessura 6,5 cm";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            break;

                        case "PAVIMENTO ASFÁLTICO RECAPEAMENTO":
                            familia = "Pavimentos";
                            servico = "Pavimento Asfaltico Recapeamento";
                            subitem = "Espessura 5 cm";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            break;

                        case "PAVIMENTO ASFÁLTICO CAPA DE ROLAMENTO":
                            familia = "Pavimentos";
                            servico = "Pavimento Asfaltico Capa de Rolamento";
                            subitem = "Espessura 5 cm";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            break;

                        case "BASE DE MACADAME HIDRÁULICO":
                            familia = "Bases e Sub-Bases";
                            servico = "Base de Macadame Hidraulico";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            break;

                        case "BASE DE BRITA GRADUADA":
                            familia = "Bases e Sub-Bases";
                            servico = "Base de Brita Graduada";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            break;

                        case "SUB BASE/COLCHÃO DRENANTE":
                            familia = "Bases e Sub-Bases";
                            servico = "Sub Base/Colchao Drenante";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            break;

                        case "EXECUÇÃO DE CAMINHO DE SERVIÇO":
                            familia = "Bases e Sub-Bases";
                            servico = "Execucao de Caminho de Servico";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            break;

                        case "REGULARIZAÇÃO E COMPACTAÇÃO DE SUBLEITO":
                            familia = "Bases e Sub-Bases";
                            servico = "Regularizacao e Compactacao de Subleito";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            break;

                        case "ESCAVAÇÃO ATÉ O SUB LEITO":
                            familia = "Bases e Sub-Bases";
                            servico = "Escavacao ate o sub leito";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m³";
                            break;

                        case "PINTURA DE LIGAÇÃO":
                            familia = "Pinturas e Imprimacao";
                            servico = "Pintura de Ligacao";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m²";
                            break;

                        case "IMPRIMAÇÃO DE BASE":
                            familia = "Pinturas e Imprimacao";
                            servico = "Imprimacao de Base";
                            subitem = "Não se aplica";
                            condicaoExecucao = "Não se aplica";
                            unidadeMedida = "m²";
                            break;
                        
                    }
                    // Capturar os dados nas colunas especificadas
                    string unidade = "";
                    string Anexo = "";
                    string Item_PPU = "";
                    string subcontrato = ""; // Deixe em branco por enquanto
                    string codigoComposicao = cells[0].InnerText.Trim().Replace(",", ";"); // Coluna 2 (Código)
                    string documentoReferencia = Path.GetFileName(docData.Filename); // Deixe em branco
                    familia = familia.Trim().Replace(",", ";");
                    servico = servico.Trim().Replace(",", ";"); // Coluna 8 (Material)
                    unidadeMedida = unidadeMedida.Trim().Replace(",", ";");
                    string quantidade = cells[2].InnerText.Trim().Replace(",", ";"); // Coluna 10 (Quantidade)
                    subitem = subitem.Trim().Replace(",", ";");
                    condicaoExecucao = condicaoExecucao.Trim().Replace(",", ";");
                    string transporteLocal = ""; // Deixe em branco
                    string transporteExterno = ""; // Deixe em branco

                    // Construir a linha no CSV
                    sb.AppendLine($"{unidade};{Anexo};{Item_PPU};{subcontrato};{codigoComposicao};{documentoReferencia};{familia};{servico};{unidadeMedida};{quantidade};{subitem};{condicaoExecucao};{transporteLocal};{transporteExterno}");
                }
            }

            // Escrever o conteúdo no arquivo CSV
            File.WriteAllText(csvPath, sb.ToString(), Encoding.GetEncoding("ISO-8859-1"));


            return csvPath;
        }
    }
}