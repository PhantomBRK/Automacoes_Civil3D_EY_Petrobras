using System;
using System.Collections.Generic;
using System.Linq;

namespace AutomacoesCivil3D
{
    // ========================================================================
    // Modelos canônicos da disciplina TERRAPLENAGEM para o pipeline LOIN.
    //
    // O resto do pipeline (LoinExportacaoSolidosCorredores, LoinCodeSetStyle...,
    // LoinIfcExportMappingLinker) é agnóstico de disciplina — quem traz a
    // semântica de terraplenagem é a planilha LOIN + o LOINMAP. Este arquivo
    // define o catálogo canônico que os módulos específicos de TRP consultam:
    //
    //   - LoinTrpPsetSeeder            → quais Psets/Qto criar no DWG
    //   - LoinTrpExtratorSuperficie    → qual camada/IfcClass aplicar nos
    //                                    sólidos gerados entre superfícies (A.2)
    //   - LoinTrpValidador             → quais camadas são obrigatórias,
    //                                    balanço corte/aterro
    //   - LoinTrpCommand               → exposição dos comandos AutoCAD
    //
    // Não toca em nada do que já está funcional (PAV).
    // ========================================================================

    public enum CamadaTrp
    {
        Subleito,
        ReforcoSubleito,
        Regularizacao,
        AterroCorpo,
        AterroCoroamento,
        CorteSolo,
        CorteRocha
    }

    // Espelho de IFC4.3 predefined types de IfcEarthworksFill / IfcEarthworksCut.
    // Mantido como string (e não enum) para preservar o que vai na coluna
    // IfcExportAs do IfcInfraExportMapping — "IfcEarthworksFill.EMBANKMENT".
    // Quando USERDEFINED é usado, ObjectType é concatenado em vez do enum nativo.
    public sealed class LoinTrpCamadaSpec
    {
        public CamadaTrp Camada { get; init; }

        // Nome em PT-BR usado em UIs, logs e na coluna "Elemento ou Status"
        // da matriz LOIN ISO 7817. Coincide com como a Petrobras escreve.
        public string NomePortugues { get; init; } = string.Empty;

        // Classe IFC final (IfcEarthworksFill / IfcEarthworksCut).
        public string IfcClass { get; init; } = string.Empty;

        // PredefinedType IFC4.3 oficial OU "USERDEFINED" quando ObjectType
        // for usado (caso o tipo brasileiro não bata com enum nativo).
        public string PredefinedType { get; init; } = string.Empty;

        // Quando PredefinedType=USERDEFINED, este vai como IfcObject.ObjectType
        // — ex: "COROAMENTO_ATERRO", "CORTE_ROCHA".
        public string ObjectType { get; init; } = string.Empty;

        // Prefixo Id para a planilha LOIN — gera "TER-001" etc., consistente
        // com LoinMapeamentoModels.PrefixoDisciplina("TERRA") → "TER".
        public string PrefixoLoinId => "TER";

        // Nome da disciplina como aparecerá na aba da matriz LOIN.
        public string Disciplina => "TERRAPLENAGEM";

        // String final usada na coluna IfcExportAs do IfcInfraExportMapping.
        // Civil 3D aceita "IfcEarthworksFill.EMBANKMENT" e, para userdefined,
        // "IfcEarthworksFill.USERDEFINED" (e o ObjectType vai via PSet IFC).
        public string IfcExportAsString
        {
            get
            {
                if (string.IsNullOrWhiteSpace(IfcClass)) return string.Empty;
                if (string.IsNullOrWhiteSpace(PredefinedType)) return IfcClass;
                return IfcClass + "." + PredefinedType;
            }
        }

        // True se for variante de aterro (IfcEarthworksFill).
        public bool EAterro => string.Equals(IfcClass, "IfcEarthworksFill", StringComparison.OrdinalIgnoreCase);

