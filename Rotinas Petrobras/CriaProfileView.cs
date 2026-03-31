using Autodesk.Aec.Modeler;
using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using System.Text.Json;
using System.Windows.Forms.Design;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Autodesk.AutoCAD.GraphicsSystem;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using CriaProfiles;

namespace AutomacoesCivil3D
{
    public class CriaProfileView
    {
        [CommandMethod("GerarProfile")]
        public static void CriaProfile()
        {

           
            //Carrega os domumentos
            Document Cad = Manager.DocCad;
            CivilDocument doc = Manager.DocCivil;
            Editor ed = Manager.DocEditor;
            Database db = Manager.DocData;

            //Seleciona a polyline para criar o alinhamento
            PromptSelectionOptions opt = new PromptSelectionOptions();
            opt.AllowDuplicates = false;
            opt.AllowSubSelections = false; // Impede a seleção de sub-objetos dentro da polyline
            opt.SingleOnly = false; // Permite selecionar múltiplas polylines
            PromptSelectionResult res = ed.GetSelection();

            List<ObjectId> alignmentIds = new List<ObjectId>();
            PolylineOptions plops = new PolylineOptions();

            //Define as configurações do Alinhamento
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    // Código que manipula objetos
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    foreach (SelectedObject obj in res.Value)
            {

                if (obj.ObjectId.GetObject(OpenMode.ForRead) is Polyline pline)
                {

                    plops.AddCurvesBetweenTangents = true;
                    plops.EraseExistingEntities = true;
                    plops.PlineId = obj.ObjectId;

                    if (pline.Closed)
                    {
                        ed.WriteMessage($"\nA Polyline com ObjectId {obj.ObjectId.Handle} está fechada e não pode ser usada para criar um alinhamento.");
                        continue; // Ignora polylines fechadas
                    }
                    alignmentIds.Add(obj.ObjectId);
                }
                else
                {
                    ed.WriteMessage($"\nObjeto com ObjectId {obj.ObjectId.Handle} não é uma Polyline válida e será ignorado.");
                }

            }
                    tr.Commit();
                }
                catch (Exception ex)
                {
                    ed.WriteMessage($"\nErro ao manipular objetos: {ex.Message}");
                    tr.Abort(); // Importante abortar em caso de erro
                }
            }
        }
    }
}
