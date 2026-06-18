# Regenera SOLIDOS_QTO.cuix trocando SO os 2 .cui (MenuGroup/RibbonRoot), copiando os
# demais entries byte-a-byte (preserva estrutura OPC exata). Invocar via UTF-8.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName WindowsBase

$cuix = Join-Path (Get-Location).Path 'SOLIDOS_QTO.cuix'
$bak  = $cuix + '.bak'
if (-not (Test-Path -LiteralPath $bak)) { Copy-Item -LiteralPath $cuix -Destination $bak -Force }
# Fonte limpa = backup do original
Copy-Item -LiteralPath $bak -Destination $cuix -Force

# ---- Tabela painel -> botoes ----
$paineis = @(
  @{ Nome="Dimensionamento"; Botoes=@(
      @{L="Dimensionar`nJusante"; C="SOL_DIMENSIONAR_REDE_POR_JUSANTE"; T="Dimensiona a rede por gravidade no sentido de jusante (modo simples)."; I="IconeJusantepng.png"}
      @{L="Diagnosticar`nRede";   C="SOL_DIAGNOSTICAR_CONECTIVIDADE";   T="Diagnostica a conectividade da rede de drenagem SOLIDOS.";            I="SADJUST.png"}
  )}
  @{ Nome="Quantitativo Geral"; Botoes=@(
      @{L="QUANT.`nGERAL"; C="SOL_QUANT_GERAL"; T="TUDO de uma vez: gera o QUANTITATIVO (memoria) e o FORMULARIO SMEC de todos os dispositivos."; I="SBUILDER.png"}
  )}
  @{ Nome="Quantitativos (individual)"; Botoes=@(
      @{L="Quant.`nTubos";       C="SOL_QUANT_TUBOS";       T="Quantitativos dos tubos da rede (memoria).";                       I="SBUILDER.png"}
      @{L="Quant. Tubos`nSMEC";  C="SOL_QUANT_TUBOS_SMEC";  T="Tubos no padrao SMEC (FORMULARIO).";                              I="SBUILDER.png"}
      @{L="Quant.`nCaixas";      C="SOL_QUANT_CAIXAS";      T="Quantitativos das caixas (memoria).";                             I="SBUILDER.png"}
      @{L="Quant.`nCanaletas";   C="SOL_QUANT_CANAL";       T="Quantitativos das canaletas (memoria: CANALET_Pluv/Cont/Oleo)."; I="SBUILDER.png"}
      @{L="Quant. Canal`nSMEC";  C="SOL_QUANT_CANAL_SMEC";  T="Canaletas no padrao SMEC (FORMULARIO, familia CANAIS E CANALETAS)."; I="SBUILDER.png"}
      @{L="Tubos`nFantasmas";    C="SOL_LISTAR_TUBOS_FANTASMAS"; T="Lista tubos sem conexao / orfaos (fantasmas).";              I="SBUILDER.png"}
      @{L="Listar`nPropriedades";C="SOL_LISTAR_PROPS";      T="Lista as propriedades dinamicas do dispositivo selecionado.";    I="SBUILDER.png"}
  )}
  @{ Nome="QTO em Dispositivos"; Botoes=@(
      @{L="QTO`nCanaleta";        C="AddQtoSmecCanaleta";            T="Insere o pacote QTO SMEC CANALETA num .sbd de canaleta."; I="SRECON.png"}
      @{L="QTO Caixas`n(Lote)";   C="AddQtoSmecEmLote";              T="Padroniza a sequência QTO SMEC em lote nos .sbd.";         I="SRECON.png"}
      @{L="Variáveis`nGlobais Caixa"; C="AddVariaveisGlobaisQtoSmecCaixa"; T="Cria as DynamicProperties de saída faltantes numa caixa."; I="SRECON.png"}
  )}
  @{ Nome="Seções / Projeção"; Botoes=@(
      @{L="Seção`nBueiro";       C="SOL_SECAO_BUEIRO";      T="Gera seção + projeção de um bueiro.";                I="IFC.png"}
      @{L="Seções`nBueiros";     C="SOL_SECAO_BUEIROS";     T="Gera seções + projeção de vários bueiros (lote).";  I="IFC.png"}
      @{L="Spike`nProjeção";     C="SOL_SPIKE_PROJECAO";    T="Spike de projeção em section view.";                 I="IFC.png"}
      @{L="Spike`nProjeção PF";  C="SOL_SPIKE_PROJECAO_PF"; T="Variante PF do spike de projeção.";                  I="IFC.png"}
  )}
  @{ Nome="Diagnóstico"; Botoes=@(
      @{L="Dump XML`n(Arquivo)"; C="DumpSolidosXml";        T="Despeja os XRecords SOLIDOS de um .sbd num .txt."; I="SIFCEXPORT.png"}
      @{L="Dump XML`n(Ativo)";   C="DumpSolidosXmlAtivo";   T="Dump dos XRecords SOLIDOS do desenho ativo.";       I="SIFCEXPORT.png"}
      @{L="Dump XML`n(Forçado)"; C="DumpSolidosXmlForcado"; T="Dump forçado dos XRecords SOLIDOS.";                I="SIFCEXPORT.png"}
  )}
)

