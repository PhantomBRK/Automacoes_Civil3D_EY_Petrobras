# ERGECivil3DPlugin — AutomacoesCivil3D (Diretrizes Petrobras)

O **ERGECivil3DPlugin** (implementado no projeto **AutomacoesCivil3D**) é uma
solução de engenharia de software de alta performance, concebida para atuar como
o braço tecnológico estratégico na implementação de processos BIM dentro do
ecossistema de infraestrutura industrial da Petrobras.

Desenvolvido sobre a arquitetura moderna do **.NET 8** e utilizando o poder da
linguagem **C#**, este plugin estende as capacidades nativas do **Autodesk
Civil 3D** (2025 e 2026+), transformando fluxos de trabalho tradicionais em
processos automatizados, precisos e orientados a metadados.

A solução foi arquitetada para enfrentar os desafios críticos da engenharia de
infraestrutura, consolidando-se em pilares fundamentais:

- **Extração Inteligente de Sólidos 3D:** coordenação multidisciplinar de
  elementos complexos de infraestrutura e drenagem.
- **Dimensionamento Hidráulico e Diagnóstico de Rede:** verificação de seção por
  Manning, conexão automática e diagnóstico de conectividade/vazão da rede de
  drenagem em sólidos.
- **Processamento Geométrico Avançado de Superfícies:** algoritmos para extração
  de quantitativos (QTO) de terraplenagem e pavimentação.
- **Quantitativos Estruturados (QTO/SMEC):** geração de quantitativos
  parametrizados em caixas, tubos e canaletas, gravados no `.sbd` e exportados
  para relatórios e IFC.
- **Gestão de Metadados e Property Sets:** modelagem da informação sob o padrão
  AWP (Advanced Work Packaging) e regras de governança de dados.
- **Interoperabilidade IFC:** exportação e mapeamento nativo via ecossistema
  robusto **Xbim 6.0**, com suporte a **IFC4** e **IFC4x3**, incluindo o fluxo
  **LOIN** (Level of Information Need).

Ao unificar interfaces de usuário sofisticadas em **WPF** com a robustez da
AutoCAD/Civil 3D .NET API, o projeto assegura eficiência operacional e total
conformidade com os manuais de entrega técnica e padrões normativos da Petrobras.

---

## 1. Visão Geral e Stack Técnica

O projeto foi construído para ser resiliente a mudanças de versão de APIs
proprietárias, isolando a lógica de negócios da dependência direta de uma única
versão do AutoCAD Civil 3D.

### 🛠️ Stack e Frameworks

| Item | Valor | Observação |
|------|-------|------------|
| Target Framework | `net8.0-windows8.0` | `SupportedOSPlatformVersion` 8.0; compatível com Civil 3D 2025 e 2026+ |
| Linguagem | C# (.NET 8) | Recursos de produtividade modernos (pattern matching, collection expressions) |
| UI Engine | WPF & WinForms | MVVM para janelas flutuantes e Palettes nativas para AutoCAD |
| Arquitetura UI | MVVM / SOA | Desacoplamento entre lógica de engenharia e renderização de interface |
| Plataforma | **x64** | Civil 3D é x64-only em runtime (o csproj também declara `AnyCPU;x64;ARM64;x86`, mas o debug real é x64) |
| Versões Suportadas | Civil 3D 2025 & 2026 | Referências do SDK resolvidas via propriedade `AcadDir` |

---

## 2. Gerenciamento de Dependências

Para evitar a fragmentação de versões de pacotes em soluções complexas, o projeto
utiliza o **Central Package Management (CPM)** por meio de um arquivo centralizado
de propriedades.

### 2.1. Configuração CPM (`Directory.Packages.props`)

Versões reais declaradas centralmente no projeto:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- Mapeamento e Interoperabilidade IFC (Xbim) -->
    <PackageVersion Include="Xbim.Common"          Version="6.0.578" />
    <PackageVersion Include="Xbim.Essentials"      Version="6.0.578" />
    <PackageVersion Include="Xbim.Ifc"             Version="6.0.578" />
    <PackageVersion Include="Xbim.Ifc2x3"          Version="6.0.578" />
    <PackageVersion Include="Xbim.Ifc4"            Version="6.0.578" />
    <PackageVersion Include="Xbim.Ifc4x3"          Version="6.0.578" />
    <PackageVersion Include="Xbim.IO.Esent"        Version="6.0.578" />
    <PackageVersion Include="Xbim.IO.MemoryModel"  Version="6.0.578" />
    <PackageVersion Include="Xbim.Geometry.Engine.Interop" Version="5.1.820" />

    <!-- Manipulação de Planilhas e Relatórios -->
    <PackageVersion Include="EPPlus"               Version="8.5.0" />
    <PackageVersion Include="EPPlus.Interfaces"    Version="8.4.0" />
    <PackageVersion Include="ClosedXML"            Version="0.105.0" />
    <PackageVersion Include="ExcelDataReader"      Version="3.8.0" />
    <PackageVersion Include="ExcelDataReader.DataSet" Version="3.8.0" />

    <!-- Parser e Utilitários de Terceiros -->
    <PackageVersion Include="HtmlAgilityPack"      Version="1.12.4" />
    <PackageVersion Include="ICSharpCode.Decompiler" Version="9.1.0.7988" />
    <PackageVersion Include="Microsoft.Extensions.Options" Version="8.0.2" />
    <PackageVersion Include="Microsoft.VisualBasic" Version="10.3.0" />
  </ItemGroup>
