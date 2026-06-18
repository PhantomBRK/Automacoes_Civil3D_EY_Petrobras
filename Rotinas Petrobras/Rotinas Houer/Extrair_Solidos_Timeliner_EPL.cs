// Refs: AutoCAD + Civil 3D + AEC PropertyData
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Label = Autodesk.Civil.DatabaseServices.Label;
using Color = Autodesk.AutoCAD.Colors.Color;
using Excel = Microsoft.Office.Interop.Excel;
using WinForms = System.Windows.Forms;

namespace AutomacoesCivil3D.EXTRAIR_SOLIDOS_CORREDORES
{
    public class SolidosCorredores
    {
        [CommandMethod("Timeliner")]
        public void Timeliner()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database docData = Manager.DocData;

            SimpleProgressForm progressForm = null;

            try
            {
                // DWG de destino
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

                // Planilha de segmentos (A, D, E, G, H)
                List<SegmentInfo> segmentos = null;
                List<double> splitStations = null;
                ObjectId alinhamentoLinearId = ObjectId.Null;

                string caminhoPlanilha = AbrirDialogoSelecaoArquivo(
                    "Planilha de Segmentos (*.xlsx;*.xls)|*.xlsx;*.xls",
                    "Selecione a planilha de segmentos");

                if (!string.IsNullOrWhiteSpace(caminhoPlanilha))
                {
                    segmentos = LerSegmentosDePlanilha(caminhoPlanilha, docEditor);
                    if (segmentos != null && segmentos.Count > 0)
                    {
                        splitStations = segmentos
                            .Select((SegmentInfo s) => s.KmFinal)
                            .Distinct()
                            .OrderBy((double v) => v)
                            .ToList();

                        // Alinhamento de referência do estaqueamento linear (TH)
                        PromptEntityOptions peoAlinhamento = new PromptEntityOptions(
                            "\nSelecione o alinhamento linear de referência (estaqueamento oficial dos TH):");
                        peoAlinhamento.SetRejectMessage("\nSelecione apenas objetos do tipo Alignment.");
                        peoAlinhamento.AddAllowedClass(typeof(Alignment), true);

                        PromptEntityResult perAlinhamento = docEditor.GetEntity(peoAlinhamento);
                        if (perAlinhamento.Status == PromptStatus.OK)
                        {
                            alinhamentoLinearId = perAlinhamento.ObjectId;
                        }
                        else
                        {
                            docEditor.WriteMessage(
                                "\nNenhum alinhamento linear selecionado. Segmentos não serão aplicados.");
                            segmentos = null;
                            splitStations = null;
                        }
                    }
                }

                ObjectIdCollection idsExportados = new ObjectIdCollection();

                int totalCorredores = civilDb.CorridorCollection.Count;
                int indiceCorredor = 0;

                if (totalCorredores > 0)
                {
                    progressForm = new SimpleProgressForm("Exportação de Sólidos de Corredores");
                    progressForm.SetMaximum(totalCorredores);
                    Application.ShowModelessDialog(progressForm);
                }

                // FASE 1: gerar sólidos, criar splits e aplicar PSETs na origem
                using (Transaction tr = docData.TransactionManager.StartTransaction())
                {

                    Alignment alinhamentoLinear = null;
                    if (!alinhamentoLinearId.IsNull)
                    {
                        alinhamentoLinear = (Alignment)tr.GetObject(
                            alinhamentoLinearId,
                            OpenMode.ForRead);
                    }



                    DictionaryPropertySetDefinitions dictionary =
                        new DictionaryPropertySetDefinitions(docData);

                    string propSetNameA = "A - Dados do Projeto";
                    string propSetNameB = "B - Informações dos Objetos e Elementos";
                    string propSetNameC = "C - Propriedades Fisicas dos Objetos e Elementos";
                    string propSetNameD = "D - Propriedades Geográficas";
                    string propSetNameE = "COORDENAÇÃO";
                    string propSetNameF = "Corridor Shape Information";

                    ObjectId propSetIdA = dictionary.GetAt(propSetNameA);
                    ObjectId propSetIdB = dictionary.GetAt(propSetNameB);
                    ObjectId propSetIdC = dictionary.GetAt(propSetNameC);
                    ObjectId propSetIdD = dictionary.GetAt(propSetNameD);
                    ObjectId propSetIdE = dictionary.GetAt(propSetNameE);
                    ObjectId propSetIdF = dictionary.GetAt(propSetNameF);

                    PropertySetDefinition propSetDefA =
                        (PropertySetDefinition)tr.GetObject(propSetIdA, OpenMode.ForWrite);
                    PropertySetDefinition propSetDefB =
                        (PropertySetDefinition)tr.GetObject(propSetIdB, OpenMode.ForWrite);
                    PropertySetDefinition propSetDefC =
                        (PropertySetDefinition)tr.GetObject(propSetIdC, OpenMode.ForWrite);
                    PropertySetDefinition propSetDefD =
                        (PropertySetDefinition)tr.GetObject(propSetIdD, OpenMode.ForWrite);
                    PropertySetDefinition propSetDefE =
                        (PropertySetDefinition)tr.GetObject(propSetIdE, OpenMode.ForWrite);
                    PropertySetDefinition propSetDefF =
                        (PropertySetDefinition)tr.GetObject(propSetIdF, OpenMode.ForWrite);

                    PropertySets propertySets = new PropertySets();

                    // Mapa RegionGUID (por corredor) -> Segmento/Ano
                    Dictionary<string, SegmentInfo> regionToSegment =
                        new Dictionary<string, SegmentInfo>(StringComparer.OrdinalIgnoreCase);

                    foreach (ObjectId corridorId in civilDb.CorridorCollection)
                    {
                        indiceCorredor++;

                        Corridor corridor =
                            (Corridor)tr.GetObject(
                                corridorId,
                                (segmentos != null && segmentos.Count > 0)
                                    ? OpenMode.ForWrite
                                    : OpenMode.ForRead);
                        string layerBaseName = corridor.Name;

                        if (corridor.IsReferenceObject)
                        {
                            if (progressForm != null)
                            {
                                progressForm.UpdateProgress(
                                    string.Format(
                                        "Ignorando corredor de referência ({0}/{1})...",
                                        indiceCorredor,
                                        totalCorredores),
                                    indiceCorredor);
                            }
                            continue;
                        }

                        if (progressForm != null)
                        {
                            progressForm.UpdateProgress(
                                string.Format(
                                    "Processando corredor {0}/{1}: {2}",
                                    indiceCorredor,
                                    totalCorredores,
                                    corridor.Name),
                                indiceCorredor);
                        }

                        // SPLIT por Kms finais da planilha + mapa região -> TH (via estaqueamento linear)
                        if (segmentos != null &&
                            segmentos.Count > 0 &&
                            splitStations != null &&
                            splitStations.Count > 0 &&
                            alinhamentoLinear != null)
                        {
                            SplitCorridorByStations(corridor, splitStations);
                            BuildRegionSegmentMap(
                                corridor,
                                segmentos,
                                regionToSegment,
                                alinhamentoLinear,
                                tr);
                        }

                        string[] shapeCodes = corridor.GetShapeCodes();
                        string[] linkCodes = corridor.GetLinkCodes();
                        string[] included = shapeCodes
                            .Concat(linkCodes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

                        ExportCorridorSolidsParams p = new ExportCorridorSolidsParams
                        {
                            IncludedCodes = included,
                            ExportLinks = true,
                            ExportShapes = true
                        };

                        ObjectIdCollection exp = corridor.ExportSolids(p, docData);

                        foreach (ObjectId id in exp)
                        {
                            if (id.ObjectClass.Name == "AcDb3dSolid")
                            {
                                Solid3d s =
                                    (Solid3d)tr.GetObject(id, OpenMode.ForWrite);

                                //DefinirLayerPorCorredor(s, docData, tr, layerBaseName);

                                PropertyDataServices.AddPropertySet(s, propSetIdA);
                                PropertyDataServices.AddPropertySet(s, propSetIdB);
                                PropertyDataServices.AddPropertySet(s, propSetIdC);
                                PropertyDataServices.AddPropertySet(s, propSetIdD);
                                PropertyDataServices.AddPropertySet(s, propSetIdE);
                                PropertyDataServices.AddPropertySet(s, propSetIdF);

                                propertySets.PSetSolid(s, docData, tr);

                                if (segmentos != null &&
                                    segmentos.Count > 0 &&
                                    regionToSegment.Count > 0)
                                {
                                    AtribuirSegmentoEAnos(
                                        s,
                                        tr,
                                        propSetIdA,
                                        propSetIdB,
                                        corridor.Name,
                                        regionToSegment);
                                }

                                idsExportados.Add(s.ObjectId);
                            }

                            if (id.ObjectClass.Name == "AcDb3dSolid")
                            {
                                Solid3d s =
                                    (Solid3d)tr.GetObject(id, OpenMode.ForWrite);

                                //DefinirLayerPorCorredor(s, docData, tr, layerBaseName);

                                PropertyDataServices.AddPropertySet(s, propSetIdA);
                                PropertyDataServices.AddPropertySet(s, propSetIdB);
                                PropertyDataServices.AddPropertySet(s, propSetIdC);
                                PropertyDataServices.AddPropertySet(s, propSetIdD);
                                PropertyDataServices.AddPropertySet(s, propSetIdE);
                                PropertyDataServices.AddPropertySet(s, propSetIdF);

                                propertySets.PSetSolid(s, docData, tr);

                                if (segmentos != null &&
                                    segmentos.Count > 0 &&
                                    regionToSegment.Count > 0)
                                {
                                    AtribuirSegmentoEAnos(
                                        s,
                                        tr,
                                        propSetIdA,
                                        propSetIdB,
                                        corridor.Name,
                                        regionToSegment);
                                }

                                idsExportados.Add(s.ObjectId);
                            }
                        }
                    }

                    tr.Commit();
                }

                if (progressForm != null)
                {
                    progressForm.UpdateProgress(
                        "Copiando objetos para o DWG de destino...",
                        totalCorredores);
                }

                // FASE 2: clonar para destino
                Civil3DObjectCopier2 copiadora = new Civil3DObjectCopier2();
                copiadora.CopyObjectsBetweenDrawings(idsExportados, caminhoDestino, null, docData);

                if (progressForm != null)
                {
                    progressForm.UpdateProgress(
                        "Removendo sólidos temporários do desenho de origem...",
                        totalCorredores);
                }

                // FASE 3: apagar na origem
                using (Transaction trErase = docData.TransactionManager.StartTransaction())
                {
                    ExclusaoObjetos ex = new ExclusaoObjetos();
                    ex.ApagarSolid3d(idsExportados, trErase);
                    trErase.Commit();
                }

                if (progressForm != null)
                {
                    progressForm.UpdateProgress("Exportação concluída.", totalCorredores);
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
            finally
            {
                if (progressForm != null && !progressForm.IsDisposed)
                {
                    progressForm.Close();
                    progressForm.Dispose();
                }
            }
        }

        // =========================================================
        // Helpers genéricos
        // =========================================================

        private static void DefinirLayerPorCorredor(
            Entity ent,
            Database db,
            Transaction tr,
            string layerBaseName)
        {
            if (ent == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(layerBaseName))
            {
                return;
            }

            LayerTable layerTable =
                (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (!layerTable.Has(layerBaseName))
            {
                layerTable.UpgradeOpen();

                LayerTableRecord layerRecord = new LayerTableRecord();
                layerRecord.Name = layerBaseName;

                ObjectId layerId = layerTable.Add(layerRecord);
                tr.AddNewlyCreatedDBObject(layerRecord, true);
            }

            ent.Layer = layerBaseName;
        }


        private static Document GarantirDocumentoAberto(string caminho)
        {
            string alvo = Path.GetFullPath(caminho);
            DocumentCollection docs = Application.DocumentManager;

            foreach (Document d in docs)
            {
                try
                {
                    if (!string.IsNullOrEmpty(d.Name) &&
                        Path.GetFullPath(d.Name)
                            .Equals(alvo, StringComparison.OrdinalIgnoreCase))
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

            using (WinForms.OpenFileDialog dlg = new WinForms.OpenFileDialog())
            {
                dlg.Filter = filtro;
                dlg.Multiselect = false;
                dlg.Title = titulo;

                if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                {
                    caminhoArquivo = dlg.FileName;
                }
            }

            return caminhoArquivo;
        }

        // =========================================================
        // Leitura da planilha de segmentos
        // =========================================================

        private static List<SegmentInfo> LerSegmentosDePlanilha(string caminho, Editor ed)
        {
            List<SegmentInfo> segmentos = new List<SegmentInfo>();

            Excel.Application app = null;
            Excel.Workbook wb = null;

            try
            {
                app = new Excel.Application();
                wb = app.Workbooks.Open(caminho);
                Excel._Worksheet ws = (Excel._Worksheet)wb.Sheets[1];
                Excel.Range used = ws.UsedRange;
                int rowCount = used.Rows.Count;

                for (int r = 2; r <= rowCount; r++)
                {
                    Excel.Range cNome = (Excel.Range)used.Cells[r, 1];
                    Excel.Range cKmIni = (Excel.Range)used.Cells[r, 4];
                    Excel.Range cKmFim = (Excel.Range)used.Cells[r, 5];
                    Excel.Range cAnoIni = (Excel.Range)used.Cells[r, 7];
                    Excel.Range cAnoFim = (Excel.Range)used.Cells[r, 8];

                    object nomeObj = cNome.Value2;
                    object kmIniObj = cKmIni.Value2;
                    object kmFimObj = cKmFim.Value2;
                    object anoIniObj = cAnoIni.Value2;
                    object anoFimObj = cAnoFim.Value2;

                    if (nomeObj == null || kmIniObj == null || kmFimObj == null)
                    {
                        continue;
                    }

                    string nome = nomeObj.ToString();
                    double kmIni = ConverterParaDouble(kmIniObj);
                    double kmFim = ConverterParaDouble(kmFimObj);

                    if (double.IsNaN(kmIni) || double.IsNaN(kmFim))
                    {
                        continue;
                    }

                    int anoIni = ConverterParaInt(anoIniObj);
                    int anoFim = ConverterParaInt(anoFimObj);

                    SegmentInfo seg = new SegmentInfo
                    {
                        Segmento = nome,
                        KmInicial = kmIni,
                        KmFinal = kmFim,
                        AnoInicio = anoIni,
                        AnoTermino = anoFim
                    };

                    segmentos.Add(seg);
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nErro ao ler planilha de segmentos: {ex.Message}");
            }
            finally
            {
                if (wb != null)
                {
                    wb.Close(false);
                }
                if (app != null)
                {
                    app.Quit();
                }
            }

            segmentos.Sort((a, b) => a.KmInicial.CompareTo(b.KmInicial));
            return segmentos;
        }

        private static double ConverterParaDouble(object valor)
        {
            if (valor == null)
            {
                return double.NaN;
            }

            if (valor is double)
            {
                return (double)valor;
            }

            if (valor is int)
            {
                return Convert.ToDouble(valor);
            }

            string s = valor.ToString();
            if (string.IsNullOrWhiteSpace(s))
            {
                return double.NaN;
            }

            string s2 = s.ToUpperInvariant();
            s2 = s2.Replace("KM", string.Empty);
            s2 = s2.Replace(" ", string.Empty);
            s2 = s2.Replace("+", string.Empty);

            double d;
            if (double.TryParse(
                    s2,
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out d))
            {
                return d;
            }
            if (double.TryParse(
                    s2,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out d))
            {
                return d;
            }

            return double.NaN;
        }

        private static int ConverterParaInt(object valor)
        {
            if (valor == null)
            {
                return 0;
            }

            if (valor is double)
            {
                return Convert.ToInt32((double)valor);
            }

            if (valor is int)
            {
                return (int)valor;
            }

            string s = valor.ToString();
            if (int.TryParse(s, out int result))
            {
                return result;
            }

            return 0;
        }

        // =========================================================
        // Split de regiões por estaca (km final dos segmentos)
        // =========================================================

        private static void SplitCorridorByStations(Corridor corridor, List<double> splitStations)
        {
            if (splitStations == null || splitStations.Count == 0)
            {
                return;
            }

            const double tol = 0.01;

            foreach (double estaca in splitStations)
            {
                bool splitFeito = false;

                foreach (Baseline baseline in corridor.Baselines)
                {
                    BaselineRegionCollection regioes = baseline.BaselineRegions;

                    foreach (BaselineRegion regiao in regioes)
                    {
                        double inicio = regiao.StartStation;
                        double fim = regiao.EndStation;

                        if (estaca > inicio + tol && estaca < fim - tol)
                        {
                            try
                            {
                                BaselineRegion novaRegiao = regiao.Split(estaca);
                            }
                            catch
                            {
                            }

                            splitFeito = true;
                            break;
                        }
                    }

                    if (splitFeito)
                    {
                        break;
                    }
                }
            }

            try
            {
                corridor.Rebuild();
            }
            catch
            {
            }
        }

        // =========================================================
        // Mapa RegionGUID -> Segmento (usando estaqueamento linear)
        // =========================================================

        private static void BuildRegionSegmentMap(
            Corridor corridor,
            List<SegmentInfo> segmentos,
            Dictionary<string, SegmentInfo> regionToSegment,
            Alignment alinhamentoLinear,
            Transaction tr)
        {
            if (segmentos == null || segmentos.Count == 0)
            {
                return;
            }
            if (regionToSegment == null)
            {
                return;
            }
            if (alinhamentoLinear == null)
            {
                return;
            }

            const double tol = 0.01;

            foreach (Baseline baseline in corridor.Baselines)
            {
                Alignment alinhamentoLocal = null;
                try
                {
                    if (!baseline.AlignmentId.IsNull)
                    {
                        alinhamentoLocal = (Alignment)tr.GetObject(
                            baseline.AlignmentId,
                            OpenMode.ForRead);
                    }
                }
                catch
                {
                    alinhamentoLocal = null;
                }

                BaselineRegionCollection regioes = baseline.BaselineRegions;

                foreach (BaselineRegion regiao in regioes)
                {
                    double inicioLocal = regiao.StartStation;
                    double fimLocal = regiao.EndStation;
                    double meioLocal = (inicioLocal + fimLocal) / 2.0;

                    double estacaLinear = double.NaN;

                    try
                    {
                        // Ponto médio da região no alinhamento do próprio baseline
                        if (alinhamentoLocal != null)
                        {
                            double e = 0.0;
                            double n = 0.0;

                            alinhamentoLocal.PointLocation(
                                meioLocal,
                                0.0,
                                ref e,
                                ref n); // estação/offset -> E/N

                            double off = 0.0;
                            alinhamentoLinear.StationOffset(
                                e,
                                n,
                                ref estacaLinear,
                                ref off); // E/N -> estação/offset no alinhamento linear
                        }
                        else
                        {
                            // Fallback: usa a própria estação da região
                            estacaLinear = meioLocal;
                        }
                    }
                    catch
                    {
                        // Se der erro na projeção, cai pro meio local mesmo
                        estacaLinear = meioLocal;
                    }

                    if (double.IsNaN(estacaLinear))
                    {
                        continue;
                    }

                    SegmentInfo seg = EncontrarSegmentoPorStation(segmentos, estacaLinear);
                    if (seg != null)
                    {
                        string key = corridor.Name + "|" + regiao.RegionGUID.ToString();
                        regionToSegment[key] = seg;
                    }
                }
            }
        }


        private static SegmentInfo EncontrarSegmentoPorStation(
            List<SegmentInfo> segmentos,
            double station)
        {
            if (segmentos == null)
            {
                return null;
            }

            const double tol = 0.01;

            foreach (SegmentInfo seg in segmentos)
            {
                if (station >= seg.KmInicial - tol &&
                    station <= seg.KmFinal + tol)
                {
                    return seg;
                }
            }

            return null;
        }

        // =========================================================
        // Preencher PSET A com Segmento / Ano Inicio / Ano Termino
        // =========================================================

        private static void AtribuirSegmentoEAnos(
            Entity ent,
            Transaction tr,
            ObjectId propSetIdA,
            ObjectId propSetIdB,
            string nomeCorredor,
            Dictionary<string, SegmentInfo> regionToSegment)
        {
            if (ent == null)
            {
                return;
            }
            if (propSetIdA.IsNull || propSetIdB.IsNull)
            {
                return;
            }
            if (regionToSegment == null || regionToSegment.Count == 0)
            {
                return;
            }

            ObjectId psBId = PropertyDataServices.GetPropertySet(ent, propSetIdB);
            if (psBId.IsNull)
            {
                return;
            }

            PropertySet psB = (PropertySet)tr.GetObject(psBId, OpenMode.ForRead);
            int idxRegionGUID = psB.PropertyNameToId("RegionName");
            if (idxRegionGUID == -1)
            {
                return;
            }

            object rawGuid = psB.GetAt(idxRegionGUID, ent);
            if (rawGuid == null)
            {
                return;
            }

            string guid = rawGuid.ToString();
            string key = nomeCorredor + "|" + guid;

            if (!regionToSegment.TryGetValue(key, out SegmentInfo seg) || seg == null)
            {
                return;
            }

            ObjectId psAId = PropertyDataServices.GetPropertySet(ent, propSetIdA);
            if (psAId.IsNull)
            {
                return;
            }

            PropertySet psA = (PropertySet)tr.GetObject(psAId, OpenMode.ForWrite);

            SetPropertyIfExists(psA, "Segmento", seg.Segmento ?? string.Empty);
            /*SetPropertyIfExists(psA, "Ano Inicio", seg.AnoInicio.ToString());
            SetPropertyIfExists(psA, "Ano Termino", seg.AnoTermino.ToString());
            // alternativos, se tiver usado underscore na definição:
            SetPropertyIfExists(psA, "Ano_Inicio", seg.AnoInicio.ToString());
            SetPropertyIfExists(psA, "Ano_Termino", seg.AnoTermino.ToString());*/
        }

        private static void SetPropertyIfExists(PropertySet pset, string propName, object value)
        {
            if (pset == null)
            {
                return;
            }

            int id = pset.PropertyNameToId(propName);
            if (id == -1)
            {
                return;
            }

            try
            {
                pset.SetAt(id, value);
            }
            catch
            {
            }
        }
    }

    internal class SegmentInfo
    {
        public string Segmento { get; set; }
        public double KmInicial { get; set; }
        public double KmFinal { get; set; }
        public int AnoInicio { get; set; }
        public int AnoTermino { get; set; }
    }

    // Form simples de progresso
    internal class SimpleProgressForm : WinForms.Form
    {
        private readonly WinForms.Label _label;
        private readonly WinForms.ProgressBar _progressBar;

        internal SimpleProgressForm(string title)
        {
            Text = title;
            FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            StartPosition = WinForms.FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Width = 420;
            Height = 130;

            _label = new WinForms.Label();
            _label.Left = 10;
            _label.Top = 10;
            _label.Width = 380;
            _label.Height = 20;
            _label.Text = "Iniciando...";

            _progressBar = new WinForms.ProgressBar();
            _progressBar.Left = 10;
            _progressBar.Top = 40;
            _progressBar.Width = 380;
            _progressBar.Height = 20;
            _progressBar.Minimum = 0;
            _progressBar.Maximum = 1;
            _progressBar.Value = 0;
            _progressBar.Style = WinForms.ProgressBarStyle.Continuous;

            Controls.Add(_label);
            Controls.Add(_progressBar);
        }

        internal void SetMaximum(int max)
        {
            if (max < 1)
            {
                max = 1;
            }

            _progressBar.Maximum = max;
            _progressBar.Value = 0;
            _progressBar.Step = 1;

            WinForms.Application.DoEvents();
        }

        internal void UpdateProgress(string text, int value)
        {
            if (IsDisposed)
            {
                return;
            }

            if (!string.IsNullOrEmpty(text))
            {
                _label.Text = text;
            }

            if (value < _progressBar.Minimum)
            {
                value = _progressBar.Minimum;
            }
            if (value > _progressBar.Maximum)
            {
                value = _progressBar.Maximum;
            }

            _progressBar.Value = value;

            _progressBar.Refresh();
            _label.Refresh();

            WinForms.Application.DoEvents();
        }
    }

    // Guard anti-reentrância para ApplyToEntity (mantido do original)
    internal static class IfcApplyGuard
    {
        [ThreadStatic] internal static bool Busy;
    }
}
