// Para PropertySetDefinition, PropertyDefinition, PropertySetData, PropertySetDefinitionServices, PropertySetDataServices
// Estas classes são fundamentais para trabalhar com Property Sets e estão tipicamente na biblioteca AecBaseMgd.dll,
// que deve ser referenciada no seu projeto (geralmente localizada em C:\Program Files\Autodesk\AutoCAD 20xx\AecBaseMgd.dll).
using Autodesk.Aec.ApplicationServices;
using Autodesk.Aec.PropertyData;
using Autodesk.Aec.PropertyData.DatabaseServices; // Importante!
using Autodesk.Aec.PropertyData.DatabaseServices; // Namespace para Property Sets
// Certifique-se de adicionar as seguintes referências ao seu projeto no Visual Studio:
// - AcCoreMgd.dll
// - AcDbMgd.dll
// - AcMgd.dll
// - AecBaseMgd.dll (essencial para DataStore e outras funcionalidades AEC)
// - AecPropDataMgd.dll (para as classes de Property Set)

using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using ICSharpCode.Decompiler.DebugInfo; // Importante!
using System; // Para System.Exception e StringComparison
using System;
using System.Collections.Specialized; // Para StringCollection
using System.Linq; // Essencial para o método .Any() na verificação de propriedades
using System.Linq;
using System.Windows.Forms.Design;
// Aliases para evitar conflitos de namespace, conforme sua instrução.
// É uma boa prática para melhorar a legibilidade e evitar ambiguidades.
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception; // Alias para a exceção específica do AutoCAD Runtime

namespace AutomacoesCivil3D
{
    public class DefinirValoresPSet
    {
        [CommandMethod("pSetTeste")]
        public void pSet()
        {
            // Obter o banco de dados atual do projeto
            Database db = Manager.DocData;
            Editor docEditor = Manager.DocEditor;

            /*/ Seleção da rede de drenagem
            PromptEntityOptions peo = new PromptEntityOptions("\nSelecione Qualquer Tubo da rede de drenagem (PipeNetwork):");
            peo.SetRejectMessage("\nPor favor, selecione apenas Tubos da Rede.");
            peo.AddAllowedClass(typeof(Corridor), false);

            PromptEntityResult per = docEditor.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
            {
                docEditor.WriteMessage("\nNão foi possível selecionar uma rede de drenagem.");
                return;
            }*/

            // Iniciar uma transação
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Acessar o dicionário de property sets
                DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);


                PropertySetDefinition ps = new PropertySetDefinition();
                ps.SetToStandard(db); // Inicializa a definição com as configurações padrão do sistema.
                ps.SubSetDatabaseDefaults(db); // Aplica quaisquer valores padrão ou configurações específicas do banco de dados.
                ps.AppliesToAll = true; // Indica que este PropertySet pode ser aplicado a qualquer tipo de objeto no desenho.
                ps.AlternateName = "Integridade no Preenchimento das Informações"; // Um nome alternativo ou mais descritivo para o PropertySet.
                ps.Description = "Integridade no Preenchimento das Informações (Atributos Minimos)"; // Uma descrição detalhada do propósito do PropertySet.

                // D1: DESCRIPTION - Identificação resumida do elemento
                PropertyDefinition propDef1 = new PropertyDefinition();
                propDef1.SetToStandard(db);
                propDef1.SubSetDatabaseDefaults(db);
                propDef1.Name = "DESCRIPTION"; // O 'Name' é o identificador programático único da propriedade. É uma boa prática usar ALL_CAPS e nomes concisos para fácil acesso via API.
                propDef1.Description = "D1 - Identificação resumida do elemento"; // A 'Description' é o nome amigável exibido aos usuários na interface.
                propDef1.DataType = Autodesk.Aec.PropertyData.DataType.Text; // Definido como 'Text' para permitir descrições alfanuméricas.
                propDef1.DefaultData = " - "; // Valor padrão para a propriedade.
                ps.Definitions.Add(propDef1); // Adiciona a definição da propriedade ao PropertySet.

                // D2: DISCIPLINE - Disciplina (AWP)
                PropertyDefinition propDef2 = new PropertyDefinition();
                propDef2.SetToStandard(db);
                propDef2.SubSetDatabaseDefaults(db);
                propDef2.Name = "DISCIPLINE";
                propDef2.Description = "D2 - Disciplina (AWP)";
                propDef2.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                propDef2.DefaultData = " - ";
                ps.Definitions.Add(propDef2);

                // D3: SUBAREA - EAP (Área/Subárea)
                PropertyDefinition propDef3 = new PropertyDefinition();
                propDef3.SetToStandard(db);
                propDef3.SubSetDatabaseDefaults(db);
                propDef3.Name = "SUBAREA";
                propDef3.Description = "D3 - EAP (Área/Subárea)";
                propDef3.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                propDef3.DefaultData = " - ";
                ps.Definitions.Add(propDef3);

                // D4: UNIDADE_MEDIDA - Unidade de medida (m, m², m³, un, kg) (SPE)
                PropertyDefinition propDef4 = new PropertyDefinition();
                propDef4.SetToStandard(db);
                propDef4.SubSetDatabaseDefaults(db);
                propDef4.Name = "UNIDADE_MEDIDA";
                propDef4.Description = "D4 - Unidade de medida (m, m², m³, un, kg) (SPE)";
                propDef4.DataType = Autodesk.Aec.PropertyData.DataType.Text; // Mantido como Text para flexibilidade, permitindo unidades como "m²" ou "un".
                propDef4.DefaultData = " - ";
                ps.Definitions.Add(propDef4);

                // D5: MIS - Model Item Status
                PropertyDefinition propDef5 = new PropertyDefinition();
                propDef5.SetToStandard(db);
                propDef5.SubSetDatabaseDefaults(db);
                propDef5.Name = "MIS";
                propDef5.Description = "D5 - Model Item Status";
                propDef5.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                propDef5.DefaultData = " - ";
                ps.Definitions.Add(propDef5);

                // D6: ACTIVITY_CODE - Código de atividade (SPE)
                PropertyDefinition propDef6 = new PropertyDefinition();
                propDef6.SetToStandard(db);
                propDef6.SubSetDatabaseDefaults(db);
                propDef6.Name = "ACTIVITY_CODE";
                propDef6.Description = "D6 - Código de atividade (SPE)";
                propDef6.DataType = Autodesk.Aec.PropertyData.DataType.Text; // Códigos podem ser alfanuméricos.
                propDef6.DefaultData = " - ";
                ps.Definitions.Add(propDef6);

                // D7: ACTIVITY_DESCRIPTION - Descrição de atividade (SPE)
                PropertyDefinition propDef7 = new PropertyDefinition();
                propDef7.SetToStandard(db);
                propDef7.SubSetDatabaseDefaults(db);
                propDef7.Name = "ACTIVITY_DESCRIPTION";
                propDef7.Description = "D7 - Descrição de atividade (SPE)";
                propDef7.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                propDef7.DefaultData = " - ";
                ps.Definitions.Add(propDef7);

                // D8: MEASUREMENT_UNIT - Quantitativo do elemento (conjunto, metro linear, área, volume, unidade)
                PropertyDefinition propDef8 = new PropertyDefinition();
                propDef8.SetToStandard(db);
                propDef8.SubSetDatabaseDefaults(db);
                propDef8.Name = "MEASUREMENT_UNIT";
                propDef8.Description = "D8 - Quantitativo do elemento (conjunto, metro linear, área, volume, unidade)";
                propDef8.DataType = Autodesk.Aec.PropertyData.DataType.Text; // Mantido como Text para flexibilidade.
                propDef8.DefaultData = " - ";
                ps.Definitions.Add(propDef8);

                // D9: MEASUREMENT_CODE - Código de medição do serviço (CMS) (SPE)
                PropertyDefinition propDef9 = new PropertyDefinition();
                propDef9.SetToStandard(db);
                propDef9.SubSetDatabaseDefaults(db);
                propDef9.Name = "MEASUREMENT_CODE";
                propDef9.Description = "D9 - Código de medição do serviço (CMS) (SPE)";
                propDef9.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                propDef9.DefaultData = " - ";
                ps.Definitions.Add(propDef9);

                // D10: CWA - CWA (Construction Work Area)
                PropertyDefinition propDef10 = new PropertyDefinition();
                propDef10.SetToStandard(db);
                propDef10.SubSetDatabaseDefaults(db);
                propDef10.Name = "CWA";
                propDef10.Description = "D10 - CWA (Construction Work Area)";
                propDef10.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                propDef10.DefaultData = " - ";
                ps.Definitions.Add(propDef10);

                // D11: CWP - CWP (Construction Work Package)
                PropertyDefinition propDef11 = new PropertyDefinition();
                propDef11.SetToStandard(db);
                propDef11.SubSetDatabaseDefaults(db);
                propDef11.Name = "CWP";
                propDef11.Description = "D11 - CWP (Construction Work Package)";
                propDef11.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                propDef11.DefaultData = " - ";
                ps.Definitions.Add(propDef11);

                // D12: EWP - EWP (Engineering Work Package)
                PropertyDefinition propDef12 = new PropertyDefinition();
                propDef12.SetToStandard(db);
                propDef12.SubSetDatabaseDefaults(db);
                propDef12.Name = "EWP";
                propDef12.Description = "D12 - EWP (Engineering Work Package)";
                propDef12.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                propDef12.DefaultData = " - ";
                ps.Definitions.Add(propDef12);

                // D13: IWP - IWP (Installation Work Package)
                PropertyDefinition propDef13 = new PropertyDefinition();
                propDef13.SetToStandard(db);
                propDef13.SubSetDatabaseDefaults(db);
                propDef13.Name = "IWP";
                propDef13.Description = "D13 - IWP (Installation Work Package)";
                propDef13.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                propDef13.DefaultData = " - ";
                ps.Definitions.Add(propDef13);

                // D14: PWP - PWP (Procurement Work Package)
                PropertyDefinition propDef14 = new PropertyDefinition();
                propDef14.SetToStandard(db);
                propDef14.SubSetDatabaseDefaults(db);
                propDef14.Name = "PWP";
                propDef14.Description = "D14 - PWP (Procurement Work Package)";
                propDef14.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                propDef14.DefaultData = " - ";
                ps.Definitions.Add(propDef14);

