using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using System;
using System.Collections.Generic;

// Importante: Resolvendo conflitos de namespace para Application e Exception
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.Runtime;

namespace AutomacoesCivil3D
{
    /// <summary>
    /// Representa um ponto em 3D genérico.
    /// Esta é uma estrutura simples para uso interno na lógica de cálculo,
    /// não a Point3d da API do AutoCAD.
    /// </summary>
    public struct Point3D
    {
        public double X, Y, Z;

        public Point3D(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString()
        {
            return $"({X:F2}, {Y:F2}, {Z:F2})";
        }
    }

    /// <summary>
    /// Representa um cuboide (bloco retangular) genérico definido por dois pontos opostos.
    /// </summary>
    public class Cuboid
    {
        public Point3D MinPoint { get; set; } // Canto mínimo (menores X, Y, Z)
        public Point3D MaxPoint { get; set; } // Canto máximo (maiores X, Y, Z)

        public Cuboid(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            MinPoint = new Point3D(minX, minY, minZ);
            MaxPoint = new Point3D(maxX, maxY, maxZ);
        }

        public override string ToString()
        {
            return $"Cuboid [Min: {MinPoint}, Max: {MaxPoint}]";
        }
    }

    /// <summary>
    /// Contém a lógica para gerar os dados geométricos (lista de Cuboids) de uma Descida D'Água de Corte em Degraus (DCD).
    /// As dimensões fixas são baseadas nas premissas do desenho fornecido.
    /// </summary>
    public class DCDSolidGenerator
    {
        // Dimensões Padrão (constantes do desenho)
        public const double STEP_RISE = 0.40; // Altura do degrau interno (queda vertical)
        public const double STEP_RUN = 0.40;  // Profundidade do degrau interno (avanço horizontal)
        private const double WALL_THICKNESS = 0.12; // Espessura da parede lateral
        private const double BASE_THICKNESS = 0.15; // Espessura da base do canal
        private const double INTERNAL_CHANNEL_DEPTH = 0.40; // Profundidade interna do canal

        // Offsets de parede para seções planas de entrada/saída
        private const double INITIAL_TOP_WALL_OFFSET_FLAT = 00.01; // '30' no desenho para o topo da parede na seção inicial
        private const double FINAL_TOP_WALL_OFFSET_FLAT = 0.01; // '70' no desenho para o topo da parede na seção final

        // Comprimentos das seções planas de início e fim (arbitrários, não dados no desenho, mas necessários)
        private const double INITIAL_END_SEGMENT_LENGTH = 0.01;
        private const double FINAL_END_SEGMENT_LENGTH = 0.01;

