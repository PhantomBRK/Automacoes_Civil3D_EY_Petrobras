using System;
using System.Collections.Generic;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace AutomacoesCivil3D
{
    public static class IfcParametersService
    {
        // Nome do subdicionário que guardará os parâmetros IFC
        private const string IfcDictionaryName = "IFC";

        // Parâmetros IFC principais (chaves) que serão criados em cada entidade
        private static readonly List<string> IfcParamKeys = new List<string>
        {
            "IfcExportAs",
            "IfcExportType",
            "IfcName",
            "IfcDescription",
            "IfcObjectType",
            "IfcPredefinedType",
            "IfcLongName",
            "IfcTag"
        };

        // Cria (se não existir) o ExtensionDictionary da entidade e retorna o subdicionário IFC
        private static DBDictionary GetOrCreateIfcSubDictionary(Transaction TransCad, Entity ent)
        {
            if (ent.ExtensionDictionary.IsNull)
            {
                ent.CreateExtensionDictionary();
            }

            DBDictionary extDict = (DBDictionary)TransCad.GetObject(ent.ExtensionDictionary, OpenMode.ForWrite);
            DBDictionary ifcDict = null;

            if (extDict.Contains(IfcDictionaryName))
            {
                ObjectId ifcDictId = extDict.GetAt(IfcDictionaryName);
                ifcDict = (DBDictionary)TransCad.GetObject(ifcDictId, OpenMode.ForWrite);
            }
            else
            {
                ifcDict = new DBDictionary();
                ObjectId newId = extDict.SetAt(IfcDictionaryName, ifcDict);
                TransCad.AddNewlyCreatedDBObject(ifcDict, true);
            }

            return ifcDict;
        }

        // Garante que todas as chaves IFC existam como XRecords no subdicionário IFC
        private static void EnsureIfcParamsOnEntity(Transaction TransCad, Entity ent)
        {
            DBDictionary ifcDict = GetOrCreateIfcSubDictionary(TransCad, ent);

            foreach (string key in IfcParamKeys)
            {
                bool exists = ifcDict.Contains(key);
                if (!exists)
                {
                    Xrecord xrec = new Xrecord();
                    // Valor inicial vazio ("")
                    ResultBuffer rb = new ResultBuffer(new TypedValue((int)DxfCode.Text, string.Empty));
                    xrec.Data = rb;

                    ifcDict.SetAt(key, xrec);
                    TransCad.AddNewlyCreatedDBObject(xrec, true);
                }
            }
        }

        // Define (cria se não existir) um parâmetro IFC específico com o valor fornecido
        public static void SetIfcParamOnEntity(Transaction TransCad, Entity ent, string paramName, string value)
        {
            DBDictionary ifcDict = GetOrCreateIfcSubDictionary(TransCad, ent);

            Xrecord xrec = null;
            if (ifcDict.Contains(paramName))
            {
                ObjectId xrecId = ifcDict.GetAt(paramName);
                xrec = (Xrecord)TransCad.GetObject(xrecId, OpenMode.ForWrite);
            }
            else
            {
                xrec = new Xrecord();
                ifcDict.SetAt(paramName, xrec);
                TransCad.AddNewlyCreatedDBObject(xrec, true);
            }

            ResultBuffer rb = new ResultBuffer(new TypedValue((int)DxfCode.Text, value ?? string.Empty));
            xrec.Data = rb;
        }

        // Lê o valor de um parâmetro IFC (retorna null se não existir)
        private static string GetIfcParamFromEntity(Transaction TransCad, Entity ent, string paramName)
        {
            string result = null;

            if (!ent.ExtensionDictionary.IsNull)
            {
                DBDictionary extDict = (DBDictionary)TransCad.GetObject(ent.ExtensionDictionary, OpenMode.ForRead);
                if (extDict.Contains(IfcDictionaryName))
                {
                    ObjectId ifcDictId = extDict.GetAt(IfcDictionaryName);
                    DBDictionary ifcDict = (DBDictionary)TransCad.GetObject(ifcDictId, OpenMode.ForRead);

                    if (ifcDict.Contains(paramName))
                    {
                        ObjectId xrecId = ifcDict.GetAt(paramName);
                        Xrecord xrec = (Xrecord)TransCad.GetObject(xrecId, OpenMode.ForRead);
                        ResultBuffer rb = xrec.Data;
                        if (rb != null)
                        {
                            foreach (TypedValue tv in rb)
                            {
                                if (tv.TypeCode == (int)DxfCode.Text)
                                {
                                    result = tv.Value as string;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        // Exposta para outros usos se necessário
        public static List<string> GetIfcParamNames()
        {
            return new List<string>(IfcParamKeys);
        }

        internal static void EnsureIfcParamsOnEntity1(Transaction transCad, Entity ent)
        {
            EnsureIfcParamsOnEntity(transCad, ent);
        }
    }

    public class IfcParametersCommands
    {
        [CommandMethod("IFC_APLICAR_PARAMETROS")]
        public void AplicarParametrosIfcNaSelecao()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;

            try
            {
                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\nSelecione os objetos para aplicar os parâmetros IFC: ";
                pso.AllowDuplicates = false;

                PromptSelectionResult psr = docEditor.GetSelection(pso);
                if (psr.Status != PromptStatus.OK)
                {
                    docEditor.WriteMessage("\nOperação cancelada.");
                    return;
                }

                Database db = civilDoc.Database;
                using (Transaction TransCad = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        int count = 0;

                        foreach (SelectedObject selObj in psr.Value)
                        {
                            if (selObj == null)
                                continue;

                            Entity ent = (Entity)TransCad.GetObject(selObj.ObjectId, OpenMode.ForWrite);
                            if (ent == null)
                                continue;

                            IfcParametersService.EnsureIfcParamsOnEntity1(TransCad, ent);
                            count++;
                        }

                        TransCad.Commit();
                        docEditor.WriteMessage($"\nParâmetros IFC aplicados a {count} objeto(s).");
                    }
                    catch (Exception ex)
                    {
                        docEditor.WriteMessage($"\nErro ao aplicar parâmetros IFC: {ex.Message}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                // Captura de exceções não-RT
                Application.ShowAlertDialog("Falha na rotina IFC_APLICAR_PARAMETROS:\n" + ex.Message);
            }
        }

        [CommandMethod("IFC_DEFINIR_PARAMETRO")]
        public void DefinirValorParametroIfcNaSelecao()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;

            try
            {
                // 1) Escolher qual parâmetro IFC da lista
                List<string> paramNames = IfcParametersService.GetIfcParamNames();

                PromptKeywordOptions pko = new PromptKeywordOptions("\nEscolha o parâmetro IFC a definir: ");
                pko.AllowArbitraryInput = false;
                pko.AllowNone = false;

                foreach (string name in paramNames)
                {
                    pko.Keywords.Add(name, name, name);
                }

                PromptResult prParam = docEditor.GetKeywords(pko);
                if (prParam.Status != PromptStatus.OK)
                {
                    docEditor.WriteMessage("\nOperação cancelada.");
                    return;
                }

                string chosenParam = prParam.StringResult;

                // 2) Valor a ser atribuído
                PromptStringOptions pso = new PromptStringOptions($"\nInforme o valor para {chosenParam}: ");
                pso.AllowSpaces = true;
                PromptResult prVal = docEditor.GetString(pso);
                if (prVal.Status != PromptStatus.OK)
                {
                    docEditor.WriteMessage("\nOperação cancelada.");
                    return;
                }
                string chosenValue = prVal.StringResult;

                // 3) Seleção dos objetos
                PromptSelectionOptions psoSel = new PromptSelectionOptions();
                psoSel.MessageForAdding = "\nSelecione os objetos que receberão o valor: ";

                PromptSelectionResult psr = docEditor.GetSelection(psoSel);
                if (psr.Status != PromptStatus.OK)
                {
                    docEditor.WriteMessage("\nOperação cancelada.");
                    return;
                }

                Database db = civilDoc.Database;
                using (Transaction TransCad = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        int count = 0;

                        foreach (SelectedObject selObj in psr.Value)
                        {
                            if (selObj == null)
                                continue;

                            Entity ent = (Entity)TransCad.GetObject(selObj.ObjectId, OpenMode.ForWrite);
                            if (ent == null)
                                continue;

                            // Garante que os parâmetros existem e, em seguida, define o valor do selecionado
                            IfcParametersService.EnsureIfcParamsOnEntity1(TransCad, ent);
                            SetIfcParamSafe(TransCad, ent, chosenParam, chosenValue);
                            count++;
                        }

                        TransCad.Commit();
                        docEditor.WriteMessage($"\nValor definido para {count} objeto(s).");
                    }
                    catch (Exception ex)
                    {
                        docEditor.WriteMessage($"\nErro ao definir valor IFC: {ex.Message}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Application.ShowAlertDialog("Falha na rotina IFC_DEFINIR_PARAMETRO:\n" + ex.Message);
            }
        }

        private static void SetIfcParamSafe(Transaction TransCad, Entity ent, string paramName, string value)
        {
            IfcParametersService.SetIfcParamOnEntity(TransCad, ent, paramName, value);
        }
    }
}