</Project>
```

> `Newtonsoft.Json` é consumido como `<Reference>` local (DLL), não via CPM.

### 2.2. Referências Locais do SDK (Autodesk) e Arquivos de Apoio

Estas bibliotecas são mapeadas a partir da pasta de instalação padrão da Autodesk
(`C:\Program Files\Autodesk\AutoCAD 2026\`, via propriedade `AcadDir`, com os
subdiretórios `C3D\`, `ACA\` e `Map\`).

> ⚠️ **Importante:** sempre configure `<Private>False</Private>` (Copy Local =
> False) nessas referências, para evitar o empacotamento de DLLs nativas do
> Civil 3D — o que gera falhas graves de carregamento de tipo (*Type Load
> Exceptions*) em tempo de execução.

- **`accoremgd.dll` / `acdbmgd.dll` / `acmgd.dll` / `acdbmgdbrep.dll`:** núcleo da API AutoCAD.
- **`AeccDbMgd.dll`:** banco de dados e manipulação de entidades de infraestrutura do Civil 3D (Alinhamentos, Corredores, Redes de Pressão e Gravidade).
- **`AecBaseMgd.dll` / `AecPropDataMgd.dll`:** motor de metadados, dicionários de extensão e Property Sets.
- **`Autodesk.Gis.Map.*` / `OSGeo.FDO` / `OSGeo.MapGuide.*`:** camada Map/GIS.
- **`SOLIDOS_2025.dll`:** biblioteca de interoperabilidade do plugin terceirizado **SOLIDOS** para controle de drenagem (referenciada a partir de `%AppData%\...\SOLIDOS.bundle\dotnet_8\`).
- **`Microsoft.Office.Interop.Excel.dll`:** biblioteca local para fluxos legados de exportação direta do Excel.

---

## 3. Estrutura da Solução (Árvore de Projeto)

Organização real de arquivos e pastas do projeto ativo
(`Rotinas Petrobras\AutomacoesCivil3D.csproj`), otimizada para desacoplamento e
portabilidade de módulos:

```
Solution 'RotinasPetrobras' (ERGECivil3DPlugin)
├── Directory.Packages.props          # Gerenciamento Centralizado de Versões (CPM)
├── App.config                        # Redirecionamentos de Binding de Assemblies
└── AutomacoesCivil3D (Project)
    ├── Properties/                    # Configurações de Assembly e Recursos
    ├── Resources/                     # Ícones de Ribbon, logos e imagens
    │
    ├── Automacoes_Solidos/            # Drenagem em Sólidos (Petrobras N-0038)
    │   ├── AjusteConexoesMontante.cs / AjusteConexoesJusante.cs   # Conexão de rede por sentido
    │   ├── AjusteConexõesMontanteJusante.cs                       # Conexão por âncora bidirecional
    │   ├── DimensionamentoDrenagem.cs                             # Verificação/dimensionamento (Manning)
    │   ├── SolidosDimensionarRedeJusante.cs                       # Dimensionamento percorrendo por jusante
    │   ├── DiagnosticoTubo.cs / DiagnosticoConectores.cs          # Diagnóstico de rede
    │   ├── SolidosDiagnosticarConectividade.cs                    # Diagnóstico de conectividade
    │   ├── HidraulicaSolidos.cs / HidraulicaCircularDren.cs       # Núcleo hidráulico (seção/lâmina)
    │   ├── SolidosVazaoCombateIncendio.cs                         # Vazão de combate a incêndio
    │   ├── AjustarConexoesCanaletas.cs                            # Sincronismo de cruzetas/canaletas
    │   ├── CatalogoTuboPadrao.cs                                  # Catálogo de DN comerciais
    │   └── OsnapPresents.cs / SolidosShowroomPetrobras.cs         # Snaps e showroom de peças
    │
    ├── IFC/                           # Interoperabilidade BIM (Motor Xbim — IFC4 / IFC4x3)
    │   ├── IfcInfraConfigEditorWindow.xaml(.cs)                   # Painel administrativo WPF para IFC
    │   ├── IfcAplicarMapeamentoJson.cs / IfcMappingReader.cs      # Mapeamento de PSets via JSON
    │   ├── IfcDrainagePsetSeeder.cs / IfcRoadworksPsetSeeder.cs   # Seed de PSets (drenagem/rodoviário)
    │   ├── IfcSolidosDrainageBinder.cs                            # Vínculo de PSets aos sólidos de drenagem
    │   ├── QUANTITIES_IFC.cs                                      # QTO/SMEC no IFC
    │   ├── Pos_Process_PAV.cs / Pos_Process_DRE.cs / Pos_Process_Ifc4x0.cs  # Pós-processamento 4x3
    │   └── XbimServiceBootstrap.cs                                # Inicialização de sessões Xbim
    │
    ├── LOIN/                          # Level of Information Need (fluxo completo)
    │   ├── LoinFluxoCompletoWindow.xaml(.cs) / LoinFluxoOrchestrator.cs
    │   ├── LoinMapeamentoWindow.xaml(.cs) / LoinMapeamentoModels.cs
    │   ├── LoinProjetoWindow.xaml(.cs)
    │   ├── LoinIfcExportMappingLinker.cs                          # Vínculo LOIN ↔ exportação IFC
    │   └── LoinTrpExtratorSuperficie.cs / LoinTrpValidador.cs     # Extração/validação TRP
    │
    ├── EXTRAIR_SOLIDOS_CORREDORES/    # Modelagem linear avançada (sólidos de corredor)
    │   ├── ExportacaoSolidosCorredores.cs / ...Service.cs / ...Window.xaml
    │   ├── CRIA_BOUNDARY_CORREDORES.cs / DELETAR_PONTAS_AL.cs
    │   ├── EDITAR_FREQUENCIA_CORREDOR.cs                          # Inserção de seções em curvas
    │   └── CodeNameMappingCatalog.cs                              # Catálogo de code names de corredor
    │
    ├── Rotinas DNIT/                  # Exportação IFC de corredores e PSets de sinalização
    │   ├── CorridorIfcExporter.cs / IfcInfraExportHelper.cs
    │   ├── CorridorPropertySetsCreator.cs / PSETSSINALIZACAO.cs
    │   └── SL_CORREDOR_PARAMETRIZADO.cs
    │
    ├── Rotinas_PropertySets/          # Gestão e Modelagem de Atributos (AWP)
    │   ├── PsetViewerControl.cs / PsetWizardForm.cs               # Palete e assistente de PSets
    │   ├── PsetJsonExportImport.cs                                # Export/import de PSets em JSON
    │   ├── CascataPSets.cs                                        # Filtros de seleção dependentes
    │   └── PsetsDrenagem.cs / PsetsSinalização.cs
    │
    ├── TesteClonePsets/               # Snapshot e clonagem de Property Sets
    │   └── PsetSnapshotService.cs / PsetSnapshotImportService.cs
    │
    ├── Rotinas Vale/                  # Instrumentação de monitoramento geotécnico
    │   ├── Piezometros.cs / Inclinometros.cs / Pluviometros.cs
    │   ├── MedidorNivelDagua.cs / Tiltimetro.cs
    │   └── PolylinesPorZonaExcel.cs
    │
    ├── Rotinas PAvelar/               # Fluxo REURB (planilha → PSets)
    │   └── PSETS_REURB.cs / ReurbPlanilhaUI.cs
    │
    ├── Rotinas Houer/                 # Rotinas de superfície/corredor (EPL) e relatórios
    │   ├── Manager.cs                                             # Singleton de Contexto Ativo
    │   ├── ClassePrincipal.cs                                    # Ponto de Entrada (IExtensionApplication)
    │   └── RelatorioPsetsXlsx.cs / Substituir_Taludes_EPL.cs
    │
    ├── Rotinas Petrobras/             # Comandos Petrobras (drenagem, escavação, QTO)
    │   ├── Main.cs                                               # Registro de comandos
    │   ├── RotinaSuperfícieEscavaçãoDRE.cs                       # Projeção de taludes de valas
    │   ├── MemoriaCalculoDrenagemCommand.cs                      # Memória de cálculo
    │   └── SolQuantCaixas.cs / SolQuantTubos.cs / SolQuantCanal.cs ...  # Quantitativos
    │
    ├── Superficies/                   # Processamento geométrico de malhas
    │   └── EXTRAIR_SOLIDOS.cs
    │
    ├── SOL_SECAO_BUEIRO.cs / SOL_SPIKE_PROJECAO.cs               # Seção e projeção de bueiros
    ├── SolAjustarBueiroDnit.cs / SolDiagBueiroDnit.cs           # Ajuste/diagnóstico de bueiro (DNIT)
    ├── AddVariaveisGlobaisQtoSmecCaixa.cs / AddQtoSmecCanaleta.cs # Variáveis globais QTO/SMEC
    ├── RibbonSolidosUltimos.cs / SOLIDOS_QTO.cuix               # Ribbon do plugin
    ├── EXTRAIR_SOLIDOS_SUPERFICIES.cs / LIMPARTRIAGULACAOSUPERFICIES.cs
    └── CodesSpecific.cs                                          # Mapeador de códigos (pontos/links/formas)
