using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Colors;
using System.Windows.Forms;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace AutomacoesCivil3D
{
    public class Helper_Criar_Layers
    {

        public void CreateLayer(string nomeLayer)
        {
            // Obtém o documento ativo no AutoCAD
            Document doc = Manager.DocCad;
            if (doc == null)
            {
                // Se não houver documento ativo, encerra a execução do comando
                return;
            }

            // Obtém o banco de dados e o editor do documento
            Database db = Manager.DocData;
            Editor ed = Manager.DocEditor;

            

            string layerName = nomeLayer;

            // Inicia uma transação para garantir a integridade do banco de dados
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Abre a tabela de layers para leitura
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead, false);

                // Verifica se a layer já existe
                if (lt.Has(layerName))
                {
                    // Se a layer já existir, exibe uma mensagem de erro e encerra a execução do comando
                    // Application.ShowAlertDialog($"Layer '{layerName}' já existe.");
                    return;
                }

                // Cria um novo registro de layer
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = layerName;

                // Define propriedades da layer
                ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, 3); // Cor Vermelha (índice 1 na paleta ACI)
                ltr.LineWeight = LineWeight.ByLineWeightDefault; // Peso da linha padrão
                ltr.LinetypeObjectId = db.ContinuousLinetype; // Tipo de linha contínua (padrão)
                ltr.IsPlottable = true; // Define se a layer é plotável

                // Abre a tabela de layers para escrita
                lt.UpgradeOpen();

                // Adiciona o novo registro de layer à tabela de layers
                ObjectId layerId = lt.Add(ltr);

                // Adiciona o novo objeto à transação
                tr.AddNewlyCreatedDBObject(ltr, true);

                // Confirma a transação e salva as alterações no banco de dados
                tr.Commit();

                // Define a nova layer como a layer atual
                //doc.Editor.SetCurrentLayer(layerName);

                // Exibe uma mensagem de sucesso ao usuário
                //Application.ShowAlertDialog($"Layer '{layerName}' foi criada com sucesso e definida como a layer atual.");
            }
        }
    }
}
