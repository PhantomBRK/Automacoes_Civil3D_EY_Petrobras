using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using System.Net;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutomacoesCivil3D
{
    public class PipeInclination
    {
        [CommandMethod("InclinacaoTubo")]
        public void SlopeTubo()
        {
            CivilDocument DocCivil = Manager.DocCivil;
            Database DocData = Manager.DocData;
            Editor DocEditor = Manager.DocEditor;

            using (Transaction TransCad = DocData.TransactionManager.StartTransaction())
            {

                // Solicitar ao usuário selecionar um tubo
                PromptEntityOptions options = new PromptEntityOptions("\nSELECIONE UM TUBO: ");
                options.SetRejectMessage("\nO objeto selecionado não é um tubo.");
                options.AddAllowedClass(typeof(Pipe), true);
                PromptEntityResult newPipeDownstreamId = DocEditor.GetEntity(options);

                // Solicitar um valor de inclinação em porcentagem
                PromptDoubleOptions pdo = new PromptDoubleOptions("\nINSIRA A INCLINAÇÃO PARA O TUBO: ");
                pdo.AllowNegative = true;
                pdo.AllowZero = false;
                PromptDoubleResult pdr = DocEditor.GetDouble(pdo);

                Pipe newPipeDownstream = (Pipe)TransCad.GetObject(newPipeDownstreamId.ObjectId, OpenMode.ForWrite);
                ObjectId str = newPipeDownstream.EndStructureId;               
                Structure newStructure = (Structure)TransCad.GetObject(str, OpenMode.ForWrite);
                Network network = (Network)TransCad.GetObject(newStructure.NetworkId, OpenMode.ForWrite);       
                ObjectId Atual = newStructure.ObjectId;
                Point3d endPoint = newPipeDownstream.EndPoint;
                ObjectId tuboAnterior = newPipeDownstreamId.ObjectId;
                Point3d endPointAnterior = newPipeDownstream.EndPoint;



                if (pdr.Status == PromptStatus.OK)
                {
                    double porcentagem = pdr.Value;

                    // Converter porcentagem para inclinação (fator multiplicador)
                    double multiplicador = Math.Tan(Math.Atan(porcentagem / 100));
                    AdjustPipeSlope(newPipeDownstream, multiplicador);



                    if (newPipeDownstreamId.Status != PromptStatus.OK)
                    {
                        DocEditor.WriteMessage("\nSeleção cancelada.");
                        return;
                    }

                    
                    
                    
                    foreach (ObjectId tuboId in network.GetPipeIds())
                    {
                        Structure startStructure = (Structure)TransCad.GetObject(Atual, OpenMode.ForWrite);
                        Pipe tubo = (Pipe)TransCad.GetObject(tuboId, OpenMode.ForWrite);

                        while (tubo.StartStructureId == Atual && startStructure.PartFamilyName != "CPO - CAIXA DE PASSAGEM OLEOSO")
                        {


                            if (tubo.StartStructureId == Atual)
                            {
                                double slope1 = tubo.Slope;
                                double multiplicador1 = Math.Tan(Math.Atan(slope1));
                                tubo.StartPoint = endPoint;
                                AdjustPipeSlope(tubo, multiplicador1);
                                endPoint = tubo.EndPoint;
                                endPointAnterior = tubo.StartPoint;
                                startStructure.ApplyRules();
                                Atual = tubo.EndStructureId;


                            }
                           










                        }
                    }
                            TransCad.Commit();
                


                }
                 else
                 {
                        DocEditor.WriteMessage("\nEntrada inválida ou operação cancelada.");
                 }
            }

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

        private static Pipe AdjustPipeSlopeStart(Pipe pipe, double slopeRatio)
        {
            // Ajustar o tubo para uma inclinação específica (1:1 por padrão)
            Point3d startPoint = pipe.StartPoint;
            Point3d endPoint = pipe.EndPoint;

            double horizontalDistance = Math.Sqrt(
                Math.Pow(endPoint.X - startPoint.X, 2) +
                Math.Pow(endPoint.Y - startPoint.Y, 2)
            );

            // Inclinação desejada
            double deltaZ = horizontalDistance * -slopeRatio;

            // Ajustar o Z do ponto final com base na inclinação desejada
            Point3d adjustedEndPoint = new Point3d(startPoint.X, startPoint.Y, endPoint.Z - deltaZ);
            pipe.EndPoint = adjustedEndPoint;
            return pipe;

        }

    }
}