```

> A pasta `PastaSolidosCorredoresNovaInterfaceLogicaAntiga/` é **excluída do
> build** (`<Compile Remove>`) e mantida apenas como referência histórica.

---

## 4. Padrões de Projeto e Convenções de Código

Para garantir a legibilidade técnica e a escalabilidade por múltiplos engenheiros
de software, a solução adota convenções estritas.

### 4.1. Convenções Linguísticas e Comunicação

- **Interface e Mensagens:** mensagens impressas no console do AutoCAD via
  `Editor.WriteMessage()`, caixas de diálogo, logs e comentários de código devem
  ser em **Português (PT-BR)**, incluindo acentuação correta.
- **Nomenclatura de Comandos:** comandos registrados com a diretiva
  `[CommandMethod("NOME_COMANDO")]` devem usar termos técnicos claros (os comandos
  de sólidos/drenagem seguem o prefixo `SOL_`).

### 4.2. Padrão de Gerenciamento de Contexto (`Manager.cs`)

Evita-se o acoplamento de chamadas estáticas do sistema operacional através do uso
do Singleton `Manager`. Todo acesso ao documento ativo do Civil 3D deve seguir
este padrão:

```csharp
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.Civil.ApplicationServices;

public sealed class Manager
{
    private static readonly Manager _instance = new Manager();
    public static Manager Instance => _instance;

