using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
// - Autodesk.AutoCAD.EditorInput
// - Autodesk.AutoCAD.Runtime
// - Autodesk.AutoCAD.Windows
// - Autodesk.AutoCAD.Geometry
// - Autodesk.Civil.ApplicationServices
// - Autodesk.Civil.DatabaseServices
// - Autodesk.Aec.PropertyData
// - Autodesk.Aec.PropertyData.DatabaseServices
// - OfficeOpenXml (EPPlus)
// - System.Windows.Forms

using Autodesk.Aec.PropertyData;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using DataType = Autodesk.Aec.PropertyData.DataType;
using Region = Autodesk.AutoCAD.DatabaseServices.Region;

namespace AutomacoesCivil3D
{
    public class MedidorNivelDagua
    {

        private const string SheetName = "7_Medidor de Nível de Água_Med";
        private const string BlockName = "INA";
        private const string RegAppName = "PSD_7_Medidor de Nível de Água_Med";

        [CommandMethod("INAS")]
        public static void InserirPiezometrosXlsx()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            CivilDocument civilDb = Manager.DocCivil;
            Database db = civilDoc.Database;


            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                Title = "Selecione a planilha de instrumentação"
            };
            if (ofd.ShowDialog() != DialogResult.OK)
            {
                docEditor.WriteMessage("\nCancelado.");
                return;
            }

            int inseridos = 0;

            // EPPlus licença (fixar sempre assim)
            ExcelPackage.License.SetNonCommercialPersonal("Gleison Bruno da Costa");

