// Usings (inclua também as libs do seu Release.rar e a sua DLL de subassemblies, se necessário)
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Label = Autodesk.Civil.DatabaseServices.Label;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace AutomacoesCivil3D
{
    public class ShoulderExtendAll_ParamPatch
    {
        [CommandMethod("ACOSTAMENTO")]
        public static void SetDaylightSlope_Zero_For_SEA()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            CivilDocument civilDb = Manager.DocCivil;
            Database db = civilDoc.Database;

            int encontrados = 0;
            int alterados = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {

                Editor ed = Manager.DocEditor;
                CivilDocument docCivil = Manager.DocCivil;
                bool sucesso = false;

                foreach (ObjectId id in docCivil.SubassemblyCollection)
                {
                    var assembly = (Autodesk.Civil.DatabaseServices.Subassembly)tr.GetObject(id, OpenMode.ForWrite);
                    var sub = assembly;

                    if (sub.Name.Contains("ShoulderExtendAll"))
                    {
                        ed.WriteMessage($"\nSubassembly encontrada: {sub.Name}\n");

                        foreach (var kv in sub.ParamsDouble)
                        {
                            
                            string name = kv.Key;
                            double val = kv.Value;

                            if (name.Equals("DaylightSlope", System.StringComparison.OrdinalIgnoreCase))
                            {
                                ed.WriteMessage($"\n    Parametro: {name} = {val}\n\n");
                                ed.WriteMessage($"\nSubassembly '{sub.Name}' - DaylightSlope: {val}");
                                val = 0.01;
                                sucesso = true;

                                // Exemplo de uso: apenas exibir o valor atual
                                ed.WriteMessage($"\nSubassembly '{sub.Name}' - Novo DaylightSlope: {val}");
                            }
                            encontrados++;

                        }
                    }

                }

                tr.Commit();
            }



        }

    }
}
