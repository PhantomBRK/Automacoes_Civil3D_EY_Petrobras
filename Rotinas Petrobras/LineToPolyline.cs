using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.Runtime;
using System.Runtime.InteropServices;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;


namespace AutomacoesCivil3D
{
    

    public class LineToPolylineConverter
    {
        [CommandMethod("Gerar")]
        public void ConvertLinesToPolylines()
        {
            CivilDocument DocCivil = Manager.DocCivil;
            Database DocData = Manager.DocData;
            Editor DocEditor = Manager.DocEditor;

            using (Transaction TransCad = DocData.TransactionManager.StartTransaction())
            {
                TinSurface superficieEscavacao = (TinSurface)TransCad.GetObject(TinSurface.Create(DocData, "Escavacao"), OpenMode.ForWrite);
                List<EscavacaoTubo> resultados = new List<EscavacaoTubo>();

                foreach (ObjectId networkId in DocCivil.GetPipeNetworkIds())
                {
                    Network network = (Network)TransCad.GetObject(networkId, OpenMode.ForRead);
                    if (network == null) continue;

                    foreach (ObjectId pipeId in network.GetPipeIds())
                    {
                        Pipe tubo = (Pipe)TransCad.GetObject(pipeId, OpenMode.ForRead);
                        if (tubo == null) continue;

                        double D = tubo.InnerDiameterOrWidth;
                        double L = CalcularLarguraVala(D);
                        double H = CalcularProfundidadeVala(D, tubo.StartPoint.Z, tubo.EndPoint.Z);

                        // 🔹 Gerar pontos para as bordas da vala
                        //Point3dCollection pontosBorda = GerarPontosBordaVala(tubo. as Polyline3d, L, H);

                        // 🔹 Adicionar breakline não destrutiva à superfície
                        SurfaceDefinitionBreaklines breaklineDef = superficieEscavacao.BreaklinesDefinition;
                        //breaklineDef.AddNonDestructiveBreaklines(pontosBorda, midOrdinateDistance: 0.1); // Ajuste a precisão conforme necessário

                        // 🔹 Calcular volume (exemplo simplificado)
                        resultados.Add(new EscavacaoTubo
                        {
                            NomeRede = network.Name,
                            NomeTubo = tubo.Name,
                            Diametro = D * 1000,
                            LarguraVala = L,
                            ProfundidadeVala = H,
                            VolumeEscavacao = L * H * tubo.Length3D
                        });
                    }
                }

                TransCad.Commit();
            }
        }

// 🔹 Método para gerar pontos ao longo das bordas da vala
private Point3dCollection GerarPontosBordaVala(Polyline3d centroide, double larguraVala, double profundidade)
        {
            Point3dCollection pontos = new Point3dCollection();
            double offset = larguraVala / 2;

            // Gerar pontos para ambos os lados do tubo
          /*  foreach (double direcao in new[] { offset, -offset })
            {
                for (int i = 0; i < centroide.ver; i++)
                {
                    Point3d ptOriginal = centroide.GetPoint3dAt(i);
                    Point3d ptDeslocado = ptOriginal.OrthoOffset(direcao, centroide.Normal); // Implementar lógica de deslocamento
                    ptDeslocado = new Point3d(ptDeslocado.X, ptDeslocado.Y, ptDeslocado.Z - profundidade); // Ajustar elevação
                    pontos.Add(ptDeslocado);
                }
            }*/

            return pontos;
        }
        // 🔹 Cálculo da largura da vala (baseado no diâmetro do tubo)
        private double CalcularLarguraVala(double D)
        {
            if (D <= 0.40) return 0.80;
            if (D > 0.40 && D <= 0.80) return D + 0.60;
            return D + 0.40;
        }

        // 🔹 Cálculo da profundidade da vala (H)
        private double CalcularProfundidadeVala(double D, double ZInicio, double ZFim)
        {
            double profundidadeMedia = Math.Abs(ZInicio - ZFim) / 2; // Média das profundidades

            if (profundidadeMedia <= 1.25) return 1.25; // Profundidade mínima
            if (profundidadeMedia <= 1.75) return 1.75; // Segundo caso
            return profundidadeMedia + 0.50; // Se for mais profunda, adicionar margem de segurança
        }




       /* public static void ExportarParaExcel(List<EscavacaoTubo> escavacoes, string caminhoArquivo)
        {
            Excel.Application excelApp = null;
            Excel.Workbook workbook = null;
            Excel.Worksheet worksheet = null;

            try
            {
                excelApp = new Excel.Application();
                if (excelApp == null)
                {
                    Console.WriteLine("Erro ao iniciar o Excel.");
                    return;
                }

                excelApp.Visible = false;
                excelApp.DisplayAlerts = false;

                workbook = excelApp.Workbooks.Add();
                worksheet = (Excel.Worksheet)workbook.Sheets[1];

                // Escrevendo os cabeçalhos
                worksheet.Cells[1, 1] = "Nome da Rede";
                worksheet.Cells[1, 2] = "Nome do Tubo";
                worksheet.Cells[1, 3] = "Diametro (mm)";
                worksheet.Cells[1, 4] = "Largura (m)";
                worksheet.Cells[1, 5] = "Profundidade (m)";
                worksheet.Cells[1, 6] = "Volume (m³)";

                // Preenchendo os dados
                int row = 2;
                foreach (var escavacao in escavacoes)
                {
                    worksheet.Cells[row, 1] = escavacao.NomeRede;
                    worksheet.Cells[row, 2] = escavacao.NomeTubo;
                    worksheet.Cells[row, 3] = escavacao.Diametro;
                    worksheet.Cells[row, 4] = escavacao.LarguraVala;
                    worksheet.Cells[row, 5] = escavacao.ProfundidadeVala;
                    worksheet.Cells[row, 6] = escavacao.VolumeEscavacao;
                    row++;
                }

                // Ajustando o tamanho das colunas
                worksheet.Columns.AutoFit();

                // Salvando o arquivo
                workbook.SaveAs(caminhoArquivo);
                workbook.Close();
                excelApp.Quit();

                Console.WriteLine($"Arquivo salvo com sucesso: {caminhoArquivo}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao exportar para Excel: {ex.Message}");
            }
            finally
            {
                // Liberar objetos COM corretamente
                if (worksheet != null) Marshal.ReleaseComObject(worksheet);
                if (workbook != null) Marshal.ReleaseComObject(workbook);
                if (excelApp != null) Marshal.ReleaseComObject(excelApp);
            }
        }*/


        public class EscavacaoTubo
        {
            public string NomeRede { get; set; }
            public string NomeTubo { get; set; }
            public double Diametro { get; set; }
            public double LarguraVala { get; set; }
            public double ProfundidadeVala { get; set; }
            public double VolumeEscavacao { get; set; }
        }
    }

    



}
 