            using (ExcelPackage pkg = new ExcelPackage(new FileInfo(ofd.FileName)))
            {
                ExcelWorkbook wb = pkg.Workbook;
                ExcelWorksheet ws = wb.Worksheets[SheetName];
                if (ws == null)
                {
                    docEditor.WriteMessage($"\nPlanilha '{SheetName}' não encontrada.");
                    return;
                }

                int headerRow = 3;
                int firstDataRow = headerRow + 1;

                Dictionary<string, int> colIndex = MapearColunas(ws, headerRow);

                int colX = ResolveColuna(colIndex, new[] { "longitude utm", "longitude (UTM)", "easting", "coord x", "x", "utm e" });
                int colY = ResolveColuna(colIndex, new[] { "latitude utm", "Latitude (UTM)", "northing", "coord y", "y", "utm n" });
                // Z prioritariamente Elevação (coluna I)
                int colZ = ResolveColuna(colIndex, new[] { "Elevacao", "elevacao", "cota de projeto (z)", "altitude", "coord z", "z" });


                int colCotaTopo = ResolveColuna(colIndex, new[] { "Cota do Topo (m)", "Cota do Topo", "Cota Topo" });

                int colCotaFundo = ResolveColuna(colIndex, new[] { "Cota do Fundo m", "Cota do Fundo (m)", "Cota do Fundo", "Cota Fundo" });

                int colDiamPol = ResolveColuna(colIndex, new[] { "Diâmetro do Tubo (')", "Diâmetro do Tubo", "Diâmetro Tubo" });

                int colProfM = ResolveColuna(colIndex, new[] { "Profundidade (m)", "Profundidade m" });


                if (colX < 1 || colY < 1)
                {
                    docEditor.WriteMessage("\nColunas de coordenadas não encontradas. Esperado: 'longitude (UTM)' e 'Latitude (UTM)'.");
                    return;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    RegistrarRegApp(db, tr, RegAppName);
                    tr.Commit();
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    /* if (!bt.Has(BlockName))
                     {
                         docEditor.WriteMessage($"\nBloco '{BlockName}' não está no desenho.");
                         return;
                     }*/
                    //ObjectId blkDefId = bt[BlockName];
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    int lastRow = ws.Dimension.End.Row;
                    int lastCol = ws.Dimension.End.Column;

                    // Títulos originais por coluna
                    List<string> titulos = new List<string>(lastCol);
                    for (int c = 1; c <= lastCol; c++)
                    {
                        object hv = ws.Cells[headerRow, c].Value;
                        string titulo = hv != null ? ws.Cells[headerRow, c].Text.Trim() : string.Empty;
                        titulos.Add(titulo);
                    }

                    DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
                    Dictionary<int, ObjectId> psetDefByCol = new Dictionary<int, ObjectId>();
                    PropertySetDefinition novo = new PropertySetDefinition();

                    if (!dictionary.Has("DADOS MEDIDOR NIVEL D'AGUA", tr))
                    {
                        novo.SetToStandard(db);
                        novo.SubSetDatabaseDefaults(db);
                        novo.AppliesToAll = true;
                        novo.AlternateName = "DADOS MEDIDOR NIVEL D'AGUA";
                        novo.Description = "Importado da planilha: " + "DADOS MEDIDOR NIVEL D'AGUA";

                        dictionary.AddNewRecord("DADOS MEDIDOR NIVEL D'AGUA", novo);
                        tr.AddNewlyCreatedDBObject(novo, true);
                    }
                    else
                    {


                        ObjectId novoId = dictionary.GetAt("DADOS MEDIDOR NIVEL D'AGUA");
                        novo = (PropertySetDefinition)tr.GetObject(novoId, OpenMode.ForWrite);

                    }


                    for (int c = 1; c <= lastCol; c++)
                    {
                        string titulo = titulos[c - 1];
                        if (string.IsNullOrWhiteSpace(titulo)) continue;

                        psetDefByCol[c] = novo.Id;                     // mapeia todas as colunas para o MESMO PSet
                    }


                    CultureInfo pt = new CultureInfo("pt-BR");
                    CultureInfo inv = CultureInfo.InvariantCulture;
                    int colCodigo = ResolveColuna(colIndex, new[] { "codigo", "código", "cod" });
                    HashSet<string> codigosVistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    for (int r = firstDataRow; r <= lastRow; r++)
                    {
                        if (LinhaVazia(ws, r, 1, lastCol)) continue;

                        string codigoBruto = colCodigo > 0 ? ws.Cells[r, colCodigo].Text?.Trim() : string.Empty;
                        string codigoLimpo = LimparCodigoParaLayer(codigoBruto); // retorna "SEM_CODIGO" se vazio
                        // dentro do for (int r = firstDataRow; r <= lastRow; r++)

                        if (!string.Equals(codigoLimpo, "SEM_CODIGO", StringComparison.OrdinalIgnoreCase))
                        {
                            if (codigosVistos.Contains(codigoLimpo))
                            {
                                // já existe na planilha nesta execução -> não cria nada para esta linha

                                continue;
                            }
                            codigosVistos.Add(codigoLimpo);
                        }

                        if (!TryGetDouble(ws.Cells[r, colX].Text, out double x, pt, inv)) continue;
                        if (!TryGetDouble(ws.Cells[r, colY].Text, out double y, pt, inv)) continue;

                        double z = 0.0;
                        if (colZ > 0) TryGetDouble(ws.Cells[r, colZ].Text, out z, pt, inv);

                        Point3d ptIns = new Point3d(x, y, z);
                        //BlockReference br = new BlockReference(ptIns, blkDefId);

                        double cotaFundoM = 0.0;
                        double diamPol = 0.0;
                        double profM = 0.0;
                        double elevacaoM = 0.0;


                        TryGetDouble(ws.Cells[r, colCotaFundo].Text, out cotaFundoM, pt, inv);
                        TryGetDouble(ws.Cells[r, colDiamPol].Text, out diamPol, pt, inv);   // se vazio, método usa 1"
                        TryGetDouble(ws.Cells[r, colProfM].Text, out profM, pt, inv);
                        TryGetDouble(ws.Cells[r, colZ].Text, out elevacaoM, pt, inv);

                        //ObjectId solidId = CriarCilindroPiezometroExtrusao(db, tr, ms, x, y, cotaFundoM, diamPol, profM);
                        ObjectId solidId = CriarRetanguloExtrudadoNoTopo(db, tr, ms, x, y, elevacaoM + 1, 0.05, 0.1, profM);
                        //ObjectId solFinalId = UnirSolidos(db, tr, solidId, retId);
                        Solid3d br = (Solid3d)tr.GetObject(solidId, OpenMode.ForWrite);

                       

                        // limpa e garante layer
                        string layerNome = LimparCodigoParaLayer(codigoBruto);

                        layerNome = GarantirLayer(db, tr, layerNome);
                        if (br != null && !br.ObjectId.IsNull) br.Layer = layerNome;



                        //ObjectId brId = ms.AppendEntity(br);
                        //tr.AddNewlyCreatedDBObject(br, true);



                        if (!br.ObjectId.IsNull && !novo.ObjectId.IsNull)
                        {
                            PropertyDataServices.AddPropertySet(br, novo.ObjectId);
                        }
                        else
                        {
                            docEditor.WriteMessage("\n ALGUM DOS DOIS É NULO AQUI");
                        }

                        // XData opcional
                        ResultBuffer rb = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, RegAppName));
                        for (int c = 1; c <= lastCol; c++)
                        {
                            string key = titulos[c - 1];
                            if (string.IsNullOrWhiteSpace(key)) continue;
                            string valTxt = ws.Cells[r, c].Text?.Trim();
                            string kv = key + "=" + valTxt;
                            rb.Add(new TypedValue(1000, kv));
                        }
                        Entity ent = (Entity)tr.GetObject(solidId, OpenMode.ForWrite);
                        ent.XData = rb;

                        // Preencher PSets por coluna (sem engolir exceções gerais)
                        foreach (KeyValuePair<int, ObjectId> kv in psetDefByCol)
                        {
                            int c = kv.Key;
                            ObjectId defId1 = kv.Value;                  // USE defId1, não defId

                            string titulo = titulos[c - 1];


                            if (string.IsNullOrWhiteSpace(titulo)) continue;

                            string valor = ws.Cells[r, c].Text?.Trim();

                            PropertyDefinition pdNew = new PropertyDefinition();
                            pdNew.SetToStandard(db);
                            pdNew.SubSetDatabaseDefaults(db);

                            pdNew.Name = titulo;
                            pdNew.Description = titulo;
                            pdNew.DataType = DataType.Text;
                            pdNew.DefaultData = " - ";
                            if (!novo.Definitions.Contains(pdNew))
                            {
                                novo.Definitions.Add(pdNew);

                            }
                            else
                            {
                                // 3) reobtém o PropertySet após alterar a definição
                                ObjectId psId = PropertyDataServices.GetPropertySet(br, novo.ObjectId);
                                PropertySet ps = (PropertySet)tr.GetObject(psId, OpenMode.ForWrite);

                                // 4) pega o id da propriedade e grava o valor
                                int pid = ps.PropertyNameToId(titulo);       // se não existir aqui, vai explodir (bom para depurar)
                                ps.SetAt(pid, valor);


                            }

                            



                        }






                        inseridos++;
                    }

