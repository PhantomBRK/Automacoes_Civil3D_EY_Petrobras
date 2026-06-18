using System.Globalization;

namespace AutomacoesCivil3D
{
    // Uma linha do relatório de dimensionamento — um registro por tubo,
    // independente do status (dimensionado ou pulado).
    public class DimensionamentoLinhaRelatorio
    {
        public string Handle { get; set; }
        public string NoMontanteFamilia { get; set; }
        public string NoMontanteHandle { get; set; }
        public string NoJusanteFamilia { get; set; }
        public string NoJusanteHandle { get; set; }
        public double? QLs { get; set; }            // L/s
        public int? DNmmAnterior { get; set; }      // DN lido ANTES do dimensionamento
        public int? DNmm { get; set; }              // DN NOVO aplicado
        public double? SlopePct { get; set; }       // % (declividade aplicada)
        public double? VMs { get; set; }            // m/s
        public double? YDPct { get; set; }          // %
        public double? ZMontante { get; set; }
        public double? ZJusante { get; set; }
        public double ComprimentoM { get; set; }
        public bool FaixaIdeal { get; set; }
        public string Status { get; set; }          // "OK" ou motivo do skip

        // --- Campos do motor novo SOL_DIMENSIONAR_DRENAGEM (2 regimes) ---
        public double? QLsIncendio { get; set; }    // L/s (Qfim = HCalcFim.Qesc)
        public double? VMsIncendio { get; set; }    // m/s no regime incêndio
        public double? YDPctIncendio { get; set; }  // % lâmina no regime incêndio
        public string RegimeGov { get; set; }       // regime que governou o (DN,i)
        public double? RecobrimentoM { get; set; }  // recobrimento resultante na montante

        public string Fmt(double? v, string fmt)
        {
            return v.HasValue ? v.Value.ToString(fmt, CultureInfo.InvariantCulture) : "—";
        }
    }
}