                dictionary.AddNewRecord("C1 - GERAL", ps); // Adiciona o PropertySet ao dicionário de PropertySets do banco de dados.
                tr.AddNewlyCreatedDBObject(ps, true); // Adiciona o PropertySet recém-criado à transação, tornando-o persistente no desenho.

                // Definição do PropertySet "C2 - CADASTRO DE INTERFERENCIAS"
                // Este PropertySet é focado em dados para o cadastro e gerenciamento de interferências.
                PropertySetDefinition cd = new PropertySetDefinition();
                cd.SetToStandard(db);
                cd.SubSetDatabaseDefaults(db);
                cd.AppliesToAll = true;
                cd.AlternateName = "Integridade no Preenchimento das Informações";
                cd.Description = "Integridade no Preenchimento das Informações (Atributos Minimos)";

                // D1: TYPE - Tipo
                PropertyDefinition cadastroD1 = new PropertyDefinition();
                cadastroD1.SetToStandard(db);
                cadastroD1.SubSetDatabaseDefaults(db);
                cadastroD1.Name = "TYPE";
                cadastroD1.Description = "D1 - Tipo (Type)";
                cadastroD1.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                cadastroD1.DefaultData = " - ";
                cd.Definitions.Add(cadastroD1);

                // D2: OBJECT - Objeto
                PropertyDefinition cadastroD2 = new PropertyDefinition();
                cadastroD2.SetToStandard(db);
                cadastroD2.SubSetDatabaseDefaults(db);
                cadastroD2.Name = "OBJECT";
                cadastroD2.Description = "D2 - Objeto (Object)";
                cadastroD2.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                cadastroD2.DefaultData = " - ";
                cd.Definitions.Add(cadastroD2);

                // D3: SICRO_CODE - Código SICRO
                PropertyDefinition cadastroD3 = new PropertyDefinition();
                cadastroD3.SetToStandard(db);
                cadastroD3.SubSetDatabaseDefaults(db);
                cadastroD3.Name = "SICRO_CODE";
                cadastroD3.Description = "D3 - Código SICRO (SICRO Code)";
                cadastroD3.DataType = Autodesk.Aec.PropertyData.DataType.Text; // Códigos podem ser alfanuméricos.
                cadastroD3.DefaultData = " - ";
                cd.Definitions.Add(cadastroD3);

                // D4: PLANNING - Planejamento
                PropertyDefinition cadastroD4 = new PropertyDefinition();
                cadastroD4.SetToStandard(db);
                cadastroD4.SubSetDatabaseDefaults(db);
                cadastroD4.Name = "PLANNING";
                cadastroD4.Description = "D4 - Planejamento (Planning)";
                cadastroD4.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                cadastroD4.DefaultData = " - ";
                cd.Definitions.Add(cadastroD4);

                // D5: MATERIAL - Material
                PropertyDefinition cadastroD5 = new PropertyDefinition();
                cadastroD5.SetToStandard(db);
                cadastroD5.SubSetDatabaseDefaults(db);
                cadastroD5.Name = "MATERIAL";
                cadastroD5.Description = "D5 - Material (Material)";
                cadastroD5.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                cadastroD5.DefaultData = " - ";
                cd.Definitions.Add(cadastroD5);

                // D6: LENGTH - Comprimento (m)
                PropertyDefinition cadastroD6 = new PropertyDefinition();
                cadastroD6.SetToStandard(db);
                cadastroD6.SubSetDatabaseDefaults(db);
                cadastroD6.Name = "LENGTH";
                cadastroD6.Description = "D6 - Comprimento (m) (Length (m))";
                cadastroD6.DataType = Autodesk.Aec.PropertyData.DataType.Real; // 'Real' é usado para números de ponto flutuante, adequado para medidas.
                cadastroD6.DefaultData = 0.0; // Valor padrão numérico.
                cd.Definitions.Add(cadastroD6);

                // D7: WIDTH - Largura (m)
                PropertyDefinition cadastroD7 = new PropertyDefinition();
                cadastroD7.SetToStandard(db);
                cadastroD7.SubSetDatabaseDefaults(db);
                cadastroD7.Name = "WIDTH";
                cadastroD7.Description = "D7 - Largura (m) (Width (m))";
                cadastroD7.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                cadastroD7.DefaultData = 0.0;
                cd.Definitions.Add(cadastroD7);

                // D8: START_STATION_KM - Estaca/km inicial
                PropertyDefinition cadastroD8 = new PropertyDefinition();
                cadastroD8.SetToStandard(db);
                cadastroD8.SubSetDatabaseDefaults(db);
                cadastroD8.Name = "START_STATION_KM";
                cadastroD8.Description = "D8 - Estaca/km inicial (Initial Stake/km)";
                cadastroD8.DataType = Autodesk.Aec.PropertyData.DataType.Real; // Estacas/km são geralmente valores numéricos.
                cadastroD8.DefaultData = 0.0;
                cd.Definitions.Add(cadastroD8);

                // D9: END_STATION_KM - Estaca/km final
                PropertyDefinition cadastroD9 = new PropertyDefinition();
                cadastroD9.SetToStandard(db);
                cadastroD9.SubSetDatabaseDefaults(db);
                cadastroD9.Name = "END_STATION_KM";
                cadastroD9.Description = "D9 - Estaca/km final (Final Stake/km)";
                cadastroD9.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                cadastroD9.DefaultData = 0.0;
                cd.Definitions.Add(cadastroD9);

                // D10: MIDDLE_STATION_KM - Estaca/km eixo
                PropertyDefinition cadastroD10 = new PropertyDefinition();
                cadastroD10.SetToStandard(db);
                cadastroD10.SubSetDatabaseDefaults(db);
                cadastroD10.Name = "MIDDLE_STATION_KM";
                cadastroD10.Description = "D10 - Estaca/km eixo (Axis Stake/km)";
                cadastroD10.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                cadastroD10.DefaultData = 0.0;
                cd.Definitions.Add(cadastroD10);

                // D11: RIM_ELEVATION - Cota início/topo
                PropertyDefinition cadastroD11 = new PropertyDefinition();
                cadastroD11.SetToStandard(db);
                cadastroD11.SubSetDatabaseDefaults(db);
                cadastroD11.Name = "RIM_ELEVATION";
                cadastroD11.Description = "D11 - Cota início/topo (Start/Top Elevation)";
                cadastroD11.DataType = Autodesk.Aec.PropertyData.DataType.Real; // Cotas/elevações são valores numéricos.
                cadastroD11.DefaultData = 0.0;
                cd.Definitions.Add(cadastroD11);

                // D12: SUMP_ELEVATION - Cota fim/fundo
                PropertyDefinition cadastroD12 = new PropertyDefinition();
                cadastroD12.SetToStandard(db);
                cadastroD12.SubSetDatabaseDefaults(db);
                cadastroD12.Name = "SUMP_ELEVATION";
                cadastroD12.Description = "D12 - Cota fim/fundo (End/Bottom Elevation)";
                cadastroD12.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                cadastroD12.DefaultData = 0.0;
                cd.Definitions.Add(cadastroD12);

                dictionary.AddNewRecord("C2 - CADASTRO DE INTERFERENCIAS", cd);
                tr.AddNewlyCreatedDBObject(cd, true);

                // Definição do PropertySet "C3 - CONTENCOES"
                // Este PropertySet é para dados de contenções.
                PropertySetDefinition ct = new PropertySetDefinition();
                ct.SetToStandard(db);
                ct.SubSetDatabaseDefaults(db);
                ct.AppliesToAll = true;
                ct.AlternateName = "Integridade no Preenchimento das Informações";
                ct.Description = "Integridade no Preenchimento das Informações (Atributos Minimos)";

                // D1: TYPE - Tipo
                PropertyDefinition contencaoD1 = new PropertyDefinition();
                contencaoD1.SetToStandard(db);
                contencaoD1.SubSetDatabaseDefaults(db);
                contencaoD1.Name = "TYPE";
                contencaoD1.Description = "D1: Tipo";
                contencaoD1.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                contencaoD1.DefaultData = " - ";
                ct.Definitions.Add(contencaoD1);

                // D2: OBJECT - Objeto
                PropertyDefinition contencaoD2 = new PropertyDefinition();
                contencaoD2.SetToStandard(db);
                contencaoD2.SubSetDatabaseDefaults(db);
                contencaoD2.Name = "OBJECT";
                contencaoD2.Description = "D2: Objeto";
                contencaoD2.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                contencaoD2.DefaultData = " - ";
                ct.Definitions.Add(contencaoD2);

                // D3: SICRO_CODE - Código SICRO
                PropertyDefinition contencaoD3 = new PropertyDefinition();
                contencaoD3.SetToStandard(db);
                contencaoD3.SubSetDatabaseDefaults(db);
                contencaoD3.Name = "SICRO_CODE";
                contencaoD3.Description = "D3: Código SICRO";
                contencaoD3.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                contencaoD3.DefaultData = " - ";
                ct.Definitions.Add(contencaoD3);

                // D4: PLANNING - Planejamento
                PropertyDefinition contencaoD4 = new PropertyDefinition();
                contencaoD4.SetToStandard(db);
                contencaoD4.SubSetDatabaseDefaults(db);
                contencaoD4.Name = "PLANNING";
                contencaoD4.Description = "D4: Planejamento";
                contencaoD4.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                contencaoD4.DefaultData = " - ";
                ct.Definitions.Add(contencaoD4);

                // D5: MATERIAL - Material
                PropertyDefinition contencaoD5 = new PropertyDefinition();
                contencaoD5.SetToStandard(db);
                contencaoD5.SubSetDatabaseDefaults(db);
                contencaoD5.Name = "MATERIAL";
                contencaoD5.Description = "D5: Material";
                contencaoD5.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                contencaoD5.DefaultData = " - ";
                ct.Definitions.Add(contencaoD5);

