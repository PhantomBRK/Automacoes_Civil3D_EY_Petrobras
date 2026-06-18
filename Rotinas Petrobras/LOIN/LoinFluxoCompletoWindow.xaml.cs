using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
// "Brush" e "Brushes" tem versões em System.Drawing (WinForms) e em
// System.Windows.Media (WPF). Aliasamos explicitamente para WPF — único
// que faz sentido dentro de uma Window WPF.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace AutomacoesCivil3D
{
    internal partial class LoinFluxoCompletoWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly LoinFluxoOrchestrator _orch = new LoinFluxoOrchestrator();

        // ----- estado dos checkboxes -----
        // Default: 1-6 marcados (APLICARPSETTODOS roda junto), IFCEXPORT (Run7) desmarcado.
        private bool _run1 = true, _run2 = true, _run3 = true, _run4 = true, _run5 = true,
                     _run6 = true, _run7 = false;
        public bool Run1 { get => _run1; set { _run1 = value; OnPropertyChanged(); } }
        public bool Run2 { get => _run2; set { _run2 = value; OnPropertyChanged(); } }
        public bool Run3 { get => _run3; set { _run3 = value; OnPropertyChanged(); } }
        public bool Run4 { get => _run4; set { _run4 = value; OnPropertyChanged(); } }
        public bool Run5 { get => _run5; set { _run5 = value; OnPropertyChanged(); } }
        public bool Run6 { get => _run6; set { _run6 = value; OnPropertyChanged(); } }
        public bool Run7 { get => _run7; set { _run7 = value; OnPropertyChanged(); } }

        // ----- status descritivo de cada etapa -----
        private string _status1 = "", _status2 = "", _status3 = "", _status4 = "", _status5 = "",
                       _status6 = "", _status7 = "—";
        public string Status1 { get => _status1; set { _status1 = value; OnPropertyChanged(); } }
        public string Status2 { get => _status2; set { _status2 = value; OnPropertyChanged(); } }
        public string Status3 { get => _status3; set { _status3 = value; OnPropertyChanged(); } }
        public string Status4 { get => _status4; set { _status4 = value; OnPropertyChanged(); } }
        public string Status5 { get => _status5; set { _status5 = value; OnPropertyChanged(); } }
        public string Status6 { get => _status6; set { _status6 = value; OnPropertyChanged(); } }
        public string Status7 { get => _status7; set { _status7 = value; OnPropertyChanged(); } }

        // ----- cor de cada status -----
        private Brush _b1 = Brushes.Gray, _b2 = Brushes.Gray, _b3 = Brushes.Gray,
                      _b4 = Brushes.Gray, _b5 = Brushes.Gray, _b6 = Brushes.Gray,
                      _b7 = Brushes.Gray;
        public Brush Status1Brush { get => _b1; set { _b1 = value; OnPropertyChanged(); } }
        public Brush Status2Brush { get => _b2; set { _b2 = value; OnPropertyChanged(); } }
        public Brush Status3Brush { get => _b3; set { _b3 = value; OnPropertyChanged(); } }
        public Brush Status4Brush { get => _b4; set { _b4 = value; OnPropertyChanged(); } }
        public Brush Status5Brush { get => _b5; set { _b5 = value; OnPropertyChanged(); } }
        public Brush Status6Brush { get => _b6; set { _b6 = value; OnPropertyChanged(); } }
        public Brush Status7Brush { get => _b7; set { _b7 = value; OnPropertyChanged(); } }

        private string _logTexto = "Aguardando execução. Use 'Atualizar pré-checks' para inspecionar o estado atual antes de rodar.";
        public string LogTexto { get => _logTexto; set { _logTexto = value; OnPropertyChanged(); } }

        private string _statusGeral = "Pronto";
        public string StatusGeral { get => _statusGeral; set { _statusGeral = value; OnPropertyChanged(); } }

        private string _caminhoLog = "";
        public string CaminhoLog { get => _caminhoLog; set { _caminhoLog = value; OnPropertyChanged(); } }

        internal LoinFluxoCompletoWindow()
        {
            InitializeComponent();
            DataContext = this;
            AtualizarPreChecks();
        }

        // ---------- pré-checks ----------

        private void Atualizar_Click(object sender, RoutedEventArgs e) => AtualizarPreChecks();

        private void AtualizarPreChecks()
        {
            ApplyPre(_orch.PreCheckProjeto(),       s => Status1 = s, b => Status1Brush = b);
            ApplyPre(_orch.PreCheckMapeamento(),    s => Status2 = s, b => Status2Brush = b);
            ApplyPre(_orch.PreCheckCodeSet(),       s => Status3 = s, b => Status3Brush = b);
            ApplyPre(_orch.PreCheckLinkarIfc(),     s => Status4 = s, b => Status4Brush = b);
            ApplyPre(_orch.PreCheckExportarSolidos(), s => Status5 = s, b => Status5Brush = b);
            Status6 = Run6
                ? "Vai rodar APLICARPSETTODOS no DWG atual após etapa 5 — preenche Largura/Altura/Volume no Pset_C unificado"
                : "Não marcado — Pset_C ficará sem dimensões preenchidas";
            Status6Brush = Run6 ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)) : Brushes.Gray;
            Status7 = Run7 ? "Vai disparar IFCEXPORT no DWG destino após etapa 5" : "Não marcado — passo final manual";
            Status7Brush = Run7 ? Brushes.DarkOrange : Brushes.Gray;
            StatusGeral = "Pré-checks atualizados em " + DateTime.Now.ToString("HH:mm:ss");
        }

        private void ApplyPre(LoinFluxoOrchestrator.PreCheck pc, Action<string> setText, Action<Brush> setBrush)
        {
            setText(pc.Summary);
            setBrush(pc.Ready ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
                              : new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00)));
        }

        // ---------- atalhos para abrir janelas das etapas individualmente ----------
        // Os botões "Abrir agora" enfileiram o comando real (não chamam o método
        // diretamente — isso quebra ao tentar abrir modal dentro de modal).

        private void AbrirProjeto_Click(object sender, RoutedEventArgs e)
            => EnfileirarEFechar("_LOIN_DADOS_PROJETO ");

        private void AbrirMapeamento_Click(object sender, RoutedEventArgs e)
            => EnfileirarEFechar("_LOINMAP ");

        private void EnfileirarEFechar(string cmd)
        {
            try
            {
                Manager.DocCad?.SendStringToExecute(cmd, true, false, true);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                StatusGeral = "Erro ao enfileirar comando: " + ex.Message;
            }
        }

        // ---------- execução em lote ----------
        //
        // Estratégia: monta a sequência de comandos selecionados, fecha a janela
        // do fluxo e usa Document.SendStringToExecute para o AutoCAD processar
        // os comandos em série. Os comandos individuais (LOIN_PROJ, LOINMAP, etc.)
        // abrem suas próprias janelas modais sem conflito de contexto.
        //
        // Etapa 7 (IFCEXPORT no DWG destino) NÃO entra na sequência porque o DWG
        // destino só vai existir DEPOIS da etapa 5 rodar — e a sequência de comandos
        // é processada toda no contexto do DWG atual. O usuário dispara IFCEXPORT
        // manualmente no DWG destino após a etapa 5 finalizar.
        // Etapa 6 (APLICARPSETTODOS) entra na sequência: roda no DWG atual após
        // a exportação. Se os sólidos finais ficarem só no DWG destino, o usuário
        // deve rodar _APLICARPSETTODOS manualmente lá também (avisado por MessageBox).
        private void Executar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusGeral = "Enfileirando comandos...";

                string seq = _orch.BuildCommandSequence(Run1, Run2, Run3, Run4, Run5, Run6);
                if (string.IsNullOrWhiteSpace(seq))
                {
                    StatusGeral = "Nada para executar — marque ao menos uma etapa";
                    return;
                }

                LoinFluxoOrchestrator.StageResult res = _orch.ExecutarSequencia(seq);
                LogTexto = _orch.Log;
                _orch.SaveLogToDwgFolder();

                if (res.Status == LoinFluxoOrchestrator.StageStatus.Error)
                {
                    StatusGeral = res.Message;
                    return;
                }

                // Avisa sobre etapa 6 (APLICARPSETTODOS) se marcada — pode precisar
                // rodar manualmente no DWG destino se os sólidos não ficarem no atual.
                if (Run6)
                {
                    System.Windows.MessageBox.Show(
                        "Etapa 6 (APLICARPSETTODOS) foi enfileirada para rodar no DWG atual.\n\n" +
                        "Se os sólidos finais ficarem apenas no DWG destino (gerado pela etapa 5), " +
                        "abra-o e rode _APLICARPSETTODOS lá também para preencher Largura/Altura/Volume " +
                        "no Pset_C - Propriedades Fisicas dos Objetos.",
                        "Fluxo LOIN — APLICARPSETTODOS",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }

                // Avisa sobre etapa 7 (IFCEXPORT) se marcada.
                if (Run7)
                {
                    System.Windows.MessageBox.Show(
                        "Etapa 7 (IFCEXPORT) será disparada manualmente no DWG destino " +
                        "depois que a etapa 5 terminar de gerar o DWG.\n\n" +
                        "Após a exportação concluir, abra o DWG destino e rode IFCEXPORT.",
                        "Fluxo LOIN — IFCEXPORT manual",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }

                // Fecha a janela do fluxo — o AutoCAD vai processar os comandos
                // assim que o controle retornar ao Editor.
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                StatusGeral = "Erro fatal: " + ex.Message;
                LogTexto = _orch.Log + Environment.NewLine + "[ERRO] " + ex.Message;
            }
        }

        private void Cancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        protected void OnPropertyChanged([CallerMemberName] string? prop = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
