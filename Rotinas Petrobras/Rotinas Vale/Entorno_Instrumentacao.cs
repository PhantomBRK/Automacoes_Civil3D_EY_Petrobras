using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using OfficeOpenXml;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Label = Autodesk.Civil.DatabaseServices.Label;
using Color = Autodesk.AutoCAD.Colors.Color;
using FlowDirection = System.Windows.Forms.FlowDirection;
using SelectionMode = System.Windows.Forms.SelectionMode;

namespace AutomacoesCivil3D
{
    public class AmbSolidosAsBuilt
    {
        // ======= Parâmetros =======
        private const string PrefixoNome = "AMB_";
        private const string RegAppName = "AMB_SOLIDOS";
        private const double LadoQuadradoM = 5.0;
        private const double HalfM = LadoQuadradoM * 0.5;     // 2.5
        private const double OffsetUpDownM = 0.5;            // +0.5 / -0.5
        private const double PassoAmostraM = 0.50;           // 0.50m => 11x11 pontos (patch 5m)
        private const int CorAciLayer = 2;

        [CommandMethod("AMB_SOLIDOS_5X5")]
        public static void GerarSolidosAmb5x5DaSuperficieAsBuilt()
        {
            Document civilDoc = Manager.DocCad;
            Editor docEditor = Manager.DocEditor;
            CivilDocument civilDb = Manager.DocCivil;
            Database db = civilDoc.Database;

            // 1) Selecionar superfície As Built
            ObjectId surfaceId = PedirTinSurface(docEditor);
            if (surfaceId.IsNull) return;

            // 2) Selecionar planilha
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
                StartModeForm.Choice modo = StartModeForm.Pick();
                if (modo == StartModeForm.Choice.Cancel)
                {
                    docEditor.WriteMessage("\nCancelado.");
                    return;
                }

                ExcelWorkbook wbPreview = pkgPreview.Workbook;
                string[] nomes = wbPreview.Worksheets.Select(w => w.Name).ToArray();

                List<string> sheetsAlvo = new List<string>();
                if (modo == StartModeForm.Choice.Uma)
                {
                    string escolha = SheetPickerForm.Pick(nomes, "Escolha a planilha");
                    if (string.IsNullOrWhiteSpace(escolha))
                    {
                        docEditor.WriteMessage("\nCancelado.");
                        return;
                    }
                    sheetsAlvo.Add(escolha);
                }
                else
                {
                    sheetsAlvo.AddRange(nomes);
                }

                int criadosTotal = 0;
                List<string> ignorados = new List<string>();
                HashSet<string> codigosVistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                using (ExcelPackage pkg = new ExcelPackage(new FileInfo(ofd.FileName)))
                {
                    ExcelWorkbook wb = pkg.Workbook;

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        TinSurface asBuilt = (TinSurface)tr.GetObject(surfaceId, OpenMode.ForRead);

                        RegistrarRegApp(db, tr, RegAppName);

                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        foreach (string sheetName in sheetsAlvo)
                        {
                            ExcelWorksheet ws = wb.Worksheets[sheetName];
                            if (ws == null)
                            {
                                docEditor.WriteMessage($"\nPlanilha '{sheetName}' não encontrada.");
                                continue;
                            }

                            int headerRow = 3;
                            int firstDataRow = headerRow + 1;

                            Dictionary<string, int> colIndex = MapearColunas(ws, headerRow);

                            int colX = ResolveColuna(colIndex, new[] { "longitude utm", "longitude (utm)", "easting", "coord x", "x", "utm e" });
                            int colY = ResolveColuna(colIndex, new[] { "latitude utm", "latitude (utm)", "northing", "coord y", "y", "utm n" });
                            int colCodigo = ResolveColuna(colIndex, new[] { "código", "codigo", "cod", "instrumento", "id" });

                            if (colX < 1 || colY < 1 || colCodigo < 1)
                            {
                                docEditor.WriteMessage($"\n[{sheetName}] Colunas mínimas não encontradas (X/Y/Código).");
                                continue;
                            }

                            int lastRow = ws.Dimension?.End.Row ?? 0;
                            int lastCol = ws.Dimension?.End.Column ?? 0;
                            if (lastRow < firstDataRow || lastCol < 1) continue;

                            CultureInfo pt = CultureInfo.GetCultureInfo("pt-BR");
                            CultureInfo inv = CultureInfo.InvariantCulture;

                            for (int r = firstDataRow; r <= lastRow; r++)
                            {
                                if (LinhaVazia(ws, r, 1, lastCol)) continue;

                                string xTxt = ws.Cells[r, colX].Text?.Trim();
                                string yTxt = ws.Cells[r, colY].Text?.Trim();
                                string codTxt = ws.Cells[r, colCodigo].Text?.Trim();

                                double x = 0.0;
                                double y = 0.0;

                                if (!TryGetDouble(xTxt, out x, pt, inv) || !TryGetDouble(yTxt, out y, pt, inv))
                                {
                                    ignorados.Add($"{sheetName}:L{r} (sem X/Y)");
                                    continue;
                                }

                                string codigoLimpo = LimparCodigoParaLayer(codTxt);
                                if (string.IsNullOrWhiteSpace(codigoLimpo)) codigoLimpo = "SEM_CODIGO";

                                string nomeAmb = codigoLimpo.StartsWith(PrefixoNome, StringComparison.OrdinalIgnoreCase)
                                    ? codigoLimpo
                                    : (PrefixoNome + codigoLimpo);

                                if (codigosVistos.Contains(nomeAmb)) continue;
                                codigosVistos.Add(nomeAmb);

                                // Layer = "AMB_..."
                                GarantirLayer(db, tr, nomeAmb, CorAciLayer);

                                ObjectId solidId = ObjectId.Null;
                                try
                                {
                                    solidId = CriarSolidoPatchAsBuilt5x5(db, tr, ms, asBuilt, x, y, PassoAmostraM, OffsetUpDownM, nomeAmb, RegAppName);

                                }
                                catch (Autodesk.Civil.PointNotOnEntityException)
                                {
                                    ignorados.Add($"{sheetName}:L{r} ({nomeAmb} fora da superfície)");
                                    continue;
                                }
                                catch (System.Exception ex)
                                {
                                    ignorados.Add($"{sheetName}:L{r} ({nomeAmb}) ERRO: {ex.Message}");
                                    continue;
                                }

                                if (solidId.IsNull) continue;

                                Entity ent = (Entity)tr.GetObject(solidId, OpenMode.ForWrite);
                                ent.Layer = nomeAmb;
                                ent.ColorIndex = 256; // ByLayer

                                ResultBuffer rb = new ResultBuffer(
                                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, RegAppName),
                                    new TypedValue(1000, "NOME=" + nomeAmb),
                                    new TypedValue(1000, "CODIGO=" + codigoLimpo),
                                    new TypedValue(1000, "SHEET=" + sheetName)
                                );
                                ent.XData = rb;

                                criadosTotal++;
                            }
                        }

                        tr.Commit();
                    }
                }

