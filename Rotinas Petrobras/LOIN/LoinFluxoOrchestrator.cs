using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutomacoesCivil3D
{
    // Coordena as 6 etapas do fluxo completo LOIN, expondo cada etapa como
    // método independente que retorna StageResult. Faz pré-checks antes de
    // executar (verifica arquivos existentes/atualizados, mapping com IfcClass
    // preenchida, etc.) e mantém um log consolidado.
    //
    // Etapas:
    //   1. Pset_A — abrir/preencher loin_projeto.json
    //   2. Mapeamento LOIN — abrir/preencher loin_mapeamento.json
    //   3. Code Set Styles dos corredores
    //   4. Linkar IFC Export Mapping XLSX/JSON
    //   5. Exportar sólidos com PSets LOIN
    //   6. APLICARPSETTODOS — preenche Largura/Altura/Volume/... no Pset_C unificado
    //   7. Disparar IFCEXPORT nativo no DWG destino (opcional)
    internal sealed class LoinFluxoOrchestrator
    {
        public enum StageStatus { Pending, Ok, Warning, Error, Skipped }

        public sealed class StageResult
        {
            public StageStatus Status { get; set; } = StageStatus.Pending;
            public string Message { get; set; } = string.Empty;
            public List<string> Warnings { get; } = new List<string>();
            public DateTime FinishedAt { get; set; } = DateTime.MinValue;

            public bool IsTerminalSuccess => Status == StageStatus.Ok || Status == StageStatus.Warning;
        }

        public sealed class PreCheck
        {
            public bool Ready { get; set; }
            public string Summary { get; set; } = string.Empty;
            public List<string> Details { get; } = new List<string>();
        }

        private readonly StringBuilder _log = new StringBuilder();

        public string Log => _log.ToString();

        public string DwgPath
        {
            get
            {
                try { return Manager.DocCad?.Name ?? string.Empty; }
                catch { return string.Empty; }
            }
        }

        // ---------------------------------------------------------------------
        // Pré-checks: leitura "passiva" do estado dos arquivos/recursos
        // ---------------------------------------------------------------------

        public PreCheck PreCheckProjeto()
        {
            PreCheck pc = new PreCheck();
            string path = LoinProjetoService.ResolverCaminhoConfig(DwgPath);
            if (!File.Exists(path))
            {
                pc.Ready = false;
                pc.Summary = "Não preenchido — abrir janela para configurar";
                pc.Details.Add("loin_projeto.json não existe em: " + path);
                return pc;
            }

            LoinProjetoDto dto = LoinProjetoService.Carregar(path);
            bool hasName = !string.IsNullOrWhiteSpace(dto.NomeProjeto);
            bool hasAuthor = !string.IsNullOrWhiteSpace(dto.Autor);
            pc.Ready = hasName && hasAuthor;
            pc.Summary = pc.Ready
                ? "Pronto — " + dto.NomeProjeto + " (atualizado " + dto.UltimaAlteracao.ToString("yyyy-MM-dd HH:mm") + ")"
                : "Existe, mas faltam campos essenciais (NomeProjeto/Autor)";
            pc.Details.Add("Arquivo: " + path);
            return pc;
        }

        public PreCheck PreCheckMapeamento()
        {
            PreCheck pc = new PreCheck();
            string path = LoinMapeamentoService.ResolverCaminhoConfig(DwgPath);
            if (!File.Exists(path))
            {
                pc.Ready = false;
                pc.Summary = "Não criado — abrir LOINMAP para mapear";
                pc.Details.Add("loin_mapeamento.json não existe em: " + path);
                return pc;
            }

            LoinMapeamentoConfig cfg = LoinMapeamentoService.Carregar(path);
            int totalLinhas = cfg.TabelaLoin?.Count ?? 0;
            int totalMap = cfg.Mapeamentos?.Count ?? 0;
            int comIfc = cfg.TabelaLoin?.Count(l => !string.IsNullOrWhiteSpace(l.IfcClass)) ?? 0;
            int mapeados = cfg.Mapeamentos?.Count(m => !string.IsNullOrWhiteSpace(m.LoinLinhaId)) ?? 0;

            pc.Ready = totalLinhas > 0 && mapeados > 0;
            pc.Summary = pc.Ready
                ? totalLinhas + " linhas LOIN (" + comIfc + " c/ IfcClass), " + mapeados + "/" + totalMap + " camadas mapeadas"
                : "Mapeamento vazio — abrir LOINMAP para configurar";
            pc.Details.Add("Arquivo: " + path);
            pc.Details.Add("Última alteração: " + cfg.UltimaAlteracao.ToString("yyyy-MM-dd HH:mm"));
            if (totalMap > mapeados)
                pc.Details.Add("Atenção: " + (totalMap - mapeados) + " entradas no grid sem linha LOIN associada");
            return pc;
        }

        public PreCheck PreCheckCodeSet()
        {
            PreCheck pc = new PreCheck();
            try
            {
                Autodesk.Civil.ApplicationServices.CivilDocument civilDoc = Manager.DocCivil;
                if (civilDoc == null)
                {
                    pc.Ready = false;
                    pc.Summary = "Nenhum documento Civil 3D ativo";
                    return pc;
                }

                int corridorCount = 0;
                foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId id in civilDoc.CorridorCollection)
                    corridorCount++;

                pc.Ready = corridorCount > 0;
                pc.Summary = corridorCount > 0
                    ? corridorCount + " corredor(es) encontrado(s)"
                    : "Nenhum corredor no desenho ativo";
            }
            catch (System.Exception ex)
            {
                pc.Ready = false;
                pc.Summary = "Falha ao inspecionar Civil 3D: " + ex.Message;
            }
            return pc;
        }

        public PreCheck PreCheckLinkarIfc()
        {
            PreCheck pc = new PreCheck();
            string folder = string.IsNullOrWhiteSpace(DwgPath)
                ? string.Empty
                : Path.GetDirectoryName(DwgPath) ?? string.Empty;

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                pc.Ready = false;
                pc.Summary = "Salve o DWG antes — caminho não resolvido";
                return pc;
            }

            string achado = Directory.GetFiles(folder, "*IfcInfraExportMapping*.xlsx").FirstOrDefault();
            pc.Ready = achado != null;
            pc.Summary = pc.Ready
                ? "XLSX encontrada: " + Path.GetFileName(achado)
                : "Nenhuma IfcInfraExportMapping*.xlsx na pasta do DWG — vai pedir via Open File";
            if (achado != null) pc.Details.Add(achado);
            return pc;
        }

        public PreCheck PreCheckExportarSolidos()
        {
            PreCheck pc = new PreCheck();
            PreCheck mp = PreCheckMapeamento();
            PreCheck cs = PreCheckCodeSet();
            pc.Ready = mp.Ready && cs.Ready;
            pc.Summary = pc.Ready
                ? "Pronto para exportar (LOINMAP + corredores OK)"
                : "Requer: " + (mp.Ready ? "" : "LOINMAP ") + (cs.Ready ? "" : "Corredor");
            pc.Details.AddRange(mp.Details);
            pc.Details.AddRange(cs.Details);
            return pc;
        }

        // ---------------------------------------------------------------------
        // Execução das etapas. Cada uma é stateless e retorna StageResult.
        // ---------------------------------------------------------------------

        // Constrói a string de comandos a enfileirar no AutoCAD. Chamar
        // diretamente os métodos [CommandMethod] de dentro de outro modal
        // (LoinFluxoCompletoWindow) dá "Invalid execution context" porque
        // várias rotinas abrem submodal ou pegam DocumentLock. O caminho
        // robusto é fechar a janela do fluxo e usar Document.SendStringToExecute
        // para o AutoCAD processar os comandos em sequência.
        public string BuildCommandSequence(bool run1, bool run2, bool run3, bool run4, bool run5, bool run6)
        {
            StringBuilder sb = new StringBuilder();
            if (run1) sb.Append("_LOIN_DADOS_PROJETO ");
            if (run2) sb.Append("_LOINMAP ");
            if (run3) sb.Append("_LOIN_CODESET_CORREDORES ");
            if (run4) sb.Append("_LOIN_LINKAR_IFCEXPORT ");
            if (run5) sb.Append("_EXSOLIDOSCORR_LOIN ");
            // Etapa 6: APLICARPSETTODOS roda no DWG ATUAL após a exportação.
            // Preenche Largura/Altura/Volume/Comprimento/Area no Pset_C unificado
            // dos sólidos resultantes da exportação (que continuam no model space).
            if (run6) sb.Append("_APLICARPSETTODOS ");
            return sb.ToString();
        }

        // Enfileira a sequência de comandos no AutoCAD. Retorna assim que
        // enfileirar — o AutoCAD vai processar os comandos serialmente após
        // o controle voltar para o editor (após a janela do fluxo fechar).
        public StageResult ExecutarSequencia(string commandSequence)
        {
            StageResult r = new StageResult();
            try
            {
                if (string.IsNullOrWhiteSpace(commandSequence))
                {
                    r.Status = StageStatus.Skipped;
                    r.Message = "Nenhum comando selecionado";
                    return r;
                }

                Document doc = Manager.DocCad;
                if (doc == null)
                {
                    r.Status = StageStatus.Error;
                    r.Message = "Nenhum documento ativo";
                    return r;
                }

                doc.SendStringToExecute(commandSequence, activate: true, wrapUpInactiveDoc: false, echoCommand: true);
                r.Status = StageStatus.Ok;
                r.Message = "Comandos enfileirados: " + commandSequence.Trim();
            }
            catch (System.Exception ex)
            {
                r.Status = StageStatus.Error;
                r.Message = "Erro: " + ex.Message;
            }
            finally
            {
                r.FinishedAt = DateTime.Now;
                AppendLog("Sequência de comandos", r);
            }
            return r;
        }

        // Disparo do IFCEXPORT nativo no DWG destino. O comando é do plugin
        // IFC Export Extension da Autodesk. Vamos abrir o DWG destino e enviar
        // a string "_IFCEXPORT " — o user provavelmente verá a janela do plugin.
        public StageResult ExecutarIfcExport(string destinationDwgPath)
        {
            StageResult r = new StageResult();
            try
            {
                if (string.IsNullOrWhiteSpace(destinationDwgPath) || !File.Exists(destinationDwgPath))
                {
                    r.Status = StageStatus.Warning;
                    r.Message = "DWG destino não encontrado para disparar IFCEXPORT: " + destinationDwgPath;
                    return r;
                }

                Document destDoc = ExportacaoSolidosCorredoresService.EnsureDocumentOpen(destinationDwgPath);
                if (destDoc == null)
                {
                    r.Status = StageStatus.Error;
                    r.Message = "Não foi possível abrir o DWG destino";
                    return r;
                }

                // Ativa o documento e envia o comando IFCEXPORT.
                try { AcadApp.DocumentManager.MdiActiveDocument = destDoc; } catch { }
                destDoc.SendStringToExecute("_IFCEXPORT ", activate: true, wrapUpInactiveDoc: false, echoCommand: false);

                r.Status = StageStatus.Ok;
                r.Message = "Comando IFCEXPORT enviado para o DWG destino — siga as instruções do plugin";
            }
            catch (System.Exception ex)
            {
                r.Status = StageStatus.Error;
                r.Message = "Erro: " + ex.Message;
            }
            finally
            {
                r.FinishedAt = DateTime.Now;
                AppendLog("6. IFCEXPORT nativo", r);
            }
            return r;
        }

        // ---------------------------------------------------------------------
        // Log + persistência
        // ---------------------------------------------------------------------

        private void AppendLog(string stageLabel, StageResult r)
        {
            _log.AppendLine("[" + r.FinishedAt.ToString("HH:mm:ss") + "] " + stageLabel +
                            " → " + r.Status + " — " + r.Message);
            foreach (string w in r.Warnings)
                _log.AppendLine("       · " + w);
        }

        public void SaveLogToDwgFolder()
        {
            try
            {
                string folder = string.IsNullOrWhiteSpace(DwgPath)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                    : (Path.GetDirectoryName(DwgPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                string logPath = Path.Combine(folder,
                    "loin_pipeline_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
                File.WriteAllText(logPath, _log.ToString(), Encoding.UTF8);
                Manager.DocEditor?.WriteMessage("\n[LOIN] Log do fluxo salvo em: " + logPath);
            }
            catch { /* não-fatal */ }
        }
    }
}
