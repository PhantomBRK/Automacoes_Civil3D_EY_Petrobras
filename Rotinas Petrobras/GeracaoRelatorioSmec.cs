using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.VisualBasic.Devices;
using System.Net;
using Autodesk.AutoCAD.ApplicationServices;
using Network = Autodesk.Civil.DatabaseServices.Network;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.EditorInput;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using System.Text;
using System.Diagnostics;
using Autodesk.Civil.DatabaseServices.Styles;
using Surface = Autodesk.Civil.DatabaseServices.Surface;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.Runtime;
using System.Globalization;

namespace AutomacoesCivil3D
{

    public class RelatorioSMEC
    {
        public class EstruturaDrenagem
        {
            public string? Unidade { get; set; }
            public string? Anexo { get; set; }
            public string? Item_PPU { get; set; }
            public string? Subcontrato { get; set; }
            public string? Código_Composição { get; set; }
            public string? Documento { get; set; }
            public string? Familia { get; set; }
            public string? Serviço { get; set; }
            public string? Unidade_de_Medida { get; set; }
            public string? Quantidade { get; set; }
            public string? Subitem { get; set; }
            public string? CondicaoExe { get; set; }
            public string? Dimensao { get; set; }
            public string? Observacoes { get; set; }
        }

        [CommandMethod("RelatorioSMECDRE")]
        public static void GerarRelatorioSMEC()
        {
            Dictionary<string, EstruturaDrenagem> estruturasDict = new Dictionary<string, EstruturaDrenagem>();
            Dictionary<string, EstruturaDrenagem> escavacaoDict = new Dictionary<string, EstruturaDrenagem>();
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database docData = Manager.DocData;


            using (Transaction TransCad = civilDoc.TransactionManager.StartTransaction())
            {
                // Processamento de Estruturas (Structures)
                foreach (ObjectId networkId in civilDb.GetPipeNetworkIds())
                {
                    Network network = (Network)TransCad.GetObject(networkId, OpenMode.ForRead);
                    if (network == null) continue;



                    foreach (ObjectId estruturaId in network.GetStructureIds())
                    {
                        Structure estrutura = (Structure)TransCad.GetObject(estruturaId, OpenMode.ForRead);
                        if (estrutura == null || estrutura.BoundingShape != BoundingShapeType.Box) continue;

                        try
                        {
                            // Validação da PartFamilyName para garantir dados consistentes
                            if (!ValidarPartFamilyName(estrutura.PartFamilyName))
                            {

                                docEditor.WriteMessage($"\nA PartFamilyName '{estrutura.PartFamilyName}' é inválida e será ignorada. Verifique a nomenclatura no Civil 3D.\n");
                                continue; // Ignora a estrutura com PartFamilyName inválida
                            }

                        }
                        catch (Exception ex)
                        {


                        }
                        string tipoEstrutura = estrutura.PartFamilyName;
                        string familia = ObterFamilia(tipoEstrutura);
                        string servico = ObterServico(tipoEstrutura);
                        string codigoComposicao = ObterCodigoComposicao(servico);

                        // Criar chave única com base na estrutura
                        string chave = $"{estrutura.PartSizeName}";

                        if (!estruturasDict.TryGetValue(chave, out EstruturaDrenagem registroExistente))
                        {
                            registroExistente = new EstruturaDrenagem
                            {
                                Unidade = "",
                                Anexo = "",
                                Item_PPU = "",
                                Subcontrato = "",
                                Código_Composição = codigoComposicao,
                                Documento = Path.GetFileNameWithoutExtension(docData.Filename),
                                Familia = familia,
                                Serviço = servico,
                                Unidade_de_Medida = "un",
                                Quantidade = "1",
                                Subitem = "Concreto Armado",
                                CondicaoExe = "Tampao/Grelha de Ferro Fundido",
                                Dimensao = "1,20m x 1,20m x 1,50m",
                                Observacoes = $" {estrutura.PartSizeName} x {estrutura.Height * 1000:F0}mm, Espessura = {estrutura.WallThickness * 100:F2}cm, {estrutura.Material}, {estrutura.Cover}, {estrutura.Frame}, {estrutura.Grate}"
                            };
                            estruturasDict[chave] = registroExistente;
                        }
                        else
                        {
                            registroExistente.Quantidade = (int.Parse(registroExistente.Quantidade) + 1).ToString();
                        }
                    }
                }

                // Processamento de Tubos (Pipes)
                foreach (ObjectId networkId in civilDb.GetPipeNetworkIds())
                {
                    Network network = (Network)TransCad.GetObject(networkId, OpenMode.ForRead);

                    if (network != null)
                    {
                        foreach (ObjectId estruturaId in network.GetPipeIds())
                        {
                            Pipe estrutura = (Pipe)TransCad.GetObject(estruturaId, OpenMode.ForRead);
                            if (estrutura == null) continue;

                            // Criar chave única com base na estrutura
                            string chave = $"{estrutura.PartFamilyName}_{estrutura.InnerDiameterOrWidth}";

                            // Processar valores
                            if (!estruturasDict.TryGetValue(chave, out EstruturaDrenagem registroExistente))
                            {
                                string tipoEstrutura = estrutura.PartFamilyName;
                                string familia = ObterFamiliaPipe(tipoEstrutura);
                                string servico = ObterServicoPipe(tipoEstrutura);
                                string codigoComposicao = ObterCodigoComposicaoPipe(servico);
                                string dimencao = ObterDimensoesPipe(estrutura, familia);

                                // Aqui usamos o comprimento do primeiro tubo para iniciar a soma
                                registroExistente = new EstruturaDrenagem
                                {
                                    Unidade = "",
                                    Anexo = "",
                                    Item_PPU = "",
                                    Subcontrato = "",
                                    Código_Composição = codigoComposicao,
                                    Documento = Path.GetFileNameWithoutExtension(docData.Filename),
                                    Familia = familia,
                                    Serviço = servico,
                                    Unidade_de_Medida = "m",
                                    Quantidade = estrutura.Length3D.ToString("F2"),
                                    Subitem = "Nao se Aplica",
                                    CondicaoExe = "Nao se Aplica",
                                    Dimensao = $"{dimencao} mm",
                                    Observacoes = $"{estrutura.PartSizeName}, {estrutura.Material}",
                                };

                                estruturasDict[chave] = registroExistente;





                            }
                            else
                            {
                                // Ao encontrar um registro já existente, acumular o comprimento
                                if (double.TryParse(registroExistente.Quantidade, out double comprimentoAcumulado))
                                {
                                    // Some o comprimento atual do tubo
                                    comprimentoAcumulado += estrutura.Length3D;
                                    registroExistente.Quantidade = comprimentoAcumulado.ToString("F2");
                                }
                                else
                                {
                                    // Se não conseguir converter, inicia com o valor do tubo atual
                                    registroExistente.Quantidade = estrutura.Length3D.ToString();
                                }
                            }
                        }
                    }
                }


                // Processamento Superficies de Volume
                foreach (ObjectId volumesId in civilDb.GetSurfaceIds())
                {

                    var superficie = (Surface)TransCad.GetObject(volumesId, OpenMode.ForRead);



                    if (superficie.IsVolumeSurface)
                    {
                        TinVolumeSurface superficieVolume = (TinVolumeSurface)superficie;
                        // Criar chave única com base na estrutura
                        string chave = superficieVolume.Name;
                        superficieVolume.Rebuild();

                        // Processar valores
                        if (!estruturasDict.TryGetValue(chave, out EstruturaDrenagem registroExistente))
                        {

                            string familia = "Escavações/Reaterros";
                            string servico = ObterServicoEscavacao(chave);
                            string subitem = ObterSubItemEscavacao(superficieVolume);
                            string codigoComposicao = ObterCodigoEscavacao(subitem, servico);
                            string condicaoExe = ObterCondicaoEscavacao(chave);

                            // Aqui usamos o comprimento do primeiro tubo para iniciar a soma
                            registroExistente = new EstruturaDrenagem
                            {
                                Unidade = "",
                                Anexo = "",
                                Item_PPU = "",
                                Subcontrato = "",
                                Código_Composição = codigoComposicao,
                                Documento = Path.GetFileNameWithoutExtension(docData.Filename),
                                Familia = familia,
                                Serviço = servico,
                                Unidade_de_Medida = "m³",
                                Quantidade = ObterVolumeEscavacao(superficieVolume),
                                Subitem = subitem,
                                CondicaoExe = condicaoExe,
                                Dimensao = "Não se Aplica",
                                Observacoes = ObterObservacaoEscavacao(chave),

                            };

                            estruturasDict[chave] = registroExistente;

                        }

                    }
                }
                TransCad.Commit();
            }
            string csvPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "Resultados_Drenagem.csv");
            GerarRelatorioCSV(estruturasDict, csvPath);
        }