                // D6: LENGTH - Comprimento (m)
                PropertyDefinition contencaoD6 = new PropertyDefinition();
                contencaoD6.SetToStandard(db);
                contencaoD6.SubSetDatabaseDefaults(db);
                contencaoD6.Name = "LENGTH";
                contencaoD6.Description = "D6: Comprimento (m)";
                contencaoD6.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                contencaoD6.DefaultData = 0.0;
                ct.Definitions.Add(contencaoD6);

                // D7: VOLUME - Volume (m³)
                PropertyDefinition contencaoD7 = new PropertyDefinition();
                contencaoD7.SetToStandard(db);
                contencaoD7.SubSetDatabaseDefaults(db);
                contencaoD7.Name = "VOLUME";
                contencaoD7.Description = "D7: Volume (m³)";
                contencaoD7.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                contencaoD7.DefaultData = 0.0;
                ct.Definitions.Add(contencaoD7);

                // D8: START_STATION_KM - Estaca/km inicial
                PropertyDefinition contencaoD8 = new PropertyDefinition();
                contencaoD8.SetToStandard(db);
                contencaoD8.SubSetDatabaseDefaults(db);
                contencaoD8.Name = "START_STATION_KM";
                contencaoD8.Description = "D8: Estaca/km inicial";
                contencaoD8.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                contencaoD8.DefaultData = 0.0;
                ct.Definitions.Add(contencaoD8);

                // D9: END_STATION_KM - Estaca/km final
                PropertyDefinition contencaoD9 = new PropertyDefinition();
                contencaoD9.SetToStandard(db);
                contencaoD9.SubSetDatabaseDefaults(db);
                contencaoD9.Name = "END_STATION_KM";
                contencaoD9.Description = "D9: Estaca/km final";
                contencaoD9.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                contencaoD9.DefaultData = 0.0;
                ct.Definitions.Add(contencaoD9);

                dictionary.AddNewRecord("C3 - CONTENCOES", ct);
                tr.AddNewlyCreatedDBObject(ct, true);


                PropertySetDefinition dr = new PropertySetDefinition();

                
                dr.SetToStandard(db);
                dr.SubSetDatabaseDefaults(db);
                StringCollection appliedto = new StringCollection();
                appliedto.Add("AeccDbPipe");      // Tubos de rede de tubulação
                appliedto.Add("AeccDbStructure"); // Estruturas de rede de tubulação (bueiros, caixas, etc.)
                appliedto.Add("AeccDbCorridor");  // Corredores (elementos de rodovias, ferrovias, etc.)
                dr.SetAppliesToFilter(appliedto, false); // O 'false' indica que a lista é inclusiva.
                dr.AlternateName = "Integridade no Preenchimento das Informações";
                dr.Description = "Integridade no Preenchimento das Informações (Atributos Minimos)";

               

                // D1: Tipo
                PropertyDefinition drenagemD1 = new PropertyDefinition();
                drenagemD1.SetToStandard(db);
                drenagemD1.SubSetDatabaseDefaults(db);
                drenagemD1.Name = "TYPE"; // Nome interno do atributo de sistema
                drenagemD1.Description = "D1 - Tipo"; // Descrição visível ao usuário
                drenagemD1.DataType = Autodesk.Aec.PropertyData.DataType.Text; // Tipo de dado: Texto
                drenagemD1.DefaultData = " - "; // Valor padrão
                dr.Definitions.Add(drenagemD1); // Adiciona a definição ao Property Set

                // D2: Objeto
                PropertyDefinition drenagemD2 = new PropertyDefinition();
                drenagemD2.SetToStandard(db);
                drenagemD2.SubSetDatabaseDefaults(db);
                drenagemD2.Name = "OBJECT";
                drenagemD2.Description = "D2 - Objeto";
                drenagemD2.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                drenagemD2.DefaultData = " - ";
                dr.Definitions.Add(drenagemD2);

                // D3: Código SICRO
                PropertyDefinition drenagemD3 = new PropertyDefinition();
                drenagemD3.SetToStandard(db);
                drenagemD3.SubSetDatabaseDefaults(db);
                drenagemD3.Name = "SICRO_CODE";
                drenagemD3.Description = "D3 - Código SICRO";
                drenagemD3.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                drenagemD3.DefaultData = " - ";
                dr.Definitions.Add(drenagemD3);

                // D4: Planejamento
                PropertyDefinition drenagemD4 = new PropertyDefinition();
                drenagemD4.SetToStandard(db);
                drenagemD4.SubSetDatabaseDefaults(db);
                drenagemD4.Name = "PLANNING";
                drenagemD4.Description = "D4 - Planejamento";
                drenagemD4.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                drenagemD4.DefaultData = " - ";
                dr.Definitions.Add(drenagemD4);

                // D5: Nome do grupo
                PropertyDefinition drenagemD5 = new PropertyDefinition();
                drenagemD5.SetToStandard(db);
                drenagemD5.SubSetDatabaseDefaults(db);
                drenagemD5.Name = "GROUP_NAME";
                drenagemD5.Description = "D5 - Nome do grupo";
                drenagemD5.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                drenagemD5.DefaultData = " - ";
                dr.Definitions.Add(drenagemD5);

                // D6: Material
                PropertyDefinition drenagemD6 = new PropertyDefinition();
                drenagemD6.SetToStandard(db);
                drenagemD6.SubSetDatabaseDefaults(db);
                drenagemD6.Name = "MATERIAL";
                drenagemD6.Description = "D6 - Material";
                drenagemD6.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                drenagemD6.DefaultData = " - ";
                dr.Definitions.Add(drenagemD6);

                // D7: Comprimento (m)
                PropertyDefinition drenagemD7 = new PropertyDefinition();
                drenagemD7.SetToStandard(db);
                drenagemD7.SubSetDatabaseDefaults(db);
                drenagemD7.Name = "LENGTH";
                drenagemD7.Description = "D7 - Comprimento (m)";
                drenagemD7.DataType = Autodesk.Aec.PropertyData.DataType.Real; // Tipo de dado: Número real (para cálculos)
                drenagemD7.DefaultData = 0.0; // Valor padrão numérico
                dr.Definitions.Add(drenagemD7);

                // D8: Largura (m)
                PropertyDefinition drenagemD8 = new PropertyDefinition();
                drenagemD8.SetToStandard(db);
                drenagemD8.SubSetDatabaseDefaults(db);
                drenagemD8.Name = "WIDTH";
                drenagemD8.Description = "D8 - Largura (m)";
                drenagemD8.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                drenagemD8.DefaultData = 0.0;
                dr.Definitions.Add(drenagemD8);

                // D9: Altura (m)
                PropertyDefinition drenagemD9 = new PropertyDefinition();
                drenagemD9.SetToStandard(db);
                drenagemD9.SubSetDatabaseDefaults(db);
                drenagemD9.Name = "HEIGHT";
                drenagemD9.Description = "D9 - Altura (m)";
                drenagemD9.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                drenagemD9.DefaultData = 0.0;
                dr.Definitions.Add(drenagemD9);

                // D10: Volume (m³)
                PropertyDefinition drenagemD10 = new PropertyDefinition();
                drenagemD10.SetToStandard(db);
                drenagemD10.SubSetDatabaseDefaults(db);
                drenagemD10.Name = "VOLUME";
                drenagemD10.Description = "D10 - Volume (m³)";
                drenagemD10.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                drenagemD10.DefaultData = 0.0;
                dr.Definitions.Add(drenagemD10);

                // D11: Estaca/km inicial
                PropertyDefinition drenagemD11 = new PropertyDefinition();
                drenagemD11.SetToStandard(db);
                drenagemD11.SubSetDatabaseDefaults(db);
                drenagemD11.Name = "START_STATION_KM";
                drenagemD11.Description = "D11 - Estaca/km inicial";
                drenagemD11.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                drenagemD11.DefaultData = 0.0;
                dr.Definitions.Add(drenagemD11);

                // D12: Estaca/km final
                PropertyDefinition drenagemD12 = new PropertyDefinition();
                drenagemD12.SetToStandard(db);
                drenagemD12.SubSetDatabaseDefaults(db);
                drenagemD12.Name = "END_STATION_KM";
                drenagemD12.Description = "D12 - Estaca/km final";
                drenagemD12.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                drenagemD12.DefaultData = 0.0;
                dr.Definitions.Add(drenagemD12);

                // D13: Cota início/topo
                PropertyDefinition drenagemD13 = new PropertyDefinition();
                drenagemD13.SetToStandard(db);
                drenagemD13.SubSetDatabaseDefaults(db);
                drenagemD13.Name = "RIM_ELEVATION";
                drenagemD13.Description = "D13 - Cota início/topo";
                drenagemD13.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                drenagemD13.DefaultData = 0.0;
                dr.Definitions.Add(drenagemD13);

                // D14: Cota fim/fundo
                PropertyDefinition drenagemD14 = new PropertyDefinition();
                drenagemD14.SetToStandard(db);
                drenagemD14.SubSetDatabaseDefaults(db);
                drenagemD14.Name = "SUMP_ELEVATION";
                drenagemD14.Description = "D14 - Cota fim/fundo";
                drenagemD14.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                drenagemD14.DefaultData = 0.0;
                dr.Definitions.Add(drenagemD14);

                // D15: Altura interna da seção da galeria
                PropertyDefinition drenagemD15 = new PropertyDefinition();
                drenagemD15.SetToStandard(db);
                drenagemD15.SubSetDatabaseDefaults(db);
                drenagemD15.Name = "GALLERY_INTERNAL_HEIGHT";
                drenagemD15.Description = "D15 - Altura interna da seção da galeria";
                drenagemD15.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                drenagemD15.DefaultData = 0.0;
                dr.Definitions.Add(drenagemD15);

                // D16: Cota de jusante
                PropertyDefinition drenagemD16 = new PropertyDefinition();
                drenagemD16.SetToStandard(db);
                drenagemD16.SubSetDatabaseDefaults(db);
                drenagemD16.Name = "DOWNSTREAM_ELEVATION";
                drenagemD16.Description = "D16 - Cota de jusante";
                drenagemD16.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                drenagemD16.DefaultData = 0.0;
                dr.Definitions.Add(drenagemD16);

