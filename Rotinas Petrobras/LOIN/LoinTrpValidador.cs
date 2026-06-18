using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AutomacoesCivil3D
{
    // ========================================================================
    // Validador estatístico/conceitual dos quantitativos de TRP.
    //
    // Lógica pura — opera sobre listas de LoinTrpQuantitativo coletados pelo
    // command (que por sua vez itera sobre Solid3d nos layers TRP_* lendo
    // MassProperties.Volume + Pset). Não toca em transação nem em AutoCAD API.
    //
    // Checks implementados (v1):
    //
    //   1. Balanço corte/aterro — totais, razão, classificação
    //      (compensado / sobra / falta).
    //
    //   2. Camadas obrigatórias presentes — alerta se uma camada esperada
    //      pelo catálogo (ex: Subleito) não apareceu em nenhum alinhamento.
    //      Configurável: o caller pode passar quais camadas "esperar".
    //
    //   3. Dupla contagem A.1 ↔ A.2 — para o MESMO alinhamento, se A.1
    //      (corredor) cobre estaqueamento que A.2 (superfície) também
    //      cobre, o volume está sendo somado em duplicidade. Detecta
    //      sobreposição de intervalos por alinhamento × camada.
    //
    //   4. Sanidade: volumes negativos ou NaN.
    //
    // Não pretende validar geometria — quem decide se a geometria está
    // correta é o engenheiro. O validador olha apenas os números.
    // ========================================================================
    public static class LoinTrpValidador
    {
        public sealed class Relatorio
        {
            public LoinTrpBalanco Balanco { get; init; } = new LoinTrpBalanco();
            public List<string> Erros { get; } = new List<string>();
            public List<string> Avisos { get; } = new List<string>();
            public List<string> Info { get; } = new List<string>();

            public bool PossuiErros => Erros.Count > 0;
            public bool PossuiAvisos => Avisos.Count > 0;

            public string Formatado()
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== Relatório LOIN-TRP ===");
                sb.AppendLine($"Corte total:      {Balanco.VolumeCorteTotalM3:N2} m³");
                sb.AppendLine($"Aterro total:     {Balanco.VolumeAterroTotalM3:N2} m³");
                sb.AppendLine($"   Subleito:      {Balanco.VolumeSubleitoM3:N2} m³");
                sb.AppendLine($"   Reforço:       {Balanco.VolumeReforcoSubleitoM3:N2} m³");
                sb.AppendLine($"   Regularização: {Balanco.VolumeRegularizacaoM3:N2} m³");
                sb.AppendLine($"Diferença (corte-aterro): {Balanco.DiferencaM3:N2} m³");
                sb.AppendLine($"Razão Corte/Aterro:       {Balanco.RazaoCorteAterro:N3}");
                sb.AppendLine();

                if (Erros.Count > 0)
                {
                    sb.AppendLine($"-- ERROS ({Erros.Count}) --");
                    foreach (string e in Erros) sb.AppendLine("  ✗ " + e);
                }
                if (Avisos.Count > 0)
                {
                    sb.AppendLine($"-- AVISOS ({Avisos.Count}) --");
                    foreach (string w in Avisos) sb.AppendLine("  ⚠ " + w);
                }
                if (Info.Count > 0)
                {
                    sb.AppendLine($"-- INFO ({Info.Count}) --");
                    foreach (string i in Info) sb.AppendLine("  · " + i);
                }
                return sb.ToString();
            }
        }

        // Camadas que normalmente um projeto rodoviário Petrobras espera ter.
        // O caller pode passar conjunto vazio para desligar esse check.
        public static readonly IReadOnlyList<CamadaTrp> CamadasEsperadasPadrao = new[]
        {
            CamadaTrp.Subleito,
            CamadaTrp.AterroCorpo,
            CamadaTrp.CorteSolo
        };

        // Limiares de classificação do balanço — ajustar caso a Petrobras
        // adote outros para projeto compensado.
        private const double RazaoMinimaCompensado = 0.85;
        private const double RazaoMaximaCompensado = 1.15;

        public static Relatorio Validar(
            IEnumerable<LoinTrpQuantitativo> quantitativos,
            IReadOnlyList<CamadaTrp>? camadasEsperadas = null)
        {
            if (quantitativos == null) throw new ArgumentNullException(nameof(quantitativos));
            List<LoinTrpQuantitativo> lista = quantitativos.ToList();
            camadasEsperadas ??= CamadasEsperadasPadrao;

            var rel = new Relatorio();

            // --- 4. Sanidade primeiro ---
            int negativos = lista.Count(q => q.VolumeM3 < 0 || double.IsNaN(q.VolumeM3));
            if (negativos > 0)
                rel.Erros.Add($"{negativos} quantitativo(s) com volume negativo ou NaN — investigar Solid3d com geometria inválida.");

            List<LoinTrpQuantitativo> validos = lista
                .Where(q => q.VolumeM3 >= 0 && !double.IsNaN(q.VolumeM3))
                .ToList();

            if (validos.Count == 0)
            {
                rel.Info.Add("Nenhum quantitativo válido para analisar.");
                return rel;
            }

            // --- 1. Balanço ---
            rel.Balanco.VolumeCorteTotalM3 = validos
                .Where(q => EhCorte(q.Camada))
                .Sum(q => q.VolumeM3);

            rel.Balanco.VolumeAterroTotalM3 = validos
                .Where(q => EhAterro(q.Camada))
                .Sum(q => q.VolumeM3);

            rel.Balanco.VolumeSubleitoM3 = validos
                .Where(q => q.Camada == CamadaTrp.Subleito)
                .Sum(q => q.VolumeM3);

            rel.Balanco.VolumeReforcoSubleitoM3 = validos
                .Where(q => q.Camada == CamadaTrp.ReforcoSubleito)
                .Sum(q => q.VolumeM3);

            rel.Balanco.VolumeRegularizacaoM3 = validos
                .Where(q => q.Camada == CamadaTrp.Regularizacao)
                .Sum(q => q.VolumeM3);

            // Classificação do balanço
            double razao = rel.Balanco.RazaoCorteAterro;
            if (rel.Balanco.VolumeAterroTotalM3 < 0.01)
            {
                rel.Info.Add("Projeto sem aterro — todo o volume é corte (escavação pura).");
            }
            else if (razao < RazaoMinimaCompensado)
            {
                rel.Avisos.Add(
                    $"Razão Corte/Aterro = {razao:F3} < {RazaoMinimaCompensado:F2} — projeto deficitário em material " +
                    $"(precisa importar empréstimo de {Math.Abs(rel.Balanco.DiferencaM3):N0} m³).");
            }
            else if (razao > RazaoMaximaCompensado)
            {
                rel.Avisos.Add(
                    $"Razão Corte/Aterro = {razao:F3} > {RazaoMaximaCompensado:F2} — sobra de material " +
                    $"({rel.Balanco.DiferencaM3:N0} m³ para bota-fora).");
            }
            else
            {
                rel.Info.Add($"Balanço compensado — razão {razao:F3} dentro de [{RazaoMinimaCompensado:F2}, {RazaoMaximaCompensado:F2}].");
            }

            // --- 2. Camadas esperadas ---
            HashSet<CamadaTrp> presentes = new HashSet<CamadaTrp>(validos.Select(q => q.Camada));
            foreach (CamadaTrp esperada in camadasEsperadas)
            {
                if (!presentes.Contains(esperada))
                {
                    LoinTrpCamadaSpec? spec = LoinTrpCatalogo.Camadas.FirstOrDefault(c => c.Camada == esperada);
                    rel.Avisos.Add(
                        $"Camada esperada ausente: {spec?.NomePortugues ?? esperada.ToString()} — " +
                        "nenhum quantitativo encontrado para essa camada no projeto inteiro.");
                }
            }

            // --- 3. Dupla contagem A.1 ↔ A.2 ---
            // Agrupa por (alinhamento, camada). Se houver >=1 do tipo Corredor
            // E >=1 do tipo EntreSuperficies, verifica overlap de intervalo
            // [EstacaInicial, EstacaFinal].
            var grupos = validos
                .Where(q => !string.IsNullOrWhiteSpace(q.AlinhamentoNome))
                .GroupBy(q => (q.AlinhamentoNome, q.Camada));

            foreach (var g in grupos)
            {
                var corredor   = g.Where(q => string.Equals(q.OrigemGeometria, "Corredor",        StringComparison.OrdinalIgnoreCase)).ToList();
                var superficie = g.Where(q => string.Equals(q.OrigemGeometria, "EntreSuperficies", StringComparison.OrdinalIgnoreCase)).ToList();
                if (corredor.Count == 0 || superficie.Count == 0) continue;

                foreach (LoinTrpQuantitativo c in corredor)
                foreach (LoinTrpQuantitativo s in superficie)
                {
                    if (IntervalosSobrepoem(c.EstacaInicial, c.EstacaFinal, s.EstacaInicial, s.EstacaFinal))
                    {
                        rel.Erros.Add(
                            $"Dupla contagem [{g.Key.AlinhamentoNome} / {g.Key.Camada}]: " +
                            $"corredor cobre estaca {Fmt(c.EstacaInicial)}–{Fmt(c.EstacaFinal)} " +
                            $"E superfície cobre {Fmt(s.EstacaInicial)}–{Fmt(s.EstacaFinal)}. " +
                            "Volume está sendo somado em duplicidade — remover um dos dois ou recortar o intervalo.");
                    }
                }
            }

            // Info de cobertura
            rel.Info.Add($"Total de quantitativos analisados: {validos.Count}");
            rel.Info.Add($"Alinhamentos distintos: {validos.Select(q => q.AlinhamentoNome).Distinct(StringComparer.OrdinalIgnoreCase).Count(s => !string.IsNullOrWhiteSpace(s))}");

            return rel;
        }

        private static bool EhCorte(CamadaTrp c) =>
            c == CamadaTrp.CorteSolo || c == CamadaTrp.CorteRocha;

        private static bool EhAterro(CamadaTrp c) =>
            c == CamadaTrp.AterroCorpo || c == CamadaTrp.AterroCoroamento ||
            c == CamadaTrp.Subleito    || c == CamadaTrp.ReforcoSubleito  ||
            c == CamadaTrp.Regularizacao;

        // Intervalos [a1, a2] e [b1, b2] se sobrepõem? Tratamento tolerante:
        // estacas inválidas (a1==a2==0) viram "ignorar" — assume cobertura
        // completa do trecho e não dispara dupla contagem (evita falso positivo
        // em quantitativos sem range definido).
        private static bool IntervalosSobrepoem(double a1, double a2, double b1, double b2)
        {
            if (a1 == 0 && a2 == 0) return false;
            if (b1 == 0 && b2 == 0) return false;
            double lo1 = Math.Min(a1, a2), hi1 = Math.Max(a1, a2);
            double lo2 = Math.Min(b1, b2), hi2 = Math.Max(b1, b2);
            return lo1 < hi2 && lo2 < hi1;
        }

        private static string Fmt(double estaca) =>
            estaca.ToString("F2", CultureInfo.InvariantCulture);
    }
}
