using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using System.Net;
using Exception = System.Exception;


public class AplicarConexoes
{
    [CommandMethod("AplicarConexao")]
    public static void CreateStructureAndAdjustPipe()
    {
        Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        Editor editor = doc.Editor;
        CivilDocument civilDoc = CivilApplication.ActiveDocument;
        try {
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
                Autodesk.Civil.DatabaseServices.Network network = (Autodesk.Civil.DatabaseServices.Network)transaction.GetObject(pipe.NetworkId, OpenMode.ForWrite);
                

                foreach (ObjectId EstruturasId in OrdenarEstruturasPorTopologia(network, transaction))
                {

                    Structure EstruturaAlvo = (Structure)transaction.GetObject(EstruturasId, OpenMode.ForWrite);
                    

                    
                    if(!ListaId.Contains(EstruturasId))
                    {
                        //EstruturaAlvo.ApplyRules();
                        ListaId.Add(EstruturasId);
                    }
                   
                    

                    for (int i = 0; i < EstruturaAlvo.ConnectedPipesCount; i++)
                    {


                        Pipe tuboMontante = (Pipe)transaction.GetObject(EstruturaAlvo.get_ConnectedPipe(i), OpenMode.ForWrite);

                        if (tuboMontante.StartStructureId == EstruturaAlvo.Id)
                        {

                            string nomeEstrutura = $"Curva 45 - 0{contador}";
                            // Criar o tubo entre a estrutura a montante e a nova estrutura com a inclinação original
                            ObjectId Montate = tuboMontante.StartStructureId;
                            ObjectId Jusante = tuboMontante.EndStructureId;
                            Structure JusanteStructure = (Structure)transaction.GetObject(Jusante, OpenMode.ForRead);
                            Structure MontanteStructure = (Structure)transaction.GetObject(Montate, OpenMode.ForRead);

                            ObjectId pipeFamilyId = tuboMontante.PartFamilyId;
                            PartFamily pipeFamily = (PartFamily)transaction.GetObject(pipeFamilyId, OpenMode.ForRead);
                            string pipeName = tuboMontante.PartSizeName;
                            ObjectId pipeSizeId = ObjectId.Null;
                            ObjectId newPipeUpstreamId = ObjectId.Null;

                            for (int j = 0; j < pipeFamily.PartSizeCount; j++)
                            {
                                ObjectId sizeId = pipeFamily[j];
                                PartSize size = (PartSize)transaction.GetObject(sizeId, OpenMode.ForRead);
                                if (size.Name == pipeName)
                                {
                                    pipeSizeId = sizeId;
                                }
                            }



                            Vector3d direction = tuboMontante.StartPoint.GetVectorTo(tuboMontante.EndPoint).GetNormal(); // Normaliza o vetor
                            // Reduzir o vetor em offsetDistance antes do ponto final
                            Point3d pontoJoelho = tuboMontante.EndPoint - (direction * (tuboMontante.InnerDiameterOrWidth + .25));

                            LineSegment3d LinhaMontante = new LineSegment3d(tuboMontante.StartPoint, pontoJoelho);
                            network.AddLinePipe(pipeFamilyId, pipeSizeId, LinhaMontante, ref newPipeUpstreamId, true);
                            Pipe newPipeUpstream = (Pipe)transaction.GetObject(newPipeUpstreamId, OpenMode.ForWrite);
                            newPipeUpstream.ConnectToStructure(ConnectorPositionType.Start, MontanteStructure.Id, true);
                            AdjustPipeSlopeToMatchOriginal(newPipeUpstream, tuboMontante);

                            ObjectId newPipeDownstreamId = ObjectId.Null;
                            //Corrigir ponto final do tubo pipe.EndPoint
                            LineSegment3d LinhaJusante = new LineSegment3d(newPipeUpstream.EndPoint, tuboMontante.EndPoint);

                            network.AddLinePipe(pipeFamilyId, pipeSizeId, LinhaJusante, ref newPipeDownstreamId, true);
                            Pipe newPipeDownstream = (Pipe)transaction.GetObject(newPipeDownstreamId, OpenMode.ForWrite);
                            tuboMontante.Erase();
                            // Criar uma estrutura no ponto selecionado
                            Structure structure = CreateStructure(civilDoc, transaction, tuboMontante, pontoJoelho, NomeEstrutura(nomeEstrutura, contador, network));
                            if (structure.Name == null)
                            {
                                editor.WriteMessage("\nErro ao criar a estrutura.");
                                return;
                            }
                            //newPipeUpstream.ConnectToStructure(ConnectorPositionType.End, structure.Id, true);
                            //newPipeDownstream.ConnectToStructure(ConnectorPositionType.Start, structure.Id, true);
                            //newPipeDownstream.ConnectToStructure(ConnectorPositionType.End, JusanteStructure.Id, false);
                            // Inserir o bloco no ponto da estrutura
                            string blockName = $"Curva-{Math.Round(tuboMontante.InnerDiameterOrWidth, 2)}"; // Corrigido para usar a propriedade correta
                            InsertBlock(doc, transaction, pontoJoelho, blockName, tuboMontante);
                            AdjustPipeSlope(newPipeDownstream, 1.0);
                            EstruturaAlvo.SumpDepth = newPipeDownstream.EndPoint.Z - newPipeDownstream.OuterDiameterOrWidth / 2 - 0.15;


                            if (EstruturaAlvo.Rotation <= 90)
                            {
                                EstruturaAlvo.Rotation = 82;
                            }
                            else
                            {
                                if (EstruturaAlvo.Rotation <= 180)
                                {
                                    EstruturaAlvo.Rotation = 172;
                                }
                                else
                                {
                                    if (EstruturaAlvo.Rotation <= 270)
                                    {
                                        EstruturaAlvo.Rotation = 262;
                                    }
                                    else
                                    {
                                        EstruturaAlvo.Rotation = 372;
                                    }
                                }
                                
                            }

                        }
                    }
                }
                 
                    contador++;
                    


               

                transaction.Commit();
                editor.WriteMessage("\nEstrutura criada, tubo ajustado e bloco inserido com sucesso.");

            }
         }
        catch (System.Exception ex)
        {
            editor.WriteMessage($"\nErro: {ex.Message}");
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }


