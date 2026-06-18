using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices; // Necessário para acessar CivilDocument
using Autodesk.Civil.DatabaseServices;    // Necessário para acessar a classe Structure

using System; // Necessário para Math.Atan2 e Math.PI
using System.Collections.Generic;

// Diretivas using para resolver conflitos de namespace, conforme solicitado.
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutomacoesCivil3D
{
    /// <summary>
    /// Classe para utilitários de blocos, contendo métodos relacionados à manipulação de blocos no desenho.
    /// </summary>
    public class BlocoUtils
    {
        /// <summary>
        /// Comando AutoCAD que lista, permite selecionar e insere blocos que seguem o padrão "DED-XX A",
        /// com opções de rotação e escala 1:1:1.
        /// </summary>
        [CommandMethod("InsereBlocoDEDComRotacao")]
        public void InsereBlocoDEDComRotacao()
        {
            // O bloco try-catch é uma boa prática para lidar com exceções inesperadas durante a execução do comando.
            try
            {
                // Obtém o documento AutoCAD ativo e seu editor para interação com o usuário.
                Document civilDoc = Manager.DocCad;
                Editor docEditor = Manager.DocEditor;
                Database db = civilDoc.Database;

                List<string> blocosDisponiveis = new List<string>();

                // Inicia uma transação para acessar o BlockTable (tabela de definições de blocos).
                using (Transaction trans = db.TransactionManager.StartTransaction())
                {
                    // Abre o BlockTable para leitura.
                    BlockTable bt = (BlockTable)trans.GetObject(db.BlockTableId, OpenMode.ForRead);

                    // Itera sobre todas as definições de bloco no desenho.
                    foreach (ObjectId btrId in bt)
                    {
                        BlockTableRecord btr = (BlockTableRecord)trans.GetObject(btrId, OpenMode.ForRead);

                        // Filtra os blocos:
                        // - !btr.IsLayout: Exclui layouts de papel.
                        // - !btr.IsAnonymous: Exclui blocos anônimos (gerados automaticamente pelo AutoCAD, como os de hachuras).
                        // - btr.Name.StartsWith("DED-") && btr.Name.EndsWith(" A"): Filtra pelo padrão de nome desejado.
                        // - Validação numérica: Garante que o número entre "DED-" e " A" esteja entre 1 e 13.
                        if (!btr.IsLayout && !btr.IsAnonymous && btr.Name.StartsWith("DED-") && btr.Name.EndsWith(" A"))
                        {
                            string[] parts = btr.Name.Split('-');
                            if (parts.Length == 2)
                            {
                                string numPart = parts[1].TrimEnd(' ', 'A'); // Remove espaços e 'A' do final
                                if (int.TryParse(numPart, out int blockNum))
                                {
                                    if (blockNum >= 1 && blockNum <= 13)
                                    {
                                        blocosDisponiveis.Add(btr.Name);
                                    }
                                }
                            }
                        }
                    }
                    trans.Commit(); // Confirma a transação de leitura.
                }

                // Verifica se algum bloco foi encontrado.
                if (blocosDisponiveis.Count == 0)
                {
                    docEditor.WriteMessage("\nNenhum bloco com o padrão DED-XX A foi encontrado no desenho atual.");
                    return;
                }

                blocosDisponiveis.Sort(); // Ordena a lista de blocos para exibição.

                // Constrói a mensagem para o usuário com a lista de blocos disponíveis.
                string promptMessage = "\nBlocos 'DED-XX A' disponíveis para inserção:\n";
                for (int i = 0; i < blocosDisponiveis.Count; i++)
                {
                    promptMessage += $"  {i + 1}. {blocosDisponiveis[i]}\n";
                }
                promptMessage += "\nDigite o NÚMERO do bloco que deseja inserir: ";

                // Solicita ao usuário que escolha um bloco pelo número.
                PromptIntegerOptions pio = new PromptIntegerOptions(promptMessage);
                pio.AllowNegative = false;
                pio.AllowZero = false;
                pio.LowerLimit = 1;
                pio.UpperLimit = blocosDisponiveis.Count;
                pio.AppendKeywordsToMessage = false; // Evita que o AutoCAD adicione opções de palavras-chave.

                PromptIntegerResult pIntRes = docEditor.GetInteger(pio);

                if (pIntRes.Status != PromptStatus.OK)
                {
                    docEditor.WriteMessage("\nSeleção de bloco cancelada pelo usuário.");
                    return;
                }

                string blocoEscolhidoNome = blocosDisponiveis[pIntRes.Value - 1]; // Obtém o nome do bloco escolhido.

                List<string> opcoesRotacao = new List<string>
                {
                    "Nenhum",
                    "Estrutura",
                    "Pontos"
                };



                /*/ Constrói a mensagem para o usuário com a lista de blocos disponíveis.
                string promptMessageRt = "\nBlocos 'DED-XX A' disponíveis para inserção:\n";
                for (int i = 0; i < 3; i++)
                {
                    promptMessageRt += $"  {i + 1}. {opcoesRotacao[i]}\n";
                }
                promptMessage += "\nDigite o NÚMERO do metodo que deseja usar: ";

                // Solicita ao usuário que escolha um bloco pelo número.
                PromptIntegerOptions pio1 = new PromptIntegerOptions(promptMessageRt);
                pio.AllowNegative = false;
                pio.AllowZero = false;
                pio.LowerLimit = 1;
                pio.UpperLimit = opcoesRotacao.Count;
                pio.AppendKeywordsToMessage = false; // Evita que o AutoCAD adicione opções de palavras-chave.

                PromptIntegerResult pIntRes1 = docEditor.GetInteger(pio1);

                if (pIntRes1.Status != PromptStatus.OK)
                {
                    docEditor.WriteMessage("\nSeleção de bloco cancelada pelo usuário.");
                    return;
                }*/

                // 1. Definição da rotação
                double rotationAngle = 0.0; // Rotação padrão (0 radianos, equivalente a 0 graus).


                docEditor.WriteMessage("\nOperação de inserção de bloco cancelada.");

                //string opcaoEscolhida = opcoesRotacao[pIntRes1.Value - 1]; // Obtém o nome do bloco escolhido.

                string opcaoEscolhida = "Pontos"; 

                // Lógica para obter a rotação com base na escolha do usuário.
                if (opcaoEscolhida == "Estrutura")
                {
                    PromptEntityOptions peo2 = new PromptEntityOptions("\nSelecione a estrutura de drenagem:");
                    // Adiciona um filtro para permitir apenas objetos do tipo Autodesk.Civil.DatabaseServices.Structure.
                    // O segundo parâmetro 'true' indica que subclasses também são permitidas.
                    peo2.SetRejectMessage("\nEntidade selecionada não é uma estrutura de drenagem ou é inválida.");
                    peo2.AddAllowedClass(typeof(Structure), true);
                    

                    PromptEntityResult per2 = docEditor.GetEntity(peo2);

                    if (per2.Status == PromptStatus.OK)
                    {
                        // Abrir uma transação interna para acessar o objeto da estrutura.
                        // É crucial usar uma nova transação para acessar objetos do banco de dados.
                        using (Transaction transInternal = db.TransactionManager.StartTransaction())
                        {
                            // Tenta converter o objeto selecionado para uma Structure do Civil 3D.
                            Structure civilStructure = transInternal.GetObject(per2.ObjectId, OpenMode.ForRead) as Structure;
                            if (civilStructure != null)
                            {
                                // A propriedade Rotation da estrutura já está em radianos e representa a rotação em relação ao WCS.
                                rotationAngle = civilStructure.Rotation;
                                docEditor.WriteMessage($"\nRotação definida pela estrutura: {rotationAngle * 180 / Math.PI:F2} graus.");
                            }
                            else
                            {
                                docEditor.WriteMessage("\nErro: A entidade selecionada não pôde ser convertida para uma estrutura de drenagem. Rotação padrão (0 graus) será usada.");
                            }
                            transInternal.Commit();
                        }
                    }
                    else
                    {
                        docEditor.WriteMessage("\nSeleção de estrutura cancelada ou inválida. Rotação padrão (0 graus) será usada.");
                    }
                }
                else if (opcaoEscolhida == "Pontos")
                {
                    // Solicita o primeiro ponto.
                    PromptPointOptions ppo1 = new PromptPointOptions("\nSelecione o primeiro ponto para definir a rotação:");
                    PromptPointResult ppr1 = docEditor.GetPoint(ppo1);

                    if (ppr1.Status == PromptStatus.OK)
                    {
                        // Solicita o segundo ponto, usando o primeiro como base para a linha de borracha (rubber-band).
                        PromptPointOptions ppo2 = new PromptPointOptions("\nSelecione o segundo ponto para definir a rotação:");
                        ppo2.UseBasePoint = true;
                        ppo2.BasePoint = ppr1.Value;
                        PromptPointResult ppr2 = docEditor.GetPoint(ppo2);

                        if (ppr2.Status == PromptStatus.OK)
                        {
                            Point3d pt1 = ppr1.Value;
                            Point3d pt2 = ppr2.Value;
                            // Calcula o ângulo em radianos entre os dois pontos. Math.Atan2 é ideal pois lida com todos os quadrantes.
                            // O ângulo é medido no sentido anti-horário a partir do eixo X positivo do WCS.
                            rotationAngle = Math.Atan2(pt2.Y - pt1.Y, pt2.X - pt1.X);
                            docEditor.WriteMessage($"\nRotação definida por dois pontos: {rotationAngle * 180 / Math.PI:F2} graus.");
                        }
                        else
                        {
                            docEditor.WriteMessage("\nSegundo ponto não selecionado. Rotação padrão (0 graus) será usada.");
                        }
                    }
                    else
                    {
                        docEditor.WriteMessage("\nPrimeiro ponto não selecionado. Rotação padrão (0 graus) será usada.");
                    }
                }
                // Para "Nenhum", rotationAngle já está em 0.0, então nenhuma ação adicional é necessária.

                // 2. Seleção do ponto de inserção
                PromptPointOptions ppo = new PromptPointOptions($"\nSELECIONE O PONTO DE INSERÇÃO DO BLOCO '{blocoEscolhidoNome}':");
                ppo.AllowNone = false; // Não permite que o usuário cancele a seleção do ponto.

                PromptPointResult pPtRes = docEditor.GetPoint(ppo);

                if (pPtRes.Status != PromptStatus.OK)
                {
                    docEditor.WriteMessage("\nOperação cancelada. Ponto de inserção não selecionado.");
                    return;
                }

                Point3d pontoInsercao = pPtRes.Value;

                // 3. Inserção do bloco com escala e rotação definidas
                // Inicia uma nova transação para modificações no banco de dados (inserção do bloco).
                using (Transaction trans = db.TransactionManager.StartTransaction())
                {
                    // Abre o BlockTable para leitura para encontrar a definição do bloco.
                    BlockTable btWrite = (BlockTable)trans.GetObject(db.BlockTableId, OpenMode.ForRead);

                    // Verifica se o bloco escolhido existe na tabela de blocos.
                    if (!btWrite.Has(blocoEscolhidoNome))
                    {
                        docEditor.WriteMessage($"\nErro: O bloco '{blocoEscolhidoNome}' não foi encontrado na tabela de blocos (após seleção).");
                        trans.Abort(); // Aborta a transação em caso de erro.
                        return;
                    }

                    // Abre o ModelSpace (espaço do modelo) para escrita, onde o bloco será inserido.
                    BlockTableRecord btrModelo = (BlockTableRecord)trans.GetObject(btWrite[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    // Obtém a definição do bloco escolhido para leitura.
                    BlockTableRecord btrBlock = (BlockTableRecord)trans.GetObject(btWrite[blocoEscolhidoNome], OpenMode.ForRead);

                    // Cria uma nova instância (referência) do bloco no ponto de inserção.
                    BlockReference blkRef = new BlockReference(pontoInsercao, btrBlock.ObjectId);

                    // Define a escala para 1:1:1 (X, Y, Z). Isso garante que o bloco seja inserido com suas dimensões originais,
                    // independentemente da escala de anotação ou de inserção padrão do AutoCAD.
                    blkRef.ScaleFactors = new Scale3d(0.001, 0.001, 0.001);
                    // Aplica a rotação calculada ou padrão (0 radianos).
                    blkRef.Rotation = rotationAngle;

                    // Adiciona a referência do bloco ao Model Space.
                    btrModelo.AppendEntity(blkRef);
                    // Informa ao AutoCAD que um novo objeto foi criado e deve ser adicionado ao banco de dados.
                    trans.AddNewlyCreatedDBObject(blkRef, true);

                    trans.Commit(); // Confirma as alterações no banco de dados.
                    docEditor.WriteMessage($"\nBloco '{blocoEscolhidoNome}' inserido com sucesso em X:{pontoInsercao.X:F2}, Y:{pontoInsercao.Y:F2}, Z:{pontoInsercao.Z:F2} com rotação de {rotationAngle * 180 / Math.PI:F2} graus.");
                }
            }
            catch (Exception ex)
            {
                // Captura e exibe qualquer exceção que ocorra durante a execução do comando.
                Application.ShowAlertDialog($"Erro ao inserir bloco: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}