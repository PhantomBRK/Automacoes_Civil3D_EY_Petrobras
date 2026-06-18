using System;
using System.Windows.Interop;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutomacoesCivil3D
{
    public class LoinProjetoCommand
    {
        [CommandMethod("LOIN_DADOS_PROJETO")]
        [CommandMethod("LOIN_PROJ")]
        public void AbrirDadosProjeto()
        {
            Editor ed = Manager.DocEditor;
            try
            {
                string? drawingPath = Manager.DocCad?.Name;
                string caminhoConfig = LoinProjetoService.ResolverCaminhoConfig(drawingPath);
                LoinProjetoDto dto = LoinProjetoService.Carregar(caminhoConfig);

                LoinProjetoWindow janela = new LoinProjetoWindow(dto, caminhoConfig);

                IntPtr owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (owner != IntPtr.Zero)
                    new WindowInteropHelper(janela).Owner = owner;

                AcadApp.ShowModalWindow(owner, janela);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                ed.WriteMessage("\nErro AutoCAD ao abrir Dados do Projeto: " + ex.Message);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nErro ao abrir Dados do Projeto: " + ex.Message);
                AcadApp.ShowAlertDialog("Falha ao abrir os Dados do Projeto LOIN:\n" + ex.Message);
            }
        }
    }
}
