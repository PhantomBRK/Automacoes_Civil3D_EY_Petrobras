using Autodesk.AutoCAD.ApplicationServices;
using AutomacoesCivil3D;
using Microsoft.Office.Interop.Excel;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.IO;
using Application = Microsoft.Office.Interop.Excel.Application;




internal static class RimElevationHelpers
{

    public static void ExportarParaExcel(List<EstruturaDrenagem> estruturas, string caminhoArquivo)
    {
        Application excelApp = new Application();
        Workbook workbook = excelApp.Workbooks.Add();
        Worksheet sheet = (Worksheet)workbook.Sheets[1];

        string[] headers = { "Unidade", "Anexo", "Item_PPU", "Subcontrato", "Código_Composição",
                                 "Documento de Referência", "Família", "Serviço", "Unidade_de_Medida",
                                 "Quantidade", "Subitem", "Condição de Execução", "Dimensão", "Observações"};

        // Escrevendo os títulos na primeira linha
        for (int i = 0; i < headers.Length; i++)
        {
            sheet.Cells[1, i + 1] = headers[i];
        }

        // Adicionando os dados
        int linha = 2;
        foreach (var estrutura in estruturas)
        {
            sheet.Cells[linha, 1] = estrutura.Unidade;
            sheet.Cells[linha, 2] = estrutura.Anexo;
            sheet.Cells[linha, 3] = estrutura.Item_PPU;
            sheet.Cells[linha, 4] = estrutura.Subcontrato;
            sheet.Cells[linha, 5] = estrutura.Código_Composição;
            sheet.Cells[linha, 6] = estrutura.DocRef;
            sheet.Cells[linha, 7] = estrutura.Familia;
            sheet.Cells[linha, 8] = estrutura.Serviço;
            sheet.Cells[linha, 9] = estrutura.Unidade_de_Medida;
            sheet.Cells[linha, 10] = estrutura.Quantidade;
            sheet.Cells[linha, 11] = estrutura.Subitem;
            sheet.Cells[linha, 12] = estrutura.CondicaoExe;
            sheet.Cells[linha, 13] = estrutura.Dimensao;
            sheet.Cells[linha, 14] = estrutura.Observacoes;
            linha++;
        }

        // Salvando o arquivo
        workbook.SaveAs(caminhoArquivo);
        workbook.Close();
        excelApp.Quit();
    }


    public static void ExportarParaExcelEPP(List<EstruturaDrenagem> estruturas, string caminhoArquivo)
    {
        //ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        using (var package = new ExcelPackage(new FileInfo("MyWorkbook.xlsx")))
        {

            ExcelWorksheet sheet = package.Workbook.Worksheets.Add("Estruturas");

            string[] headers = { "Unidade", "Anexo", "Item_PPU", "Subcontrato", "Código_Composição",
                                     "Documento de Referência", "Família", "Serviço", "Unidade_de_Medida",
                                     "Quantidade", "Subitem", "Condição de Execução", "Dimensão", "Observações"};

            for (int i = 0; i < headers.Length; i++)
            {
                sheet.Cells[1, i + 1].Value = headers[i];
            }
            
            int colunas = headers.Length;

            // Adicionar cabeçalhos formatados
            for (int i = 0; i < colunas; i++)
            {
                var cell = sheet.Cells[1, i + 1];
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;  // Negrito no cabeçalho
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(118, 104, 174)); // Cor de fundo roxa
                cell.Style.Font.Color.SetColor(Color.White); // Texto branco
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center; // Centralizar texto
                cell.Style.Border.BorderAround(ExcelBorderStyle.Thin); // Adicionar borda
            }

            int linha = 2;
            foreach (var estrutura in estruturas)
            {
                sheet.Cells[linha, 1].Value = estrutura.Unidade;
                sheet.Cells[linha, 2].Value = estrutura.Anexo;
                sheet.Cells[linha, 3].Value = estrutura.Item_PPU;
                sheet.Cells[linha, 4].Value = estrutura.Subcontrato;
                sheet.Cells[linha, 5].Value = estrutura.Código_Composição;
                sheet.Cells[linha, 6].Value = estrutura.DocRef;
                sheet.Cells[linha, 7].Value = estrutura.Familia;
                sheet.Cells[linha, 8].Value = estrutura.Serviço;
                sheet.Cells[linha, 9].Value = estrutura.Unidade_de_Medida;
                sheet.Cells[linha, 10].Value = estrutura.Quantidade;
                sheet.Cells[linha, 11].Value = estrutura.Subitem;
                sheet.Cells[linha, 12].Value = estrutura.CondicaoExe;
                sheet.Cells[linha, 13].Value = estrutura.Dimensao;
                sheet.Cells[linha, 14].Value = estrutura.Observacoes;

                // Adicionando cores alternadas nas linhas
                if (linha % 2 == 0)
                {
                    for (int col = 1; col <= colunas; col++)
                    {
                        sheet.Cells[linha, col].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        sheet.Cells[linha, col].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 239, 255)); // Cor lilás claro
                    }
                }

                linha++;
            }
            // Ajustar largura automática das colunas
            sheet.Cells[sheet.Dimension.Address].AutoFitColumns();

            // Aplicar bordas finas em todas as células
            using (var range = sheet.Cells[1, 1, linha - 1, colunas])
            {
                range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            }

            File.WriteAllBytes(caminhoArquivo, package.GetAsByteArray());
        }
    }
}
