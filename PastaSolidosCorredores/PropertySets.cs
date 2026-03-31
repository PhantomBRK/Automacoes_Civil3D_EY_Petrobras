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
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Color = Autodesk.AutoCAD.Colors.Color;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutomacoesCivil3D
{
    /// <summary>
    /// Classe refatorada para coletar parÃ¢metros de forma genÃ©rica de QUALQUER subassembly
    /// e alimentar os Property Sets conforme a lÃ³gica jÃ¡ existente.
    /// MantÃ©m as assinaturas de PSet22 e ParametrosGuid2.
    /// </summary>
    public class PropertySets
    {
        private static readonly Dictionary<string, string[]> PropertySetAliasesRuntime = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Pset_Rodoviario"] = new[] { "Pset_Rodoviario", "PSET_PETROBRAS_RODOVIARIO" },
            ["Pset_Pavimentacao"] = new[] { "Pset_Pavimentacao", "PSET_PETROBRAS_PAVIMENTACAO" },
            ["Pset_Terraplenagem"] = new[] { "Pset_Terraplenagem", "PSET_PETROBRAS_TERRAPLENAGEM" }
        };

	        // ============================================================
	        // ===============   HELPERS (ROBUSTEZ)   =====================
	        // ============================================================

	        private static bool TryGetAecPropId(PropertySet ps, string propName, out int id)
	        {
	            id = -1;
	            if (ps == null || string.IsNullOrWhiteSpace(propName)) return false;
	            try
	            {
	                id = ps.PropertyNameToId(propName);
	                return id >= 0;
	            }
	            catch
	            {
	                id = -1;
	                return false;
	            }
	        }

	        private static bool TryGetAecValue(PropertySet ps, Entity host, string propName, out string value)
	        {
	            value = string.Empty;
	            if (ps == null || host == null) return false;
	            if (!TryGetAecPropId(ps, propName, out int id)) return false;
	
	            try
	            {
	                object v = ps.GetAt(id, host);
	                value = v?.ToString() ?? string.Empty;
	                return !string.IsNullOrWhiteSpace(value);
	            }
	            catch
	            {
	                value = string.Empty;
	                return false;
	            }
	        }

	        private static ObjectId GetPropertySetIdSafe(Entity ent, ObjectId defId)
	        {
	            if (ent == null || defId == ObjectId.Null) return ObjectId.Null;
	            try
	            {
	                return PropertyDataServices.GetPropertySet(ent, defId);
	            }
	            catch
	            {
	                return ObjectId.Null;
	            }
	        }

	        private static string SanitizeLayerName(string layerName)
	        {
	            if (string.IsNullOrWhiteSpace(layerName)) return string.Empty;
	
	            // AutoCAD invalida: <>/\\":;?*|,=
	            string cleaned = layerName;
	            char[] invalid = new[] { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=' };
	            foreach (char c in invalid)
	                cleaned = cleaned.Replace(c, '_');
	
	            // remove controle / quebras
	            cleaned = Regex.Replace(cleaned, @"[\r\n\t]", " ");
	
	            // colapsa espaÃ§os
	            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
	
	            // opcional: troca espaÃ§o por '_' para evitar variaÃ§Ãµes
	            cleaned = cleaned.Replace(' ', '_');
	
	            // limite conservador
	            if (cleaned.Length > 200)
	                cleaned = cleaned.Substring(0, 200);
	
	            return cleaned;
	        }
        // Container por-sÃ³lido (evita estÃ¡ticos â€œvazandoâ€ entre sÃ³lidos)
        private class ExtractedValues
        {
            public double Width { get; set; }
            public double Pave1Depth { get; set; }
            public double Pave2Depth { get; set; }
            public double BaseDepth { get; set; }
            public double SubBaseDepth { get; set; }
            public double GuiaDepth { get; set; }
            public double PasseioDepth { get; set; }
            public double ExtentHeight { get; set; }
            public double LengthMeters { get; set; }
            public double Volume { get; set; }
            public double CenterE { get; set; }
            public double CenterN { get; set; }
            public double CenterZ { get; set; }
            public double StartStation { get; set; } = double.NaN;
            public double EndStation { get; set; } = double.NaN;
            public string CodeName { get; set; } = string.Empty;
            public string SubassemblyName { get; set; } = string.Empty;
            public string AssemblyName { get; set; } = string.Empty;
            public string AssemblyHandle { get; set; } = string.Empty;
            public string SubassemblyHandle { get; set; } = string.Empty;
            public string CorridorName { get; set; } = string.Empty;
            public string RegionGuid { get; set; } = string.Empty;
            public string Disciplina { get; set; } = string.Empty;
            public string Localizacao { get; set; } = string.Empty;
            public string Situacao { get; set; } = string.Empty;
            public string Lado { get; set; } = string.Empty;
            public string CodigoObjeto { get; set; } = string.Empty;
            public string Material { get; set; } = string.Empty;
            public string Camada { get; set; } = string.Empty;
            public string FuncaoCamada { get; set; } = string.Empty;
            public string TipoPavimento { get; set; } = string.Empty;
            public string NaturezaMovimentoTerra { get; set; } = string.Empty;
            public string ProjectId { get; set; } = string.Empty;
            public string ProjectName { get; set; } = string.Empty;
            public bool IsPavement { get; set; }
            public bool IsEarthworks { get; set; }
            public bool IsCut { get; set; }
            public bool IsFill { get; set; }
            public bool IsMilling { get; set; }
            public bool IsTransition { get; set; }
            public double Height { get; set; } // para CFTDepth
            public double Slope { get; set; }  // em fraÃ§Ã£o; no final converto para %
            public double LengthKm { get; set; } // jÃ¡ convertido para km
            public double Area { get; set; }

            public void RecomputeArea()
            {
                double effectiveLengthMeters = LengthMeters > 0.0
                    ? LengthMeters
                    : LengthKm > 0.0 ? LengthKm * 1000.0 : 0.0;

                Area = (Width > 0.0 && effectiveLengthMeters > 0.0)
                    ? Width * effectiveLengthMeters
                    : 0.0;
            }
        }

        public void PSetBody(Body solid, Database db, Transaction tr)
        {
            ApplyRoadworksPropertySetsRuntime(solid, db, tr);
            return;

            Editor ed = Manager.DocEditor;
            Database docData = ed.Document.Database;
            string nomeCamada = "";
            string nomeSub = "";
            string nomeCorredor = "";
            string guid = "";
            string comprimentoStr = "";
            string nomeAssembly = "";

            // DicionÃ¡rios de PSets
            DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
            const string PSET_B = "B - Informações dos Objetos e Elementos";
            const string PSET_F = "Corridor Shape Information";

            var values = new ExtractedValues();

            try
            {


                
                // --- Ler PSet B (CodeName) para classificar pavimentaÃ§Ã£o no IFC ---
                if (dictionary.Has(PSET_B, tr))
                {
                    ObjectId defIdB = dictionary.GetAt(PSET_B);
                    PropertySetDefinition defB = (PropertySetDefinition)tr.GetObject(defIdB, OpenMode.ForWrite);
	                    ObjectId psBId = GetPropertySetIdSafe(solid, defIdB);
                    if (psBId != ObjectId.Null)
                    {
                        PropertySet psB = (PropertySet)tr.GetObject(psBId, OpenMode.ForWrite);
	                        TryGetAecValue(psB, solid, "CodeName", out nomeCamada);
                    }
                }

                // --- Ler PSet F (mantendo sua lÃ³gica) ---
                if (dictionary.Has(PSET_F, tr))
                {
                    ObjectId defIdF = dictionary.GetAt(PSET_F);
                    PropertySetDefinition defF = (PropertySetDefinition)tr.GetObject(defIdF, OpenMode.ForWrite);
	                    ObjectId psFId = GetPropertySetIdSafe(solid, defIdF);
	                    if (psFId != ObjectId.Null)
	                    {
	                        PropertySet psF = (PropertySet)tr.GetObject(psFId, OpenMode.ForWrite);

	                        if (TryGetAecValue(psF, solid, "AssemblyName", out string tmpAss))
	                            nomeAssembly = LimparPrefixoContador(tmpAss);

	                        if (TryGetAecValue(psF, solid, "CorridorName", out string tmpCorr))
	                            nomeCorredor = LimparPrefixoContador(tmpCorr);
	                    }

                }

                /*||||||||||||||||EXPORTAÃ‡ÃƒO IFC E LAYERS IFC||||||||||||||||*/
                ApplyToEntity(solid, nomeCamada, db, tr);
                //ApplyToEntity(solid, nomeAssembly, db, tr);



            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nErro na classe PropertySets3 (PSet22): {ex.Message}");
                ed.WriteMessage($"\nDetalhes: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// MantÃ©m a assinatura original. 
        /// LÃª dados do PSet B, chama coleta genÃ©rica por GUID, e preenche PSet C conforme CodeName.
        /// </summary>
        public void PSetSolid(Solid3d solid, Database db, Transaction tr)
        {
            ApplyRoadworksPropertySetsRuntime(solid, db, tr);
            return;

            Editor ed = Manager.DocEditor;
            Document document = Manager.DocCad;
            Database docData = Manager.DocData;

            string nomeCamada = "";
            string nomeSub = "";
            string nomeCorredor = "";
            string guid = "";
            string comprimentoStr = "";
            string nomeAssembly = "";

            // DicionÃ¡rios de PSets
            DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
            const string PSET_B = "B - Informações dos Objetos e Elementos";
            const string PSET_C = "C - Propriedades Físicas dos Objetos e Elementos";
            const string PSET_F = "Corridor Shape Information";

            var values = new ExtractedValues();

            try
            {
                // --- Ler PSet B (mantendo sua lÃ³gica) ---
                if (dictionary.Has(PSET_B, tr))
                {
                    ObjectId defIdB = dictionary.GetAt(PSET_B);
                    PropertySetDefinition defB = (PropertySetDefinition)tr.GetObject(defIdB, OpenMode.ForWrite);
	                    ObjectId psBId = GetPropertySetIdSafe(solid, defIdB);
	                    if (psBId != ObjectId.Null)
	                    {
	                        PropertySet psB = (PropertySet)tr.GetObject(psBId, OpenMode.ForWrite);

	                        TryGetAecValue(psB, solid, "CodeName", out nomeCamada);
	                        TryGetAecValue(psB, solid, "SubassemblyName", out nomeSub);

	                        // Alguns templates usam nomes diferentes.
	                        if (!TryGetAecValue(psB, solid, "NomeCorredorSolido", out nomeCorredor))
	                            TryGetAecValue(psB, solid, "CorridorName", out nomeCorredor);

	                        if (!TryGetAecValue(psB, solid, "RegionName", out guid))
	                            TryGetAecValue(psB, solid, "RegionGUID", out guid);

	                        TryGetAecValue(psB, solid, "Comprimento", out comprimentoStr);
	                    }

                    if (double.TryParse(comprimentoStr, out double tempLen))
                        values.LengthKm = tempLen / 1000.0; // manter conversÃ£o que vocÃª jÃ¡ usa
                    else
                        values.LengthKm = 0.0;

	                    // Coleta genÃ©rica de parÃ¢metros da subassembly (por GUID / corredor)
	                    if (!string.IsNullOrWhiteSpace(guid) && !string.IsNullOrWhiteSpace(nomeCorredor))
	                    {
	                        ColetarParametrosPorGuidGenerico(guid, nomeCorredor, db, tr, values);
	                    }
                }




                // --- Preencher PSet C conforme sua lÃ³gica existente ---
                if (dictionary.Has(PSET_C, tr))
                {
                    ObjectId defIdC = dictionary.GetAt(PSET_C);
                    PropertySetDefinition defC = (PropertySetDefinition)tr.GetObject(defIdC, OpenMode.ForWrite);
	                    ObjectId psCId = GetPropertySetIdSafe(solid, defIdC);
	                    if (psCId == ObjectId.Null)
	                    {
	                        // Sem PSet C no desenho: nÃ£o trava a rotina.
	                        return;
	                    }
	                    PropertySet psC = (PropertySet)tr.GetObject(psCId, OpenMode.ForWrite);

                    // Converter inclinaÃ§Ã£o para percentual no momento de gravar (mantendo seu fluxo)
                    double slopePercent = values.Slope * 10.0;
                    values.RecomputeArea();

                    void SetStr(string prop, string val)
                    {
                        try { psC.SetAt(psC.PropertyNameToId(prop), val); }
                        catch
                        { /* evita travar */
                        }
                    }

                    // Regras idÃªnticas ao que vocÃª faz hoje (CodeName contÃ©m ...)
                    if (nomeCamada.Contains("PAVIMENTO", StringComparison.OrdinalIgnoreCase) ||
                        nomeCamada.Contains("PAVIMENTO1", StringComparison.OrdinalIgnoreCase) ||
                        nomeCamada.Contains("CBUQ", StringComparison.OrdinalIgnoreCase) ||
                        nomeCamada.Contains("CONCRETO", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.Pave1Depth.ToString("F2"));
                        SetStr("Declividade", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthMeters.ToString("F2"));
                    }

                    if (nomeCamada.Contains("PASSEIO", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.PasseioDepth.ToString("F2"));
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthMeters.ToString("F2"));
                    }

                    if (nomeCamada.Contains("BASE", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.BaseDepth.ToString("F2"));
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthMeters.ToString("F2"));
                    }

                    if (nomeCamada.Contains("SUB_BASE", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.SubBaseDepth.ToString("F2"));
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthMeters.ToString("F2"));
                    }

                    if (nomeCamada.Contains("GUIA", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.GuiaDepth.ToString("F2"));
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthMeters.ToString("F2"));
                    }

                    if (nomeCamada.Contains("CFT", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.Height.ToString("F2")); // CFTDepth
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthMeters.ToString("F2"));
                    }

                    if (nomeCamada.Contains("Top", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.GuiaDepth.ToString("F2")); // igual ao seu cÃ³digo atual
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthMeters.ToString("F2"));
                    }

                    if (nomeCamada.Contains("OFFSET_TALUDE", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.GuiaDepth.ToString("F2")); // igual ao seu cÃ³digo atual
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthMeters.ToString("F2"));
                    }

                    if (nomeCamada.Contains("TALUDE_ATERRO", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.GuiaDepth.ToString("F2")); // igual ao seu cÃ³digo atual
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthMeters.ToString("F2"));
                    }

                    if (nomeCamada.Contains("TALUDE_CORTE", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.GuiaDepth.ToString("F2")); // igual ao seu cÃ³digo atual
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthMeters.ToString("F2"));
                    }

                    if (nomeCamada.Contains("Barreira", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.GuiaDepth.ToString("F2")); // igual ao seu cÃ³digo atual
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthMeters.ToString("F2"));
                    }

                    if (nomeCamada.Contains("PONTE", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.GuiaDepth.ToString("F2")); // igual ao seu cÃ³digo atual
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthMeters.ToString("F2"));
                    }

                    if (nomeCamada.Contains("Acostamento_pavimento", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStr("Largura", values.Width.ToString("F2"));
                        SetStr("Altura", values.GuiaDepth.ToString("F2")); // igual ao seu cÃ³digo atual
                        SetStr("Inclinação", slopePercent.ToString("F2"));
                        SetStr("Área", values.Area.ToString("F2"));
                        SetStr("Comprimento", values.LengthMeters.ToString("F2"));
                    }
                }

                // --- Ler PSet F (mantendo sua lÃ³gica) ---
                if (dictionary.Has(PSET_F, tr))
                {
                    ObjectId defIdF = dictionary.GetAt(PSET_F);
                    PropertySetDefinition defF = (PropertySetDefinition)tr.GetObject(defIdF, OpenMode.ForWrite);
	                    ObjectId psFId = GetPropertySetIdSafe(solid, defIdF);
	                    if (psFId != ObjectId.Null)
	                    {
	                        PropertySet psF = (PropertySet)tr.GetObject(psFId, OpenMode.ForWrite);

	                        if (TryGetAecValue(psF, solid, "AssemblyName", out string tmpAss))
	                            nomeAssembly = LimparPrefixoContador(tmpAss);

	                        if (TryGetAecValue(psF, solid, "CorridorName", out string tmpCorr))
	                            nomeCorredor = LimparPrefixoContador(tmpCorr);
	                    }

                }

                /*||||||||||||||||EXPORTAÃ‡ÃƒO IFC E LAYERS IFC||||||||||||||||*/
                ApplyToEntity(solid, nomeCamada, db, tr);
                //ApplyToEntity(solid, nomeAssembly, db, tr);


            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nErro na classe PropertySets3 (PSet22): {ex.Message}");
                ed.WriteMessage($"\nDetalhes: {ex.StackTrace}");
            }
        }

        private void ApplyRoadworksPropertySetsRuntime(Entity entity, Database db, Transaction tr)
        {
            ExtractedValues values = new ExtractedValues();
            LoadExistingMetadataRuntime(entity, db, tr, values);
            PopulateGeometryMetricsRuntime(entity, values);

            if (entity is Solid3d solid)
                TryPopulateSolidVolumeRuntime(solid, values);

            bool resolvedByHandle = TryPopulateFromCorridorHandlesRuntime(values, db, tr);

            if ((!resolvedByHandle || ShouldUseRegionFallbackRuntime(values)) &&
                !string.IsNullOrWhiteSpace(values.RegionGuid) &&
                !string.IsNullOrWhiteSpace(values.CorridorName))
                ColetarParametrosPorGuidGenerico(values.RegionGuid, values.CorridorName, db, tr, values);

            ClassifyExtractedValuesRuntime(entity, values);
            ApplyLegacyAndIfcPropertySetsRuntime(entity, db, tr, values);
        }

        private static void LoadExistingMetadataRuntime(Entity entity, Database db, Transaction tr, ExtractedValues values)
        {
            DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
            string drawingName = string.Empty;

            try
            {
                drawingName = Manager.DocCad?.Name ?? string.Empty;
            }
            catch
            {
            }

            string projectName = string.IsNullOrWhiteSpace(drawingName)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(drawingName) ?? string.Empty;

            values.ProjectId = projectName;
            values.ProjectName = projectName;

            values.CodeName = ReadPropertyRuntime(dictionary, tr, entity, "Corridor Shape Information", "CodeName").ToUpper();
            values.SubassemblyName = ReadPropertyRuntime(dictionary, tr, entity, "B - Informações dos Objetos e Elementos", "SubassemblyName");
            values.AssemblyName = ReadPropertyRuntime(dictionary, tr, entity, "B - Informações dos Objetos e Elementos", "AssemblyName");
            values.CorridorName = FirstNonEmptyRuntime(
                ReadPropertyRuntime(dictionary, tr, entity, "Corridor Model Information", "CorridorName"),
                ReadPropertyRuntime(dictionary, tr, entity, "B - Informações dos Objetos e Elementos", "NomeCorredorSolidos"),
                ReadPropertyRuntime(dictionary, tr, entity, "Corridor Model Information", "CorridorName"));
            values.RegionGuid = FirstNonEmptyRuntime(
                ReadPropertyRuntime(dictionary, tr, entity, "Corridor Identity", "RegionName"),
                ReadPropertyRuntime(dictionary, tr, entity, "Corridor Identity", "RegionGUID"));
            values.Disciplina = ReadPropertyRuntime(dictionary, tr, entity, "B - Informações dos Objetos e Elementos", "Disciplina");
            values.Localizacao = FirstNonEmptyRuntime(
                ReadPropertyRuntime(dictionary, tr, entity, "B - Informações dos Objetos e Elementos", "Localizacao"),
                ReadPropertyRuntime(dictionary, tr, entity, "B - Informações dos Objetos e Elementos", "Localizacao"));
            values.Situacao = FirstNonEmptyRuntime(
                ReadPropertyRuntime(dictionary, tr, entity, "B - Informações dos Objetos e Elementos", "Situação"),
                ReadPropertyRuntime(dictionary, tr, entity, "B - Informações dos Objetos e Elementos", "Situacao"));
            values.CodigoObjeto = FirstNonEmptyRuntime(
                ReadPropertyRuntime(dictionary, tr, entity, "B - Informações dos Objetos e Elementos", "Codigo_do_Objeto"),
                ReadPropertyRuntime(dictionary, tr, entity, "B - Informações dos Objetos e Elementos", "CodigoObjeto"));
            values.Lado = FirstNonEmptyRuntime(
                ReadPropertyRuntime(dictionary, tr, entity, "Corridor Shape Information", "Side"),
                ReadPropertyRuntime(dictionary, tr, entity, "B - Informações dos Objetos e Elementos", "Lado"));
            values.AssemblyHandle = FirstNonEmptyRuntime(
                values.AssemblyHandle,
                ReadPropertyRuntime(dictionary, tr, entity, "Corridor Identity", "AssemblyHandle"));
            values.SubassemblyHandle = FirstNonEmptyRuntime(
                values.SubassemblyHandle,
                ReadPropertyRuntime(dictionary, tr, entity, "Corridor Identity", "SubassemblyHandle"));
            values.SubassemblyName = FirstNonEmptyRuntime(
                values.SubassemblyName,
                ReadPropertyRuntime(dictionary, tr, entity, "Corridor Identity", "SubassemblyName"));
            values.RegionGuid = FirstNonEmptyRuntime(
                values.RegionGuid,
                ReadPropertyRuntime(dictionary, tr, entity, "Corridor Identity", "RegionGuid"),
                ReadPropertyRuntime(dictionary, tr, entity, "Corridor Identity", "RegionGUID"));

            if (TryParseDoubleRuntime(ReadPropertyRuntime(dictionary, tr, entity, "COORDENAÇÃO", "COMPRIMENTO_SOLIDOS_CORREDOR"), out double length))
            {
                values.LengthMeters = length;
                values.LengthKm = length / 1000.0;
            }

            if (TryParseDoubleRuntime(FirstNonEmptyRuntime(
                ReadPropertyRuntime(dictionary, tr, entity, "COORDENAÇÃO", "COMPRIMENTO_SOLIDOS_CORREDOR"),
                ReadPropertyRuntime(dictionary, tr, entity, "COORDENACAO", "COMPRIMENTO_SOLIDOS_CORREDOR")), out double coordinateLength) &&
                coordinateLength > 0.0)
            {
                values.LengthMeters = coordinateLength;
                values.LengthKm = coordinateLength / 1000.0;
            }

            if (TryParseDoubleRuntime(FirstNonEmptyRuntime(
                ReadPropertyRuntime(dictionary, tr, entity, "Corridor Shape Information", "AssemblyStartStation"),
                ReadPropertyRuntime(dictionary, tr, entity, "Corridor Identity", "StartStation"),
                ReadPropertyRuntime(dictionary, tr, entity, "Corridor Identity", "StartStation")), out double start))
            {
                values.StartStation = start;
            }

            if (TryParseDoubleRuntime(FirstNonEmptyRuntime(
                ReadPropertyRuntime(dictionary, tr, entity, "Corridor Identity", "EndStation"),
                ReadPropertyRuntime(dictionary, tr, entity, "B - Informações dos Objetos e Elementos", "Estaqueamento_Final"),
                ReadPropertyRuntime(dictionary, tr, entity, "Corridor Identity", "EndStation")), out double end))
            {
                values.EndStation = end;
            }

            values.CodeName = FirstNonEmptyRuntime(
                values.CodeName,
                ReadPropertyRuntime(dictionary, tr, entity, "Corridor Shape Information", "CodeName").ToUpper());
            values.AssemblyName = FirstNonEmptyRuntime(
                values.AssemblyName,
                StripCounterPrefixRuntime(ReadPropertyRuntime(dictionary, tr, entity, "Corridor Shape Information", "AssemblyName")));
            values.CorridorName = FirstNonEmptyRuntime(
                values.CorridorName,
                StripCounterPrefixRuntime(ReadPropertyRuntime(dictionary, tr, entity, "Corridor Model Information", "CorridorName")),
                StripCounterPrefixRuntime(ReadPropertyRuntime(dictionary, tr, entity, "Corridor Shape Information", "CorridorName")));
        }

        private static void PopulateGeometryMetricsRuntime(Entity entity, ExtractedValues values)
        {
            try
            {
                Extents3d ext = entity.GeometricExtents;
                values.CenterE = (ext.MinPoint.X + ext.MaxPoint.X) / 2.0;
                values.CenterN = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0;
                values.CenterZ = (ext.MinPoint.Z + ext.MaxPoint.Z) / 2.0;
                values.ExtentHeight = Math.Abs(ext.MaxPoint.Z - ext.MinPoint.Z);
            }
            catch
            {
            }
        }

        private static void TryPopulateSolidVolumeRuntime(Solid3d solid, ExtractedValues values)
        {
            try
            {
                var massPropertiesProperty = solid.GetType().GetProperty("MassProperties");
                object massProperties = massPropertiesProperty?.GetValue(solid);
                object rawVolume = massProperties?.GetType().GetProperty("Volume")?.GetValue(massProperties);
                if (rawVolume != null && TryParseDoubleRuntime(rawVolume.ToString(), out double volume) && volume > 0.0)
                    values.Volume = volume;
            }
            catch
            {
            }
        }

        private static void ClassifyExtractedValuesRuntime(Entity entity, ExtractedValues values)
        {
            string key = NormalizeCodeRuntime(values.CodeName);

            values.Lado = FirstNonEmptyRuntime(values.Lado, InferSideRuntime(key));
            values.CodigoObjeto = FirstNonEmptyRuntime(values.CodigoObjeto, values.CodeName, entity.Handle.ToString());
            values.Localizacao = FirstNonEmptyRuntime(values.Localizacao, values.CorridorName);
            values.Situacao = FirstNonEmptyRuntime(values.Situacao, "Projeto");

            if (key.Contains("FRES") || key.Contains("MILL"))
            {
                values.IsPavement = true;
                values.IsMilling = true;
                values.Camada = ResolveFriendlyLayerNameRuntime(values.Camada, key, "Fresagem");
                values.FuncaoCamada = FirstNonEmptyRuntime(values.FuncaoCamada, "MILLING");
                values.TipoPavimento = FirstNonEmptyRuntime(values.TipoPavimento, "REABILITACAO");
            }
            else if (key.Contains("SUB_BASE") || key.Contains("SUBBASE"))
            {
                values.IsPavement = true;
                values.Camada = ResolveFriendlyLayerNameRuntime(values.Camada, key, "Sub-base");
                values.FuncaoCamada = FirstNonEmptyRuntime(values.FuncaoCamada, "SUBBASE");
                values.TipoPavimento = FirstNonEmptyRuntime(values.TipoPavimento, "GRANULAR");
            }
            else if (key.Contains("BASE"))
            {
                values.IsPavement = true;
                values.Camada = ResolveFriendlyLayerNameRuntime(values.Camada, key, "Base");
                values.FuncaoCamada = FirstNonEmptyRuntime(values.FuncaoCamada, "BASE");
                values.TipoPavimento = FirstNonEmptyRuntime(values.TipoPavimento, "ESTRUTURAL");
            }
            else if (key.Contains("CBUQ") || key.Contains("PAVE2") || key.Contains("BINDER"))
            {
                values.IsPavement = true;
                values.Camada = ResolveFriendlyLayerNameRuntime(values.Camada, key, "Binder");
                values.FuncaoCamada = FirstNonEmptyRuntime(values.FuncaoCamada, "BINDER");
                values.TipoPavimento = FirstNonEmptyRuntime(values.TipoPavimento, "ASFALTICO");
            }
            else if (key.Contains("CONCRETO") || key.Contains("PAVE1") || key.Contains("WEARING") || key.Contains("PAVIMENTO") || key.Contains("PAVE"))
            {
                values.IsPavement = true;
                values.Camada = ResolveFriendlyLayerNameRuntime(values.Camada, key, "Revestimento");
                values.FuncaoCamada = FirstNonEmptyRuntime(values.FuncaoCamada, "WEARING");
                values.TipoPavimento = FirstNonEmptyRuntime(values.TipoPavimento, "ASFALTICO");
            }
            else if (key.Contains("PASSEIO") || key.Contains("CALCADA"))
            {
                values.IsPavement = true;
                values.Camada = ResolveFriendlyLayerNameRuntime(values.Camada, key, "Passeio");
                values.FuncaoCamada = FirstNonEmptyRuntime(values.FuncaoCamada, "SIDEWALK");
                values.TipoPavimento = FirstNonEmptyRuntime(values.TipoPavimento, "CONCRETO");
            }
            else if (key.Contains("GUIA") || key.Contains("MEIO_FIO") || key.Contains("MEIOFIO"))
            {
                values.IsPavement = true;
                values.Camada = ResolveFriendlyLayerNameRuntime(values.Camada, key, "Guia");
                values.FuncaoCamada = FirstNonEmptyRuntime(values.FuncaoCamada, "CURB");
                values.TipoPavimento = FirstNonEmptyRuntime(values.TipoPavimento, "CONCRETO");
            }
            else if (key.Contains("ACOSTAMENTO"))
            {
                values.IsPavement = true;
                values.Camada = ResolveFriendlyLayerNameRuntime(values.Camada, key, "Acostamento");
                values.FuncaoCamada = FirstNonEmptyRuntime(values.FuncaoCamada, "SHOULDER");
                values.TipoPavimento = FirstNonEmptyRuntime(values.TipoPavimento, "PAVIMENTO");
            }
            else if (key.Contains("TALUDE_ATERRO") || key.Contains("ATERRO") || key.Contains("FILL") || key.Contains("EMBANK"))
            {
                values.IsEarthworks = true;
                values.IsFill = true;
                values.Camada = "Talude de aterro";
                values.NaturezaMovimentoTerra = "Aterro";
            }
            else if (key.Contains("TALUDE_CORTE") || key.Contains("CORTE") || key.Contains("CUT") || key.Contains("OFFSET_TALUDE") || key.Contains("CFT"))
            {
                values.IsEarthworks = true;
                values.IsCut = true;
                values.Camada = "Talude de corte";
                values.NaturezaMovimentoTerra = "Corte";
            }
            else if (key.Contains("TRANS"))
            {
                values.IsEarthworks = true;
                values.IsTransition = true;
                values.Camada = "Transição";
                values.NaturezaMovimentoTerra = "Transição";
            }
            else if (key.Contains("TOP") || key.Contains("PLATAFORMA"))
            {
                values.IsEarthworks = true;
                values.Camada = "Plataforma";
                values.NaturezaMovimentoTerra = "Plataforma";
            }
            else
            {
                values.IsPavement = true;
                values.Camada = ResolveFriendlyLayerNameRuntime(values.Camada, key, values.CodeName);
                values.FuncaoCamada = FirstNonEmptyRuntime(values.FuncaoCamada, "COURSE");
            }

            values.Disciplina = FirstNonEmptyRuntime(values.Disciplina, values.IsEarthworks ? "Terraplenagem" : "Pavimentação");
            values.Material = ResolveFriendlyMaterialRuntime(values.Material, key, values);

            double stationBasedLength = GetStationBasedLengthRuntime(values);
            if (values.LengthMeters <= 0.0 && stationBasedLength > 0.0)
            {
                values.LengthMeters = stationBasedLength;
                values.LengthKm = values.LengthMeters / 1000.0;
            }

            values.RecomputeArea();

            double thickness = GetPrimaryThicknessRuntime(values);
            if (values.Volume <= 0.0 && values.Area > 0.0 && thickness > 0.0)
                values.Volume = values.Area * thickness;
        }

        private static void ApplyLegacyAndIfcPropertySetsRuntime(Entity entity, Database db, Transaction tr, ExtractedValues values)
        {
            DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
            double slopePercent = values.Slope * 10.0;
            double thickness = GetPrimaryThicknessRuntime(values);
            double cotaFundo = values.CenterZ - (thickness / 2.0);
            double cotaTopo = values.CenterZ + (thickness / 2.0);

            WritePsetRuntime(entity, dictionary, tr, "A - Dados do Projeto",
                ("Identificador do Projeto", values.ProjectId),
                ("NomeProjeto", values.ProjectName),
                ("Segmento", values.CorridorName),
                ("Trecho", values.CorridorName),
                ("EstagioProjeto", "Projeto"));

            WritePsetRuntime(entity, dictionary, tr, "B - Informações dos Objetos e Elementos",
                ("Disciplina", values.Disciplina),
                ("Localização", values.Localizacao),
                ("Localizacao", values.Localizacao),
                ("Situação", values.Situacao),
                ("Situacao", values.Situacao),
                ("EstaqueamentoInicial", FormatDoubleRuntime(values.StartStation)),
                ("EstaqueamentoFinal", FormatDoubleRuntime(values.EndStation)),
                ("Estaqueamento_Inicial", FormatDoubleRuntime(values.StartStation)),
                ("Estaqueamento_Final", FormatDoubleRuntime(values.EndStation)),
                ("Código_do_Objeto", values.CodigoObjeto),
                ("CodigoObjeto", values.CodigoObjeto),
                ("CodeName", values.CodeName),
                ("SubassemblyName", values.SubassemblyName),
                ("AssemblyName", values.AssemblyName),
                ("NomeCorredorSolido", values.CorridorName),
                ("NomeCorredorSolidos", values.CorridorName),
                ("RegionName", values.RegionGuid),
                ("RegionGUID", values.RegionGuid),
                ("Comprimento", FormatDoubleRuntime(values.LengthMeters)),
                ("Lado", values.Lado));

            WritePsetRuntime(entity, dictionary, tr, "C - Propriedades Físicas dos Objetos e Elementos",
                ("Comprimento", FormatDoubleRuntime(values.LengthMeters)),
                ("Largura", FormatDoubleRuntime(values.Width)),
                ("Altura", FormatDoubleRuntime(thickness)),
                ("Ãrea", FormatDoubleRuntime(values.Area)),
                ("Area", FormatDoubleRuntime(values.Area)),
                ("Volume", FormatDoubleRuntime(values.Volume)),
                ("InclinaÃ§Ã£o", FormatDoubleRuntime(slopePercent)),
                ("Inclinacao", FormatDoubleRuntime(slopePercent)),
                ("Cota_de_Fundo", FormatDoubleRuntime(cotaFundo)),
                ("Cota_de_Topo", FormatDoubleRuntime(cotaTopo)));

            WritePsetRuntime(entity, dictionary, tr, "D - Propriedades Geográficas",
                ("Coordenada_E", FormatDoubleRuntime(values.CenterE)),
                ("Coordenada_N", FormatDoubleRuntime(values.CenterN)),
                ("Coordenada_Z", FormatDoubleRuntime(values.CenterZ)));

            WritePsetRuntime(entity, dictionary, tr, "COORDENAÇÃO",
                ("AREA_3D_SUPERFICIE", FormatDoubleRuntime(values.Area)),
                ("COMPRIMENTO_3D_FEATURE_LINES", FormatDoubleRuntime(values.LengthMeters)),
                ("COMPRIMENTO_SOLIDOS_CORREDOR", FormatDoubleRuntime(values.LengthMeters)));

            WritePsetRuntime(entity, dictionary, tr, "E - Requisitos Específicos de Projeto",
                ("Material", values.Material),
                ("ClasseMaterial", values.Material));

            WritePsetRuntime(entity, dictionary, tr, "Pset_CivilElementCommon",
                ("Reference", BuildReferenceRuntime(values)),
                ("Status", values.Situacao));

            WritePsetRuntime(entity, dictionary, tr, "Pset_ReferentCommon",
                ("NameFormat", "Estaqueamento"));

            WritePsetRuntime(entity, dictionary, tr, "Pset_Stationing",
                ("IncomingStation", FormatDoubleRuntime(values.StartStation)),
                ("Station", FormatDoubleRuntime(!double.IsNaN(values.EndStation) ? values.EndStation : values.StartStation)),
                ("HasIncreasingStation", "true"),
                ("StationInterval", BuildStationLabelRuntime(values.StartStation, values.EndStation)));

            WritePsetRuntime(entity, dictionary, tr, "Pset_LinearReferencingMethod",
                ("LRMName", "Estaqueamento"),
                ("LRMType", "CHAINAGE"),
                ("LRMUnit", "m"));

            WritePsetRuntime(entity, dictionary, tr, "Pset_RoadDesignCriteriaCommon",
                ("Crossfall", FormatDoubleRuntime(slopePercent)),
                ("LaneWidth", FormatDoubleRuntime(values.Width)));

            WritePsetRuntime(entity, dictionary, tr, "Pset_Superelevation",
                ("Side", values.Lado),
                ("Superelevation", FormatDoubleRuntime(slopePercent)),
                ("TransitionSuperelevation", FormatDoubleRuntime(slopePercent)));

            WritePsetRuntime(entity, dictionary, tr, "Pset_Width",
                ("Side", values.Lado),
                ("NominalWidth", FormatDoubleRuntime(values.Width)),
                ("TransitionWidth", FormatDoubleRuntime(values.Width)));

            WritePsetRuntime(entity, dictionary, tr, "Pset_Rodoviario",
                ("Segmento", values.CorridorName),
                ("Trecho", values.CorridorName),
                ("Rodovia", values.ProjectName),
                ("CodigoObjeto", values.CodigoObjeto),
                ("CodeName", values.CodeName),
                ("SubassemblyName", values.SubassemblyName),
                ("NomeCorredor", values.CorridorName),
                ("RegionName", values.RegionGuid),
                ("Side", values.Lado),
                ("Situacao", values.Situacao),
                ("Localizacao", values.Localizacao),
                ("EstaqueamentoInicial", FormatDoubleRuntime(values.StartStation)),
                ("EstaqueamentoFinal", FormatDoubleRuntime(values.EndStation)),
                ("EstaqueamentoInicialTexto", FormatDoubleRuntime(values.StartStation)),
                ("EstaqueamentoFinalTexto", FormatDoubleRuntime(values.EndStation)),
                ("IntervaloEstacas", BuildStationLabelRuntime(values.StartStation, values.EndStation)),
                ("LRMName", "Estaqueamento"),
                ("EstagioProjeto", "Projeto"));

            if (values.IsPavement)
            {
                string spreadingRate = values.Area > 0.0 && values.Volume > 0.0
                    ? FormatDoubleRuntime(values.Volume / values.Area)
                    : string.Empty;

                WritePsetRuntime(entity, dictionary, tr, "Pset_CourseCommon",
                    ("NominalLength", FormatDoubleRuntime(values.LengthMeters)),
                    ("NominalThickness", FormatDoubleRuntime(thickness)),
                    ("NominalWidth", FormatDoubleRuntime(values.Width)));

                WritePsetRuntime(entity, dictionary, tr, "Pset_CourseApplicationConditions",
                    ("ApplicationTemperature", string.Empty),
                    ("WeatherConditions", string.Empty));

                WritePsetRuntime(entity, dictionary, tr, "Pset_BoundedCourseCommon",
                    ("SpreadingRate", spreadingRate));

                WritePsetRuntime(entity, dictionary, tr, "Qto_CourseBaseQuantities",
                    ("Length", FormatDoubleRuntime(values.LengthMeters)),
                    ("Width", FormatDoubleRuntime(values.Width)),
                    ("Thickness", FormatDoubleRuntime(thickness)),
                    ("Volume", FormatDoubleRuntime(values.Volume)),
                    ("GrossVolume", FormatDoubleRuntime(values.Volume)));

                WritePsetRuntime(entity, dictionary, tr, "Pset_PavementCommon",
                    ("Reference", BuildReferenceRuntime(values)),
                    ("Status", values.Situacao),
                    ("NominalThicknessEnd", FormatDoubleRuntime(thickness)),
                    ("StructuralSlope", FormatDoubleRuntime(slopePercent)),
                    ("StructuralSlopeType", "Crossfall"),
                    ("NominalWidth", FormatDoubleRuntime(values.Width)),
                    ("NominalLength", FormatDoubleRuntime(values.LengthMeters)),
                    ("NominalThickness", FormatDoubleRuntime(thickness)));

                WritePsetRuntime(entity, dictionary, tr, "Qto_PavementBaseQuantities",
                    ("Length", FormatDoubleRuntime(values.LengthMeters)),
                    ("Width", FormatDoubleRuntime(values.Width)),
                    ("Depth", FormatDoubleRuntime(thickness)),
                    ("GrossArea", FormatDoubleRuntime(values.Area)),
                    ("NetArea", FormatDoubleRuntime(values.Area)),
                    ("GrossVolume", FormatDoubleRuntime(values.Volume)),
                    ("NetVolume", FormatDoubleRuntime(values.Volume)));

                WritePsetRuntime(entity, dictionary, tr, "Pset_PavementSurfaceCommon",
                    ("PavementTexture", values.Camada));

                if (values.IsMilling)
                {
                    WritePsetRuntime(entity, dictionary, tr, "Pset_PavementMillingCommon",
                        ("NominalDepth", FormatDoubleRuntime(thickness)),
                        ("NominalWidth", FormatDoubleRuntime(values.Width)));
                }

                WritePsetRuntime(entity, dictionary, tr, "Pset_Pavimentacao",
                    ("Disciplina", values.Disciplina),
                    ("Faixa", FirstNonEmptyRuntime(values.SubassemblyName, values.Lado)),
                    ("Camada", FirstNonEmptyRuntime(values.CodeName, values.Camada)),
                    ("FuncaoCamada", values.FuncaoCamada),
                    ("TipoPavimento", values.TipoPavimento),
                    ("Material", values.Material),
                    ("EspessuraProjeto", FormatDoubleRuntime(thickness)),
                    ("LarguraProjeto", FormatDoubleRuntime(values.Width)),
                    ("ComprimentoProjeto", FormatDoubleRuntime(values.LengthMeters)),
                    ("AreaProjeto", FormatDoubleRuntime(values.Area)),
                    ("VolumeProjeto", FormatDoubleRuntime(values.Volume)),
                    ("CrossfallProjeto", FormatDoubleRuntime(slopePercent)),
                    ("SuperelevacaoProjeto", FormatDoubleRuntime(slopePercent)),
                    ("EstaqueamentoInicialTexto", FormatDoubleRuntime(values.StartStation)),
                    ("EstaqueamentoFinalTexto", FormatDoubleRuntime(values.EndStation)),
                    ("ProfundidadeFresagem", values.IsMilling ? FormatDoubleRuntime(thickness) : string.Empty));
            }

            if (values.IsEarthworks)
            {
                if (values.IsTransition)
                {
                    WritePsetRuntime(entity, dictionary, tr, "Pset_TransitionSectionCommon",
                        ("NominalLength", FormatDoubleRuntime(values.LengthMeters)));
                }

                if (NormalizeCodeRuntime(values.CodeName).Contains("VALA") || NormalizeCodeRuntime(values.CodeName).Contains("TRENCH"))
                {
                    WritePsetRuntime(entity, dictionary, tr, "Pset_TrenchExcavationCommon",
                        ("NominalDepth", FormatDoubleRuntime(thickness)),
                        ("NominalWidth", FormatDoubleRuntime(values.Width)));
                }

                WritePsetRuntime(entity, dictionary, tr, "Pset_Terraplenagem",
                    ("Disciplina", values.Disciplina),
                    ("NaturezaMovimentoTerra", values.NaturezaMovimentoTerra),
                    ("SecaoTipo", values.SubassemblyName),
                    ("Material", values.Material),
                    ("VolumeCorte", values.IsCut ? FormatDoubleRuntime(values.Volume) : string.Empty),
                    ("VolumeAterro", values.IsFill ? FormatDoubleRuntime(values.Volume) : string.Empty),
                    ("AreaSecao", FormatDoubleRuntime(values.Area)),
                    ("Altura", FormatDoubleRuntime(thickness)),
                    ("Largura", FormatDoubleRuntime(values.Width)),
                    ("TaludeCorte", values.IsCut ? FormatDoubleRuntime(slopePercent) : string.Empty),
                    ("TaludeAterro", values.IsFill ? FormatDoubleRuntime(slopePercent) : string.Empty),
                    ("ProfundidadeEscavacao", values.IsCut ? FormatDoubleRuntime(thickness) : string.Empty));
            }

            ApplyToEntity(entity, values.CodeName, db, tr);
        }

        private static string ReadPropertyRuntime(DictionaryPropertySetDefinitions dictionary, Transaction tr, Entity entity, string psetName, string propertyName)
        {
            if (!TryResolvePsetDefinitionIdRuntime(dictionary, tr, psetName, out ObjectId defId))
                return string.Empty;
            ObjectId psId = GetPropertySetIdSafe(entity, defId);
            if (psId == ObjectId.Null)
                return string.Empty;

            PropertySet pset = (PropertySet)tr.GetObject(psId, OpenMode.ForRead);
            return TryGetAecValue(pset, entity, propertyName, out string value) ? value : string.Empty;
        }

        private static void WritePsetRuntime(Entity entity, DictionaryPropertySetDefinitions dictionary, Transaction tr, string psetName, params (string Name, string Value)[] assignments)
        {
            if (!TryResolvePsetDefinitionIdRuntime(dictionary, tr, psetName, out ObjectId defId))
                return;
            ObjectId psId = GetPropertySetIdSafe(entity, defId);
            if (psId == ObjectId.Null)
            {
                try
                {
                    PropertyDataServices.AddPropertySet(entity, defId);
                    psId = GetPropertySetIdSafe(entity, defId);
                }
                catch
                {
                    return;
                }
            }

            if (psId == ObjectId.Null)
                return;

            PropertySet pset = (PropertySet)tr.GetObject(psId, OpenMode.ForWrite);
            foreach ((string Name, string Value) assignment in assignments)
            {
                if (string.IsNullOrWhiteSpace(assignment.Name) || string.IsNullOrWhiteSpace(assignment.Value))
                    continue;

                if (!TryGetAecPropId(pset, assignment.Name, out int id))
                    continue;

                try
                {
                    pset.SetAt(id, assignment.Value);
                }
                catch
                {
                }
            }
        }

        private static bool TryResolvePsetDefinitionIdRuntime(DictionaryPropertySetDefinitions dictionary, Transaction tr, string psetName, out ObjectId defId)
        {
            IEnumerable<string> candidateNames = PropertySetAliasesRuntime.TryGetValue(psetName, out string[] aliases)
                ? aliases
                : new[] { psetName };

            foreach (string candidate in candidateNames)
            {
                if (!dictionary.Has(candidate, tr))
                    continue;

                defId = dictionary.GetAt(candidate);
                if (defId != ObjectId.Null)
                    return true;
            }

            defId = ObjectId.Null;
            return false;
        }

        private static double GetPrimaryThicknessRuntime(ExtractedValues values)
        {
            string key = NormalizeCodeRuntime(values.CodeName);

            if (key.Contains("SUB_BASE") || key.Contains("SUBBASE"))
                return FirstPositiveRuntime(values.SubBaseDepth, values.ExtentHeight, values.Height);

            if (key.Contains("CBUQ") || key.Contains("PAVE2") || key.Contains("BINDER"))
                return FirstPositiveRuntime(values.Pave2Depth, values.Pave1Depth, values.ExtentHeight);

            if (key.Contains("CONCRETO") || key.Contains("PAVE1") || key.Contains("WEARING"))
                return FirstPositiveRuntime(values.Pave1Depth, values.ExtentHeight);

            if (key.Contains("BASE"))
                return FirstPositiveRuntime(values.BaseDepth, values.ExtentHeight);

            if (key.Contains("PAVIMENTO") || key.Contains("PAVE"))
                return FirstPositiveRuntime(values.Pave1Depth, values.Pave2Depth, values.ExtentHeight);

            if (key.Contains("PASSEIO") || key.Contains("CALCADA"))
                return FirstPositiveRuntime(values.PasseioDepth, values.ExtentHeight);

            if (key.Contains("GUIA") || key.Contains("MEIO_FIO") || key.Contains("MEIOFIO"))
                return FirstPositiveRuntime(values.GuiaDepth, values.ExtentHeight);

            if (key.Contains("CFT"))
                return FirstPositiveRuntime(values.Height, values.ExtentHeight);

            return FirstPositiveRuntime(
                values.Pave1Depth,
                values.Pave2Depth,
                values.BaseDepth,
                values.SubBaseDepth,
                values.PasseioDepth,
                values.GuiaDepth,
                values.Height,
                values.ExtentHeight);
        }

        private static string BuildReferenceRuntime(ExtractedValues values)
        {
            return FirstNonEmptyRuntime(values.CorridorName, values.CodigoObjeto, values.SubassemblyName, values.CodeName, values.ProjectName);
        }

        private static string BuildStationLabelRuntime(double startStation, double endStation)
        {
            bool hasStart = !double.IsNaN(startStation);
            bool hasEnd = !double.IsNaN(endStation);

            if (!hasStart && !hasEnd)
                return string.Empty;

            if (hasStart && hasEnd)
                return $"{FormatDoubleRuntime(startStation)} a {FormatDoubleRuntime(endStation)}";

            return hasStart
                ? FormatDoubleRuntime(startStation)
                : FormatDoubleRuntime(endStation);
        }

        private static string InferMaterialRuntime(string key, ExtractedValues values)
        {
            if (key.Contains("CBUQ") || key.Contains("PAVE") || key.Contains("WEARING") || key.Contains("BINDER")) return "MISTURA ASFALTICA";
            if (key.Contains("BASE") || key.Contains("SUB_BASE") || key.Contains("SUBBASE")) return "MATERIAL GRANULAR";
            if (key.Contains("GUIA") || key.Contains("CONCRETO") || key.Contains("CURB") || key.Contains("PASSEIO")) return "CONCRETO";
            if (values.IsEarthworks) return "Solo";
            return string.Empty;
        }

        private static double FirstPositiveRuntime(params double[] values)
        {
            foreach (double value in values)
            {
                if (value > 0.0)
                    return value;
            }

            return 0.0;
        }

        private static string ResolveFriendlyLayerNameRuntime(string currentValue, string key, string fallback)
        {
            string current = (currentValue ?? string.Empty).Trim();
            string normalizedCurrent = NormalizeCodeRuntime(current);

            if (string.IsNullOrWhiteSpace(current) || normalizedCurrent == key || IsGenericLayerCodeRuntime(normalizedCurrent))
                return fallback;

            return current;
        }

        private static string ResolveFriendlyMaterialRuntime(string currentValue, string key, ExtractedValues values)
        {
            string current = (currentValue ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(current))
                return current;

            return InferMaterialRuntime(key, values);
        }

        private static bool IsGenericLayerCodeRuntime(string value)
        {
            switch (value)
            {
                case "BASE":
                case "SUBBASE":
                case "SUB_BASE":
                case "PAVE":
                case "PAVE1":
                case "PAVE2":
                case "PAVIMENTO":
                case "CBUQ":
                case "CONCRETO":
                case "WEARING":
                case "BINDER":
                    return true;
                default:
                    return false;
            }
        }

        private static string InferSideRuntime(string key)
        {
            if (key.Contains("LEFT") || key.Contains("ESQ") || key.Contains("_L")) return "LEFT";
            if (key.Contains("RIGHT") || key.Contains("DIR") || key.Contains("_R")) return "RIGHT";
            return string.Empty;
        }

        private static string NormalizeSideValueRuntime(string value)
        {
            string key = NormalizeCodeRuntime(value);
            if (key.Contains("LEFT") || key.Contains("ESQ")) return "LEFT";
            if (key.Contains("RIGHT") || key.Contains("DIR")) return "RIGHT";
            if (key.Contains("CENTER") || key.Contains("CENTRO")) return "CENTER";
            return value.Trim();
        }

        private static string NormalizeCodeRuntime(string codeName)
        {
            return (codeName ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Replace("-", "_")
                .Replace(" ", "_");
        }

        private static string StripCounterPrefixRuntime(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : Regex.Replace(value, @"^(?:\s*\(\d+\)\s*)+", "").TrimStart();
        }

        private static bool TryParseDoubleRuntime(string text, out double value)
        {
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                return true;

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("pt-BR"), out value))
                return true;

            value = 0.0;
            return false;
        }

        private static string FormatDoubleRuntime(double value)
        {
            return double.IsNaN(value) ? string.Empty : value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FirstNonEmptyRuntime(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
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

	            string safeLayerName = SanitizeLayerName(layerBaseName);
	            if (string.IsNullOrWhiteSpace(safeLayerName)) return;

            LayerTable layerTable =
                (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

	            if (!layerTable.Has(safeLayerName))
            {
                layerTable.UpgradeOpen();

                LayerTableRecord layerRecord = new LayerTableRecord();
	                layerRecord.Name = safeLayerName;

                ObjectId layerId = layerTable.Add(layerRecord);
                tr.AddNewlyCreatedDBObject(layerRecord, true);
            }

	            ent.Layer = safeLayerName;
        }

        public string LimparPrefixoContador(string s)
        {
            // remove " (n)" ou "(n)" do inÃ­cio, repetidos ou nÃ£o, e espaÃ§os
            string limpo = Regex.Replace(s, @"^(?:\s*\(\d+\)\s*)+", "");
            return limpo.TrimStart();
        }

        /// <summary>
        /// MantÃ©m assinatura original: busca o Region por GUID no corredor e extrai parÃ¢metros
        /// de forma genÃ©rica (funciona para QUALQUER subassembly).
        /// </summary>
        public static void ParametrosGuid2(string guid, string nomeCorredorSolido, Database db, Transaction tr)
        {
            // Mantido por compatibilidade, mas agora a extraÃ§Ã£o genÃ©rica Ã© feita por ColetarParametrosPorGuidGenerico
            // Este mÃ©todo segue existindo para nÃ£o quebrar chamadas externas (se houver).
        }

        // ============================================================
        // ===========   IMPLEMENTAÃ‡ÃƒO GENÃ‰RICA: CORE   ===============
        // ============================================================

        private static bool TryPopulateFromCorridorHandlesRuntime(ExtractedValues values, Database db, Transaction tr)
        {
            bool foundAny = false;

            if (TryGetObjectIdFromHandleRuntime(db, values.AssemblyHandle, out ObjectId assemblyId))
            {
                try
                {
                    var assembly = (Autodesk.Civil.DatabaseServices.Assembly)tr.GetObject(assemblyId, OpenMode.ForRead);
                    values.AssemblyName = FirstNonEmptyRuntime(values.AssemblyName, assembly?.Name);
                    foundAny = true;
                }
                catch
                {
                }
            }

            if (TryGetObjectIdFromHandleRuntime(db, values.SubassemblyHandle, out ObjectId subassemblyId))
            {
                try
                {
                    var subassembly = (Autodesk.Civil.DatabaseServices.Subassembly)tr.GetObject(subassemblyId, OpenMode.ForRead);
                    CollectParametersFromSubassemblyRuntime(subassembly, values);
                    foundAny = true;
                }
                catch
                {
                }
            }

            double stationBasedLength = GetStationBasedLengthRuntime(values);
            if (stationBasedLength > 0.0)
            {
                values.LengthMeters = stationBasedLength;
                values.LengthKm = stationBasedLength / 1000.0;
            }

            return foundAny;
        }

        private static bool ShouldUseRegionFallbackRuntime(ExtractedValues values)
        {
            return values.Width <= 0.0
                || GetPrimaryThicknessRuntime(values) <= 0.0
                || string.IsNullOrWhiteSpace(values.SubassemblyName)
                || string.IsNullOrWhiteSpace(values.AssemblyName);
        }

        private static bool TryGetObjectIdFromHandleRuntime(Database db, string rawHandle, out ObjectId objectId)
        {
            objectId = ObjectId.Null;
            if (string.IsNullOrWhiteSpace(rawHandle))
                return false;

            string cleaned = rawHandle.Trim();
            if (!long.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long handleValue))
                return false;

            try
            {
                Handle handle = new Handle(handleValue);
                objectId = db.GetObjectId(false, handle, 0);
                return objectId != ObjectId.Null;
            }
            catch
            {
                objectId = ObjectId.Null;
                return false;
            }
        }

        private static void CollectParametersFromSubassemblyRuntime(Autodesk.Civil.DatabaseServices.Subassembly subassembly, ExtractedValues values)
        {
            if (subassembly == null)
                return;

            values.SubassemblyName = FirstNonEmptyRuntime(values.SubassemblyName, subassembly.Name);

            foreach (var kv in subassembly.ParamsDouble)
                MapDouble(values, kv.Key, kv.Value);

            foreach (var kv in subassembly.ParamsLong)
                MapLong(values, kv.Key, kv.Value);

            foreach (var kv in subassembly.ParamsString)
            {
                string stringValue = Convert.ToString(kv.Value, CultureInfo.InvariantCulture) ?? string.Empty;
                MapString(values, kv.Key, stringValue);
            }
        }

        private static double GetStationBasedLengthRuntime(ExtractedValues values)
        {
            return !double.IsNaN(values.StartStation) && !double.IsNaN(values.EndStation)
                ? Math.Abs(values.EndStation - values.StartStation)
                : 0.0;
        }

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
                            // Compara GUID da regiÃ£o (normalizando para evitar problemas de case/acentos)
                            if (!region.RegionGUID.ToString().Equals(guid, StringComparison.OrdinalIgnoreCase) &&
                                !region.RegionGUID.ToString().Contains(guid, StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (!string.IsNullOrWhiteSpace(values.AssemblyHandle))
                            {
                                try
                                {
                                    var regionAssembly = (Autodesk.Civil.DatabaseServices.Assembly)tr.GetObject(region.AssemblyId, OpenMode.ForRead);
                                    values.AssemblyName = FirstNonEmptyRuntime(values.AssemblyName, regionAssembly?.Name);
                                    if (!regionAssembly.Handle.ToString().Equals(values.AssemblyHandle, StringComparison.OrdinalIgnoreCase))
                                        continue;
                                }
                                catch
                                {
                                }
                            }

                            // Pega a subassembly aplicada no inÃ­cio da regiÃ£o (mantendo seu padrÃ£o)
                            values.CorridorName = FirstNonEmptyRuntime(values.CorridorName, corridor.Name);
                            values.RegionGuid = region.RegionGUID.ToString();
                            values.StartStation = region.StartStation;
                            values.EndStation = region.EndStation;
                            if (values.LengthMeters <= 0.0)
                            {
                                values.LengthMeters = Math.Abs(region.EndStation - region.StartStation);
                                values.LengthKm = values.LengthMeters / 1000.0;
                            }

                            var assembly = bl.GetAppliedAssemblyAtStation(region.StartStation);

                            // Vamos varrer TODAS as subassemblies aplicadas e coletar parÃ¢metros
                            foreach (AppliedSubassembly appliedSub in assembly.GetAppliedSubassemblies())
                            {
                                var sub = (Autodesk.Civil.DatabaseServices.Subassembly)tr.GetObject(appliedSub.SubassemblyId, OpenMode.ForRead);
                                if (!string.IsNullOrWhiteSpace(values.SubassemblyHandle) &&
                                    !sub.Handle.ToString().Equals(values.SubassemblyHandle, StringComparison.OrdinalIgnoreCase))
                                    continue;

                                if (string.IsNullOrWhiteSpace(values.SubassemblyHandle) &&
                                    !string.IsNullOrWhiteSpace(values.SubassemblyName) &&
                                    !sub.Name.Equals(values.SubassemblyName, StringComparison.OrdinalIgnoreCase))
                                    continue;

                                values.SubassemblyName = FirstNonEmptyRuntime(values.SubassemblyName, sub?.Name);

                                // 1) Doubles
                                foreach (var kv in sub.ParamsDouble)
                                {
                                    string name = kv.Key;
                                    double val = kv.Value;
                                    MapDouble(values, name, val);
                                }

                                // 2) Longs (poucos impactam nos calculos fisicos, mas deixo mapeavel se precisar)
                                foreach (var kv in sub.ParamsLong)
                                {
                                    string name = kv.Key;
                                    long val = kv.Value;
                                    MapLong(values, name, val);
                                }

                                // 3) Strings (materiais, lado, nomes de camada e metadados da subassembly)
                                foreach (var kv in sub.ParamsString)
                                {
                                    string name = kv.Key;
                                    string val = Convert.ToString(kv.Value, CultureInfo.InvariantCulture) ?? string.Empty;
                                    MapString(values, name, val);
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(values.SubassemblyName) || values.Width > 0.0 || GetPrimaryThicknessRuntime(values) > 0.0)
                                return;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nErro na coleta genÃ©rica por GUID: {ex.Message}");
            }
        }

        // ---------- Mapeamento genÃ©rico de nomes -> valores usados na sua lÃ³gica ----------
        private static void MapDouble(ExtractedValues v, string paramName, double val)
        {
            // LARGURA
            if (EqualsAny(paramName, "Width", "LaneWidth", "ShoulderWidth"))
                v.Width = val;

            if (ContainsAny(paramName, "Length") && v.LengthMeters <= 0.0)
            {
                v.LengthMeters = val;
                v.LengthKm = val / 1000.0;
            }

            // CAMADAS (profundidades)
            if (EqualsAny(paramName, "Pave1Depth")) v.Pave1Depth = val;
            if (EqualsAny(paramName, "Pave2Depth")) v.Pave2Depth = val;
            if (ContainsAny(paramName, "BaseDepth")) v.BaseDepth = val;
            if (ContainsAny(paramName, "SubBaseDepth", "SubbaseDepth")) v.SubBaseDepth = val;

            // GUIA / PASSEIO / ALTURA ESPECÃFICA
            if (EqualsAny(paramName, "Depth")) v.GuiaDepth = val;           // usado para GUIA nos seus cÃ³digos atuais
            if (EqualsAny(paramName, "SidewalkDepth")) v.PasseioDepth = val; // se existir
            if (ContainsAny(paramName, "CFTDepth")) v.Height = val;         // CFT

            // INCLINAÃ‡Ã•ES
            if (ContainsAny(paramName, "DefaultSlope", "Slope", "ShoulderSlope", "Deflection", "LinkSlope", "Crossfall"))
                v.Slope = val;

            // atualiza Ã¡rea sempre que algo relevante muda
            v.RecomputeArea();
        }

        private static void MapLong(ExtractedValues v, string paramName, long val)
        {
            if (ContainsAny(paramName, "LaneCount", "NumberOfLanes") && string.IsNullOrWhiteSpace(v.CodigoObjeto))
                v.CodigoObjeto = $"LANES_{val}";
        }

        private static void MapString(ExtractedValues v, string paramName, string val)
        {
            if (string.IsNullOrWhiteSpace(paramName) || string.IsNullOrWhiteSpace(val))
                return;

            if (ContainsAny(paramName, "Material", "Materia"))
                v.Material = FirstNonEmptyRuntime(v.Material, val);

            if (ContainsAny(paramName, "Layer", "Course", "Camada"))
                v.Camada = FirstNonEmptyRuntime(v.Camada, val);

            if (ContainsAny(paramName, "Function", "Funcao", "Role"))
                v.FuncaoCamada = FirstNonEmptyRuntime(v.FuncaoCamada, val);

            if (ContainsAny(paramName, "PavementType", "SurfaceType", "TipoPavimento"))
                v.TipoPavimento = FirstNonEmptyRuntime(v.TipoPavimento, val);

            if (ContainsAny(paramName, "Side", "Lado"))
                v.Lado = FirstNonEmptyRuntime(v.Lado, NormalizeSideValueRuntime(val));

            if (ContainsAny(paramName, "CodeName") && string.IsNullOrWhiteSpace(v.CodeName))
                v.CodeName = val.Trim();

            if (ContainsAny(paramName, "Comment", "Description", "Observ"))
                v.Localizacao = FirstNonEmptyRuntime(v.Localizacao, val.Trim());
        }

        // Helpers de comparaÃ§Ã£o:
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



        public static void ApplyToEntity(Entity entity, string codeName, Database db, Transaction tr)
        {
            if (entity == null) return;

            // 1) Normaliza e escolhe regra (layer + IfcExportAs)
            (string ExportAs, string Layer, short Aci, string ObjType) rule = GetPavRule(codeName);

            // 2) Garante layer + aplica layer + cor EXPLÃCITA (nÃ£o ByLayer) para sair certo no IFC
            EnsureLayer(db, tr, rule.Layer, rule.Aci);

            entity.UpgradeOpen();
            entity.Layer = rule.Layer;
            entity.Color = Color.FromColorIndex(ColorMethod.ByAci, rule.Aci);

            // 3) Injeta parÃ¢metros IFC se existir um PSet com os campos IFC:: (Autodesk IFC Extension)
            TryApplyIfcExportParameters(entity, db, tr, rule.ExportAs, rule.ObjType);
        }

        private static (string ExportAs, string Layer, short Aci, string ObjType) GetPavRule(string codeName)
        {
            string key = (codeName ?? string.Empty).Trim().ToUpperInvariant();

            // Ordem importa (SUBBASE antes de BASE)
            if (key.Contains("SUB_BASE") || key.Contains("SUBBASE"))
                return ("IfcCourse.PAVEMENT", "PAV_SUBBASE_IFC", (short)80, "SUBBASE");

            // BASE
            if (key.Contains("BASE"))
                return ("IfcCourse.PAVEMENT", "PAV_BASE_IFC", (short)30, "BASE");

            // PAVIMENTO (assumindo 2 camadas: PAVIMENTO1 = WEARING; PAVIMENTO2 = BINDER)
            if (key.Contains("CBUQ") || key.Contains("PAVE1") || ((key.Contains("PAVIMENTO") || key.Contains("PAVE")) && !key.Contains("PAVIMENTO2") && !key.Contains("PAVE2")))
                return ("IfcCourse.PAVEMENT", "PAV_WEARING_IFC", (short)1, "WEARING");

            if (key.Contains("CONCRETO") || key.Contains("PAVE2"))
                return ("IfcCourse.PAVEMENT", "PAV_BINDER_IFC", (short)2, "BINDER");

            // PASSEIO / CALÃ‡ADA
            if (key.Contains("PASSEIO") || key.Contains("CALCADA") || key.Contains("CALÃ‡ADA"))
                return ("IfcCourse.PAVEMENT", "PAV_SIDEWALK_IFC", (short)140, "SIDEWALK");

            // GUIA / MEIO-FIO (mantÃ©m como course, mas marca ObjectType)
            if (key.Contains("GUIA") || key.Contains("MEIOFIO") || key.Contains("MEIO-FIO"))
                return ("IfcCourse.PAVEMENT", "PAV_CURB_IFC", (short)6, "CURB");

            if (key.Contains("FRES") || key.Contains("MILL"))
                return ("IfcCourse.PAVEMENT", "PAV_MILLING_IFC", (short)21, "MILLING");

            if (key.Contains("TALUDE_ATERRO") || key.Contains("ATERRO") || key.Contains("FILL") || key.Contains("EMBANK"))
                return ("IfcGeotechnicalStratum.USERDEFINED", "TERR_ATERRO_IFC", (short)32, "FILL");

            if (key.Contains("TALUDE_CORTE") || key.Contains("CORTE") || key.Contains("CUT") || key.Contains("OFFSET_TALUDE") || key.Contains("CFT"))
                return ("IfcGeotechnicalStratum.USERDEFINED", "TERR_CORTE_IFC", (short)33, "CUT");

            if (key.Contains("TRANS"))
                return ("IfcGeotechnicalStratum.USERDEFINED", "TERR_TRANSICAO_IFC", (short)34, "TRANSITION");

            // fallback
            return ("IfcCourse.PAVEMENT", "PAV_COURSE_IFC", (short)7, key);
        }

        private static void TryApplyIfcExportParameters(Entity entity, Database db, Transaction tr, string exportAs, string objectType)
        {
            // Evita reentrÃ¢ncia (alguns ambientes disparam eventos ao setar psets)
            if (SolidosCorredores.IfcApplyGuard.Busy) return;

            try
            {
                SolidosCorredores.IfcApplyGuard.Busy = true;

                DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);

                // nomes comuns no template Autodesk / Infra IFC extension
                string[] psetCandidates = new[]
                {
                    "IfcObject Properties",
                    "Ifc Properties",
                    "IFC Properties",
                    "IFC"
                };

                ObjectId defId = ObjectId.Null;

                foreach (string psetName in psetCandidates)
                {
                    if (dictionary.Has(psetName, tr))
                    {
                        defId = dictionary.GetAt(psetName);
                        break;
                    }
                }

                if (defId == ObjectId.Null)
                {
                    return; // desenho nÃ£o tem o PSet IFC instalado
                }

                // garante pset aplicado
                PropertyDataServices.AddPropertySet(entity, defId);

	                ObjectId psId = GetPropertySetIdSafe(entity, defId);
	                if (psId == ObjectId.Null) return;

	                PropertySet pset = (PropertySet)tr.GetObject(psId, OpenMode.ForWrite);

	                // IFC::IfcExportAs (ex: IfcCourse.PAVEMENT)
	                if (TryGetAecPropId(pset, "IFC::IfcExportAs", out int idExportAs))
	                    pset.SetAt(idExportAs, exportAs);

	                // alguns templates usam IFC::IfcExportType (apenas tipo) + IFC::IfcExportAs (classe)
	                if (TryGetAecPropId(pset, "IFC::IfcExportType", out int idExportType))
                {
                    string[] parts = exportAs.Split('.');
                    if (parts.Length == 2) pset.SetAt(idExportType, parts[1]);
                }

	                // PredefinedType separado (quando existir)
	                if (TryGetAecPropId(pset, "IFC::PredefinedType", out int idPreType))
                {
                    string[] parts = exportAs.Split('.');
                    if (parts.Length == 2) pset.SetAt(idPreType, parts[1]);
                }

                // ObjectType (quando existir) â€“ Ãºtil para BASE/SUBBASE/etc.
                if (!string.IsNullOrWhiteSpace(objectType))
                {
	                    if (TryGetAecPropId(pset, "IFC::ObjectType", out int idObjType))
	                        pset.SetAt(idObjType, objectType);
                }
            }
            catch
            {
                // nÃ£o quebra exportaÃ§Ã£o por falha de pset
            }
            finally
            {
                SolidosCorredores.IfcApplyGuard.Busy = false;
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

        /*||||||||||||||||EXPORTAÃ‡ÃƒO IFC E LAYERS IFC||||||||||||||||

        private static readonly Dictionary<string, short> LayerColors = new Dictionary<string, short>
        {
            { "DREN_TUBO_IFC",        3 },   // Verde
            { "DREN_BUEIRO_IFC",      130 },
            { "DREN_CONEXAO_IFC",     6 },   // Magenta
            { "DREN_ABERTA_IFC",      1 },   // Vermelho
            { "DREN_ESTRUTURA_IFC",   5 },   // Azul
            { "IFC_PROXY",            8 }    // Cinza
        };

        // 1) Cria TODOS os layers IFC padronizados (chamar 1x por transaÃ§Ã£o)
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
	                ObjectId psId = GetPropertySetIdSafe(entity, defId);
	                if (psId == ObjectId.Null) return;
	                PropertySet pset = (PropertySet)tr.GetObject(psId, OpenMode.ForWrite);


	                if (TryGetAecPropId(pset, "IFC::IfcExportAs", out int idExportAs))
	                    pset.SetAt(idExportAs, ifcClass);

	                if (TryGetAecPropId(pset, "IFC::PredefinedType", out int idPreType))
	                    pset.SetAt(idPreType, predef);
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

