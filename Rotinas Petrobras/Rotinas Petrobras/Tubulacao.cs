using System;
using System.Collections.Generic;
using Excel = Microsoft.Office.Interop.Excel;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using System.Net;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Microsoft.Office.Interop.Excel;
using System.Text.RegularExpressions;

using Microsoft.VisualBasic.Devices;
using Network = Autodesk.Civil.DatabaseServices.Network;

namespace AutomacoesCivil3D
{

    public class Estrutura
    {
        public double LarguraExterna { get; set; }
        public double ComprimentoExterno { get; set; }
        public double Profundidade { get; set; }

        public double VolumeEscavacao { get; set; }

        public double CalcularVolumeEscavacao()
        {
            double largura = LarguraExterna + 0.5;
            double comprimento = ComprimentoExterno + 0.5;
            double altura = Profundidade + 0.5;



            if (Profundidade <= 1.25)
            {
                VolumeEscavacao = largura * altura * comprimento;
            }
            else
            {
                if (Profundidade <= 1.75)
                {
                    double hipo;
                    double catetoOp;
                    hipo = (Profundidade - 1.25) / Math.Cos(0.785398);
                    catetoOp = hipo * Math.Sin(0.785398);
                    VolumeEscavacao = (1.25 * largura + (altura - 1.25) * catetoOp + (altura - 1.25) * largura) * comprimento;
                }
                else
                {
                    VolumeEscavacao = largura * altura * comprimento;
                }

            }

            return VolumeEscavacao;
        }
    }

    public class Tubulacao
    {
        public double DiametroExterno { get; set; }
        public double Profundidade { get; set; }
        public double Comprimento { get; set; }
        public string Rede { get; set; }
        public string Montante { get; set; }
        public string Jusante { get; set; }
        public string NomeTubo { get; set; }

        public double LarguraVala;

        public double VolumeEscavação { get; set; }

        public double CalcularVolumeEscavacao()
        {
            if (DiametroExterno <= 0.4)
            {
                LarguraVala = 0.8;
            }
            else
            {
                if (DiametroExterno <= 0.8)
                {
                    LarguraVala = DiametroExterno + 0.6;
                }
                else
                {
                    LarguraVala = DiametroExterno + 0.4;
                }
            }


            if (Profundidade <= 1.25)
            {
                VolumeEscavação = LarguraVala * Profundidade * Comprimento;
            }
            else
            {
                if (Profundidade <= 1.75)
                {
                    double hipo;
                    double catetoOp;
                    hipo = (Profundidade - 1.25) / Math.Cos(0.785398);
                    catetoOp = hipo * Math.Sin(0.785398);
                    VolumeEscavação = (1.25 * LarguraVala + (Profundidade - 1.25) * catetoOp + (Profundidade - 1.25) * LarguraVala) * Comprimento;
                }
                else
                {
                    VolumeEscavação = LarguraVala * Profundidade * Comprimento;
                }

            }

            return VolumeEscavação;
        }
    }

    public class ProjetoTubulacao
    {
        public List<Tubulacao> Tubos { get; set; } = new List<Tubulacao>();
        public List<Estrutura> Estruturas { get; set; } = new List<Estrutura>();

        public void AdicionarTubo(Tubulacao tubo)
        {
            Tubos.Add(tubo);
        }
        public void AdicionarEstrutura(Estrutura estrutura)
        {
            Estruturas.Add(estrutura);
        }
        public double CalcularVolumeTotalEscavacao()
        {
            double volumeTotal = 0;
            foreach (var tubo in Tubos)
            {
                volumeTotal += tubo.CalcularVolumeEscavacao();
            }

            /*foreach (var estrutura in Estruturas)
            {
                volumeTotal += estrutura.CalcularVolumeEscavacao();
            }*/

            return volumeTotal;
        }