        // }
        // catch (System.Exception ex)
        // {
        // Exibir mensagem de erro no AutoCAD
        // Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument.Editor
        //  .WriteMessage($"\nErro ao gerar relatório: {ex.Message}");

        // }


        private static string ObterVolumeEscavacao(TinVolumeSurface superficie, string volumeEscavacao = "")
        {
            volumeEscavacao = Math.Abs(superficie.GetVolumeProperties().AdjustedNetVolume).ToString("F2");



            return volumeEscavacao;
        }

        private static string ObterObservacaoEscavacao(string nomeSuperficie)
        {
            string observacao = "";

            if (nomeSuperficie.Contains("TUBO"))
                observacao = "Escavação/Reaterro Tubos";

            if (nomeSuperficie.Contains("CAIXA"))
                observacao = "Escavação/Reaterro Caixas";

            return observacao;

        }

        private static string ObterCodigoEscavacao(string subitem, string servico)
        {
            string codigo = "";

            if (subitem.Contains("Profundidade") && servico.Contains("Escavação"))
                codigo = "06.2100.01";




            if (subitem.Contains("Concreto") && servico.Contains("Embasamento"))
                codigo = "06.1800.01";

            if (subitem.Contains("Pedra") && servico.Contains("Embasamento"))
                codigo = "06.1800.02";

            if (subitem.Contains("Areia") && servico.Contains("Embasamento"))
                codigo = "06.1800.03";




            if (subitem.Contains("Concreto") && servico.Contains("Reaterro"))
                codigo = "06.1900.01";

            if (subitem.Contains("Pedra") && servico.Contains("Reaterro"))
                codigo = "06.1900.02";

            if (subitem.Contains("Areia") && servico.Contains("Reaterro"))
                codigo = "06.1900.03";

            if (subitem.Contains("Jazida") && servico.Contains("Reaterro"))
                codigo = "06.1900.04";

            if (subitem.Contains("Escavação") && servico.Contains("Reaterro"))
                codigo = "06.1900.05";


            return codigo;
        }