        /// <summary>
        /// Gera uma lista de Cuboids que representam o sólido da Descida D'Água de Corte em Degraus (DCD).
        /// </summary>
        /// <param name="initialQuota">Cota (altura Z) do ponto de encaixe inicial.</param>
        /// <param name="finalQuota">Cota (altura Z) do ponto de encaixe final.</param>
        /// <param name="channelWidth_a">Largura interna do canal (parâmetro 'a' do desenho).</param>
        /// <param name="initialExtraWallHeight_b">Altura extra da parede na seção inicial (parâmetro 'b' do desenho). Usada também para a altura externa das paredes nos degraus.</param>
        /// <returns>Uma lista de objetos Cuboid que compõem o sólido DCD.</returns>
        public static List<Cuboid> GenerateDCDSolid(
            double initialQuota,
            double finalQuota,
            double channelWidth_a,
            double initialExtraWallHeight_b)
        {
            List<Cuboid> solidParts = new List<Cuboid>();

            // Calcular a largura total da estrutura (incluindo paredes)
            double totalWidth = channelWidth_a + 2 * WALL_THICKNESS;

            // Calcular a diferença total de altura
            double totalHeightDrop = initialQuota - finalQuota;

            // Calcular o número de degraus
            // Usa Math.Ceiling para garantir que todos os degraus necessários sejam incluídos para a queda de altura.
            int numSteps = (int)Math.Ceiling(Math.Abs(totalHeightDrop) / STEP_RISE);
            if (totalHeightDrop < 0) // Se for uma "subida", ajusta a lógica para que os degraus subam
            {
                // Para uma subida, a lógica precisaria ser revisada para garantir que os pontos de base e topo dos cuboides
                // sejam calculados incrementalmente para cima em vez de para baixo.
                // Por enquanto, a implementação assume 'initialQuota' > 'finalQuota' (descida).
                // Uma implementação de "subida" completa envolveria ajustar as cotas e a direção da construção.
                // Para simplificar, vou manter a premissa de 'descida'.
                // Se a diferença for negativa, a lógica pode não ser totalmente aplicável sem ajustes.
                numSteps = (int)Math.Ceiling(totalHeightDrop / STEP_RISE); // Isto resultaria em um número negativo de degraus se totalHeightDrop for negativo
                
                // Melhor garantir que numSteps seja não-negativo para o loop e talvez dar um aviso.
                
                numSteps = 0; // Se a cota final for maior, significa subida. Não geramos degraus de descida.
                
                // Ou, se quisermos que gere degraus de subida, a lógica dos Z's e X's precisa ser invertida.
                // Para este exemplo, assumimos uma descida (initialQuota > finalQuota).
            }
            // Posição X atual para a construção ao longo do comprimento da escada
            double currentX = 0;
            // Z do fundo interno do canal no ponto atual. Usamos initialQuota como referência para o topo da escada.
            double currentZ_channel_bottom_inner = initialQuota - INTERNAL_CHANNEL_DEPTH;

            Editor docEditor = Manager.DocEditor;

             //docEditor.WriteMessage no lugar de Console.WriteLine para feedback no AutoCAD
            docEditor.WriteMessage($"\nGerando DCD com:");
            docEditor.WriteMessage($"  Cota Inicial: {initialQuota}, Cota Final: {finalQuota}");  
            docEditor.WriteMessage($"  Largura Canal (a): {channelWidth_a}, Altura Extra Parede (b): {initialExtraWallHeight_b}");
            docEditor.WriteMessage($"  Diferença de Altura: {totalHeightDrop}, Número de Degraus: {numSteps}");
            docEditor.WriteMessage($"  Largura Total: {totalWidth}");


            // --- 1. Seção Plana Inicial (Entrada D'água) ---
            // Esta seção é um bloco retangular simples que inclui a base e as paredes.
            double x_end_initial = currentX + INITIAL_END_SEGMENT_LENGTH;
            double z_base_outer_initial = currentZ_channel_bottom_inner - BASE_THICKNESS;
            // Altura total da parede externa para a seção inicial
            double z_top_outer_wall_initial = currentZ_channel_bottom_inner + INTERNAL_CHANNEL_DEPTH + initialExtraWallHeight_b + INITIAL_TOP_WALL_OFFSET_FLAT;

            solidParts.Add(new Cuboid(currentX, 0, z_base_outer_initial, x_end_initial, totalWidth, z_top_outer_wall_initial));
            // docEditor.WriteMessage($"  Adicionado Bloco Inicial: {solidParts[solidParts.Count - 1]}");
            currentX = x_end_initial;

            // --- 2. Seções dos Degraus ---
            for (int i = 0; i < numSteps; i++)
            {
                double step_x_start = currentX;
                double step_x_end = currentX + STEP_RUN;

                double z_this_step_channel_bottom = currentZ_channel_bottom_inner;
                double z_next_step_channel_bottom = currentZ_channel_bottom_inner - STEP_RISE; // Próximo nível de piso do canal

                // Altura externa da parede para os degraus: base do degrau + profundidade do canal + altura extra 'b'
                // Assumimos que 'b' se aplica à altura das paredes nos degraus também.
                double z_this_step_top_exterior_wall = z_this_step_channel_bottom + INTERNAL_CHANNEL_DEPTH + initialExtraWallHeight_b;
                double z_this_step_base_exterior = z_this_step_channel_bottom - BASE_THICKNESS;

                // Cuboide principal do degrau: forma o "L" horizontal (tread) da escada e as paredes externas.
                solidParts.Add(new Cuboid(step_x_start, 0, z_this_step_base_exterior, totalWidth, step_x_end, z_this_step_top_exterior_wall));
                // docEditor.WriteMessage($"  Adicionado Bloco Degrau {i + 1} (Tread/Walls): {solidParts[solidParts.Count - 1]}");

                // Cuboide para o "riser" (parte vertical do degrau)
                // Conecta o nível atual ao próximo nível mais baixo.
                // A profundidade em X é a espessura da parede para criar uma face vertical sólida.
                // Vai da base do degrau de baixo até o topo da parede do degrau de cima.
                solidParts.Add(new Cuboid(step_x_end - WALL_THICKNESS, 0, z_next_step_channel_bottom - BASE_THICKNESS, step_x_end, totalWidth, z_this_step_top_exterior_wall));
                // docEditor.WriteMessage($"  Adicionado Bloco Degrau {i + 1} (Riser): {solidParts[solidParts.Count - 1]}");

                currentX = step_x_end;
                currentZ_channel_bottom_inner = z_next_step_channel_bottom; // Atualiza a cota Z para o próximo degrau
            }

            // --- 3. Seção Plana Final (Caixa Coletora) ---
            // Similar à seção inicial, mas na cota final e com offset de parede diferente.
            double x_end_final = currentX + FINAL_END_SEGMENT_LENGTH;
            double z_base_outer_final = currentZ_channel_bottom_inner - BASE_THICKNESS;
            // Altura total da parede externa para a seção final
            double z_top_outer_wall_final = currentZ_channel_bottom_inner + INTERNAL_CHANNEL_DEPTH + FINAL_TOP_WALL_OFFSET_FLAT;

            solidParts.Add(new Cuboid(currentX, 0, z_base_outer_final, x_end_final, totalWidth, z_top_outer_wall_final));
            // docEditor.WriteMessage($"  Adicionado Bloco Final: {solidParts[solidParts.Count - 1]}");

            // docEditor.WriteMessage($"\nTotal de Cuboides gerados: {solidParts.Count}");
            return solidParts;
        }
    }

