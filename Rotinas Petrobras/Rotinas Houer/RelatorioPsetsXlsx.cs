// Gera XLSX listando TODOS os 3DSOLID e BODY do desenho (Model + blocos)
// e cria colunas dinamicas com TODOS os parametros dos PropertySets (PSET.Prop).
//
// Requisito: Microsoft Excel instalado (Interop).
//
// Comando: RELATORIO_PSET_XLSX

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using Autodesk.Aec.PropertyData.DatabaseServices;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Excel = Microsoft.Office.Interop.Excel;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutomacoesCivil3D
{
    public class RelatorioPsetsXlsx
    {
        [CommandMethod("RELATORIO_PSET_XLSX")]
        public void GerarRelatorioPsetsXlsx()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            Database docData = Manager.DocData;

            string xlsxPath = AskSaveXlsxPath();
            if (string.IsNullOrWhiteSpace(xlsxPath))
            {
                docEditor.WriteMessage("\nOperação cancelada.");
                return;
            }

            using (DocumentLock docLock = civilDoc.LockDocument())
            {
                using (Transaction tr = docData.TransactionManager.StartTransaction())
                {
                    List<Entity> entities = CollectAllSolidsAndBodies(docData, tr);

                    if (entities.Count == 0)
                    {
                        docEditor.WriteMessage("\nNenhum 3DSOLID/BODY encontrado.");
                        return;
                    }

                    // 1) Ler tudo e montar dataset (linhas + colunas dinâmicas)
                    List<ReportRow> rows = new List<ReportRow>(entities.Count);
                    HashSet<string> dynamicColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (Entity ent in entities)
                    {
                        ReportRow row = BuildRowFromEntity(docData, tr, ent, dynamicColumns);
                        rows.Add(row);
                    }

                    // 2) Montar colunas (fixas + dinâmicas)
                    List<string> columns = new List<string>();
                    columns.Add("EntityType");
                    columns.Add("Handle");
                    columns.Add("Layer");
                    columns.Add("OwnerBTR");

                    List<string> dyn = dynamicColumns
                        .OrderBy((string s) => s, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    columns.AddRange(dyn);

                    tr.Commit();

                    // 3) Exportar XLSX
                    WriteXlsx(xlsxPath, columns, rows);

                    docEditor.WriteMessage($"\nXLSX gerado: {xlsxPath}");
                }
            }
        }

        // -----------------------------
        // Coleta: ModelSpace + blocos
        // -----------------------------
        private static List<Entity> CollectAllSolidsAndBodies(Database db, Transaction tr)
        {
            List<Entity> list = new List<Entity>();

            BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

            foreach (ObjectId btrId in blockTable)
            {
                BlockTableRecord btr = null;

                try
                {
                    btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                }
                catch
                {
                    continue;
                }

                // Ignora XREF/overlay
                try
                {
                    if (btr.IsFromExternalReference || btr.IsFromOverlayReference)
                    {
                        continue;
                    }
                }
                catch
                {
                }

                foreach (ObjectId entId in btr)
                {
                    if (!entId.IsValid || entId.IsNull)
                    {
                        continue;
                    }

                    DBObject obj = null;

                    try
                    {
                        obj = tr.GetObject(entId, OpenMode.ForRead);
                    }
                    catch
                    {
                        continue;
                    }

                    if (obj is Solid3d)
                    {
                        Solid3d solid = (Solid3d)obj;
                        list.Add(solid);
                        continue;
                    }

                    if (obj is Body)
                    {
                        Body body = (Body)obj;
                        list.Add(body);
                        continue;
                    }
                }
            }

            return list;
        }

        // -----------------------------
        // Linha de relatório
        // -----------------------------
        private sealed class ReportRow
        {
            internal string EntityType { get; set; }
            internal string Handle { get; set; }
            internal string Layer { get; set; }
            internal string OwnerBTR { get; set; }

            internal Dictionary<string, string> Values { get; }

            internal ReportRow()
            {
                Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static ReportRow BuildRowFromEntity(Database db, Transaction tr, Entity ent, HashSet<string> dynamicColumns)
        {
            ReportRow row = new ReportRow();

            row.EntityType = GetEntityType(ent);
            row.Handle = ent.Handle.ToString();
            row.Layer = ent.Layer;

            string ownerName = "";
            try
            {
                DBObject ownerObj = tr.GetObject(ent.OwnerId, OpenMode.ForRead);
                BlockTableRecord ownerBtr = ownerObj as BlockTableRecord;
                if (ownerBtr != null)
                {
                    ownerName = ownerBtr.Name;
                }
            }
            catch
            {
                ownerName = "";
            }

            row.OwnerBTR = ownerName;

            // property sets anexados ao objeto
            ObjectIdCollection psetIds = null;

            try
            {
                psetIds = PropertyDataServices.GetPropertySets(ent);
            }
            catch
            {
                psetIds = new ObjectIdCollection();
            }

            foreach (ObjectId psId in psetIds)
            {
                if (psId.IsNull || !psId.IsValid)
                {
                    continue;
                }

                PropertySet ps = null;
                try
                {
                    ps = (PropertySet)tr.GetObject(psId, OpenMode.ForRead);
                }
                catch
                {
                    continue;
                }

                ObjectId defId = ObjectId.Null;
                try
                {
                    defId = ps.PropertySetDefinition;
                }
                catch
                {
                    defId = ObjectId.Null;
                }

                string psetName = ResolvePsetName(db, tr, defId);
                if (string.IsNullOrWhiteSpace(psetName))
                {
                    // fallback: usa handle do definition
                    try
                    {
                        psetName = "PSET_" + defId.Handle.ToString();
                    }
                    catch
                    {
                        psetName = "PSET";
                    }
                }

                // lista propriedades pelo definition (mais confiável)
                PropertySetDefinition def = null;
                try
                {
                    if (!defId.IsNull)
                    {
                        def = (PropertySetDefinition)tr.GetObject(defId, OpenMode.ForRead);
                    }
                }
                catch
                {
                    def = null;
                }

                if (def == null)
                {
                    continue;
                }

                foreach (PropertyDefinition pdef in def.Definitions)
                {
                    if (pdef == null)
                    {
                        continue;
                    }

                    string propName = pdef.Name;
                    if (string.IsNullOrWhiteSpace(propName))
                    {
                        continue;
                    }

                    string col = psetName + "." + propName;
                    string val = TryReadPsetValue(ps, ent, propName);

                    if (!row.Values.ContainsKey(col))
                    {
                        row.Values.Add(col, val);
                    }

                    dynamicColumns.Add(col);
                }
            }

            return row;
        }

        private static string GetEntityType(Entity ent)
        {
            if (ent is Solid3d)
            {
                return "3DSOLID";
            }

            if (ent is Body)
            {
                return "BODY";
            }

            return ent.GetType().Name;
        }

        // -----------------------------
        // Ler valor de propriedade (inclui fórmulas)
        // -----------------------------
        private static string TryReadPsetValue(PropertySet ps, Entity ent, string propName)
        {
            if (ps == null || ent == null || string.IsNullOrWhiteSpace(propName))
            {
                return "";
            }

            int idx = -1;

            try
            {
                idx = ps.PropertyNameToId(propName);
            }
            catch
            {
                idx = -1;
            }

            if (idx < 0)
            {
                return "";
            }

            object v = null;

            try
            {
                v = ps.GetAt(idx, ent); // tenta com entidade (fórmulas)
            }
            catch
            {
                try
                {
                    v = ps.GetAt(idx); // fallback
                }
                catch
                {
                    v = null;
                }
            }

            if (v == null)
            {
                return "";
            }

            try
            {
                string s = Convert.ToString(v, CultureInfo.InvariantCulture);
                return s ?? "";
            }
            catch
            {
                return v.ToString();
            }
        }

        // -----------------------------
        // Nome do PSET (sem chutar API)
        // usa reflection em PropertySetDefinition para tentar pegar "Name"
        // -----------------------------
        private static string ResolvePsetName(Database db, Transaction tr, ObjectId defId)
        {
            if (defId.IsNull)
            {
                return "";
            }

            try
            {
                PropertySetDefinition def = (PropertySetDefinition)tr.GetObject(defId, OpenMode.ForRead);
                if (def == null)
                {
                    return "";
                }

                // tenta pegar propriedade "Name" via reflection (compatível com variações)
                System.Reflection.PropertyInfo pi = def.GetType().GetProperty("Name");
                if (pi != null)
                {
                    object nameObj = pi.GetValue(def, null);
                    if (nameObj != null)
                    {
                        return nameObj.ToString();
                    }
                }
            }
            catch
            {
            }

            return "";
        }

        // -----------------------------
        // Export XLSX via Excel Interop
        // -----------------------------
        private static void WriteXlsx(string xlsxPath, List<string> columns, List<ReportRow> rows)
        {
            Excel.Application excelApp = null;
            Excel.Workbook wb = null;
            Excel.Worksheet ws = null;

            try
            {
                excelApp = new Excel.Application();
                excelApp.DisplayAlerts = false;

                wb = excelApp.Workbooks.Add();
                ws = (Excel.Worksheet)wb.Worksheets[1];
                ws.Name = "Relatorio";

                int rowCount = rows.Count + 1; // + header
                int colCount = columns.Count;

                object[,] data = new object[rowCount, colCount];

                // headers
                for (int c = 0; c < colCount; c++)
                {
                    data[0, c] = columns[c];
                }

                // rows
                for (int r = 0; r < rows.Count; r++)
                {
                    ReportRow rr = rows[r];
                    int outRow = r + 1;

                    for (int c = 0; c < colCount; c++)
                    {
                        string col = columns[c];

                        if (col.Equals("EntityType", StringComparison.OrdinalIgnoreCase))
                        {
                            data[outRow, c] = rr.EntityType;
                            continue;
                        }

                        if (col.Equals("Handle", StringComparison.OrdinalIgnoreCase))
                        {
                            data[outRow, c] = rr.Handle;
                            continue;
                        }

                        if (col.Equals("Layer", StringComparison.OrdinalIgnoreCase))
                        {
                            data[outRow, c] = rr.Layer;
                            continue;
                        }

                        if (col.Equals("OwnerBTR", StringComparison.OrdinalIgnoreCase))
                        {
                            data[outRow, c] = rr.OwnerBTR;
                            continue;
                        }

                        if (rr.Values.TryGetValue(col, out string v))
                        {
                            data[outRow, c] = v;
                        }
                        else
                        {
                            data[outRow, c] = "";
                        }
                    }
                }

                Excel.Range start = (Excel.Range)ws.Cells[1, 1];
                Excel.Range end = (Excel.Range)ws.Cells[rowCount, colCount];
                Excel.Range range = ws.Range[start, end];
                range.Value2 = data;

                // melhora leitura
                //ws.Rows[1].ToString(). true;
                ws.Columns.AutoFit();

                string folder = Path.GetDirectoryName(xlsxPath);
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                wb.SaveAs(xlsxPath, Excel.XlFileFormat.xlOpenXMLWorkbook);
                wb.Close(true);
                excelApp.Quit();
            }
            finally
            {
                if (ws != null) { Marshal.ReleaseComObject(ws); }
                if (wb != null) { Marshal.ReleaseComObject(wb); }
                if (excelApp != null) { Marshal.ReleaseComObject(excelApp); }

                ws = null;
                wb = null;
                excelApp = null;

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // -----------------------------
        // UI: salvar xlsx
        // -----------------------------
        private static string AskSaveXlsxPath()
        {
            string file = null;

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "Excel (*.xlsx)|*.xlsx";
                dlg.Title = "Salvar relatório (XLSX)";
                dlg.FileName = "Relatorio_PSET_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".xlsx";
                dlg.OverwritePrompt = true;

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    file = dlg.FileName;
                }
            }

            return file;
        }
    }
}
