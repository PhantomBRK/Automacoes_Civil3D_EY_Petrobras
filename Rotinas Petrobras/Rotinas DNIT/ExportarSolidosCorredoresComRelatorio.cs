

// Refs: AutoCAD + Civil 3D + AEC PropertyData
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using Autodesk.Aec.DatabaseServices;
using Autodesk.Aec.PropertyData;
using Autodesk.Aec.PropertyData.DatabaseServices;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using ObjectIdCollection = Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection;

namespace AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES

{
    public class ExportarSolidosRelatorio
    {
        [CommandMethod("ExportarSolidosComRelatorio")]
        public void ExportarSolidosCorredoresComRelatorio()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database docData = Manager.DocData;

            try
            {
                string caminhoDestino = AbrirDialogoSelecaoArquivo(
                    "Arquivos DWG (*.dwg)|*.dwg",
                    "Selecione o DWG de destino");

                if (string.IsNullOrWhiteSpace(caminhoDestino))
                {
                    docEditor.WriteMessage("\nNenhum arquivo de destino selecionado.");
                    return;
                }

                Document destDoc = GarantirDocumentoAberto(caminhoDestino);
                if (destDoc == null)
                {
                    docEditor.WriteMessage("\nFalha ao abrir/criar o destino.");
                    return;
                }

                string sugestaoCsv = BuildDefaultCsvPathFromDwg(caminhoDestino);
                string caminhoCsv = AbrirDialogoSalvarArquivo(
                    "CSV (*.csv)|*.csv",
                    "Salvar relatório (CSV)",
                    sugestaoCsv);

                if (string.IsNullOrWhiteSpace(caminhoCsv))
                {
                    docEditor.WriteMessage("\nRelatório não selecionado. A exportação continuará sem planilha.");
                }

                ObjectIdCollection idsExportados = new ObjectIdCollection();
                List<ReportRow> reportRows = new List<ReportRow>();

                // ============================
                // FASE 1: gerar sólidos/bodies e aplicar PSETs (origem) + capturar relatório
                // ============================
                using (Transaction tr = docData.TransactionManager.StartTransaction())
                {
                    DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(docData);

                    string propSetNameA = "A - Dados do Projeto";
                    string propSetNameB = "B - Informações dos Objetos e Elementos";
                    string propSetNameC = "C - Propriedades Fisicas dos Objetos e Elementos";
                    string propSetNameD = "D - Propriedades Geográficas";


                    ObjectId propSetIdA = TryGetPsetDefinitionId(dictionary, tr, propSetNameA);
                    ObjectId propSetIdB = TryGetPsetDefinitionId(dictionary, tr, propSetNameB);
                    ObjectId propSetIdC = TryGetPsetDefinitionId(dictionary, tr, propSetNameC);
                    ObjectId propSetIdD = TryGetPsetDefinitionId(dictionary, tr, propSetNameD);


                    PropertySetDefinition propSetDefA = OpenPsetDef(tr, propSetIdA);
                    PropertySetDefinition propSetDefB = OpenPsetDef(tr, propSetIdB);
                    PropertySetDefinition propSetDefC = OpenPsetDef(tr, propSetIdC);
                    PropertySetDefinition propSetDefD = OpenPsetDef(tr, propSetIdD);


                    List<PsetInfo> psetsParaRelatorio = new List<PsetInfo>();
                    psetsParaRelatorio.Add(new PsetInfo(propSetNameA, propSetIdA, propSetDefA));
                    psetsParaRelatorio.Add(new PsetInfo(propSetNameB, propSetIdB, propSetDefB));
                    psetsParaRelatorio.Add(new PsetInfo(propSetNameC, propSetIdC, propSetDefC));
                    psetsParaRelatorio.Add(new PsetInfo(propSetNameD, propSetIdD, propSetDefD));


                    PropertySets propertySets = new PropertySets();

                    foreach (ObjectId corridorId in civilDb.CorridorCollection)
                    {
                        Corridor corridor = (Corridor)tr.GetObject(corridorId, OpenMode.ForRead);
                        if (corridor.IsReferenceObject)
                        {
                            continue;
                        }

                        string[] shapeCodes = corridor.GetShapeCodes();
                        string[] linkCodes = corridor.GetLinkCodes();
                        string[] included = shapeCodes
                            .Concat(linkCodes)
                            .Concat(new string[] { "INICIO TALUDE" })
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();

                        ExportCorridorSolidsParams p = new ExportCorridorSolidsParams
                        {
                            IncludedCodes = included,
                            ExportLinks = true,
                            ExportShapes = true
                        };
                        // ====== Varre ModelSpace ======
                        BlockTable blockTable = (BlockTable)tr.GetObject(docData.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                        //ObjectIdCollection exp = corridor.ExportSolids(p, docData);

                        foreach (ObjectId id in modelSpace)
                        {
                            if (!id.IsValid || id.IsNull)
                            {
                                continue;
                            }

                            // Solid3d
                            if (id.ObjectClass != null && id.ObjectClass.Name == "AcDb3dSolid")
                            {
                                Solid3d s = (Solid3d)tr.GetObject(id, OpenMode.ForWrite);

                                EnsurePset(s, propSetIdA);
                                EnsurePset(s, propSetIdB);
                                EnsurePset(s, propSetIdC);
                                EnsurePset(s, propSetIdD);


                                //propertySets.PSetSolid(s, docData, tr);
                                idsExportados.Add(s.ObjectId);

                                if (!string.IsNullOrWhiteSpace(caminhoCsv))
                                {
                                    ReportRow row = BuildReportRow(tr, s, "3DSOLID", "corridor.Name", psetsParaRelatorio);
                                    reportRows.Add(row);
                                }
                            }
                            // Body
                            else if (id.ObjectClass != null && id.ObjectClass.Name == "AcDbBody")
                            {
                                Body b = (Body)tr.GetObject(id, OpenMode.ForWrite);

                                EnsurePset(b, propSetIdA);
                                EnsurePset(b, propSetIdB);
                                EnsurePset(b, propSetIdC);
                                EnsurePset(b, propSetIdD);


                                //propertySets.PSetBody(b, docData, tr);
                                idsExportados.Add(b.ObjectId);

                                if (!string.IsNullOrWhiteSpace(caminhoCsv))
                                {
                                    ReportRow row = BuildReportRow(tr, b, "BODY", corridor.Name, psetsParaRelatorio);
                                    reportRows.Add(row);
                                }
                            }
                        }
                    }

                    tr.Commit();
                }

                // ============================
                // FASE 1.1: salvar planilha (CSV)
                // ============================
                if (!string.IsNullOrWhiteSpace(caminhoCsv))
                {
                    ExportReportToCsv(caminhoCsv, reportRows);
                    docEditor.WriteMessage($"\nRelatório CSV gerado: {caminhoCsv}");
                }

                // ============================
                // FASE 2: clonar para destino
                // ============================
                Civil3DObjectCopier2 copiadora = new Civil3DObjectCopier2();
                //copiadora.CopyObjectsBetweenDrawings(idsExportados, caminhoDestino, null, docData);

                // ============================
                // FASE 3: apagar na origem (genérico: solid + body)
                // ============================
                using (Transaction trErase = docData.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in idsExportados)
                    {
                        if (!id.IsValid || id.IsNull)
                        {
                            continue;
                        }

                        try
                        {
                            DBObject obj = trErase.GetObject(id, OpenMode.ForWrite, false);
                            if (obj != null && !obj.IsErased)
                            {
                                obj.Erase(true);
                            }
                        }
                        catch
                        {
                        }
                    }

                    trErase.Commit();
                }

                docEditor.WriteMessage("\nExportação concluída.");
            }
            catch (Exception exAcad)
            {
                Editor ed = Manager.DocEditor;
                ed.WriteMessage($"\nErro AutoCAD: {exAcad.Message}");
            }
            catch (System.Exception ex)
            {
                Editor ed = Manager.DocEditor;
                ed.WriteMessage($"\nErro geral: {ex.Message}");
            }
        }

