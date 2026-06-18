using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutomacoesCivil3D
{
    // Modelo persistido das regras de dimensionamento. Serializado em JSON em
    // %APPDATA%\AutomacoesPetrobras\dimensionamento_regras.json — sobrevive a
    // atualizações do plugin.
    public class DimensionamentoConfig
    {
        // Slopes (m/m). Faixa ideal é tentada primeiro; se nenhum DN encerra
        // na ideal, repete com a faixa dura.
        public double IdealSlopeMin { get; set; } = 0.002;   // 0,2%
        public double IdealSlopeMax { get; set; } = 0.025;   // 2,5%
        public double DuraSlopeMin  { get; set; } = 0.002;   // 0,2%
        public double DuraSlopeMax  { get; set; } = 0.040;   // 4,0%

        // Velocidade média na seção parcial (m/s).
        public double Vmin  { get; set; } = 0.60;
        public double Vmax  { get; set; } = 2.00;

        // Lâmina máxima admissível (Y/D).
        public double YDmax { get; set; } = 0.67;

        // Passo de iteração do slope (m/m).
        public double SlopeStep { get; set; } = 0.0005;      // 0,05%

        // Coeficiente de Manning padrão (usado quando o tubo não tem ACMan/etc).
        public double ManningDefault { get; set; } = 0.013;

        // Recobrimento mínimo (m): folga entre o topo do tubo e o terreno
        // (SurfaceElevation). Trava: zFundo + D + recobrimento <= SurfaceElevation.
        public double RecobrimentoMinM { get; set; } = 0.30;

        // Folga do selo do ralo (m): ÚLTIMO recurso para cota de saída quando o
        // dispositivo não tem MaxElevAllowed/CotaSaida — usa SumpElevation + esta folga.
        // Na prática raramente usado (MaxElevAllowed cobre os ralos).
        public double FolgaSeloRaloM { get; set; } = 0.80;

        // Catálogo de DNs disponíveis (mm). Default = ValueOptions do "Catalogo"
        // no .sbd Petrobras.
        public List<int> CatalogoDNsMm { get; set; } = new List<int>
        {
            100, 150, 200, 250, 300, 350, 400, 450, 500,
            600, 700, 800, 900, 1000, 1100, 1200
        };

        // ====================================================================
        // Motor novo SOL_DIMENSIONAR_DRENAGEM (dois regimes + menor escavação).
        // Campos aditivos — não afetam a rotina antiga SOL_DIMENSIONAR_REDE_POR_JUSANTE.
        // ====================================================================

        // --- Cenário PLUVIAL (regime principal, Qini = HCalcIni.Qesc, recorrência Tr) ---
        // Reaproveita Vmin/Vmax/YDmax acima (0,60–2,00 m/s, lâmina ≤ 67%).

        // --- Cenário INCÊNDIO (regime de verificação, Qfim = HCalcFim.Qesc, recorrência Trv) ---
        // A vazão pontual de regime crítico é definida por outra rotina (área×coef).
        // Se o tubo não tiver Qfim (HCalcFim.Qesc = 0/NaN), dimensiona só pelo pluvial.
        public bool   IncendioAtivo  { get; set; } = true;
        public double IncendioVmax   { get; set; } = 4.00;   // m/s
        public double IncendioYDmax  { get; set; } = 0.80;   // lâmina ≤ 80%
        // Incêndio não tem Vmin (autolimpeza é exigida só no pluvial).

        // Faixa única de declividade do dimensionamento (m/m) e passo de varredura.
        // Reaproveita DuraSlopeMin/DuraSlopeMax (0,2%–4,0%) e SlopeStep (0,05%).

        // Recobrimento mínimo sobre a geratriz superior do tubo (m), usado para
        // ancorar a cabeceira o mais raso possível (estratégia "menor escavação").
        // DN externo considerado = D + 2·Parede.
        public double RecobrimentoMinDrenM { get; set; } = 0.45;

        // Setar SetarReferencia="PONTO BAIXO" nas junções (CONEXÕES/JUNÇÃO) ao
        // dimensionar — alinha a junção ao tubo mais baixo (gravidade correta),
        // dispensando a correção manual de conexões. Default true.
        public bool JuncaoPontoBaixo { get; set; } = true;

        // Modo "manter cotas e declividade": NÃO recalcula cotas nem declividade —
        // mantém a geometria atual do tubo e altera APENAS o diâmetro (Catalogo) para
        // tentar atender lâmina/velocidade da norma. Para troncos de cota fixa
        // (exutório controlado) onde só o DN pode variar. Ignora i mín/máx e PONTO
        // BAIXO. Default false (modo MENOR ESCAVAÇÃO, que recalcula cota+declividade).
        public bool ManterCotasDeclividade { get; set; } = false;

        // -------- Persistência --------

        public static string ConfigPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AutomacoesPetrobras");
                return Path.Combine(dir, "dimensionamento_regras.json");
            }
        }

        public static DimensionamentoConfig Carregar()
        {
            try
            {
                string path = ConfigPath;
                if (!File.Exists(path)) return new DimensionamentoConfig();
                string json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<DimensionamentoConfig>(
                    json,
                    new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip });
                return cfg ?? new DimensionamentoConfig();
            }
            catch
            {
                return new DimensionamentoConfig();
            }
        }

        public void Salvar()
        {
            try
            {
                string path = ConfigPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var opts = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.Never
                };
                File.WriteAllText(path, JsonSerializer.Serialize(this, opts));
            }
            catch
            {
                // Persistência é best-effort; falha não impede dimensionamento.
            }
        }

        // Ponte para RegrasDimensionamento (a struct usada pela HidraulicaSolidos).
        public RegrasDimensionamento ParaRegras()
        {
            return new RegrasDimensionamento
            {
                IdealSlopeMin = this.IdealSlopeMin,
                IdealSlopeMax = this.IdealSlopeMax,
                DuraSlopeMin  = this.DuraSlopeMin,
                DuraSlopeMax  = this.DuraSlopeMax,
                Vmin          = this.Vmin,
                Vmax          = this.Vmax,
                YDmax         = this.YDmax,
                SlopeStep     = this.SlopeStep
            };
        }
    }
}