    public static CivilDocument DocCivil => CivilApplication.ActiveDocument;
    public static Document      DocCad   => Application.DocumentManager.MdiActiveDocument;
    public static Database      DocData  => DocCad.Database;
    public static Editor        DocEditor => DocCad.Editor;
}
```

### 4.3. Padrão de Escopo de Transações

Para evitar corrupção do banco de dados do AutoCAD (`Database`) e garantir que
transações falhas voltem ao estado original sem causar *crashes*, utiliza-se a
declaração `using` simplificada:

```csharp
public void ExecutarOperacao()
{
    var db = Manager.DocData;
    using var trans = db.TransactionManager.StartTransaction();

    try
    {
        // Operações no Banco de Dados do AutoCAD...
        trans.Commit();
    }
    catch (System.Exception ex)
    {
        Manager.DocEditor.WriteMessage($"\n[ERRO] Operação cancelada: {ex.Message}");
        // O Rollback é implícito ao sair do escopo sem chamar o Commit()
    }
}
```

### 4.4. Glossário Técnico

| Termo em Português | Termo Técnico Autodesk | Aplicação no Plugin |
|--------------------|------------------------|---------------------|
| Corredor | `Corridor` | Modelagem de vias de acesso industriais |
| Superfície | `TinSurface` / `Surface` | Malhas de terreno primitivo e terraplenagem |
| Estrutura / PV | `Structure` | Poços de Visita e Caixas de Passagem de drenagem |
| Rede | `PipeNetwork` | Rede coletora de gravidade |
| Alinhamento | `Alignment` | Eixo de referência horizontal |
| Perfil | `Profile` | Eixo de referência vertical (greide) |
| PSet | `PropertySet` | Metadados atribuídos a objetos no AutoCAD |
| Cruzeta / Canaleta | Entidades personalizadas / Sólidos | Dispositivos de Drenagem Urbana e de Área Industrial |
| Bueiro | Sólido + alas | Travessia de drenagem (regras DNIT) |

---

## 5. Diretrizes Normativas: Petrobras N-0038 (Drenagem On-Site)

As ferramentas presentes na pasta `Automacoes_Solidos` seguem os critérios
geométricos e de vazão exigidos pela Norma Técnica **Petrobras N-0038** (Projeto
de Drenagem de Áreas Industriais).

### 5.1. Dimensionamento Hidráulico e Escoamento Superficial

O plugin auxilia na verificação da capacidade de escoamento de canaletas e
cruzetas sob regime de fluxo uniforme, aplicando a **Fórmula de Manning**:

$$V = \frac{1}{n} \cdot R_h^{2/3} \cdot S^{1/2} \qquad Q = V \cdot A_m$$

Onde:

- $V$ — velocidade média do escoamento (m/s).
- $n$ — coeficiente de rugosidade de Manning do material (ex.: concreto liso ≈ 0,013).
- $R_h$ — raio hidráulico, razão entre a área molhada e o perímetro molhado: $R_h = A_m / P_m$.
- $S$ — declividade de fundo da canaleta ou tubulação (m/m).
- $A_m$ — área molhada da seção; $Q$ — vazão de escoamento (m³/s).

### 5.2. Mapeamento de Vazão para Combate a Incêndio

O módulo de drenagem fornece rotinas para mapeamento de áreas de contribuição de
bacias, calculando a vazão de projeto combinando a vazão pluvial (método racional)
e a vazão de água de combate a incêndio exigida para o cenário industrial:

$$Q_{projeto} = Q_{pluvial} + Q_{inc\hat{e}ndio} \qquad Q_{pluvial} = \frac{C \cdot i \cdot A}{360}$$

As estruturas (PVs e caixas) são marcadas com metadados indicando o atendimento
aos limites de capacidade hidráulica previstos pela norma N-0038, impedindo
transbordamentos e minimizando o risco de dispersão de contaminantes de processos
químicos industriais.

### 5.3. Comandos de Conexão, Dimensionamento e Diagnóstico

| Comando | Função |
|---------|--------|
| `SOL_CONECTAR_REDE_POR_MONTANTE` / `_JUSANTE` / `_ANCHOR_BI` | Conecta automaticamente tubos e dispositivos da rede |
| `SOL_DIMENSIONAR_DRENAGEM` | Verifica/dimensiona a rede a montante do âncora (Manning) |
| `SOL_DIMENSIONAR_REDE_POR_JUSANTE` | Dimensionamento percorrendo a rede por jusante |
| `SOL_SYNC_CRUZETA_CANALETAS` | Sincroniza cruzetas e canaletas |
| `SOL_DIAG_TUBO` / `SOL_DIAG_CONECTORES` / `SOL_DIAGNOSTICAR_CONECTIVIDADE` | Diagnóstico de conectividade e vazão; apontam onde a rede se quebra |
| `SOL_LISTAR_PROPS` / `SOL_LISTAR_TUBOS_FANTASMAS` / `SOL_DUMP_DISPOSITIVOS` | Inspeção de propriedades e diagnósticos |

### 5.4. Travessias (Bueiros) — Regras DNIT

| Comando | Função |
|---------|--------|
| `SOL_SECAO_BUEIRO` / `SOL_SECAO_BUEIROS` | Gera a seção do bueiro sobre o eixo (Section View) |
| `SOL_SPIKE_PROJECAO` / `_PF` | Projeção do dispositivo na seção |
| `SOL_DIAG_BUEIRO_DNIT` | Diagnóstico do bueiro frente às regras DNIT (recobrimento, declividade, comprimento pé-a-pé) — somente relatório |
| `SOL_AJUSTAR_BUEIRO_DNIT` | Ajusta o bueiro e as alas ao terreno conforme as regras DNIT |

---

## 6. Módulos Funcionais e Catálogo de Comandos

O plugin expõe **~180 comandos** registrados via `[CommandMethod("...")]` — cada
nome é o ponto de entrada no código. Abaixo, os módulos não detalhados na
Seção 5.

### 6.1. Quantitativos Estruturados (QTO / SMEC)

| Comando | Função |
|---------|--------|
| `AddVariaveisGlobaisQtoSmecCaixa` | Cria as variáveis globais de QTO/SMEC em caixas |
| `AddQtoSmecCanaleta` / `AddQtoSmecEmLote` | Aplica QTO/SMEC a canaletas e em lote |
| `AddQuantitativosCaixa` / `AddConectoresParametricos` | Quantitativos e conectores paramétricos |
| `SOL_PATCH_SBD_QTO_SMEC_ESTRUTURADO` | Grava quantitativos QTO/SMEC estruturados no `.sbd` |
| `SOL_QUANT_CAIXAS` / `_TUBOS` / `_CANAL` / `_GERAL` / `_SMEC` / `_TUBOS_SMEC` / `_CANAL_SMEC` | Quantitativos da rede de drenagem |
| `RelatorioSMECDRE` / `GerarRelatorioTubulacoes` | Relatórios de quantitativos |
| `QTO_MATERIAIS_CSV` / `QTO_SUPERFICIES_TRP_PAV` / `QuantitativoSuperficies` | Quantitativos de materiais e superfícies |

### 6.2. Interoperabilidade IFC (IFC4 / IFC4x3) e LOIN

| Comando | Função |
|---------|--------|
| `EXPORTAR_IFC43_INFRA` / `EXPORTAR_IFC_CORREDORES` | Exporta IFC 4x3 de infraestrutura / corredores |
| `IFC_CONFIG_INFRA` / `IFCEDITCONFIG` | Configuração do exportador IFC (painel WPF) |
| `IFC_DEFINIR_PARAMETRO` / `IFC_APLICAR_PARAMETROS` / `IFC_APLICAR_MAPEAMENTO_JSON` | Parâmetros e mapeamento de PSets via JSON |
| `IFC_CRIAR_PSETS_DRENAGEM` / `IFC_CRIAR_PSETS_RODOVIARIOS` | Cria Property Sets IFC (drenagem / rodoviários) |
| `IFC_VINCULAR_PSETS_SOLIDOS_DRENAGEM` / `IFC_PREP_PIPENET_SOLIDS` | Vincula PSets aos sólidos de drenagem |
| `SIFC_QTO_SMEC` / `SIFC_QTO_SMEC_IFC4` | Quantitativos QTO/SMEC embarcados no IFC |
| `SIFC_DRE_POST_4X3` / `SIFC_PAV_POST_4X3` / `SIFC_PAV_TRANSFERIR_PSETS_4X3` | Pós-processamento IFC 4x3 (drenagem/pavimento) |
| `LOIN_LINKAR_IFCEXPORT` / `_LOIN_TRP_EXTRAIR_SUPERFICIE` / `_LOIN_TRP_VALIDAR` | Fluxo LOIN: vínculo, extração e validação |

### 6.3. Property Sets (AWP), Snapshot, REURB e Sinalização

| Comando | Função |
|---------|--------|
| `AplicarPSet` / `AplicarPsetTodos` / `CRIA_PSETS_CORREDOR` | Aplica/cria Property Sets em objetos e corredores |
| `PSET_EXPORTAR_JSON` / `PSET_IMPORTAR_JSON` | Export/import de PSets em JSON |
| `EXPORTAR_SNAPSHOT_PSETS` / `IMPORTAR_SNAPSHOT_PSETS` / `CLONAR_PSETS_COPY` | Snapshot e clonagem de PSets entre desenhos |
| `RELATORIO_PSET_XLSX` | Relatório de PSets em Excel |
| `SINAL_PSET_APLICAR` / `SINAL_PSET_MAPEAR` | PSets de sinalização (DNIT) |
| `REURB_IMPORT` / `REURB_IMPORT_APLICAR` / `REURB_XLS_APLICAR` / `ZONAS_POLY_XLS` | Fluxo REURB (planilha → PSets) |

### 6.4. Corredores e Sólidos de Corredor

| Comando | Função |
|---------|--------|
| `CRIA_SUP_CORRIDORES` / `CRIA_SUP_QTO_CORREDORES` / `SET_SURFTARGET_ALL_CORRIDORS` | Superfícies e targets de corredores |
| `SPLITCORRIDORREGIONS` / `SPLITREG_ESTACA` / `APPLY_REGION_FREQS_ALL` | Regiões e frequências de corredor |
| `AC3D_ExtractBoundaryFromCorridors_ALL` / `C3D_VOL_BOUNDARY_AUTOMATE` / `CROPSURF2DWG` | Boundaries e recorte de superfície |
| `ExportarSolidosCorredores` / `ExportarSolidosCorredoresNovaInterface` / `EXSOLIDOSCORR_JSON` / `_LOIN` / `_NI` | Extração de sólidos de corredor |

### 6.5. Instrumentação Geotécnica (Monitoramento)

| Comando | Função |
|---------|--------|
| `PIEZOMETROS` | Inserção/locação de piezômetros |
| `Inclinometros` / `Tiltimetro` / `Pluviometros` / `MedidorNivelDagua` | Demais instrumentos de monitoramento geotécnico |

### 6.6. Superfícies, Escavação e Utilitários

| Comando | Função |
|---------|--------|
| `EscavacaoDrenagem` / `CalcularEscavacao` / `CriarEscavacaoDRE` | Cálculo e modelagem de escavação (DRE) |
| `RebuidSurface` / `TIN_NATIVO_PARA_MESH` / `CRIA_TILT` | Manipulação de superfícies TIN |
| `MEMORIA_CALCULO_DRENAGEM` | Memória de cálculo de drenagem |
| `SOL_RIBBON` / `RP_RIBBON` / `RP_UI` / `RP_UI_REFRESH` | Ribbon e interface do plugin (`SOLIDOS_QTO.cuix`) |
| `ActivateCivilWorkspace` / `TrocarWS` / `ReiniciarCivil` | Ambiente / workspace |
| `TutorialDre` / `TutorialPav` / `TutorialTrp` | Tutoriais embutidos |

---

## 7. Guia de Manutenção e Migração Multi-versão (2025 → 2026+)

O plugin foi projetado de forma que o suporte a novas versões anuais do Civil 3D
exija o mínimo de alteração de código fonte, alterando apenas referências no nível
de build do MSBuild.

### 7.1. Condicionamento das Referências do `.csproj`

O `.csproj` resolve as DLLs do SDK a partir da propriedade `AcadDir` (padrão:
`C:\Program Files\Autodesk\AutoCAD 2026`), que pode ser sobrescrita por linha de
comando. O alvo final do *deploy* é condicionado pela existência da pasta-bundle:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows8.0</TargetFramework>
    <UseWpf>true</UseWpf>
    <UseWindowsForms>true</UseWindowsForms>
    <Platforms>AnyCPU;x64;ARM64;x86</Platforms>
    <AcadDir Condition="'$(AcadDir)' == ''">C:\Program Files\Autodesk\AutoCAD 2026</AcadDir>
    <CivilDir>$(AcadDir)\C3D</CivilDir>
  </PropertyGroup>

  <!-- Referências do SDK Autodesk (Copy Local = False) -->
  <ItemGroup>
    <Reference Include="accoremgd"><HintPath>$(AcadDir)\accoremgd.dll</HintPath><Private>False</Private></Reference>
    <Reference Include="acdbmgd"><HintPath>$(AcadDir)\acdbmgd.dll</HintPath><Private>False</Private></Reference>
    <Reference Include="acmgd"><HintPath>$(AcadDir)\acmgd.dll</HintPath><Private>False</Private></Reference>
    <Reference Include="AeccDbMgd"><HintPath>$(AcadDir)\C3D\AeccDbMgd.dll</HintPath><Private>False</Private></Reference>
    <Reference Include="AecBaseMgd"><HintPath>$(AcadDir)\ACA\AecBaseMgd.dll</HintPath><Private>False</Private></Reference>
    <Reference Include="AecPropDataMgd"><HintPath>$(AcadDir)\ACA\AecPropDataMgd.dll</HintPath><Private>False</Private></Reference>
  </ItemGroup>
</Project>
```

