using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutomacoesCivil3D
{
    public class SOLIDOS_SUPERFÍCIES
    {



        [CommandMethod("SolidoSurface")]
        public void ExtrairSolidoSurface()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            CivilDocument civilDb = Manager.DocCivil;
            Database db = civilDoc.Database;

            try
            {
                using (DocumentLock docLock = civilDoc.LockDocument())
                using (Transaction TransCad = db.TransactionManager.StartTransaction())
                {
                    // 1) Selecionar superfície superior (TIN)
                    PromptEntityOptions peoSup = new PromptEntityOptions("\nSelecione a superfície TIN (topo):");
                    peoSup.SetRejectMessage("\nObjeto inválido. Selecione uma TinSurface.\n");
                    peoSup.AddAllowedClass(typeof(TinSurface), false);
                    PromptEntityResult perSup = docEditor.GetEntity(peoSup);
                    if (perSup.Status != PromptStatus.OK)
                        return;

                    TinSurface supTin = (TinSurface)TransCad.GetObject(perSup.ObjectId, OpenMode.ForRead);

                   

                    
                        PromptEntityOptions peoBase = new PromptEntityOptions("\nSelecione a superfície TIN base:");
                        peoBase.SetRejectMessage("\nObjeto inválido. Selecione uma TinSurface.\n");
                        peoBase.AddAllowedClass(typeof(TinSurface), false);
                        PromptEntityResult perBase = docEditor.GetEntity(peoBase);
                        if (perBase.Status != PromptStatus.OK)
                            return;

                        string caminho = AbrirDialogoSelecaoArquivo();
                        if (string.IsNullOrWhiteSpace(caminho))
                            return;

                        supTin.CreateSolidsAtSurfaceToFile(perBase.ObjectId, "0", 0, ref caminho);


                    TransCad.Commit();
                }

                docEditor.WriteMessage("\nMalha fechada criada com sucesso (sem conversão para sólido).");
            }
            catch (System.Exception ex)
            {
                Editor ed = Manager.DocEditor;
                ed.WriteMessage($"\nErro: {ex.Message}");
            }
        }


        public static string AbrirDialogoSelecaoArquivo(string filtro = "Arquivos DWG (*.dwg)|*.dwg")
        {
            string caminhoArquivo = null;
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = filtro;
                openFileDialog.Multiselect = false;
                openFileDialog.Title = "SELECIONE O ARQUIVOS DE DESTINO DOS SÓLIDOS";

                DialogResult resultadoDialogo = openFileDialog.ShowDialog();
                if (resultadoDialogo == DialogResult.OK)
                    caminhoArquivo = openFileDialog.FileName;
            }
            return caminhoArquivo;
        }
    }
}
