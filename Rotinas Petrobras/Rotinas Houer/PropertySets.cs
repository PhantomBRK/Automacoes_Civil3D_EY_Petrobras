using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using DocumentFormat.OpenXml.Vml.Office;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Color = Autodesk.AutoCAD.Colors.Color;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutomacoesCivil3D.Rotinas_Houer
{
    /// <summary>
    /// Classe refatorada para coletar parâmetros de forma genérica de QUALQUER subassembly
    /// e alimentar os Property Sets conforme a lógica já existente.
    /// Mantém as assinaturas de PSet22 e ParametrosGuid2.
    /// </summary>
    public class PropertySets
    {
        // Container por-sólido (evita estáticos “vazando” entre sólidos)
        private class ExtractedValues
        {
            public double Width { get; set; }
            public double Pave1Depth { get; set; }
            public double Pave2Depth { get; set; }
            public double BaseDepth { get; set; }
            public double SubBaseDepth { get; set; }
            public double GuiaDepth { get; set; }
            public double PasseioDepth { get; set; }
            public double Height { get; set; } // para CFTDepth
            public double Slope { get; set; }  // em fração; no final converto para %
            public double LengthKm { get; set; } // já convertido para km
            public double Area { get; set; }

            public void RecomputeArea()
            {
                Area = Width * LengthKm; // mantém sua lógica atual
            }
        }

        public void PSetBody(Body solid, Database db, Transaction tr)
        {
            Editor ed = Manager.DocEditor;
            Database docData = ed.Document.Database;
            string nomeCamada = "";
            string nomeSub = "";
            string nomeCorredor = "";
            string guid = "";
            string comprimentoStr = "";
            string nomeAssembly = "";

            // Dicionários de PSets
            DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
            const string PSET_F = "Corridor Shape Information";

            var values = new ExtractedValues();

            try
            {


                // --- Ler PSet F (mantendo sua lógica) ---
                if (dictionary.Has(PSET_F, tr))
                {
                    ObjectId defIdF = dictionary.GetAt(PSET_F);
                    PropertySetDefinition defF = (PropertySetDefinition)tr.GetObject(defIdF, OpenMode.ForWrite);
                    ObjectId psFId = PropertyDataServices.GetPropertySet(solid, defIdF);
                    PropertySet psF = (PropertySet)tr.GetObject(psFId, OpenMode.ForWrite);
                    //NOME ASSEMBLY NO LAYER
                    int idxAssName = psF.PropertyNameToId("AssemblyName");
                    nomeAssembly = psF.GetAt(idxAssName, solid).ToString();
                    nomeAssembly = LimparPrefixoContador(nomeAssembly);

                    //NOME DO CORREDOR NO LAYER
                    int idxCorrName = psF.PropertyNameToId("CorridorName");
                    nomeCorredor = psF.GetAt(idxCorrName, solid).ToString();
                    nomeCorredor = LimparPrefixoContador(nomeCorredor);

                }

                /*||||||||||||||||EXPORTAÇÃO IFC E LAYERS IFC||||||||||||||||*/
                ApplyToEntity(solid, nomeCorredor, db, tr);
                //ApplyToEntity(solid, nomeAssembly, db, tr);



            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nErro na classe PropertySets3 (PSet22): {ex.Message}");
                ed.WriteMessage($"\nDetalhes: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Mantém a assinatura original. 
        /// Lê dados do PSet B, chama coleta genérica por GUID, e preenche PSet C conforme CodeName.
        /// </summary>
        public void PSetSolid(Solid3d solid, Database db, Transaction tr)
        {
            Editor ed = Manager.DocEditor;
            Document document = Manager.DocCad;
            Database docData = Manager.DocData;

            string nomeCamada = "";
            string nomeSub = "";
            string nomeCorredor = "";
            string guid = "";
            string comprimentoStr = "";
            string nomeAssembly = "";

            // Dicionários de PSets
            DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
            const string PSET_B = "B - Informações dos Objetos e Elementos";
            const string PSET_C = "C - Propriedades Fisicas dos Objetos e Elementos";
            const string PSET_F = "Corridor Shape Information";

            var values = new ExtractedValues();

            try
            {
                // --- Ler PSet B (mantendo sua lógica) ---
                if (dictionary.Has(PSET_B, tr))
                {
                    ObjectId defIdB = dictionary.GetAt(PSET_B);
                    PropertySetDefinition defB = (PropertySetDefinition)tr.GetObject(defIdB, OpenMode.ForWrite);
                    ObjectId psBId = PropertyDataServices.GetPropertySet(solid, defIdB);
                    PropertySet psB = (PropertySet)tr.GetObject(psBId, OpenMode.ForWrite);

                    int idxCodeName = psB.PropertyNameToId("CodeName");
                    int idxSubName = psB.PropertyNameToId("SubassemblyName");
                    int idxAssName = psB.PropertyNameToId("SubassemblyName");
                    int idxCorridorName = psB.PropertyNameToId("NomeCorredorSolido");
                    int idxRegionGUID = psB.PropertyNameToId("RegionName");
                    int idxComprimento = psB.PropertyNameToId("Comprimento");

                    nomeCamada = psB.GetAt(idxCodeName, solid).ToString();
                    nomeSub = psB.GetAt(idxSubName, solid).ToString();
                    nomeAssembly = psB.GetAt(idxAssName, solid).ToString();
                    nomeCorredor = psB.GetAt(idxCorridorName, solid).ToString();
                    guid = psB.GetAt(idxRegionGUID, solid).ToString();
                    comprimentoStr = psB.GetAt(idxComprimento, solid).ToString();

                    DefinirLayerPorCorredor(solid, docData, tr, nomeCorredor);

                    if (double.TryParse(comprimentoStr, out double tempLen))
                        values.LengthKm = tempLen / 1000.0; // manter conversão que você já usa
                    else
                        values.LengthKm = 0.0;

                    // Coleta genérica de parâmetros da subassembly (por GUID / corredor)
                    ColetarParametrosPorGuidGenerico(guid, nomeCorredor, db, tr, values);
                }




                // --- Preencher PSet C conforme sua lógica existente ---
                if (dictionary.Has(PSET_C, tr))
                {
                    ObjectId defIdC = dictionary.GetAt(PSET_C);
                    PropertySetDefinition defC = (PropertySetDefinition)tr.GetObject(defIdC, OpenMode.ForWrite);
                    ObjectId psCId = PropertyDataServices.GetPropertySet(solid, defIdC);
                    PropertySet psC = (PropertySet)tr.GetObject(psCId, OpenMode.ForWrite);

                    // Converter inclinação para percentual no momento de gravar (mantendo seu fluxo)
                    double slopePercent = values.Slope * 10.0;
                    values.RecomputeArea();

                    void SetStr(string prop, string val)
                    {
                        try { psC.SetAt(psC.PropertyNameToId(prop), val); }
                        catch
                        { /* evita travar */
                        }
                    }

                    // Regras idênticas ao que você faz hoje (CodeName contém ...)
                    if (nomeCamada.Contains("PAVIMENTO", StringComparison.OrdinalIgnoreCase) ||
                        nomeCamada.Contains("PAVIMENTO1", StringComparison.OrdinalIgnoreCase) ||
                        nomeCamada.Contains("PAVIMENTO2", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.Pave1Depth.ToString("F2"));
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthKm.ToString("F2"));
                    }

                    if (nomeCamada.Contains("PASSEIO", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.PasseioDepth.ToString("F2"));
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthKm.ToString("F2"));
                    }

                    if (nomeCamada.Contains("BASE", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.BaseDepth.ToString("F2"));
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthKm.ToString("F2"));
                    }

                    if (nomeCamada.Contains("SUB_BASE", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.SubBaseDepth.ToString("F2"));
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthKm.ToString("F2"));
                    }

                    if (nomeCamada.Contains("GUIA", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.GuiaDepth.ToString("F2"));
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthKm.ToString("F2"));
                    }

                    if (nomeCamada.Contains("CFT", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.Height.ToString("F2")); // CFTDepth
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthKm.ToString("F2"));
                    }

                    if (nomeCamada.Contains("Top", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.GuiaDepth.ToString("F2")); // igual ao seu código atual
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthKm.ToString("F2"));
                    }

                    if (nomeCamada.Contains("OFFSET_TALUDE", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.GuiaDepth.ToString("F2")); // igual ao seu código atual
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthKm.ToString("F2"));
                    }

                    if (nomeCamada.Contains("TALUDE_ATERRO", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.GuiaDepth.ToString("F2")); // igual ao seu código atual
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthKm.ToString("F2"));
                    }

                    if (nomeCamada.Contains("TALUDE_CORTE", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.GuiaDepth.ToString("F2")); // igual ao seu código atual
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthKm.ToString("F2"));
                    }

                    if (nomeCamada.Contains("Barreira", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.GuiaDepth.ToString("F2")); // igual ao seu código atual
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthKm.ToString("F2"));
                    }

                    if (nomeCamada.Contains("PONTE", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.GuiaDepth.ToString("F2")); // igual ao seu código atual
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthKm.ToString("F2"));
                    }

                    if (nomeCamada.Contains("Acostamento_pavimento", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.GuiaDepth.ToString("F2")); // igual ao seu código atual
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthKm.ToString("F2"));
                    }
                }

                // --- Ler PSet F (mantendo sua lógica) ---
                if (dictionary.Has(PSET_F, tr))
                {
                    ObjectId defIdF = dictionary.GetAt(PSET_F);
                    PropertySetDefinition defF = (PropertySetDefinition)tr.GetObject(defIdF, OpenMode.ForWrite);
                    ObjectId psFId = PropertyDataServices.GetPropertySet(solid, defIdF);
                    PropertySet psF = (PropertySet)tr.GetObject(psFId, OpenMode.ForWrite);

                    int idxAssName = psF.PropertyNameToId("AssemblyName");
                    nomeAssembly = psF.GetAt(idxAssName, solid).ToString();
                    LimparPrefixoContador(nomeAssembly);
                    //NOME DO CORREDOR NO LAYER
                    int idxCorrName = psF.PropertyNameToId("CorridorName");
                    nomeCorredor = psF.GetAt(idxCorrName, solid).ToString();
                    nomeCorredor = LimparPrefixoContador(nomeCorredor);

                }

                /*||||||||||||||||EXPORTAÇÃO IFC E LAYERS IFC||||||||||||||||*/
                ApplyToEntity(solid, nomeCorredor, db, tr);
                //ApplyToEntity(solid, nomeAssembly, db, tr);


            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nErro na classe PropertySets3 (PSet22): {ex.Message}");
                ed.WriteMessage($"\nDetalhes: {ex.StackTrace}");
            }
        }

        private static void DefinirLayerPorCorredor(
            Entity ent,
            Database db,
            Transaction tr,
            string layerBaseName)
        {
            if (ent == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(layerBaseName))
            {
                return;
            }

            LayerTable layerTable =
                (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (!layerTable.Has(layerBaseName))
            {
                layerTable.UpgradeOpen();

                LayerTableRecord layerRecord = new LayerTableRecord();
                layerRecord.Name = layerBaseName;

                ObjectId layerId = layerTable.Add(layerRecord);
                tr.AddNewlyCreatedDBObject(layerRecord, true);
            }

            ent.Layer = layerBaseName;
        }

        public string LimparPrefixoContador(string s)
        {
            // remove " (n)" ou "(n)" do início, repetidos ou não, e espaços
            string limpo = Regex.Replace(s, @"^(?:\s*\(\d+\)\s*)+", "");
            return limpo.TrimStart();
        }

        /// <summary>
        /// Mantém assinatura original: busca o Region por GUID no corredor e extrai parâmetros
        /// de forma genérica (funciona para QUALQUER subassembly).
        /// </summary>
        public static void ParametrosGuid2(string guid, string nomeCorredorSolido, Database db, Transaction tr)
        {
            // Mantido por compatibilidade, mas agora a extração genérica é feita por ColetarParametrosPorGuidGenerico
            // Este método segue existindo para não quebrar chamadas externas (se houver).
        }

        // ============================================================
        // ===========   IMPLEMENTAÇÃO GENÉRICA: CORE   ===============
        // ============================================================

        private static void ColetarParametrosPorGuidGenerico(
            string guid,
            string nomeCorredorSolido,
            Database db,
            Transaction tr,
            ExtractedValues values)
        {
            Editor ed = Manager.DocEditor;
            CivilDocument docCivil = Manager.DocCivil;

            try
            {
                foreach (ObjectId id in docCivil.CorridorCollection)
                {
                    var corridor = (Corridor)tr.GetObject(id, OpenMode.ForRead);
                    if (!corridor.Name.Contains(nomeCorredorSolido, StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (Baseline bl in corridor.Baselines)
                    {
                        foreach (BaselineRegion region in bl.BaselineRegions)
                        {
                            // Compara GUID da região (normalizando para evitar problemas de case/acentos)
                            if (!region.RegionGUID.ToString().Equals(guid, StringComparison.OrdinalIgnoreCase) &&
                                !region.RegionGUID.ToString().Contains(guid, StringComparison.OrdinalIgnoreCase))
                                continue;

                            // Pega a subassembly aplicada no início da região (mantendo seu padrão)
                            var assembly = bl.GetAppliedAssemblyAtStation(region.StartStation);

                            // Vamos varrer TODAS as subassemblies aplicadas e coletar parâmetros
                            foreach (AppliedSubassembly appliedSub in assembly.GetAppliedSubassemblies())
                            {
                                var sub = (Autodesk.Civil.DatabaseServices.Subassembly)tr.GetObject(appliedSub.SubassemblyId, OpenMode.ForRead);

                                // 1) Doubles
                                foreach (var kv in sub.ParamsDouble)
                                {
                                    string name = kv.Key;
                                    double val = kv.Value;
                                    MapDouble(values, name, val);
                                }

                                // 2) Longs (poucos impactam nos cálculos físicos, mas deixo mapeável se precisar)
                                foreach (var kv in sub.ParamsLong)
                                {
                                    string name = kv.Key;
                                    long val = kv.Value;
                                    MapLong(values, name, val);
                                }

                                // 3) Strings (nomes de camadas/materiais; não entram nos cálculos aqui)
                                // Mantemos a possibilidade de evoluir no futuro:
                                // foreach (var kv in sub.ParamsString) { /* se necessário */ }
                            }

                            // a região correta foi processada; pode sair
                            return;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nErro na coleta genérica por GUID: {ex.Message}");
            }
        }

        // ---------- Mapeamento genérico de nomes -> valores usados na sua lógica ----------
        private static void MapDouble(ExtractedValues v, string paramName, double val)
        {
            // LARGURA
            if (EqualsAny(paramName, "Width", "LaneWidth", "ShoulderWidth"))
                v.Width = val;

            // CAMADAS (profundidades)
            if (EqualsAny(paramName, "Pave1Depth")) v.Pave1Depth = val;
            if (EqualsAny(paramName, "Pave2Depth")) { v.Pave2Depth = val; v.Pave1Depth += val; } // mantém sua soma
            if (ContainsAny(paramName, "BaseDepth")) v.BaseDepth = val;
            if (ContainsAny(paramName, "SubBaseDepth", "SubbaseDepth")) v.SubBaseDepth = val;

            // GUIA / PASSEIO / ALTURA ESPECÍFICA
            if (EqualsAny(paramName, "Depth")) v.GuiaDepth = val;           // usado para GUIA nos seus códigos atuais
            if (EqualsAny(paramName, "SidewalkDepth")) v.PasseioDepth = val; // se existir
            if (ContainsAny(paramName, "CFTDepth")) v.Height = val;         // CFT

            // INCLINAÇÕES
            if (ContainsAny(paramName, "DefaultSlope", "Slope", "ShoulderSlope", "Deflection", "LinkSlope"))
                v.Slope = val;

            // atualiza área sempre que algo relevante muda
            v.RecomputeArea();
        }

        private static void MapLong(ExtractedValues v, string paramName, long val)
        {
            // Por enquanto, não altera largura/altura/slope/área.
            // Se precisar, mapeie enums (Side etc.).
        }

        // Helpers de comparação:
        private static bool EqualsAny(string input, params string[] opts)
        {
            foreach (var o in opts)
                if (input.Equals(o, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool ContainsAny(string input, params string[] opts)
        {
            foreach (var o in opts)
                if (input.IndexOf(o, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }



        private static readonly Dictionary<string, short> LayerColors = new Dictionary<string, short>
        {
            { "DREN_TUBO_IFC",        3 },   // Verde
            { "DREN_BUEIRO_IFC",      130 },
            { "DREN_CONEXAO_IFC",     6 },   // Magenta
            { "DREN_ABERTA_IFC",      1 },   // Vermelho
            { "DREN_ESTRUTURA_IFC",   5 },   // Azul
            { "IFC_PROXY",            8 }    // Cinza
        };



        public static void ApplyToEntity(Entity entity, string corridorName, Database db, Transaction tr)
        {
            Document docCad = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;

            // garante layers prontos
            EnsureLayer(db, tr, corridorName, (short)7);


            //string ifcClass = "IfcBuildingElementProxy";
            //string predef = "USERDEFINED";
            string tgtLayer = $"PAV_{corridorName}";

            string key = (corridorName ?? "").ToUpperInvariant();


            // aplica layer
            SetEntityLayer(entity, db, tr, tgtLayer);

        }

        // Helpers
        private static void EnsureLayer(Database db, Transaction tr, string layerName, short aci)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName)) return;

            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord();
            ltr.Name = layerName;
            ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
            ObjectId newId = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        private static void SetEntityLayer(Entity ent, Database db, Transaction tr, string layerName)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
                EnsureLayer(db, tr, layerName, (short)7);

            ent.UpgradeOpen();
            ent.Layer = layerName;
        }

        /*||||||||||||||||EXPORTAÇÃO IFC E LAYERS IFC||||||||||||||||

        private static readonly Dictionary<string, short> LayerColors = new Dictionary<string, short>
        {
            { "DREN_TUBO_IFC",        3 },   // Verde
            { "DREN_BUEIRO_IFC",      130 },
            { "DREN_CONEXAO_IFC",     6 },   // Magenta
            { "DREN_ABERTA_IFC",      1 },   // Vermelho
            { "DREN_ESTRUTURA_IFC",   5 },   // Azul
            { "IFC_PROXY",            8 }    // Cinza
        };

        // 1) Cria TODOS os layers IFC padronizados (chamar 1x por transação)
        public static void EnsureIfcLayers(Database db, Transaction tr)
        {
            // Sempre inclui PROXY
            HashSet<string> set = new HashSet<string>(LayerColors.Keys);
            foreach (KeyValuePair<string, (string IfcClass, string PreType, string Layer)> kv in Rules)
                set.Add(kv.Value.Layer);

            foreach (string layerName in set)
                EnsureLayer(db, tr, layerName, LayerColors.ContainsKey(layerName) ? LayerColors[layerName] : (short)7);
        }

        public static void ApplyToEntity(Entity entity, string codeName, Database db, Transaction tr)
        {
            Document docCad = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;

            // garante layers prontos
            EnsureIfcLayers(db, tr);


            string ifcClass = "IfcBuildingElementProxy";
            string predef = "USERDEFINED";
            string tgtLayer = "IFC_PROXY";

            string key = (codeName ?? "").ToUpperInvariant();
            foreach (KeyValuePair<string, (string IfcClass, string PreType, string Layer)> kv in Rules)
            {
                if (key.Contains(kv.Key))
                {
                    ifcClass = kv.Value.IfcClass;
                    predef = kv.Value.PreType;
                    tgtLayer = kv.Value.Layer;
                    break;
                }
            }

            // aplica layer
            SetEntityLayer(entity, db, tr, tgtLayer);
            // injeta PSET IFC:: se existir no DWG
            string psetIfc = "IfcObject Properties";
            DictionaryPropertySetDefinitions dict = new DictionaryPropertySetDefinitions(db);
            PropertySetDefinition novo = new PropertySetDefinition();

            if (dict.Has(psetIfc, tr))
            {
                ObjectId defId = dict.GetAt(psetIfc);
                PropertyDataServices.AddPropertySet(entity, defId);
                PropertySet pset = (PropertySet)tr.GetObject(PropertyDataServices.GetPropertySet(entity, defId), OpenMode.ForWrite);


                int idExportAs = pset.PropertyNameToId("IFC::IfcExportAs");
                if (idExportAs != -1) pset.SetAt(idExportAs, ifcClass);

                int idPreType = pset.PropertyNameToId("IFC::PredefinedType");
                if (idPreType != -1) pset.SetAt(idPreType, predef);
            }

        }

        // Helpers
        private static void EnsureLayer(Database db, Transaction tr, string layerName, short aci)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName)) return;

            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord();
            ltr.Name = layerName;
            ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
            ObjectId newId = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        private static void SetEntityLayer(Entity ent, Database db, Transaction tr, string layerName)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
                EnsureLayer(db, tr, layerName, (short)7);

            ent.UpgradeOpen();
            ent.Layer = layerName;
        }


        private static readonly Dictionary<string, (string IfcClass, string PreType, string Layer)> Rules =
               new Dictionary<string, (string, string, string)>
               {
                { "BUEIRO",   ("IfcPipeSegment", "CULVERT",       "DREN_BUEIRO_IFC") },
                { "TUBO",     ("IfcPipeSegment", "RIGIDSEGMENT",  "DREN_TUBO_IFC") },
                { "JOELHO",   ("IfcPipeFitting", "BEND",          "DREN_CONEXAO_IFC") },
                { "CURVA",    ("IfcPipeFitting", "BEND",          "DREN_CONEXAO_IFC") },
                { "TEE",      ("IfcPipeFitting", "JUNCTION",      "DREN_CONEXAO_IFC") },
                { "JUNCTION", ("IfcPipeFitting", "JUNCTION",      "DREN_CONEXAO_IFC") },
                { "REDU",     ("IfcPipeFitting", "TRANSITION",    "DREN_CONEXAO_IFC") },
                { "TRANSITION",("IfcPipeFitting","TRANSITION",    "DREN_CONEXAO_IFC") },
                { "VALETA",   ("IfcPipeSegment", "GUTTER",        "DREN_ABERTA_IFC") },
                { "CANALETA", ("IfcPipeSegment", "GUTTER",        "DREN_ABERTA_IFC") },
                { "DESCIDA",  ("IfcPipeSegment", "GUTTER",        "DREN_ABERTA_IFC") },
                { "PV",       ("IfcDistributionChamberElement","MANHOLE","DREN_ESTRUTURA_IFC") },
                { "MANHOLE",  ("IfcDistributionChamberElement","MANHOLE","DREN_ESTRUTURA_IFC") },
                { "BL",       ("IfcDistributionChamberElement","INLET",  "DREN_ESTRUTURA_IFC") },
                { "INLET",    ("IfcDistributionChamberElement","INLET",  "DREN_ESTRUTURA_IFC") },
               };


        */
    }
}