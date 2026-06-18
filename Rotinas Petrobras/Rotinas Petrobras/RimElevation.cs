using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutomacoesCivil3D
{
    public class RimElevation
    {
        [CommandMethod("RimElevation")]
        public void CotaTopo()
        {
            CivilDocument DocCivil = Manager.DocCivil;
            Database DocData = Manager.DocData;
            Editor DocEditor = Manager.DocEditor;
            int cont1 = 0;
            int cont2 = 0;
            int cont3 = 0;
            int index = 0;
            // Criar um dicionário para garantir que apenas um item de cada tipo seja adicionado
            Dictionary<string, EstruturaDrenagem> estruturasDict = new Dictionary<string, EstruturaDrenagem>();

            using (Transaction TransCad = DocData.TransactionManager.StartTransaction())
            {

                // Solicitar ao usuário selecionar um tubo
                PromptEntityOptions options = new PromptEntityOptions("\nSELECIONE UMA ESTRUTURA: ");
                options.SetRejectMessage("\nO objeto selecionado não é um tubo.");
                options.AddAllowedClass(typeof(Structure), true);
                PromptEntityResult newStructureId = DocEditor.GetEntity(options);
                ObjectId atual = newStructureId.ObjectId;
                Point3d endPoint = new Point3d(0, 0, 0);

                if (newStructureId.Status == PromptStatus.OK)
                {

                    Structure newStructure = (Structure)TransCad.GetObject(newStructureId.ObjectId, OpenMode.ForWrite);
                    Network network = (Network)TransCad.GetObject(newStructure.NetworkId, OpenMode.ForWrite);
                    Autodesk.AutoCAD.ApplicationServices.Core.Application.ShowAlertDialog($"\n A COTA ATUAL DA ESTRUTURA É:  {newStructure.RimElevation:F2}");
                    ObjectId Atual = newStructure.ObjectId;
                    int controle = 1;
                    // Solicitar um valor de inclinação em porcentagem
                    PromptDoubleOptions pdo = new PromptDoubleOptions("\nINSIRA A COTA DE TOPO DA ESTRUTURA: ");
                    pdo.AllowNegative = true;
                    pdo.AllowZero = false;
                    PromptDoubleResult pdr = DocEditor.GetDouble(pdo);
                    if (pdr.Status == PromptStatus.OK)                        
                    {
                        
                       
                        foreach (ObjectId tuboId in network.GetPipeIds())
                        {
                            Structure startStructure = (Structure)TransCad.GetObject(atual, OpenMode.ForWrite);
                            Pipe tubo = (Pipe)TransCad.GetObject(tuboId, OpenMode.ForWrite);

                            while (tubo.StartStructureId == atual && startStructure.BoundingShape == BoundingShapeType.Undefined)
                            {
                                
                            
                                if (tubo.StartStructureId == atual && controle == 1)
                                {
                                    controle = 0;
                                    double cota = pdr.Value;

                                    if (tubo.InnerDiameterOrWidth == 0.15)
                                    {
                                        cota = cota - 0.125;

                                    }
                                    else
                                    {
                                        cota = cota - 0.075;
                                    }


                                    //Application.ShowAlertDialog($"\n O SLOPE ATUAL DO TUBO É:  {tubo.Slope * 100}%");
                                    double slope = tubo.Slope;
                                    double multiplicador = Math.Tan(Math.Atan(slope));

                                    Point3d point = tubo.StartPoint;
                                    Point3d newStartPoint = new Point3d(point.X, point.Y, cota);
                                    tubo.StartPoint = newStartPoint;
                                    AdjustPipeSlope(tubo, multiplicador);
                                    endPoint = tubo.EndPoint;
                                    startStructure.ApplyRules();
                                    atual = tubo.EndStructureId;



                                    
                                }
                                else
                                {
                                    if (tubo.StartStructureId == atual)
                                    {
                                        double slope1 = tubo.Slope;
                                        double multiplicador1 = Math.Tan(Math.Atan(slope1));
                                        tubo.StartPoint = endPoint;
                                        AdjustPipeSlope(tubo, multiplicador1);
                                        endPoint = tubo.EndPoint;
                                        startStructure.ApplyRules();
                                        atual = tubo.EndStructureId;

                                        
                                    }
                                }



                            }

                        }
                       
                    }
                        
                }

                TransCad.Commit();
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
    }
}
