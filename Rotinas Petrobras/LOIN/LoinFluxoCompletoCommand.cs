using System;
using System.Windows.Interop;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutomacoesCivil3D
{
    // Orquestrador end-to-end: 6 etapas (Pset_A → Mapeamento → Code Set Styles
    // → Linkar IFC → Exportar Sólidos → IFCEXPORT) em uma janela única.
    // Aliases: LOIN_FLUXO_COMPLETO, LOIN_RUN, LOIN_FLUXO.
    public class LoinFluxoCompletoCommand
    {
        [CommandMethod("LOIN_FLUXO_COMPLETO")]
        [CommandMethod("LOIN_FLUXO")]
        [CommandMethod("LOIN_RUN")]
        public void AbrirFluxoCompleto()
        {
            Editor ed = Manager.DocEditor;
            try
            {
                LoinFluxoCompletoWindow janela = new LoinFluxoCompletoWindow();

                IntPtr owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (owner != IntPtr.Zero)
                    new WindowInteropHelper(janela).Owner = owner;

                AcadApp.ShowModalWindow(owner, janela);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                ed.WriteMessage("\nErro AutoCAD ao abrir o fluxo completo: " + ex.Message);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nErro ao abrir o fluxo completo: " + ex.Message);
                AcadApp.ShowAlertDialog("Falha ao abrir o fluxo completo LOIN:\n" + ex.Message);
            }
        }
    }
}
