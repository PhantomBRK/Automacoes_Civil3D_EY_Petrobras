using System.Collections.Specialized;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Autodesk.Aec.PropertyData;
using Autodesk.Aec.PropertyData.DatabaseServices;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Label = Autodesk.Civil.DatabaseServices.Label;
using Color = Autodesk.AutoCAD.Colors.Color;
using DataType = Autodesk.Aec.PropertyData.DataType;

namespace AutomacoesCivil3D
{
    public class CorridorPropertySetsCreator
    {
        [CommandMethod("CRIA_PSETS_CORREDOR")]
        public void CriarPropertySetsCorredor()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            Database db = civilDoc.Database;

            try
            {
                using (Transaction trans = db.TransactionManager.StartTransaction())
                {
                    DictionaryPropertySetDefinitions dictPsets =
                        new DictionaryPropertySetDefinitions(db);

                    this.EnsurePsetA_DadosDoProjeto(dictPsets, db, trans);
                    this.EnsurePsetB_InformacoesObjetosElementos(dictPsets, db, trans);
                    this.EnsurePsetCoordenacao(dictPsets, db, trans);

                    trans.Commit();
                }

                docEditor.WriteMessage(
                    "\nProperty Sets de corredor verificados/criados (A, B e COORDENAÇÃO).");
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage("\nErro ao criar Property Sets: " + ex.Message);
            }
        }

        private void EnsurePsetA_DadosDoProjeto(
            DictionaryPropertySetDefinitions dictPsets,
            Database db,
            Transaction trans)
        {
            string psetName = "A - Dados do Projeto";

            if (dictPsets.Has(psetName, trans))
            {
                return;
            }

            PropertySetDefinition psetDef = new PropertySetDefinition();
            psetDef.SetToStandard(db);
            psetDef.SubSetDatabaseDefaults(db);
            psetDef.AlternateName = psetName;
            psetDef.Description = "Dados gerais do projeto";

            StringCollection appliesTo = new StringCollection();
            appliesTo.Add("AcDb3dSolid"); // aplicar em sólidos 3D (sólidos de corredor)
            bool applyToStyles = false;
            psetDef.SetAppliesToFilter(appliesTo, applyToStyles);

            // IdentificadordoProjeto (Text)
            PropertyDefinition defIdentificador = new PropertyDefinition();
            defIdentificador.SetToStandard(db);
            defIdentificador.SubSetDatabaseDefaults(db);
            defIdentificador.Name = "IdentificadordoProjeto";
            defIdentificador.Description = "Identificador do Projeto";
            defIdentificador.DataType = DataType.Text;
            defIdentificador.DefaultData = string.Empty;
            psetDef.Definitions.Add(defIdentificador);

            // NomeProjeto (Text, default BR-101)
            PropertyDefinition defNomeProjeto = new PropertyDefinition();
            defNomeProjeto.SetToStandard(db);
            defNomeProjeto.SubSetDatabaseDefaults(db);
            defNomeProjeto.Name = "NomeProjeto";
            defNomeProjeto.Description = "Nome do Projeto";
            defNomeProjeto.DataType = DataType.Text;
            defNomeProjeto.DefaultData = "BR-101";
            psetDef.Definitions.Add(defNomeProjeto);

            // Segmento (Text, default TH)
            PropertyDefinition defSegmento = new PropertyDefinition();
            defSegmento.SetToStandard(db);
            defSegmento.SubSetDatabaseDefaults(db);
            defSegmento.Name = "Segmento";
            defSegmento.Description = "Segmento";
            defSegmento.DataType = DataType.Text;
            defSegmento.DefaultData = "TH";
            psetDef.Definitions.Add(defSegmento);

            dictPsets.AddNewRecord(psetName, psetDef);
            trans.AddNewlyCreatedDBObject(psetDef, true);
        }