$script:uidN = 0x21000
function NextUid([string]$prefix) { $h=('{0:X}' -f $script:uidN); $script:uidN++; return "${prefix}_251_$h" }

foreach ($p in $paineis) {
  $p.UidPanel = NextUid 'RBNU'; $p.UidPanelName = NextUid 'XLS'; $p.UidRow = NextUid 'RBNU'
  foreach ($b in $p.Botoes) {
    $b.Mmu=NextUid 'MMU'; $b.XName=NextUid 'XLS'; $b.XHelp=NextUid 'XLS'
    $b.XCli=NextUid 'XLS'; $b.Rbtn=NextUid 'RBNU'; $b.XTip=NextUid 'XLS'
    $b.OneLine = ($b.L -replace "`n"," ")
  }
}
$NL = "`r`n"

$mg = New-Object System.Text.StringBuilder
[void]$mg.Append('<?xml version="1.0" encoding="utf-8"?>' + $NL)
[void]$mg.Append('<!-- SOLIDOS QTO (SOL_QUANT_GERAL + canaletas). -->' + $NL)
[void]$mg.Append('<MenuGroup xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" Name="SOLIDOS_QTO_3DIAS" DisplayName="SOLIDOS_QTO_3DIAS">' + $NL)
[void]$mg.Append('  <MacroGroup Name="NEWGROUP">' + $NL)
foreach ($p in $paineis) { foreach ($b in $p.Botoes) {
  [void]$mg.Append("    <MenuMacro UID=`"$($b.Mmu)`">" + $NL)
  [void]$mg.Append('      <Macro type="Any">' + $NL)
  [void]$mg.Append('        <Revision MajorVersion="16" MinorVersion="2" UserVersion="1" />' + $NL)
  [void]$mg.Append('        <ModifiedRev MajorVersion="25" MinorVersion="1" UserVersion="1" />' + $NL)
  [void]$mg.Append("        <Name xlate=`"true`" UID=`"$($b.XName)`">$($b.OneLine)</Name>" + $NL)
  [void]$mg.Append("        <Command>^C^C_$($b.C)</Command>" + $NL)
  [void]$mg.Append("        <HelpString xlate=`"true`" UID=`"$($b.XHelp)`">$($b.T)</HelpString>" + $NL)
  [void]$mg.Append("        <SmallImage Name=`"$($b.I)`" />" + $NL)
  [void]$mg.Append("        <LargeImage Name=`"$($b.I)`" />" + $NL)
  [void]$mg.Append("        <CLICommand xlate=`"true`" UID=`"$($b.XCli)`">$($b.OneLine)</CLICommand>" + $NL)
  [void]$mg.Append('      </Macro>' + $NL)
  [void]$mg.Append('    </MenuMacro>' + $NL)
}}
[void]$mg.Append('  </MacroGroup>' + $NL + '</MenuGroup>' + $NL)

$rb = New-Object System.Text.StringBuilder
[void]$rb.Append('<?xml version="1.0" encoding="utf-8"?>' + $NL)
[void]$rb.Append('<!-- SOLIDOS QTO. -->' + $NL)
[void]$rb.Append('<RibbonRoot>' + $NL)
[void]$rb.Append('  <RibbonPanelSourceCollection xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">' + $NL)
foreach ($p in $paineis) {
  [void]$rb.Append("    <RibbonPanelSource UID=`"$($p.UidPanel)`" Text=`"$($p.Nome)`" HiddenInEditor=`"false`">" + $NL)
  [void]$rb.Append('      <ModifiedRev MajorVersion="25" MinorVersion="1" UserVersion="1" />' + $NL)
  [void]$rb.Append("      <Name xlate=`"true`" UID=`"$($p.UidPanelName)`">$($p.Nome)</Name>" + $NL)
  [void]$rb.Append("      <RibbonRow UID=`"$($p.UidRow)`">" + $NL)
  [void]$rb.Append('        <ModifiedRev MajorVersion="25" MinorVersion="1" UserVersion="1" />' + $NL)
  foreach ($b in $p.Botoes) {
    $txt = ($b.L -replace "`n", $NL)
    [void]$rb.Append("        <RibbonCommandButton UID=`"$($b.Rbtn)`" Id=`"AcRibbonCommandButton`" Text=`"$txt`" ButtonStyle=`"LargeWithText`" MenuMacroID=`"$($b.Mmu)`" KeyTip=`"`">" + $NL)
    [void]$rb.Append("          <TooltipTitle xlate=`"true`" UID=`"$($b.XTip)`">$($b.OneLine)</TooltipTitle>" + $NL)
    [void]$rb.Append('          <ModifiedRev MajorVersion="25" MinorVersion="1" UserVersion="1" />' + $NL)
    [void]$rb.Append('        </RibbonCommandButton>' + $NL)
  }
  [void]$rb.Append('      </RibbonRow>' + $NL + '    </RibbonPanelSource>' + $NL)
}
[void]$rb.Append('  </RibbonPanelSourceCollection>' + $NL)
[void]$rb.Append('  <RibbonTabSourceCollection>' + $NL)
$tabUid = NextUid 'RBNU'; $tabName = NextUid 'XLS'
[void]$rb.Append("    <RibbonTabSource Text=`"SOLIDOS QTO`" UID=`"$tabUid`" WorkspaceBehavior=`"AddTabOnly`">" + $NL)
[void]$rb.Append('      <ModifiedRev MajorVersion="25" MinorVersion="1" UserVersion="1" />' + $NL)
[void]$rb.Append("      <Name xlate=`"true`" UID=`"$tabName`">SOLIDOS QTO</Name>" + $NL)
foreach ($p in $paineis) {
  $refUid = NextUid 'RBNU'
  [void]$rb.Append("      <RibbonPanelSourceReference UID=`"$refUid`" PanelId=`"$($p.UidPanel)`" ResizeStyle=`"NoCollapse`">" + $NL)
  [void]$rb.Append('        <ModifiedRev MajorVersion="25" MinorVersion="1" UserVersion="1" />' + $NL)
  [void]$rb.Append('      </RibbonPanelSourceReference>' + $NL)
}
[void]$rb.Append('    </RibbonTabSource>' + $NL + '  </RibbonTabSourceCollection>' + $NL + '</RibbonRoot>' + $NL)

# valida XML
[void][xml]$mg.ToString(); [void][xml]$rb.ToString()
Write-Output "XML valido."

$enc = New-Object System.Text.UTF8Encoding($false)
$mgBytes = $enc.GetBytes($mg.ToString())
$rbBytes = $enc.GetBytes($rb.ToString())

# ---- zip-para-zip: copia entries, troca os 2 .cui ----
$out = Join-Path $env:TEMP ("SQ_" + [guid]::NewGuid().ToString("N") + ".cuix")
$src = [System.IO.Compression.ZipFile]::OpenRead($cuix)
$dst = [System.IO.Compression.ZipFile]::Open($out, [System.IO.Compression.ZipArchiveMode]::Create)
foreach ($e in $src.Entries) {
  $ne = $dst.CreateEntry($e.FullName)
  $os = $ne.Open()
  if ($e.FullName -eq 'MenuGroup.cui') { $os.Write($mgBytes,0,$mgBytes.Length) }
  elseif ($e.FullName -eq 'RibbonRoot.cui') { $os.Write($rbBytes,0,$rbBytes.Length) }
  else { $is = $e.Open(); $is.CopyTo($os); $is.Dispose() }
  $os.Dispose()
}
$src.Dispose(); $dst.Dispose()

# valida OPC
$pkg = [System.IO.Packaging.Package]::Open($out, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read); $pkg.Close()
Write-Output "OPC valido."

Copy-Item -LiteralPath $out -Destination $cuix -Force
Remove-Item -LiteralPath $out -Force
Write-Output "OK -> $cuix"