        private static string ObterServicoEscavacao(string NomeSuperficie)
        {
            string servico = "";

            if (NomeSuperficie.Contains("EMB"))
                servico = "Embasamento de Tubulação";

            if (NomeSuperficie.Contains("ESC"))
                servico = "Escavação de Vala para Drenagem";

            if (NomeSuperficie.Contains("COMP"))
                servico = "Reaterro de Vala em Drenagem";


            return servico;
        }

        private static string ObterSubItemEscavacao(TinVolumeSurface superficie)
        {
            string subitem = superficie.Description;
            if (subitem == "")
                subitem = "Profundidade de 1,50 m a 3,00 m";

            return subitem;
        }

        private static string ObterCondicaoEscavacao(string NomeSuperficie)
        {
            string servico = "";

            if (NomeSuperficie.Contains("EMB"))
                servico = "Compactação Manual";

            if (NomeSuperficie.Contains("MECÂNICA"))
                servico = "Compactação Mecânica";

            if (NomeSuperficie.Contains("MANUAL"))
                servico = "Compactação Manual";

            if (NomeSuperficie.Contains("ESC"))
                servico = "Sem Esgotamento";


            return servico;
        }


        private static string ObterFamilia(string tipoEstrutura)
        {
            if (tipoEstrutura.Contains("CAIXA"))
                return "Caixas e Pocos";
            if (tipoEstrutura.Contains("BUEIRO"))
                return "Bueiros e Canalizacoes";
            if (tipoEstrutura.Contains("RALO"))
                return "Caixas e Pocos";
            if (tipoEstrutura.Contains("CANALETA"))
                return "Canaletas";
            if (tipoEstrutura.Contains("VALETA"))
                return "Valetas";
            return "Outros";
        }

        private static string ObterServico(string tipoEstrutura)
        {
            if (tipoEstrutura.Contains("CAIXA"))
                return "Caixa de Passagem";
            if (tipoEstrutura.Contains("BUEIRO"))
                return "Poco de Visita";
            if (tipoEstrutura.Contains("RALO"))
                return "Caixa de Ralo";
            if (tipoEstrutura.Contains("CANALETA"))
                return "Canaleta de Concreto Armado";
            if (tipoEstrutura.Contains("VALETA"))
                return "Valeta Trapezoidal em Grama";


            return "Outros";
        }


        private static string ObterCodigoComposicao(string familia)
        {
            return familia switch
            {
                "Caixa de Passagem" => "06.1000.02",
                "Poco de Visita" => "06.0900.01",
                "Caixa de Ralo" => "06.0800.01",
                "Valeta Trapezoidal em Grama" => "06.1100.01",
                "Canaleta de Concreto Armado" => "06.1200.02",
                _ => "00.0000.00"
            };
        }

