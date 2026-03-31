using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using System;
using System.Collections.Generic;
using System.Linq;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutomacoesCivil3D
{
    public sealed class QtoCorridorLinkSurfaces
    {
        private sealed class CorridorLinkSurfaceDefinition
        {
            public CorridorLinkSurfaceDefinition(string surfaceSuffix, params string[] linkCodeAliases)
            {
                SurfaceSuffix = surfaceSuffix;
                LinkCodeAliases = linkCodeAliases ?? Array.Empty<string>();
            }

            public string SurfaceSuffix { get; }
            public IReadOnlyList<string> LinkCodeAliases { get; }

            public bool Matches(string linkCode)
            {
                return LinkCodeAliases.Any(alias =>
                    string.Equals(alias, linkCode, StringComparison.OrdinalIgnoreCase));
            }
        }

        private static readonly IReadOnlyList<CorridorLinkSurfaceDefinition> DefaultDefinitions =
            new List<CorridorLinkSurfaceDefinition>
            {
                new CorridorLinkSurfaceDefinition("TOP", "TOP", "Top"),
                new CorridorLinkSurfaceDefinition("PAVE", "PAVE", "Pave"),
                new CorridorLinkSurfaceDefinition("PAVE1", "PAVE1", "Pave1", "PAVE_1", "Pave_1"),
                new CorridorLinkSurfaceDefinition("BASE", "BASE", "Base"),
                new CorridorLinkSurfaceDefinition("SUBBASE", "SUBBASE", "Subbase", "SUB-BASE", "SUB_BASE"),
                new CorridorLinkSurfaceDefinition("SUBLEITO", "SUBLEITO", "Subleito", "SUB-LEITO", "SUB_LEITO"),
                new CorridorLinkSurfaceDefinition("DATUM", "DATUM", "Datum")
            };

        [CommandMethod("CRIA_SUP_QTO_CORREDORES")]
        public void CreateQtoCorridorSurfaces()
        {
            Document doc = Manager.DocCad;
            Editor editor = Manager.DocEditor;
            CivilDocument civilDoc = Manager.DocCivil;
            Database db = Manager.DocData;

            int processedCorridors = 0;
            int updatedCorridors = 0;
            int createdOrUpdatedSurfaces = 0;
            List<string> warnings = new List<string>();

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId surfaceStyleId = TryGetSurfaceStyleId(civilDoc, "TRI_PTO_BRD");

                foreach (ObjectId corridorId in civilDoc.CorridorCollection)
                {
                    processedCorridors++;

                    Corridor corridor = tr.GetObject(corridorId, OpenMode.ForWrite) as Corridor;
                    if (corridor == null)
                    {
                        continue;
                    }

                    string[] actualLinkCodes = TryGetLinkCodes(corridor);
                    if (actualLinkCodes.Length == 0)
                    {
                        warnings.Add($"Corredor '{corridor.Name}' sem link codes legiveis.");
                        continue;
                    }

                    int corridorSurfaceCount = 0;

                    foreach (CorridorLinkSurfaceDefinition definition in DefaultDefinitions)
                    {
                        List<string> matchedCodes = actualLinkCodes
                            .Where(definition.Matches)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        if (matchedCodes.Count == 0)
                        {
                            continue;
                        }

                        string baseSurfaceName = $"{corridor.Name}-{definition.SurfaceSuffix}";
                        CorridorSurface? corridorSurface =
                            GetOrCreateCorridorSurface(corridor, baseSurfaceName, editor, out string surfaceNameUsed);

                        if (corridorSurface == null)
                        {
                            warnings.Add($"Falha ao criar a superficie '{baseSurfaceName}'.");
                            continue;
                        }

                        try
                        {
                            corridorSurface.OverhangCorrection = OverhangCorrectionType.BottomLinks;
                        }
                        catch
                        {
                        }

                        foreach (string matchedCode in matchedCodes)
                        {
                            try
                            {
                                corridorSurface.AddLinkCode(matchedCode, true);
                            }
                            catch
                            {
                            }
                        }

                        EnsureCorridorExtentsBoundary(corridorSurface);

                        try
                        {
                            ObjectId surfaceId = corridorSurface.SurfaceId;
                            if (!surfaceId.IsNull && !surfaceStyleId.IsNull)
                            {
                                Autodesk.Civil.DatabaseServices.Surface? surface =
                                    tr.GetObject(surfaceId, OpenMode.ForWrite, false) as Autodesk.Civil.DatabaseServices.Surface;

                                if (surface != null)
                                {
                                    surface.StyleId = surfaceStyleId;
                                }
                            }
                        }
                        catch
                        {
                        }

                        corridorSurfaceCount++;
                        createdOrUpdatedSurfaces++;
                    }

                    if (corridorSurfaceCount == 0)
                    {
                        warnings.Add($"Corredor '{corridor.Name}' nao possui Top/Pave/Pave1/Base/Subbase/Subleito/Datum.");
                        continue;
                    }

                    try
                    {
                        corridor.Rebuild();
                    }
                    catch (System.Exception ex)
                    {
                        warnings.Add($"Rebuild do corredor '{corridor.Name}' falhou: {ex.Message}");
                    }

                    updatedCorridors++;
                }

                tr.Commit();
            }

            string summary =
                "Superficies QTO de corredor concluidas.\n" +
                $"Corredores lidos: {processedCorridors}\n" +
                $"Corredores atualizados: {updatedCorridors}\n" +
                $"Superficies criadas/atualizadas: {createdOrUpdatedSurfaces}";

            if (warnings.Count > 0)
            {
                summary += "\n\nAvisos:\n- " + string.Join("\n- ", warnings.Take(6));
            }

            editor.WriteMessage("\n" + summary.Replace("\n", "\n"));
            AcadApp.ShowAlertDialog(summary);
        }

        private static string[] TryGetLinkCodes(Corridor corridor)
        {
            try
            {
                return corridor.GetLinkCodes() ?? Array.Empty<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static void EnsureCorridorExtentsBoundary(CorridorSurface corridorSurface)
        {
            try
            {
                CorridorSurfaceBoundaryCollection boundaries = corridorSurface.Boundaries;
                string[] boundaryNames = boundaries.BoundaryNames();
                bool hasExtents = boundaryNames.Any(name =>
                    string.Equals(name, "CORRIDOR_EXTENTS", StringComparison.OrdinalIgnoreCase));

                if (!hasExtents)
                {
                    boundaries.AddCorridorExtentsBoundary("CORRIDOR_EXTENTS");
                }
            }
            catch
            {
            }
        }

        private static ObjectId TryGetSurfaceStyleId(CivilDocument civilDoc, string preferredName)
        {
            try
            {
                SurfaceStyleCollection styles = civilDoc.Styles.SurfaceStyles;

                try
                {
                    return styles[preferredName];
                }
                catch
                {
                    foreach (ObjectId styleId in styles)
                    {
                        return styleId;
                    }
                }
            }
            catch
            {
            }

            return ObjectId.Null;
        }

        private static CorridorSurface? GetOrCreateCorridorSurface(
            Corridor corridor,
            string baseName,
            Editor editor,
            out string surfaceNameUsed)
        {
            CorridorSurfaceCollection collection = corridor.CorridorSurfaces;
            surfaceNameUsed = baseName;

            try
            {
                CorridorSurface existing = collection[baseName];
                if (existing != null)
                {
                    return existing;
                }
            }
            catch
            {
            }

            string attemptName = baseName;
            for (int index = 0; index < 100; index++)
            {
                try
                {
                    CorridorSurface created = collection.Add(attemptName);
                    if (created != null)
                    {
                        surfaceNameUsed = attemptName;
                        if (!string.Equals(attemptName, baseName, StringComparison.OrdinalIgnoreCase))
                        {
                            editor.WriteMessage($"\nSuperficie '{baseName}' ja existia. Foi usada '{attemptName}'.");
                        }

                        return created;
                    }
                }
                catch
                {
                    attemptName = $"{baseName}_{(index + 1).ToString("00")}";
                }
            }

            surfaceNameUsed = string.Empty;
            return null;
        }
    }
}
