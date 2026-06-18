using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;

namespace RotinasPetrobras.Quantitativos
{
    /// <summary>
    /// Caixas (Family "PETROBRAS - CAIXAS"): leitura + agregação SMEC.
    /// A caixa não tem item próprio — é a SOMA dos insumos (concreto armado, magro, aço,
    /// fôrmas + earthwork). Tudo já calculado no construtor (pacote QTO SMEC), lido aqui.
    /// </summary>
    public partial class SolQuantTubos
    {
        // Família CAIXAS na TABELAS_AUXILIARES
        private const string FAMILIA_CAIXAS = "CAIXAS";

        // Itens estruturais (grafia confirmada na coluna CAIXAS da TABELAS_AUXILIARES)
        private const string IT_CX_CONCRETO_EST   = "CONCRETO ESTRUTURAL - FCK= 30 MPA";
        private const string IT_CX_CONCRETO_MAGRO = "CONCRETO MAGRO - FCK = 10MPA";
        private const string IT_CX_ACO            = "ARMADURA DE AÇO CA-50";
        private const string IT_CX_FORMAS         = "FÔRMAS DE MADEIRA COMPENSADA ATÉ 3 UTILIZAÇÕES";
        // escavação/apiloamento/reaterro/bota-fora reutilizam as constantes dos tubos
        // (IT_ESC_*, IT_APILOAMENTO, IT_REATERRO, IT_BOTAFORA) — mesma classe partial.

