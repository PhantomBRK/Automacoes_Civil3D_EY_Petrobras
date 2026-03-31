// Para PropertySetDefinition, PropertyDefinition, PropertySetData, PropertySetDefinitionServices, PropertySetDataServices
// Estas classes são fundamentais para trabalhar com Property Sets e estão tipicamente na biblioteca AecBaseMgd.dll,
// que deve ser referenciada no seu projeto (geralmente localizada em C:\Program Files\Autodesk\AutoCAD 20xx\AecBaseMgd.dll).
using Autodesk.Aec.PropertyData.DatabaseServices; // Importante!
using Autodesk.AutoCAD.ApplicationServices;
// Certifique-se de adicionar as seguintes referências ao seu projeto no Visual Studio:
// - AcCoreMgd.dll
// - AcDbMgd.dll
// - AcMgd.dll
// - AecBaseMgd.dll (essencial para DataStore e outras funcionalidades AEC)
// - AecPropDataMgd.dll (para as classes de Property Set)


using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms.Design;

// Aliases para evitar conflitos de namespace, conforme sua instrução.
// É uma boa prática para melhorar a legibilidade e evitar ambiguidades.
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using ObjectIdCollection = Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection;
using Region = Autodesk.AutoCAD.DatabaseServices.Region;

namespace AutomacoesCivil3D
{
    public class PropertySets2
    {

        static double width = 0;
        static double baseDepth = 0;
        static double subBaseDepth = 0;
        static double pave1Depth = 0;
        static double depth = 0;
        static double height = 0;
        static double length = 0;
        static double guiaDepth = 0;
        static double passeioDepth = 0;
        static double slope = 0;

        [CommandMethod("AplicarPSet")]
        public void pSet21()
        {
            // Obter o banco de dados atual do projeto
            Database db = Manager.DocData;
            Editor docEditor = Manager.DocEditor;
            CivilDocument docCivil = Manager.DocCivil;

            var nomeCamada = "";
            var nomeSub = "";
            var nomeCorredor = "";
            var guid = "";

            double areaTotalPista = 0.0;
            double areaTotalPasseio = 0.0;

            // Seleção da rede de drenagem
            PromptEntityOptions peo = new PromptEntityOptions("\nSelecione o Objeto");
            peo.SetRejectMessage("\nPor favor, selecione apenas Tubos da Rede.");
            peo.AddAllowedClass(typeof(Solid3d), false);
            PromptEntityResult per = docEditor.GetEntity(peo);
            // Verificar se a seleção foi bem-sucedida
            if (per.Status != PromptStatus.OK)
            {
                docEditor.WriteMessage("\nNão foi possível selecionar uma rede de drenagem.");
                return;
            }

            // Iniciar uma transação
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Acessar o dicionário de property sets
                DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);

                Solid3d solid = (Solid3d)tr.GetObject(per.ObjectId, OpenMode.ForRead);

                // Nome do property set (ajuste conforme o seu projeto)
                string propSetNameB = "B - Informações dos Objetos e Elementos";

                // Verificar se o property set existe
                if (dictionary.Has(propSetNameB, tr))
                {

                    // Obtem o ID do propertySetDefinition a partir do dicionário
                    ObjectId propSetId = dictionary.GetAt(propSetNameB);
                    // Obter o objeto PropertySetDefinition
                    PropertySetDefinition propSetDef = (PropertySetDefinition)tr.GetObject(propSetId, OpenMode.ForWrite);

                    System.Windows.Forms.MessageBox.Show($"Pset de nome {propSetDef.Name} encontada.");

                    // Obtem o ID da propriedade associada ao property set
                    ObjectId psetsB = PropertyDataServices.GetPropertySet(solid, propSetId);
                    // Obtem o objeto PropertySet associado ao ID
                    PropertySet psetB = (PropertySet)tr.GetObject(psetsB, OpenMode.ForWrite);

                    int indexNomeCamada = psetB.PropertyNameToId("CodeName");
                    int indexNomeSub = psetB.PropertyNameToId("SubassemblyName");
                    int indexNomeCorredor = psetB.PropertyNameToId("NomeCorredorSolido");
                    int indexRegionGUID = psetB.PropertyNameToId("RegionName");

                    // Define o valor do campo do property set altura
                    nomeCamada = psetB.GetAt(indexNomeCamada, solid).ToString();
                    docEditor.WriteMessage($"\nO nome da camada é: {nomeCamada}");
                    nomeSub = psetB.GetAt(indexNomeSub, solid).ToString();
                    docEditor.WriteMessage($"\nO nome do sub é: {nomeSub}");
                    nomeCorredor = psetB.GetAt(indexNomeCorredor, solid).ToString();
                    docEditor.WriteMessage($"\nO nome do corredor é: {nomeCorredor}");
                    guid = psetB.GetAt(indexRegionGUID, solid).ToString();
                    docEditor.WriteMessage($"\nO GUID da região é: {guid}");

                    ParametrosGuid(guid, nomeCorredor, per);
                }

