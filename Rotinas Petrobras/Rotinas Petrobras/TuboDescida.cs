using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Autodesk.Civil.Settings;

namespace AutomacoesCivil3D
{
    public class TubodeDescida
    {
        [CommandMethod("TuboDescida")]
        public static void TuboDescida()
        {
            CivilDocument DocCivil = Manager.DocCivil;
            Database DocData = Manager.DocData;
            Editor DocEditor = Manager.DocEditor;

            using (Transaction TransCad = DocData.TransactionManager.StartTransaction())
            {
                // Solicitar ao usuário selecionar um tubo
                PromptEntityOptions options = new PromptEntityOptions("\nSELECIONE O TUBO DE QUEDA:");
                options.SetRejectMessage("\nO objeto selecionado não é um tubo.");
                options.AddAllowedClass(typeof(Pipe), true);

                PromptEntityResult newPipeDownstreamId = DocEditor.GetEntity(options);
                if (newPipeDownstreamId.Status != PromptStatus.OK)
                {
                    DocEditor.WriteMessage("\nSeleção cancelada.");
                    return;
                }

                Pipe newPipeDownstream = (Pipe)TransCad.GetObject(newPipeDownstreamId.ObjectId, OpenMode.ForWrite);
                AdjustPipeSlope(newPipeDownstream, 1.0);
                
                TransCad.Commit();
                DocEditor.WriteMessage("\nEstrutura criada, tubo ajustado e bloco inserido com sucesso.");
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
