using System;
using System.Collections.Generic;
using System.Linq;

namespace AutomacoesCivil3D
{
    public enum TipoSecaoHid
    {
        Circular,
        Retangular,
        Estrutura
    }

    public sealed class RegrasDimensionamento
    {
        public double IdealSlopeMin { get; set; } = 0.002;   // 0.2%
        public double IdealSlopeMax { get; set; } = 0.025;   // 2.5%
        public double DuraSlopeMin  { get; set; } = 0.002;   // 0.2%
        public double DuraSlopeMax  { get; set; } = 0.040;   // 4.0%
        public double Vmin          { get; set; } = 0.60;    // m/s
        public double Vmax          { get; set; } = 2.00;    // m/s
        public double YDmax         { get; set; } = 0.67;    // lâmina máxima
        public double SlopeStep     { get; set; } = 0.0005;  // 0.05% por iter
    }

    public sealed class SolucaoTubo
    {
        public double D     { get; set; }   // m
        public double Slope { get; set; }   // m/m
        public double YD    { get; set; }   // ratio
        public double V     { get; set; }   // m/s
        public bool FaixaIdeal { get; set; }
    }

    public static class HidraulicaSolidos
    {
        public static TipoSecaoHid ClassificarSecao(string familia)
        {
            string f = (familia ?? string.Empty).ToUpperInvariant();
            if (f.Contains("CANALETA") || f.Contains("VALETA") || f.Contains("GALERIA") ||
                f.Contains("GUTTER")   || f.Contains("CONDUITO RETANGULAR"))
            {
                return TipoSecaoHid.Retangular;
            }
            if (f.Contains("CAIXA") || f.Contains("PV") || f.Contains("POCO") ||
                f.Contains("CHAMBER") || f.Contains("MANHOLE") || f.Contains("SUMP") ||
                f.Contains("RALO") || f.Contains("BOCA") || f.Contains("BL") ||
                f.Contains("FUNIL") || f.Contains("INSPECAO"))
            {
                return TipoSecaoHid.Estrutura;
            }
            return TipoSecaoHid.Circular;
        }

        public static double ManningPadrao(TipoSecaoHid tipo, string familia)
        {
            string f = familia?.ToUpperInvariant() ?? string.Empty;
            if (f.Contains("CONCRETO")) return 0.013;
            if (f.Contains("PVC") || f.Contains("PEAD") || f.Contains("HDPE")) return 0.011;
            if (f.Contains("METAL") || f.Contains("ACO")) return 0.012;
            if (tipo == TipoSecaoHid.Retangular) return 0.015;
            return 0.013;
        }

        // Manning a seção plena. Retorna (Qp, Vp) em m³/s e m/s.
        public static (double? Qp, double? Vp) ManningPlenaCircular(double D, double n, double i)
        {
            if (D <= 0 || n <= 0 || i <= 0) return (null, null);
            double area = Math.PI * D * D / 4.0;
            double rh   = D / 4.0;
            double v    = (1.0 / n) * Math.Pow(rh, 2.0 / 3.0) * Math.Sqrt(i);
            return (v * area, v);
        }

        // Inverte função adimensional de Manning circular: q/Qp -> Y/D (busca binária).
        public static double? YsobreD_Circular(double qQp)
        {
            if (qQp <= 0) return 0.0;
            if (qQp >= 1.0) return 1.0;

            double lo = 0, hi = 1.0;
            for (int k = 0; k < 60; k++)
            {
                double mid   = (lo + hi) / 2;
                double theta = 2 * Math.Acos(Math.Max(-1, Math.Min(1, 1 - 2 * mid)));
                double aRat  = (theta - Math.Sin(theta)) / (2 * Math.PI);
                double rhRat = theta > 0 ? (theta - Math.Sin(theta)) / theta : 0;
                double ratio = aRat * Math.Pow(rhRat, 2.0 / 3.0);
                if (Math.Abs(ratio - qQp) < 1e-7) return mid;
                if (ratio < qQp) lo = mid; else hi = mid;
            }
            return (lo + hi) / 2;
        }

        // Dimensionamento de tubo circular pra atender Q sob as regras.
        // dnCandidatos: diâmetros disponíveis no catálogo, em METROS, ordem indiferente.
        // Estratégia: preferir manter D pequeno (escavação menor) e menor i (declividade
        // baixa = menos profundidade jusante). Faixa ideal primeiro; se não der, expande.
        public static SolucaoTubo DimensionarCircular(
            double Q,
            double n,
            IEnumerable<double> dnCandidatos,
            RegrasDimensionamento regras)
        {
            if (Q <= 0 || n <= 0 || dnCandidatos == null) return null;

            List<double> DNs = dnCandidatos.Where(d => d > 0).OrderBy(d => d).Distinct().ToList();
            if (DNs.Count == 0) return null;

            // Tentativa 1: faixa ideal
            foreach (double D in DNs)
            {
                SolucaoTubo s = BuscarSlopeViavelCircular(
                    Q, n, D,
                    regras.IdealSlopeMin, regras.IdealSlopeMax,
                    regras.Vmin, regras.Vmax, regras.YDmax, regras.SlopeStep);
                if (s != null)
                {
                    s.FaixaIdeal = true;
                    return s;
                }
            }

            // Tentativa 2: faixa dura
            foreach (double D in DNs)
            {
                SolucaoTubo s = BuscarSlopeViavelCircular(
                    Q, n, D,
                    regras.DuraSlopeMin, regras.DuraSlopeMax,
                    regras.Vmin, regras.Vmax, regras.YDmax, regras.SlopeStep);
                if (s != null)
                {
                    s.FaixaIdeal = false;
                    return s;
                }
            }

            return null;
        }

        // Varre i no intervalo dado, retorna a MENOR declividade viável (D, n, Q fixos).
        // Viável = Y/D ≤ YDmax E Vmin ≤ V ≤ Vmax.
        private static SolucaoTubo BuscarSlopeViavelCircular(
            double Q, double n, double D,
            double iMin, double iMax,
            double Vmin, double Vmax, double YDmax, double passo)
        {
            double area_plena = Math.PI * D * D / 4.0;
            double rh_plena   = D / 4.0;
            double k_v        = (1.0 / n) * Math.Pow(rh_plena, 2.0 / 3.0);

            for (double i = iMin; i <= iMax + 1e-12; i += passo)
            {
                double Vp = k_v * Math.Sqrt(i);
                double Qp = Vp * area_plena;
                if (Qp <= 0) continue;

                double ratio = Q / Qp;
                if (ratio >= 1.0) continue; // não cabe nem na plena

                double? yd = YsobreD_Circular(ratio);
                if (!yd.HasValue || yd.Value <= 0) continue;
                if (yd.Value > YDmax) continue;

                double theta = 2 * Math.Acos(Math.Max(-1, Math.Min(1, 1 - 2 * yd.Value)));
                double area_parcial = D * D * (theta - Math.Sin(theta)) / 8.0;
                if (area_parcial <= 0) continue;

                double V = Q / area_parcial;
                if (V < Vmin || V > Vmax) continue;

                return new SolucaoTubo { D = D, Slope = i, YD = yd.Value, V = V };
            }
            return null;
        }
    }
}