                if (ignorados.Count > 0)
                {
                    string lista = string.Join("\n", ignorados.Take(50));
                    docEditor.WriteMessage($"\nIgnorados/erros (até 50):\n{lista}");
                    if (ignorados.Count > 50) docEditor.WriteMessage($"\n... +{ignorados.Count - 50} itens");
                }

                docEditor.WriteMessage($"\n{criadosTotal} sólidos AMB_... criados.");
            }
        }

        private static ObjectId PedirTinSurface(Editor docEditor)
        {
            PromptEntityOptions peo = new PromptEntityOptions("\nSelecione a superfície As Built (TIN): ");
            peo.SetRejectMessage("\nSelecione uma TinSurface.");
            peo.AddAllowedClass(typeof(TinSurface), exactMatch: true);

            PromptEntityResult per = docEditor.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return ObjectId.Null;

            return per.ObjectId;
        }

        /// <summary>
        /// Cria sólido 5x5 baseado na superfície (amostragem em grid) e espessura +/-offset (via Thicken ambos os lados).
        /// </summary>
        private static ObjectId CriarSolidoPatchAsBuilt5x5(
            Database db,
            Transaction tr,
            BlockTableRecord ms,
            TinSurface surf,
            double xCentro,
            double yCentro,
            double passoAmostraM,
            double offsetUpDownM,
            string layerName,
            string regAppName
        )
        {
            ObjectId meshId = ObjectId.Null;
            ObjectId surfId = ObjectId.Null;

            try
            {
                if (passoAmostraM <= 0.0) passoAmostraM = 0.5;

                int div = (int)Math.Round(LadoQuadradoM / passoAmostraM, MidpointRounding.AwayFromZero);
                if (div < 2) div = 2;

                double step = LadoQuadradoM / (double)div;
                int n = div + 1;

                // 1) Amostragem em memória (se falhar aqui, não cria nada no DWG)
                Point3dCollection vertices = new Point3dCollection();
                Int32Collection faces = new Int32Collection();

                for (int iy = 0; iy < n; iy++)
                {
                    double y = (yCentro - HalfM) + (iy * step);
                    for (int ix = 0; ix < n; ix++)
                    {
                        double x = (xCentro - HalfM) + (ix * step);
                        double z = surf.FindElevationAtXY(x, y); // pode lançar exceção se fora
                        vertices.Add(new Point3d(x, y, z));
                    }
                }

                for (int iy = 0; iy < div; iy++)
                {
                    for (int ix = 0; ix < div; ix++)
                    {
                        int v00 = (iy * n) + ix;
                        int v10 = v00 + 1;
                        int v01 = v00 + n;
                        int v11 = v01 + 1;

                        faces.Add(3); faces.Add(v00); faces.Add(v10); faces.Add(v11);
                        faces.Add(3); faces.Add(v00); faces.Add(v11); faces.Add(v01);
                    }
                }

                // 2) Cria mesh já no layer certo (e com XData pra “limpeza” se precisar)
                SubDMesh mesh = new SubDMesh();
                mesh.SetDatabaseDefaults();
                mesh.Layer = layerName;
                mesh.ColorIndex = 256;
                mesh.SetSubDMesh(vertices, faces, 0);

                meshId = ms.AppendEntity(mesh);
                tr.AddNewlyCreatedDBObject(mesh, true);
                SetXDataAmb(mesh, regAppName, layerName);

                // 3) Converte para Surface já no layer certo
                Autodesk.AutoCAD.DatabaseServices.Surface patchSurface = mesh.ConvertToSurface(convertAsSmooth: false, optimize: true);
                patchSurface.SetDatabaseDefaults();
                patchSurface.Layer = layerName;
                patchSurface.ColorIndex = 256;

                surfId = ms.AppendEntity(patchSurface);
                tr.AddNewlyCreatedDBObject(patchSurface, true);
                SetXDataAmb(patchSurface, regAppName, layerName);

                // 4) Thicken => +0,5 / -0,5
                double thickness = offsetUpDownM * 2.0;
                Solid3d sol = patchSurface.Thicken(thickness, bothSides: true);
                sol.SetDatabaseDefaults();
                sol.Layer = layerName;
                sol.ColorIndex = 256;

                ObjectId solidId = ms.AppendEntity(sol);
                tr.AddNewlyCreatedDBObject(sol, true);
                SetXDataAmb(sol, regAppName, layerName);

                return solidId;
            }
            catch
            {
                // fallback: box 5x5, na cota do centro, espessura 2*offset
                return CriarSolidoBox5x5(db, tr, ms, surf, xCentro, yCentro, offsetUpDownM, layerName, regAppName);
            }

            finally
            {
                // SEMPRE apaga intermediários pra não sobrar no Layer 0 (ou em layer nenhum)
                ApagarSeExiste(tr, surfId);
                ApagarSeExiste(tr, meshId);
            }
        }

        private static void ApagarSeExiste(Transaction tr, ObjectId id)
        {
            if (id.IsNull) return;

            DBObject obj = (DBObject)tr.GetObject(id, OpenMode.ForWrite, false);
            if (obj == null) return;
            if (obj.IsErased) return;

            obj.Erase();
        }

        private static void SetXDataAmb(DBObject obj, string regAppName, string layerName)
        {
            // Requer que RegistrarRegApp(...) já tenha sido chamado
            ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, regAppName),
                new TypedValue(1000, "AMB=1"),
                new TypedValue(1000, "LAYER=" + layerName)
            );
            obj.XData = rb;
        }


        // ======= Helpers Excel/CAD (mesma linha da sua classe) =======

        private static Dictionary<string, int> MapearColunas(ExcelWorksheet ws, int headerRow)
        {
            Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int lastCol = ws.Dimension?.End.Column ?? 0;
            for (int c = 1; c <= lastCol; c++)
            {
                string txt = ws.Cells[headerRow, c].Text;
                string norm = Normalizar(txt);
                if (string.IsNullOrWhiteSpace(norm)) continue;
                if (!map.ContainsKey(norm)) map.Add(norm, c);
            }
            return map;
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
            while (noAccents.Contains("  ")) noAccents = noAccents.Replace("  ", " ");
            return noAccents;
        }

        private static string RemoverAcentos(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            string formD = input.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder(formD.Length);
            for (int i = 0; i < formD.Length; i++)
            {
                System.Globalization.UnicodeCategory uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(formD[i]);
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

        private static ObjectId CriarSolidoBox5x5(
        Database db,
        Transaction tr,
        BlockTableRecord ms,
        TinSurface surf,
        double xCentro,
        double yCentro,
        double offsetUpDownM,
        string layerName,
        string regAppName
        )
        {
            double zCentro = 0.0;

            try
            {
                zCentro = surf.FindElevationAtXY(xCentro, yCentro);
            }
            catch
            {
                return ObjectId.Null;
            }

            double thickness = offsetUpDownM * 2.0; // 1.0m => +0.5 / -0.5
            if (thickness <= 0.0) thickness = 1.0;

            Solid3d sol = new Solid3d();
            sol.SetDatabaseDefaults();
            sol.CreateBox(LadoQuadradoM, LadoQuadradoM, thickness);

            // Base do box em (x-2.5, y-2.5, z-0.5)
            Vector3d desloc = new Vector3d(
                xCentro - 0,
                yCentro - 0,
                zCentro - offsetUpDownM
            );
            sol.TransformBy(Matrix3d.Displacement(desloc));

            sol.Layer = layerName;
            sol.ColorIndex = 256; // ByLayer

            ObjectId solidId = ms.AppendEntity(sol);
            tr.AddNewlyCreatedDBObject(sol, true);

            SetXDataAmb(sol, regAppName, layerName);

            return solidId;
        }

        private static string GarantirLayer(Database db, Transaction tr, string layerName, int aciColor)
        {
            if (aciColor < 0) aciColor = 0;
            if (aciColor > 255) aciColor = 255;

            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName))
            {
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

        // ======= UI (mesma ideia da sua classe) =======

        private class StartModeForm : Form
        {
            public enum Choice { Uma, Todas, Cancel }
            public Choice Result = Choice.Cancel;

            public StartModeForm()
            {
                this.Text = "AMB - Sólidos 5x5"; this.Width = 360; this.Height = 160;
                FlowLayoutPanel p = new FlowLayoutPanel() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12) };

                Button b1 = new Button() { Text = "Percorrer TODAS as planilhas", Width = 300 };
                Button b2 = new Button() { Text = "Escolher UMA planilha", Width = 300 };
                Button b3 = new Button() { Text = "Cancelar", Width = 300 };

                b1.Click += (s, e) => { Result = Choice.Todas; this.DialogResult = DialogResult.OK; };
                b2.Click += (s, e) => { Result = Choice.Uma; this.DialogResult = DialogResult.OK; };
                b3.Click += (s, e) => { Result = Choice.Cancel; this.DialogResult = DialogResult.Cancel; };

                p.Controls.Add(b1); p.Controls.Add(b2); p.Controls.Add(b3);
                this.Controls.Add(p);
            }

            public static Choice Pick()
            {
                using (StartModeForm f = new StartModeForm())
                {
                    DialogResult dr = Application.ShowModalDialog(f);
                    return (dr == DialogResult.OK) ? f.Result : Choice.Cancel;
                }
            }
        }

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

                FlowLayoutPanel panel = new FlowLayoutPanel();
                panel.Dock = DockStyle.Bottom;
                panel.Height = 60;
                panel.FlowDirection = FlowDirection.RightToLeft;
                panel.Padding = new Padding(10);

                _ok = new Button() { Text = "OK", Width = 90 };
                _cancel = new Button() { Text = "Cancelar", Width = 90 };

                _ok.Click += (s, e) => { this.DialogResult = DialogResult.OK; };
                _cancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; };

                panel.Controls.Add(_ok);
                panel.Controls.Add(_cancel);

                this.Controls.Add(panel);
            }

            public static string Pick(string[] items, string title)
            {
                using (SheetPickerForm f = new SheetPickerForm(items, title))
                {
                    DialogResult dr = Application.ShowModalDialog(f);
                    if (dr != DialogResult.OK) return null;
                    return f._list.SelectedItem?.ToString();
                }
            }
        }
    }
}