        public void GerarRelatorioExcel(Dictionary<string, ProjetoTubulacao> redes, string caminhoArquivo)
        {

            Excel.Application excelApp = null;
            Workbook workbook = null;

            try
            {

                excelApp = new Excel.Application();
                excelApp.Visible = false;
                excelApp.DisplayAlerts = false;
                workbook = excelApp.Workbooks.Add();

                foreach (var entrada in redes)
                {
                    string nomeRede = entrada.Key;
                    ProjetoTubulacao projeto = entrada.Value;
                    Worksheet worksheet = (Worksheet)workbook.Sheets.Add();
                    worksheet.Name = nomeRede.Length > 31 ? nomeRede.Substring(0, 31) : nomeRede; // Limite máximo de 31 caracteres no nome da planilha

                    // Definição de intervalos para mesclar células
                    /*worksheet.Range["A1:A3"].Merge(); // REDE
                    worksheet.Range["B1:B3"].Merge(); // TUBO
                    worksheet.Range["C1:D2"].Merge(); // TRECHOS
                    worksheet.Range["E1:E3"].Merge(); // DIAMETRO
                    worksheet.Range["F1:F3"].Merge(); // PROFUNDIDADE
                    worksheet.Range["G1:G3"].Merge(); // COMPRIMENTO
                    worksheet.Range["H1:H3"].Merge(); // VOLUME */

                    // Definição de textos nos cabeçalhos
                    worksheet.Cells[1, 1] = "REDES";
                    worksheet.Cells[1, 2] = "TUBOS";
                    worksheet.Cells[1, 3] = "TRECHOS";
                    worksheet.Cells[3, 3] = "MONTANTE";
                    worksheet.Cells[3, 4] = "JUSANTE";
                    worksheet.Cells[1, 5] = "DIAMETRO TUBO (mm)";
                    worksheet.Cells[1, 6] = "PROFUNDIDADE (m)";
                    worksheet.Cells[1, 7] = "COMPRIMENTO (m)";
                    worksheet.Cells[1, 8] = "VOLUME ESCAVAÇÃO (m³)";

                    // Aplicação de cor de fundo
                    /*Excel.Range headerRange = worksheet.Range["A1:H3"];
                    headerRange.Interior.Color = ColorTranslator.ToOle(Color.Beige);
                    headerRange.Borders.LineStyle = Excel.XlLineStyle.xlContinuous;
                    headerRange.Borders.Weight = Excel.XlBorderWeight.xlThin;
                    headerRange.Font.Bold = true;
                    headerRange.HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;
                    headerRange.VerticalAlignment = Excel.XlVAlign.xlVAlignCenter;*/

                    // Preenchimento dos dados
                    int row = 4;
                    foreach (var tubo in projeto.Tubos)
                    {

                        worksheet.Cells[row, 1] = tubo.Rede;
                        worksheet.Cells[row, 2] = tubo.NomeTubo;
                        worksheet.Cells[row, 3] = tubo.Montante;
                        worksheet.Cells[row, 4] = tubo.Jusante;
                        worksheet.Cells[row, 5] = $"{Math.Round(tubo.DiametroExterno, 2) * 1000}mm";
                        worksheet.Cells[row, 6] = Math.Round(tubo.Profundidade, 2);
                        worksheet.Cells[row, 7] = Math.Round(tubo.Comprimento, 2);
                        worksheet.Cells[row, 8] = Math.Round(tubo.CalcularVolumeEscavacao(), 2);
                        row++;
                    }

                   

                    /*Excel.Range corpo = worksheet.Range[$"A4:H{row - 1}"];
                    corpo.Borders.LineStyle = Excel.XlLineStyle.xlContinuous;
                    corpo.Borders.Weight = Excel.XlBorderWeight.xlThin;
                    corpo.HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;
                    corpo.VerticalAlignment = Excel.XlVAlign.xlVAlignCenter;
                    // Resumo total
                    Excel.Range headerRange2 = worksheet.Range[$"G{row}:H{row}"];
                    headerRange2.Interior.Color = ColorTranslator.ToOle(Color.Beige);
                    headerRange2.Borders.LineStyle = Excel.XlLineStyle.xlContinuous;
                    headerRange2.Borders.Weight = Excel.XlBorderWeight.xlThin;
                    headerRange2.Font.Bold = true;
                    headerRange2.HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;
                    headerRange2.VerticalAlignment = Excel.XlVAlign.xlVAlignCenter;
                    worksheet.Cells[row, 7] = "VOLUME TOTAL:";
                    worksheet.Cells[row, 8] = $"{Math.Round(projeto.CalcularVolumeTotalEscavacao(), 2)}m³";
                    worksheet.Columns.AutoFit();*/
                }
                // Salvando o arquivo
                workbook.SaveAs(caminhoArquivo);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erro ao gerar relatório: " + ex.Message);
            }
            finally
            {
                if (workbook != null)
                {
                    workbook.Close(false);
                    ReleaseObject(workbook);
                }
                if (excelApp != null)
                {
                    excelApp.Quit();
                    ReleaseObject(excelApp);
                }
            }
        }



       

