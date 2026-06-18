using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutomacoesCivil3D
{
    public class IfcInfraExportHelper
    {
        [CommandMethod("EXPORTAR_IFC43_INFRA")]
        public void ExportarIfc43Infra()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;

            try
            {
                // (Opcional) garantir que os Psets existem/estão atualizados
                CorridorPropertySetsCreator creator = new CorridorPropertySetsCreator();
                creator.CriarPropertySetsCorredor();

                // Chama o comando da extensão IFC 4.3
                // (abre a mesma caixa "Export to IFC" do ribbon)
                Application.DocumentManager.MdiActiveDocument
                    .SendStringToExecute("IFCINFRAEXPORT ", true, false, false);
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage("\nErro ao chamar IFCINFRAEXPORT: " + ex.Message);
            }
        }
    }
}
