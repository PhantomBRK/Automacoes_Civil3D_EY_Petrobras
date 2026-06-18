using System;

namespace AutomacoesCivil3D
{
    // ============================================================================
    // Hidráulica de tubo circular por Manning — escrita do zero para o motor novo
    // SOL_DIMENSIONAR_DRENAGEM.
    //
    // Replica exatamente o que o SOLIDOS faz numericamente (GravityFunctions.CalcQ /
    // CalculateFlowHeight, vistos na decompilação): dada a seção, a declividade i, a
    // rugosidade n e a vazão Q, resolve a LÂMINA de escoamento normal h tal que
    // Q_manning(h) = Q; dela saem a velocidade V e a relação Y/D.
    //
    // Para um círculo as relações são analíticas (θ = ângulo molhado, em rad):
    //   θ      = 2·acos(1 − 2·h/D)            , h ∈ [0, D]
    //   A(h)   = D²/8 · (θ − sin θ)            (área molhada)
    //   P(h)   = D·θ/2                          (perímetro molhado)
    //   Q(h)   = A · (A/P)^(2/3) · √i / n       (Manning — idêntico ao SOLIDOS)
    //   V(h)   = Q / A
    //
    // Q(h) é monotônica crescente até h/D ≈ 0,938 (vazão máxima, acima da seção
    // plena) e depois cai. Como o projeto limita a lâmina a ≤ 80%, trabalhamos
    // sempre no ramo crescente — a bisseção é feita em [0, 0,938·D] por segurança.
    // ============================================================================
    public static class HidraulicaCircularDren
    {
        // Lâmina relativa onde a vazão de Manning é máxima num círculo.
        public const double YDVazaoMax = 0.938;

        public struct Estado
        {
            public double H;    // lâmina (m)
            public double YD;   // h/D (adimensional)
            public double V;    // velocidade média (m/s)
            public double A;    // área molhada (m²)
            public bool Ok;     // true se a lâmina foi resolvida
        }

        public static double AreaMolhada(double D, double h)
        {
            if (D <= 0 || h <= 0) return 0.0;
            if (h >= D) return Math.PI * D * D / 4.0;
            double theta = 2.0 * Math.Acos(1.0 - 2.0 * h / D);
            return D * D / 8.0 * (theta - Math.Sin(theta));
        }

        public static double PerimetroMolhado(double D, double h)
        {
            if (D <= 0 || h <= 0) return 0.0;
            if (h >= D) return Math.PI * D;
            double theta = 2.0 * Math.Acos(1.0 - 2.0 * h / D);
            return D * theta / 2.0;
        }

        // Vazão de Manning na lâmina h (m³/s). Mesma fórmula do SOLIDOS:
        // Q = A·(A/P)^(2/3)·√i / n.
        public static double VazaoManning(double D, double h, double n, double i)
        {
            if (D <= 0 || n <= 0 || i <= 0 || h <= 0) return 0.0;
            double A = AreaMolhada(D, h);
            double P = PerimetroMolhado(D, h);
            if (A <= 0 || P <= 0) return 0.0;
            return A * Math.Pow(A / P, 2.0 / 3.0) * Math.Sqrt(i) / n;
        }

        // Velocidade a seção plena (m/s) — referência/diagnóstico.
        public static double VelocidadePlena(double D, double n, double i)
        {
            if (D <= 0 || n <= 0 || i <= 0) return 0.0;
            double rh = D / 4.0;                       // A/P = (πD²/4)/(πD) = D/4
            return Math.Pow(rh, 2.0 / 3.0) * Math.Sqrt(i) / n;
        }

        // Resolve a lâmina de escoamento normal h para a vazão Q, na seção D, com n e i.
        // Retorna Estado.Ok=false se Q não couber respeitando o ramo crescente (≤0,938·D).
        public static Estado ResolverLamina(double Q, double D, double n, double i)
        {
            var e = new Estado { Ok = false };
            if (Q <= 0 || D <= 0 || n <= 0 || i <= 0) return e;

            double hMax = YDVazaoMax * D;
            double Qmax = VazaoManning(D, hMax, n, i);
            if (Q > Qmax) return e;                    // não cabe no ramo de projeto

            double lo = 0.0, hi = hMax;
            for (int k = 0; k < 100; k++)
            {
                double mid = 0.5 * (lo + hi);
                double q = VazaoManning(D, mid, n, i);
                if (q < Q) lo = mid; else hi = mid;
            }
            double h = 0.5 * (lo + hi);
            double A = AreaMolhada(D, h);
            if (A <= 0) return e;

            e.H = h;
            e.YD = h / D;
            e.V = Q / A;
            e.A = A;
            e.Ok = true;
            return e;
        }
    }
}
