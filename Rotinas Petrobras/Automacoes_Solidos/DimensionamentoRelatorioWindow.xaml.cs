using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace AutomacoesCivil3D
{
    public partial class DimensionamentoRelatorioWindow : Window
    {
        private readonly List<DimensionamentoLinhaRelatorio> _linhas;
        private List<RowVm> _rows;

        public DimensionamentoRelatorioWindow(
            IEnumerable<DimensionamentoLinhaRelatorio> linhas,
            string resumoHeader)
        {
            InitializeComponent();
            _linhas = linhas?.ToList() ?? new List<DimensionamentoLinhaRelatorio>();

            TxtResumo.Text = resumoHeader ?? string.Empty;

            // Adapta cada linha para uma row com strings formatadas (DataGrid).
            _rows = _linhas.Select(LinhaParaRow).ToList();

            // Cards de resumo.
            int ok = _rows.Count(r => r.IsOk);
            TxtTotal.Text = _rows.Count.ToString(CultureInfo.InvariantCulture);
            TxtOk.Text = ok.ToString(CultureInfo.InvariantCulture);
            TxtPulados.Text = (_rows.Count - ok).ToString(CultureInfo.InvariantCulture);

            AplicarFiltro();
        }

        private void AplicarFiltro()
        {
            bool soPulados = ChkSoPulados?.IsChecked == true;
            Grid.ItemsSource = soPulados ? _rows.Where(r => !r.IsOk).ToList() : _rows;
        }

        private void OnFiltroMudou(object sender, RoutedEventArgs e) => AplicarFiltro();

        // -------- Formatação para Grid --------

        private static RowVm LinhaParaRow(DimensionamentoLinhaRelatorio l)
        {
            return new RowVm
            {
                Handle = l.Handle,
                NoMontanteFamilia = l.NoMontanteFamilia,
                NoJusanteFamilia  = l.NoJusanteFamilia,
                QLsTxt            = l.Fmt(l.QLs, "0.00"),
                DNmmAnterior      = l.DNmmAnterior.HasValue ? l.DNmmAnterior.Value.ToString(CultureInfo.InvariantCulture) : "—",
                DNmm              = l.DNmm.HasValue ? l.DNmm.Value.ToString(CultureInfo.InvariantCulture) : "—",
                SlopePctTxt       = l.Fmt(l.SlopePct, "0.00"),
                VMsTxt            = l.Fmt(l.VMs, "0.00"),
                YDPctTxt          = l.Fmt(l.YDPct, "0.0"),
                QLsIncTxt         = l.Fmt(l.QLsIncendio, "0.00"),
                VMsIncTxt         = l.Fmt(l.VMsIncendio, "0.00"),
                YDPctIncTxt       = l.Fmt(l.YDPctIncendio, "0.0"),
                RegimeTxt         = string.IsNullOrWhiteSpace(l.RegimeGov) ? "—" : l.RegimeGov,
                ZMontanteTxt      = l.Fmt(l.ZMontante, "0.000"),
                ZJusanteTxt       = l.Fmt(l.ZJusante, "0.000"),
                ComprimentoMTxt   = l.ComprimentoM.ToString("0.00", CultureInfo.InvariantCulture),
                RecobTxt          = l.Fmt(l.RecobrimentoM, "0.00"),
                FaixaTxt          = l.Status == "OK" ? (l.FaixaIdeal ? "ideal" : "dura") : "—",
                Status            = l.Status,
                IsOk              = l.Status == "OK"
            };
        }

        public class RowVm
        {
            public bool IsOk { get; set; }
            public string Handle { get; set; }
            public string NoMontanteFamilia { get; set; }
            public string NoJusanteFamilia { get; set; }
            public string QLsTxt { get; set; }
            public string DNmmAnterior { get; set; }
            public string DNmm { get; set; }
            public string SlopePctTxt { get; set; }
            public string VMsTxt { get; set; }
            public string YDPctTxt { get; set; }
            public string QLsIncTxt { get; set; }
            public string VMsIncTxt { get; set; }
            public string YDPctIncTxt { get; set; }
            public string RegimeTxt { get; set; }
            public string ZMontanteTxt { get; set; }
            public string ZJusanteTxt { get; set; }
            public string ComprimentoMTxt { get; set; }
            public string RecobTxt { get; set; }
            public string FaixaTxt { get; set; }
            public string Status { get; set; }
        }

        // -------- CSV --------

        private string BuildCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Handle;Familia_Montante;Familia_Jusante;DN_ant_mm;DN_novo_mm;i_pct;Regime;Q_plv_Ls;V_plv_ms;YD_plv_pct;Q_inc_Ls;V_inc_ms;YD_inc_pct;Z_montante;Z_jusante;L_m;Recob_m;Status");
            foreach (var l in _linhas)
            {
                sb.Append(l.Handle ?? "");                                 sb.Append(';');
                sb.Append(Csv(l.NoMontanteFamilia));                       sb.Append(';');
                sb.Append(Csv(l.NoJusanteFamilia));                        sb.Append(';');
                sb.Append(l.DNmmAnterior.HasValue ? l.DNmmAnterior.ToString() : ""); sb.Append(';');
                sb.Append(l.DNmm.HasValue ? l.DNmm.ToString() : "");        sb.Append(';');
                sb.Append(Num(l.SlopePct, "0.00"));                        sb.Append(';');
                sb.Append(Csv(l.RegimeGov));                               sb.Append(';');
                sb.Append(Num(l.QLs, "0.00"));                             sb.Append(';');
                sb.Append(Num(l.VMs, "0.00"));                             sb.Append(';');
                sb.Append(Num(l.YDPct, "0.0"));                            sb.Append(';');
                sb.Append(Num(l.QLsIncendio, "0.00"));                     sb.Append(';');
                sb.Append(Num(l.VMsIncendio, "0.00"));                     sb.Append(';');
                sb.Append(Num(l.YDPctIncendio, "0.0"));                    sb.Append(';');
                sb.Append(Num(l.ZMontante, "0.000"));                      sb.Append(';');
                sb.Append(Num(l.ZJusante, "0.000"));                       sb.Append(';');
                sb.Append(l.ComprimentoM.ToString("0.00", CultureInfo.InvariantCulture)); sb.Append(';');
                sb.Append(Num(l.RecobrimentoM, "0.000"));                  sb.Append(';');
                sb.AppendLine(Csv(l.Status));
            }
            return sb.ToString();
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(';') || s.Contains('"') || s.Contains('\n'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static string Num(double? v, string fmt)
        {
            return v.HasValue ? v.Value.ToString(fmt, CultureInfo.InvariantCulture) : "";
        }

        // -------- Handlers --------

        private void OnCopiarClipboard(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(BuildCsv());
                MessageBox.Show(this, $"{_linhas.Count} linha(s) copiada(s).", "OK",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Falha ao copiar",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnExportarCsv(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv|Todos (*.*)|*.*",
                FileName = $"dimensionamento_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(dlg.FileName, BuildCsv(), new UTF8Encoding(true));
                MessageBox.Show(this, $"Exportado:\n{dlg.FileName}", "OK",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Falha ao exportar",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnFechar(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
