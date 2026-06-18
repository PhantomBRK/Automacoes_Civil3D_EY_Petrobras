using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using SOLIDOS;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutomacoesCivil3D
{
    // Par de comandos para desconectar TODAS as bacias de TODOS os dispositivos do
    // desenho de uma vez (e reconectar depois).
    //
    // A lógica de (des)conexão é a mesma usada pelo dimensionamento/incêndio:
    //   - SolidosAPI.DisConnectNodes(baciaId, deviceId)  -> bacia é upstream do dispositivo
    //   - SolidosAPI.ConnectNodes(baciaId, deviceId)     -> religa
    // A lista de bacias de cada dispositivo vem de "Catchments"
    // (SolidosVazaoCombateIncendioSOL.ListarCatchments).
    //
    // O mapeamento dispositivo -> bacias é gravado num JSON ao lado do DWG
    // (SolidosBaciasEstado) para que o reconectar saiba o que religar.
    public class SolidosDesconectarBaciasSOL
    {
        public const string LongCommandName = "SOL_DESCONECTAR_BACIAS";
        public const string ShortCommandName = "SDESBAC";

        [CommandMethod(LongCommandName)]
        public void ExecuteLong() => Execute();

        [CommandMethod(ShortCommandName)]
        public void ExecuteShort() => Execute();

        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            // Estado persistente: JSON ao lado do DWG. Sem ele, não dá para reconectar depois.
            string estadoPath = SolidosBaciasEstado.ResolverCaminhoEstado(doc.Name);
            if (estadoPath == null)
            {
                ed.WriteMessage(
                    "\n[SOLIDOS] DWG não está salvo em disco — o mapeamento das bacias não pode ser gravado," +
                    "\n          então o SOL_RECONECTAR_BACIAS não terá como religá-las." +
                    "\n          Salve o DWG antes de desconectar.\n");
                return;
            }

            ed.WriteMessage(
                "\n[SOLIDOS] Desconectar bacias de TODOS os dispositivos do desenho." +
                "\n          O mapeamento será salvo para o SOL_RECONECTAR_BACIAS poder religar.\n");

            // Faz merge com o que já existir (caso desconecte mais de uma vez antes de reconectar).
            SolidosBaciasEstado estado = SolidosBaciasEstado.Carregar(estadoPath);
            estado.DwgPath = doc.Name;

            ObjectId[] ids = GetAllEntityIds(db);

            int dispositivosComBacia = 0;
            int baciasDesconectadas = 0;
            int baciasFalha = 0;

            // Contadores de diagnóstico — para descobrir onde a varredura para,
            // caso nenhuma bacia seja desconectada.
            int nodesSolidos = 0;          // entidades que respondem ao SolidosAPI
            int dispComArea = 0;           // dispositivos com ContributionArea > 0
            int totalBaciasEncontradas = 0;

            foreach (ObjectId id in ids)
            {
                // Só objetos SOLIDOS respondem a ListProperties com algo.
                if (!EhNoSolidos(id)) continue;
                nodesSolidos++;

                // Cross-check: ContributionArea > 0 indica bacia(s) conectada(s),
                // mesmo que a leitura de Catchments falhe por algum motivo.
                double area = SolidosVazaoCombateIncendioSOL.ConvertToDouble(
                    SolidosVazaoCombateIncendioSOL.TryGetRawParam(id, "ContributionArea"));
                if (area > 0) dispComArea++;

                List<ObjectId> bacias = SolidosVazaoCombateIncendioSOL.ListarCatchments(id);
                if (bacias == null || bacias.Count == 0)
                {
                    // Dispositivo tem área mas Catchments veio vazio — sinaliza divergência
                    // entre ContributionArea e a lista de bacias (problema de propriedade).
                    if (area > 0)
                    {
                        string nomeDiag = SolidosVazaoCombateIncendioSOL.TryGetParam<string>(id, "Name") ?? "(sem nome)";
                        ed.WriteMessage(
                            $"\n    [Diag] {nomeDiag}: ContributionArea={area:N1} m² mas Catchments vazio.");
                    }
                    continue;
                }
                totalBaciasEncontradas += bacias.Count;

                string nome = SolidosVazaoCombateIncendioSOL.TryGetParam<string>(id, "Name") ?? "(sem nome)";
                List<string> baciasOkHandles = new List<string>();

                foreach (ObjectId baciaId in bacias)
                {
                    try
                    {
                        // Bacia é upstream, dispositivo é downstream (descarrega NELE).
                        string ret = SolidosAPI.DisConnectNodes(baciaId, id);
                        if (SolidosVazaoCombateIncendioSOL.ConexaoOk(ret))
                        {
                            baciasDesconectadas++;
                            baciasOkHandles.Add(SolidosBaciasEstado.ObjectIdToHandle(baciaId));
                        }
                        else
                        {
                            baciasFalha++;
                            ed.WriteMessage(
                                $"\n    [Aviso] {nome}: DisConnect bacia handle {baciaId.Handle} retornou: {SolidosVazaoCombateIncendioSOL.DescreverRetornoConexao(ret)}");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        baciasFalha++;
                        System.Exception root = ex;
                        while (root.InnerException != null) root = root.InnerException;
                        ed.WriteMessage(
                            $"\n    [Erro] {nome}: falha ao desconectar bacia handle {baciaId.Handle}: {root.Message}");
                    }
                }

                if (baciasOkHandles.Count > 0)
                {
                    dispositivosComBacia++;
                    estado.RegistrarOuMesclar(
                        SolidosBaciasEstado.ObjectIdToHandle(id), nome, baciasOkHandles);
                    ed.WriteMessage(
                        $"\n  ✓ {nome}: {baciasOkHandles.Count} bacia(s) desconectada(s)");
                }
            }

            // Persiste o mapeamento.
            if (baciasDesconectadas > 0)
            {
                try
                {
                    estado.Salvar(estadoPath);
                    ed.WriteMessage($"\n[SOLIDOS] Mapeamento salvo em: {estadoPath}");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage(
                        $"\n[AVISO] Não foi possível salvar o mapeamento ({ex.Message})." +
                        "\n        A reconexão automática pode não funcionar.");
                }
            }

            try { SolidosAPI.DocCommit(); } catch { }

            ed.WriteMessage(
                "\n[SOLIDOS] Resumo:" +
                $"\n  Entidades no ModelSpace varridas: {ids.Length}" +
                $"\n  Objetos SOLIDOS encontrados: {nodesSolidos}" +
                $"\n  Dispositivos com ContributionArea > 0: {dispComArea}" +
                $"\n  Bacias encontradas (Catchments): {totalBaciasEncontradas}" +
                $"\n  Dispositivos com bacia desconectada: {dispositivosComBacia}" +
                $"\n  Bacias desconectadas: {baciasDesconectadas}" +
                $"\n  Bacias com falha: {baciasFalha}\n");

            // Mensagens de orientação quando nada foi desconectado.
            if (baciasDesconectadas == 0)
            {
                if (nodesSolidos == 0)
                    ed.WriteMessage(
                        "\n[SOLIDOS] A varredura não encontrou NENHUM objeto SOLIDOS no ModelSpace." +
                        "\n          Verifique se o desenho ativo é o que contém a rede (e não um XREF)," +
                        "\n          e se os dispositivos não estão dentro de um bloco.\n");
                else if (dispComArea > 0)
                    ed.WriteMessage(
                        "\n[SOLIDOS] Há dispositivos com área de contribuição, mas a lista 'Catchments'" +
                        "\n          veio vazia — a leitura das bacias falhou. Rode SOL_DUMP_DISPOSITIVOS" +
                        "\n          num desses dispositivos e me diga o nome da propriedade das bacias.\n");
                else
                    ed.WriteMessage(
                        "\n[SOLIDOS] Nenhum dispositivo tem bacia conectada (ContributionArea = 0 em todos)." +
                        "\n          Não há o que desconectar.\n");
            }

            if (baciasDesconectadas > 0)
                PerguntarRebuild(ed, doc);
        }

        // True se a entidade responde ao SolidosAPI (= é um nó SOLIDOS).
        private static bool EhNoSolidos(ObjectId id)
        {
            try
            {
                List<string> props = SolidosAPI.ListProperties(id);
                return props != null && props.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        // Itera TODOS os objetos do ModelSpace. O filtro do que é SOLIDOS é feito
        // depois, por ListarCatchments (só dispositivos têm a propriedade Catchments).
        private static ObjectId[] GetAllEntityIds(Database db)
        {
            List<ObjectId> ids = new List<ObjectId>();
            using (Transaction t = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)t.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms =
                    (BlockTableRecord)t.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId oid in ms) ids.Add(oid);
                t.Commit();
            }
            return ids.ToArray();
        }

        internal static void PerguntarRebuild(Editor ed, Document doc)
        {
            PromptKeywordOptions pko = new PromptKeywordOptions(
                $"\nRodar {SolidosVazaoCombateIncendioSOL.RebuildCommand} agora para recalcular a rede? [Sim/Não] <Sim>: ");
            pko.Keywords.Add("Sim");
            pko.Keywords.Add("Não");
            pko.Keywords.Default = "Sim";
            pko.AllowNone = true;

            PromptResult pr = ed.GetKeywords(pko);
            string ans = (pr.Status == PromptStatus.OK) ? pr.StringResult : "Sim";
            if (string.Equals(ans, "Sim", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    doc.SendStringToExecute(
                        SolidosVazaoCombateIncendioSOL.RebuildCommand + " ", true, false, false);
                    ed.WriteMessage($"\n[SOLIDOS] {SolidosVazaoCombateIncendioSOL.RebuildCommand} disparado.\n");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[SOLIDOS] Falha ao disparar rebuild: {ex.Message}\n");
                }
            }
        }
    }

    // Reconecta as bacias usando o mapeamento gravado por SOL_DESCONECTAR_BACIAS.
    // Apaga o JSON ao final se tudo religou (ou mantém o que falhou para nova tentativa).
    public class SolidosReconectarBaciasSOL
    {
        public const string LongCommandName = "SOL_RECONECTAR_BACIAS";
        public const string ShortCommandName = "SRECBAC";

        [CommandMethod(LongCommandName)]
        public void ExecuteLong() => Execute();

        [CommandMethod(ShortCommandName)]
        public void ExecuteShort() => Execute();

        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            string estadoPath = SolidosBaciasEstado.ResolverCaminhoEstado(doc.Name);
            if (estadoPath == null)
            {
                ed.WriteMessage("\n[SOLIDOS] DWG não está salvo em disco — não há mapeamento para reconectar.\n");
                return;
            }

            // Se não existe o JSON com o nome EXATO do DWG atual, procura o
            // .solidos-bacias.json mais recente na mesma pasta. Isso cobre o fluxo de
            // salvar cópias datadas (desconecta num DWG, "Salvar Como" outro, reconecta).
            if (!System.IO.File.Exists(estadoPath))
            {
                string alt = AcharMapeamentoMaisRecente(estadoPath);
                if (alt != null)
                {
                    ed.WriteMessage(
                        $"\n[SOLIDOS] Não há mapeamento com o nome do DWG atual." +
                        $"\n          Usando o mais recente da pasta: {System.IO.Path.GetFileName(alt)}\n");
                    estadoPath = alt;
                }
            }

            SolidosBaciasEstado estado = SolidosBaciasEstado.Carregar(estadoPath);
            if (estado.Dispositivos == null || estado.Dispositivos.Count == 0)
            {
                ed.WriteMessage(
                    $"\n[SOLIDOS] Nenhum mapeamento de bacias encontrado em: {estadoPath}" +
                    "\n          Rode SOL_DESCONECTAR_BACIAS primeiro." +
                    "\n          (Procurei também por outros .solidos-bacias.json na mesma pasta.)\n");
                return;
            }

            int totalBacias = estado.Dispositivos.Sum(d => d.BaciasHandles?.Count ?? 0);
            ed.WriteMessage(
                "\n[SOLIDOS] Reconectar bacias a partir do mapeamento salvo." +
                $"\n  Arquivo: {estadoPath}" +
                $"\n  Dispositivos: {estado.Dispositivos.Count}  |  Bacias: {totalBacias}\n");

            int dispositivosOk = 0;
            int baciasReconectadas = 0;
            int baciasFalha = 0;
            List<string> dispositivosTotalmenteOk = new List<string>();

            foreach (var disp in estado.Dispositivos)
            {
                if (!SolidosBaciasEstado.TryHandleToObjectId(db, disp.DeviceHandle, out ObjectId deviceId))
                {
                    ed.WriteMessage(
                        $"\n  [{disp.DeviceName ?? disp.DeviceHandle}] dispositivo não existe mais no DWG (handle {disp.DeviceHandle}); pulado.");
                    baciasFalha += disp.BaciasHandles?.Count ?? 0;
                    continue;
                }

                bool todasOk = true;
                int okNesse = 0;

                foreach (string baciaHandle in disp.BaciasHandles ?? new List<string>())
                {
                    if (!SolidosBaciasEstado.TryHandleToObjectId(db, baciaHandle, out ObjectId baciaId))
                    {
                        ed.WriteMessage(
                            $"\n  [{disp.DeviceName ?? disp.DeviceHandle}] bacia handle {baciaHandle} não existe mais; ignorada.");
                        baciasFalha++;
                        todasOk = false;
                        continue;
                    }

                    try
                    {
                        // Mesma semântica do disconnect: bacia upstream, dispositivo downstream.
                        string ret = SolidosAPI.ConnectNodes(baciaId, deviceId);
                        if (SolidosVazaoCombateIncendioSOL.ConexaoOk(ret))
                        {
                            baciasReconectadas++;
                            okNesse++;
                        }
                        else
                        {
                            ed.WriteMessage(
                                $"\n  [{disp.DeviceName ?? disp.DeviceHandle}] ConnectNodes retornou: {SolidosVazaoCombateIncendioSOL.DescreverRetornoConexao(ret)}");
                            baciasFalha++;
                            todasOk = false;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        System.Exception root = ex;
                        while (root.InnerException != null) root = root.InnerException;
                        ed.WriteMessage(
                            $"\n  [{disp.DeviceName ?? disp.DeviceHandle}] falha ao reconectar bacia {baciaHandle}: {root.Message}");
                        baciasFalha++;
                        todasOk = false;
                    }
                }

                if (okNesse > 0)
                {
                    dispositivosOk++;
                    ed.WriteMessage(
                        $"\n  ✓ {disp.DeviceName ?? disp.DeviceHandle}: {okNesse} bacia(s) reconectada(s)");
                }
                if (todasOk) dispositivosTotalmenteOk.Add(disp.DeviceHandle);
            }

            // Remove do estado os dispositivos que religaram 100%; mantém os com pendência.
            estado.Dispositivos.RemoveAll(d =>
                dispositivosTotalmenteOk.Any(h => string.Equals(h, d.DeviceHandle, StringComparison.OrdinalIgnoreCase)));

            try
            {
                if (estado.Dispositivos.Count == 0)
                    System.IO.File.Delete(estadoPath);
                else
                    estado.Salvar(estadoPath);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[AVISO] Falha ao atualizar o arquivo de mapeamento: {ex.Message}");
            }

            try { SolidosAPI.DocCommit(); } catch { }

            ed.WriteMessage(
                "\n[SOLIDOS] Resumo:" +
                $"\n  Dispositivos religados: {dispositivosOk}" +
                $"\n  Bacias reconectadas: {baciasReconectadas}" +
                $"\n  Bacias com falha: {baciasFalha}");
            if (estado.Dispositivos.Count > 0)
                ed.WriteMessage(
                    $"\n  Dispositivos com pendência no mapeamento: {estado.Dispositivos.Count}" +
                    "\n  (provavelmente porque o dispositivo/bacia foi apagado do DWG)");
            ed.WriteMessage("\n");

            if (baciasReconectadas > 0)
                SolidosDesconectarBaciasSOL.PerguntarRebuild(ed, doc);
        }

        // Procura, na mesma pasta do DWG, o .solidos-bacias.json mais recente
        // (excluindo o próprio nome esperado, que já se sabe não existir).
        private static string AcharMapeamentoMaisRecente(string estadoPathEsperado)
        {
            try
            {
                string folder = System.IO.Path.GetDirectoryName(estadoPathEsperado);
                if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder))
                    return null;

                string maisRecente = null;
                DateTime maisRecenteData = DateTime.MinValue;
                foreach (string f in System.IO.Directory.GetFiles(folder, "*.solidos-bacias.json"))
                {
                    DateTime dt = System.IO.File.GetLastWriteTime(f);
                    if (dt > maisRecenteData)
                    {
                        maisRecenteData = dt;
                        maisRecente = f;
                    }
                }
                return maisRecente;
            }
            catch
            {
                return null;
            }
        }
    }
}
