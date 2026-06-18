using System;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.Aec.PropertyData;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Label = Autodesk.Civil.DatabaseServices.Label;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace AutomacoesCivil3D
{
    public class CorridorSolidsPhysicalPset
    {
        private const string PhysicalPsetName = "C - Propriedades Fisicas dos Objetos e Elementos";

        [CommandMethod("ATUALIZA_PSET_FISICO_SOLIDOS")]
        public void AtualizarPsetFisicoSolidos()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database db = civilDoc.Database;

            using (Transaction trans = db.TransactionManager.StartTransaction())
            {
                // Localiza o Pset C no desenho
                DictionaryPropertySetDefinitions dictPsd =
                    new DictionaryPropertySetDefinitions(db);

                if (!dictPsd.Has(PhysicalPsetName, trans))
                {
                    docEditor.WriteMessage(
                        "\nProperty Set Definition \"{0}\" não encontrado. Verifique se o Pset está definido no desenho.",
                        PhysicalPsetName);
                    return;
                }

                ObjectId psetDefId = dictPsd.GetAt(PhysicalPsetName);

                BlockTable blockTable =
                    (BlockTable)trans.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace =
                    (BlockTableRecord)trans.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId entId in modelSpace)
                {
                    Entity entity = (Entity)trans.GetObject(entId, OpenMode.ForWrite);
                    Solid3d solid = entity as Solid3d;

                    if (solid == null)
                    {
                        continue;
                    }

                    try
                    {
                        AtualizarPropriedadesFisicasDoSolido(trans, solid, psetDefId);
                    }
                    catch (Exception ex)
                    {
                        docEditor.WriteMessage(
                            "\nFalha ao atualizar Pset físico do sólido {0}: {1}",
                            solid.Handle.ToString(), ex.Message);
                    }
                }

                trans.Commit();
            }
        }

        private static void AtualizarPropriedadesFisicasDoSolido(
            Transaction trans,
            Solid3d solid,
            ObjectId psetDefId)
        {
            // Garante que o Property Set esteja anexado ao sólido
            DBObject dbObj = solid;
            ObjectId psetId;

            try
            {
                psetId = PropertyDataServices.GetPropertySet(dbObj, psetDefId);
            }
            catch (Exception)
            {
                PropertyDataServices.AddPropertySet(dbObj, psetDefId);
                psetId = PropertyDataServices.GetPropertySet(dbObj, psetDefId);
            }

            PropertySet propertySet =
                (PropertySet)trans.GetObject(psetId, OpenMode.ForWrite);

            // Comprimento / Largura / Espessura via extents (WCS)
            Extents3d extents = solid.GeometricExtents;
            double comprimento = Math.Abs(extents.MaxPoint.X - extents.MinPoint.X);
            double largura = Math.Abs(extents.MaxPoint.Y - extents.MinPoint.Y);
            double espessura = Math.Abs(extents.MaxPoint.Z - extents.MinPoint.Z);

            // Área de superfície
            double area = 0.0;
            try
            {
                area = solid.Area;
            }
            catch (Exception)
            {
                area = 0.0;
            }

            // Volume via MassProperties
            double volume = 0.0;
            try
            {
                Solid3dMassProperties massProps = solid.MassProperties;
                volume = massProps.Volume;
            }
            catch (Exception)
            {
                volume = 0.0;
            }

            // Grava nos campos do Pset C (se existirem)
            SetDoubleIfExists(propertySet, "Comprimento", comprimento);
            SetDoubleIfExists(propertySet, "Largura", largura);
            SetDoubleIfExists(propertySet, "Espessura", espessura);
            SetDoubleIfExists(propertySet, "Area", area);
            SetDoubleIfExists(propertySet, "Volume", volume);
        }

        private static void SetDoubleIfExists(
            PropertySet propertySet,
            string propertyName,
            double value)
        {
            int propertyId;

            try
            {
                propertyId = propertySet.PropertyNameToId(propertyName);
            }
            catch (Exception)
            {
                // Campo não existe nesse Pset
                return;
            }

            propertySet.SetAt(propertyId, value);
        }
    }
}
