using System;

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

namespace AutomacoesCivil3D
{
    public class ReplaceSubassembliesSimple
    {
        [CommandMethod("SUB_REPLACE_ALL")]
        public void SubstituirSubassembliesPorNome()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            CivilDocument civilDb = Manager.DocCivil;
            Database db = civilDoc.Database;

            using (Transaction trans = db.TransactionManager.StartTransaction())
            {
                PromptEntityOptions peoOld = new PromptEntityOptions(
                    "\nSelecione UMA subassembly que será substituída (modelo origem): ");
                peoOld.SetRejectMessage("\nSelecione apenas objetos do tipo Subassembly.");
                peoOld.AddAllowedClass(typeof(Subassembly), true);

                PromptEntityResult perOld = docEditor.GetEntity(peoOld);
                if (perOld.Status != PromptStatus.OK)
                {
                    return;
                }

                PromptEntityOptions peoNew = new PromptEntityOptions(
                    "\nSelecione UMA subassembly que substituirá a anterior (modelo destino): ");
                peoNew.SetRejectMessage("\nSelecione apenas objetos do tipo Subassembly.");
                peoNew.AddAllowedClass(typeof(Subassembly), true);

                PromptEntityResult perNew = docEditor.GetEntity(peoNew);
                if (perNew.Status != PromptStatus.OK)
                {
                    return;
                }

                Subassembly oldSample =
                    (Subassembly)trans.GetObject(perOld.ObjectId, OpenMode.ForRead);
                Subassembly newSample =
                    (Subassembly)trans.GetObject(perNew.ObjectId, OpenMode.ForRead);

                string oldName = oldSample.Name;
                string newName = newSample.Name;

                SubassemblyGenerator gen = newSample.GeometryGenerator;

                if (gen.GeometryGenerateMode != SubassemblyGeometryGenerateMode.UseDotNet)
                {
                    docEditor.WriteMessage(
                        "\nA subassembly de substituição precisa ser baseada em .NET (GeometryGenerateMode = UseDotNet).");
                    return;
                }

                string className =
                    "C:\\Users\\Gleison Costa\\OneDrive\\Área de Trabalho\\ARQUIVOS HOUER\\CONTRATO EPL01 - EDUARDO\\ARQUIVOS BASE\\SUBASSEMBLIES\\"
                    + newName + ".pkt";

                SubassemblyCollection subCol = civilDb.SubassemblyCollection;

                ObjectIdCollection idsParaTrocar = subCol.GetSubassemblyIdsByName(oldName);
                if (idsParaTrocar == null || idsParaTrocar.Count == 0)
                {
                    docEditor.WriteMessage(
                        "\nNenhuma subassembly com o nome \"{0}\" foi encontrada no desenho.", oldName);
                    return;
                }

                int contador = 0;
                int trocadas = 0;

                foreach (ObjectId oldId in idsParaTrocar)
                {
                    Subassembly subOld =
                        (Subassembly)trans.GetObject(oldId, OpenMode.ForRead);

                    if (!subOld.HasParentAssembly)
                    {
                        continue;
                    }

                    ObjectId parentId = subOld.AssemblyId;
                    if (parentId.IsNull || parentId.IsErased)
                    {
                        continue;
                    }

                    string newSubName = newName + "_" + contador;
                    ObjectId newUnassignedId = ObjectId.Null;

                    try
                    {
                        newUnassignedId = subCol.ImportSACSubassembly(
                            newSubName,
                            className,
                            subOld.Origin);
                    }
                    catch (System.Exception ex)
                    {
                        docEditor.WriteMessage(
                            "\nFalha ao importar a subassembly \"{0}\": {1}",
                            newSubName, ex.Message);
                        contador++;
                        continue;
                    }

                    if (newUnassignedId.IsNull)
                    {
                        contador++;
                        continue;
                    }

                    try
                    {
                        Subassembly subNew =
                            (Subassembly)trans.GetObject(newUnassignedId, OpenMode.ForWrite);

                        // copia parâmetros da antiga
                        CopiarParametrosSubassembly(subOld, subNew);

                        // ajusta lado da sub destino em função do Width
                        AjustarLadoPorWidth(subNew, subOld);

                        Assembly assembly =
                            (Assembly)trans.GetObject(parentId, OpenMode.ForWrite);

                        assembly.ReplaceSubassembly(newUnassignedId, oldId);
                        trocadas++;
                    }
                    catch (System.Exception ex)
                    {
                        docEditor.WriteMessage(
                            "\nFalha ao substituir subassembly \"{0}\": {1}",
                            oldName, ex.Message);
                        contador++;
                        continue;
                    }

                    contador++;
                }

                trans.Commit();

                docEditor.WriteMessage(
                    "\nSubstituição concluída. Subassemblies trocadas: {0}", trocadas);
            }
        }