        private void EnsurePsetB_InformacoesObjetosElementos(
            DictionaryPropertySetDefinitions dictPsets,
            Database db,
            Transaction trans)
        {
            string psetName = "B - Informações dos Objetos e Elementos";

            if (dictPsets.Has(psetName, trans))
            {
                return;
            }

            PropertySetDefinition psetDef = new PropertySetDefinition();
            psetDef.SetToStandard(db);
            psetDef.SubSetDatabaseDefaults(db);
            psetDef.AlternateName = psetName;
            psetDef.Description = "Informações dos sólidos de corredor";

            StringCollection appliesTo = new StringCollection();
            appliesTo.Add("AcDb3dSolid"); // sólidos 3D
            bool applyToStyles = false;
            psetDef.SetAppliesToFilter(appliesTo, applyToStyles);

            // 1) CodeName [Formula] -> [Corridor Shape Information:CodeName]
            PropertyDefinitionFormula defCodeName = new PropertyDefinitionFormula();
            defCodeName.SetToStandard(db);
            defCodeName.SubSetDatabaseDefaults(db);
            defCodeName.Name = "CodeName";
            defCodeName.Description = "CodeName";
            defCodeName.DataType = DataType.Text;
            defCodeName.SetFormulaString("[Corridor Shape Information:CodeName]");
            psetDef.Definitions.Add(defCodeName);

            // 2) Código_do_Objeto (Text)
            PropertyDefinition defCodigoObjeto = new PropertyDefinition();
            defCodigoObjeto.SetToStandard(db);
            defCodigoObjeto.SubSetDatabaseDefaults(db);
            defCodigoObjeto.Name = "Código_do_Objeto";
            defCodigoObjeto.Description = "Código_do_Objeto";
            defCodigoObjeto.DataType = DataType.Text;
            defCodigoObjeto.DefaultData = string.Empty;
            psetDef.Definitions.Add(defCodigoObjeto);

            // 3) Comprimento [Formula] -> [COORDENAÇÃO:COMPRIMENTO_SOLIDOS_CORREDOR]
            PropertyDefinitionFormula defComprimento = new PropertyDefinitionFormula();
            defComprimento.SetToStandard(db);
            defComprimento.SubSetDatabaseDefaults(db);
            defComprimento.Name = "Comprimento";
            defComprimento.Description = "Comprimento do sólido de corredor";
            defComprimento.DataType = DataType.Real;
            defComprimento.SetFormulaString("[COORDENAÇÃO:COMPRIMENTO_SOLIDOS_CORREDOR]");
            psetDef.Definitions.Add(defComprimento);

            // 4) Disciplina (Text, default Pavimento)
            PropertyDefinition defDisciplina = new PropertyDefinition();
            defDisciplina.SetToStandard(db);
            defDisciplina.SubSetDatabaseDefaults(db);
            defDisciplina.Name = "Disciplina";
            defDisciplina.Description = "Disciplina";
            defDisciplina.DataType = DataType.Text;
            defDisciplina.DefaultData = "Pavimento";
            psetDef.Definitions.Add(defDisciplina);

            // 5) Estaqueamento_Final [Formula] -> [Corridor Identity:EndStation]
            PropertyDefinitionFormula defEstFinal = new PropertyDefinitionFormula();
            defEstFinal.SetToStandard(db);
            defEstFinal.SubSetDatabaseDefaults(db);
            defEstFinal.Name = "Estaqueamento_Final";
            defEstFinal.Description = "Estaqueamento Final";
            defEstFinal.DataType = DataType.Real;
            defEstFinal.SetFormulaString("[Corridor Identity:EndStation]");
            psetDef.Definitions.Add(defEstFinal);

            // 6) Estaqueamento_Inicial [Formula] -> [Corridor Identity:StartStation]
            PropertyDefinitionFormula defEstInicial = new PropertyDefinitionFormula();
            defEstInicial.SetToStandard(db);
            defEstInicial.SubSetDatabaseDefaults(db);
            defEstInicial.Name = "Estaqueamento_Inicial";
            defEstInicial.Description = "Estaqueamento Inicial";
            defEstInicial.DataType = DataType.Real;
            defEstInicial.SetFormulaString("[Corridor Identity:StartStation]");
            psetDef.Definitions.Add(defEstInicial);

            // 7) Localização (Text, default BR-101)
            PropertyDefinition defLocalizacao = new PropertyDefinition();
            defLocalizacao.SetToStandard(db);
            defLocalizacao.SubSetDatabaseDefaults(db);
            defLocalizacao.Name = "Localização";
            defLocalizacao.Description = "Localização";
            defLocalizacao.DataType = DataType.Text;
            defLocalizacao.DefaultData = "BR-101";
            psetDef.Definitions.Add(defLocalizacao);

            // 8) NomeCorredorSolido [Formula] -> [Corridor Model Information:CorridorName]
            PropertyDefinitionFormula defNomeCorridor = new PropertyDefinitionFormula();
            defNomeCorridor.SetToStandard(db);
            defNomeCorridor.SubSetDatabaseDefaults(db);
            defNomeCorridor.Name = "NomeCorredorSolido";
            defNomeCorridor.Description = "Nome do corredor do sólido";
            defNomeCorridor.DataType = DataType.Text;
            defNomeCorridor.SetFormulaString("[Corridor Model Information:CorridorName]");
            psetDef.Definitions.Add(defNomeCorridor);

            // 9) RegionName [Formula] -> [Corridor Identity:RegionGuid]
            PropertyDefinitionFormula defRegionName = new PropertyDefinitionFormula();
            defRegionName.SetToStandard(db);
            defRegionName.SubSetDatabaseDefaults(db);
            defRegionName.Name = "RegionName";
            defRegionName.Description = "Nome da Região do Corredor";
            defRegionName.DataType = DataType.Text;
            defRegionName.SetFormulaString("[Corridor Identity:RegionGuid]");
            psetDef.Definitions.Add(defRegionName);

            // 10) Situação
            // No estilo está como List; aqui deixei como Text com default Implantação.
            // Quando você mandar os valores de lista, ajustamos para o tipo adequado.
            PropertyDefinition defSituacao = new PropertyDefinition();
            defSituacao.SetToStandard(db);
            defSituacao.SubSetDatabaseDefaults(db);
            defSituacao.Name = "Situação";
            defSituacao.Description = "Situação";
            defSituacao.DataType = DataType.Text;
            defSituacao.DefaultData = "Implantação";
            psetDef.Definitions.Add(defSituacao);

            // 11) SubassemblyName [Formula] -> [Corridor Identity:SubassemblyName]
            PropertyDefinitionFormula defSubassemblyName = new PropertyDefinitionFormula();
            defSubassemblyName.SetToStandard(db);
            defSubassemblyName.SubSetDatabaseDefaults(db);
            defSubassemblyName.Name = "SubassemblyName";
            defSubassemblyName.Description = "Nome da Subassembly";
            defSubassemblyName.DataType = DataType.Text;
            defSubassemblyName.SetFormulaString("[Corridor Identity:SubassemblyName]");
            psetDef.Definitions.Add(defSubassemblyName);

            dictPsets.AddNewRecord(psetName, psetDef);
            trans.AddNewlyCreatedDBObject(psetDef, true);
        }