                // D17: Cota de montante
                PropertyDefinition drenagemD17 = new PropertyDefinition();
                drenagemD17.SetToStandard(db);
                drenagemD17.SubSetDatabaseDefaults(db);
                drenagemD17.Name = "UPSTREAM_ELEVATION";
                drenagemD17.Description = "D17 - Cota de montante";
                drenagemD17.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                drenagemD17.DefaultData = 0.0;
                dr.Definitions.Add(drenagemD17);

                // D18: Data de execução
                PropertyDefinition drenagemD18 = new PropertyDefinition();
                drenagemD18.SetToStandard(db);
                drenagemD18.SubSetDatabaseDefaults(db);
                drenagemD18.Name = "EXECUTION_DATE"; // Note o potencial de duplicidade de 'Name' com D25 e D30
                drenagemD18.Description = "D18 - Data de execução";
                drenagemD18.DataType = Autodesk.Aec.PropertyData.DataType.Text; // Data como texto, pois não será usada em cálculos numéricos
                drenagemD18.DefaultData = " - ";
                dr.Definitions.Add(drenagemD18);

                // D19: Declividade
                PropertyDefinition drenagemD19 = new PropertyDefinition();
                drenagemD19.SetToStandard(db);
                drenagemD19.SubSetDatabaseDefaults(db);
                drenagemD19.Name = "SLOPE";
                drenagemD19.Description = "D19 - Declividade";
                drenagemD19.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                drenagemD19.DefaultData = 0.0;
                dr.Definitions.Add(drenagemD19);

                // D20: Diâmetro externo do tubo
                PropertyDefinition drenagemD20 = new PropertyDefinition();
                drenagemD20.SetToStandard(db);
                drenagemD20.SubSetDatabaseDefaults(db);
                drenagemD20.Name = "PIPE_EXTERNAL_DIAMETER";
                drenagemD20.Description = "D20 - Diâmetro externo do tubo";
                drenagemD20.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                drenagemD20.DefaultData = 0.0;
                dr.Definitions.Add(drenagemD20);

                // D21: Diâmetro interno do tubo
                PropertyDefinition drenagemD21 = new PropertyDefinition();
                drenagemD21.SetToStandard(db);
                drenagemD21.SubSetDatabaseDefaults(db);
                drenagemD21.Name = "PIPE_INTERNAL_DIAMETER";
                drenagemD21.Description = "D21 - Diâmetro interno do tubo";
                drenagemD21.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                drenagemD21.DefaultData = 0.0;
                dr.Definitions.Add(drenagemD21);

                // D22: Tipo de berço
                PropertyDefinition drenagemD22 = new PropertyDefinition();
                drenagemD22.SetToStandard(db);
                drenagemD22.SubSetDatabaseDefaults(db);
                drenagemD22.Name = "BEDDING_TYPE";
                drenagemD22.Description = "D22 - Tipo de berço";
                drenagemD22.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                drenagemD22.DefaultData = " - ";
                dr.Definitions.Add(drenagemD22);

                // D23: Tipo do tubo
                PropertyDefinition drenagemD23 = new PropertyDefinition();
                drenagemD23.SetToStandard(db);
                drenagemD23.SubSetDatabaseDefaults(db);
                drenagemD23.Name = "PIPE_TYPE";
                drenagemD23.Description = "D23 - Tipo do tubo";
                drenagemD23.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                drenagemD23.DefaultData = " - ";
                dr.Definitions.Add(drenagemD23);

                // D24: Uso da estrutura
                PropertyDefinition drenagemD24 = new PropertyDefinition();
                drenagemD24.SetToStandard(db);
                drenagemD24.SubSetDatabaseDefaults(db);
                drenagemD24.Name = "STRUCTURE_USE"; // Note o potencial de duplicidade de 'Name' com D27 e D36
                drenagemD24.Description = "D24 - Uso da estrutura";
                drenagemD24.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                drenagemD24.DefaultData = " - ";
                dr.Definitions.Add(drenagemD24);

                // D25: Data de execução (duplicado D18)
                PropertyDefinition drenagemD25 = new PropertyDefinition();
                drenagemD25.SetToStandard(db);
                drenagemD25.SubSetDatabaseDefaults(db);
                drenagemD25.Name = "EXECUTION_DATE"; // Potencial duplicidade de 'Name'
                drenagemD25.Description = "D25 - Data de execução";
                drenagemD25.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                drenagemD25.DefaultData = " - ";
                dr.Definitions.Add(drenagemD25);



                // D26: Localização - Det.: Notação XXX+XXX
                PropertyDefinition drenagemD26 = new PropertyDefinition();
                drenagemD26.SetToStandard(db);
                drenagemD26.SubSetDatabaseDefaults(db);
                drenagemD26.Name = "D26 - Localização - Det.: Notação XXX+XXX";
                drenagemD26.Description = "D26 - Localização - Det.: Notação XXX+XXX";
                drenagemD26.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                drenagemD26.DefaultData = " - ";
                dr.Definitions.Add(drenagemD26);

                // D27: Uso da estrutura (duplicado D24)
                PropertyDefinition drenagemD27 = new PropertyDefinition();
                drenagemD27.SetToStandard(db);
                drenagemD27.SubSetDatabaseDefaults(db);
                drenagemD27.Name = "D27 - Uso da estrutura";
                drenagemD27.Description = "D27 - Uso da estrutura";
                drenagemD27.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                drenagemD27.DefaultData = " - ";
                dr.Definitions.Add(drenagemD27);

                // D28: Cota de jusante (duplicado D16)
                PropertyDefinition drenagemD28 = new PropertyDefinition();
                drenagemD28.SetToStandard(db);
                drenagemD28.SubSetDatabaseDefaults(db);
                drenagemD28.Name = "D28 - Cota de jusante";
                drenagemD28.Description = "D28 - Cota de jusante";
                drenagemD28.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                drenagemD28.DefaultData = 0.0;
                dr.Definitions.Add(drenagemD28);

                // D29: Cota de montante (duplicado D17)
                PropertyDefinition drenagemD29 = new PropertyDefinition();
                drenagemD29.SetToStandard(db);
                drenagemD29.SubSetDatabaseDefaults(db);
                drenagemD29.Name = "D29 - Cota de montante";
                drenagemD29.Description = "D29 - Cota de montante";
                drenagemD29.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                drenagemD29.DefaultData = 0.0;
                dr.Definitions.Add(drenagemD29);

                // D30: Data de execução (duplicado D18, D25)
                PropertyDefinition drenagemD30 = new PropertyDefinition();
                drenagemD30.SetToStandard(db);
                drenagemD30.SubSetDatabaseDefaults(db);
                drenagemD30.Name = "D30 - Data de execução";
                drenagemD30.Description = "D30 - Data de execução";
                drenagemD30.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                drenagemD30.DefaultData = " - ";
                dr.Definitions.Add(drenagemD30);

                // D31: Extensão
                PropertyDefinition drenagemD31 = new PropertyDefinition();
                drenagemD31.SetToStandard(db);
                drenagemD31.SubSetDatabaseDefaults(db);
                drenagemD31.Name = "D31 - Extensão";
                drenagemD31.Description = "D31 - Extensão";
                drenagemD31.DataType = Autodesk.Aec.PropertyData.DataType.Text; // Assumido como texto descritivo
                drenagemD31.DefaultData = " - ";
                dr.Definitions.Add(drenagemD31);

                // D32: Localização final - Det.: Notação XXX+XXX
                PropertyDefinition drenagemD32 = new PropertyDefinition();
                drenagemD32.SetToStandard(db);
                drenagemD32.SubSetDatabaseDefaults(db);
                drenagemD32.Name = "D32 - Localização final - Det.: Notação XXX+XXX";
                drenagemD32.Description = "D32 - Localização final - Det.: Notação XXX+XXX";
                drenagemD32.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                drenagemD32.DefaultData = " - ";
                dr.Definitions.Add(drenagemD32);

                // D33: Localização início - Det.: Notação XXX+XXX
                PropertyDefinition drenagemD33 = new PropertyDefinition();
                drenagemD33.SetToStandard(db);
                drenagemD33.SubSetDatabaseDefaults(db);
                drenagemD33.Name = "D33 - Localização início - Det.: Notação XXX+XXX";
                drenagemD33.Description = "D33 - Localização início - Det.: Notação XXX+XXX";
                drenagemD33.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                drenagemD33.DefaultData = " - ";
                dr.Definitions.Add(drenagemD33);

                // D34: Material (duplicado D6)
                PropertyDefinition drenagemD34 = new PropertyDefinition();
                drenagemD34.SetToStandard(db);
                drenagemD34.SubSetDatabaseDefaults(db);
                drenagemD34.Name = "D34 - Material";
                drenagemD34.Description = "D34 - Material";
                drenagemD34.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                drenagemD34.DefaultData = " - ";
                dr.Definitions.Add(drenagemD34);

                // D35: Projeto-tipo
                PropertyDefinition drenagemD35 = new PropertyDefinition();
                drenagemD35.SetToStandard(db);
                drenagemD35.SubSetDatabaseDefaults(db);
                drenagemD35.Name = "D35 - Projeto-tipo";
                drenagemD35.Description = "D35 - Projeto-tipo";
                drenagemD35.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                drenagemD35.DefaultData = " - ";
                dr.Definitions.Add(drenagemD35);

                // D36: Uso da estrutura (duplicado D24, D27)
                PropertyDefinition drenagemD36 = new PropertyDefinition();
                drenagemD36.SetToStandard(db);
                drenagemD36.SubSetDatabaseDefaults(db);
                drenagemD36.Name = "D36 - Uso da estrutura";
                drenagemD36.Description = "D36 - Uso da estrutura";
                drenagemD36.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                drenagemD36.DefaultData = " - ";
                dr.Definitions.Add(drenagemD36);

                // D37: Volume
                PropertyDefinition drenagemD37 = new PropertyDefinition();
                drenagemD37.SetToStandard(db);
                drenagemD37.SubSetDatabaseDefaults(db);
                drenagemD37.Name = "D37 - Volume";
                drenagemD37.Description = "D37 - Volume";
                drenagemD37.DataType = Autodesk.Aec.PropertyData.DataType.Real; // Assumido como valor numérico
                drenagemD37.DefaultData = 0.0;
                dr.Definitions.Add(drenagemD37);

