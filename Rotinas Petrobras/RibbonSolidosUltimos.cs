using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;                 // Ribbon API
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutomacoesCivil3D.Ribbons
{
    /// <summary>
    /// Ribbon "SOLIDOS QTO" — reúne os comandos desenvolvidos nos últimos 3 dias
    /// (drenagem SOLIDOS: dimensionamento, quantitativos, QTO em dispositivos, seções,
    /// diagnóstico). Mesmo padrão da ribbon de PSets (RP_RIBBON em EXPORTAR_PSETS.cs):
    /// Autodesk.Windows + ComponentManager.Ribbon, botões disparam o comando via
    /// SendStringToExecute.
    ///
    /// Comando: SOL_RIBBON  (monta/atualiza a aba e a deixa ativa).
    /// </summary>
    public class RibbonSolidosUltimos
    {
        private const string TabTitle = "SOLIDOS QTO";
        private const string TabId = "SolidosQtoTab_Ultimos3Dias";

        // (Rótulo do botão, comando AutoCAD, dica/tooltip). Agrupado por painel.
        private sealed class Btn
        {
            public string Label, Cmd, Tip;
            public Btn(string label, string cmd, string tip) { Label = label; Cmd = cmd; Tip = tip; }
        }

        private static readonly (string Painel, Btn[] Botoes)[] PAINEIS = new[]
        {
            ("Dimensionamento", new[]
            {
                new Btn("Dimensionar\nJusante", "SOL_DIMENSIONAR_REDE_POR_JUSANTE",
                        "Dimensiona a rede por gravidade no sentido de jusante (modo simples: lê Qcalc do tubo)."),
                new Btn("Diagnosticar\nRede", "SOL_DIAGNOSTICAR_CONECTIVIDADE",
                        "Diagnostica a conectividade da rede de drenagem SOLIDOS."),
            }),

            ("Quantitativo Geral", new[]
            {
                new Btn("QUANT.\nGERAL", "SOL_QUANT_GERAL",
                        "TUDO de uma vez: gera 2 arquivos — QUANTITATIVO (memória: tubos, caixas, canaletas) e FORMULÁRIO SMEC (todos os dispositivos + válvulas/conexões/coletores)."),
            }),

            ("Quantitativos (individual)", new[]
            {
                new Btn("Quant.\nTubos", "SOL_QUANT_TUBOS",
                        "Quantitativos dos tubos da rede (memória)."),
                new Btn("Quant. Tubos\nSMEC", "SOL_QUANT_TUBOS_SMEC",
                        "Tubos no padrão SMEC (FORMULÁRIO)."),
                new Btn("Quant.\nCaixas", "SOL_QUANT_CAIXAS",
                        "Quantitativos das caixas (memória)."),
                new Btn("Quant.\nCanaletas", "SOL_QUANT_CANAL",
                        "Quantitativos das canaletas (memória: CANALET_Pluv/Cont/Oleo)."),
                new Btn("Quant. Canal\nSMEC", "SOL_QUANT_CANAL_SMEC",
                        "Canaletas no padrão SMEC (FORMULÁRIO, família CANAIS E CANALETAS)."),
                new Btn("Tubos\nFantasmas", "SOL_LISTAR_TUBOS_FANTASMAS",
                        "Lista tubos sem conexão / órfãos (fantasmas)."),
                new Btn("Listar\nPropriedades", "SOL_LISTAR_PROPS",
                        "Lista as propriedades dinâmicas do dispositivo selecionado."),
            }),

            ("QTO em Dispositivos (.sbd)", new[]
            {
                new Btn("QTO\nCanaleta", "AddQtoSmecCanaleta",
                        "Insere o pacote QTO SMEC CANALETA (cálculos + variáveis) num .sbd de canaleta, 1 por vez."),
                new Btn("QTO Caixas\n(Lote)", "AddQtoSmecEmLote",
                        "Padroniza a sequência QTO SMEC em lote nos .sbd de caixas/ralos."),
                new Btn("Variáveis\nGlobais Caixa", "AddVariaveisGlobaisQtoSmecCaixa",
                        "Cria as DynamicProperties de saída (variáveis globais) faltantes numa caixa."),
            }),

            ("Seções / Projeção", new[]
            {
                new Btn("Seção\nBueiro", "SOL_SECAO_BUEIRO",
                        "Gera seção + projeção de um bueiro."),
                new Btn("Seções\nBueiros", "SOL_SECAO_BUEIROS",
                        "Gera seções + projeção de vários bueiros (lote)."),
                new Btn("Spike\nProjeção", "SOL_SPIKE_PROJECAO",
                        "Spike de projeção de dispositivo em section view."),
                new Btn("Spike\nProjeção PF", "SOL_SPIKE_PROJECAO_PF",
                        "Variante PF do spike de projeção."),
            }),

            ("Diagnóstico", new[]
            {
                new Btn("Dump XML\n(Arquivo)", "DumpSolidosXml",
                        "Despeja todos os XRecords SOLIDOS de um .sbd num .txt legível."),
                new Btn("Dump XML\n(Ativo)", "DumpSolidosXmlAtivo",
                        "Dump dos XRecords SOLIDOS do desenho ativo."),
                new Btn("Dump XML\n(Forçado)", "DumpSolidosXmlForcado",
                        "Dump forçado dos XRecords SOLIDOS."),
            }),
        };

        [CommandMethod("SOL_RIBBON", CommandFlags.Session)]
        public void BuildRibbon()
        {
            var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
            try
            {
                var rc = ComponentManager.Ribbon;
                if (rc == null)
                {
                    ed?.WriteMessage("\n[SOL_RIBBON] Ribbon indisponível neste ambiente.");
                    return;
                }

                // Acha ou cria a aba
                var tab = rc.Tabs.FirstOrDefault(t => t.Id == TabId)
                          ?? new RibbonTab { Title = TabTitle, Id = TabId };
                if (!rc.Tabs.Contains(tab)) rc.Tabs.Add(tab);

                // Recria painéis (idempotente)
                tab.Panels.Clear();

                int totalBtns = 0;
                foreach (var (painel, botoes) in PAINEIS)
                {
                    var src = new RibbonPanelSource { Title = painel };
                    var panel = new RibbonPanel { Source = src };
                    tab.Panels.Add(panel);

                    foreach (var b in botoes)
                    {
                        var btn = new RibbonButton
                        {
                            Text = b.Label,
                            ShowText = true,
                            ShowImage = false,
                            Size = RibbonItemSize.Large,
                            Orientation = System.Windows.Controls.Orientation.Vertical,
                            ToolTip = $"{b.Tip}\n\nComando: {b.Cmd}",
                            CommandHandler = new SolRibbonCmdHandler(b.Cmd)
                        };
                        src.Items.Add(btn);
                        totalBtns++;
                    }
                }

                tab.IsActive = true;
                ed?.WriteMessage($"\n[SOL_RIBBON] Aba \"{TabTitle}\" criada: {PAINEIS.Length} painéis, {totalBtns} comandos.");
            }
            catch (System.Exception ex)
            {
                ed?.WriteMessage($"\n[SOL_RIBBON] Erro ao montar a ribbon: {ex.Message}");
            }
        }
    }

    /// <summary>Dispara o comando AutoCAD do botão (como se digitado).</summary>
    internal sealed class SolRibbonCmdHandler : System.Windows.Input.ICommand
    {
        private readonly string _cmd;
        public SolRibbonCmdHandler(string cmd) => _cmd = cmd;
        public bool CanExecute(object parameter) => true;
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public void Execute(object parameter)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc?.SendStringToExecute(_cmd + " ", true, false, true);
        }
    }
}