                    tr.Commit();
                }
            }

            docEditor.WriteMessage($"\n{inseridos} blocos '{BlockName}' inseridos. PSets por coluna criados e preenchidos. Z=Elevação.");

        }

        /// <summary>
        /// Cria um cilindro (Solid3d) no ponto do piezômetro:
        /// - Centro no (x,y, Z = cotaTopo)
        /// - Diâmetro em polegadas convertido para metros
        /// - Extrusão para baixo com profundidade (m)
        /// Retorna ObjectId do Solid3d ou ObjectId.Null se não criar.
        /// </summary>
        // Cria cilindro por extrusão: centro (x,y, Z=cotaTopoM), diâmetro em polegadas, extrusão negativa = profundidadeM
        public static ObjectId CriarCilindroPiezometroExtrusao(
            Database db,
            Transaction tr,
            BlockTableRecord ms,
            double x,
            double y,
            double cotaFundoM,
            double diamPolegadas,   // se <=0, usa 1"
            double profundidadeM)   // se <=0, não cria
        {
            if (profundidadeM <= 0.0) return ObjectId.Null;

            const double IN_TO_M = 0.0254;
            double diamM = ((diamPolegadas > 0.0) ? diamPolegadas : 1.0) * IN_TO_M;

            ///////////////////////////////////////////////////////////////////////////////
            /// TROQUEI O DIAMETRO AQUI PRA TESTAR
            //double raioM = diamM * 0.5;
            double raioM = 0.25;

            // Perfil: círculo em Z = cotaFundoM
            Circle circ = new Circle(new Point3d(x, y, cotaFundoM), Vector3d.ZAxis, raioM);

            ObjectId circId = ms.AppendEntity(circ);
            tr.AddNewlyCreatedDBObject(circ, true);

            // Região a partir do círculo
            DBObjectCollection curves = new DBObjectCollection();
            curves.Add(circ);
            DBObjectCollection regs = Region.CreateFromCurves(curves);
            if (regs == null || regs.Count == 0)
            {
                circ.Erase();
                return ObjectId.Null;
            }

            Region reg = (Region)regs[0];
            ObjectId regId = ms.AppendEntity(reg);
            tr.AddNewlyCreatedDBObject(reg, true);

            // Sólido por extrusão usando o overload com SubentityId e SweepOptions
            Solid3d sol = new Solid3d();
            sol.SetDatabaseDefaults();



            sol.Extrude(reg, profundidadeM, 0);

            ObjectId solidId = ms.AppendEntity(sol);
            tr.AddNewlyCreatedDBObject(sol, true);

            // Limpeza
            reg.Erase();
            circ.Erase();

            return solidId;
        }

        // Cria prisma retangular  (larg x comp x prof) a partir de Polyline + Extrude
        public static ObjectId CriarRetanguloExtrudadoNoTopo(
            Database db,
            Transaction tr,
            BlockTableRecord ms,
            double x,
            double y,
            double elevacaoM,
            double largura,        // ex.: 0.3
            double comprimento,   // ex.: 0.3
            double profundidadeM   // use o mesmo sinal que você usou no círculo (sol.Extrude(reg, profundidadeM, 0))


        )
        {
            // 1) Polyline retangular centrada no topo
            double hx = largura * 0.5;
            double hy = comprimento * 0.5;

            Autodesk.AutoCAD.DatabaseServices.Polyline pl = new Autodesk.AutoCAD.DatabaseServices.Polyline(4);
            pl.AddVertexAt(0, new Point2d(x - hx, y - hy), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x + hx, y - hy), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x + hx, y + hy), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(x - hx, y + hy), 0, 0, 0);
            pl.Closed = true;
            pl.Elevation = elevacaoM;           // coloca o retângulo no Z da cota de topo
            pl.Normal = Vector3d.ZAxis;

            ObjectId plId = ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);

            // 2) Região a partir do retângulo
            DBObjectCollection curvas = new DBObjectCollection();
            curvas.Add(pl);
            DBObjectCollection regs = Region.CreateFromCurves(curvas);
            if (regs == null || regs.Count == 0)
            {
                pl.Erase();
                return ObjectId.Null;
            }

            Region reg = (Region)regs[0];
            ObjectId regId = ms.AppendEntity(reg);
            tr.AddNewlyCreatedDBObject(reg, true);

            // 3) Extrusão igual ao círculo
            Solid3d sol = new Solid3d();
            sol.SetDatabaseDefaults();
            sol.Extrude(reg, -profundidadeM, 0);   // mesmo padrão que você usou no círculo

            ObjectId solidId = ms.AppendEntity(sol);
            tr.AddNewlyCreatedDBObject(sol, true);

            // 4) Limpeza
            reg.Erase();
            pl.Erase();

            return solidId;
        }

        public static ObjectId UnirSolidos(Database db, Transaction tr, ObjectId cilindroId, ObjectId caixaId)
        {
            if (cilindroId.IsNull || caixaId.IsNull) return ObjectId.Null;

            Solid3d solCilindro = (Solid3d)tr.GetObject(cilindroId, OpenMode.ForWrite);
            Solid3d solCaixa = (Solid3d)tr.GetObject(caixaId, OpenMode.ForWrite);

            solCilindro.BooleanOperation(BooleanOperationType.BoolUnite, solCaixa);
            solCaixa.Erase();


            return solCilindro.ObjectId; // sólido resultante
        }


        // === Helpers Excel/CAD ===

        private static Dictionary<string, int> MapearColunas(ExcelWorksheet ws, int headerRow)
        {
            int lastCol = ws.Dimension.End.Column;
            Dictionary<string, int> map = new Dictionary<string, int>();
            for (int c = 1; c <= lastCol; c++)
            {
                string txt = ws.Cells[headerRow, c].Text?.Trim();
                if (string.IsNullOrWhiteSpace(txt)) continue;
                string norm = Normalizar(txt);
                if (!map.ContainsKey(norm)) map.Add(norm, c);
            }
            return map;
        }

        private static int ResolveColuna(Dictionary<string, int> map, IEnumerable<string> candidatos)
        {
            foreach (string cand in candidatos)
            {
                string norm = Normalizar(cand);
                if (map.TryGetValue(norm, out int col)) return col;
            }
            return -1;
        }

        private static string Normalizar(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            string lower = s.Trim().ToLowerInvariant();
            string noAccents = new string(lower
                .Normalize(NormalizationForm.FormD)
                .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                .ToArray())
                .Normalize(NormalizationForm.FormC);
            return noAccents.Replace("  ", " ");
        }

        private static bool TryGetDouble(string text, out double value, CultureInfo pt, CultureInfo inv)
        {
            value = 0.0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (double.TryParse(text, NumberStyles.Float, pt, out value)) return true;
            if (double.TryParse(text, NumberStyles.Float, inv, out value)) return true;
            string swap = text.Contains(',') ? text.Replace(".", "").Replace(',', '.') : text.Replace(",", "");
            return double.TryParse(swap, NumberStyles.Float, inv, out value);
        }

        private static bool LinhaVazia(ExcelWorksheet ws, int row, int c1, int c2)
        {
            for (int c = c1; c <= c2; c++)
            {
                if (!string.IsNullOrWhiteSpace(ws.Cells[row, c].Text))
                    return false;
            }
            return true;
        }

        private static void RegistrarRegApp(Database db, Transaction tr, string regAppName)
        {
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(regAppName))
            {
                rat.UpgradeOpen();
                RegAppTableRecord rec = new RegAppTableRecord { Name = regAppName };
                rat.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }
        }

        // helpers
        private static string LimparCodigoParaLayer(string codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return "SEM_CODIGO";
            string s = codigo.Trim().ToUpperInvariant();
            s = s.Replace(' ', '_').Replace('/', '_').Replace('\\', '_');
            // remove caracteres inválidos p/ layer: <>/\";?*|,= e outros
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.')
                    sb.Append(ch);
                else
                    sb.Append('_');
            }
            string res = sb.ToString();
            while (res.Contains("__")) res = res.Replace("__", "_");
            res = res.Trim('_');
            if (string.IsNullOrEmpty(res)) res = "SEM_CODIGO";
            if (res.Length > 255) res = res.Substring(0, 255);
            return res;
        }

        private static string GarantirLayer(Database db, Transaction tr, string layerName)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName)) return layerName;

            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord();
            ltr.Name = layerName;
            ltr.IsOff = false;
            ltr.IsLocked = false;
            ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 2); // verde            
            ObjectId id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            lt.DowngradeOpen();
            return layerName;
        }
    }
}