                dictionary.AddNewRecord("C5 - DRENAGEM", dr); // Adicionando o property set ao dicionário  

                tr.AddNewlyCreatedDBObject(dr, true); // Adicionando o property set à transação


                // PropertySetDefinition para FERROVIA
                PropertySetDefinition fe = new PropertySetDefinition();
                fe.SetToStandard(db);
                fe.SubSetDatabaseDefaults(db);
                fe.AppliesToAll = true;
                fe.AlternateName = "Integridade no Preenchimento das Informações";
                fe.Description = "Integridade no Preenchimento das Informações (Atributos Minimos)";

                


                // D1: Tipo
                PropertyDefinition ferroviaD1 = new PropertyDefinition();
                ferroviaD1.SetToStandard(db);
                ferroviaD1.SubSetDatabaseDefaults(db);
                ferroviaD1.Name = "D1 - Tipo";
                ferroviaD1.Description = "D1 - Tipo";
                ferroviaD1.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                ferroviaD1.DefaultData = " - ";
                fe.Definitions.Add(ferroviaD1);

                // D2: Objeto
                PropertyDefinition ferroviaD2 = new PropertyDefinition();
                ferroviaD2.SetToStandard(db);
                ferroviaD2.SubSetDatabaseDefaults(db);
                ferroviaD2.Name = "D2 - Objeto";
                ferroviaD2.Description = "D2 - Objeto";
                ferroviaD2.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                ferroviaD2.DefaultData = " - ";
                fe.Definitions.Add(ferroviaD2);

                // D3: Código SICRO
                PropertyDefinition ferroviaD3 = new PropertyDefinition();
                ferroviaD3.SetToStandard(db);
                ferroviaD3.SubSetDatabaseDefaults(db);
                ferroviaD3.Name = "D3 - Código SICRO";
                ferroviaD3.Description = "D3 - Código SICRO";
                ferroviaD3.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                ferroviaD3.DefaultData = " - ";
                fe.Definitions.Add(ferroviaD3);

                // D4: Planejamento
                PropertyDefinition ferroviaD4 = new PropertyDefinition();
                ferroviaD4.SetToStandard(db);
                ferroviaD4.SubSetDatabaseDefaults(db);
                ferroviaD4.Name = "D4 - Planejamento";
                ferroviaD4.Description = "D4 - Planejamento";
                ferroviaD4.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                ferroviaD4.DefaultData = " - ";
                fe.Definitions.Add(ferroviaD4);

                // D5: Material
                PropertyDefinition ferroviaD5 = new PropertyDefinition();
                ferroviaD5.SetToStandard(db);
                ferroviaD5.SubSetDatabaseDefaults(db);
                ferroviaD5.Name = "D5 - Material";
                ferroviaD5.Description = "D5 - Material";
                ferroviaD5.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                ferroviaD5.DefaultData = " - ";
                fe.Definitions.Add(ferroviaD5);

                // D6: Largura (m)
                PropertyDefinition ferroviaD6 = new PropertyDefinition();
                ferroviaD6.SetToStandard(db);
                ferroviaD6.SubSetDatabaseDefaults(db);
                ferroviaD6.Name = "D6 - Largura (m)";
                ferroviaD6.Description = "D6 - Largura (m)";
                ferroviaD6.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                ferroviaD6.DefaultData = 0.0;
                fe.Definitions.Add(ferroviaD6);

                // D7: Área (m²)
                PropertyDefinition ferroviaD7 = new PropertyDefinition();
                ferroviaD7.SetToStandard(db);
                ferroviaD7.SubSetDatabaseDefaults(db);
                ferroviaD7.Name = "D7 - Área (m²)";
                ferroviaD7.Description = "D7 - Área (m²)";
                ferroviaD7.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                ferroviaD7.DefaultData = 0.0;
                fe.Definitions.Add(ferroviaD7);

                // D8: Volume (m³)
                PropertyDefinition ferroviaD8 = new PropertyDefinition();
                ferroviaD8.SetToStandard(db);
                ferroviaD8.SubSetDatabaseDefaults(db);
                ferroviaD8.Name = "D8 - Volume (m³)";
                ferroviaD8.Description = "D8 - Volume (m³)";
                ferroviaD8.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                ferroviaD8.DefaultData = 0.0;
                fe.Definitions.Add(ferroviaD8);

                // D9: Estaca/km inicial
                PropertyDefinition ferroviaD9 = new PropertyDefinition();
                ferroviaD9.SetToStandard(db);
                ferroviaD9.SubSetDatabaseDefaults(db);
                ferroviaD9.Name = "D9 - Estaca/km inicial";
                ferroviaD9.Description = "D9 - Estaca/km inicial";
                ferroviaD9.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                ferroviaD9.DefaultData = 0.0;
                fe.Definitions.Add(ferroviaD9);

                // D10: Estaca/km final
                PropertyDefinition ferroviaD10 = new PropertyDefinition();
                ferroviaD10.SetToStandard(db);
                ferroviaD10.SubSetDatabaseDefaults(db);
                ferroviaD10.Name = "D10 - Estaca/km final";
                ferroviaD10.Description = "D10 - Estaca/km final";
                ferroviaD10.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                ferroviaD10.DefaultData = 0.0;
                fe.Definitions.Add(ferroviaD10);

                // D11: Estaca/km eixo
                PropertyDefinition ferroviaD11 = new PropertyDefinition();
                ferroviaD11.SetToStandard(db);
                ferroviaD11.SubSetDatabaseDefaults(db);
                ferroviaD11.Name = "D11 - Estaca/km eixo";
                ferroviaD11.Description = "D11 - Estaca/km eixo";
                ferroviaD11.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                ferroviaD11.DefaultData = 0.0;
                fe.Definitions.Add(ferroviaD11);

                // D12: Comprimento (m)
                PropertyDefinition ferroviaD12 = new PropertyDefinition();
                ferroviaD12.SetToStandard(db);
                ferroviaD12.SubSetDatabaseDefaults(db);
                ferroviaD12.Name = "D12 - Comprimento (m)";
                ferroviaD12.Description = "D12 - Comprimento (m)";
                ferroviaD12.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                ferroviaD12.DefaultData = 0.0;
                fe.Definitions.Add(ferroviaD12);


                dictionary.AddNewRecord("C8 - FERROVIA", fe); // Adicionando o property set ao dicionário              
                tr.AddNewlyCreatedDBObject(fe, true); // Adicionando o property set à transação


                PropertySetDefinition ge = new PropertySetDefinition();
                ge.SetToStandard(db);
                ge.SubSetDatabaseDefaults(db);
                ge.AppliesToAll = true;
                ge.AlternateName = "Integridade no Preenchimento das Informações";
                ge.Description = "Integridade no Preenchimento das Informações (Atributos Minimos)";

                // D1: Tipo
                PropertyDefinition geometriaD1 = new PropertyDefinition();
                geometriaD1.SetToStandard(db);
                geometriaD1.SubSetDatabaseDefaults(db);
                geometriaD1.Name = "D1 - Tipo";
                geometriaD1.Description = "D1 - Tipo";
                geometriaD1.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                geometriaD1.DefaultData = " - ";
                ge.Definitions.Add(geometriaD1);

                // D2: Objeto
                PropertyDefinition geometriaD2 = new PropertyDefinition();
                geometriaD2.SetToStandard(db);
                geometriaD2.SubSetDatabaseDefaults(db);
                geometriaD2.Name = "D2 - Objeto";
                geometriaD2.Description = "D2 - Objeto";
                geometriaD2.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                geometriaD2.DefaultData = " - ";
                ge.Definitions.Add(geometriaD2);

                // D3: Planejamento
                PropertyDefinition geometriaD3 = new PropertyDefinition();
                geometriaD3.SetToStandard(db);
                geometriaD3.SubSetDatabaseDefaults(db);
                geometriaD3.Name = "D3 - Planejamento";
                geometriaD3.Description = "D3 - Planejamento";
                geometriaD3.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                geometriaD3.DefaultData = " - ";
                ge.Definitions.Add(geometriaD3);

                // D4: Comprimento (m)
                PropertyDefinition geometriaD4 = new PropertyDefinition();
                geometriaD4.SetToStandard(db);
                geometriaD4.SubSetDatabaseDefaults(db);
                geometriaD4.Name = "D4 - Comprimento (m)";
                geometriaD4.Description = "D4 - Comprimento (m)";
                geometriaD4.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                geometriaD4.DefaultData = 0.0;
                ge.Definitions.Add(geometriaD4);

                // D5: Estaca/km inicial
                PropertyDefinition geometriaD5 = new PropertyDefinition();
                geometriaD5.SetToStandard(db);
                geometriaD5.SubSetDatabaseDefaults(db);
                geometriaD5.Name = "D5 - Estaca/km inicial";
                geometriaD5.Description = "D5 - Estaca/km inicial";
                geometriaD5.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                geometriaD5.DefaultData = 0.0;
                ge.Definitions.Add(geometriaD5);

                // D6: Estaca/km final
                PropertyDefinition geometriaD6 = new PropertyDefinition();
                geometriaD6.SetToStandard(db);
                geometriaD6.SubSetDatabaseDefaults(db);
                geometriaD6.Name = "D6 - Estaca/km final";
                geometriaD6.Description = "D6 - Estaca/km final";
                geometriaD6.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                geometriaD6.DefaultData = 0.0;
                ge.Definitions.Add(geometriaD6);

                // D7: Estaca/km eixo
                PropertyDefinition geometriaD7 = new PropertyDefinition();
                geometriaD7.SetToStandard(db);
                geometriaD7.SubSetDatabaseDefaults(db);
                geometriaD7.Name = "D7 - Estaca/km eixo";
                geometriaD7.Description = "D7 - Estaca/km eixo";
                geometriaD7.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                geometriaD7.DefaultData = 0.0;
                ge.Definitions.Add(geometriaD7);

                // D8: Cota início/topo
                PropertyDefinition geometriaD8 = new PropertyDefinition();
                geometriaD8.SetToStandard(db);
                geometriaD8.SubSetDatabaseDefaults(db);
                geometriaD8.Name = "D8 - Cota início/topo";
                geometriaD8.Description = "D8 - Cota início/topo";
                geometriaD8.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                geometriaD8.DefaultData = 0.0;
                ge.Definitions.Add(geometriaD8);

