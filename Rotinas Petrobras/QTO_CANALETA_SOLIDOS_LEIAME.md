# Pacote QTO SMEC — CANALETAS (SOLIDOS Builder)

Gerado a partir da planilha
`TEMPLATE DRENAGEM\PLANILHA DE CALCULO DE QUANTITATIVOS LEANDRO COM OTIMIZAÇÃO DA ABA RESUMO.xlsx`
(abas `CANALET_Pluv` / `CANALET_Cont` / `CANALET_Oleo` — fórmulas idênticas).

O pacote reproduz, dentro do dispositivo **linear** de canaleta do SOLIDOS
(`SolGravityLinear`, famílias `PETROBRAS - CANALETAS - *`), exatamente as colunas de
quantitativo da planilha. Segue o mesmo padrão da sequência **`QTO TUB_OLEO`** já usada
no tubo (`TUBO_OLEO_SOLIDOS_LIMPO.xml`).

Arquivos:
- **`QTO_CANALETA_SOLIDOS.xml`** — bloco de **cálculos** (fluxograma). Cole no SOLIDOS Builder.
- Este LEIAME — mapeamento de entradas + lista de **variáveis globais** (saídas) a registrar.

---

## 1. Como inserir

### 1a. Cálculos (o `.xml`)
1. Abra a canaleta no **SOLIDOS Builder** (aba do fluxograma / "Criação das Atividades").
2. Copie todo o conteúdo de `QTO_CANALETA_SOLIDOS.xml` e **cole** (paste XML).
   O Builder cria uma nova sequência de topo **`QTO SMEC CANALETA`** com as 22 saídas.

### 1b. Variáveis globais (as saídas — passo OBRIGATÓRIO)
O paste de XML só insere **atividades**. Para os valores **aparecerem no painel e serem
exportados pelo QTO**, cada saída precisa de uma **DynamicProperty** declarada na lista
`Properties` (senão o `SetOutPutParam` é calculado e **silenciosamente descartado** — mesmo
comportamento confirmado nas caixas).

Crie as DynamicProperties da tabela da seção 3 (no painel "Criação das Propriedades",
Categoria **`QTO SMEC`**, `Direction = Leitura e Escrita`, `ValueProvider = 3`).
Se preferir um clique só (insere cálculos **e** propriedades direto no `.sbd`), peça o comando
`.NET` `AddQtoSmecCanaleta` — é o análogo de
[`AddQtoSmecEmLote.cs`](AddQtoSmecEmLote.cs) para canaletas.

---

## 2. Mapeamento de ENTRADAS — **VERIFIQUE antes de usar**

As fórmulas referenciam propriedades que o dispositivo já deve expor. Os nomes abaixo foram
extraídos do código real do projeto (leitor IFC em `IFC/IfcSolidosDrainageBinder.cs` e a
sequência `QTO TUB_OLEO` do tubo). **Confirme que batem com a SUA canaleta**; se algum diferir,
corrija só na linha correspondente do `.xml`.

| Planilha | Significado | Entrada no SOLIDOS | Origem da confirmação |
|---|---|---|---|
| `H` (E) | Extensão do trecho | `Axis3D.Length` → `Comprimento` | igual ao tubo (`Comprimento=Axis3d.Length`) |
| `D` | Elev. terreno **montante** | `StartTopElevation` | usado no `QTO TUB_OLEO` |
| `E` | FIT (fundo interno) **montante** | `StartInvertElevation` | usado no `QTO TUB_OLEO` |
| `F` | Elev. terreno **jusante** | `EndTopElevation` | usado no `QTO TUB_OLEO` |
| `G` | FIT (fundo interno) **jusante** | `EndInvertElevation` | usado no `QTO TUB_OLEO` |
| `J` (b) | Largura **interna** | `Largura` | `IfcSolidosDrainageBinder` lê `Largura`/`Width` |
| `K` (e) | Espessura **paredes e fundo** | `Parede` | `IfcSolidosDrainageBinder` lê `Parede` |

**Premissas embutidas (iguais às da planilha):**
- O **topo** da canaleta está no nível do terreno → altura interna = `terreno − FIT`.
  Para canaleta de **bordo de aterro** (topo acima do terreno) isso não vale; ajuste.
- `Parede` representa parede **e** fundo (a planilha usa um único `e`).
- `Largura` é a largura **interna** (`b`). Se na sua canaleta `Largura` já for a **externa**,
  troque a fórmula de `LarguraExterna` para `Largura` e ajuste `VolConcCanaleta`.
- Concreto magro `EspConcMagro = 0,05 m`, massa específica `1,8 t/m³` e taxa de aço
  `50 kg/m³` são constantes da planilha — já vêm como saídas editáveis (mude num lugar só).

---

## 3. SAÍDAS (variáveis globais) — registrar como DynamicProperty

Ordem de cálculo = ordem da tabela (cada linha pode usar as anteriores). Categoria `QTO SMEC`.

