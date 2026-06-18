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
    /// AddQtoSmecCanaleta: insere o pacote "QTO SMEC CANALETA" (cálculos + variáveis
    /// globais) em UM dispositivo LINEAR de canaleta (SolGravityLinear) por vez.
    ///
    /// Reproduz exatamente as colunas das abas CANALET_* da planilha de quantitativos
    /// (Leandro). Mesmo padrão da sequência "QTO TUB_OLEO" do tubo.
    ///
    /// Diferente do AddQtoSmecEmLote (que rodou em lote e deu errado), aqui:
    ///  - Processa SÓ 1 arquivo selecionado.
    ///  - Remoção idempotente atinge APENAS a sequência chamada "QTO SMEC CANALETA"
    ///    (e seus filhos). NUNCA remove SetOutPutParam solto de geometria.
    ///  - ABORTA com aviso se as entradas de seção (Largura / Parede) não existirem
    ///    no device (sintoma clássico de device errado / nome diferente).
    ///  - Backup antes de gravar; log .qto.canaleta.log.txt ao lado.
    ///
    /// Mapeamento de entradas (CONFIRMADO em IFC/IfcSolidosDrainageBinder.cs e na
    /// sequência QTO TUB_OLEO):
    ///   H (extensão)          = Axis3D.Length              -> Comprimento
    ///   D/F (terreno mont/jus)= StartTopElevation/EndTopElevation
    ///   E/G (FIT mont/jus)    = StartInvertElevation/EndInvertElevation
    ///   J=b (largura interna) = Largura
    ///   K=e (parede e fundo)  = Parede
    /// Premissa (igual à planilha): topo da canaleta no nível do terreno.
    ///
    /// Simplificações vs planilha (unidade limpa nas DynamicProperties):
    ///   - Taxa de aço 50 kg/m³ embutida em MassaAco (edite na fórmula se mudar).
    ///   - TaxaFormas (m²/m³, QA) não é exportada como quantitativo.
    /// </summary>
    public class AddQtoSmecCanaleta
    {
        private const string SeqDisplayName = "QTO SMEC CANALETA";

        // Unidades (TypeConverter) e macros (P2) — idênticas ao padrão do device.
        private const string CvDist = "SOLIDOS.UnidadeDistancia";
        private const string CvArea = "SOLIDOS.UnidadeArea";
        private const string CvVol  = "SOLIDOS.UnidadeVolume";
        private const string CvMass = "SOLIDOS.UnidadeMassa";
        private const string CvDens = "SOLIDOS.UnidadeDensidade";
        private const string MDist = "[(T1|U9|P2|D0|N0|M1|Z0)]";
        private const string MArea = "[(T1|U38|P2|D0|N0|M1|Z0)]";
        private const string MVol  = "[(T1|U15|P2|D0|N0|M1|Z0)]";
        private const string MMass = "[(T1|U126|P2|D0|N0|M1|Z0)]";
        private const string MDens = "[(T1|U71|P2|D0|N0|M1|Z0)]";

        private sealed class Out
        {
            public string Name, Formula, Display, Conv, Macro;
            public Out(string n, string f, string d, string cv, string mc)
            { Name = n; Formula = f; Display = d; Conv = cv; Macro = mc; }
        }

        // Ordem = ordem de cálculo (cada linha pode usar as anteriores). 20 saídas.
        private static readonly List<Out> OUTPUTS = new List<Out>
        {
            new Out("Comprimento",     "Axis3D.Length",
                    "Comprimento (extensão)",                CvDist, MDist),
            new Out("EspConcMagro",    "0.05",
                    "Espessura concreto magro",              CvDist, MDist),
            new Out("LarguraExterna",  "2*Parede+Largura",
                    "Largura externa (B)",                   CvDist, MDist),
            new Out("LarguraVala",     "If(LarguraExterna<=0.4,0.8,If(LarguraExterna>0.8,LarguraExterna+0.4,LarguraExterna+0.6))",
                    "Largura da vala (L)",                   CvDist, MDist),
            new Out("ProfValaMont",    "StartTopElevation-StartInvertElevation+Parede+EspConcMagro",
                    "Profundidade vala montante",            CvDist, MDist),
            new Out("ProfValaJus",     "EndTopElevation-EndInvertElevation+Parede+EspConcMagro",
                    "Profundidade vala jusante",             CvDist, MDist),
            new Out("SecValaMont",     "If(ProfValaMont<=1.25,(LarguraVala*ProfValaMont),(LarguraVala+ProfValaMont)*ProfValaMont)",
                    "Seção vala montante (S1)",              CvArea, MArea),
            new Out("SecValaJus",      "If(ProfValaJus<=1.25,(LarguraVala*ProfValaJus),ProfValaJus*(LarguraVala+ProfValaJus))",
                    "Seção vala jusante (S2)",               CvArea, MArea),
            new Out("AlturaMedia",     "((StartTopElevation-StartInvertElevation+Parede)+(EndTopElevation-EndInvertElevation+Parede))/2",
                    "Altura média (Hmed)",                   CvDist, MDist),
            new Out("AreaApiloamento", "Comprimento*LarguraVala",
                    "Área de apiloamento",                   CvArea, MArea),
            new Out("VolEscav",        "((SecValaMont+SecValaJus)/2)*Comprimento",
                    "Volume de escavação (VE)",              CvVol,  MVol),
            new Out("VolConcMagro",    "(LarguraExterna+0.1)*EspConcMagro*Comprimento",
                    "Volume concreto magro (Vcm)",           CvVol,  MVol),
            new Out("VolCanaleta",     "AlturaMedia*LarguraExterna*Comprimento",
                    "Volume canaleta - bruto (Vc)",          CvVol,  MVol),
            new Out("VolReaterro",     "VolEscav-VolConcMagro-VolCanaleta",
                    "Volume de reaterro (VR)",               CvVol,  MVol),
            new Out("VolBotaFora",     "VolEscav-VolConcMagro-VolReaterro",
                    "Volume de bota-fora (Vbf)",             CvVol,  MVol),
            new Out("MassaEspAdotada", "1.8",
                    "Massa específica adotada",              CvDens, MDens),
            new Out("MassaBotaFora",   "VolBotaFora*MassaEspAdotada",
                    "Massa de bota-fora (Mbf)",              CvMass, MMass),
            new Out("VolConcCanaleta", "((LarguraExterna*AlturaMedia)-(Largura*(AlturaMedia-Parede)))*Comprimento",
                    "Volume concreto canaleta (V_cc)",       CvVol,  MVol),
            new Out("MassaAco",        "VolConcCanaleta*50",
                    "Massa de aço (M_aço) [taxa 50 kg/m³]",  CvMass, MMass),
            new Out("AreaFormas",      "(AlturaMedia*Comprimento*2)+((AlturaMedia-Parede)*Comprimento*2)",
                    "Área de fôrmas (A_form)",               CvArea, MArea),
        };

        // Entradas de SEÇÃO que PRECISAM existir no device (senão fórmulas leem 0 = lixo).
        // NÃO incluo as built-ins de runtime (StartTopElevation/Axis3D/etc.), que não
        // aparecem na lista de propriedades declaradas.
        private static readonly string[] REQUIRED_INPUTS = { "Largura", "Parede" };

        [CommandMethod("AddQtoSmecCanaleta", CommandFlags.Session)]
        public void Run()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            var pfo = new PromptOpenFileOptions("\nSelecione o .sbd da CANALETA")
            {
                Filter = "SOLIDOS Builder (*.sbd;*.dwg)|*.sbd;*.dwg|Todos (*.*)|*.*",
                DialogCaption = "Selecionar SOLIDOS Builder (canaleta)"
            };
            var fr = ed.GetFileNameForOpen(pfo);
            if (fr.Status != PromptStatus.OK) return;
            string path = fr.StringResult;

            try
            {
                string resumo = ProcessFile(path, ed);
                ed.WriteMessage($"\n[OK] {Path.GetFileName(path)}: {resumo}");
                ed.WriteMessage($"\nLog: {Path.ChangeExtension(path, ".qto.canaleta.log.txt")}");
            }
            catch (CanceladoException cex)
            {
                ed.WriteMessage($"\n[CANCELADO] {cex.Message}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[ERRO] {ex.Message}");
            }
        }

        private sealed class CanceladoException : System.Exception
        {
            public CanceladoException(string m) : base(m) { }
        }

        private string ProcessFile(string path, Editor ed)
        {
            var log = new List<string>();
            string nome = Path.GetFileNameWithoutExtension(path);
            log.Add($"== {nome} ==  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            int emitted = 0, propsAdded = 0, propsSkipped = 0, removedGroups = 0;

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

                    Xrecord ctor = SelecionarConstructor(solidos, t, nome, ed, log);
                    if (ctor == null) throw new CanceladoException("Nenhum constructor selecionado.");

                    var v = new List<TypedValue>();
                    foreach (TypedValue tv in ctor.Data) v.Add(tv);

                    // ---- guarda: entradas de seção precisam existir ----
                    var tokens = CollectTokens(v);
                    var faltando = REQUIRED_INPUTS.Where(r => !tokens.Contains(r)).ToList();
                    if (faltando.Count > 0)
                    {
                        ed.WriteMessage($"\nATENÇÃO: este device NÃO tem a(s) propriedade(s) de seção: {string.Join(", ", faltando)}.");
                        ed.WriteMessage("\nSem elas as fórmulas leem 0 (resultado inválido). Talvez seja o device errado ou os nomes diferem.");
                        var pko = new PromptKeywordOptions("\nGravar mesmo assim?")
                        { AllowNone = false };
                        pko.Keywords.Add("Sim");
                        pko.Keywords.Add("Nao");
                        pko.Keywords.Default = "Nao";
                        var rk = ed.GetKeywords(pko);
                        if (rk.Status != PromptStatus.OK || rk.StringResult != "Sim")
                            throw new CanceladoException($"Entradas faltando: {string.Join(",", faltando)}");
                        log.Add($"AVISO: gravando mesmo com entradas faltando: {string.Join(",", faltando)}");
                    }

                    // ---- Backup só agora (depois de validar e antes de gravar) ----
                    string backup = path + $".backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                    File.Copy(path, backup, overwrite: false);
                    log.Add($"Backup: {backup}");

                    // ---- Activities bounds ----
                    int actMarker = FindMarker(v, "Activities|SOLIDOS.ListActivity");
                    if (actMarker < 0) throw new System.Exception("Marcador Activities não encontrado");
                    int actListEnd = FindListEnd(v, actMarker + 1);

                    // ---- remove SÓ a(s) sequência(s) "QTO SMEC CANALETA" + filhos (idempotência) ----
                    var grupos = ExtrairGrupos(v, actMarker + 1, actListEnd);
                    var qtoSeqIds = new List<int>();
                    foreach (var g in grupos)
                        if (g.GroupName == "SOLIDOS.ActivitySequence" &&
                            string.Equals(g.DisplayName, SeqDisplayName, StringComparison.OrdinalIgnoreCase))
                            qtoSeqIds.Add(g.Id);

                    var removeSet = new HashSet<int>();
                    for (int i = 0; i < grupos.Count; i++)
                    {
                        var g = grupos[i];
                        bool rem = (g.GroupName == "SOLIDOS.ActivitySequence" &&
                                    string.Equals(g.DisplayName, SeqDisplayName, StringComparison.OrdinalIgnoreCase))
                                   || qtoSeqIds.Contains(g.ParentId);
                        if (rem) { removeSet.Add(i); removedGroups++; log.Add($"REMOVE grupo {g.GroupName} id={g.Id} '{g.DisplayName ?? g.PropName}'"); }
                    }

                    var newVals = RebuildWithout(v, grupos, removeSet);
                    int actListEnd2 = FindListEnd(newVals, FindMarker(newVals, "Activities|SOLIDOS.ListActivity") + 1);

                    // ---- ids / parent para o novo bloco ----
                    int maxId = FindMaxId(newVals);
                    int parentForSeq = InferRootSeqParent(grupos); // linear => normalmente 0
                    int idCursor = maxId + 1;
                    int seqId = idCursor++;
                    int boxId = idCursor++;
                    log.Add($"Nova sequência '{SeqDisplayName}': parentid={parentForSeq} id={seqId}");

                    var blocos = new List<TypedValue>();
                    blocos.AddRange(BuildSequence(seqId, parentForSeq, -1, SeqDisplayName));
                    blocos.AddRange(BuildSequenceBox(boxId, seqId, SeqDisplayName));
                    int sIdx = 1;
                    foreach (var o in OUTPUTS)
                    {
                        blocos.AddRange(BuildSetOutPutParam(o.Name, o.Formula, seqId, idCursor++, sIdx, $"20,{30 * sIdx}"));
                        emitted++; sIdx++;
                        log.Add($"EMITE {o.Name} = {o.Formula}");
                    }
                    newVals.InsertRange(actListEnd2, blocos);

                    // ---- DynamicProperties (variáveis globais): só adiciona as que faltam ----
                    int propMarker = FindMarker(newVals, "Properties|SOLIDOS.ListDynamicProperty");
                    if (propMarker < 0) throw new System.Exception("Marcador Properties não encontrado");
                    int propEnd = FindListEnd(newVals, propMarker + 1);
                    var existingProps = CollectExistingNames(newVals, propMarker + 2, propEnd, "Name|System.String");

                    var newProps = new List<TypedValue>();
                    foreach (var o in OUTPUTS)
                    {
                        if (existingProps.Contains(o.Name)) { propsSkipped++; log.Add($"PROP já existe: {o.Name} (mantida)"); continue; }
                        newProps.AddRange(BuildOutputProperty(o.Name, o.Display, o.Conv, o.Macro));
                        propsAdded++;
                        log.Add($"PROP nova: {o.Name}");
                    }
                    if (newProps.Count > 0) newVals.InsertRange(propEnd, newProps);

                    ctor.UpgradeOpen();
                    ctor.Data = new ResultBuffer(newVals.ToArray());
                    t.Commit();
                }

                db.SaveAs(path, db.OriginalFileVersion);
            }

            log.Add($"RESULTADO: {emitted} cálculos, {propsAdded} props novas, {propsSkipped} props já existentes, {removedGroups} grupos QTO antigos removidos.");
            File.WriteAllLines(Path.ChangeExtension(path, ".qto.canaleta.log.txt"), log, Encoding.UTF8);
            return $"{emitted} cálculos / {propsAdded} props novas / {propsSkipped} mantidas / {removedGroups} removido";
        }

        // ===================== CONSTRUTOR =====================
        private static Xrecord SelecionarConstructor(DBDictionary solidos, Transaction t, string fileName, Editor ed, List<string> log)
        {
            var nomes = new List<string>();
            var xrecs = new List<Xrecord>();
            string wanted = Normalize(fileName);
            int match = -1;

            foreach (DBDictionaryEntry e in solidos)
            {
                if (!e.Key.Contains("Constructor")) continue;
                if (!(t.GetObject(e.Value, OpenMode.ForRead) is Xrecord xr) || xr.Data == null) continue;
                string nm = ValorPorChave(xr.Data.AsArray(), "Name|System.String");
                log.Add($"Constructor {e.Key} Name='{nm}'");
                xrecs.Add(xr);
                nomes.Add(nm ?? "(sem nome)");
                if (!string.IsNullOrEmpty(nm) && Normalize(nm) == wanted) match = xrecs.Count - 1;
            }

            if (xrecs.Count == 0) return null;
            if (xrecs.Count == 1) return xrecs[0];
            if (match >= 0) { log.Add($"Constructor escolhido por nome do arquivo: '{nomes[match]}'"); return xrecs[match]; }

            // múltiplos sem match: o usuário escolhe
            ed.WriteMessage("\nMúltiplos constructors neste arquivo:");
            for (int i = 0; i < nomes.Count; i++) ed.WriteMessage($"\n  {i + 1}) {nomes[i]}");
            var pio = new PromptIntegerOptions($"\nQual usar? (1..{nomes.Count})")
            { LowerLimit = 1, UpperLimit = nomes.Count, AllowNegative = false, AllowZero = false };
            var ri = ed.GetInteger(pio);
            if (ri.Status != PromptStatus.OK) return null;
            log.Add($"Constructor escolhido manualmente: '{nomes[ri.Value - 1]}'");
            return xrecs[ri.Value - 1];
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
            public int StartIndex, EndIndex, Id = -1, ParentId = -1;
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

        private static List<TypedValue> RebuildWithout(List<TypedValue> v, List<GroupInfo> grupos, HashSet<int> removeIdx)
        {
            if (removeIdx.Count == 0) return new List<TypedValue>(v);

            var keepRanges = new List<(int s, int e)>();
            for (int idx = 0; idx < grupos.Count; idx++)
                if (!removeIdx.Contains(idx)) keepRanges.Add((grupos[idx].StartIndex, grupos[idx].EndIndex));

            var outList = new List<TypedValue>();
            int firstGroupStart = grupos.Count > 0 ? grupos[0].StartIndex : v.Count;
            for (int k = 0; k < firstGroupStart; k++) outList.Add(v[k]);
            foreach (var (s, e) in keepRanges)
                for (int k = s; k <= e; k++) outList.Add(v[k]);
            int lastGroupEnd = grupos.Count > 0 ? grupos[grupos.Count - 1].EndIndex : -1;
            for (int k = lastGroupEnd + 1; k < v.Count; k++) outList.Add(v[k]);
            return outList;
        }

        private static int InferRootSeqParent(List<GroupInfo> grupos)
        {
            var counts = new Dictionary<int, int>();
            foreach (var g in grupos)
                if (g.GroupName == "SOLIDOS.ActivitySequence" && g.ParentId >= 0)
                    counts[g.ParentId] = counts.TryGetValue(g.ParentId, out int n) ? n + 1 : 1;
            return counts.Count > 0 ? counts.OrderByDescending(k => k.Value).First().Key : 0;
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
                    if (Regex.IsMatch(val, @"^[A-Za-z_][A-Za-z0-9_]*$")) set.Add(val);
                }
            }
            return set;
        }

        private static HashSet<string> CollectExistingNames(List<TypedValue> v, int from, int toExclusive, string field)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            for (int i = from; i < toExclusive - 1; i++)
                if (v[i].TypeCode == 1000 && (v[i].Value?.ToString() ?? "") == field && v[i + 1].TypeCode == 1)
                    set.Add(v[i + 1].Value?.ToString() ?? "");
            return set;
        }

        // ===================== BUILDERS =====================
        private static List<TypedValue> BuildSequence(int id, int parentId, int seqIdx, string name) => new List<TypedValue>
        {
            new TypedValue(102, "{SOLIDOS.ActivitySequence"),
            new TypedValue(1000, "location|System.String"), new TypedValue(1, "370,320"),
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

        // DynamicProperty de saída no template QTO SMEC (IsDefaultDescriptor=0, ValueProvider=3)
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
    }
}
