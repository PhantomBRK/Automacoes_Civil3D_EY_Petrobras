using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Color = Autodesk.AutoCAD.Colors.Color;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Label = Autodesk.Civil.DatabaseServices.Label;

namespace AutomacoesCivil3D
{
    public class PolylinesPorZonaExcel
    {
        [CommandMethod("ZONAS_POLY_XLS")]
        public void CriarPolylinesPorZona()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;

            PromptOpenFileOptions promptFile = new PromptOpenFileOptions("\nSelecione a planilha XLSX com colunas ZONA, X, Y:");
            promptFile.Filter = "Excel (*.xlsx)|*.xlsx";
            PromptFileNameResult fileRes = docEditor.GetFileNameForOpen(promptFile);
            if (fileRes.Status != PromptStatus.OK)
            {
                return;
            }

            String xlsxPath = fileRes.StringResult;
            if (!File.Exists(xlsxPath))
            {
                docEditor.WriteMessage($"\nArquivo não encontrado: {xlsxPath}");
                return;
            }

            Dictionary<string, List<Point2d>> zonas = new Dictionary<string, List<Point2d>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                ExcelPackage.License.SetNonCommercialPersonal("Gleison Bruno da Costa");

                FileInfo fileInfo = new FileInfo(xlsxPath);
                using (ExcelPackage package = new ExcelPackage(fileInfo))
                {
                    ExcelWorksheet ws = package.Workbook.Worksheets["vertices"];
                    if (ws == null && package.Workbook.Worksheets.Count > 0)
                    {
                        ws = package.Workbook.Worksheets[0];
                    }

                    if (ws == null || ws.Dimension == null)
                    {
                        docEditor.WriteMessage("\nPlanilha vazia ou inválida.");
                        return;
                    }

                    Int32 headerRow = FindHeaderRow(ws);
                    if (headerRow < 1)
                    {
                        docEditor.WriteMessage("\nNão achei o cabeçalho. Precisa ter colunas: ZONA, X, Y.");
                        return;
                    }

                    Int32 colZona = FindColumn(ws, headerRow, "ZONA");
                    Int32 colX = FindColumn(ws, headerRow, "X");
                    Int32 colY = FindColumn(ws, headerRow, "Y");

                    if (colZona < 1 || colX < 1 || colY < 1)
                    {
                        docEditor.WriteMessage("\nCabeçalho encontrado, mas faltou coluna ZONA/X/Y.");
                        return;
                    }

                    Int32 lastRow = ws.Dimension.End.Row;

                    for (Int32 r = headerRow + 1; r <= lastRow; r++)
                    {
                        Object zonaObj = ws.Cells[r, colZona].Value;
                        Object xObj = ws.Cells[r, colX].Value;
                        Object yObj = ws.Cells[r, colY].Value;

                        if (zonaObj == null || xObj == null || yObj == null)
                        {
                            continue;
                        }

                        String zona = Convert.ToString(zonaObj)?.Trim();
                        if (String.IsNullOrWhiteSpace(zona))
                        {
                            continue;
                        }

                        Double x = ToDouble(xObj);
                        Double y = ToDouble(yObj);

                        if (!zonas.ContainsKey(zona))
                        {
                            zonas.Add(zona, new List<Point2d>());
                        }

                        zonas[zona].Add(new Point2d(x, y));
                    }
                }
            }
            catch (System.Exception ex)
            {
                docEditor.WriteMessage($"\nErro lendo XLSX: {ex.Message}");
                return;
            }

            if (zonas.Count == 0)
            {
                docEditor.WriteMessage("\nNenhum dado encontrado (ZONA/X/Y).");
                return;
            }

            Database db = civilDoc.Database;

            using (DocumentLock docLock = civilDoc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                Int32 criadas = 0;

                foreach (KeyValuePair<string, List<Point2d>> kv in zonas)
                {
                    String zona = kv.Key;
                    List<Point2d> pts = kv.Value;

                    if (pts == null || pts.Count < 2)
                    {
                        continue;
                    }

                    EnsureLayer(db, tr, zona);

                    Polyline pl = new Polyline();
                    pl.SetDatabaseDefaults();
                    pl.Layer = zona;
                    pl.Elevation = 0.0;

                    for (Int32 i = 0; i < pts.Count; i++)
                    {
                        Point2d p2 = pts[i];
                        pl.AddVertexAt(i, p2, 0.0, 0.0, 0.0);
                    }

                    // Fecha sempre (polígono por zona)
                    pl.Closed = true;

                    ms.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);

                    criadas++;
                }

                tr.Commit();
                docEditor.WriteMessage($"\nOK. Polylines criadas: {criadas}. Layers criadas/usadas: {zonas.Count}.");
            }
        }

        private static void EnsureLayer(Database db, Transaction tr, String layerName)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName))
            {
                return;
            }

            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord();
            ltr.Name = layerName;
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        private static Int32 FindHeaderRow(ExcelWorksheet ws)
        {
            Int32 lastRow = ws.Dimension.End.Row;
            Int32 lastCol = ws.Dimension.End.Column;

            for (Int32 r = 1; r <= Math.Min(lastRow, 50); r++)
            {
                Boolean hasZona = false;
                Boolean hasX = false;
                Boolean hasY = false;

                for (Int32 c = 1; c <= Math.Min(lastCol, 20); c++)
                {
                    String v = Convert.ToString(ws.Cells[r, c].Value)?.Trim();
                    if (String.IsNullOrWhiteSpace(v)) continue;

                    if (v.Equals("ZONA", StringComparison.OrdinalIgnoreCase)) hasZona = true;
                    if (v.Equals("X", StringComparison.OrdinalIgnoreCase)) hasX = true;
                    if (v.Equals("Y", StringComparison.OrdinalIgnoreCase)) hasY = true;
                }

                if (hasZona && hasX && hasY)
                {
                    return r;
                }
            }

            return -1;
        }

        private static Int32 FindColumn(ExcelWorksheet ws, Int32 headerRow, String header)
        {
            Int32 lastCol = ws.Dimension.End.Column;

            for (Int32 c = 1; c <= lastCol; c++)
            {
                String v = Convert.ToString(ws.Cells[headerRow, c].Value)?.Trim();
                if (String.IsNullOrWhiteSpace(v)) continue;

                if (v.Equals(header, StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }

            return -1;
        }

        private static Double ToDouble(Object value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            if (value is Double d) return d;
            if (value is Single f) return f;
            if (value is Int32 i) return i;
            if (value is Int64 l) return l;
            if (value is Decimal m) return (Double)m;

            String s = value.ToString();
            if (Double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out Double v)) return v;
            if (Double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out v)) return v;

            throw new FormatException($"Valor numérico inválido: {s}");
        }
    }
}
