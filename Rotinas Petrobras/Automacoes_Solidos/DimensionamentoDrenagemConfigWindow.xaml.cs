using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;

namespace AutomacoesCivil3D
{
    // Janela de parâmetros do motor SOL_DIMENSIONAR_DRENAGEM. Dois regimes
    // (pluvial + incêndio) e FAIXA DINÂMICA DE DIÂMETROS: o usuário marca, a cada
    // execução, quais DNs do catálogo SOLIDOS ficam disponíveis (contígua via De/Até
    // ou subconjunto arbitrário pelos checkboxes).
    public partial class DimensionamentoDrenagemConfigWindow : Window
    {
        public DimensionamentoConfig Config { get; private set; }
        public bool Confirmado { get; private set; }

        // DNs suportados pelo template SOLIDOS (.sbd Petrobras) → 1 checkbox cada.
        private readonly List<int> _dnsSuportados = CatalogoTuboPadrao.DNsMm.OrderBy(d => d).ToList();
        private readonly Dictionary<int, CheckBox> _chkDNs = new Dictionary<int, CheckBox>();

        public DimensionamentoDrenagemConfigWindow(DimensionamentoConfig cfgInicial)
        {
            InitializeComponent();
            Config = cfgInicial ?? new DimensionamentoConfig();
            MontarCheckboxesDN();
            MontarCombosFaixa();
            CarregarParaUI(Config);
        }

        private void MontarCheckboxesDN()
        {
            PanelDNs.Children.Clear();
            _chkDNs.Clear();
            foreach (int dn in _dnsSuportados)
            {
                var chk = new CheckBox
                {
                    Content = dn.ToString(CultureInfo.InvariantCulture),
                    Tag = dn,
                    MinWidth = 78,
                    Margin = new Thickness(0, 2, 16, 2),
                    VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                    FontSize = 13
                };
                _chkDNs[dn] = chk;
                PanelDNs.Children.Add(chk);
            }
        }

        private void MontarCombosFaixa()
        {
            CboDe.ItemsSource = _dnsSuportados.ToList();
            CboAte.ItemsSource = _dnsSuportados.ToList();
            if (_dnsSuportados.Count > 0)
            {
                CboDe.SelectedItem = _dnsSuportados.First();
                CboAte.SelectedItem = _dnsSuportados.Last();
            }
        }

        private void CarregarParaUI(DimensionamentoConfig c)
        {
            // Pluvial.
            TxtVmin.Text  = c.Vmin.ToString("0.###", CultureInfo.InvariantCulture);
            TxtVmax.Text  = c.Vmax.ToString("0.###", CultureInfo.InvariantCulture);
            TxtYDmax.Text = (c.YDmax * 100).ToString("0.###", CultureInfo.InvariantCulture);

            // Incêndio.
            ChkIncendio.IsChecked = c.IncendioAtivo;
            TxtIncVmax.Text  = c.IncendioVmax.ToString("0.###", CultureInfo.InvariantCulture);
            TxtIncYDmax.Text = (c.IncendioYDmax * 100).ToString("0.###", CultureInfo.InvariantCulture);

            // Declividade / material / cotas.
            TxtSlopeMin.Text  = (c.DuraSlopeMin * 100).ToString("0.###", CultureInfo.InvariantCulture);
            TxtSlopeMax.Text  = (c.DuraSlopeMax * 100).ToString("0.###", CultureInfo.InvariantCulture);
            TxtSlopeStep.Text = (c.SlopeStep * 100).ToString("0.####", CultureInfo.InvariantCulture);
            TxtManning.Text   = c.ManningDefault.ToString("0.####", CultureInfo.InvariantCulture);
            TxtRecobrimento.Text = c.RecobrimentoMinDrenM.ToString("0.###", CultureInfo.InvariantCulture);
            ChkPontoBaixo.IsChecked = c.JuncaoPontoBaixo;
            ChkManterCotas.IsChecked = c.ManterCotasDeclividade;

            // Catálogo: marca os DNs salvos (interseção com os suportados).
            var ativos = new HashSet<int>(c.CatalogoDNsMm ?? new List<int>());
            bool algum = false;
            foreach (var kv in _chkDNs)
            {
                kv.Value.IsChecked = ativos.Contains(kv.Key);
                algum |= kv.Value.IsChecked == true;
            }
            // Se nada coincidiu (config antiga/vazia), marca todos.
            if (!algum) foreach (var kv in _chkDNs) kv.Value.IsChecked = true;
        }

        private List<int> DNsMarcados()
        {
            return _chkDNs.Where(kv => kv.Value.IsChecked == true)
                          .Select(kv => kv.Key).OrderBy(d => d).ToList();
        }

