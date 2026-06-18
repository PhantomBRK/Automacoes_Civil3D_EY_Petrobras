using Autodesk.Aec.PropertyData;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Color = Autodesk.AutoCAD.Colors.Color;
using DataType = Autodesk.Aec.PropertyData.DataType;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Label = Autodesk.Civil.DatabaseServices.Label;

namespace AutomacoesCivil3D
{
    public class IfcPrepPipeNetworkSolids
    {
        private const string LayerPipes = "IFC_PIPES_SOLID";
        private const string LayerStructs = "IFC_STRUCTURES_SOLID";
        private const string LayerMeta = "IFC_EXPORT_META";

        private const string PsetIfcIdentity = "Pset_IFC_Identity";
        private const string PsetPipe = "Pset_PipeSegment";
        private const string PsetChamber = "Pset_DistributionChamber";

        [CommandMethod("IFC_PREP_PIPENET_SOLIDS")]
        public void Run()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database db = civilDoc.Database;

            using (DocumentLock docLock = civilDoc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        ObjectId layerPipesId = EnsureLayer(db, tr, LayerPipes);
                        ObjectId layerStructsId = EnsureLayer(db, tr, LayerStructs);
                        ObjectId layerMetaId = EnsureLayer(db, tr, LayerMeta);

                        ObjectId psetIfcId = EnsurePsetIfcIdentity(db, tr);
                        ObjectId psetPipeId = EnsurePsetPipe(db, tr);
                        ObjectId psetChamberId = EnsurePsetChamber(db, tr);

                        // Se quiser idempotência: limpa as cópias antigas
                        CleanupExportSolids(db, tr, new string[] { LayerPipes, LayerStructs, LayerMeta });

                        BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        ObjectIdCollection networkIds = civilDb.GetPipeNetworkIds();
                        int createdCount = 0;
                        int skippedCount = 0;

                        foreach (ObjectId netId in networkIds)
                        {
                            Network net = (Network)tr.GetObject(netId, OpenMode.ForRead);

                            // PIPES
                            ObjectIdCollection pipeIds = net.GetPipeIds();
                            foreach (ObjectId pipeId in pipeIds)
                            {
                                Pipe pipe = (Pipe)tr.GetObject(pipeId, OpenMode.ForRead);

                                Solid3d solidClone = TryClonePartSolid(pipe, out string skipReason);
                                if (solidClone == null)
                                {
                                    skippedCount++;
                                    continue;
                                }

                                solidClone.LayerId = layerPipesId;
                                solidClone.SetDatabaseDefaults(db);

                                ObjectId solidId = modelSpace.AppendEntity(solidClone);
                                tr.AddNewlyCreatedDBObject(solidClone, true);

                                AttachAndSetText(tr, solidId, psetIfcId, "IfcEntity", "IfcPipeSegment");
                                AttachAndSetText(tr, solidId, psetIfcId, "IfcName", Safe(pipe.Name));

                                // IFC identity
                                AttachAndSetText(tr, solidId, psetIfcId, "IfcEntity", "IfcPipeSegment");
                                AttachAndSetText(tr, solidId, psetIfcId, "IfcName", Safe(pipe.Name));
                                docEditor.WriteMessage($"\nProcessando Pipe: {pipe.Name} (Handle: {pipe.Handle}, PartFamily: {pipe.PartFamilyName}, PartSize: {pipe.PartSizeName}). SkipReason: {skipReason}");
                                AttachAndSetText(tr, solidId, psetIfcId, "SourceType", "Pipe");
                                AttachAndSetText(tr, solidId, psetIfcId, "SourceHandle", pipe.Handle.ToString());
                                AttachAndSetText(tr, solidId, psetIfcId, "SourceNetwork", Safe(net.Name));
                                AttachAndSetText(tr, solidId, psetIfcId, "PartFamily", Safe(pipe.PartFamilyName));
                                AttachAndSetText(tr, solidId, psetIfcId, "PartSize", Safe(pipe.PartSizeName));

                                // Pipe Pset
                                AttachAndSetDouble(tr, solidId, psetPipeId, "InnerDiameterOrWidth", pipe.InnerDiameterOrWidth);
                                AttachAndSetDouble(tr, solidId, psetPipeId, "Length2D", pipe.Length2D);
                                AttachAndSetDouble(tr, solidId, psetPipeId, "Length3D", pipe.Length3D);
                                AttachAndSetDouble(tr, solidId, psetPipeId, "Slope", pipe.Slope);
                                AttachAndSetDouble(tr, solidId, psetPipeId, "SlopePercent", pipe.Slope * 100.0);

                                Point3d startPt = pipe.StartPoint;
                                Point3d endPt = pipe.EndPoint;

                                double radius = pipe.InnerDiameterOrWidth * 0.5;
                                double invStart = startPt.Z - radius;
                                double invEnd = endPt.Z - radius;

                                AttachAndSetDouble(tr, solidId, psetPipeId, "StartEasting", startPt.X);
                                AttachAndSetDouble(tr, solidId, psetPipeId, "StartNorthing", startPt.Y);
                                AttachAndSetDouble(tr, solidId, psetPipeId, "StartCenterZ", startPt.Z);
                                AttachAndSetDouble(tr, solidId, psetPipeId, "StartInvertLevel", invStart);

                                AttachAndSetDouble(tr, solidId, psetPipeId, "EndEasting", endPt.X);
                                AttachAndSetDouble(tr, solidId, psetPipeId, "EndNorthing", endPt.Y);
                                AttachAndSetDouble(tr, solidId, psetPipeId, "EndCenterZ", endPt.Z);
                                AttachAndSetDouble(tr, solidId, psetPipeId, "EndInvertLevel", invEnd);

                                // Estruturas conectadas
                                string startStructName = "";
                                string endStructName = "";
                                string startStructHandle = "";
                                string endStructHandle = "";

                                if (!pipe.StartStructureId.IsNull && pipe.StartStructureId.IsValid)
                                {
                                    Structure st = tr.GetObject(pipe.StartStructureId, OpenMode.ForRead, false) as Structure;
                                    if (st != null)
                                    {
                                        startStructName = Safe(st.Name);
                                        startStructHandle = st.Handle.ToString();
                                    }
                                }

                                if (!pipe.EndStructureId.IsNull && pipe.EndStructureId.IsValid)
                                {
                                    Structure st = tr.GetObject(pipe.EndStructureId, OpenMode.ForRead, false) as Structure;
                                    if (st != null)
                                    {
                                        endStructName = Safe(st.Name);
                                        endStructHandle = st.Handle.ToString();
                                    }
                                }

                                AttachAndSetText(tr, solidId, psetPipeId, "StartStructureName", startStructName);
                                AttachAndSetText(tr, solidId, psetPipeId, "EndStructureName", endStructName);
                                AttachAndSetText(tr, solidId, psetPipeId, "StartStructureHandle", startStructHandle);
                                AttachAndSetText(tr, solidId, psetPipeId, "EndStructureHandle", endStructHandle);

                                createdCount++;
                            }

                            // STRUCTURES
                            ObjectIdCollection structIds = net.GetStructureIds();
                            foreach (ObjectId stId in structIds)
                            {
                                Structure st = (Structure)tr.GetObject(stId, OpenMode.ForRead);

                                Solid3d solidClone = TryClonePartSolid(st, out string skipReason);
                                if (solidClone == null)
                                {
                                    skippedCount++;
                                    continue;
                                }

                                solidClone.LayerId = layerStructsId;
                                solidClone.SetDatabaseDefaults(db);

                                ObjectId solidId = modelSpace.AppendEntity(solidClone);
                                tr.AddNewlyCreatedDBObject(solidClone, true);

                               

                                // IFC identity
                                AttachAndSetText(tr, solidId, psetIfcId, "IfcEntity", "IfcPipeSegment");
                                AttachAndSetText(tr, solidId, psetIfcId, "IfcName", Safe(st.Name));
                             
                                AttachAndSetDouble(tr, solidId, psetChamberId, "SumpElevation", st.SumpElevation);


                                // IFC identity
                                AttachAndSetText(tr, solidId, psetIfcId, "IfcEntity", "IfcDistributionChamberElement");
                                AttachAndSetText(tr, solidId, psetIfcId, "IfcName", Safe(st.Name));
                                AttachAndSetText(tr, solidId, psetIfcId, "SourceType", "Structure");
                                AttachAndSetText(tr, solidId, psetIfcId, "SourceHandle", st.Handle.ToString());
                                AttachAndSetText(tr, solidId, psetIfcId, "SourceNetwork", Safe(net.Name));
                                AttachAndSetText(tr, solidId, psetIfcId, "PartFamily", Safe(st.PartFamilyName));
                                AttachAndSetText(tr, solidId, psetIfcId, "PartSize", Safe(st.PartSizeName));

                               // Chamber Pset (o que dá pra preencher direto do Civil)
                                Point3d loc = st.Location;

                                AttachAndSetText(tr, solidId, psetChamberId, "Name", Safe(st.Name));
                                AttachAndSetDouble(tr, solidId, psetChamberId, "Easting", loc.X);
                                AttachAndSetDouble(tr, solidId, psetChamberId, "Northing", loc.Y);
                                AttachAndSetDouble(tr, solidId, psetChamberId, "LocationZ", loc.Z);

                                AttachAndSetDouble(tr, solidId, psetChamberId, "Station", st.Station);
                                AttachAndSetDouble(tr, solidId, psetChamberId, "RimElevation", st.RimElevation);
                                
                                AttachAndSetDouble(tr, solidId, psetChamberId, "InvertLevel", st.SumpElevation);   // IFC: lowest internal
                                AttachAndSetDouble(tr, solidId, psetChamberId, "SoffitLevel", st.RimElevation);   // aproximação (topo interno)
                                //AttachAndSetDouble(tr, solidClone, psetChamberId, "RimToSumpHeight", st.RimToSumpHeight);

                                createdCount++;
                            }
                        }

                        tr.Commit();
                        docEditor.WriteMessage($"\nOK. Solids criados: {createdCount}. Ignorados (sem Solid3dBody): {skippedCount}.\n");
                    }
                    catch (System.Exception ex)
                    {
                        docEditor.WriteMessage($"\nERRO: {ex.Message}\n{ex.StackTrace}\n");
                        tr.Abort();
                    }
                }
            }
        }

        // -----------------------------
        // SOLID EXTRACTION
        // -----------------------------
        private static Solid3d TryClonePartSolid(Part part, out string reason)
        {
            reason = "";
            try
            {
                Solid3d body = part.Solid3dBody;
                if (body == null)
                {
                    reason = "Solid3dBody null";
                    return null;
                }

                DBObject cloned = (DBObject)body.Clone();
                Solid3d solidClone = cloned as Solid3d;
                if (solidClone == null)
                {
                    reason = "Clone is not Solid3d";
                    return null;
                }

                return solidClone;
            }
            catch (System.Exception ex)
            {
                reason = ex.Message;
                return null;
            }
        }

        // -----------------------------
        // PSET CREATION
        // -----------------------------
        private sealed class PsetProp
        {
            public string Name;
            public DataType Type;
            public string Description;

            public PsetProp(string name, DataType type, string description)
            {
                Name = name;
                Type = type;
                Description = description;
            }
        }

        private static ObjectId EnsurePsetIfcIdentity(Database db, Transaction tr)
        {
            List<PsetProp> props = new List<PsetProp>()
            {
                new PsetProp("IfcEntity", DataType.Text, "Nome da entidade IFC (ex: IfcPipeSegment)"),
                new PsetProp("IfcName", DataType.Text, "Nome do objeto no IFC"),
                new PsetProp("SourceType", DataType.Text, "Pipe/Structure"),
                new PsetProp("SourceHandle", DataType.Text, "Handle do objeto Civil 3D de origem"),
                new PsetProp("SourceNetwork", DataType.Text, "Nome da PipeNetwork"),
                new PsetProp("PartFamily", DataType.Text, "PartFamilyName"),
                new PsetProp("PartSize", DataType.Text, "PartSizeName"),
            };

            return EnsurePropertySetDefinition(db, tr, PsetIfcIdentity, "Metadados para exportação IFC", props, "AcDb3dSolid");
        }

        private static ObjectId EnsurePsetPipe(Database db, Transaction tr)
        {
            List<PsetProp> props = new List<PsetProp>()
            {
                new PsetProp("InnerDiameterOrWidth", DataType.Real, "Diâmetro interno ou largura interna"),
                new PsetProp("Length2D", DataType.Real, "Comprimento 2D"),
                new PsetProp("Length3D", DataType.Real, "Comprimento 3D"),
                new PsetProp("Slope", DataType.Real, "Declividade (decimal)"),
                new PsetProp("SlopePercent", DataType.Real, "Declividade (%)"),

                new PsetProp("StartEasting", DataType.Real, "Este inicial"),
                new PsetProp("StartNorthing", DataType.Real, "Norte inicial"),
                new PsetProp("StartCenterZ", DataType.Real, "Z do centro inicial"),
                new PsetProp("StartInvertLevel", DataType.Real, "Cota da geratriz inferior inicial (aprox.)"),

                new PsetProp("EndEasting", DataType.Real, "Este final"),
                new PsetProp("EndNorthing", DataType.Real, "Norte final"),
                new PsetProp("EndCenterZ", DataType.Real, "Z do centro final"),
                new PsetProp("EndInvertLevel", DataType.Real, "Cota da geratriz inferior final (aprox.)"),

                new PsetProp("StartStructureName", DataType.Text, "Nome estrutura inicial"),
                new PsetProp("EndStructureName", DataType.Text, "Nome estrutura final"),
                new PsetProp("StartStructureHandle", DataType.Text, "Handle estrutura inicial"),
                new PsetProp("EndStructureHandle", DataType.Text, "Handle estrutura final"),
            };

            return EnsurePropertySetDefinition(db, tr, PsetPipe, "Propriedades para IfcPipeSegment", props, "AcDb3dSolid");
        }

        private static ObjectId EnsurePsetChamber(Database db, Transaction tr)
        {
            List<PsetProp> props = new List<PsetProp>()
            {
                new PsetProp("Name", DataType.Text, "Nome do dispositivo"),
                new PsetProp("Easting", DataType.Real, "Este"),
                new PsetProp("Northing", DataType.Real, "Norte"),
                new PsetProp("LocationZ", DataType.Real, "Z do ponto de inserção"),
                new PsetProp("Station", DataType.Real, "Estaca/Station"),
                new PsetProp("RimElevation", DataType.Real, "Topo (RimElevation)"),
                new PsetProp("SumpElevation", DataType.Real, "Fundo (SumpElevation)"),
                new PsetProp("RimToSumpHeight", DataType.Real, "Altura (RimToSumpHeight)"),

                // IFC-inspired
                new PsetProp("InvertLevel", DataType.Real, "IFC: nível do ponto mais baixo interno (aqui = SumpElevation)"),
                new PsetProp("SoffitLevel", DataType.Real, "IFC: nível do ponto mais alto interno (aqui ~ RimElevation)"),
            };

            return EnsurePropertySetDefinition(db, tr, PsetChamber, "Propriedades para IfcDistributionChamberElement", props, "AcDb3dSolid");
        }

        private static ObjectId EnsurePropertySetDefinition(
            Database db,
            Transaction tr,
            string psetName,
            string psetDescription,
            List<PsetProp> props,
            string appliesToDxfName)
        {
            DictionaryPropertySetDefinitions dict = new DictionaryPropertySetDefinitions(db);

            ObjectId psetId = ObjectId.Null;

            if (dict.Has(psetName, tr))
            {
                psetId = dict.GetAt(psetName);
            }
            else
            {
                PropertySetDefinition psetDef = new PropertySetDefinition();
                psetDef.SetToStandard(db);
                psetDef.SubSetDatabaseDefaults(db);
                psetDef.Description = psetDescription;

                StringCollection appliesTo = new StringCollection();
                appliesTo.Add(appliesToDxfName);
                psetDef.SetAppliesToFilter(appliesTo, false);

                dict.AddNewRecord(psetName, psetDef);
                psetId = dict.GetAt(psetName);
            }

            // garante que props existem
            PropertySetDefinition existing = (PropertySetDefinition)tr.GetObject(psetId, OpenMode.ForWrite);

            HashSet<string> existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PropertyDefinition d in existing.Definitions)
            {
                if (!string.IsNullOrWhiteSpace(d.Name))
                {
                    existingNames.Add(d.Name);
                }
            }

            foreach (PsetProp p in props)
            {
                if (existingNames.Contains(p.Name))
                {
                    continue;
                }

                PropertyDefinition pd = new PropertyDefinition();
                pd.SetToStandard(db);
                pd.SubSetDatabaseDefaults(db);
                pd.Name = p.Name;
                pd.Description = p.Description;
                pd.DataType = p.Type;

                existing.Definitions.Add(pd);
            }

            // garante aplica-to
            StringCollection appliesToFinal = new StringCollection();
            appliesToFinal.Add(appliesToDxfName);
            existing.SetAppliesToFilter(appliesToFinal, false);

            return psetId;
        }


        // -----------------------------
        // PSET ATTACH + SET (CORRIGIDO)
        // -----------------------------
        private static void AttachAndSetText(Transaction tr, ObjectId hostEntId, ObjectId psetDefId, string propName, string value)
        {
            EnsureAttached(tr, hostEntId, psetDefId);
            SetPsetValue(tr, hostEntId, psetDefId, propName, value ?? "");
        }

        private static void AttachAndSetDouble(Transaction tr, ObjectId hostEntId, ObjectId psetDefId, string propName, double value)
        {
            EnsureAttached(tr, hostEntId, psetDefId);
            SetPsetValue(tr, hostEntId, psetDefId, propName, value);
        }

        private static void EnsureAttached(Transaction tr, ObjectId hostEntId, ObjectId psetDefId)
        {
             var hostEnt = tr.GetObject(hostEntId, OpenMode.ForWrite);
            // Evita anexar duplicado
            ObjectIdCollection psetIds = PropertyDataServices.GetPropertySets(hostEnt);
            foreach (ObjectId psId in psetIds)
            {
                PropertySet ps = (PropertySet)tr.GetObject(psId, OpenMode.ForRead);
                ObjectId defId = ps.PropertySetDefinition; // <<< O QUE IMPORTA
                if (defId == psetDefId)
                {
                    return; // já anexado
                }
            }

            
            PropertyDataServices.AddPropertySet(hostEnt, psetDefId);
        }

        private static void SetPsetValue(Transaction tr, ObjectId hostEntId, ObjectId psetDefId, string propName, object value)
        {

            var hostEnt = tr.GetObject(hostEntId, OpenMode.ForWrite);
            ObjectIdCollection psetIds = PropertyDataServices.GetPropertySets(hostEnt);
            foreach (ObjectId psId in psetIds)
            {
                PropertySet ps = (PropertySet)tr.GetObject(psId, OpenMode.ForWrite);

                // psId é a instância. A definição é ps.PropertySetDefinition.
                if (ps.PropertySetDefinition != psetDefId)
                {
                    continue;
                }

                int pid = ps.PropertyNameToId(propName);
                if (pid < 0)
                {
                    return;
                }

                ps.SetAt(pid, value);
                return;
            }
        }


        // -----------------------------
        // LAYER + CLEANUP
        // -----------------------------
        private static ObjectId EnsureLayer(Database db, Transaction tr, string layerName)
        {
            LayerTable layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (layerTable.Has(layerName))
            {
                return layerTable[layerName];
            }

            layerTable.UpgradeOpen();

            LayerTableRecord layer = new LayerTableRecord();
            layer.Name = layerName;

            ObjectId layerId = layerTable.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);

            return layerId;
        }

        private static void CleanupExportSolids(Database db, Transaction tr, string[] layerNames)
        {
            HashSet<string> layers = new HashSet<string>(layerNames, StringComparer.OrdinalIgnoreCase);

            BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in modelSpace)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null)
                {
                    continue;
                }

                if (!layers.Contains(ent.Layer))
                {
                    continue;
                }

                ent.UpgradeOpen();
                ent.Erase();
            }
        }

        private static string Safe(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? "" : s.Trim();
        }
    }
}