        private void ReleaseObject(object obj)
        {
            try
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(obj);
                obj = null;
            }
            catch (Exception ex)
            {
                obj = null;
                Console.WriteLine("Erro ao liberar o objeto: " + ex.ToString());
            }
            finally
            {
                GC.Collect();
            }
        }
    }

    public class Rotinas
    {
        [CommandMethod("GerarRelatorioTubulacoes")]
        public void GerarRelatorio()
        {
            string nomeProjeto = "";
            Editor editor = Manager.DocEditor;
            Database db = Manager.DocData;

            try
            {

                Dictionary<string, ProjetoTubulacao> redesDict = new Dictionary<string, ProjetoTubulacao>();

                // Inicia uma transação para acessar os dados da rede
                using (Transaction trans = db.TransactionManager.StartTransaction())
                {
                    CivilDocument civilDoc = CivilApplication.ActiveDocument;
                    Database docData = Manager.DocData;
                    ObjectIdCollection redes = civilDoc.GetPipeNetworkIds();
                    nomeProjeto = docData.ProjectName; 

                    foreach (ObjectId netId in redes)
                    {
                        Network rede = (Network)trans.GetObject(netId, OpenMode.ForRead);                
                        ProjetoTubulacao projeto = new ProjetoTubulacao();

                        foreach (ObjectId pipeId in rede.GetPipeIds())
                        {
                            Pipe tubo = (Pipe)trans.GetObject(pipeId, OpenMode.ForRead);

                            if (tubo != null)
                            {
                                Structure montanteId = (Structure)trans.GetObject(tubo.StartStructureId, OpenMode.ForRead);
                                Structure jusanteId = (Structure)trans.GetObject(tubo.EndStructureId, OpenMode.ForRead);
                                string nomeRede = rede.Name; //Nome da rede pertencente
                                string nome = tubo.Name; //Nome do tubo
                                string montante = montanteId.Name; //Nome da estrutura a montante
                                string jusante = jusanteId.Name; //Nome da estrutura a Jusante
                                double diametro = tubo.OuterDiameterOrWidth; // Diametro Externo
                                double comprimento = tubo.Length3D; // Comprimento
                                double profundidade = Math.Abs((tubo.CoverOfStartPoint + tubo.CoverOfEndpoint) / 2); //Calculo da profundidade média do tubo

                                projeto.AdicionarTubo(new Tubulacao
                                {
                                    DiametroExterno = diametro,
                                    Profundidade = profundidade,
                                    Comprimento = comprimento,
                                    Rede = nomeRede,
                                    NomeTubo = nome,
                                    Montante = montante,
                                    Jusante = jusante,

                                });
                            }


                            // Coleta de Estruturas
                            foreach (ObjectId structId in rede.GetStructureIds())
                            {
                                Structure estrutura = (Structure)trans.GetObject(structId, OpenMode.ForRead);

                                if (estrutura != null && estrutura.PartType == PartType.StructJunction)
                                {
                                    double larguraExterna = estrutura.DiameterOrWidth; // Exemplo, ajustar conforme necessário
                                    double comprimentoExterno = estrutura.Length; // Exemplo, ajustar conforme necessário
                                    double profundidade = estrutura.SumpDepth; // Ajustar com base na lógica de profundidade específica

                                    projeto.AdicionarEstrutura(new Estrutura
                                    {
                                        LarguraExterna = larguraExterna,
                                        ComprimentoExterno = comprimentoExterno,
                                        Profundidade = profundidade
                                    });
                                }
                            }
                        }
                        redesDict.Add(rede.Name, projeto);
                    }
                }
                // Caminho para salvar o relatório
                string caminhoArquivo = @$"C:\Users\gleis\OneDrive\Área de Trabalho\{nomeProjeto}.csv";
                ProjetoTubulacao relatorioExcel = new ProjetoTubulacao();
                relatorioExcel.GerarRelatorioExcel(redesDict, caminhoArquivo);
                Autodesk.AutoCAD.ApplicationServices.Core.Application.ShowAlertDialog("\nRelatório gerado com sucesso em: " + caminhoArquivo);
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage("\nErro ao gerar relatório: " + ex.Message);
            }
        }
    }
}