        // ============================
        // RELATÓRIO (CSV)
        // ============================

        private sealed class PsetInfo
        {
            internal string PsetName { get; }
            internal ObjectId PsetDefId { get; }
            internal PropertySetDefinition PsetDef { get; }

            internal PsetInfo(string psetName, ObjectId psetDefId, PropertySetDefinition psetDef)
            {
                PsetName = psetName;
                PsetDefId = psetDefId;
                PsetDef = psetDef;
            }
        }

        private sealed class ReportRow
        {
            internal string EntityType { get; set; }
            internal string Handle { get; set; }
            internal string Layer { get; set; }
            internal string Corridor { get; set; }
            internal Dictionary<string, string> Values { get; }

            internal ReportRow()
            {
                Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static ReportRow BuildReportRow(
            Transaction tr,
            Entity ent,
            string entityType,
            string corridorName,
            List<PsetInfo> psets)
        {
            ReportRow row = new ReportRow();
            row.EntityType = entityType;
            row.Handle = ent.Handle.ToString();
            row.Layer = ent.Layer;
            row.Corridor = corridorName ?? string.Empty;

            foreach (PsetInfo p in psets)
            {
                if (p == null || p.PsetDefId.IsNull || p.PsetDef == null)
                {
                    continue;
                }

                ObjectId psId = PropertyDataServices.GetPropertySet(ent, p.PsetDefId);
                if (psId.IsNull)
                {
                    continue;
                }

                PropertySet ps = (PropertySet)tr.GetObject(psId, OpenMode.ForRead);

                foreach (PropertyDefinition def in p.PsetDef.Definitions)
                {
                    if (def == null)
                    {
                        continue;
                    }

                    string propName = def.Name;
                    if (string.IsNullOrWhiteSpace(propName))
                    {
                        continue;
                    }

                    string key = p.PsetName + "." + propName;
                    string val = TryReadPropertyValue(ps, ent, propName);

                    if (!row.Values.ContainsKey(key))
                    {
                        row.Values.Add(key, val ?? string.Empty);
                    }
                }
            }

            return row;
        }

        private static string TryReadPropertyValue(PropertySet ps, Entity ent, string propName)
        {
            try
            {
                int idx = ps.PropertyNameToId(propName);
                if (idx < 0)
                {
                    return string.Empty;
                }

                object v = null;

                // algumas versões aceitam GetAt(idx, ent) (útil p/ fórmulas)
                try
                {
                    v = ps.GetAt(idx, ent);
                }
                catch
                {
                    v = ps.GetAt(idx);
                }

                if (v == null)
                {
                    return string.Empty;
                }

                string s = Convert.ToString(v, CultureInfo.InvariantCulture);
                return s ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void ExportReportToCsv(string csvPath, List<ReportRow> rows)
        {
            if (rows == null)
            {
                rows = new List<ReportRow>();
            }

            // colunas fixas
            List<string> columns = new List<string>();
            columns.Add("EntityType");
            columns.Add("Handle");
            columns.Add("Layer");
            columns.Add("Corridor");

            // colunas dinâmicas (todos os PSET.Prop)
            HashSet<string> dyn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ReportRow r in rows)
            {
                foreach (string k in r.Values.Keys)
                {
                    dyn.Add(k);
                }
            }

            List<string> dynSorted = dyn.OrderBy((string s) => s, StringComparer.OrdinalIgnoreCase).ToList();
            columns.AddRange(dynSorted);

            StringBuilder sb = new StringBuilder(1024);
            sb.AppendLine(string.Join(";", columns.Select(EscapeCsv)));

            foreach (ReportRow r in rows)
            {
                List<string> line = new List<string>(columns.Count);

                for (int i = 0; i < columns.Count; i++)
                {
                    string c = columns[i];

                    if (c.Equals("EntityType", StringComparison.OrdinalIgnoreCase))
                    {
                        line.Add(EscapeCsv(r.EntityType));
                    }
                    else if (c.Equals("Handle", StringComparison.OrdinalIgnoreCase))
                    {
                        line.Add(EscapeCsv(r.Handle));
                    }
                    else if (c.Equals("Layer", StringComparison.OrdinalIgnoreCase))
                    {
                        line.Add(EscapeCsv(r.Layer));
                    }
                    else if (c.Equals("Corridor", StringComparison.OrdinalIgnoreCase))
                    {
                        line.Add(EscapeCsv(r.Corridor));
                    }
                    else
                    {
                        string v = string.Empty;
                        if (r.Values.TryGetValue(c, out string vv))
                        {
                            v = vv ?? string.Empty;
                        }
                        line.Add(EscapeCsv(v));
                    }
                }

                sb.AppendLine(string.Join(";", line));
            }

            File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
        }

        private static string EscapeCsv(string v)
        {
            if (v == null)
            {
                return "";
            }

            string s = v.Replace("\r", " ").Replace("\n", " ");
            bool needQuotes = s.Contains(";") || s.Contains("\"");

            if (s.Contains("\""))
            {
                s = s.Replace("\"", "\"\"");
            }

            if (needQuotes)
            {
                s = "\"" + s + "\"";
            }

            return s;
        }

        private static string BuildDefaultCsvPathFromDwg(string dwgPath)
        {
            string folder = Path.GetDirectoryName(dwgPath);
            string name = Path.GetFileNameWithoutExtension(dwgPath);

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string file = name + "_RelatorioPSET_" + stamp + ".csv";

            return Path.Combine(folder, file);
        }

        // ============================
        // PSET HELPERS
        // ============================

        private static PropertySetDefinition OpenPsetDef(Transaction tr, ObjectId psetDefId)
        {
            if (psetDefId.IsNull)
            {
                return null;
            }

            try
            {
                PropertySetDefinition def = (PropertySetDefinition)tr.GetObject(psetDefId, OpenMode.ForRead);
                return def;
            }
            catch
            {
                return null;
            }
        }

        private static ObjectId TryGetPsetDefinitionId(DictionaryPropertySetDefinitions dict, Transaction tr, string name)
        {
            if (dict == null || tr == null || string.IsNullOrWhiteSpace(name))
            {
                return ObjectId.Null;
            }

            try
            {
                Autodesk.Aec.DatabaseServices.Dictionary aecDict = (Autodesk.Aec.DatabaseServices.Dictionary)dict;
                if (aecDict.Has(name, tr))
                {
                    return aecDict.GetAt(name);
                }
            }
            catch
            {
            }

            return ObjectId.Null;
        }

        private static void EnsurePset(Entity ent, ObjectId psetDefId)
        {
            if (ent == null || psetDefId.IsNull)
            {
                return;
            }

            try
            {
                ObjectId psId = PropertyDataServices.GetPropertySet(ent, psetDefId);
                if (psId.IsNull)
                {
                    PropertyDataServices.AddPropertySet(ent, psetDefId);
                }
            }
            catch
            {
            }
        }

        // ============================
        // IO / DOC HELPERS
        // ============================

        private static Document GarantirDocumentoAberto(string caminho)
        {
            string alvo = Path.GetFullPath(caminho);
            DocumentCollection docs = Application.DocumentManager;

            foreach (Document d in docs)
            {
                try
                {
                    if (!string.IsNullOrEmpty(d.Name) &&
                        Path.GetFullPath(d.Name).Equals(alvo, StringComparison.OrdinalIgnoreCase))
                    {
                        return d;
                    }
                }
                catch
                {
                }
            }

            if (!File.Exists(alvo))
            {
                Database novoDb = new Database(true, true);
                novoDb.SaveAs(alvo, DwgVersion.Current);
                novoDb.Dispose();
            }

            Document aberto = Application.DocumentManager.Open(alvo, false);
            return aberto;
        }

        public static string AbrirDialogoSelecaoArquivo(string filtro, string titulo)
        {
            string caminhoArquivo = null;

            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = filtro;
                dlg.Multiselect = false;
                dlg.Title = titulo;

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    caminhoArquivo = dlg.FileName;
                }
            }

            return caminhoArquivo;
        }

        public static string AbrirDialogoSalvarArquivo(string filtro, string titulo, string sugestaoCompleta)
        {
            string caminhoArquivo = null;

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = filtro;
                dlg.Title = titulo;

                try
                {
                    dlg.InitialDirectory = Path.GetDirectoryName(sugestaoCompleta);
                    dlg.FileName = Path.GetFileName(sugestaoCompleta);
                }
                catch
                {
                }

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    caminhoArquivo = dlg.FileName;
                }
            }

            return caminhoArquivo;
        }

        // Guard anti-reentrância (mantido)
        internal static class IfcApplyGuard
        {
            [ThreadStatic] internal static bool Busy;
        }
    }
}

