using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace AutomacoesCivil3D
{
    internal partial class LoinProjetoWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly string _caminhoConfig;
        private LoinProjeto _projeto;

        public LoinProjeto Projeto
        {
            get => _projeto;
            private set { _projeto = value; OnPropertyChanged(); }
        }

        public string CaminhoConfigExibido => _caminhoConfig;

        internal LoinProjetoWindow(LoinProjetoDto dto, string caminhoConfig)
        {
            _caminhoConfig = caminhoConfig;
            _projeto = LoinProjetoService.DtoParaVm(dto ?? new LoinProjetoDto());

            // Default sensato em campos vazios na primeira abertura
            if (string.IsNullOrWhiteSpace(_projeto.Data))
                _projeto.Data = DateTime.Now.ToString("yyyy-MM-dd");

            InitializeComponent();
            DataContext = this;
        }

        private void Salvar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoinProjetoService.Salvar(_caminhoConfig, LoinProjetoService.VmParaDto(_projeto));
                Manager.DocEditor.WriteMessage($"\nDados do projeto LOIN salvos em: {_caminhoConfig}");
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao salvar:\n" + ex.Message, "Dados do Projeto",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
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