                // D9: Cota fim/fundo
                PropertyDefinition geometriaD9 = new PropertyDefinition();
                geometriaD9.SetToStandard(db);
                geometriaD9.SubSetDatabaseDefaults(db);
                geometriaD9.Name = "D9 - Cota fim/fundo";
                geometriaD9.Description = "D9 - Cota fim/fundo";
                geometriaD9.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                geometriaD9.DefaultData = 0.0;
                ge.Definitions.Add(geometriaD9);

                dictionary.AddNewRecord("C9 - GEOMETRIA", ge); // Adicionando o property set ao dicionário              
                tr.AddNewlyCreatedDBObject(ge, true); // Adicionando o property set à transação


                // PropertySetDefinition para PAVIMENTAÇÃO
                PropertySetDefinition pv = new PropertySetDefinition();
                pv.SetToStandard(db);
                pv.SubSetDatabaseDefaults(db);
                pv.AppliesToAll = true;
                pv.AlternateName = "Integridade no Preenchimento das Informações";
                pv.Description = "Integridade no Preenchimento das Informações (Atributos Minimos)";





                // D1: Tipo
                PropertyDefinition pavimentacaoD1 = new PropertyDefinition();
                pavimentacaoD1.SetToStandard(db);
                pavimentacaoD1.SubSetDatabaseDefaults(db);
                pavimentacaoD1.Name = "D1 - Tipo";
                pavimentacaoD1.Description = "D1 - Tipo";
                pavimentacaoD1.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                pavimentacaoD1.DefaultData = " - ";
                pv.Definitions.Add(pavimentacaoD1);

                // D2: Objeto
                PropertyDefinition pavimentacaoD2 = new PropertyDefinition();
                pavimentacaoD2.SetToStandard(db);
                pavimentacaoD2.SubSetDatabaseDefaults(db);
                pavimentacaoD2.Name = "D2 - Objeto";
                pavimentacaoD2.Description = "D2 - Objeto";
                pavimentacaoD2.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                pavimentacaoD2.DefaultData = " - ";
                pv.Definitions.Add(pavimentacaoD2);

                // D3: Código SICRO
                PropertyDefinition pavimentacaoD3 = new PropertyDefinition();
                pavimentacaoD3.SetToStandard(db);
                pavimentacaoD3.SubSetDatabaseDefaults(db);
                pavimentacaoD3.Name = "D3 - Código SICRO";
                pavimentacaoD3.Description = "D3 - Código SICRO";
                pavimentacaoD3.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                pavimentacaoD3.DefaultData = " - ";
                pv.Definitions.Add(pavimentacaoD3);

                // D4: Planejamento
                PropertyDefinition pavimentacaoD4 = new PropertyDefinition();
                pavimentacaoD4.SetToStandard(db);
                pavimentacaoD4.SubSetDatabaseDefaults(db);
                pavimentacaoD4.Name = "D4 - Planejamento";
                pavimentacaoD4.Description = "D4 - Planejamento";
                pavimentacaoD4.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                pavimentacaoD4.DefaultData = " - ";
                pv.Definitions.Add(pavimentacaoD4);

                // D5: Material
                PropertyDefinition pavimentacaoD5 = new PropertyDefinition();
                pavimentacaoD5.SetToStandard(db);
                pavimentacaoD5.SubSetDatabaseDefaults(db);
                pavimentacaoD5.Name = "D5 - Material";
                pavimentacaoD5.Description = "D5 - Material";
                pavimentacaoD5.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                pavimentacaoD5.DefaultData = " - ";
                pv.Definitions.Add(pavimentacaoD5);

                // D6: Comprimento (m)
                PropertyDefinition pavimentacaoD6 = new PropertyDefinition();
                pavimentacaoD6.SetToStandard(db);
                pavimentacaoD6.SubSetDatabaseDefaults(db);
                pavimentacaoD6.Name = "D6 - Comprimento (m)";
                pavimentacaoD6.Description = "D6 - Comprimento (m)";
                pavimentacaoD6.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                pavimentacaoD6.DefaultData = 0.0;
                pv.Definitions.Add(pavimentacaoD6);

                // D7: Largura (m)
                PropertyDefinition pavimentacaoD7 = new PropertyDefinition();
                pavimentacaoD7.SetToStandard(db);
                pavimentacaoD7.SubSetDatabaseDefaults(db);
                pavimentacaoD7.Name = "D7 - Largura (m)";
                pavimentacaoD7.Description = "D7 - Largura (m)";
                pavimentacaoD7.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                pavimentacaoD7.DefaultData = 0.0;
                pv.Definitions.Add(pavimentacaoD7);

                // D8: Altura (m)
                PropertyDefinition pavimentacaoD8 = new PropertyDefinition();
                pavimentacaoD8.SetToStandard(db);
                pavimentacaoD8.SubSetDatabaseDefaults(db);
                pavimentacaoD8.Name = "D8 - Altura (m)";
                pavimentacaoD8.Description = "D8 - Altura (m)";
                pavimentacaoD8.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                pavimentacaoD8.DefaultData = 0.0;
                pv.Definitions.Add(pavimentacaoD8);

                // D9: Área (m²)
                PropertyDefinition pavimentacaoD9 = new PropertyDefinition();
                pavimentacaoD9.SetToStandard(db);
                pavimentacaoD9.SubSetDatabaseDefaults(db);
                pavimentacaoD9.Name = "D9 - Área (m²)";
                pavimentacaoD9.Description = "D9 - Área (m²)";
                pavimentacaoD9.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                pavimentacaoD9.DefaultData = 0.0;
                pv.Definitions.Add(pavimentacaoD9);

                // D10: Volume (m³)
                PropertyDefinition pavimentacaoD10 = new PropertyDefinition();
                pavimentacaoD10.SetToStandard(db);
                pavimentacaoD10.SubSetDatabaseDefaults(db);
                pavimentacaoD10.Name = "D10 - Volume (m³)";
                pavimentacaoD10.Description = "D10 - Volume (m³)";
                pavimentacaoD10.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                pavimentacaoD10.DefaultData = 0.0;
                pv.Definitions.Add(pavimentacaoD10);

                // D11: Estaca/km inicial
                PropertyDefinition pavimentacaoD11 = new PropertyDefinition();
                pavimentacaoD11.SetToStandard(db);
                pavimentacaoD11.SubSetDatabaseDefaults(db);
                pavimentacaoD11.Name = "D11 - Estaca/km inicial";
                pavimentacaoD11.Description = "D11 - Estaca/km inicial";
                pavimentacaoD11.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                pavimentacaoD11.DefaultData = 0.0;
                pv.Definitions.Add(pavimentacaoD11);

                // D12: Estaca/km final
                PropertyDefinition pavimentacaoD12 = new PropertyDefinition();
                pavimentacaoD12.SetToStandard(db);
                pavimentacaoD12.SubSetDatabaseDefaults(db);
                pavimentacaoD12.Name = "D12 - Estaca/km final";
                pavimentacaoD12.Description = "D12 - Estaca/km final";
                pavimentacaoD12.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                pavimentacaoD12.DefaultData = 0.0;
                pv.Definitions.Add(pavimentacaoD12);

                // D13: Cota início/topo
                PropertyDefinition pavimentacaoD13 = new PropertyDefinition();
                pavimentacaoD13.SetToStandard(db);
                pavimentacaoD13.SubSetDatabaseDefaults(db);
                pavimentacaoD13.Name = "D13 - Cota início/topo";
                pavimentacaoD13.Description = "D13 - Cota início/topo";
                pavimentacaoD13.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                pavimentacaoD13.DefaultData = 0.0;
                pv.Definitions.Add(pavimentacaoD13);

                // D14: Cota fim/fundo
                PropertyDefinition pavimentacaoD14 = new PropertyDefinition();
                pavimentacaoD14.SetToStandard(db);
                pavimentacaoD14.SubSetDatabaseDefaults(db);
                pavimentacaoD14.Name = "D14 - Cota fim/fundo";
                pavimentacaoD14.Description = "D14 - Cota fim/fundo";
                pavimentacaoD14.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                pavimentacaoD14.DefaultData = 0.0;
                pv.Definitions.Add(pavimentacaoD14);


                dictionary.AddNewRecord("C10 - PAVIMENTAÇÃO", pv); // Adicionando o property set ao dicionário              
                tr.AddNewlyCreatedDBObject(pv, true); // Adicionando o property set à transação


                // PropertySetDefinition para SINALIZAÇÃO
                PropertySetDefinition si = new PropertySetDefinition();
                si.SetToStandard(db);
                si.SubSetDatabaseDefaults(db);
                si.AppliesToAll = true;
                si.AlternateName = "Integridade no Preenchimento das Informações";
                si.Description = "Integridade no Preenchimento das Informações (Atributos Minimos)";




                // D1: Tipo
                PropertyDefinition sinalizacaoD1 = new PropertyDefinition();
                sinalizacaoD1.SetToStandard(db);
                sinalizacaoD1.SubSetDatabaseDefaults(db);
                sinalizacaoD1.Name = "D1 - Tipo";
                sinalizacaoD1.Description = "D1 - Tipo";
                sinalizacaoD1.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                sinalizacaoD1.DefaultData = " - ";
                si.Definitions.Add(sinalizacaoD1);

                // D2: Objeto
                PropertyDefinition sinalizacaoD2 = new PropertyDefinition();
                sinalizacaoD2.SetToStandard(db);
                sinalizacaoD2.SubSetDatabaseDefaults(db);
                sinalizacaoD2.Name = "D2 - Objeto";
                sinalizacaoD2.Description = "D2 - Objeto";
                sinalizacaoD2.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                sinalizacaoD2.DefaultData = " - ";
                si.Definitions.Add(sinalizacaoD2);

                // D3: Código SICRO
                PropertyDefinition sinalizacaoD3 = new PropertyDefinition();
                sinalizacaoD3.SetToStandard(db);
                sinalizacaoD3.SubSetDatabaseDefaults(db);
                sinalizacaoD3.Name = "D3 - Código SICRO";
                sinalizacaoD3.Description = "D3 - Código SICRO";
                sinalizacaoD3.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                sinalizacaoD3.DefaultData = " - ";
                si.Definitions.Add(sinalizacaoD3);