        private void EnsurePsetCoordenacao(
            DictionaryPropertySetDefinitions dictPsets,
            Database db,
            Transaction trans)
        {
            string psetName = "COORDENAÇÃO";

            if (dictPsets.Has(psetName, trans))
            {
                ObjectId existingId = dictPsets.GetAt(psetName);
                PropertySetDefinition existing = (PropertySetDefinition)trans.GetObject(existingId, OpenMode.ForWrite);
                this.EnsureTextField(existing, db, "AREA_3D_SUPERFICIE", "Área 3D da superfície do sólido de corredor");
                this.EnsureTextField(existing, db, "COMPRIMENTO_3D_FEATURE_LINES", "Comprimento 3D associado às feature lines do sólido de corredor");
                this.EnsureTextField(existing, db, "COMPRIMENTO_SOLIDOS_CORREDOR", "Comprimento do sólido de corredor");
                return;
            }

            PropertySetDefinition psetDef = new PropertySetDefinition();
            psetDef.SetToStandard(db);
            psetDef.SubSetDatabaseDefaults(db);
            psetDef.AlternateName = psetName;
            psetDef.Description = "Métricas geométricas e de coordenação dos sólidos de corredor";

            StringCollection appliesTo = new StringCollection();
            appliesTo.Add("AcDb3dSolid");
            appliesTo.Add("AcDbBody");
            bool applyToStyles = false;
            psetDef.SetAppliesToFilter(appliesTo, applyToStyles);

            PropertyDefinition defArea = new PropertyDefinition();
            defArea.SetToStandard(db);
            defArea.SubSetDatabaseDefaults(db);
            defArea.Name = "AREA_3D_SUPERFICIE";
            defArea.Description = "Área 3D da superfície do sólido de corredor";
            defArea.DataType = DataType.Text;
            defArea.DefaultData = string.Empty;
            psetDef.Definitions.Add(defArea);

            PropertyDefinition defFeatureLines = new PropertyDefinition();
            defFeatureLines.SetToStandard(db);
            defFeatureLines.SubSetDatabaseDefaults(db);
            defFeatureLines.Name = "COMPRIMENTO_3D_FEATURE_LINES";
            defFeatureLines.Description = "Comprimento 3D associado às feature lines do sólido de corredor";
            defFeatureLines.DataType = DataType.Text;
            defFeatureLines.DefaultData = string.Empty;
            psetDef.Definitions.Add(defFeatureLines);

            PropertyDefinition defComprimentoSolido = new PropertyDefinition();
            defComprimentoSolido.SetToStandard(db);
            defComprimentoSolido.SubSetDatabaseDefaults(db);
            defComprimentoSolido.Name = "COMPRIMENTO_SOLIDOS_CORREDOR";
            defComprimentoSolido.Description = "Comprimento do sólido de corredor";
            defComprimentoSolido.DataType = DataType.Text;
            defComprimentoSolido.DefaultData = string.Empty;
            psetDef.Definitions.Add(defComprimentoSolido);

            dictPsets.AddNewRecord(psetName, psetDef);
            trans.AddNewlyCreatedDBObject(psetDef, true);
        }

        private void EnsureTextField(
            PropertySetDefinition psetDef,
            Database db,
            string fieldName,
            string description)
        {
            foreach (PropertyDefinition definition in psetDef.Definitions)
            {
                if (string.Equals(definition?.Name, fieldName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            PropertyDefinition field = new PropertyDefinition();
            field.SetToStandard(db);
            field.SubSetDatabaseDefaults(db);
            field.Name = fieldName;
            field.Description = description;
            field.DataType = DataType.Text;
            field.DefaultData = string.Empty;
            psetDef.Definitions.Add(field);
        }
    }
}