        private static void CopiarParametrosSubassembly(Subassembly subOrigem, Subassembly subDestino)
        {
            if (subOrigem == null || subDestino == null)
            {
                return;
            }

            // Lado original (se quiser manter quando Width = 0)
            try
            {
                if (subOrigem.HasSide && subDestino.HasSide)
                {
                    
                    subDestino.Side = subOrigem.Side;
                }
            }
            catch
            {
            }

            // Doubles
            try
            {
                ParamDoubleCollection doublesOrigem = subOrigem.ParamsDouble;
                ParamDoubleCollection doublesDestino = subDestino.ParamsDouble;

                foreach (ParamDouble paramOrigem in doublesOrigem)
                {
                    string key = paramOrigem.Key;
                    try
                    {
                        ParamDouble paramDestino = doublesDestino[key];
                        if (paramDestino != null && !paramDestino.IsReadOnly)
                        {
                            paramDestino.Value = paramOrigem.Value;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            // Long
            try
            {
                ParamLongCollection longsOrigem = subOrigem.ParamsLong;
                ParamLongCollection longsDestino = subDestino.ParamsLong;

                foreach (ParamLong paramOrigem in longsOrigem)
                {
                    string key = paramOrigem.Key;
                    try
                    {
                        ParamLong paramDestino = longsDestino[key];
                        if (paramDestino != null && !paramDestino.IsReadOnly)
                        {
                            paramDestino.Value = paramOrigem.Value;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            // Bool
            try
            {
                ParamBoolCollection boolsOrigem = subOrigem.ParamsBool;
                ParamBoolCollection boolsDestino = subDestino.ParamsBool;

                foreach (ParamBool paramOrigem in boolsOrigem)
                {
                    string key = paramOrigem.Key;
                    try
                    {
                        ParamBool paramDestino = boolsDestino[key];
                        if (paramDestino != null && !paramDestino.IsReadOnly)
                        {
                            paramDestino.Value = paramOrigem.Value;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            // String
            try
            {
                ParamStringCollection stringsOrigem = subOrigem.ParamsString;
                ParamStringCollection stringsDestino = subDestino.ParamsString;

                foreach (ParamString paramOrigem in stringsOrigem)
                {
                    string key = paramOrigem.Key;
                    try
                    {
                        ParamString paramDestino = stringsDestino[key];
                        if (paramDestino != null && !paramDestino.IsReadOnly)
                        {
                            paramDestino.Value = paramOrigem.Value;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static void AjustarLadoPorWidth(Subassembly subDestino, Subassembly subOrigin)
        {

            Editor editor = Manager.DocEditor;
            if (subDestino == null)
            {
                return;
            }
            if (!subDestino.HasSide)
            {
                return;
            }

            try
            {
                ParamDoubleCollection doubleOrigin = subOrigin.ParamsDouble;
                ParamDoubleCollection doubleDestino = subDestino.ParamsDouble;
                if (doubleOrigin == null)
                {
                    return;
                }

                ParamDouble widthParam = null;
                ParamDouble widthParam2 = null;
                try
                {
                    widthParam = doubleOrigin["Offset"];
                    
                    editor.WriteMessage(
                        $"\nUsando parâmetro 'Offset' para determinar o lado da subassembly. e o valor é {widthParam.Value}");

                }
                catch
                {
                    widthParam = null;
                }

                if (widthParam == null)
                {
                    return;
                }

                double width = widthParam.Value;
                double width2 = widthParam.Value;
                editor.WriteMessage(
                    $"\n O lado era: {subDestino.Side.ToString()}");

                if (width < 0.0)
                {
                    subDestino.Side = SubassemblySideType.Left;
                    doubleDestino.Add("Width", -width);
                }
                else if (width > 0.0)
                {
                    subDestino.Side = SubassemblySideType.Right;
                    doubleDestino.Add("Width", width);
                }
                // width == 0 -> mantém o Side que veio da origem
                editor.WriteMessage(
                    $"\n Agora é: {subDestino.Side.ToString()}");

            }
            catch
            {
            }
        }
    }
}