| # | Nome (PropName) | Unid. | TypeConverter | Coluna planilha |
|---|---|---|---|---|
| 1 | `Comprimento` | m | `SOLIDOS.UnidadeDistancia` | H (E) |
| 2 | `EspConcMagro` | m | `SOLIDOS.UnidadeDistancia` | N |
| 3 | `TaxaAco` | kg/m³ | (numérico) | AC |
| 4 | `LarguraExterna` | m | `SOLIDOS.UnidadeDistancia` | L (B) = 2·e+b |
| 5 | `LarguraVala` | m | `SOLIDOS.UnidadeDistancia` | M (L) |
| 6 | `ProfValaMont` | m | `SOLIDOS.UnidadeDistancia` | O (H) |
| 7 | `ProfValaJus` | m | `SOLIDOS.UnidadeDistancia` | P |
| 8 | `SecValaMont` | m² | `SOLIDOS.UnidadeArea` | Q (S1) |
| 9 | `SecValaJus` | m² | `SOLIDOS.UnidadeArea` | R (S2) |
| 10 | `AlturaMedia` | m | `SOLIDOS.UnidadeDistancia` | S (Hmed) |
| 11 | `AreaApiloamento` | m² | `SOLIDOS.UnidadeArea` | T |
| 12 | `VolEscav` | m³ | `SOLIDOS.UnidadeVolume` | U (VE) |
| 13 | `VolConcMagro` | m³ | `SOLIDOS.UnidadeVolume` | V (Vcm) |
| 14 | `VolCanaleta` | m³ | `SOLIDOS.UnidadeVolume` | W (Vc) |
| 15 | `VolReaterro` | m³ | `SOLIDOS.UnidadeVolume` | X (VR) |
| 16 | `VolBotaFora` | m³ | `SOLIDOS.UnidadeVolume` | Y (Vbf) |
| 17 | `MassaEspAdotada` | t/m³ | `SOLIDOS.UnidadeDensidade` | Z (M_esp) |
| 18 | `MassaBotaFora` | t | `SOLIDOS.UnidadeMassa` | AA (Mbf) |
| 19 | `VolConcCanaleta` | m³ | `SOLIDOS.UnidadeVolume` | AB (V_cc) |
| 20 | `MassaAco` | kg | `SOLIDOS.UnidadeMassa` | AD (M_aço) |
| 21 | `AreaFormas` | m² | `SOLIDOS.UnidadeArea` | AE (A_form) |
| 22 | `TaxaFormas` | m²/m³ | `SOLIDOS.UnidadeArea` | AF (Tx_form) |

Macros (campo `Macro`): Distância `[(T1|U9|P2|D0|N0|M1|Z0)]` · Área `[(T1|U38|P2|D0|N0|M1|Z0)]`
· Volume `[(T1|U15|P2|D0|N0|M1|Z0)]` · Massa `[(T1|U126|P2|D0|N0|M1|Z0)]`
· Densidade `[(T1|U71|P2|D0|N0|M1|Z0)]`.

---

## 4. Fórmulas (idênticas à planilha)

```
Comprimento     = Axis3D.Length                                            (H)
LarguraExterna  = 2*Parede + Largura                                       (L = 2e+b)
LarguraVala     = If(B<=0.4, 0.8, If(B>0.8, B+0.4, B+0.6))   [B=LarguraExterna]  (M)
ProfValaMont    = (terrenoMont - FITmont) + Parede + EspConcMagro          (O = D-E+K+N)
ProfValaJus     = (terrenoJus  - FITjus ) + Parede + EspConcMagro          (P = F-G+K+N)
SecValaMont     = If(O<=1.25, M*O, (M+O)*O)                                (Q)
SecValaJus      = If(P<=1.25, M*P, P*(M+P))                                (R)
AlturaMedia     = ((D-E+K) + (F-G+K)) / 2                                  (S = Hmed)
AreaApiloamento = Comprimento * LarguraVala                               (T = H*M)
VolEscav        = ((SecValaMont+SecValaJus)/2) * Comprimento              (U)
VolConcMagro    = (LarguraExterna+0.1) * EspConcMagro * Comprimento       (V)
VolCanaleta     = AlturaMedia * LarguraExterna * Comprimento              (W, bruto)
VolReaterro     = VolEscav - VolConcMagro - VolCanaleta                   (X)
VolBotaFora     = VolEscav - VolConcMagro - VolReaterro                   (Y = VolCanaleta)
MassaBotaFora   = VolBotaFora * MassaEspAdotada                           (AA)
VolConcCanaleta = ((L*S) - (b*(S-K))) * Comprimento                       (AB, líquido)
MassaAco        = VolConcCanaleta * TaxaAco                               (AD)
AreaFormas      = (S*H*2) + ((S-K)*H*2)                                   (AE)
TaxaFormas      = AreaFormas / VolConcCanaleta   (com guarda /0)          (AF)
```

Observações:
- `VolBotaFora` é algebricamente igual a `VolCanaleta` (a planilha calcula pelo caminho longo).
- A planilha **não** trava `VolReaterro` em ≥0; em vala muito rasa ele pode ficar negativo.
  Se quiser o comportamento do tubo (`VolBotaFora = If(VolEscav-VolReaterro<0,0,…)`), avise.
- `VALA TRAPEZ.` é outro dispositivo (vala trapezoidal, não canaleta) — fora deste pacote.
