using Autodesk.Aec.Modeler;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Exception = System.Exception;

namespace AutomacoesCivil3D
{
    public class StructureRotation
    {

        [CommandMethod("CorrigirRotacao")]
        public static void CorrigirRotacao()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            Editor editor = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;
            try
            {
                using (Transaction transaction = doc.Database.TransactionManager.StartTransaction())
                {
                    // Solicitar ao usuário selecionar um tubo
                    PromptEntityOptions options = new PromptEntityOptions("\nSELECIONE QUALQUER TUBO DA REDE PARA COMEÇAR:");
                    options.SetRejectMessage("\nO objeto selecionado não é um tubo.");
                    options.AddAllowedClass(typeof(Pipe), true);

                    PromptEntityResult result = editor.GetEntity(options);
                    if (result.Status != PromptStatus.OK)
                    {
                        editor.WriteMessage("\nSeleção cancelada.");
                        return;
                    }

                    // Abrir o tubo selecionado
                    Pipe pipe = (Pipe)transaction.GetObject(result.ObjectId, OpenMode.ForWrite);
                    if (pipe == null)
                    {
                        editor.WriteMessage("\nErro ao acessar o tubo.");
                        return;
                    }

                    List<ObjectId> ListaId = new List<ObjectId>();
                    ObjectId previousStructureId = ObjectId.Null;
                    int contador = 1;


                    // Obter a rede associada ao tubo
                    Network network = (Network)transaction.GetObject(pipe.NetworkId, OpenMode.ForWrite);


                    foreach (ObjectId EstruturasId in OrdenarEstruturasPorTopologia(network, transaction))
                    {

                        Structure EstruturaAlvo = (Structure)transaction.GetObject(EstruturasId, OpenMode.ForWrite);

                                

                        editor.WriteMessage($"\nEstrutura: {EstruturaAlvo.Rotation*(180 * Math.PI)}");
                        double graus = 90;
                        double radians = ConvertToRadians(graus);

                        if (EstruturaAlvo.Rotation <= ConvertToRadians(90))
                        {
                                    EstruturaAlvo.Rotation = ConvertToRadians(82);
                        }
                        else
                        {
                           if (EstruturaAlvo.Rotation <= ConvertToRadians(180))
                           {
                               EstruturaAlvo.Rotation = ConvertToRadians(172);
                           }
                           else
                           {
                              if (EstruturaAlvo.Rotation <= ConvertToRadians(270))
                              {
                                  EstruturaAlvo.Rotation = ConvertToRadians(262);
                              }
                              else
                              {
                                  EstruturaAlvo.Rotation = ConvertToRadians(352);
                              }
                           }

                        }

                    }
                        

                    contador++;





                    transaction.Commit();
                    editor.WriteMessage("\nEstrutura criada, tubo ajustado e bloco inserido com sucesso.");

                }
            }
            catch (Exception ex)
            {
                editor.WriteMessage($"\nErro: {ex.Message}");
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        public static double ConvertToRadians(double degrees)
        {
            return degrees * (Math.PI / 180.0);
        }

        public static List<ObjectId> OrdenarEstruturasPorTopologia(Network network, Transaction TransCad)
        {
            List<ObjectId> estruturasOrdenadas = new List<ObjectId>();
            HashSet<ObjectId> visitados = new HashSet<ObjectId>();
            Queue<ObjectId> fila = new Queue<ObjectId>();

            // Encontrar a estrutura inicial (montante)
            ObjectId estruturaInicialId = EncontrarEstruturaMontante(network, TransCad);
            if (estruturaInicialId == ObjectId.Null)
            {
                throw new Exception("Não foi possível encontrar a estrutura inicial.");
            }

            // Começar com a estrutura inicial
            fila.Enqueue(estruturaInicialId);

            while (fila.Count > 0)
            {
                ObjectId estruturaAtualId = fila.Dequeue();

                if (visitados.Contains(estruturaAtualId))
                {
                    continue;
                }

                estruturasOrdenadas.Add(estruturaAtualId);
                visitados.Add(estruturaAtualId);

                // Adicionar as próximas estruturas conectadas
                foreach (ObjectId tuboId in network.GetPipeIds())
                {
                    Pipe tubo = (Pipe)TransCad.GetObject(tuboId, OpenMode.ForRead);

                    if (tubo.StartStructureId == estruturaAtualId && !visitados.Contains(tubo.EndStructureId))
                    {
                        fila.Enqueue(tubo.EndStructureId);
                    }
                    else if (tubo.EndStructureId == estruturaAtualId && !visitados.Contains(tubo.StartStructureId))
                    {
                        fila.Enqueue(tubo.StartStructureId);
                    }
                }
            }

            foreach (ObjectId estruturaId in estruturasOrdenadas)
            {
                Structure structure = (Structure)TransCad.GetObject(estruturaId, OpenMode.ForRead);
                Editor editor = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument.Editor;
                editor.WriteMessage($"\nEstrutura: {structure.Name}");
            }

            return estruturasOrdenadas;
        }


        private static ObjectId EncontrarEstruturaMontante(Network network, Transaction TransCad)
        {
            Dictionary<ObjectId, int> conexoes = new Dictionary<ObjectId, int>();

            // Contar conexões para cada estrutura
            foreach (ObjectId tuboId in network.GetPipeIds())
            {
                Pipe tubo = (Pipe)TransCad.GetObject(tuboId, OpenMode.ForRead);

                if (!conexoes.ContainsKey(tubo.StartStructureId))
                {
                    conexoes[tubo.StartStructureId] = 0;
                }
                if (!conexoes.ContainsKey(tubo.EndStructureId))
                {
                    conexoes[tubo.EndStructureId] = 0;
                }

                conexoes[tubo.StartStructureId]++;
                conexoes[tubo.EndStructureId]++;
            }

            // Encontrar a estrutura com menor número de conexões
            foreach (var estrutura in conexoes)
            {
                if (estrutura.Value == 1) // Estruturas com apenas uma conexão (potencial montante)
                {
                    return estrutura.Key;
                }
            }

            return ObjectId.Null; // Nenhuma estrutura de montante encontrada
        }






    }
}