        private static CaixaQuantData LerCaixa(ObjectId id, string docRefDwg)
        {
            try
            {
                string dwg = LerString(id, "Dwg");
                var c = new CaixaQuantData
                {
                    Nome        = LerString(id, "Name"),
                    SubType     = LerString(id, "SubType"),
                    DocRef      = string.IsNullOrWhiteSpace(dwg) ? docRefDwg : dwg,
                    NomeRede    = LerNomeReferenciado(id, "RootId"),
                    // Geometria (inputs)
                    CotaTopo    = LerDouble(id, "RimElevation"),
                    CotaFundo   = LerDouble(id, "SumpElevation"),
                    Parede      = LerDouble(id, "Parede"),
                    LajeFundo   = LerDouble(id, "AltPiso"),
                    LajeTopo    = LerDouble(id, "AltLaje"),
                    Di1         = LerDouble(id, "Comprimento"),
                    Di2         = LerDouble(id, "Largura"),
                    De1         = LerDouble(id, "ComprimentoExterno"),
                    De2         = LerDouble(id, "LarguraExterna"),
                    AlturaInterna = LerDouble(id, "Altura"),
                    EspMagro    = LerDouble(id, "EspessuraConcretoMagro"),
                    LargVala1   = LerDouble(id, "LargVala1"),
                    LargVala2   = LerDouble(id, "LargVala2"),
                    AlturaEscav = LerDouble(id, "AlturaEscav"),
                    TaxaArmadura = LerDouble(id, "TaxaArmadura"),
                    MassaEspAdotada = LerDouble(id, "MassaEspAdotada"),
                    // Estrutura
                    VolCA       = LerDouble(id, "VolCA"),
                    VolLajes    = LerDouble(id, "VolLajes"),
                    VolParedes  = LerDouble(id, "VolParedes"),
                    VolCM       = LerDouble(id, "VolCM"),
                    QuantAco    = LerDouble(id, "QuantAco"),
                    AreaFormas  = LerDouble(id, "AreaFormas"),
                    // Earthwork (calculado no construtor)
                    VolEscav        = LerDouble(id, "VolEscav"),
                    AreaApiloamento = LerDouble(id, "AreaApiloamento"),
                    VolReaterro     = LerDouble(id, "VolReaterro"),
                    VolBotaFora     = LerDouble(id, "VolBotaFora"),
                    MassaBotaFora   = LerDouble(id, "MassaBotaFora"),
                };
                if (string.IsNullOrWhiteSpace(c.NomeRede) || c.NomeRede == "?") c.NomeRede = "SEM REDE";
                return c;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Agrega caixas em linhas SMEC (família CAIXAS, global). Soma dos insumos:
        /// concreto armado, magro, aço, fôrmas, escavação (3 faixas), apiloamento,
        /// reaterro, bota-fora.
        /// </summary>
        private static List<SmecLinha> AgregarSmecCaixas(
            List<CaixaQuantData> caixas, bool incluirDemolicao, string docRef)
        {
            var linhas = new List<SmecLinha>();
            if (caixas == null || caixas.Count == 0) return linhas;

            // Estrutura
            AddSe(linhas, FAMILIA_CAIXAS, IT_CX_CONCRETO_EST,   caixas.Sum(c => c.VolCA),      docRef);
            AddSe(linhas, FAMILIA_CAIXAS, IT_CX_CONCRETO_MAGRO, caixas.Sum(c => c.VolCM),      docRef);
            AddSe(linhas, FAMILIA_CAIXAS, IT_CX_ACO,            caixas.Sum(c => c.QuantAco),   docRef);
            AddSe(linhas, FAMILIA_CAIXAS, IT_CX_FORMAS,         caixas.Sum(c => c.AreaFormas), docRef);

            // Escavação em 3 faixas (por altura de escavação da caixa)
            double escAte15 = 0, esc15_175 = 0, escEscor = 0;
            foreach (var c in caixas)
            {
                double h = c.AlturaEscav;
                if (h <= 1.5) escAte15 += c.VolEscav;
                else if (h <= 1.75) esc15_175 += c.VolEscav;
                else escEscor += c.VolEscav;
            }
            AddSe(linhas, FAMILIA_CAIXAS, IT_ESC_ATE15,  escAte15,  docRef);
            AddSe(linhas, FAMILIA_CAIXAS, IT_ESC_15_175, esc15_175, docRef);
            AddSe(linhas, FAMILIA_CAIXAS, IT_ESC_ESCOR,  escEscor,  docRef);

            // Demais serviços
            AddSe(linhas, FAMILIA_CAIXAS, IT_APILOAMENTO, caixas.Sum(c => c.AreaApiloamento), docRef);
            AddSe(linhas, FAMILIA_CAIXAS, IT_REATERRO,    caixas.Sum(c => c.VolReaterro),     docRef);
            AddSe(linhas, FAMILIA_CAIXAS, IT_BOTAFORA,    caixas.Sum(c => c.VolBotaFora),     docRef);

            // NOTA: caixa não tem item próprio (soma dos insumos). Demolição não está no
            // pacote do construtor da caixa — se precisar, adicionar DemolRecomp lá depois.

            return linhas;
        }
    }

    /// <summary>Dados de uma caixa para SMEC e memória de cálculo (aba CAIXAS).</summary>
    internal class CaixaQuantData
    {
        public string Nome;
        public string SubType;
        public string DocRef;
        public string NomeRede;
        // Geometria (inputs)
        public double CotaTopo;       // RimElevation
        public double CotaFundo;      // SumpElevation
        public double Parede;
        public double LajeFundo;      // AltPiso
        public double LajeTopo;       // AltLaje
        public double Di1;            // Comprimento (interna)
        public double Di2;            // Largura (interna)
        public double De1;            // ComprimentoExterno
        public double De2;            // LarguraExterna
        public double AlturaInterna;  // Altura (hi)
        public double EspMagro;       // EspessuraConcretoMagro
        public double LargVala1;
        public double LargVala2;
        public double AlturaEscav;
        public double TaxaArmadura;
        public double MassaEspAdotada;
        // Estrutura
        public double VolCA;       // concreto armado/estrutural total (m³)
        public double VolLajes;    // parcela das lajes + piso (m³)
        public double VolParedes;  // parcela corpo+septo (m³)
        public double VolCM;       // concreto magro (m³)
        public double QuantAco;    // aço (kg)
        public double AreaFormas;  // fôrmas (m²)
        // Earthwork
        public double VolEscav;
        public double AreaApiloamento;
        public double VolReaterro;
        public double VolBotaFora;
        public double MassaBotaFora;
    }
}