        // True se for variante de corte (IfcEarthworksCut).
        public bool ECorte => string.Equals(IfcClass, "IfcEarthworksCut", StringComparison.OrdinalIgnoreCase);
    }

    // Catálogo canônico das 7 camadas de terraplenagem da Petrobras.
    // Mapeamento contra IFC4.3 ADD2:
    //
    //   IfcEarthworksFill.SUBGRADE         → camada de fundação do pavimento (subleito)
    //   IfcEarthworksFill.SUBGRADEBED      → reforço do subleito
    //   IfcEarthworksFill.TRANSITIONLAYER  → regularização entre subleito e BGS
    //   IfcEarthworksFill.EMBANKMENT       → corpo do aterro
    //   IfcEarthworksFill.USERDEFINED      → coroamento do aterro (não há enum 4.3)
    //   IfcEarthworksCut.CUT               → corte em solo
    //   IfcEarthworksCut.USERDEFINED       → corte em rocha (não há enum 4.3)
    public static class LoinTrpCatalogo
    {
        public static readonly IReadOnlyList<LoinTrpCamadaSpec> Camadas = new List<LoinTrpCamadaSpec>
        {
            new LoinTrpCamadaSpec
            {
                Camada = CamadaTrp.Subleito,
                NomePortugues = "Subleito",
                IfcClass = "IfcEarthworksFill",
                PredefinedType = "SUBGRADE"
            },
            new LoinTrpCamadaSpec
            {
                Camada = CamadaTrp.ReforcoSubleito,
                NomePortugues = "Reforço do subleito",
                IfcClass = "IfcEarthworksFill",
                PredefinedType = "SUBGRADEBED"
            },
            new LoinTrpCamadaSpec
            {
                Camada = CamadaTrp.Regularizacao,
                NomePortugues = "Regularização do subleito",
                IfcClass = "IfcEarthworksFill",
                PredefinedType = "TRANSITIONLAYER"
            },
            new LoinTrpCamadaSpec
            {
                Camada = CamadaTrp.AterroCorpo,
                NomePortugues = "Aterro - corpo",
                IfcClass = "IfcEarthworksFill",
                PredefinedType = "EMBANKMENT"
            },
            new LoinTrpCamadaSpec
            {
                Camada = CamadaTrp.AterroCoroamento,
                NomePortugues = "Aterro - coroamento",
                IfcClass = "IfcEarthworksFill",
                PredefinedType = "USERDEFINED",
                ObjectType = "COROAMENTO_ATERRO"
            },
            new LoinTrpCamadaSpec
            {
                Camada = CamadaTrp.CorteSolo,
                NomePortugues = "Corte em solo",
                IfcClass = "IfcEarthworksCut",
                PredefinedType = "CUT"
            },
            new LoinTrpCamadaSpec
            {
                Camada = CamadaTrp.CorteRocha,
                NomePortugues = "Corte em rocha",
                IfcClass = "IfcEarthworksCut",
                PredefinedType = "USERDEFINED",
                ObjectType = "CORTE_ROCHA"
            }
        };