                // Nome do property set (ajuste conforme o seu projeto)
                string propSetName = "C - Propriedades Fisicas dos Objetos e Elementos";

                if (dictionary.Has(propSetName, tr))
                {

                    // Obtem o ID do propertySetDefinition a partir do dicionário
                    ObjectId propSetId = dictionary.GetAt(propSetName);
                    // Obter o objeto PropertySetDefinition
                    PropertySetDefinition propSetDef = (PropertySetDefinition)tr.GetObject(propSetId, OpenMode.ForWrite);

                    System.Windows.Forms.MessageBox.Show($"Pset de nome {propSetDef.Name} encontada.");

                    // Obtem o ID da propriedade associada ao property set
                    ObjectId psets = PropertyDataServices.GetPropertySet(solid, propSetId);
                    // Obtem o objeto PropertySet associado ao ID
                    PropertySet pset = (PropertySet)tr.GetObject(psets, OpenMode.ForWrite);


                    // Define valores sobre o pavimento
                    if (nomeCamada.Contains("PAVIMENTO") && !nomeSub.Contains("PASSEIO"))
                    {
                        int index2 = pset.PropertyNameToId("Largura");
                        // Define o valor do campo do property set largura
                        pset.SetAt(index2, width.ToString("F2"));

                        int index1 = pset.PropertyNameToId("Altura");
                        // Define o valor do campo do property set altura
                        pset.SetAt(index1, pave1Depth.ToString("F2"));

                        int index3 = pset.PropertyNameToId("Inclinação");
                        // Define o valor do campo do property set altura
                        pset.SetAt(index3, slope.ToString("F2"));



                    }

                    // Define valores sobre o passeio
                    if (nomeCamada.Contains("PAVIMENTO") && nomeSub.Contains("PASSEIO"))
                    {
                        int index2 = pset.PropertyNameToId("Largura");
                        // Define o valor do campo do property set largura
                        pset.SetAt(index2, width.ToString("F2"));

                        int index1 = pset.PropertyNameToId("Altura");
                        // Define o valor do campo do property set altura
                        pset.SetAt(index1, passeioDepth.ToString("F2"));

                        int index3 = pset.PropertyNameToId("Inclinação");
                        // Define o valor do campo do property set altura
                        pset.SetAt(index3, slope.ToString("F2"));
                    }

                    if (nomeCamada == "BASE")
                    {
                        int index2 = pset.PropertyNameToId("Largura");
                        // Define o valor do campo do property set largura
                        pset.SetAt(index2, width.ToString("F2"));

                        int index1 = pset.PropertyNameToId("Altura");
                        // Define o valor do campo do property set altura
                        pset.SetAt(index1, baseDepth.ToString("F2"));

                        int index3 = pset.PropertyNameToId("Inclinação");
                        // Define o valor do campo do property set altura
                        pset.SetAt(index3, slope.ToString("F2"));


                    }

                    if (nomeCamada == "SUB_BASE")
                    {
                        int index2 = pset.PropertyNameToId("Largura");
                        // Define o valor do campo do property set largura
                        pset.SetAt(index2, width.ToString("F2"));

                        int index1 = pset.PropertyNameToId("Altura");
                        // Define o valor do campo do property set altura
                        pset.SetAt(index1, subBaseDepth.ToString("F2"));

                        int index3 = pset.PropertyNameToId("Inclinação");
                        // Define o valor do campo do property set altura
                        pset.SetAt(index3, slope.ToString("F2"));


                    }



                    if (nomeCamada == "GUIA")
                    {
                        int index2 = pset.PropertyNameToId("Largura");
                        // Define o valor do campo do property set largura
                        pset.SetAt(index2, width.ToString("F2"));

                        int index1 = pset.PropertyNameToId("Altura");
                        // Define o valor do campo do property set altura
                        pset.SetAt(index1, guiaDepth.ToString("F2"));

                        int index3 = pset.PropertyNameToId("Inclinação");
                        // Define o valor do campo do property set altura
                        pset.SetAt(index3, slope.ToString("F2"));


                    }
                    else
                    {
                        System.Windows.Forms.MessageBox.Show($"O campo NomeCamada já está preenchido com o valor: {nomeCamada}");
                    }




                    




                }