    private static Structure CreateStructure(CivilDocument civilDoc, Transaction transaction, Pipe pipe, Point3d location, string structureName)
    {
        // Obter a rede associada ao tubo
        Autodesk.Civil.DatabaseServices.Network network = (Autodesk.Civil.DatabaseServices.Network)transaction.GetObject(pipe.NetworkId, OpenMode.ForWrite);

       
        // Definir parâmetros adicionais
        double rotation = CalculatePipeAngle(pipe); // Rotação padrão
        bool applyRules = false;

        // Criar a estrutura
        ObjectId newStructureId = ObjectId.Null;
        ObjectId familyId = transaction.GetObject(network.PartsListId , OpenMode.ForRead).Id;
        PartsList partsList = (PartsList)transaction.GetObject(familyId, OpenMode.ForRead);
        ObjectIdCollection structureList = partsList.GetPartFamilyIdsByDomain(DomainType.Structure);
        PartFamily partFamily = (PartFamily)transaction.GetObject(structureList[0], OpenMode.ForRead);
        ObjectId partFamilyId = partFamily.Id;
        ObjectId StrSizeId = ObjectId.Null;


        for (int i = 0; i < partFamily.PartSizeCount; i++)
        {
            ObjectId sizeId = partFamily[i];
            PartSize size = (PartSize)transaction.GetObject(sizeId, OpenMode.ForRead);
           if(size.Name == "Null Structure")
            {
                
                // Aqui você pode usar o ID do size como necessário
                StrSizeId = sizeId;
                // Use structureSizeId no restante do código
            }
        }

        network.AddStructure(partFamilyId, StrSizeId, location, rotation, ref newStructureId, applyRules);  
        Structure structure = (Structure)transaction.GetObject(newStructureId, OpenMode.ForWrite);
        structure.Name = structureName;
        return structure;

    }