        // Resolve por nome PT-BR (tolerante a acento e maiúsculas), por código
        // da camada (enum.ToString()) ou por substring conhecida do nome.
        // Retorna null se não bater — chamador decide o que fazer (warning,
        // fallback para USERDEFINED genérico, etc.).
        public static LoinTrpCamadaSpec? Resolver(string nomeOuCodigo)
        {
            if (string.IsNullOrWhiteSpace(nomeOuCodigo)) return null;

            string alvo = Normalizar(nomeOuCodigo);

            // Match exato no enum
            foreach (LoinTrpCamadaSpec c in Camadas)
                if (string.Equals(Normalizar(c.Camada.ToString()), alvo, StringComparison.OrdinalIgnoreCase))
                    return c;

            // Match exato no nome PT-BR
            foreach (LoinTrpCamadaSpec c in Camadas)
                if (string.Equals(Normalizar(c.NomePortugues), alvo, StringComparison.OrdinalIgnoreCase))
                    return c;

            // Match por substring — protege contra "SUBLEITO_FINAL", "ATERRO CORPO",
            // "CORTE EM ROCHA SA", etc. Ordem importa: matches mais específicos primeiro.
            (string token, CamadaTrp camada)[] heuristicas =
            {
                ("REFORCOSUBLEITO",   CamadaTrp.ReforcoSubleito),
                ("REFORCO",           CamadaTrp.ReforcoSubleito),
                ("REGULARIZACAO",     CamadaTrp.Regularizacao),
                ("COROAMENTO",        CamadaTrp.AterroCoroamento),
                ("ATERROCORPO",       CamadaTrp.AterroCorpo),
                ("ATERRO",            CamadaTrp.AterroCorpo),
                ("CORPO",             CamadaTrp.AterroCorpo),
                ("CORTEROCHA",        CamadaTrp.CorteRocha),
                ("ROCHA",             CamadaTrp.CorteRocha),
                ("CORTESOLO",         CamadaTrp.CorteSolo),
                ("CORTE",             CamadaTrp.CorteSolo),
                ("SUBLEITO",          CamadaTrp.Subleito)
            };

            foreach ((string token, CamadaTrp camada) in heuristicas)
            {
                if (alvo.Contains(token, StringComparison.OrdinalIgnoreCase))
                    return Camadas.FirstOrDefault(c => c.Camada == camada);
            }

            return null;
        }

        // Remove acentos e espaços para casamento robusto. Não muda case
        // (chamador decide com OrdinalIgnoreCase).
        private static string Normalizar(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            string norm = s.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(norm.Length);
            foreach (char ch in norm)
            {
                System.Globalization.UnicodeCategory cat =
                    System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_') continue;
                sb.Append(ch);
            }
            return sb.ToString();
        }

    }

    // ========================================================================
    // Quantitativos por camada — alimenta o validador e o relatório de balanço.
    // Preenchido pelo extrator (A.1 lê do Solid3d.MassProperties, A.2 calcula
    // direto do BREP gerado).
    // ========================================================================
    public sealed class LoinTrpQuantitativo
    {
        public CamadaTrp Camada { get; init; }
        public string AlinhamentoNome { get; init; } = string.Empty;
        public double EstacaInicial { get; init; }
        public double EstacaFinal { get; init; }
        public double VolumeM3 { get; init; }
        public double AreaM2 { get; init; }

        // "Corredor" (A.1) ou "EntreSuperficies" (A.2). Usado pelo validador
        // para detectar dupla contagem quando os dois caminhos cobrem
        // estaqueamento sobreposto.
        public string OrigemGeometria { get; init; } = string.Empty;
    }

    // ========================================================================
    // Resultado consolidado do balanço corte/aterro. Calculado pelo validador
    // a partir da lista de quantitativos.
    // ========================================================================
    public sealed class LoinTrpBalanco
    {
        public double VolumeCorteTotalM3 { get; set; }
        public double VolumeAterroTotalM3 { get; set; }
        public double VolumeReforcoSubleitoM3 { get; set; }
        public double VolumeSubleitoM3 { get; set; }
        public double VolumeRegularizacaoM3 { get; set; }

        // Diferença em m³ (corte - aterro). Positivo = sobra de material;
        // negativo = falta (importar bota-fora reverso).
        public double DiferencaM3 => VolumeCorteTotalM3 - VolumeAterroTotalM3;

        // Razão para análise — alvo típico em projeto compensado: ~1.0.
        // Acima de 1.2 indica desperdício; abaixo de 0.8 indica empréstimo.
        public double RazaoCorteAterro =>
            VolumeAterroTotalM3 > 0.0001 ? VolumeCorteTotalM3 / VolumeAterroTotalM3 : 0.0;

        public List<string> Avisos { get; } = new List<string>();
    }
}