                /*foreach (ObjectId id in db.GetCivilAlignmentIds())
                {
                    // Obter o nome do property set
                    Alignment alignment = (Alignment)tr.GetObject(id, OpenMode.ForRead);
                    if (alignment.Name == "DML - M14")
                    {
                        // Associa o conjunto de propriedades ao objeto                           
                        PropertyDataServices.AddPropertySet(alignment, propSetDef.Id);


                    }


                }*/







                // Iterar sobre os campos do property set
                /* foreach (PropertyDefinition propDef1 in propSetDef.Definitions)
                 {
                     /// Verificar se o campo é do tipo texto e definir o valor "teste"
                     if (propDef1.Name == "D1 - Identificação resumida do elemento")
                     {
                         propDef1.DefaultData = "teste1";
                         // Associa o conjunto de propriedades ao objeto                           
                         PropertyDataServices.AddPropertySet(corridor, propSetDef.Id);
                     }
                     if (propDef1.Name == "D2 - Disciplina (AWP)")
                     {
                         propDef1.DefaultData = "Pavimentação";
                         PropertyDataServices.AddPropertySet(corridor, propSetDef.Id);
                     }

                     if (propDef1.Name == "D3 - EAP (Área/Subárea)")
                     {
                         propDef1.DefaultData = "TOP-004";
                     }
                     if (propDef1.Name == "D10 - CWA (Construction Work Area)")
                     {
                         propDef1.DefaultData = "Pavimentação";
                     }
                     /*if (propDef1.Name == "D11 - CWP (Construction Work Package)")
                     {
                         propDef1.DefaultData = "Pavimentação";
                     }
                     if (propDef1.Name == "D12 - EWP (Engineering Work Package)")
                     {
                         propDef1.DefaultData = "Pavimentação";
                     }
                     if (propDef1.Name == "D13 - IWP (Installation Work Package)")
                     {
                         propDef1.DefaultData = "Pavimentação";
                     }
                     if (propDef1.Name == "D14 - PWP (Procurement Work Package)")
                     {
                         propDef1.DefaultData = "Pavimentação";
                     }
                 }*/

