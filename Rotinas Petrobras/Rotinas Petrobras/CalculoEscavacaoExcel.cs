using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Excel = Microsoft.Office.Interop.Excel;

namespace AutomacoesCivil3D
{
    public class CalculoEscavacaoExcel
    {
        public static void ExportarParaExcel(List<string> escavacoes, string caminhoArquivo)
        {
            Excel.Application excelApp = new Excel.Application();
            Excel.Workbook workbook = null;
            Excel.Worksheet worksheet = null;

            try
            {
                if (excelApp == null)
                {
                    Console.WriteLine("Erro ao iniciar o Excel.");
                    return;
                }

                excelApp.Visible = false;
                excelApp.DisplayAlerts = false;

                workbook = excelApp.Workbooks.Add();
                worksheet = (Excel.Worksheet)workbook.Sheets[1];

                // Escrevendo os cabeçalhos
                worksheet.Cells[1, 1] = "ID";
                worksheet.Cells[1, 2] = "Descrição";

                // Preenchendo os dados
                int row = 2;
                foreach (var escavacao in escavacoes)
                {
                    worksheet.Cells[row, 1] = row - 1;
                    worksheet.Cells[row, 2] = escavacao;
                    row++;
                }

                // Salvando o arquivo
                workbook.SaveAs(caminhoArquivo);
                workbook.Close();
                excelApp.Quit();

                Console.WriteLine($"Arquivo salvo com sucesso: {caminhoArquivo}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao exportar para Excel: {ex.Message}");
            }
            finally
            {
                // Liberar objetos COM
                if (worksheet != null) Marshal.ReleaseComObject(worksheet);
                if (workbook != null) Marshal.ReleaseComObject(workbook);
                if (excelApp != null) Marshal.ReleaseComObject(excelApp);
            }
        }
    }
}
