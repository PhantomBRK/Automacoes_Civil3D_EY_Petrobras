// Referências necessárias no projeto:
// - Autodesk.AutoCAD.ApplicationServices
// - Autodesk.AutoCAD.DatabaseServices
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
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Vml.Office;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Color = Autodesk.AutoCAD.Colors.Color;
using DataType = Autodesk.Aec.PropertyData.DataType;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using FlowDirection = System.Windows.Forms.FlowDirection;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using Region = Autodesk.AutoCAD.DatabaseServices.Region;
using SelectionMode = System.Windows.Forms.SelectionMode;

namespace AutomacoesCivil3D
{
    public static class Inclinometros
    {
        // topo da classe Inclinometros
        private struct Cell { public int I; public int J; public Cell(int i, int j) { I = i; J = j; } }
        private sealed class CellComparer : IEqualityComparer<Cell>
        {
            public bool Equals(Cell a, Cell b) => a.I == b.I && a.J == b.J;
            public int GetHashCode(Cell c) => (c.I * 73856093) ^ (c.J * 19349663);
        }

        // retorna o índice incrementado daquela célula (0,1,2,…)
        private static int BumpCell(Dictionary<Cell, int> counts, double x, double y, double grid)
        {
            int i = (int)Math.Floor(x / grid);
            int j = (int)Math.Floor(y / grid);
            Cell key = new Cell(i, j);
            int n;
            if (!counts.TryGetValue(key, out n)) { counts[key] = 1; return 0; }
            counts[key] = n + 1;
            return n; // índice anterior
        }

        // gera ponto do MText a partir de um índice (fan 8 direções, anéis crescentes)
        private static Point3d FanOffset(Point3d basePt, int idx, double baseR, double stepR)
        {
            int dir = idx % 8;              // 0..7 (N,NE,E,SE,S,SW,W,NW)
            int ring = (idx / 8) + 1;       // 1..∞
            double r = baseR + (ring - 1) * stepR;
            double ang = (Math.PI / 4.0) * dir;
            double dx = r * Math.Cos(ang);
            double dy = r * Math.Sin(ang);
            return new Point3d(basePt.X + dx, basePt.Y + dy, basePt.Z);
        }

