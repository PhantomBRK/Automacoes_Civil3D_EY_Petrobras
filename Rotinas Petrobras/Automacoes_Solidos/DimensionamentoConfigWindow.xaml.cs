using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace AutomacoesCivil3D
{
    public partial class DimensionamentoConfigWindow : Window
    {
        public DimensionamentoConfig Config { get; private set; }
        public bool Confirmado { get; private set; }

        public DimensionamentoConfigWindow(DimensionamentoConfig cfgInicial)
        {
            InitializeComponent();
            Config = cfgInicial ?? new DimensionamentoConfig();
            CarregarParaUI(Config);
        }

        private void CarregarParaUI(DimensionamentoConfig c)
        {
            // Slopes editados em PERCENTUAL (mais natural para o usuário).
            TxtIdealMin.Text  = (c.IdealSlopeMin * 100).ToString("0.###", CultureInfo.InvariantCulture);
            TxtIdealMax.Text  = (c.IdealSlopeMax * 100).ToString("0.###", CultureInfo.InvariantCulture);
            TxtDuraMin.Text   = (c.DuraSlopeMin  * 100).ToString("0.###", CultureInfo.InvariantCulture);
            TxtDuraMax.Text   = (c.DuraSlopeMax  * 100).ToString("0.###", CultureInfo.InvariantCulture);
            TxtVmin.Text      = c.Vmin.ToString("0.###", CultureInfo.InvariantCulture);
            TxtVmax.Text      = c.Vmax.ToString("0.###", CultureInfo.InvariantCulture);
            TxtYDmax.Text     = (c.YDmax * 100).ToString("0.###", CultureInfo.InvariantCulture);
            TxtSlopeStep.Text = (c.SlopeStep * 100).ToString("0.####", CultureInfo.InvariantCulture);
            TxtManning.Text   = c.ManningDefault.ToString("0.####", CultureInfo.InvariantCulture);
            TxtRecobrimento.Text = c.RecobrimentoMinM.ToString("0.###", CultureInfo.InvariantCulture);
            TxtFolgaSeloRalo.Text = c.FolgaSeloRaloM.ToString("0.###", CultureInfo.InvariantCulture);
            TxtCatalogo.Text  = string.Join(", ", c.CatalogoDNsMm);
        }

        private bool TentarLerUI(out DimensionamentoConfig c, out string erro)
        {
            c = new DimensionamentoConfig();
            erro = null;
            try
            {
                c.IdealSlopeMin  = ParsePct(TxtIdealMin.Text, "i mín ideal");
                c.IdealSlopeMax  = ParsePct(TxtIdealMax.Text, "i máx ideal");
                c.DuraSlopeMin   = ParsePct(TxtDuraMin.Text,  "i mín dura");
                c.DuraSlopeMax   = ParsePct(TxtDuraMax.Text,  "i máx dura");
                c.Vmin           = ParseNumber(TxtVmin.Text,  "V mín");
                c.Vmax           = ParseNumber(TxtVmax.Text,  "V máx");
                c.YDmax          = ParsePct(TxtYDmax.Text,    "Y/D máx");
                c.SlopeStep      = ParsePct(TxtSlopeStep.Text,"passo i");
                c.ManningDefault = ParseNumber(TxtManning.Text,"Manning default");
                c.RecobrimentoMinM = ParseNumber(TxtRecobrimento.Text, "recobrimento mín");
                c.FolgaSeloRaloM = ParseNumber(TxtFolgaSeloRalo.Text, "folga selo ralo");
                c.CatalogoDNsMm  = ParseCatalogo(TxtCatalogo.Text);

                // Validações leves.
                if (c.IdealSlopeMin <= 0 || c.IdealSlopeMax <= c.IdealSlopeMin)
                    throw new Exception("Faixa ideal inválida (min < max e ambos > 0).");
                if (c.DuraSlopeMax  <= c.DuraSlopeMin)
                    throw new Exception("Faixa dura inválida (min < max).");
                if (c.Vmin <= 0 || c.Vmax <= c.Vmin)
                    throw new Exception("Velocidade inválida (Vmin < Vmax e ambos > 0).");
                if (c.YDmax <= 0 || c.YDmax > 1)
                    throw new Exception("Y/D máx fora de (0, 100]%.");
                if (c.SlopeStep <= 0)
                    throw new Exception("Passo i precisa ser > 0.");
                if (c.ManningDefault <= 0)
                    throw new Exception("Manning default precisa ser > 0.");
                if (c.RecobrimentoMinM < 0)
                    throw new Exception("Recobrimento mínimo não pode ser negativo.");
                if (c.FolgaSeloRaloM < 0)
                    throw new Exception("Folga do selo do ralo não pode ser negativa.");
                if (c.CatalogoDNsMm.Count == 0)
                    throw new Exception("Catálogo de DNs vazio.");

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

        private static double ParsePct(string txt, string nome)
        {
            // Entrada em PERCENTUAL → retorna em fração.
            return ParseNumber(txt, nome) / 100.0;
        }

        private static List<int> ParseCatalogo(string txt)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(txt)) return result;
            foreach (string raw in txt.Split(new[] { ',', ';', ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0)
                {
                    if (!result.Contains(n)) result.Add(n);
                }
            }
            result.Sort();
            return result;
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