        private bool TentarLerUI(out DimensionamentoConfig c, out string erro)
        {
            // Parte da config atual para preservar campos não editados aqui.
            c = DimensionamentoConfig.Carregar();
            erro = null;
            try
            {
                c.Vmin  = ParseNumber(TxtVmin.Text, "V mín (pluvial)");
                c.Vmax  = ParseNumber(TxtVmax.Text, "V máx (pluvial)");
                c.YDmax = ParsePct(TxtYDmax.Text,   "Y/D máx (pluvial)");

                c.IncendioAtivo = ChkIncendio.IsChecked == true;
                c.IncendioVmax  = ParseNumber(TxtIncVmax.Text, "V máx (incêndio)");
                c.IncendioYDmax = ParsePct(TxtIncYDmax.Text,   "Y/D máx (incêndio)");

                c.DuraSlopeMin = ParsePct(TxtSlopeMin.Text, "i mín");
                c.DuraSlopeMax = ParsePct(TxtSlopeMax.Text, "i máx");
                c.SlopeStep    = ParsePct(TxtSlopeStep.Text, "passo i");
                c.ManningDefault = ParseNumber(TxtManning.Text, "Manning default");
                c.RecobrimentoMinDrenM = ParseNumber(TxtRecobrimento.Text, "recobrimento mín");
                c.JuncaoPontoBaixo = ChkPontoBaixo.IsChecked == true;
                c.ManterCotasDeclividade = ChkManterCotas.IsChecked == true;

                c.CatalogoDNsMm = DNsMarcados();

                // Validações.
                if (c.Vmin <= 0 || c.Vmax <= c.Vmin)
                    throw new Exception("Velocidade pluvial inválida (0 < Vmín < Vmáx).");
                if (c.YDmax <= 0 || c.YDmax > 1)
                    throw new Exception("Y/D máx pluvial fora de (0, 100]%.");
                if (c.IncendioAtivo)
                {
                    if (c.IncendioVmax <= 0)
                        throw new Exception("V máx do incêndio precisa ser > 0.");
                    if (c.IncendioYDmax <= 0 || c.IncendioYDmax > 1)
                        throw new Exception("Y/D máx incêndio fora de (0, 100]%.");
                }
                if (c.DuraSlopeMin <= 0 || c.DuraSlopeMax <= c.DuraSlopeMin)
                    throw new Exception("Faixa de declividade inválida (0 < i mín < i máx).");
                if (c.SlopeStep <= 0)
                    throw new Exception("Passo de declividade precisa ser > 0.");
                if (c.ManningDefault <= 0)
                    throw new Exception("Manning default precisa ser > 0.");
                if (c.RecobrimentoMinDrenM < 0)
                    throw new Exception("Recobrimento mínimo não pode ser negativo.");
                if (c.CatalogoDNsMm.Count == 0)
                    throw new Exception("Selecione ao menos um diâmetro (DN).");

                return true;
            }
            catch (Exception ex)
            {
                erro = ex.Message;
                return false;
            }
        }

        private static double ParseNumber(string txt, string nome)
        {
            string t = (txt ?? "").Replace(",", ".").Trim();
            if (double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) return v;
            throw new Exception($"Valor inválido em '{nome}': '{txt}'");
        }

        private static double ParsePct(string txt, string nome) => ParseNumber(txt, nome) / 100.0;

        // ---- handlers ----

        private void OnAplicarFaixa(object sender, RoutedEventArgs e)
        {
            if (CboDe.SelectedItem is int de && CboAte.SelectedItem is int ate)
            {
                int lo = Math.Min(de, ate), hi = Math.Max(de, ate);
                foreach (var kv in _chkDNs)
                    kv.Value.IsChecked = (kv.Key >= lo && kv.Key <= hi);
            }
        }

        private void OnMarcarTodos(object sender, RoutedEventArgs e)
        {
            foreach (var kv in _chkDNs) kv.Value.IsChecked = true;
        }

        private void OnDimensionar(object sender, RoutedEventArgs e)
        {
            if (!TentarLerUI(out var c, out string erro))
            {
                MessageBox.Show(this, erro, "Entrada inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Config = c;
            Config.Salvar();
            Confirmado = true;
            DialogResult = true;
            Close();
        }

        private void OnCancelar(object sender, RoutedEventArgs e)
        {
            Confirmado = false;
            DialogResult = false;
            Close();
        }

        private void OnRestaurarDefault(object sender, RoutedEventArgs e)
        {
            CarregarParaUI(new DimensionamentoConfig());
        }
    }
}
