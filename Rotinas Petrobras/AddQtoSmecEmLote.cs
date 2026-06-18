using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace RotinasPetrobras.Diagnostics
{
    /// <summary>
    /// AddQtoSmecEmLote: padroniza a sequência "QTO SMEC" (variáveis globais + definições)
    /// em LOTE nos .sbd das pastas PETROBRAS CAIXAS e PETROBRAS RALOS.
    ///
    /// Decisões aplicadas (combinadas com o usuário):
    ///  - Ralos cilíndricos: subconjunto seguro (SolidVolume + VolCA via sólido de concreto
    ///    existente; SEM VolParedes/VolLajes/VolCM).
    ///  - Blocos legados: remove a(s) sequência(s) "QTO SMEC" existentes (consolida) e
    ///    remove SetOutPutParam legados (VolumeConcreto/VolumeConcretoArmado/VolumeConcretoMagro).
    ///  - AlturaEscav = Altura+AltLaje+AltPiso+0.05 (NÃO usa RimElevation).
    ///
    /// SEGURANÇA (não dá pra testar runtime daqui):
    ///  - Faz BACKUP de cada .sbd antes de gravar.
    ///  - VALIDA: só emite uma fórmula se TODOS os sólidos/propriedades que ela referencia
    ///    existem de fato naquele .sbd; senão PULA e registra no log (não grava fórmula quebrada).
    ///  - Não remove SetOutPutParam de geometria (LarguraExterna/ComprimentoExterno/Altura/etc.)
    ///    que estejam FORA de uma sequência "QTO SMEC".
    ///  - Gera um .log.txt por arquivo com tudo que foi removido/emitido/pulado.
    ///
    /// Config por DISPOSITIVO é chaveada pelo NOME DO ARQUIVO (sem extensão) — ver CONFIG.
    /// O mapeamento de sólidos veio da análise do dump master (solidos_xrecord_dump.txt).
    /// REVISE a tabela CONFIG: é onde estão os nomes de sólido por dispositivo.
    /// </summary>
    public class AddQtoSmecEmLote
    {
        // ===================== CONFIG POR DISPOSITIVO =====================
        // Chave = nome do arquivo .sbd sem extensão (como está em disco, com "_barra_").
        // Expressões já COM ".Volume". Para retangulares: Walls/LajesNet/LajesGross/Cm.
        // Para cilíndricos: Proxy (concreto) e Cylindrical=true (só SolidVolume/VolCA/VolCM=0).
        private sealed class DevCfg
        {
            public string Walls;       // VolParedes
            public string LajesNet;    // VolLajes (líquidas) -> VolCA
            public string LajesGross;  // parte de laje do SolidVolume (bruta); se null usa LajesNet
            public string Cm;          // VolCM (sólido concreto magro); null => VolCM=0
            public string Proxy;       // cilíndrico: SolidVolume/VolCA = Proxy; null => retangular
            public bool Cylindrical;
            public string Note;
        }

        private static readonly Dictionary<string, DevCfg> CONFIG =
            new Dictionary<string, DevCfg>(StringComparer.OrdinalIgnoreCase)
        {
            // ---- CAIXAS COLETORA (retangulares, full) ----
            ["CAIXA COLETORA OLEOSO"] = new DevCfg { Walls="joinCorpoSepto.Volume", LajesNet="SubtractLaje.Volume+extrPiso.Volume", LajesGross="LAJESUPERIOR.Volume+LAJEINFERIOR.Volume", Cm="extConcretoMagro.Volume" },
            ["CAIXA COLETORA CONTAMINADO"] = new DevCfg { Walls="joinCorpoSepto.Volume", LajesNet="SubtractLaje.Volume+extrPiso.Volume", LajesGross="LAJESUPERIOR.Volume+LAJEINFERIOR.Volume", Cm="extConcretoMagro.Volume" },

            // ---- CAIXAS DE AREIA (retangulares, full) ----
            ["CAIXA DE AREIA GRELHA"] = new DevCfg { Walls="SubtracaoCorpo.Volume", LajesNet="SubtractLaje.Volume+extrPiso.Volume", LajesGross="LAJESUPERIOR.Volume+LAJEINFERIOR.Volume", Cm="extConcretoMagro.Volume" },
            ["CAIXA DE AREIA TAMPA"] = new DevCfg { Walls="SubtracaoCorpo.Volume", LajesNet="SubtractLaje.Volume+extrPiso.Volume", LajesGross="LAJESUPERIOR.Volume+LAJEINFERIOR.Volume", Cm="extConcretoMagro.Volume" },

            // ---- CAIXAS DE PASSAGEM (retangulares) ----
            ["PASSAGEM SIMPLES"] = new DevCfg { Walls="SubtracaoCorpo.Volume", LajesNet="SubtractLaje.Volume+extrPiso.Volume", LajesGross="LAJESUPERIOR.Volume+LAJEINFERIOR.Volume", Cm="extConcretoMagro.Volume" },
            ["PASSAGEM COM SELO HÍDRICO"] = new DevCfg { Walls="SubtracaoCorpo.Volume", LajesNet="SubtractLaje.Volume+extrPiso.Volume", LajesGross="LAJESUPERIOR.Volume+LAJEINFERIOR.Volume", Cm="extConcretoMagro.Volume" },
            ["PASSAGEM SIMPLES TAMPA CENTRAL CONCRETO"] = new DevCfg { Walls="SubtracaoCorpo.Volume", LajesNet="SubtractLaje.Volume+extrPiso.Volume", LajesGross="LAJESUPERIOR.Volume+LAJEINFERIOR.Volume", Cm="extConcretoMagro.Volume" },
            ["PASSAGEM SIMPLES TAMPA CENTRAL GRELHA"] = new DevCfg { Walls="SubtracaoCorpo.Volume", LajesNet="SubtractLaje.Volume+extrPiso.Volume", LajesGross="LAJESUPERIOR.Volume+LAJEINFERIOR.Volume", Cm="extConcretoMagro.Volume" },
            ["PASSAGEM COM SEPTO E SELO HÍDRICO"] = new DevCfg { Walls="joinCorpoSepto.Volume", LajesNet="SubtractLaje.Volume+extrPiso.Volume", LajesGross="LAJESUPERIOR.Volume+LAJEINFERIOR.Volume", Cm="extConcretoMagro.Volume" },
            // pescoço/chaminé entra nas paredes (concreto)
            ["PASSAGEM SIMPLES C_barra_ PESCOÇO"] = new DevCfg { Walls="Corpo.Volume+Chamine.Volume", LajesNet="LAJESUPERIOR.Volume+LAJEINFERIOR.Volume", LajesGross="LAJESUPERIOR.Volume+LAJEINFERIOR.Volume", Cm="extConcretoMagro.Volume", Note="pescoço incluso nas paredes; confirmar casing Chamine" },
            ["PASSAGEM COM SEPTO E SELO HÍDRICO C_barra_ PESCOÇO"] = new DevCfg { Walls="joinCorpoSepto.Volume+solChamine.Volume+solChamine2.Volume", LajesNet="solLaje.Volume+solPiso.Volume", LajesGross="solLaje.Volume+solPiso.Volume", Cm="extConcretoMagro.Volume", Note="MÉDIA confiança: septo+pescoço" },

            // ---- CAIXAS DE VÁLVULA ----
            ["CAIXA DE VALVULA"] = new DevCfg { Walls="CORPOCOMSEPTO.Volume", LajesNet="LAJESUPERIOR.Volume", LajesGross="LAJESUPERIOR.Volume", Cm="CONCRETOMAGRO.Volume", Note="MÉDIA: piso pode estar embutido no corpo; sem TaxaArmadura" },
            ["CAIXA DE VALVULA 1C"] = new DevCfg { Walls="CORPO.Volume", LajesNet="LAJEINFERIOR.Volume+LAJESUPERIOR.Volume", LajesGross="LAJEINFERIOR.Volume+LAJESUPERIOR.Volume", Cm="CONCRETOMAGRO.Volume" },
            ["ETE COMPACTA"] = new DevCfg { Walls="CORPO.Volume", LajesNet="LAJESUPERIOR.Volume+LAJEINFERIOR.Volume", LajesGross="LAJESUPERIOR.Volume+LAJEINFERIOR.Volume", Cm="CONCRETOMAGRO.Volume", Note="MÉDIA: avaliar Piso1; sem TaxaArmadura/terraplenagem" },

            // ---- RALO retangular (full, sem concreto magro) ----
            ["CAIXA RALO - S_barra_ SELO HIDRICO"] = new DevCfg { Walls="SubtracaoCorpo.Volume", LajesNet="extrLaje.Volume+extrPiso.Volume", LajesGross="extrLaje.Volume+extrPiso.Volume", Cm=null, Note="sem concreto magro (VolCM=0)" },

            // ---- RALOS CILÍNDRICOS (subconjunto seguro) ----
            ["CANALETA DE AREA CONTIDA DE BOMBAS - RALO"] = new DevCfg { Cylindrical=true, Proxy="SUBVOLUMECONCRETO.Volume", Note="proxy concreto = SUBVOLUMECONCRETO" },
            ["RALO - SIMPLES DE PISO"] = new DevCfg { Cylindrical=true, Proxy="BASERALOFINAL.Volume", Note="proxy = BASERALOFINAL" },
            ["CAIXA RALO - SELO HIDRICO C_barra_ FLANGE"] = new DevCfg { Cylindrical=true, Proxy="SbCaixaFinal.Volume", Note="BAIXA confiança: proxy SbCaixaFinal" },
            ["CAIXA RALO - SELO HIDRICO C_barra_ BOLSA"] = new DevCfg { Cylindrical=true, Proxy=null, Note="BAIXA: sem sólido de concreto claro — concreto será PULADO" },
            ["TERMINAL DE LIMPEZA"] = new DevCfg { Cylindrical=true, Proxy=null, Note="BAIXA: sem sólido de concreto claro — concreto será PULADO" },
        };

        // ===================== OUTPUTS QTO SMEC (ordem + unidade) =====================
        private enum U { Dist, Area, Vol, Massa, Dens }

        private sealed class OutSpec
        {
            public string Name;
            public U Unit;
            public string Display;
            public Func<DevCfg, string> Formula;   // gera a expressão; null => não aplicável p/ esse device
        }

        // Fórmulas genéricas (decisão: AlturaEscav usa +0.05, não RimElevation)
        private static readonly List<OutSpec> OUTPUTS = new List<OutSpec>
        {
            new OutSpec{ Name="AreaFormas", Unit=U.Area, Display="Área de Fôrmas",
                Formula=c=>"4*((Altura+AltLaje+AltPiso)*Comprimento+(Altura+AltLaje+AltPiso)*Largura)" },
            new OutSpec{ Name="SolidVolume", Unit=U.Vol, Display="Volume do Sólido",
                Formula=c=> c.Cylindrical ? c.Proxy : Combine(c.Walls, c.LajesGross ?? c.LajesNet) },
            new OutSpec{ Name="VolCA", Unit=U.Vol, Display="Volume Concreto Armado",
                Formula=c=> c.Cylindrical ? c.Proxy : Combine(c.Walls, c.LajesNet) },
            new OutSpec{ Name="VolParedes", Unit=U.Vol, Display="Volume Paredes",
                Formula=c=> c.Cylindrical ? null : c.Walls },
            new OutSpec{ Name="VolLajes", Unit=U.Vol, Display="Volume Lajes",
                Formula=c=> c.Cylindrical ? null : c.LajesNet },
            new OutSpec{ Name="VolCM", Unit=U.Vol, Display="Volume Concreto Magro",
                Formula=c=> c.Cylindrical ? "0" : (c.Cm ?? "0") },
            new OutSpec{ Name="QuantAco", Unit=U.Massa, Display="Quantidade de Aço",
                Formula=c=>"VolCA*TaxaArmadura" },
            new OutSpec{ Name="TrenchHeight", Unit=U.Dist, Display="Altura da Vala (corte sup.)",
                Formula=c=>"Altura+AltPiso" },
            new OutSpec{ Name="LarguraExterna", Unit=U.Dist, Display="Largura Externa",
                Formula=c=>"Largura+Parede*2" },
            new OutSpec{ Name="ComprimentoExterno", Unit=U.Dist, Display="Comprimento Externo",
                Formula=c=>"Comprimento+Parede*2" },
            new OutSpec{ Name="AlturaEscav", Unit=U.Dist, Display="Altura de Escavação",
                Formula=c=>"Altura+AltLaje+AltPiso+0.05" },
            new OutSpec{ Name="LargVala1", Unit=U.Dist, Display="Largura Vala 1",
                Formula=c=>"ComprimentoExterno+0.4" },
            new OutSpec{ Name="LargVala2", Unit=U.Dist, Display="Largura Vala 2",
                Formula=c=>"LarguraExterna+0.4" },
            new OutSpec{ Name="VolEscav", Unit=U.Vol, Display="Volume de Escavação",
                Formula=c=>"LargVala1*LargVala2*AlturaEscav" },
            new OutSpec{ Name="AreaApiloamento", Unit=U.Area, Display="Área de Apiloamento",
                Formula=c=>"LargVala1*LargVala2" },
            new OutSpec{ Name="VolReaterro", Unit=U.Vol, Display="Volume de Reaterro",
                Formula=c=>"VolEscav-(ComprimentoExterno*LarguraExterna*AlturaEscav)-VolCM" },
            new OutSpec{ Name="VolBotaFora", Unit=U.Vol, Display="Volume Bota-Fora",
                Formula=c=>"VolEscav-VolReaterro" },
            new OutSpec{ Name="MassaEspAdotada", Unit=U.Dens, Display="Massa Específica Adotada",
                Formula=c=>"1.8" },
            new OutSpec{ Name="MassaBotaFora", Unit=U.Massa, Display="Massa Bota-Fora",
                Formula=c=>"VolBotaFora*MassaEspAdotada" },
        };

        private static string Combine(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;
            return a + "+" + b;
        }

        // Tokens que NÃO precisam existir como propriedade/sólido (built-ins/membros)
        private static readonly HashSet<string> BUILTINS = new HashSet<string>(StringComparer.Ordinal)
        {
            "Volume","X","Y","Z","If","Math","Max","Min","Abs","Sqrt","Pow","PI","Round",
            "True","False","Count","Points","Length","Area"
        };

        // SetOutPutParam que podem ser removidos com segurança onde quer que estejam (saídas
        // puramente quantitativas — NUNCA geometria). Consolida duplicatas de blocos antigos.
        // NÃO inclui LarguraExterna/ComprimentoExterno/SolidVolume/TrenchHeight/AlturaEscav/
        // LargVala* (podem ser usados por geometria/IFC; a nova sequência os reescreve mesmo).
        private static readonly HashSet<string> REMOVABLE_STANDALONE = new HashSet<string>(StringComparer.Ordinal)
        {
            "VolumeConcreto", "VolumeConcretoArmado", "VolumeConcretoMagro",
            "VolCA", "VolParedes", "VolLajes", "VolCM", "AreaFormas", "QuantAco",
            "VolEscav", "AreaApiloamento", "VolReaterro", "VolBotaFora", "MassaBotaFora", "MassaEspAdotada"
        };

        // Unidades
        private static (string conv, string macro) UnitInfo(U u) => u switch
        {
            U.Dist  => ("SOLIDOS.UnidadeDistancia", "[(T1|U9|P2|D0|N0|M1|Z0)]"),
            U.Area  => ("SOLIDOS.UnidadeArea",      "[(T1|U38|P2|D0|N0|M1|Z0)]"),
            U.Vol   => ("SOLIDOS.UnidadeVolume",    "[(T1|U15|P2|D0|N0|M1|Z0)]"),
            U.Massa => ("SOLIDOS.UnidadeMassa",     "[(T1|U126|P2|D0|N0|M1|Z0)]"),
            U.Dens  => ("SOLIDOS.UnidadeDensidade", "[(T1|U71|P2|D0|N0|M1|Z0)]"),
            _       => ("SOLIDOS.UnidadeVolume",    "[(T1|U15|P2|D0|N0|M1|Z0)]"),
        };

        // ===================== COMANDO =====================
        [CommandMethod("AddQtoSmecEmLote", CommandFlags.Session)]
        public void Run()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            // Modo: 1 arquivo (teste) ou pasta inteira
            var pko = new PromptKeywordOptions("\nProcessar [Arquivo único/Pasta]?")
            { AllowNone = false };
            pko.Keywords.Add("Arquivo");
            pko.Keywords.Add("Pasta");
            pko.Keywords.Default = "Arquivo";
            var rk = ed.GetKeywords(pko);
            if (rk.Status != PromptStatus.OK) return;
            bool pastaInteira = rk.StringResult == "Pasta";

            var arquivos = new List<string>();
            if (!pastaInteira)
            {
                var pfo = new PromptOpenFileOptions("\nSelecione UM .sbd para testar")
                {
                    Filter = "SOLIDOS Builder (*.sbd;*.dwg)|*.sbd;*.dwg|Todos (*.*)|*.*"
                };
                var fr = ed.GetFileNameForOpen(pfo);
                if (fr.Status != PromptStatus.OK) return;
                arquivos.Add(fr.StringResult);
            }
            else
            {
                var pso = new PromptStringOptions("\nCaminho da pasta (varre *.sbd recursivamente): ")
                { AllowSpaces = true };
                var sr = ed.GetString(pso);
                if (sr.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(sr.StringResult)) return;
                string dir = sr.StringResult.Trim().Trim('"');
                if (!Directory.Exists(dir)) { ed.WriteMessage($"\nPasta não existe: {dir}"); return; }
                arquivos.AddRange(Directory.GetFiles(dir, "*.sbd", SearchOption.AllDirectories));
                if (arquivos.Count == 0) { ed.WriteMessage("\nNenhum .sbd encontrado."); return; }
                ed.WriteMessage($"\n{arquivos.Count} arquivo(s) .sbd encontrado(s).");
            }

            int okCount = 0, skipCount = 0, errCount = 0;
            foreach (var path in arquivos)
            {
                try
                {
                    string nome = Path.GetFileNameWithoutExtension(path);
                    if (!CONFIG.TryGetValue(nome, out var cfg))
                    {
                        ed.WriteMessage($"\n[PULADO] '{nome}': sem config (não é alvo). ");
                        skipCount++;
                        continue;
                    }
                    string resumo = ProcessFile(path, cfg);
                    ed.WriteMessage($"\n[OK] {nome}: {resumo}");
                    okCount++;
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[ERRO] {Path.GetFileName(path)}: {ex.Message}");
                    errCount++;
                }
            }
            ed.WriteMessage($"\n\n=== FIM: {okCount} ok, {skipCount} pulado(s), {errCount} erro(s). Veja os .log.txt por arquivo. ===");
        }

        // ===================== PROCESSA UM ARQUIVO =====================
        private string ProcessFile(string path, DevCfg cfg)
        {
            var log = new List<string>();
            string nome = Path.GetFileNameWithoutExtension(path);
            log.Add($"== {nome} ==  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            if (!string.IsNullOrEmpty(cfg.Note)) log.Add($"NOTA config: {cfg.Note}");

            // Backup
            string backup = path + $".backup_{DateTime.Now:yyyyMMdd_HHmmss}";
            File.Copy(path, backup, overwrite: false);
            log.Add($"Backup: {backup}");

            int emitted = 0, skipped = 0, propsAdded = 0, removedGroups = 0;

            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(path, FileShare.Read, true, "");
                db.CloseInput(true);

                using (var t = db.TransactionManager.StartTransaction())
                {
                    var nod = (DBDictionary)t.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                    DBDictionary solidos = null;
                    foreach (DBDictionaryEntry e in nod)
                        if (e.Key.StartsWith("SOLIDOS") && t.GetObject(e.Value, OpenMode.ForRead) is DBDictionary d)
                        { solidos = d; break; }
                    if (solidos == null) throw new System.Exception("NOD/SOLIDOS_ não encontrado");

                    Xrecord ctor = FindConstructor(solidos, t, nome, log);
                    if (ctor == null) throw new System.Exception("Constructor não encontrado");

                    var v = new List<TypedValue>();
                    foreach (TypedValue tv in ctor.Data) v.Add(tv);

                    // Conjunto de tokens existentes (props + sólidos)
                    var tokens = CollectTokens(v);
                    log.Add($"Tokens detectados: {tokens.Count}");

                    // ---- Activities bounds ----
                    int actMarker = FindMarker(v, "Activities|SOLIDOS.ListActivity");
                    if (actMarker < 0) throw new System.Exception("Marcador Activities não encontrado");
                    int actListEnd = FindListEnd(v, actMarker + 1);

                    // ---- Parse grupos dentro de Activities ----
                    var grupos = ExtrairGrupos(v, actMarker + 1, actListEnd);

                    // ---- Remover QTO SMEC existente(s) + legados ----
                    int reuseParent = -1, reuseSeqIdx = -1;
                    var removeSet = new HashSet<int>(); // índices de grupo a remover
                    var qtoSeqIds = new List<int>();
                    foreach (var g in grupos)
                        if (g.GroupName == "SOLIDOS.ActivitySequence" &&
                            string.Equals(g.DisplayName, "QTO SMEC", StringComparison.OrdinalIgnoreCase))
                        { qtoSeqIds.Add(g.Id); if (reuseParent < 0) { reuseParent = g.ParentId; reuseSeqIdx = g.SequenceIndex; } }

                    for (int i = 0; i < grupos.Count; i++)
                    {
                        var g = grupos[i];
                        bool rem = false;
                        if (g.GroupName == "SOLIDOS.ActivitySequence" &&
                            string.Equals(g.DisplayName, "QTO SMEC", StringComparison.OrdinalIgnoreCase)) rem = true;
                        else if (qtoSeqIds.Contains(g.ParentId)) rem = true; // filho de QTO SMEC
                        else if (g.GroupName == "SOLIDOS.ActivitySetOutPutParam" &&
                                 g.PropName != null && REMOVABLE_STANDALONE.Contains(g.PropName)) rem = true; // QTO duplicado/legado solto
                        if (rem) { removeSet.Add(i); removedGroups++; log.Add($"REMOVE grupo {g.GroupName} id={g.Id} '{g.DisplayName ?? g.PropName}'"); }
                    }

                    // Reconstrói lista de TypedValues sem os grupos removidos
                    var newVals = RebuildWithout(v, actMarker + 1, actListEnd, grupos, removeSet);
                    // recomputa fim da lista após remoção
                    int actListEnd2 = FindListEnd(newVals, FindMarker(newVals, "Activities|SOLIDOS.ListActivity") + 1);

                    // ---- ids/sequenceindex p/ novo bloco ----
                    int maxId = FindMaxId(newVals);
                    int parentForSeq = reuseParent >= 0 ? reuseParent : InferRootSeqParent(grupos);
                    int seqIdxForSeq = reuseSeqIdx >= 0 ? reuseSeqIdx : (MaxRootSeqIndex(grupos, parentForSeq) + 1);
                    log.Add($"Nova sequência: parentid={parentForSeq} sequenceindex={seqIdxForSeq} (reuso={(reuseParent>=0)})");

                    int idCursor = maxId + 1;
                    int seqId = idCursor++;
                    int boxId = idCursor++;

                    // ---- decide quais outputs emitir (validação de tokens + dependências) ----
                    var emittedNames = new HashSet<string>(StringComparer.Ordinal);
                    var blocos = new List<TypedValue>();
                    blocos.AddRange(BuildSequence(seqId, parentForSeq, seqIdxForSeq, "QTO SMEC"));
                    blocos.AddRange(BuildSequenceBox(boxId, seqId, "QTO SMEC"));

                    int sIdx = 0;
                    foreach (var spec in OUTPUTS)
                    {
                        string formula = spec.Formula(cfg);
                        if (formula == null) { skipped++; log.Add($"PULA {spec.Name}: não aplicável p/ este device"); continue; }

                        // tokens da fórmula que precisam existir (props/sólidos), menos outputs já emitidos e builtins
                        var faltando = MissingTokens(formula, tokens, emittedNames);
                        if (faltando.Count > 0)
                        {
                            skipped++;
                            log.Add($"PULA {spec.Name} = {formula}  -> falta: {string.Join(",", faltando)}");
                            continue;
                        }

                        blocos.AddRange(BuildSetOutPutParam(spec.Name, formula, seqId, idCursor++, sIdx, $"20,{30 + sIdx * 30}"));
                        emittedNames.Add(spec.Name);
                        emitted++; sIdx++;
                        log.Add($"EMITE {spec.Name} = {formula}");
                    }

                    // insere novo bloco no fim da lista Activities
                    newVals.InsertRange(actListEnd2, blocos);

                    // ---- garante DynamicProperty (variável global) p/ cada output emitido ----
                    int propMarker = FindMarker(newVals, "Properties|SOLIDOS.ListDynamicProperty");
                    if (propMarker < 0) throw new System.Exception("Marcador Properties não encontrado");
                    int propEnd = FindListEnd(newVals, propMarker + 1);
                    var existingProps = CollectExistingNames(newVals, propMarker + 2, propEnd, "Name|System.String");
                    var newProps = new List<TypedValue>();
                    foreach (var spec in OUTPUTS)
                    {
                        if (!emittedNames.Contains(spec.Name)) continue;
                        if (existingProps.Contains(spec.Name)) continue;
                        var (conv, macro) = UnitInfo(spec.Unit);
                        newProps.AddRange(BuildOutputProperty(spec.Name, spec.Display, conv, macro));
                        propsAdded++;
                        log.Add($"PROP nova: {spec.Name} ({spec.Unit})");
                    }
                    if (newProps.Count > 0) newVals.InsertRange(propEnd, newProps);

                    // ctor já foi aberto ForWrite em FindConstructor
                    ctor.Data = new ResultBuffer(newVals.ToArray());
                    t.Commit();
                }

                db.SaveAs(path, db.OriginalFileVersion);
            }

            log.Add($"RESULTADO: {emitted} emitidos, {skipped} pulados, {propsAdded} props novas, {removedGroups} grupos removidos.");
            File.WriteAllLines(Path.ChangeExtension(path, ".qto.log.txt"), log, Encoding.UTF8);
            return $"{emitted} emit / {skipped} pulado / {propsAdded} props / {removedGroups} removido";
        }

        // ===================== VALIDAÇÃO DE TOKENS =====================
        private static List<string> MissingTokens(string formula, HashSet<string> tokens, HashSet<string> emitted)
        {
            var ids = Regex.Matches(formula, @"[A-Za-z_][A-Za-z0-9_]*")
                           .Select(m => m.Value).Distinct();
            var falta = new List<string>();
            foreach (var id in ids)
            {
                if (BUILTINS.Contains(id)) continue;
                if (emitted.Contains(id)) continue;        // output já emitido nesta sequência
                if (tokens.Contains(id)) continue;          // prop ou sólido do device
                falta.Add(id);
            }
            return falta;
        }

        // Coleta nomes de propriedade (DynamicProperty Name) + nomes de sólido (DisplayName/PropName identificadores)
        private static HashSet<string> CollectTokens(List<TypedValue> v)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < v.Count - 1; i++)
            {
                if (v[i].TypeCode != 1000) continue;
                string key = v[i].Value?.ToString() ?? "";
                if ((key == "Name|System.String" || key == "DisplayName|System.String" || key == "PropName|System.String")
                    && v[i + 1].TypeCode == 1)
                {
                    string val = v[i + 1].Value?.ToString() ?? "";
                    // só identificadores puros (nomes de sólido/prop), ignora "X=Y", "FlowStep", frases
                    if (Regex.IsMatch(val, @"^[A-Za-z_][A-Za-z0-9_]*$")) set.Add(val);
                }
            }
            return set;
        }

        // ===================== CONSTRUTOR =====================
        private static Xrecord FindConstructor(DBDictionary solidos, Transaction t, string fileName, List<string> log)
        {
            Xrecord first = null;
            string wanted = Normalize(fileName);
            foreach (DBDictionaryEntry e in solidos)
            {
                if (!e.Key.Contains("Constructor")) continue;
                if (!(t.GetObject(e.Value, OpenMode.ForWrite) is Xrecord xr) || xr.Data == null) continue;
                if (first == null) first = xr;
                var arr = xr.Data.AsArray();
                string nm = ValorPorChave(arr, "Name|System.String");
                log.Add($"Constructor {e.Key} Name='{nm}'");
                if (!string.IsNullOrEmpty(nm) && Normalize(nm) == wanted) return xr;
            }
            if (first != null) log.Add("AVISO: usando o primeiro constructor (sem match exato de nome).");
            return first;
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.ToUpperInvariant().Replace("_BARRA_", "/").Replace("Í", "I").Replace("Ç", "C")
                 .Replace("Á", "A").Replace("Ã", "A").Replace("Â", "A").Replace("É", "E").Replace("Ê", "E")
                 .Replace("Ó", "O").Replace("Õ", "O").Replace("Ô", "O").Replace("Ú", "U");
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        private static string ValorPorChave(TypedValue[] arr, string chave)
        {
            for (int i = 0; i < arr.Length - 1; i++)
                if (arr[i].TypeCode == 1000 && (arr[i].Value?.ToString() ?? "") == chave && arr[i + 1].Value is string s)
                    return s;
            return "";
        }

        // ===================== GRUPOS (parse/rebuild) =====================
        private sealed class GroupInfo
        {
            public int StartIndex, EndIndex, Id = -1, ParentId = -1, SequenceIndex = -1;
            public string GroupName, DisplayName, PropName;
        }

        private static List<GroupInfo> ExtrairGrupos(List<TypedValue> v, int from, int toExclusive)
        {
            var grupos = new List<GroupInfo>();
            int i = from;
            while (i < toExclusive)
            {
                if (v[i].TypeCode == 102 && (v[i].Value?.ToString() ?? "").StartsWith("{SOLIDOS.", StringComparison.Ordinal))
                {
                    int depth = 0, fim = -1;
                    for (int j = i; j < toExclusive; j++)
                    {
                        if (v[j].TypeCode == 102)
                        {
                            string s = v[j].Value?.ToString() ?? "";
                            if (s.StartsWith("{")) depth++;
                            else if (s == "}") { depth--; if (depth == 0) { fim = j; break; } }
                        }
                    }
                    if (fim < 0) break;
                    var g = new GroupInfo
                    {
                        StartIndex = i,
                        EndIndex = fim,
                        GroupName = ((string)v[i].Value).Substring(1)
                    };
                    g.Id = IntField(v, i, fim, "id");
                    g.ParentId = IntField(v, i, fim, "parentid");
                    g.SequenceIndex = IntField(v, i, fim, "sequenceindex");
                    g.DisplayName = StrField(v, i, fim, "DisplayName|System.String");
                    g.PropName = StrField(v, i, fim, "PropName|System.String");
                    grupos.Add(g);
                    i = fim + 1;
                    continue;
                }
                i++;
            }
            return grupos;
        }

        private static List<TypedValue> RebuildWithout(List<TypedValue> v, int from, int toExclusive,
            List<GroupInfo> grupos, HashSet<int> removeIdx)
        {
            var keepRanges = new List<(int s, int e)>();
            foreach (var (g, idx) in grupos.Select((g, idx) => (g, idx)))
                if (!removeIdx.Contains(idx)) keepRanges.Add((g.StartIndex, g.EndIndex));

            var outList = new List<TypedValue>();
            // tudo antes do 1º grupo (inclui '{' da lista e marcadores)
            int firstGroupStart = grupos.Count > 0 ? grupos[0].StartIndex : toExclusive;
            for (int k = 0; k < firstGroupStart; k++) outList.Add(v[k]);
            // grupos mantidos
            foreach (var (s, e) in keepRanges)
                for (int k = s; k <= e; k++) outList.Add(v[k]);
            // tudo após o último grupo até o fim do arquivo (inclui '}' da lista, Activities tail, etc.)
            int lastGroupEnd = grupos.Count > 0 ? grupos[grupos.Count - 1].EndIndex : from - 1;
            for (int k = lastGroupEnd + 1; k < v.Count; k++) outList.Add(v[k]);
            return outList;
        }

        private static int InferRootSeqParent(List<GroupInfo> grupos)
        {
            // parentid mais comum entre ActivitySequence (aproxima o container raiz)
            var counts = new Dictionary<int, int>();
            foreach (var g in grupos)
                if (g.GroupName == "SOLIDOS.ActivitySequence" && g.ParentId >= 0)
                    counts[g.ParentId] = counts.TryGetValue(g.ParentId, out int n) ? n + 1 : 1;
            return counts.Count > 0 ? counts.OrderByDescending(k => k.Value).First().Key : 2;
        }

        private static int MaxRootSeqIndex(List<GroupInfo> grupos, int parent)
        {
            int max = -1;
            foreach (var g in grupos)
                if (g.GroupName == "SOLIDOS.ActivitySequence" && g.ParentId == parent && g.SequenceIndex > max)
                    max = g.SequenceIndex;
            return max;
        }

        private static int IntField(List<TypedValue> v, int from, int to, string key)
        {
            for (int i = from; i < to; i++)
                if (v[i].TypeCode == 1000 && (v[i].Value?.ToString() ?? "") == key && i + 1 <= to)
                {
                    var nx = v[i + 1];
                    if (nx.TypeCode == 90 && nx.Value is int ii) return ii;
                    if (nx.TypeCode == 70 && nx.Value is short ss) return ss;
                }
            return -1;
        }

        private static string StrField(List<TypedValue> v, int from, int to, string key)
        {
            for (int i = from; i < to; i++)
                if (v[i].TypeCode == 1000 && (v[i].Value?.ToString() ?? "") == key && i + 1 <= to && v[i + 1].Value is string s)
                    return s;
            return null;
        }

        private static int FindMaxId(List<TypedValue> v)
        {
            int max = 0;
            for (int i = 0; i < v.Count - 1; i++)
                if (v[i].TypeCode == 1000 && (v[i].Value?.ToString() ?? "") == "id" && v[i + 1].TypeCode == 90)
                { int id = Convert.ToInt32(v[i + 1].Value); if (id > max) max = id; }
            return max;
        }

        // ===================== BUILDERS =====================
        private static List<TypedValue> BuildSequence(int id, int parentId, int seqIdx, string name) => new List<TypedValue>
        {
            new TypedValue(102, "{SOLIDOS.ActivitySequence"),
            new TypedValue(1000, "location|System.String"), new TypedValue(1, "-100,1060"),
            new TypedValue(1000, "DisplayName|System.String"), new TypedValue(1, name),
            new TypedValue(1000, "Description|System.String"), new TypedValue(1, ""),
            new TypedValue(1000, "parentid"), new TypedValue(90, parentId),
            new TypedValue(1000, "id"), new TypedValue(90, id),
            new TypedValue(1000, "sequenceindex"), new TypedValue(90, seqIdx),
            new TypedValue(102, "}"),
        };

        private static List<TypedValue> BuildSequenceBox(int id, int parentId, string name) => new List<TypedValue>
        {
            new TypedValue(102, "{SOLIDOS.ActivitySequenceBox"),
            new TypedValue(1000, "DisplayName|System.String"), new TypedValue(1, name),
            new TypedValue(1000, "Description|System.String"), new TypedValue(1, ""),
            new TypedValue(1000, "parentid"), new TypedValue(90, parentId),
            new TypedValue(1000, "id"), new TypedValue(90, id),
            new TypedValue(1000, "sequenceindex"), new TypedValue(90, -1),
            new TypedValue(1000, "location|System.String"), new TypedValue(1, ""),
            new TypedValue(102, "}"),
        };

        private static List<TypedValue> BuildSetOutPutParam(string prop, string value, int parentId, int id, int seqIdx, string loc) => new List<TypedValue>
        {
            new TypedValue(102, "{SOLIDOS.ActivitySetOutPutParam"),
            new TypedValue(1000, "PropName|System.String"), new TypedValue(1, prop),
            new TypedValue(1000, "Value|System.String"), new TypedValue(1, value),
            new TypedValue(1000, "DisplayName|System.String"), new TypedValue(1, $"{prop}={value}"),
            new TypedValue(1000, "Description|System.String"), new TypedValue(1, ""),
            new TypedValue(1000, "parentid"), new TypedValue(90, parentId),
            new TypedValue(1000, "id"), new TypedValue(90, id),
            new TypedValue(1000, "sequenceindex"), new TypedValue(90, seqIdx),
            new TypedValue(1000, "location|System.String"), new TypedValue(1, loc),
            new TypedValue(102, "}"),
        };

        // DynamicProperty de saída no template QTO SMEC do device (IsDefaultDescriptor=0, ValueProvider=3)
        private static List<TypedValue> BuildOutputProperty(string name, string display, string conv, string macro) => new List<TypedValue>
        {
            new TypedValue(102, "{SOLIDOS.DynamicProperty"),
            new TypedValue(1000, "IsDefaultDescriptor"), new TypedValue(290, (short)0),
            new TypedValue(1000, "Name|System.String"), new TypedValue(1, name),
            new TypedValue(1000, "ValueProvider"), new TypedValue(90, 3),
            new TypedValue(1000, "VarType|System.RuntimeType"), new TypedValue(1, "System.Double"),
            new TypedValue(1000, "TypeConverter|System.RuntimeType"), new TypedValue(1, conv),
            new TypedValue(1000, "Direction"), new TypedValue(90, 1),
            new TypedValue(1000, "Macro|System.String"), new TypedValue(1, macro),
            new TypedValue(1000, "DisplayName|System.String"), new TypedValue(1, display),
            new TypedValue(1000, "Category|System.String"), new TypedValue(1, "QTO SMEC"),
            new TypedValue(1000, "DefValue"), new TypedValue(40, 0.0),
            new TypedValue(1000, "Visible"), new TypedValue(290, (short)1),
            new TypedValue(1000, "KeepOnChange"), new TypedValue(290, (short)0),
            new TypedValue(102, "}"),
        };

        // ===================== HELPERS GERAIS =====================
        private static int FindMarker(List<TypedValue> v, string marker)
        {
            for (int i = 0; i < v.Count - 1; i++)
                if (v[i].TypeCode == 1000 && (v[i].Value?.ToString() ?? "") == marker) return i;
            return -1;
        }

        private static int FindListEnd(List<TypedValue> v, int listStart)
        {
            if (v[listStart].TypeCode != 102 || (v[listStart].Value?.ToString() ?? "") != "{")
                throw new System.Exception("Esperado '{' no início da lista");
            int depth = 1;
            for (int i = listStart + 1; i < v.Count; i++)
                if (v[i].TypeCode == 102)
                {
                    string s = v[i].Value?.ToString() ?? "";
                    if (s.StartsWith("{")) depth++;
                    else if (s == "}") { depth--; if (depth == 0) return i; }
                }
            throw new System.Exception("Fim de lista não encontrado");
        }

        private static HashSet<string> CollectExistingNames(List<TypedValue> v, int from, int toExclusive, string field)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            for (int i = from; i < toExclusive - 1; i++)
                if (v[i].TypeCode == 1000 && (v[i].Value?.ToString() ?? "") == field && v[i + 1].TypeCode == 1)
                    set.Add(v[i + 1].Value?.ToString() ?? "");
            return set;
        }
    }
}