                // D4: Planejamento
                PropertyDefinition sinalizacaoD4 = new PropertyDefinition();
                sinalizacaoD4.SetToStandard(db);
                sinalizacaoD4.SubSetDatabaseDefaults(db);
                sinalizacaoD4.Name = "D4 - Planejamento";
                sinalizacaoD4.Description = "D4 - Planejamento";
                sinalizacaoD4.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                sinalizacaoD4.DefaultData = " - ";
                si.Definitions.Add(sinalizacaoD4);

                // D5: Material
                PropertyDefinition sinalizacaoD5 = new PropertyDefinition();
                sinalizacaoD5.SetToStandard(db);
                sinalizacaoD5.SubSetDatabaseDefaults(db);
                sinalizacaoD5.Name = "D5 - Material";
                sinalizacaoD5.Description = "D5 - Material";
                sinalizacaoD5.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                sinalizacaoD5.DefaultData = " - ";
                si.Definitions.Add(sinalizacaoD5);

                // D6: Comprimento (m)
                PropertyDefinition sinalizacaoD6 = new PropertyDefinition();
                sinalizacaoD6.SetToStandard(db);
                sinalizacaoD6.SubSetDatabaseDefaults(db);
                sinalizacaoD6.Name = "D6 - Comprimento (m)";
                sinalizacaoD6.Description = "D6 - Comprimento (m)";
                sinalizacaoD6.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                sinalizacaoD6.DefaultData = 0.0;
                si.Definitions.Add(sinalizacaoD6);

                // D7: Volume (m³)
                PropertyDefinition sinalizacaoD7 = new PropertyDefinition();
                sinalizacaoD7.SetToStandard(db);
                sinalizacaoD7.SubSetDatabaseDefaults(db);
                sinalizacaoD7.Name = "D7 - Volume (m³)";
                sinalizacaoD7.Description = "D7 - Volume (m³)";
                sinalizacaoD7.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                sinalizacaoD7.DefaultData = 0.0;
                si.Definitions.Add(sinalizacaoD7);

                // D8: Estaca/km inicial
                PropertyDefinition sinalizacaoD8 = new PropertyDefinition();
                sinalizacaoD8.SetToStandard(db);
                sinalizacaoD8.SubSetDatabaseDefaults(db);
                sinalizacaoD8.Name = "D8 - Estaca/km inicial";
                sinalizacaoD8.Description = "D8 - Estaca/km inicial";
                sinalizacaoD8.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                sinalizacaoD8.DefaultData = 0.0;
                si.Definitions.Add(sinalizacaoD8);

                // D9: Estaca/km final
                PropertyDefinition sinalizacaoD9 = new PropertyDefinition();
                sinalizacaoD9.SetToStandard(db);
                sinalizacaoD9.SubSetDatabaseDefaults(db);
                sinalizacaoD9.Name = "D9 - Estaca/km final";
                sinalizacaoD9.Description = "D9 - Estaca/km final";
                sinalizacaoD9.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                sinalizacaoD9.DefaultData = 0.0;
                si.Definitions.Add(sinalizacaoD9);

                // D10: Área (m²)
                PropertyDefinition sinalizacaoD10 = new PropertyDefinition();
                sinalizacaoD10.SetToStandard(db);
                sinalizacaoD10.SubSetDatabaseDefaults(db);
                sinalizacaoD10.Name = "D10 - Área (m²)";
                sinalizacaoD10.Description = "D10 - Área (m²)";
                sinalizacaoD10.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                sinalizacaoD10.DefaultData = 0.0;
                si.Definitions.Add(sinalizacaoD10);

                // D11: Estaca/km eixo
                PropertyDefinition sinalizacaoD11 = new PropertyDefinition();
                sinalizacaoD11.SetToStandard(db);
                sinalizacaoD11.SubSetDatabaseDefaults(db);
                sinalizacaoD11.Name = "D11 - Estaca/km eixo";
                sinalizacaoD11.Description = "D11 - Estaca/km eixo";
                sinalizacaoD11.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                sinalizacaoD11.DefaultData = 0.0;
                si.Definitions.Add(sinalizacaoD11);

                // D12: Largura (m)
                PropertyDefinition sinalizacaoD12 = new PropertyDefinition();
                sinalizacaoD12.SetToStandard(db);
                sinalizacaoD12.SubSetDatabaseDefaults(db);
                sinalizacaoD12.Name = "D12 - Largura (m)";
                sinalizacaoD12.Description = "D12 - Largura (m)";
                sinalizacaoD12.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                sinalizacaoD12.DefaultData = 0.0;
                si.Definitions.Add(sinalizacaoD12);

                // D13: Altura (m)
                PropertyDefinition sinalizacaoD13 = new PropertyDefinition();
                sinalizacaoD13.SetToStandard(db);
                sinalizacaoD13.SubSetDatabaseDefaults(db);
                sinalizacaoD13.Name = "D13 - Altura (m)";
                sinalizacaoD13.Description = "D13 - Altura (m)";
                sinalizacaoD13.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                sinalizacaoD13.DefaultData = 0.0;
                si.Definitions.Add(sinalizacaoD13);

                // D14: Cota início/topo
                PropertyDefinition sinalizacaoD14 = new PropertyDefinition();
                sinalizacaoD14.SetToStandard(db);
                sinalizacaoD14.SubSetDatabaseDefaults(db);
                sinalizacaoD14.Name = "D14 - Cota início/topo";
                sinalizacaoD14.Description = "D14 - Cota início/topo";
                sinalizacaoD14.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                sinalizacaoD14.DefaultData = 0.0;
                si.Definitions.Add(sinalizacaoD14);

                // D15: Cota fim/fundo
                PropertyDefinition sinalizacaoD15 = new PropertyDefinition();
                sinalizacaoD15.SetToStandard(db);
                sinalizacaoD15.SubSetDatabaseDefaults(db);
                sinalizacaoD15.Name = "D15 - Cota fim/fundo";
                sinalizacaoD15.Description = "D15 - Cota fim/fundo";
                sinalizacaoD15.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                sinalizacaoD15.DefaultData = 0.0;
                si.Definitions.Add(sinalizacaoD15);

                // D16: Diâmetro (m)
                PropertyDefinition sinalizacaoD16 = new PropertyDefinition();
                sinalizacaoD16.SetToStandard(db);
                sinalizacaoD16.SubSetDatabaseDefaults(db);
                sinalizacaoD16.Name = "D16 - Diâmetro (m)";
                sinalizacaoD16.Description = "D16 - Diâmetro (m)";
                sinalizacaoD16.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                sinalizacaoD16.DefaultData = 0.0;
                si.Definitions.Add(sinalizacaoD16);


                dictionary.AddNewRecord("C11 - SINALIZAÇÃO", si); // Adicionando o property set ao dicionário              
                tr.AddNewlyCreatedDBObject(si, true); // Adicionando o property set à transação


                // PropertySetDefinition para TERRAPLENAGEM
                PropertySetDefinition te = new PropertySetDefinition();
                te.SetToStandard(db);
                te.SubSetDatabaseDefaults(db);
                te.AppliesToAll = true;
                te.AlternateName = "Integridade no Preenchimento das Informações";
                te.Description = "Integridade no Preenchimento das Informações (Atributos Minimos)";

                



                // D1: Tipo
                PropertyDefinition terraplenagemD1 = new PropertyDefinition();
                terraplenagemD1.SetToStandard(db);
                terraplenagemD1.SubSetDatabaseDefaults(db);
                terraplenagemD1.Name = "D1 - Tipo";
                terraplenagemD1.Description = "D1 - Tipo";
                terraplenagemD1.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                terraplenagemD1.DefaultData = " - ";
                te.Definitions.Add(terraplenagemD1);

                // D2: Objeto
                PropertyDefinition terraplenagemD2 = new PropertyDefinition();
                terraplenagemD2.SetToStandard(db);
                terraplenagemD2.SubSetDatabaseDefaults(db);
                terraplenagemD2.Name = "D2 - Objeto";
                terraplenagemD2.Description = "D2 - Objeto";
                terraplenagemD2.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                terraplenagemD2.DefaultData = " - ";
                te.Definitions.Add(terraplenagemD2);

                // D3: Código SICRO
                PropertyDefinition terraplenagemD3 = new PropertyDefinition();
                terraplenagemD3.SetToStandard(db);
                terraplenagemD3.SubSetDatabaseDefaults(db);
                terraplenagemD3.Name = "D3 - Código SICRO";
                terraplenagemD3.Description = "D3 - Código SICRO";
                terraplenagemD3.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                terraplenagemD3.DefaultData = " - ";
                te.Definitions.Add(terraplenagemD3);

                // D4: Planejamento
                PropertyDefinition terraplenagemD4 = new PropertyDefinition();
                terraplenagemD4.SetToStandard(db);
                terraplenagemD4.SubSetDatabaseDefaults(db);
                terraplenagemD4.Name = "D4 - Planejamento";
                terraplenagemD4.Description = "D4 - Planejamento";
                terraplenagemD4.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                terraplenagemD4.DefaultData = " - ";
                te.Definitions.Add(terraplenagemD4);

                // D5: Material
                PropertyDefinition terraplenagemD5 = new PropertyDefinition();
                terraplenagemD5.SetToStandard(db);
                terraplenagemD5.SubSetDatabaseDefaults(db);
                terraplenagemD5.Name = "D5 - Material";
                terraplenagemD5.Description = "D5 - Material";
                terraplenagemD5.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                terraplenagemD5.DefaultData = " - ";
                te.Definitions.Add(terraplenagemD5);

                // D6: Destino (PDE NOME, PDER NOME, CAVA NOME, maciço NOME, temporário)
                PropertyDefinition terraplenagemD6 = new PropertyDefinition();
                terraplenagemD6.SetToStandard(db);
                terraplenagemD6.SubSetDatabaseDefaults(db);
                terraplenagemD6.Name = "D6 - Destino (PDE NOME, PDER NOME, CAVA NOME, maciço NOME, temporário)";
                terraplenagemD6.Description = "D6 - Destino (PDE NOME, PDER NOME, CAVA NOME, maciço NOME, temporário)";
                terraplenagemD6.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                terraplenagemD6.DefaultData = " - ";
                te.Definitions.Add(terraplenagemD6);