        // janela simples de modo inicial
        private class StartModeForm : Form
        {
            public enum Choice { Uma, Todas, Cancel }
            public Choice Result = Choice.Cancel;
            public StartModeForm()
            {
                this.Text = "Locação de equipamentos"; this.Width = 360; this.Height = 160;
                FlowLayoutPanel p = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12) };
                Button b1 = new Button() { Text = "Percorrer TODAS as planilhas", Width = 300 };
                Button b2 = new Button() { Text = "Escolher UMA planilha", Width = 300 };
                Button b3 = new Button() { Text = "Cancelar", Width = 300 };
                b1.Click += (s, e) => { Result = Choice.Todas; this.DialogResult = DialogResult.OK; };
                b2.Click += (s, e) => { Result = Choice.Uma; this.DialogResult = DialogResult.OK; };
                b3.Click += (s, e) => { Result = Choice.Cancel; this.DialogResult = DialogResult.Cancel; };
                p.Controls.Add(b1); p.Controls.Add(b2); p.Controls.Add(b3); this.Controls.Add(p);
            }
            public static Choice Pick()
            {
                using (var f = new StartModeForm()) { var dr = Application.ShowModalDialog(f); return (dr == DialogResult.OK) ? f.Result : Choice.Cancel; }
            }

        }


        private const string BlockName = "Inclinometro";
        private const string SuperficieTopoNome = "TN";

        [CommandMethod("LocarEquipamentos")]
        public static void InserirInclinometrosXlsx()
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

            ExcelPackage.License.SetNonCommercialPersonal("Gleison Bruno da Costa");





            using (ExcelPackage pkgPreview = new ExcelPackage(new FileInfo(ofd.FileName)))
            {
                var modo = StartModeForm.Pick();
                if (modo == StartModeForm.Choice.Cancel) { docEditor.WriteMessage("\nCancelado."); return; }

                ExcelWorkbook wbPreview = pkgPreview.Workbook;
                string[] nomes = wbPreview.Worksheets.Select(w => w.Name).ToArray();

                List<string> sheetsAlvo = new List<string>();
                if (modo == StartModeForm.Choice.Uma)
                {
                    string escolha = SheetPickerForm.Pick(nomes, "Escolha a planilha");
                    if (string.IsNullOrWhiteSpace(escolha)) { docEditor.WriteMessage("\nCancelado."); return; }
                    sheetsAlvo.Add(escolha);
                }
                else // todas
                {
                    sheetsAlvo.AddRange(nomes);
                }



                int inseridosTotal = 0;
                List<int> linhasSemCoord = new List<int>();

                using (ExcelPackage pkg = new ExcelPackage(new FileInfo(ofd.FileName)))
                {
                    ExcelWorkbook wb = pkg.Workbook;


                    using (Transaction trPrep = db.TransactionManager.StartTransaction())
                    { RegistrarRegApp(db, trPrep, regAppName: "PSD_3_ALL"); trPrep.Commit(); }

                    int pos = 1;
                    foreach (string sheetName in sheetsAlvo)
                    {
                        string regAppName, psetAltName; DerivarNomesDePlanilha(sheetName, out regAppName, out psetAltName);
                        ExcelWorksheet ws = wb.Worksheets[sheetName]; if (ws == null) { docEditor.WriteMessage($"\nPlanilha '{sheetName}' não encontrada."); continue; }

                        int corAciPlanilha = ExtrairNumeroInicial(sheetName);
                        int headerRow = 3, firstDataRow = headerRow + 1;
                        Dictionary<string, int> colIndex = MapearColunas(ws, headerRow);

                        int colX = ResolveColuna(colIndex, new[] { "longitude utm", "longitude (utm)", "easting", "coord x", "x", "utm e" });
                        int colY = ResolveColuna(colIndex, new[] { "latitude utm", "latitude (utm)", "northing", "coord y", "y", "utm n" });
                        int colZ = ResolveColuna(colIndex, new[] { "elevação", "elevacao", "cota de projeto (z)", "altitude", "coord z", "z" });
                        int colModelo = ResolveColuna(colIndex, new[] { "modelo", "modelo do equipamento", "model" });
                        int colCotaTopo = ResolveColuna(colIndex, new[] { "cota do topo (m)", "cota de projeto (z)", "cota topo", "cota de topo" });
                        int colCotaFundo = ResolveColuna(colIndex, new[] { "cota do fundo (m)", "cota de fundo", "cota fundo" });
                        int colDiamPol = ResolveColuna(colIndex, new[] { "diâmetro do tubo (')", "diâmetro do tubo", "diâmetro tubo" });
                        int colDiamM = ResolveColuna(colIndex, new[] { "diâmetro (m)", "diametro (m)", "diâmetro do tubo (m)" });
                        int colCodigo = ResolveColuna(colIndex, new[] { "código", "codigo", "cod" });
                        int colProfM = ResolveColuna(colIndex, new[] { "Profundidade (m)", "Profundidade m" });


                        if (colX < 1 || colY < 1)
                        {
                            docEditor.WriteMessage("\nColunas de coordenadas não encontradas. Esperado: 'longitude (UTM)' e 'Latitude (UTM)'.");
                            return;
                        }

                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            RegistrarRegApp(db, tr, regAppName);
                            tr.Commit();
                        }

                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                            // dicionário por célula para distribuir rótulos nesta planilha
                            Dictionary<Cell, int> cellCounts = new Dictionary<Cell, int>(new CellComparer());
                            double grid = 300.0;        // tamanho da célula em unidades do desenho
                            double baseR = 24;        // raio inicial do texto
                            double stepR = 6.0;        // incremento de raio por “anel”

                            int lastRow = ws.Dimension.End.Row;
                            int lastCol = ws.Dimension.End.Column;

                            List<string> titulos = new List<string>(lastCol);
                            for (int c = 1; c <= lastCol; c++)
                            {
                                object hv = ws.Cells[headerRow, c].Value;
                                string titulo = hv != null ? ws.Cells[headerRow, c].Text.Trim() : string.Empty;
                                titulos.Add(titulo);
                            }

                            DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);
                            Dictionary<int, ObjectId> psetDefByCol = new Dictionary<int, ObjectId>();
                            PropertySetDefinition psetDef = new PropertySetDefinition();

                            if (!dictionary.Has(psetAltName, tr))
                            {
                                psetDef.SetToStandard(db);
                                psetDef.SubSetDatabaseDefaults(db);
                                psetDef.AppliesToAll = true;
                                psetDef.AlternateName = psetAltName;
                                psetDef.Description = "Importado da planilha: " + sheetName;

                                dictionary.AddNewRecord(psetAltName, psetDef);
                                tr.AddNewlyCreatedDBObject(psetDef, true);
                            }
                            else
                            {
                                ObjectId pdefId = dictionary.GetAt(psetAltName);
                                psetDef = (PropertySetDefinition)tr.GetObject(pdefId, OpenMode.ForWrite);
                            }

                            for (int c = 1; c <= lastCol; c++)
                            {
                                string titulo = titulos[c - 1];
                                if (string.IsNullOrWhiteSpace(titulo)) continue;
                                psetDefByCol[c] = psetDef.Id;
                            }

                            CultureInfo pt = new CultureInfo("pt-BR");
                            CultureInfo inv = CultureInfo.InvariantCulture;

                            HashSet<string> codigosVistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                            TinSurface supTopo = ObterSuperficieTin(civilDb, tr, SuperficieTopoNome);
                            for (int r = firstDataRow; r <= ws.Dimension.End.Row; r++)
                            {
                                if (LinhaVazia(ws, r, 1, lastCol)) continue;

                                double x = 0.0;
                                double y = 0.0;
                                bool okX = TryGetDouble(ws.Cells[r, colX].Text, out x, pt, inv);
                                bool okY = TryGetDouble(ws.Cells[r, colY].Text, out y, pt, inv);
                                if (!okX || !okY)
                                {
                                    linhasSemCoord.Add(r);
                                    continue;
                                }

                                string codigoBruto = colCodigo > 0 ? ws.Cells[r, colCodigo].Text?.Trim() : string.Empty;
                                string codigoLimpo = LimparCodigoParaLayer(codigoBruto);
                                if (!string.Equals(codigoLimpo, "SEM_CODIGO", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (codigosVistos.Contains(codigoLimpo)) continue;
                                    codigosVistos.Add(codigoLimpo);
                                }

                                // Z da planilha
                                double zPlanilha = 0.0;
                                bool temZPlanilha = false;
                                if (colZ > 0) temZPlanilha = TryGetDouble(ws.Cells[r, colZ].Text, out zPlanilha, pt, inv);

                                // Cota topo e fundo da planilha
                                double cotaTopoM = 0.0;
                                bool temTopo = (colCotaTopo > 0) && TryGetDouble(ws.Cells[r, colCotaTopo].Text, out cotaTopoM, pt, inv);

                                double cotaFundoM = 0.0;
                                bool temFundo = (colCotaFundo > 0) && TryGetDouble(ws.Cells[r, colCotaFundo].Text, out cotaFundoM, pt, inv);

                                // Busca cota na superfície quando faltar topo/projeto
                                double zSup = double.NaN;
                                if (supTopo != null)
                                {
                                    try
                                    {
                                        zSup = supTopo.FindElevationAtXY(x, y);
                                    }
                                    catch { zSup = double.NaN; }
                                }

                                if (!temTopo)
                                {
                                    if (!double.IsNaN(zSup)) cotaTopoM = zSup;
                                    else if (temZPlanilha) cotaTopoM = zPlanilha;
                                    else cotaTopoM = 0.0; // fallback
                                }

                                if (!temFundo)
                                {
                                    double baseZ = !double.IsNaN(zSup) ? zSup : cotaTopoM;
                                    cotaFundoM = baseZ - 2.0;
                                }

                                // Elevação = topo definido
                                double elevacaoM = cotaTopoM;

                                // Diâmetro: tenta em m, depois polegadas; default 0,5 m
                                double diamM = 0.125;
                                double valTemp = 0.0;
                                if (colDiamM > 0 && TryGetDouble(ws.Cells[r, colDiamM].Text, out valTemp, pt, inv) && valTemp > 0) diamM = valTemp;
                                else if (colDiamPol > 0 && TryGetDouble(ws.Cells[r, colDiamPol].Text, out valTemp, pt, inv) && valTemp > 0)
                                {
                                    diamM = valTemp * 0.0254;
                                }
          ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
          ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

                                // Profundidade
                                double profM = cotaTopoM - cotaFundoM;
                                if (profM <= 0) profM = 0.25; // evita erro de extrusão

                                TiltimetroTools tilt = new TiltimetroTools();
                                ObjectId solFinalId = ObjectId.Null;    

                                // Criação
                                if (psetAltName == "DADOS TILTIMETROS")
                                {

                                    //solFinalId = TiltimetroTools.CriarTiltimetro3D(db, tr, ms, x, y, zSup);
                                }
                                else if(psetAltName == "DADOS INCLINOMETROS")
                                {
                                    try {


                                        TryGetDouble(ws.Cells[r, colProfM].Text, out profM, pt, inv);
                                        Piezometros piezometros = new Piezometros();
                                        ObjectId solidId = piezometros.CriarRetanguloExtrudadoNoTopoPiez(db, tr, ms, x, y, cotaTopoM, 0.1, 0.1, profM, "");
                                        //solFinalId = piezometros.CriarRetanguloExtrudadoNoTopoPiez(db, tr, ms, x, y, zSup, 0.5, 0.5, 0.5);
                                        docEditor.WriteMessage($"\nCota de topo: {cotaTopoM}");
                                        docEditor.WriteMessage($"\nCota de fundo: {cotaFundoM}");
                                        docEditor.WriteMessage($"\nProfundidade: {profM}");
                                        docEditor.WriteMessage($"\nCota superfície: {zSup}");
                                    }
                                    catch {
                                    
                                        docEditor.WriteMessage("\nErro ao criar inclinômetro na linha " + r.ToString());
                                        docEditor.WriteMessage($"\nCota de topo: {cotaTopoM}");
                                        docEditor.WriteMessage($"\nCota de fundo: {cotaFundoM}");
                                        docEditor.WriteMessage($"\nProfundidade: {profM}");
                                        docEditor.WriteMessage($"\nCota superfície: {zSup}");
                                    }
                                    

                                }
                                /*else if (psetAltName == "DADOS PIEZOMETROS") {
                                    if (cotaTopoM < (zSup))
                                        profM = zSup + 1 - cotaFundoM;
                                    Piezometros piezometros = new Piezometros();
                                    ObjectId solidId = Piezometros.CriarCilindroPiezometroExtrusao(db, tr, ms, x, y, cotaFundoM, 0.1, profM);
                                    //solFinalId = piezometros.CriarRetanguloExtrudadoNoTopoPiez(db, tr, ms, x, y, zSup, 0.8, 0.8, 0.3);
                                }
                                else if (psetAltName == "DADOS SISMÓGRAFOS")
                                {
                                    // padrão: cilindro

                                    solFinalId = CriarRetanguloExtrudadoNoTopo(db, tr, ms, x, y, zSup, 0.5, 0.5, 0.5, 0.0);

                                }
                                else if (psetAltName == "DADOS PLUVIÓGRAFOS")
                                {
                                    // padrão: cilindro
                                    PluviometroTools pluviometroTools = new PluviometroTools();
                                    //solFinalId = pluviometroTools.CriarBocalPluviometro(db, tr, ms, x, y, zSup, 0.16, 0.08, 0.08, 0.1, 0.12);

                                }
                                else if (sheetName.Contains("Triangular"))
                                {
                                    
                                    Piezometros piezometros = new Piezometros();
                                    //ObjectId solidId = CriarCilindroPorDiametroM(db, tr, ms, x, y, zSup, 0.4, 0.1);
                                    solFinalId = piezometros.CriarRetanguloExtrudadoNoTopoTriangular(db, tr, ms, x, y, cotaFundoM, .2, .2, profM);

                                }
                              else if (psetAltName == "DADOS INAS")
                                {
                                    // padrão: cilindro
                                    //if (cotaTopoM < (zSup))
                                    profM = zSup + 1 - cotaFundoM;
                                    Piezometros piezometros = new Piezometros();
                                    //ObjectId solidId = piezometros.CriarRetanguloExtrudadoNoTopoPiez(db, tr, ms, x, y, zSup, 0.05, 0.1, profM);
                                    solFinalId = piezometros.CriarRetanguloExtrudadoNoTopoPiez(db, tr, ms, x, y, zSup, 0.2, 0.2, 0.2);

                                }
                                else if (psetAltName == "DADOS MARCOS TOPOGRÁFICOS")
                                {

                                    Piezometros piezometros = new Piezometros();
                                    solFinalId = piezometros.CriarRetanguloExtrudadoNoTopoPiez(db, tr, ms, x, y, zSup, 0.3, 0.3, 0.2);

                                }
                               
                                else
                                {
                                    // padrão: cilindro
                                    Piezometros piezometros = new Piezometros();
                                    solFinalId = solFinalId = piezometros.CriarRetanguloExtrudadoNoTopoPiez(db, tr, ms, x, y, zSup, 1.0, 1.0, 0.4);
                                }*/

                                if (solFinalId.IsNull) continue;

                                Solid3d br = (Solid3d)tr.GetObject(solFinalId, OpenMode.ForWrite);

                                // Layer com cor ACI pela numeração da planilha
                                string layerNome = codigoLimpo;
                                layerNome = GarantirLayer(db, tr, layerNome, corAciPlanilha);

                                // ... depois de setar 'br.Layer = layerNome;'
                                // === ML com distribuição automática ===
                                string modeloTxt = (colModelo > 0) ? (ws.Cells[r, colModelo].Text ?? "").Trim() : "";
                                string rotulo = string.IsNullOrWhiteSpace(modeloTxt) ? codigoLimpo : (modeloTxt + "\n" + codigoLimpo);

                                Point3d anchor = new Point3d(x, y, cotaTopoM + 0.10);
                                int idxCell = BumpCell(cellCounts, x, y, grid);
                                Point3d posTexto = FanOffset(anchor, idxCell, baseR, stepR);

                                //CriarMultileader(db, tr, ms, anchor, posTexto, rotulo, layerNome);
                                pos++;


                                if (br != null && !br.ObjectId.IsNull) br.Layer = layerNome;

                                if (!br.ObjectId.IsNull && !psetDef.ObjectId.IsNull)
                                {
                                    PropertyDataServices.AddPropertySet(br, psetDef.ObjectId);
                                }
                                else
                                {
                                    docEditor.WriteMessage("\nALGUM DOS DOIS É NULO AQUI");
                                }

                                // XData
                                ResultBuffer rb = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, regAppName));
                                for (int c = 1; c <= lastCol; c++)
                                {
                                    string key = titulos[c - 1];
                                    if (string.IsNullOrWhiteSpace(key)) continue;
                                    string valTxt = ws.Cells[r, c].Text?.Trim();
                                    string kv = key + "=" + valTxt;
                                    rb.Add(new TypedValue(1000, kv));
                                }
                                Entity ent = (Entity)tr.GetObject(solFinalId, OpenMode.ForWrite);
                                ent.XData = rb;

                                // PSet por coluna
                                for (int c = 1; c <= lastCol; c++)
                                {
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

                                    if (!psetDef.Definitions.Contains(pdNew))
                                    {
                                        psetDef.Definitions.Add(pdNew);
                                    }
                                    else
                                    {
                                        ObjectId psId = PropertyDataServices.GetPropertySet(br, psetDef.ObjectId);
                                        PropertySet ps = (PropertySet)tr.GetObject(psId, OpenMode.ForWrite);
                                        int pid = ps.PropertyNameToId(titulo);
                                        ps.SetAt(pid, valor);
                                    }
                                }

                                inseridosTotal++;
                            }


                            tr.Commit();
                        }
                    }
                }

                if (linhasSemCoord.Count > 0)
                {
                    string lista = string.Join(", ", linhasSemCoord);
                    docEditor.WriteMessage($"\nLinhas ignoradas sem Latitude/Longitude: {lista}");
                }

                docEditor.WriteMessage($"\n{inseridosTotal} sólidos inseridos.");
            }
        }


        // ======= Derivações e parsing =======

        private static void DerivarNomesDePlanilha(string wsName, out string regAppName, out string psetAltName)
        {
            string baseNorm = Normalizar(wsName);
            string token = CriarTokenSeguro(wsName);

            if (baseNorm.Contains("piez"))
            {
                psetAltName = "DADOS PIEZOMETROS";
            }
            else if (baseNorm.Contains("inclino") || baseNorm.Contains("inclinomet"))
            {
                psetAltName = "DADOS INCLINOMETROS";
            }
            else if (baseNorm.Contains("Pluvió") || baseNorm.Contains("pluviografo"))
            {
                psetAltName = "DADOS PLUVIÓGRAFOS";
            }
            else if (baseNorm.Contains("medidor") || baseNorm.Contains("medidor de nivel"))
            {
                psetAltName = "DADOS INAS";
            }
            else if (baseNorm.Contains("marco") || baseNorm.Contains("marco topogr"))
            {
                psetAltName = "DADOS MARCOS TOPOGRÁFICOS";
            }
            else if (baseNorm.Contains("medidor de Vaz") || baseNorm.Contains("medidor de vazão_"))
            {
                psetAltName = "DADOS MEDIDORES DE VAZÃO";
            }
            else if (baseNorm.Contains("tiltí") || baseNorm.Contains("tiltimetro"))
            {
                psetAltName = "DADOS TILTIMETROS";
            }
            else if (baseNorm.Contains("pluviometros") || baseNorm.Contains("pluviometros_"))
            {
                psetAltName = "DADOS PLUVIÔMETROS";
            }
            else if (baseNorm.Contains("rad") || baseNorm.Contains("radar"))
            {
                psetAltName = "DADOS RADARES";
            }
            else if (baseNorm.Contains("came") || baseNorm.Contains("camera_"))
            {
                psetAltName = "DADOS CÂMERAS";
            }
            else if (baseNorm.Contains("geofone") || baseNorm.Contains("sismografo"))
            {
                psetAltName = "DADOS SISMÓGRAFOS";
            }
            else if (baseNorm.Contains("estacao") || baseNorm.Contains("topografica_robo"))
            {
                psetAltName = "DADOS ESTAÇÕES TOPOGRÁFICAS";
            }
            else
            {
                psetAltName = "DADOS IMPORTADOS";
            }

            regAppName = "PSD_3_" + token;
            if (regAppName.Length > 255) regAppName = regAppName.Substring(0, 255);
        }

        private static int ExtrairNumeroInicial(string name)
        {
            int i = 0;
            while (i < name.Length && char.IsWhiteSpace(name[i])) i++;
            StringBuilder sb = new StringBuilder();
            while (i < name.Length && char.IsDigit(name[i]))
            {
                sb.Append(name[i]);
                i++;
            }
            int val = 0;
            if (sb.Length > 0 && int.TryParse(sb.ToString(), out val))
            {
                if (val < 0) val = 0;
                if (val > 255) val = 255;
                return val;
            }
            return 7; // default ACI neutro
        }

        private static string CriarTokenSeguro(string s)
        {
            string noAcc = RemoverAcentos(s ?? string.Empty).ToUpperInvariant();
            StringBuilder sb = new StringBuilder(noAcc.Length);
            for (int i = 0; i < noAcc.Length; i++)
            {
                char ch = noAcc[i];
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else if (ch == '_' || ch == '-') sb.Append(ch);
                else sb.Append('_');
            }
            string res = sb.ToString();
            while (res.Contains("__")) res = res.Replace("__", "_");
            res = res.Trim('_');
            if (string.IsNullOrEmpty(res)) res = "PLANILHA";
            return res;
        }

        // ======= Geometria =======

        // Versão por diâmetro em metros
        private static ObjectId CriarCilindroPorDiametroM(
            Database db,
            Transaction tr,
            BlockTableRecord ms,
            double x,
            double y,
            double cotaFundoM,
            double diamM,
            double profundidadeM)
        {
            if (profundidadeM <= 0.0 || diamM <= 0.0) return ObjectId.Null;

            double raioM = diamM * 0.5;

            Circle circ = new Circle(new Point3d(x, y, cotaFundoM), Vector3d.ZAxis, raioM);
            ObjectId circId = ms.AppendEntity(circ);
            tr.AddNewlyCreatedDBObject(circ, true);

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

            Solid3d sol = new Solid3d();
            sol.SetDatabaseDefaults();
            sol.Extrude(reg, profundidadeM, 0);

            ObjectId solidId = ms.AppendEntity(sol);
            tr.AddNewlyCreatedDBObject(sol, true);

            reg.Erase();
            circ.Erase();

            return solidId;
        }

        public static ObjectId CriarRetanguloExtrudadoNoTopo(
            Database db,
            Transaction tr,
            BlockTableRecord ms,
            double x,
            double y,
            double elevacaoM,
            double largura,
            double comprimento,
            double profundidadeM,
            double diametroM)
        {

            if (diametroM != 0)
            {
                Circle circ = new Circle(new Point3d(x, y, elevacaoM-0.1), Vector3d.ZAxis, diametroM);
                ObjectId circId = ms.AppendEntity(circ);
                tr.AddNewlyCreatedDBObject(circ, true);

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

                Solid3d sol = new Solid3d();
                sol.SetDatabaseDefaults();
                sol.Extrude(reg, profundidadeM, 0);

                ObjectId solidId = ms.AppendEntity(sol);
                tr.AddNewlyCreatedDBObject(sol, true);
                reg.Erase();
                circ.Erase();

                return solidId;
            }
            else
            {
                // continua para retângulo

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
                Solid3d sol = new Solid3d();
                sol.SetDatabaseDefaults();
                sol.Extrude(reg, profundidadeM, 0);

                ObjectId solidId = ms.AppendEntity(sol);
                tr.AddNewlyCreatedDBObject(sol, true);

                reg.Erase();
                pl.Erase();

                return solidId;
            }
        }

        public static ObjectId UnirSolidos(Database db, Transaction tr, ObjectId cilindroId, ObjectId caixaId)
        {
            if (cilindroId.IsNull || caixaId.IsNull) return ObjectId.Null;

            Solid3d solCilindro = (Solid3d)tr.GetObject(cilindroId, OpenMode.ForWrite);
            Solid3d solCaixa = (Solid3d)tr.GetObject(caixaId, OpenMode.ForWrite);

            solCilindro.BooleanOperation(BooleanOperationType.BoolUnite, solCaixa);
            solCaixa.Erase();

            return solCilindro.ObjectId;
        }

        // ======= Helpers Excel/CAD =======

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

        public static ObjectId CriarMultileader(
            Database db, Transaction tr, BlockTableRecord ms,
            Point3d ancoragem, Point3d posTexto, string texto, string layerName)
        {
            MText mt = new MText();
            mt.SetDatabaseDefaults();
            mt.Contents = string.IsNullOrWhiteSpace(texto) ? "\n" : texto;
            mt.TextHeight = 1;
            mt.Location = posTexto;
            mt.Attachment = AttachmentPoint.MiddleLeft;

            //ObjectId mtId = ms.AppendEntity(mt);
            //tr.AddNewlyCreatedDBObject(mt, true);

            MLeader ml = new MLeader();
            ml.SetDatabaseDefaults();
            int ld = ml.AddLeader();
            int ln = ml.AddLeaderLine(ld);
            ml.AddFirstVertex(ln, ancoragem);                 // ponta
            ml.AddLastVertex(ln, posTexto.Add(new Vector3d(-1, 0, 0))); // dogleg junto ao texto
            ml.Layer = layerName;
            ml.ContentType = ContentType.MTextContent;
            ml.MText = mt;
            ml.EnableDogleg = true;
            ml.DoglegLength = 2.0;
            ml.LandingGap = 1.0;
            ml.ArrowSize = 2.0;
            ml.ExtendLeaderToText = true;

            ObjectId mlId = ms.AppendEntity(ml);
            tr.AddNewlyCreatedDBObject(ml, true);
            return mlId;
        }

        private static int ResolveColuna(Dictionary<string, int> map, IEnumerable<string> candidatos)
        {
            foreach (string cand in candidatos)
            {
                string norm = Normalizar(cand);
                int col = -1;
                if (map.TryGetValue(norm, out col)) return col;
            }
            return -1;
        }

        private static string Normalizar(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            string lower = s.Trim().ToLowerInvariant();
            string noAccents = RemoverAcentos(lower);
            return noAccents.Replace("  ", " ");
        }

        private static string RemoverAcentos(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            string formD = input.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder(formD.Length);
            for (int i = 0; i < formD.Length; i++)
            {
                System.Globalization.UnicodeCategory uc = CharUnicodeInfo.GetUnicodeCategory(formD[i]);
                if (uc != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(formD[i]);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static bool TryGetDouble(string text, out double value, CultureInfo pt, CultureInfo inv)
        {
            value = 0.0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            double parsed = 0.0;
            if (double.TryParse(text, NumberStyles.Float, pt, out parsed)) { value = parsed; return true; }
            if (double.TryParse(text, NumberStyles.Float, inv, out parsed)) { value = parsed; return true; }
            string swap = text.Contains(',') ? text.Replace(".", "").Replace(',', '.') : text.Replace(",", "");
            if (double.TryParse(swap, NumberStyles.Float, inv, out parsed)) { value = parsed; return true; }
            return false;
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

        private static string LimparCodigoParaLayer(string codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return "SEM_CODIGO";
            string s = codigo.Trim().ToUpperInvariant();
            s = s.Replace(' ', '_').Replace('/', '_').Replace('\\', '_');
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
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

        private static string GarantirLayer(Database db, Transaction tr, string layerName, int aciColor)
        {
            if (aciColor < 0) aciColor = 0;
            if (aciColor > 255) aciColor = 255;

            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName))
            {
                // Atualiza cor se necessário
                LayerTableRecord ltrExist = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
                if (ltrExist.Color.ColorIndex != aciColor)
                {
                    ltrExist.Color = Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, (short)aciColor);
                }
                return layerName;
            }


            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord();
            ltr.Name = layerName;
            ltr.IsOff = false;
            ltr.IsLocked = false;
            ltr.Color = Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, (short)aciColor);
            ObjectId id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            lt.DowngradeOpen();
            return layerName;
        }

        // ======= Civil 3D =======

        private static TinSurface ObterSuperficieTin(CivilDocument civilDb, Transaction tr, string nome)
        {
            try
            {

                foreach (ObjectId sid in civilDb.GetSurfaceIds())
                {
                    if (sid.IsNull) return null;
                    TinSurface s = (TinSurface)tr.GetObject(sid, OpenMode.ForRead);
                    if (s.Name == nome)
                        return s;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        // ======= UI =======

        private class SheetPickerForm : Form
        {
            private ListBox _list;
            private Button _ok;
            private Button _cancel;

            private SheetPickerForm(string[] items, string title)
            {
                this.Text = title;
                this.StartPosition = FormStartPosition.CenterScreen;
                this.Width = 420;
                this.Height = 360;
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.MaximizeBox = false;
                this.MinimizeBox = false;

                _list = new ListBox();
                _list.Dock = DockStyle.Top;
                _list.Height = 260;
                _list.SelectionMode = SelectionMode.One;
                _list.Items.AddRange(items);
                if (_list.Items.Count > 0) _list.SelectedIndex = 0;
                this.Controls.Add(_list);

                FlowLayoutPanel pnl = new FlowLayoutPanel();
                pnl.Dock = DockStyle.Bottom;
                pnl.Height = 48;

                _ok = new Button();
                _ok.Text = "OK";
                _ok.Width = 120;
                _ok.Click += (s, e) => { this.DialogResult = DialogResult.OK; this.Close(); };

                _cancel = new Button();
                _cancel.Text = "Cancelar";
                _cancel.Width = 120;
                _cancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

                pnl.Controls.Add(_ok);
                pnl.Controls.Add(_cancel);
                this.Controls.Add(pnl);
            }


            public static string Pick(string[] items, string title)
            {
                string escolha = string.Empty;
                using (SheetPickerForm frm = new SheetPickerForm(items, title))
                {
                    DialogResult dr = Application.ShowModalDialog(frm);
                    if (dr == DialogResult.OK && frm._list.SelectedItem != null)
                    {
                        escolha = frm._list.SelectedItem.ToString();
                    }
                }
                return escolha;
            }




        }
    }
}
