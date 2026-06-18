using System;
using System.Collections.Generic;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Label = Autodesk.Civil.DatabaseServices.Label;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace AutomacoesCivil3D
{
    public class CorridorsCreateSurfaces
    {
        private struct CorridorSurfaceRef
        {
            public ObjectId CorridorId;
            public string SurfaceName;

            public CorridorSurfaceRef(ObjectId corridorId, string surfaceName)
            {
                CorridorId = corridorId;
                SurfaceName = surfaceName;
            }
        }

        [CommandMethod("CRIA_SUP_CORRIDORES")]
        public void CriarSuperficiesCorridores()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            CivilDocument civilDb = Manager.DocCivil;
            Database db = civilDoc.Database;

            string[] linkCodes =
            {
                "Datum",
                "OFFSET_ATERRO",
                "OFFSET_CORTE",
                "Rip Rap",
                "Ditch",
                "Daylight",
                "Pista Existente",
            };

            int totalCorridors = 0;
            List<CorridorSurfaceRef> corridorSurfaceRefs = new List<CorridorSurfaceRef>();

            // --------------------------
            // 1) Cria/atualiza superfícies de corredor
            // --------------------------
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId surfStyleId = TryGetSurfaceStyleId(civilDb, "TRI_PTO_BRD");

                foreach (ObjectId corId in civilDb.CorridorCollection)
                {
                    Corridor corridor = (Corridor)tr.GetObject(corId, OpenMode.ForWrite);

                    string baseName = corridor.Name;

                    string surfaceNameUsed = string.Empty;
                    CorridorSurface corridorSurface =
                        GetOrCreateCorridorSurface(corridor, baseName, docEditor, out surfaceNameUsed);

                    if (corridorSurface == null)
                    {
                        continue;
                    }

                    // overhang: Bottom Links
                    try
                    {
                        corridorSurface.OverhangCorrection = OverhangCorrectionType.BottomLinks;
                    }
                    catch
                    {
                    }

                    // link codes como breakline
                    foreach (string code in linkCodes)
                    {
                        try
                        {
                            corridorSurface.AddLinkCode(code, true);
                        }
                        catch
                        {
                            // já existe ou inválido → ignora
                        }
                    }

                    // boundary de extents do corredor
                    try
                    {
                        CorridorSurfaceBoundaryCollection boundaries = corridorSurface.Boundaries;

                        string[] bNames = boundaries.BoundaryNames();
                        bool hasExtents = false;

                        for (int i = 0; i < bNames.Length; i++)
                        {
                            if (string.Equals(bNames[i], "CORRIDOR_EXTENTS", StringComparison.OrdinalIgnoreCase))
                            {
                                hasExtents = true;
                                break;
                            }
                        }

                        if (!hasExtents)
                        {
                            boundaries.AddCorridorExtentsBoundary("CORRIDOR_EXTENTS");
                        }
                    }
                    catch
                    {
                    }

                    // força rebuild do corredor (ajuda a materializar SurfaceId mais cedo)
                    try
                    {
                        corridor.Rebuild();
                    }
                    catch
                    {
                    }

                    corridorSurfaceRefs.Add(new CorridorSurfaceRef(corId, surfaceNameUsed));
                    totalCorridors++;
                }

                tr.Commit();
            }

            Application.DocumentManager.MdiActiveDocument.Editor.Regen();

            // --------------------------
            // 2) Monta TRP - FINAL em outra transação (SurfaceId já existe)
            // --------------------------
            using (Transaction tr2 = db.TransactionManager.StartTransaction())
            {
                ObjectId surfStyleId = TryGetSurfaceStyleId(civilDb, "TRI_PTO_BRD");

                List<ObjectId> corridorTinSurfaceIds = new List<ObjectId>();

                for (int i = 0; i < corridorSurfaceRefs.Count; i++)
                {
                    ObjectId surfaceId = GetCorridorTinSurfaceId(
                        tr2,
                        civilDb,
                        corridorSurfaceRefs[i].CorridorId,
                        corridorSurfaceRefs[i].SurfaceName);

                    if (!surfaceId.IsNull && surfaceId.IsValid)
                    {
                        // aplica estilo agora (agora o TinSurface existe de verdade)
                        try
                        {
                            Autodesk.Civil.DatabaseServices.Surface srf =
                                (Autodesk.Civil.DatabaseServices.Surface)tr2.GetObject(surfaceId, OpenMode.ForWrite);

                            if (!surfStyleId.IsNull)
                            {
                                srf.StyleId = surfStyleId;
                            }
                        }
                        catch
                        {
                        }

                        if (!corridorTinSurfaceIds.Contains(surfaceId))
                        {
                            corridorTinSurfaceIds.Add(surfaceId);
                        }
                    }
                }

                CreateOrUpdateTrpFinalSurface(tr2, db, civilDb, docEditor, corridorTinSurfaceIds);

                tr2.Commit();
            }

            docEditor.WriteMessage(
                $"\nSuperfícies de corredor criadas/atualizadas em {totalCorridors} corredor(es).");
            docEditor.WriteMessage(
                "\nSuperfície 'TRP - FINAL' atualizada (TN + superfícies de corredor).");

            Application.DocumentManager.MdiActiveDocument.Editor.Regen();
        }

        // ==========================
        // MÉTODO EXTRAÍDO: TRP - FINAL
        // ==========================
        private static void CreateOrUpdateTrpFinalSurface(
            Transaction tr,
            Database db,
            CivilDocument civilDb,
            Editor ed,
            List<ObjectId> corridorSurfaceIds)
        {
            ObjectId tnSurfaceId = ObjectId.Null;
            ObjectId trpFinalIdExistente = ObjectId.Null;

            ObjectIdCollection allSurfIds = civilDb.GetSurfaceIds();

            foreach (ObjectId sid in allSurfIds)
            {
                Autodesk.Civil.DatabaseServices.Surface srfBase =
                    (Autodesk.Civil.DatabaseServices.Surface)tr.GetObject(sid, OpenMode.ForRead);

                if (srfBase.Name.Contains("TN", StringComparison.OrdinalIgnoreCase))
                {
                    tnSurfaceId = sid;

                    // opcional: estilo da TN
                    ObjectId tnStyleId = TryGetSurfaceStyleId(civilDb, "INVISIVEL");
                    if (!tnStyleId.IsNull)
                    {
                        try
                        {
                            TinSurface tn = (TinSurface)tr.GetObject(sid, OpenMode.ForWrite);
                            tn.StyleId = tnStyleId;
                        }
                        catch
                        {
                        }
                    }
                }

                if (string.Equals(srfBase.Name, "TRP - FINAL", StringComparison.OrdinalIgnoreCase))
                {
                    trpFinalIdExistente = sid;
                }
            }

            // se já existir TRP - FINAL, apaga para recriar
            if (!trpFinalIdExistente.IsNull)
            {
                try
                {
                    Autodesk.Civil.DatabaseServices.Surface srfDel =
                        (Autodesk.Civil.DatabaseServices.Surface)tr.GetObject(trpFinalIdExistente, OpenMode.ForWrite);
                    srfDel.Erase();
                }
                catch
                {
                }
            }

            if (tnSurfaceId.IsNull && corridorSurfaceIds.Count == 0)
            {
                ed.WriteMessage("\nNenhuma superfície 'TN' ou superfície de corredor encontrada para compor 'TRP - FINAL'.");
                return;
            }

            // criar TRP - FINAL
            ObjectId trpFinalId = TinSurface.Create(db, "TRP - FINAL");
            TinSurface trpFinal = (TinSurface)tr.GetObject(trpFinalId, OpenMode.ForWrite);

            // estilo da TRP - FINAL
            ObjectId finalStyleId = TryGetSurfaceStyleId(civilDb, "INVISIVEL");
            if (!finalStyleId.IsNull)
            {
                try
                {
                    trpFinal.StyleId = finalStyleId;
                }
                catch
                {
                }
            }

            // 1) colar TN primeiro
            if (!tnSurfaceId.IsNull)
            {
                try
                {
                    trpFinal.PasteSurface(tnSurfaceId);
                }
                catch
                {
                }
            }

            // 2) colar superfícies de corredor
            for (int i = 0; i < corridorSurfaceIds.Count; i++)
            {
                try
                {
                    trpFinal.PasteSurface(corridorSurfaceIds[i]);
                }
                catch
                {
                }
            }

            try
            {
                trpFinal.Rebuild();
            }
            catch
            {
            }
        }

        // Pega SurfaceId do TinSurface gerado pela CorridorSurface (com fallback por nome)
        private static ObjectId GetCorridorTinSurfaceId(
            Transaction tr,
            CivilDocument civilDb,
            ObjectId corridorId,
            string corridorSurfaceName)
        {
            try
            {
                Corridor corridor = (Corridor)tr.GetObject(corridorId, OpenMode.ForRead);
                CorridorSurfaceCollection coll = corridor.CorridorSurfaces;

                CorridorSurface cs = coll[corridorSurfaceName];
                ObjectId sid = cs.SurfaceId;

                if (!sid.IsNull && sid.IsValid)
                {
                    return sid;
                }
            }
            catch
            {
            }

            // fallback: procurar nas superfícies do desenho por nome exato
            try
            {
                ObjectIdCollection allSurfIds = civilDb.GetSurfaceIds();
                foreach (ObjectId sid in allSurfIds)
                {
                    Autodesk.Civil.DatabaseServices.Surface srf =
                        (Autodesk.Civil.DatabaseServices.Surface)tr.GetObject(sid, OpenMode.ForRead);

                    if (string.Equals(srf.Name, corridorSurfaceName, StringComparison.OrdinalIgnoreCase))
                    {
                        return sid;
                    }
                }
            }
            catch
            {
            }

            return ObjectId.Null;
        }

        private static ObjectId TryGetSurfaceStyleId(CivilDocument civilDb, string preferredName)
        {
            ObjectId styleId = ObjectId.Null;

            try
            {
                SurfaceStyleCollection styles = civilDb.Styles.SurfaceStyles;

                try
                {
                    styleId = styles[preferredName];
                }
                catch
                {
                    foreach (ObjectId idStyle in styles)
                    {
                        styleId = idStyle;
                        break;
                    }
                }
            }
            catch
            {
                styleId = ObjectId.Null;
            }

            return styleId;
        }

        // Tenta obter a superfície de corredor; se não existir, cria com nome único (baseName, baseName_01, _02...)
        private static CorridorSurface GetOrCreateCorridorSurface(
            Corridor corridor,
            string baseName,
            Editor ed,
            out string surfaceNameUsed)
        {
            CorridorSurfaceCollection coll = corridor.CorridorSurfaces;
            CorridorSurface surf = null;

            surfaceNameUsed = baseName;

            // tentar pegar existente
            try
            {
                surf = coll[baseName];
                if (surf != null)
                {
                    surfaceNameUsed = baseName;
                    return surf;
                }
            }
            catch
            {
                // não existe -> criar
            }

            // criar com nome único
            string nameTry = baseName;

            for (int i = 0; i < 100; i++)
            {
                try
                {
                    surf = coll.Add(nameTry);
                    if (surf != null)
                    {
                        surfaceNameUsed = nameTry;

                        if (!nameTry.Equals(baseName, StringComparison.OrdinalIgnoreCase))
                        {
                            ed.WriteMessage(
                                $"\nSuperfície de corredor '{baseName}' já existia. Criada '{nameTry}'.");
                        }

                        return surf;
                    }
                }
                catch
                {
                    nameTry = $"{baseName}_{(i + 1).ToString("00")}";
                }
            }

            ed.WriteMessage($"\nFalha ao criar superfície de corredor para '{baseName}'.");
            surfaceNameUsed = string.Empty;
            return null;
        }
    }
}
