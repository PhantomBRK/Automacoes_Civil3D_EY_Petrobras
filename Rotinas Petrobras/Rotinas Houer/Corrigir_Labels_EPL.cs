using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Colors;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Label = Autodesk.Civil.DatabaseServices.Label;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace AutomacoesCivil3D
{
    public class StationOffsetLabelsTools
    {
        [CommandMethod("FIX_STATIONOFFSET_LABELS")]
        public static void FixStationOffsetLabels()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            CivilDocument civilDb = Manager.DocCivil;
            Database db = civilDoc.Database;

            // 1) Pega um label de referência já com estilo configurado
            PromptEntityOptions peo = new PromptEntityOptions(
                "\nSelecione um Station Offset label de referência (sem background/frame e texto ByLayer): ");
            peo.SetRejectMessage("\nSelecione apenas labels do Civil 3D.");
            peo.AddAllowedClass(typeof(Label), false);

            PromptEntityResult per = docEditor.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
            {
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Label refLabel = (Label)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                ObjectId refStyleId = refLabel.StyleId;

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms =
                    (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                int total = 0;
                int alterados = 0;

                foreach (ObjectId entId in ms)
                {
                    Entity ent = (Entity)tr.GetObject(entId, OpenMode.ForRead);
                    Label lbl = ent as Label;
                    if (lbl == null)
                    {
                        continue;
                    }

                    // Filtra só labels cujo tipo gerenciado contém "StationOffset"
                    string typeName = lbl.GetType().FullName ?? string.Empty;
                    if (!typeName.Contains("StationOffset"))
                    {
                        continue;
                    }

                    total++;

                    if (lbl.StyleId != refStyleId)
                    {
                        lbl.UpgradeOpen();
                        lbl.StyleId = refStyleId;
                        alterados++;
                    }
                }

                tr.Commit();

                docEditor.WriteMessage(
                    $"\nStation Offset labels encontrados: {total}. Estilos alterados: {alterados}.");
            }
        }
    }
}