                // D7: Layer conforme material
                PropertyDefinition terraplenagemD7 = new PropertyDefinition();
                terraplenagemD7.SetToStandard(db);
                terraplenagemD7.SubSetDatabaseDefaults(db);
                terraplenagemD7.Name = "D7 - Layer conforme material";
                terraplenagemD7.Description = "D7 - Layer conforme material";
                terraplenagemD7.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                terraplenagemD7.DefaultData = " - ";
                te.Definitions.Add(terraplenagemD7);

                // D8: Origem (sedimento NOME, rejeito NOME, nomes de empréstimo, maciço NOME, levantar)
                PropertyDefinition terraplenagemD8 = new PropertyDefinition();
                terraplenagemD8.SetToStandard(db);
                terraplenagemD8.SubSetDatabaseDefaults(db);
                terraplenagemD8.Name = "D8 - Origem (sedimento NOME, rejeito NOME, nomes de empréstimo, maciço NOME, levantar)";
                terraplenagemD8.Description = "D8 - Origem (sedimento NOME, rejeito NOME, nomes de empréstimo, maciço NOME, levantar)";
                terraplenagemD8.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                terraplenagemD8.DefaultData = " - ";
                te.Definitions.Add(terraplenagemD8);

                // D9: Uso
                PropertyDefinition terraplenagemD9 = new PropertyDefinition();
                terraplenagemD9.SetToStandard(db);
                terraplenagemD9.SubSetDatabaseDefaults(db);
                terraplenagemD9.Name = "D9 - Uso";
                terraplenagemD9.Description = "D9 - Uso";
                terraplenagemD9.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                terraplenagemD9.DefaultData = " - ";
                te.Definitions.Add(terraplenagemD9);

                // D10: Comprimento (m)
                PropertyDefinition terraplenagemD10 = new PropertyDefinition();
                terraplenagemD10.SetToStandard(db);
                terraplenagemD10.SubSetDatabaseDefaults(db);
                terraplenagemD10.Name = "D10 - Comprimento (m)";
                terraplenagemD10.Description = "D10 - Comprimento (m)";
                terraplenagemD10.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                terraplenagemD10.DefaultData = 0.0;
                te.Definitions.Add(terraplenagemD10);

                // D11: Largura (m)
                PropertyDefinition terraplenagemD11 = new PropertyDefinition();
                terraplenagemD11.SetToStandard(db);
                terraplenagemD11.SubSetDatabaseDefaults(db);
                terraplenagemD11.Name = "D11 - Largura (m)";
                terraplenagemD11.Description = "D11 - Largura (m)";
                terraplenagemD11.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                terraplenagemD11.DefaultData = 0.0;
                te.Definitions.Add(terraplenagemD11);

                // D12: Altura (m)
                PropertyDefinition terraplenagemD12 = new PropertyDefinition();
                terraplenagemD12.SetToStandard(db);
                terraplenagemD12.SubSetDatabaseDefaults(db);
                terraplenagemD12.Name = "D12 - Altura (m)";
                terraplenagemD12.Description = "D12 - Altura (m)";
                terraplenagemD12.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                terraplenagemD12.DefaultData = 0.0;
                te.Definitions.Add(terraplenagemD12);

                // D13: Área (m²)
                PropertyDefinition terraplenagemD13 = new PropertyDefinition();
                terraplenagemD13.SetToStandard(db);
                terraplenagemD13.SubSetDatabaseDefaults(db);
                terraplenagemD13.Name = "D13 - Área (m²)";
                terraplenagemD13.Description = "D13 - Área (m²)";
                terraplenagemD13.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                terraplenagemD13.DefaultData = 0.0;
                te.Definitions.Add(terraplenagemD13);

                // D14: Volume (m³)
                PropertyDefinition terraplenagemD14 = new PropertyDefinition();
                terraplenagemD14.SetToStandard(db);
                terraplenagemD14.SubSetDatabaseDefaults(db);
                terraplenagemD14.Name = "D14 - Volume (m³)";
                terraplenagemD14.Description = "D14 - Volume (m³)";
                terraplenagemD14.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                terraplenagemD14.DefaultData = 0.0;
                te.Definitions.Add(terraplenagemD14);

                // D15: Estaca/km inicial
                PropertyDefinition terraplenagemD15 = new PropertyDefinition();
                terraplenagemD15.SetToStandard(db);
                terraplenagemD15.SubSetDatabaseDefaults(db);
                terraplenagemD15.Name = "D15 - Estaca/km inicial";
                terraplenagemD15.Description = "D15 - Estaca/km inicial";
                terraplenagemD15.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                terraplenagemD15.DefaultData = 0.0;
                te.Definitions.Add(terraplenagemD15);

                // D16: Estaca/km final
                PropertyDefinition terraplenagemD16 = new PropertyDefinition();
                terraplenagemD16.SetToStandard(db);
                terraplenagemD16.SubSetDatabaseDefaults(db);
                terraplenagemD16.Name = "D16 - Estaca/km final";
                terraplenagemD16.Description = "D16 - Estaca/km final";
                terraplenagemD16.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                terraplenagemD16.DefaultData = 0.0;
                te.Definitions.Add(terraplenagemD16);

                // D17: Cota início/topo
                PropertyDefinition terraplenagemD17 = new PropertyDefinition();
                terraplenagemD17.SetToStandard(db);
                terraplenagemD17.SubSetDatabaseDefaults(db);
                terraplenagemD17.Name = "D17 - Cota início/topo";
                terraplenagemD17.Description = "D17 - Cota início/topo";
                terraplenagemD17.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                terraplenagemD17.DefaultData = 0.0;
                te.Definitions.Add(terraplenagemD17);

                // D18: Cota fim/fundo
                PropertyDefinition terraplenagemD18 = new PropertyDefinition();
                terraplenagemD18.SetToStandard(db);
                terraplenagemD18.SubSetDatabaseDefaults(db);
                terraplenagemD18.Name = "D18 - Cota fim/fundo";
                terraplenagemD18.Description = "D18 - Cota fim/fundo";
                terraplenagemD18.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                terraplenagemD18.DefaultData = 0.0;
                te.Definitions.Add(terraplenagemD18);

                dictionary.AddNewRecord("C12 - TERRAPLENAGEM", te); // Adicionando o property set ao dicionário              
                tr.AddNewlyCreatedDBObject(te, true); // Adicionando o property set à transação

                

                /*/ Nome do property set (ajuste conforme o seu projeto)
                string propSetName = "TESTE";

                // Verificar se o property set existe
                if (dictionary.Has(propSetName, tr))
                {



                    // Obter o property set
                    ObjectId propSetId = dictionary.GetAt(propSetName);
                    PropertySetDefinition propSetDef = (PropertySetDefinition)tr.GetObject(propSetId, OpenMode.ForWrite);
                    
                    

                    foreach (ObjectId id in db.GetCivilAlignmentIds())
                    {
                        // Obter o nome do property set
                        Alignment alignment = (Alignment)tr.GetObject(id, OpenMode.ForRead);
                        if (alignment.Name == "DML - M14")
                        {
                            // Associa o conjunto de propriedades ao objeto                           
                            PropertyDataServices.AddPropertySet(alignment, propSetDef.Id);
                            
                            
                        }


                    }

                   


                    


                    // Iterar sobre os campos do property set
                    foreach (PropertyDefinition propDef1 in propSetDef.Definitions)
                    {
                        // Verificar se o campo é do tipo texto e definir o valor "teste"
                        if (propDef1.Name == "TESTE1")
                        {
                            propDef1.DefaultData = "teste1";
                        }
                        if (propDef1.Name == "TESTE2")
                        {
                            propDef1.DefaultData = "teste2";
                        }
                    }

                    // Salvar as alterações
                    tr.Commit();
                }
                else
                {
                    System.Windows.Forms.MessageBox.Show($"O property set '{propSetName}' não foi encontrado.");
                }


                    PropertySetDefinition ct = new PropertySetDefinition();
                    dr.SetToStandard(db);
                    dr.SubSetDatabaseDefaults(db);
                    dr.AppliesToAll = true;
                    dr.AlternateName = "Dados Geometricos1";
                    dr.Description = "Parametros Geométricos dos Objetos";

                    PropertyDefinition propDef = new PropertyDefinition();
                    propDef.SetToStandard(db);
                    propDef.SubSetDatabaseDefaults(db);
                    propDef.Name = "Comprimento";
                    propDef.Description = "Comprimento do Objeto";
                    propDef.DataType = Autodesk.Aec.PropertyData.DataType.Text; // Definindo o tipo de dado como texto
                    propDef.DefaultData = "0"; // Definindo o valor padrão como "teste"
                    

                foreach (ObjectId id in db.GetCivilAlignmentIds())
                {
                    // Obter o nome do property set
                    Alignment alignment = (Alignment)tr.GetObject(id, OpenMode.ForRead);
                    if (alignment.Name == "DML - M14")
                    {
                        double comprimento = alignment.Length;
                        propDef.DefaultData = comprimento.ToString("F2"); // Definindo o valor padrão como o comprimento do alinhamento
                        dr.Definitions.Add(propDef); // Adicionando a definição de propriedade ao property set

                    }


                }



                
                if (dictionary.Has("Dados Geometricos1", tr))
                {
                    System.Windows.Forms.MessageBox.Show("\nO property set já existe.");
                    return;
                }*/
                //dictionary.AddNewRecord("Dados Geometricos1", dr); // Adicionando o property set ao dicionário              
                //tr.AddNewlyCreatedDBObject(dr, true); // Adicionando o property set à transação


                //System.Windows.Forms.MessageBox.Show($"\nO property set '{dr.Name}' foi criado.");
                //System.Windows.Forms.MessageBox.Show($"\nA definição '{propDef.Name}' foi Implementada.");

                // Salvar as alterações
                tr.Commit();
               
            }


        }
    }
}
