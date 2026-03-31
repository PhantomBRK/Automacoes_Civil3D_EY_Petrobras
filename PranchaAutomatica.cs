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
    public class AtualizaAtributosPrancha
    {
        [CommandMethod("AtualizaPranchaPorPlanilha")]
        public static void AtualizaPranchaPorPlanilhaCommand()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;

            // Exibe a notificação do tipo SIM/NÃO para o usuário
            DialogResult resposta = MessageBox.Show(
                "Deseja abrir a planilha para editar antes de atualizar a prancha?",
                "Editar Planilha",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            string arquivoSelecionado = AbrirDialogoSelecaoArquivo("Planilhas Excel (*.xlsx)|*.xlsx|Todos Arquivos (*.*)|*.*");
            if (arquivoSelecionado == null)
                return;

            // Se usuário pediu para editar, chama o Excel
            if (resposta == DialogResult.Yes)
            {
                ProcessarPlanilhaEBloco(arquivoSelecionado);
                // A função acima já aguarda o fechamento do Excel antes de prosseguir.
            }

            // Agora, independente de ter editado ou não, lê a planilha normalmente para atualizar os blocos
            string caminhoExcel = arquivoSelecionado;
            string nomeBlocoPrancha = "CFP BRU"; // Use o nome exato do bloco de prancha no seu desenho

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

                BlockTableRecord space = (BlockTableRecord)trans.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

                foreach (ObjectId entId in space)
                {
                    Entity ent = (Entity)trans.GetObject(entId, OpenMode.ForRead);
                    if (ent is BlockReference blocoRef)
                    {
                        BlockTableRecord blkDef = (BlockTableRecord)trans.GetObject(blocoRef.BlockTableRecord, OpenMode.ForRead);
                        if (blkDef.Name.Equals(nomeBlocoPrancha, StringComparison.OrdinalIgnoreCase))
                        {
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


        public static void ProcessarPlanilhaEBloco(string caminhoPlanilha)
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;

            dynamic excelApp = null;
            dynamic workbook = null;

            try
            {
                excelApp = Activator.CreateInstance(Type.GetTypeFromProgID("Excel.Application"));
                workbook = excelApp.Workbooks.Open(caminhoPlanilha);

                excelApp.Visible = true;
                docEditor.WriteMessage("\nEdite a planilha. Após salvar e fechar, a rotina continuará.");

                // Aguarda fechar (não força close nem quit, apenas espera o usuário)
                while (true)
                {
                    Thread.Sleep(1000);
                    try
                    {
                        if (workbook.Windows(1).Visible == false) // Em alguns Office pode dar erro aqui se já fechou
                            break;
                    }
                    catch
                    {
                        break;
                    }
                }

                // Fecha e limpa com segurança
                if (workbook != null)
                {
                    try { workbook.Close(false); } catch { }
                    Marshal.ReleaseComObject(workbook);
                }
                if (excelApp != null)
                {
                    if (excelApp.Workbooks.Count == 0)
                    {
                        excelApp.Quit();
                    }
                    Marshal.ReleaseComObject(excelApp);
                }
            }
            catch (Exception ex)
            {
                docEditor.WriteMessage($"\nErro ao manipular o Excel: {ex.Message}");
            }
            finally
            {
                // Força o coletor de lixo para limpar objetos COM.
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            docEditor.WriteMessage("\nProsseguindo com o preenchimento do bloco...");

            // Agora prossiga com sua rotina (ler planilha via EPPlus, preencher bloco, etc)
        }

        // Lê os dados da planilha para um dicionário {TAG, VALOR}
        private static Dictionary<string, string> LerDadosDaPlanilha(string caminhoExcel)
        {
            var dict = new Dictionary<string, string>();

            // Configure EPPlus para uso não-comercial
            ExcelPackage.License.SetNonCommercialPersonal("Gleison Bruno da Costa");

            using (var package = new ExcelPackage(new FileInfo(caminhoExcel)))
            {
                var ws = package.Workbook.Worksheets[0];
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