    /// <summary>
    /// Classe responsável por criar os sólidos 3D da DCD no ambiente Civil 3D.
    /// </summary>
    public class CriarDCDSolido
    {
        /// <summary>
        /// Gera o sólido 3D completo da Descida D'Água de Corte em Degraus (DCD) no Civil 3D.
        /// </summary>
        /// <param name="initialQuota">Cota (altura Z) do ponto de encaixe inicial.</param>
        /// <param name="finalQuota">Cota (altura Z) do ponto de encaixe final.</param>
        /// <param name="channelWidth_a">Largura interna do canal (parâmetro 'a' do desenho).</param>
        /// <param name="initialExtraWallHeight_b">Altura extra da parede na seção inicial (parâmetro 'b' do desenho).</param>
        public static void GerarSolido3DDCD(double initialQuota, double finalQuota, double channelWidth_a, double initialExtraWallHeight_b)
        {
            // Obter o documento e o editor correntes através da sua classe Manager
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            Database acDb = civilDoc.Database;

            // Validar as cotas para garantir que é uma descida ou uma elevação pequena
            if (initialQuota < finalQuota)
            {
                docEditor.WriteMessage("\nAs cotas indicam uma subida. Este método foi projetado para descidas (cota inicial > cota final).");
                docEditor.WriteMessage("Ajustando a cota final para ser menor que a inicial para demonstração.");
                finalQuota = initialQuota - (DCDSolidGenerator.STEP_RISE * 1); // Força uma descida de pelo menos um degrau
                if (finalQuota >= initialQuota) finalQuota = initialQuota - 0.001; // Garante que finalQuota é menor.
            }
            if (Math.Abs(initialQuota - finalQuota) < DCDSolidGenerator.STEP_RISE && initialQuota != finalQuota)
            {
                docEditor.WriteMessage("\nA diferença de cotas é menor que um degrau completo. A DCD será gerada com apenas as seções planas.");
            }
            else if (initialQuota == finalQuota)
            {
                docEditor.WriteMessage("\nCota inicial e final são iguais. A DCD será gerada como uma seção plana.");
            }


            using (Transaction acTrans = acDb.TransactionManager.StartTransaction())
            {
                try
                {
                    // Abrir o BlockTable para leitura e o ModelSpace para escrita
                    BlockTable acBlkTbl = (BlockTable)acTrans.GetObject(acDb.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord acMs = (BlockTableRecord)acTrans.GetObject(acBlkTbl[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // Gerar a lista de Cuboids usando a lógica da DCD
                    List<Cuboid> dcdParts = DCDSolidGenerator.GenerateDCDSolid(initialQuota, finalQuota, channelWidth_a, initialExtraWallHeight_b);

                    // Iterar sobre cada Cuboid e convertê-lo em um Solid3d no AutoCAD
                    foreach (Cuboid cuboid in dcdParts)
                    {
                        Solid3d acSolid = new Solid3d(); // Explicitando a classe na frente da definição

                        // Calcular dimensões do cuboide
                        double length = cuboid.MaxPoint.X - cuboid.MinPoint.X;
                        double width = cuboid.MaxPoint.Y - cuboid.MinPoint.Y;
                        double height = cuboid.MaxPoint.Z - cuboid.MinPoint.Z;

                        // Criar a geometria da caixa. CreateBox cria o canto inferior-frontal-esquerdo em (0,0,0).
                        acSolid.CreateBox(length, width, height);

                        // Mover o sólido para a posição correta no espaço do modelo
                        // Convertendo nosso Point3D para Autodesk.AutoCAD.Geometry.Point3d
                        Autodesk.AutoCAD.Geometry.Point3d acadMinPoint = new Autodesk.AutoCAD.Geometry.Point3d(cuboid.MinPoint.X, cuboid.MinPoint.Y, cuboid.MinPoint.Z);
                        acSolid.TransformBy(Matrix3d.Displacement(acadMinPoint - Autodesk.AutoCAD.Geometry.Point3d.Origin));

                        // Adicionar o Solid3d ao ModelSpace
                        acMs.AppendEntity(acSolid);
                        acTrans.AddNewlyCreatedDBObject(acSolid, true);

                        docEditor.WriteMessage($"\n  Sólido criado: Min{cuboid.MinPoint} Max{cuboid.MaxPoint}");
                    }

                    // Commitar a transação para salvar as alterações no desenho
                    acTrans.Commit();
                    docEditor.WriteMessage($"\nSólido 3D da Descida D'Água de Corte em Degraus (DCD) gerado com sucesso! Total de {dcdParts.Count} partes.");
                }
                catch (Exception ex)
                {
                    // Em caso de erro, abortar a transação para não deixar o desenho em estado inconsistente
                    acTrans.Abort();
                    docEditor.WriteMessage($"\nErro ao gerar o sólido DCD: {ex.Message}");
                    docEditor.WriteMessage($"\nStack Trace: {ex.StackTrace}"); // Para depuração
                }
            }
        }

        // Exemplo de como você chamaria este método a partir de um comando no Civil 3D
         [CommandMethod("GERARDCD")]
         public static void TesteGerarDCD()
         {
             Editor docEditor = Manager.DocEditor;
        
        
           try
          {
                // Solicitar as cotas e dimensões ao usuário
                 PromptDoubleOptions pdoInitial = new PromptDoubleOptions("\nDigite a cota inicial (Z): ");
                 pdoInitial.AllowNegative = true;
                PromptDoubleResult pdrInitial = docEditor.GetDouble(pdoInitial);
                if (pdrInitial.Status != PromptStatus.OK) return;
                double initialQuota = pdrInitial.Value;
       
                PromptDoubleOptions pdoFinal = new PromptDoubleOptions("\nDigite a cota final (Z): ");
                pdoFinal.AllowNegative = true;
               PromptDoubleResult pdrFinal = docEditor.GetDouble(pdoFinal);
                 if (pdrFinal.Status != PromptStatus.OK) return;
                 double finalQuota = pdrFinal.Value;
        
                 PromptDoubleOptions pdoWidth = new PromptDoubleOptions("\nDigite a largura interna do canal (a): ");
                 pdoWidth.AllowZero = false;
                 pdoWidth.AllowNegative = false;
                 PromptDoubleResult pdrWidth = docEditor.GetDouble(pdoWidth);
                 if (pdrWidth.Status != PromptStatus.OK) return;
                 double channelWidth_a = pdrWidth.Value;
        
                 PromptDoubleOptions pdoExtraHeight = new PromptDoubleOptions("\nDigite a altura extra da parede (b): ");
                 pdoExtraHeight.AllowNegative = false;
                 PromptDoubleResult pdrExtraHeight = docEditor.GetDouble(pdoExtraHeight);
                 if (pdrExtraHeight.Status != PromptStatus.OK) return;
                 double initialExtraWallHeight_b = pdrExtraHeight.Value;
        
                 // Chamar o método para gerar o sólido 3D da DCD
                 GerarSolido3DDCD(initialQuota, finalQuota, channelWidth_a, initialExtraWallHeight_b);
             }
             catch (Exception ex)
             {
                docEditor.WriteMessage($"\nErro no comando GERARDCD: {ex.Message}");
            }
         }
    }

    // A sua classe Manager que você mencionou no contexto.
    // Presumi que ela já existe e tem estas propriedades.
    // Se não existir, você precisaria implementá-la ou adaptar o código.
    /*
    public static class Manager
    {
        public static Document DocCad
        {
            get { return Application.DocumentManager.MdiActiveDocument; }
        }

        public static CivilDocument DocCivil
        {
            get { return CivilApplication.ActiveDocument; }
        }

        public static Editor DocEditor
        {
            get { return Application.DocumentManager.MdiActiveDocument.Editor; }
        }
    }
    */
}