    private static void AdjustPipeSlopeToMatchOriginal(Pipe NewPipe, Pipe pipe)
    {
        // Ajustar o tubo para manter a inclinação original
        Point3d startPoint = pipe.StartPoint;
        Point3d endPoint = pipe.EndPoint;

        Point3d NewStartPoint = NewPipe.StartPoint;
        Point3d NewEndPoint = NewPipe.EndPoint;


        double deltaZ = endPoint.Z - startPoint.Z;
        double horizontalDistance = Math.Sqrt(
            Math.Pow(endPoint.X - startPoint.X, 2) +
            Math.Pow(endPoint.Y - startPoint.Y, 2)
        );


        double NewDeltaZ = NewEndPoint.Z - NewStartPoint.Z;
        double NewHorizontalDistance = Math.Sqrt(
            Math.Pow(NewEndPoint.X - NewStartPoint.X, 2) +
            Math.Pow(NewEndPoint.Y - NewStartPoint.Y, 2)
        );

        // Inclinação original
        double slope = deltaZ / horizontalDistance;

        // Ajustar o Z do ponto final com base na inclinação original
        double newDeltaZ = slope * NewHorizontalDistance;
        Point3d adjustedEndPoint = new Point3d(NewEndPoint.X, NewEndPoint.Y, startPoint.Z + newDeltaZ);

        NewPipe.StartPoint = startPoint;
        NewPipe.EndPoint = adjustedEndPoint;
    }

    private static Pipe AdjustPipeSlope(Pipe pipe, double slopeRatio)
    {
        // Ajustar o tubo para uma inclinação específica (1:1 por padrão)
        Point3d startPoint = pipe.StartPoint;
        Point3d endPoint = pipe.EndPoint;

        double horizontalDistance = Math.Sqrt(
            Math.Pow(endPoint.X - startPoint.X, 2) +
            Math.Pow(endPoint.Y - startPoint.Y, 2)
        );

        // Inclinação desejada
        double deltaZ = horizontalDistance * slopeRatio;

        // Ajustar o Z do ponto final com base na inclinação desejada
        Point3d adjustedEndPoint = new Point3d(endPoint.X, endPoint.Y, startPoint.Z - deltaZ);
        pipe.EndPoint = adjustedEndPoint;
        return pipe;

    }

    public static string NomeEstrutura(string NomeStructure, int contAL, Network network)
        {
        Document DocData = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        CivilDocument civilDoc = CivilApplication.ActiveDocument;

        using (Transaction TransCad = DocData.TransactionManager.StartTransaction())
            {
               
            List<ObjectId> ListaIdStructure = new List<ObjectId>();

            foreach (ObjectId Id in network.GetStructureIds())
            {
                Structure structure = (Structure)TransCad.GetObject(Id, OpenMode.ForRead);
                ListaIdStructure.Add(Id);
            }
            
            ListaIdStructure.Sort();

            label1:
            foreach (ObjectId Id in ListaIdStructure)
                {
                    Structure structure = (Structure)TransCad.GetObject(Id, OpenMode.ForRead);
                    if (structure.Name.Equals(NomeStructure))
                    {
                        contAL++;
                        string NomeEstrutura = (string)$"Curva 45 - 0{contAL}";
                        NomeStructure = NomeEstrutura;
                    goto label1;
                }
                }
                TransCad.Commit();
            }
            return NomeStructure;
        }


    private static double CalculatePipeAngle(Pipe pipe)
    {
        // Obter os pontos inicial e final do tubo
        Point3d startPoint = pipe.StartPoint;
        Point3d endPoint = pipe.EndPoint;

        // Calcular o ângulo em relação ao eixo X
        return Math.Atan2(endPoint.Y - startPoint.Y, endPoint.X - startPoint.X);
    }


    private static void InsertBlock(Document doc, Transaction transaction, Point3d position, string blockName, Pipe pipe)
    {
        Database db = doc.Database;

        // Verificar se o bloco já existe
        BlockTable blockTable = (BlockTable)transaction.GetObject(db.BlockTableId, OpenMode.ForRead);
        if (!blockTable.Has(blockName))
        {
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\nBloco não encontrado: {blockName}");
            return;
        }

        // Inserir o bloco no ponto
        BlockTableRecord btr = (BlockTableRecord)transaction.GetObject(blockTable[blockName], OpenMode.ForRead);
        BlockReference blockReference = new BlockReference(position, btr.ObjectId);
        
        blockReference.Rotation = CalculatePipeAngle(pipe); // Rotação padrão

        BlockTableRecord currentSpace = (BlockTableRecord)transaction.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        currentSpace.AppendEntity(blockReference);
        transaction.AddNewlyCreatedDBObject(blockReference, true);




       



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
            Editor editor = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
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
