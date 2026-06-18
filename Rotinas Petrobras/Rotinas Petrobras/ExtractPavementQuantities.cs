using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DataShortcuts;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;


namespace AutomacoesCivil3D
{
    public class ExtractPavementQuantities
    {


public void ExtractPavementQuantities1()
    {
        Document doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
        Database db = doc.Database;
        Autodesk.AutoCAD.EditorInput.Editor editor = doc.Editor;

        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            try
            {
                // Acessar o CivilDocument
                CivilDocument civilDoc = CivilApplication.ActiveDocument;

                // Acessar todos os corredores no desenho
                
                foreach (ObjectId corridorId in civilDoc.CorridorCollection)
                {
                    Corridor corridor = (Corridor)tr.GetObject(corridorId, OpenMode.ForRead);
                    if (corridor == null) continue;

                    editor.WriteMessage($"\nCorredor: {corridor.Name}");

                    // Acessar as linhas de amostra (baselines)
                    foreach (Baseline baseline in corridor.Baselines)
                    {
                        // Acessar as regiões do corredor
                        foreach (BaselineRegion region in baseline.BaselineRegions)
                        {
                            // Acessar os assemblies aplicados
                            Assembly assembly = (Assembly) tr.GetObject(region.AssemblyId, OpenMode.ForRead);
                            if (assembly == null) continue;

                            // Acessar os volumes de material
                           
                        }
                    }
                }

                tr.Commit();
            }
            catch (Exception ex)
            {
                editor.WriteMessage($"Erro: {ex.Message}");
                tr.Abort();
            }
        }
    }
}
}