        private static string ObterDimensoes(Structure estrutura)
        {
            try
            {
                return estrutura.PartSizeName;
            }
            catch
            {
                return "Dimensao indisponivel";
            }
        }

        private static string ObterFamiliaPipe(string tipoEstrutura)
        {
            if (tipoEstrutura.Contains("TUBO"))
                return "Tubulacoes";
            if (tipoEstrutura.Contains("CANALETA"))
                return "Canaletas";
            if (tipoEstrutura.Contains("VALETA"))
                return "Valetas";

            return "Outros";
        }

        private static string ObterServicoPipe(string tipoEstrutura)
        {
            if (tipoEstrutura.Contains("TUBO FERRO FUNDIDO"))
                return "Tubo de Ferro Fundido";
            if (tipoEstrutura.Contains("TUBO FERRO FUNDIDO COLETORES"))
                return "Tubo de Ferro Fundido";
            if (tipoEstrutura.Contains("BUEIRO"))
                return "Tubo de Concreto Armado";
            if (tipoEstrutura.Contains("TUBO AÇO"))
                return "Tubo de Aço Carbono";
            if (tipoEstrutura.Contains("TUBO DE CHAPA"))
                return "Tubo de Chapa de Aco Ondulada - Armco";
            if (tipoEstrutura.Contains("CANALETA"))
                return "Canaleta de Concreto Armado";
            if (tipoEstrutura.Contains("VALETA"))
                return "Valeta Trapezoidal em Grama";


            return "Outros";
        }


        private static string ObterCodigoComposicaoPipe(string servico)
        {
            return servico switch
            {
                "Tubo de Aço Carbono" => "06.1500.01",
                "Tubo de Chapa de Aco Ondulada - Armco" => "06.1400.01",
                "Tubo de Ferro Fundido" => "06.1600.01",
                "Tubo de Concreto Armado" => "06.1700.01",
                "Valeta Trapezoidal em Grama" => "06.1100.01",
                "Canaleta de Concreto Armado" => "06.1200.02",
                _ => "00.0000.00"

            };
        }




        private static string ObterDimensoesPipe(Pipe estrutura, string familia)
        {
            try
            {
                if (familia.Contains("Canaleta"))
                {

                    return $"Largura {estrutura.InnerDiameterOrWidth * 1000:F0}";
                }
                else
                {
                    return $"Diâmetro {estrutura.InnerDiameterOrWidth * 1000:F0}";
                }
            }
            catch
            {
                return "Dimensao indisponivel";
            }
        }

        private static bool ValidarPartFamilyName(string partFamilyName)
        {
            // Valida a PartFamilyName para garantir que segue um padrão específico
            if (string.IsNullOrEmpty(partFamilyName))
            {
                return false; // PartFamilyName não pode ser nula ou vazia
            }

            if (partFamilyName.StartsWith("Invalid_"))
            {
                return false; // PartFamilyName não pode começar com "Invalid_"
            }

            return true; // PartFamilyName é válida
        }





        private static void GerarRelatorioCSV(Dictionary<string, EstruturaDrenagem> estruturasDict, string filePath)
        {
            using (StreamWriter writer = new StreamWriter(filePath, false, Encoding.GetEncoding("ISO-8859-1")))
            {
                // Cabeçalho do CSV
                writer.WriteLine("Unidade;Anexo;Item_PPU;Subcontrato;Codigo_Composicao;Documento de Referencia;Familia;Servico;Unidade_de_Medida;Quantidade;Subitem;Condicao de Execucao;Dimensao;Observacoes");

                // Linhas de dados
                foreach (var estrutura in estruturasDict.Values)
                {
                    writer.WriteLine(
                        $"{estrutura.Unidade};{estrutura.Anexo};{estrutura.Item_PPU};{estrutura.Subcontrato};{estrutura.Código_Composição};{estrutura.Documento};{estrutura.Familia};{estrutura.Serviço};{estrutura.Unidade_de_Medida};{estrutura.Quantidade};{estrutura.Subitem};{estrutura.CondicaoExe};{estrutura.Dimensao};{estrutura.Observacoes}");
                }
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true // Importante para usar o programa padrão
            };

            Process.Start(startInfo);
            Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument.Editor
                .WriteMessage($"\nRelatório salvo em: {filePath}");
        }
    }
}