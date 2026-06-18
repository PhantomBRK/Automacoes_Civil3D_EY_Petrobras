// Referências + aliases
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.Runtime;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Label = Autodesk.Civil.DatabaseServices.Label;
using Color = Autodesk.AutoCAD.Colors.Color;
using Subassembly = Autodesk.Civil.DatabaseServices.Subassembly;


namespace AutomacoesCivil3D
{
    public class ShoulderExtendAll_Fix
    {
        [CommandMethod("CORRIGIRACO")]
        public void SetDaylightSlope_Epsilon_For_SEA()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            CivilDocument civilDb = Manager.DocCivil;
            Database db = civilDoc.Database;

            double eps = 0; // >0 para passar na validação e exibir 0.00:1

            int encontrados = 0;
            int alterados = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Melhor atuar sobre as instâncias do desenho:
                // percorrer Assemblies -> Groups -> Subassemblies
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId entId in ms)
                {
                    DBObject dbObj = (DBObject)tr.GetObject(entId, OpenMode.ForRead);
                    if (!(dbObj is Assembly))
                    {
                        continue;
                    }

                    Assembly assembly = (Assembly)dbObj;
                    AssemblyGroupCollection grupos = assembly.Groups;

                    foreach (AssemblyGroup g in grupos)
                    {
                        ObjectIdCollection subIds = g.GetSubassemblyIds();
                        foreach (ObjectId sid in subIds)
                        {
                            Autodesk.Civil.DatabaseServices.Subassembly sub = (Autodesk.Civil.DatabaseServices.Subassembly)tr.GetObject(sid, OpenMode.ForWrite);

                            string cls = GetStr(sub, ".NET Class Name");
                           
                            bool ehSEA = cls != null && cls.IndexOf("Subassembly.ShoulderExtendAll", System.StringComparison.OrdinalIgnoreCase) >= 0;
                            if (ehSEA)
                                continue;

                            ParamDoubleCollection pD = sub.ParamsDouble;
                            if (pD == null)
                            {
                                continue;
                            }

                            // ler valor atual (input) DaylightSlope
                            double atual;
                            try { atual = pD.Value("DaylightSlope"); }
                            catch { continue; }

                            encontrados++;

                            // GRAVAR o novo valor (não basta alterar variável local)
                            bool ok = SetParamDouble(pD, "DaylightSlope", 1e8);
                            if (ok)
                            {
                                // confirma pós-escrita
                                try
                                {
                                    double apos = pD.Value("DaylightSlope");
                                    if (System.Math.Abs(apos - eps) <= 0.001) { alterados++; }
                                }
                                catch { }
                            }
                        }
                    }
                }

                tr.Commit();
            }

            // Rebuild dos corredores para refletir visualmente
            //RebuildAllCorridors();

            docEditor.WriteMessage($"\nShoulderExtendAll encontradas: {encontrados}. Ajustadas: {alterados} (DaylightSlope≈0.00:1).");
        }

        private static string GetStr(object obj, string prop)
        {
            System.Reflection.PropertyInfo pi = obj.GetType().GetProperty(prop);
            if (pi == null || pi.PropertyType != typeof(string)) { return null; }
            try { object v = pi.GetValue(obj, null); return v as string ?? v?.ToString(); } catch { return null; }
        }

        // Escreve no ParamDoubleCollection via SetValue; se não existir, tenta Add(name, value)
        private static bool SetParamDouble(ParamDoubleCollection coll, string name, double value)
        {
            // preferencial: SetValue(string,double)
            System.Reflection.MethodInfo miSet = coll.GetType().GetMethod("SetValue", new System.Type[] { typeof(string), typeof(double) });
            if (miSet != null)
            {
                try { miSet.Invoke(coll, new object[] { name, value }); return true; } catch { }
            }

            // fallback: Add(string,double) (alguns builds atualizam se já existir)
            System.Reflection.MethodInfo miAdd = coll.GetType().GetMethod("Add", new System.Type[] { typeof(string), typeof(double) });
            if (miAdd != null)
            {
                try { miAdd.Invoke(coll, new object[] { name, value }); return true; } catch { }
            }

            return false;
        }

        private static void RebuildAllCorridors()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Database db = civilDoc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId cid in civilDb.CorridorCollection)
                {
                    Corridor cor = (Corridor)tr.GetObject(cid, OpenMode.ForWrite);
                    cor.Rebuild();
                }
                tr.Commit();
            }

            Application.DocumentManager.MdiActiveDocument.Editor.Regen();
        }
    }
}