                // Salvar as alterações
                tr.Commit();
            }
                

        }


            




        

        public static void ParametrosGuid(string guid, string nomeCorredorSolido, PromptEntityResult per)
        {

            Database db = Manager.DocData;
            Editor docEditor = Manager.DocEditor;
            CivilDocument docCivil = Manager.DocCivil;

            // Iniciar uma transação
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                                
                foreach (ObjectId id in docCivil.CorridorCollection)
                {
                    Corridor corridor = (Corridor)tr.GetObject(id, OpenMode.ForRead);

                    if(corridor.Name == nomeCorredorSolido)
                    {
                        foreach (Baseline baseLine in corridor.Baselines)
                        {
                            
                            foreach (BaselineRegion region in baseLine.BaselineRegions)
                            {

                                //docEditor.WriteMessage($"\nA REGIONGUID DO CORREDOR É: {region.RegionGUID.ToString()} ");
                                
                                if (region.Name.ToString().Contains(guid))
                                {
                                    //docEditor.WriteMessage($"\nENTRAMOS NA REGIAO {region.Name} ");
                                    AppliedAssembly assembly = baseLine.GetAppliedAssemblyAtStation(region.StartStation);
                                    foreach (AppliedSubassembly appliedSub in assembly.GetAppliedSubassemblies())
                                    {
                                        //docEditor.WriteMessage($"\nENTRAMOS NA ASSEMBLY {appliedSub.CorridorId.ToString()} ");
                                        Autodesk.Civil.DatabaseServices.Subassembly sub = (Autodesk.Civil.DatabaseServices.Subassembly)tr.GetObject(appliedSub.SubassemblyId, OpenMode.ForRead);
                                        docEditor.WriteMessage($"\nA CLASS NAME E: {sub.GeometryGenerator.MacroOrClassName} ");

                                        if (sub.GeometryGenerator.MacroOrClassName.Contains("LaneSuperelevationAOR"))
                                        {
                                            
                                            foreach (var param in sub.ParamsDouble)
                                            {
                                                string paramName = param.Key;
                                                docEditor.WriteMessage($"\nNome do Parametro: {paramName}");

                                                if (paramName == "Width")
                                                {
                                                    width = param.Value;
                                                    docEditor.WriteMessage($"\nValor Largura: {width}");
                                                }
                                                if (paramName == "Pave1Depth")
                                                {
                                                    pave1Depth = param.Value;
                                                    docEditor.WriteMessage($"\nValor Pave1Depth: {pave1Depth}");

                                                }
                                                if (paramName == "Pave2Depth")
                                                {
                                                    pave1Depth += param.Value;
                                                    docEditor.WriteMessage($"\nValor Pave2Depth: {pave1Depth}");
                                                }
                                                if (paramName.Contains("BaseDepth"))
                                                {
                                                    baseDepth = param.Value;
                                                    docEditor.WriteMessage($"\nValor BaseDepth: {baseDepth}");
                                                }
                                                if (paramName.Contains("SubBaseDepth"))
                                                {
                                                    subBaseDepth = param.Value;
                                                    docEditor.WriteMessage($"\nValor SubBaseDepth: {subBaseDepth}");
                                                }
                                                if (paramName.Contains("DefaultSlope"))
                                                {
                                                    slope = param.Value;
                                                    docEditor.WriteMessage($"\nValor DefaultSlope: {slope}");
                                                }

                                            }

                                        }
                                        else
                                        {
                                            if (sub.GeometryGenerator.MacroOrClassName.Contains("BasicLane"))
                                            {
                                                //docEditor.WriteMessage($"\nSub-assembly encontrada: {sub.Name}");
                                                //docEditor.WriteMessage($"\nComprimento do trecho: {length.ToString("F2")} metros");

                                                foreach (var param in sub.ParamsDouble)
                                                {
                                                    string paramName = param.Key;

                                                    if (paramName == "Width")
                                                    {
                                                        width = param.Value;
                                                    }
                                                    if (paramName == "Depth")
                                                    {
                                                        pave1Depth = param.Value;
                                                    }

                                                    if (paramName.Contains("Slope"))
                                                    {
                                                        slope = param.Value;
                                                    }

                                                }

                                            }
                                            else
                                            {

                                                if (sub.GeometryGenerator.MacroOrClassName.Contains("BasicCurb"))
                                                {
                                                    //docEditor.WriteMessage($"\nSub-assembly encontrada: {sub.Name}");
                                                    //docEditor.WriteMessage($"\nComprimento do trecho: {length.ToString("F2")} metros");

                                                    foreach (var param in sub.ParamsDouble)
                                                    {
                                                        string paramName = param.Key;

                                                        if (paramName == "Width")
                                                        {
                                                            width = param.Value;
                                                        }
                                                        if (paramName == "Depth")
                                                        {
                                                            guiaDepth = param.Value;
                                                        }

                                                        if (paramName.Contains("Deflection"))
                                                        {
                                                            slope = param.Value;
                                                        }

                                                    }

                                                }

                                                else
                                                {

                                                    if (sub.GeometryGenerator.MacroOrClassName.Contains("ShoulderExtendAll"))
                                                    {
                                                        //docEditor.WriteMessage($"\nSub-assembly encontrada: {sub.Name}");
                                                        //docEditor.WriteMessage($"\nComprimento do trecho: {length.ToString("F2")} metros");

                                                        foreach (var param in sub.ParamsDouble)
                                                        {
                                                            string paramName = param.Key;

                                                            if (paramName == "ShoulderWidth")
                                                            {
                                                                width = param.Value;
                                                            }
                                                            if (paramName == "Pave1Depth")
                                                            {
                                                                pave1Depth = param.Value;
                                                            }
                                                            if (paramName == "Pave2Depth")
                                                            {
                                                                pave1Depth += param.Value;
                                                            }
                                                            if (paramName.Contains("BaseDepth"))
                                                            {
                                                                baseDepth = param.Value;
                                                            }
                                                            if (paramName.Contains("SubbaseDepth"))
                                                            {
                                                                subBaseDepth = param.Value;
                                                            }
                                                            if (paramName.Contains("ShoulderSlope"))
                                                            {
                                                                slope = param.Value;
                                                            }

                                                        }


                                                    }
                                                    else
                                                    {
                                                        //docEditor.WriteMessage($"\nSub-assembly não encontrada: {sub.Name}");
                                                    }

                                                }
                                            }
                                        }
                                    }// Fim do foreach AppliedSubassembly

                                }
                                else
                                {
                                    //docEditor.WriteMessage($"\nA região com GUID {guid} já foi processada.");
                                }


                            }// Fim do foreach BaselineRegion

                        } // Fim do foreach Baseline




                    }
                    

                }// Fim do foreach Corridor



                tr.Commit();
            }
        }
      
    }
}
