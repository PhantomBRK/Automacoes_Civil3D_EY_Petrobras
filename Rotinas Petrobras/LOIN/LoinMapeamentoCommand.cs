using System.Windows.Interop;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using CriaProfiles;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutomacoesCivil3D
{
    public class LoinMapeamentoCommand
    {
        [CommandMethod("LOIN_MAPEAMENTO")]
        [CommandMethod("LOINMAP")]
        public void AbrirLoinMapeamento()
        {
            Editor ed = Manager.DocEditor;
            try
            {
                string? drawingPath = Manager.DocCad?.Name;
                string configPath   = LoinMapeamentoService.ResolverCaminhoConfig(drawingPath);
                LoinMapeamentoConfig config = LoinMapeamentoService.Carregar(configPath);

                LoinMapeamentoWindow janela = new LoinMapeamentoWindow(config, configPath);

                // Owner = janela principal do AutoCAD, para a modal ficar acima da MDI
                System.IntPtr owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (owner != System.IntPtr.Zero)
                    new WindowInteropHelper(janela).Owner = owner;

                AcadApp.ShowModalWindow(owner, janela);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                ed.WriteMessage($"\nErro AutoCAD ao abrir o mapeamento LOIN: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nErro ao abrir o mapeamento LOIN: {ex.Message}");
                AcadApp.ShowAlertDialog("Falha ao abrir o Mapeamento LOIN:\n" + ex.Message);
            }
        }
    }
}
