# Automações Civil 3D — Petrobras

Plugin para **AutoCAD Civil 3D 2026** (C# / .NET 8) com automações de
**drenagem, sólidos de corredor, quantitativos (QTO/SMEC), exportação IFC e
Property Sets** voltadas aos fluxos de projeto de infraestrutura.

> Os comandos são executados dentro do Civil 3D digitando o nome do comando na
> linha de comando do AutoCAD, ou pelo ribbon do plugin (`SOL_RIBBON` / `RP_RIBBON`).

---

## ✨ Novidades recentes

Funcionalidades adicionadas/consolidadas no ciclo atual:

- **Dimensionamento hidráulico da rede de drenagem em Sólidos** — verificação de
  seção por Manning, dimensionamento a montante de âncora e por jusante
  (`SOL_DIMENSIONAR_DRENAGEM`, `SOL_DIMENSIONAR_REDE_POR_JUSANTE`).
- **Diagnóstico de rede** — apontam onde a conectividade/vazão se quebra
  (`SOL_DIAG_TUBO`, `SOL_DIAG_CONECTORES`, `SOL_DIAGNOSTICAR_CONECTIVIDADE`).
- **Conexão automática de rede** por montante, jusante e âncora bidirecional
  (`SOL_CONECTAR_REDE_POR_MONTANTE`, `_JUSANTE`, `_ANCHOR_BI`).
- **Quantitativos QTO/SMEC estruturados** para caixas, tubos e canaletas, com
  variáveis globais e gravação no `.sbd`
  (`AddVariaveisGlobaisQtoSmecCaixa`, `AddQtoSmecCanaleta`, `SOL_QUANT_*`,
  `SOL_PATCH_SBD_QTO_SMEC_ESTRUTURADO`).
- **Seção e projeção de bueiros** sobre o eixo com Section Views
  (`SOL_SECAO_BUEIRO`, `SOL_SECAO_BUEIROS`, `SOL_SPIKE_PROJECAO`).
- **Regras DNIT para bueiros** — diagnóstico e ajuste ao terreno (recobrimento,
  declividade mínima, comprimento pé-a-pé) — `SOL_DIAG_BUEIRO_DNIT`,
  `SOL_AJUSTAR_BUEIRO_DNIT`.
- **Sincronismo de cruzetas/canaletas** (`SOL_SYNC_CRUZETA_CANALETAS`).
- **Exportação IFC 4x3 de infraestrutura** + Property Sets de drenagem e
  rodoviários + integração LOIN
  (`EXPORTAR_IFC43_INFRA`, `SIFC_QTO_SMEC`, `IFC_CRIAR_PSETS_DRENAGEM`,
  `LOIN_LINKAR_IFCEXPORT`).
- **Ribbon dedicado** carregado via `SOLIDOS_QTO.cuix` (`SOL_RIBBON`).

---

## Requisitos

| Item | Versão |
|------|--------|
| AutoCAD Civil 3D | **2026** |
| .NET | **8.0** (`net8.0-windows8.0`) |
| Plataforma | **Windows x64** |
| IDE / Build | **Visual Studio + MSBuild** (full framework) |

Dependências de terceiros (Autodesk Managed API, Xbim, EPPlus/ClosedXML,
HtmlAgilityPack etc.) são referenciadas **localmente** a partir de
`Program Files\Autodesk\AutoCAD 2026\` e do NuGet — **não são versionadas**
neste repositório (ver `.gitignore`). Pacotes NuGet são gerenciados de forma
central via `Directory.Packages.props`.

---

## Build e instalação

O projeto ativo é **`Rotinas Petrobras\AutomacoesCivil3D.csproj`**
(solução `RotinasPetrobras.sln`).

> ⚠️ **Use o MSBuild do Visual Studio, não `dotnet build`.** O csproj contém
> `<COMReference>` que exige a task `ResolveComReference` do MSBuild full-framework;
> o CLI `dotnet` falha com `error MSB4803`.

```powershell
# Build Debug x64
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
    "Rotinas Petrobras\AutomacoesCivil3D.csproj" `
    -p:Configuration=Debug -p:Platform=x64 -restore -nologo -v:quiet
```

Após o build, o alvo `DeployAutomacoesPetrobrasBundle` copia automaticamente a
saída para
`%AppData%\Autodesk\ApplicationPlugins\AutomacoesPetrobras.bundle\Contents\`.
Basta **reabrir o Civil 3D** para recarregar o plugin.

---

## Catálogo de comandos

### 🟦 Drenagem em Sólidos (`SOL_*`)

| Comando | Função |
|---------|--------|
| `SOL_CONECTAR_REDE_POR_MONTANTE` / `_JUSANTE` / `_ANCHOR_BI` | Conecta a rede de tubos/dispositivos automaticamente |
| `SOL_DIMENSIONAR_DRENAGEM` | Dimensiona/verifica a rede a montante do âncora (Manning) |
| `SOL_DIMENSIONAR_REDE_POR_JUSANTE` | Dimensionamento percorrendo por jusante |
| `SOL_DIAG_TUBO` / `SOL_DIAG_CONECTORES` / `SOL_DIAGNOSTICAR_CONECTIVIDADE` | Diagnóstico de conectividade e vazão da rede |
| `SOL_SECAO_BUEIRO` / `SOL_SECAO_BUEIROS` | Seção dos bueiros sobre o eixo (Section View) |
| `SOL_SPIKE_PROJECAO` / `_PF` | Projeção do dispositivo na seção |
| `SOL_AJUSTAR_BUEIRO_DNIT` / `SOL_DIAG_BUEIRO_DNIT` | Ajuste/diagnóstico de bueiro pelas regras DNIT |
| `SOL_SYNC_CRUZETA_CANALETAS` | Sincroniza cruzetas e canaletas |
| `SOL_QUANT_CAIXAS` / `_TUBOS` / `_CANAL` / `_GERAL` / `_SMEC` / `_TUBOS_SMEC` / `_CANAL_SMEC` | Quantitativos da rede |
| `SOL_PATCH_SBD_QTO_SMEC_ESTRUTURADO` | Grava quantitativos QTO/SMEC estruturados no `.sbd` |
| `SOL_LISTAR_PROPS` / `SOL_LISTAR_TUBOS_FANTASMAS` / `SOL_DUMP_DISPOSITIVOS` | Inspeção de propriedades e diagnósticos |
| `DumpSolidosXml` / `DumpSolidosXmlAtivo` / `DumpSolidosXmlForcado` | Dump do XRecord SOLIDOS |
| `SOL_RIBBON` | Carrega o ribbon de QTO/Sólidos (`SOLIDOS_QTO.cuix`) |

### 🟩 Quantitativos (QTO / SMEC)

| Comando | Função |
|---------|--------|
| `AddVariaveisGlobaisQtoSmecCaixa` | Cria variáveis globais de QTO em caixas |
| `AddQtoSmecCanaleta` / `AddQtoSmecEmLote` | Aplica QTO SMEC a canaletas / em lote |
| `AddQuantitativosCaixa` / `AddConectoresParametricos` | Quantitativos e conectores paramétricos |
| `RelatorioSMECDRE` / `GerarRelatorioTubulacoes` | Relatórios de quantitativos |
| `QTO_MATERIAIS_CSV` / `QTO_SUPERFICIES_TRP_PAV` / `QuantitativoSuperficies` | Quantitativos de materiais e superfícies |
| `ExportToCsvPav` / `ExportToCsvTrp` / `ExportSampleLineVolumes` | Exportação de volumes/quantitativos para CSV |

### 🟨 Exportação / Importação IFC

| Comando | Função |
|---------|--------|
| `EXPORTAR_IFC43_INFRA` / `EXPORTAR_IFC_CORREDORES` | Exporta IFC 4x3 de infraestrutura / corredores |
| `IFC_CONFIG_INFRA` / `IFCEDITCONFIG` | Configuração do exportador IFC |
| `IFC_DEFINIR_PARAMETRO` / `IFC_APLICAR_PARAMETROS` / `IFC_APLICAR_MAPEAMENTO_JSON` | Parâmetros e mapeamento de PSets via JSON |
| `IFC_CRIAR_PSETS_DRENAGEM` / `IFC_CRIAR_PSETS_RODOVIARIOS` | Cria Property Sets IFC de drenagem / rodoviários |
| `IFC_VINCULAR_PSETS_SOLIDOS_DRENAGEM` / `IFC_IMPORTAR_PSETS_MODELOS_SOLIDOS` / `IFC_PREP_PIPENET_SOLIDS` | Vínculo de PSets aos sólidos de drenagem |
| `SIFC_QTO_SMEC` / `SIFC_QTO_SMEC_IFC4` | Quantitativos QTO/SMEC no IFC |
| `SIFC_DRE_POST_4X3` / `SIFC_PAV_POST_4X3` / `SIFC_PAV_TRANSFERIR_PSETS_4X3` | Pós-processamento IFC 4x3 (drenagem/pavimento) |
| `LOIN_LINKAR_IFCEXPORT` / `_LOIN_TRP_EXTRAIR_SUPERFICIE` / `_LOIN_TRP_VALIDAR` | Integração e validação LOIN |

### 🟧 Property Sets (AutoCAD)

| Comando | Função |
|---------|--------|
| `AplicarPSet` / `AplicarPsetTodos` / `AplicarConexao` | Aplica Property Sets a objetos |
| `CRIA_PSETS_CORREDOR` / `ATUALIZA_PSET_FISICO_SOLIDOS` | PSets de corredor / atualização física |
| `PSET_EXPORTAR_JSON` / `PSET_IMPORTAR_JSON` / `EXPORTAR_SNAPSHOT_PSETS` / `IMPORTAR_SNAPSHOT_PSETS` | Export/import de PSets (JSON / snapshot) |
| `CLONAR_PSETS_COPY` / `IMPORTAR_PSETS_DWG` / `RELATORIO_PSET_XLSX` | Clonagem, importação de DWG e relatório XLSX |
| `SINAL_PSET_APLICAR` / `SINAL_PSET_MAPEAR` | PSets de sinalização |
| `AWPSet` / `AWPViewer` / `CREATE_AWP_LAYERS` / `PSetsCascata` / `PsetsPlacas` | Fluxos AWP e PSets em cascata |
| `REURB_IMPORT` / `REURB_IMPORT_APLICAR` / `REURB_XLS_APLICAR` / `ZONAS_POLY_XLS` | Fluxo REURB (planilha → PSets) |

### 🟪 Corredores e Sólidos de corredor

| Comando | Função |
|---------|--------|
| `CRIA_SUP_CORRIDORES` / `CRIA_SUP_QTO_CORREDORES` / `SET_SURFTARGET_ALL_CORRIDORS` | Superfícies e targets de corredores |
| `SPLITCORRIDORREGIONS` / `SPLITREG_ESTACA` / `APPLY_REGION_FREQS_ALL` | Regiões e frequências de corredor |
| `AC3D_ExtractBoundaryFromCorridors_ALL` / `C3D_VOL_BOUNDARY_AUTOMATE` / `CROPSURF2DWG` | Boundaries e recorte de superfície |
| `ExportarSolidosCorredores` / `ExportarSolidosCorredoresNovaInterface` / `ExportarSolidosComRelatorio` | Extração de sólidos de corredor |
| `EXSOLIDOSCORR_JSON` / `_LOIN` / `_NI` / `_NI_LOIN` | Variantes de extração (JSON / LOIN / nova interface) |

### 🟫 Superfícies, escavação e feature lines

| Comando | Função |
|---------|--------|
| `RebuidSurface` / `AJUSTA_TEXTOS_SUP` / `TIN_NATIVO_PARA_MESH` / `CRIA_TILT` | Manipulação de superfícies TIN |
| `CalcularEscavacao` / `CriarEscavacaoDRE` / `EscavacaoDrenagem` | Cálculo e modelagem de escavação (DRE) |
| `CriarFeatureLinePorRede` / `ExtractFeatureLinePoints` / `TabelaPIPointsComCogoPointsAtualizavel` | Feature lines e pontos |

### ⬜ Perfis, seções, pranchas e utilitários

| Comando | Função |
|---------|--------|
| `CriaProfile` / `GerarProfile` / `CriarSampleLinesSectionViews` / `CriarSectionViewEixo` | Perfis e seções |
| `InclinacaoTubo` / `TuboDescida` / `RimElevation` / `FIX_STATIONOFFSET_LABELS` | Tubos, cotas e rótulos |
| `AtualizaPranchaPorPlanilha` / `PreencherPranchaPAvelar` / `GERARDCD` | Pranchas e documentos |
| `MEMORIA_CALCULO_DRENAGEM` | Memória de cálculo de drenagem |
| `TutorialDre` / `TutorialPav` / `TutorialTrp` | Tutoriais embutidos |
| `ActivateCivilWorkspace` / `TrocarWS` / `ReiniciarCivil` / `CONFIGURAR_PROJETOS_EPL` | Ambiente / workspace |
| `RP_RIBBON` / `RP_UI` / `RP_UI_REFRESH` / `SOL_RIBBON` | Ribbon e interface do plugin |

> Catálogo representativo — o plugin expõe **~180 comandos** no total. O nome de
> cada comando (`[CommandMethod("...")]`) é o ponto de entrada no código.

---

## Estrutura do repositório

```
Rotinas Petrobras/            ← projeto principal (AutomacoesCivil3D.csproj)
  ├─ IFC/                     ← exportação/importação IFC, PSets, Xbim
  ├─ Automacoes_Solidos/      ← drenagem em sólidos, dimensionamento, diagnósticos
  ├─ Rotinas Petrobras/       ← comandos Petrobras (drenagem, escavação)
  ├─ EXTRAIR_SOLIDOS_CORREDORES/ ← extração de sólidos de corredor
  ├─ LOIN/                    ← exportação LOIN
  ├─ Resources/               ← recursos visuais (excluídos do compile)
  └─ ...
CLAUDE.md                     ← convenções de projeto e stack (referência)
```

Convenções de código, glossário PT-BR ↔ Civil 3D e detalhes de stack estão
documentados em [`CLAUDE.md`](CLAUDE.md).

---

## Convenções

- **Idioma:** comentários, mensagens ao usuário e nomes de variáveis em **PT-BR**.
- **Comandos:** declarados via `[CommandMethod("Nome")]`, executados dentro de uma
  `Transaction` do `TransactionManager`.
- **Acesso a documentos:** via singleton `Manager.DocCivil` / `Manager.DocData` /
  `Manager.DocEditor`.