> Para apontar para outra instalação (unidade `D:\`, rede etc.), sobrescreva
> `AcadDir` no comando de build: `-p:AcadDir="D:\Autodesk\AutoCAD 2026"`.

### 7.2. Manifesto do Pacote (`PackageContents.xml`)

Para carregar o plugin automaticamente via tecnologia **Autoloader** da Autodesk:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0" Version="2026.1.0" Name="ERGE Civil 3D Plugin" AppCode="ERGE.Civil3D">
  <RuntimeRequirements SeriesMin="R25.0" SeriesMax="R26.0" Platform="AutoCAD*" />
  <Components>
    <RuntimeEntry OS="Win64" Platform="Civil3D" SeriesMin="R25.0" SeriesMax="R26.0"
                  AppName="AutomacoesCivil3D" ModuleName="./Contents/AutomacoesCivil3D.dll" />
  </Components>
</ApplicationPackage>
```

> `R25.0` = AutoCAD Civil 3D 2025 · `R26.0` = AutoCAD Civil 3D 2026.

---

## 8. Pipeline de Build, Compilação e Deploy Automático

### 8.1. Compilação via Linha de Comando

> ⚠️ **Use o MSBuild do Visual Studio, não `dotnet build`.** O `.csproj` contém
> `<COMReference>` (10 referências COM), que exigem a task `ResolveComReference`
> do MSBuild full-framework; o CLI `dotnet build` falha com `error MSB4803`.

```powershell
# Build Debug x64
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
    "Rotinas Petrobras\AutomacoesCivil3D.csproj" `
    -p:Configuration=Debug -p:Platform=x64 -restore -nologo -v:quiet

# Build Release x64
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
    "Rotinas Petrobras\AutomacoesCivil3D.csproj" `
    -p:Configuration=Release -p:Platform=x64 -restore -nologo -v:quiet
```

Se o Civil 3D estiver instalado em caminho personalizado, sobrescreva `AcadDir`:
`-p:AcadDir="D:\Autodesk\AutoCAD 2026"`.

### 8.2. Deploy de Teste Automatizado (Post-Build Target)

Para acelerar o ciclo *code-build-test*, o `.csproj` possui uma tarefa
pós-compilação (`DeployAutomacoesPetrobrasBundle`, `AfterTargets="Build"`) que
copia os binários para a pasta monitorada pelo Autoloader **se o bundle existir**:

```xml
<Target Name="DeployAutomacoesPetrobrasBundle" AfterTargets="Build"
        Condition="Exists('$(PetrobrasBundleDir)')">
  <PropertyGroup>
    <PetrobrasBundleDir>$(AppData)\Autodesk\ApplicationPlugins\AutomacoesPetrobras.bundle\Contents\</PetrobrasBundleDir>
  </PropertyGroup>
  <ItemGroup>
    <OutputFiles Include="$(OutputPath)\**\*.*" />
  </ItemGroup>
  <Copy SourceFiles="@(OutputFiles)"
        DestinationFiles="@(OutputFiles->'$(PetrobrasBundleDir)%(RecursiveDir)%(Filename)%(Extension)')"
        SkipUnchangedFiles="true" />
</Target>
```

> Após o build, basta **reabrir o Civil 3D** para recarregar o plugin a partir de
> `%AppData%\Autodesk\ApplicationPlugins\AutomacoesPetrobras.bundle\`.

---

## 9. Segurança de Código, Ofuscação e Distribuição

A propriedade intelectual das rotinas de cálculo de volume de terraplenagem e
regras de negócio industriais deve ser protegida antes da entrega técnica oficial.

```
[Código C# Compilado (Release)]
            │
            ▼
   ┌─────────────────┐
   │   .NET Reactor  │ ◄── Ofuscação de classes/métodos e criptografia de strings
   └────────┬────────┘
            ▼
   ┌─────────────────┐
   │  Assinatura     │ ◄── Certificado digital (evita bloqueios no AutoCAD)
   │  Digital (Sign) │
   └────────┬────────┘
            ▼
   ┌─────────────────┐
   │   Inno Setup 6  │ ◄── Geração do pacote executável (.exe)
   └─────────────────┘
```

### 9.1. Ofuscação com .NET Reactor

As compilações `Release` aplicam, via .NET Reactor:

- **Ofuscação de fluxo de controle:** impede que decompiladores (ILSpy, dnSpy)
  decodifiquem algoritmos e fórmulas.
- **Criptografia de strings:** oculta segredos e queries internas.
- **Remoção de metadados públicos:** suprime assinaturas de membros internos
  desnecessários à execução.

### 9.2. Assinatura Digital do Assembly

O assembly resultante (`AutomacoesCivil3D.dll`) deve ser assinado digitalmente com
uma autoridade certificadora corporativa Petrobras ou AC raiz confiável pelo
Windows, evitando alertas de "Editor Não Confiável" e bloqueios de segurança em
máquinas de produção.

---

## 10. Empacotamento e Geração do Instalador (Inno Setup)

O processo de empacotamento reúne todos os arquivos de execução e dependências em
um único instalador autônomo executável (`.exe`).

### 10.1. Estrutura de Distribuição

```
%APPDATA%\Autodesk\ApplicationPlugins\AutomacoesPetrobras.bundle\
├── PackageContents.xml          # Manifesto de carregamento (Autoloader)
└── Contents/
    ├── AutomacoesCivil3D.dll     # Assembly principal (ofuscado e assinado)
    ├── Xbim.*.dll, EPPlus.dll, ClosedXML.dll, ...   # Dependências
    └── Resources/
        ├── Templates/            # Arquivos .dwt corporativos
        ├── Stylesheets/          # Modelos .xsl para relatórios de QTO
        └── Icons/                # Ícones da Ribbon de comandos
```

### 10.2. Script do Inno Setup (`erge-plugin-installer.iss`)

```iss
[Setup]
AppName=ERGE Civil 3D Plugin
AppVersion=2026.1.0
AppPublisher=ERGE Petrobras Tech Team
DefaultDirName={userappdata}\Autodesk\ApplicationPlugins\AutomacoesPetrobras.bundle
DisableDirPage=yes
OutputBaseFilename=Setup_ERGE_Civil3D_2026
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64

[Files]
Source: "..\bundle\PackageContents.xml"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release\Contents\*"; DestDir: "{app}\Contents"; Flags: ignoreversion recursesubdirs
Source: "..\bundle\Resources\*"; DestDir: "{app}\Resources"; Flags: ignoreversion recursesubdirs

[Code]
function IsCivil3D2025Installed(): Boolean;
begin
  Result := RegKeyExists(HKEY_LOCAL_MACHINE, 'SOFTWARE\Autodesk\AutoCAD\R25.0\ACAD-8100:409');
end;

function IsCivil3D2026Installed(): Boolean;
begin
  Result := RegKeyExists(HKEY_LOCAL_MACHINE, 'SOFTWARE\Autodesk\AutoCAD\R26.0\ACAD-9100:409');
end;

function InitializeSetup(): Boolean;
begin
  Result := true;
  if not (IsCivil3D2025Installed() or IsCivil3D2026Installed()) then
    MsgBox('Aviso: nenhuma instalação do Autodesk Civil 3D (2025 ou 2026) foi detectada.' + #13#10 +
           'O plugin será instalado, mas pode não funcionar até um ambiente compatível ser configurado.',
           mbInformation, MB_OK);
end;
```

### 10.3. Automação via MSBuild (Post-Build Event)

Para integrar a geração do instalador ao build de produção (`Release`):

```
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "$(ProjectDir)installer\erge-plugin-installer.iss"
```
