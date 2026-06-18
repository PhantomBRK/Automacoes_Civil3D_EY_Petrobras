using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using System.Runtime.InteropServices;
using Excel = Microsoft.Office.Interop.Excel;

namespace AutomacoesCivil3D
{
    public class AtualizaAtributosPranchaPAvelar
    {
        [CommandMethod("PreencherPranchaPAvelar")]
        public static void AtualizaPranchaPorPlanilhaCommand()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;

            string arquivoSelecionado = AbrirDialogoSelecaoArquivo("Planilhas Excel (*.xlsx)|*.xlsx|Todos Arquivos (*.*)|*.*");
            if (arquivoSelecionado != null)
            {

                // Caminhos podem ser parametrizados ou solicitados do usuário via PromptOpenFileDialog, por exemplo
                string caminhoExcel = arquivoSelecionado;
                string nomeBlocoPrancha = "CFP BRU"; // Use o nome exato do bloco de prancha no seu desenho

                ProcessarPlanilhaEBloco(caminhoExcel);

                // Ler valores da planilha
                Dictionary<string, string> dadosAtributos = LerDadosDaPlanilha(caminhoExcel);

                using (DocumentLock docLock = civilDoc.LockDocument())
                using (Transaction trans = civilDoc.TransactionManager.StartTransaction())
                {
                    Database db = civilDoc.Database;
                    BlockTable blockTable = (BlockTable)trans.GetObject(db.BlockTableId, OpenMode.ForRead);

                    if (!blockTable.Has(nomeBlocoPrancha))
                    {
                        docEditor.WriteMessage($"\nO bloco '{nomeBlocoPrancha}' não está inserido no desenho!");
                        return;
                    }

                    // Percorre o espaco atual (PaperSpace, ModelSpace, etc)
                    BlockTableRecord space = (BlockTableRecord)trans.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

                    foreach (ObjectId entId in space)
                    {
                        Entity ent = (Entity)trans.GetObject(entId, OpenMode.ForRead);
                        // Verifica se é uma referência do bloco de prancha
                        if (ent is BlockReference blocoRef)
                        {
                            BlockTableRecord blkDef = (BlockTableRecord)trans.GetObject(blocoRef.BlockTableRecord, OpenMode.ForRead);
                            if (blkDef.Name.Equals(nomeBlocoPrancha, StringComparison.OrdinalIgnoreCase))
                            {
                                // Só atualiza os atributos desse bloco
                                if (blocoRef.AttributeCollection.Count > 0)
                                {
                                    foreach (ObjectId attId in blocoRef.AttributeCollection)
                                    {
                                        AttributeReference attRef = (AttributeReference)trans.GetObject(attId, OpenMode.ForWrite);
                                        if (dadosAtributos.TryGetValue(attRef.Tag, out string valor))
                                        {
                                            attRef.TextString = valor;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    trans.Commit();
                    docEditor.WriteMessage("\nAtributos do bloco de prancha atualizados a partir da planilha!");
                }
            }
        }


        public static void ProcessarPlanilhaEBloco(string caminhoPlanilha)
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;

            // Passo 1: Abrir o Excel
            Excel.Application excelApp = null;
            Excel.Workbook workbook = null;

            try
            {
                excelApp = new Excel.Application();
                workbook = excelApp.Workbooks.Open(caminhoPlanilha);

                // Deixa o Excel visível para o usuário editar
                excelApp.Visible = true;

                docEditor.WriteMessage("\nEdite a planilha. Após salvar e fechar, a rotina continuará.");

                // Aguarda até o usuário fechar o workbook
                while (true)
                {
                    Thread.Sleep(1000);
                    try
                    {
                        // Tenta acessar uma propriedade do workbook para detectar quando for fechado
                        var _ = workbook.Name;
                    }
                    catch (COMException)
                    {
                        // Se der COMException, a planilha foi fechada
                        break;
                    }
                }
            }
            finally
            {
                // Garante que o Excel será finalizado (apenas se não houverem mais workbooks abertos)
                if (excelApp != null)
                {
                    if (excelApp.Workbooks.Count == 0)
                    {
                        excelApp.Quit();
                        Marshal.ReleaseComObject(excelApp);
                    }
                }
            }

            // Passo 2: Prossiga sua rotina normalmente
            docEditor.WriteMessage("\nA leitura e o preenchimento do bloco serão iniciados.");

            // Aqui você pode inserir sua lógica de leitura (EPPlus ou Interop) e atualização de atributos do bloco
            // Exemplo: AtualizaAtributosPrancha.AtualizaPranchaPorPlanilhaCommand(caminhoPlanilha, nomeBlocoPrancha);
        }

        // Lê os dados da planilha para um dicionário {TAG, VALOR}
        private static Dictionary<string, string> LerDadosDaPlanilha(string caminhoExcel)
        {
            var dict = new Dictionary<string, string>();

            // Configure EPPlus para uso não-comercial
            ExcelPackage.License.SetNonCommercialPersonal("Gleison Bruno da Costa");

            using (var package = new ExcelPackage(new FileInfo(caminhoExcel)))
            {
                var ws = package.Workbook.Worksheets[1];
                int row = 2;
                while (!string.IsNullOrEmpty(ws.Cells[row, 1].Text))
                {
                    string tag = ws.Cells[row, 1].Text.Trim();
                    string valor = ws.Cells[row, 2].Text.Trim();
                    dict[tag] = valor;
                    row++;
                }
            }
            return dict;
        }

        public static string AbrirDialogoSelecaoArquivo(string filtro = "Todos Arquivos (*.*)|*.*")
        {
            string caminhoArquivo = null;
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = filtro;
                openFileDialog.Multiselect = false;
                openFileDialog.Title = "Selecione o arquivo";

                DialogResult resultadoDialogo = openFileDialog.ShowDialog();
                if (resultadoDialogo == DialogResult.OK)
                    caminhoArquivo = openFileDialog.FileName;
            }
            return caminhoArquivo;
        }
    }
}