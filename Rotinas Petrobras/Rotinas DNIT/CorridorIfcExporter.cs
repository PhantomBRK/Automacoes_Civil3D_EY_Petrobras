using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.BoundaryRepresentation;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
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
        public class CorridorIfcExporter
        {
            [CommandMethod("EXPORTAR_IFC_CORREDORES")]
            public void ExportarIfcCorredores()
            {
                Document civilDoc = Manager.DocCad;
                CivilDocument civilDb = Manager.DocCivil;
                Editor docEditor = Manager.DocEditor;
                Database db = civilDoc.Database;

                try
                {
                    PromptStringOptions pso = new PromptStringOptions(
                        "\nInforme o caminho completo do arquivo IFC (ex: C:\\\\Temp\\\\modelo.ifc): ");
                    pso.AllowSpaces = true;

                    PromptResult prFile = docEditor.GetString(pso);
                    if (prFile.Status != PromptStatus.OK)
                    {
                        return;
                    }

                    string ifcFilePath = prFile.StringResult;
                    if (string.IsNullOrWhiteSpace(ifcFilePath))
                    {
                        docEditor.WriteMessage("\nCaminho inválido.");
                        return;
                    }

                    List<IfcElementBase> elements = new List<IfcElementBase>();

                    using (Transaction trans = db.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)trans.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord ms = (BlockTableRecord)trans.GetObject(
                            bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                        foreach (ObjectId entId in ms)
                        {
                            Entity ent = (Entity)trans.GetObject(entId, OpenMode.ForRead);
                            Solid3d solid = ent as Solid3d;
                            if (solid == null)
                            {
                                continue;
                            }

                            string layerName = solid.Layer;
                            if (!this.IsCorridorSolidLayer(layerName))
                            {
                                continue;
                            }

                            double volume = 0.0;
                            double area = 0.0;
                            double length = 0.0;
                            double width = 0.0;
                            double thickness = 0.0;

                            try
                            {
                                Solid3dMassProperties massProps = solid.MassProperties;
                                volume = massProps.Volume;           // volume do sólido
                            }
                            catch (System.Exception)
                            {
                            }

                            try
                            {
                                area = solid.Area;                    // área de superfície do sólido
                            }
                            catch (System.Exception)
                            {
                            }

                            try
                            {
                                Extents3d extents = solid.GeometricExtents;

                                double dx = Math.Abs(extents.MaxPoint.X - extents.MinPoint.X);
                                double dy = Math.Abs(extents.MaxPoint.Y - extents.MinPoint.Y);
                                double dz = Math.Abs(extents.MaxPoint.Z - extents.MinPoint.Z);

                                double[] dims = new double[3];
                                dims[0] = dx;
                                dims[1] = dy;
                                dims[2] = dz;

                                Array.Sort(dims);

                                thickness = dims[0];
                                width = dims[1];
                                length = dims[2];
                            }
                            catch (Exception)
                            {
                            }

                            string name = this.GetNameFromSolid(solid);
                            string corridorName = this.GetCorridorNameFromLayer(layerName);

                            IfcElementBase element = this.CreateIfcElementFromLayer(layerName);

                            element.Name = name;
                            element.CorridorName = corridorName;
                            element.LayerName = layerName;
                            element.Length = length;
                            element.Width = width;
                            element.Thickness = thickness;
                            element.Area = area;
                            element.Volume = volume;

                            elements.Add(element);
                        }

                        trans.Commit();
                    }

                    if (elements.Count == 0)
                    {
                        docEditor.WriteMessage("\nNenhum sólido de corredor encontrado para exportação.");
                        return;
                    }

                    string projectName = civilDoc.Name;

                    IfcFileBuilder ifcBuilder = new IfcFileBuilder();
                    string ifcContent = ifcBuilder.BuildIfcFile(elements, projectName);

                    File.WriteAllText(ifcFilePath, ifcContent, System.Text.Encoding.UTF8);

                    docEditor.WriteMessage("\nArquivo IFC gerado: " + ifcFilePath);
                }
                catch (System.Exception ex)
                {
                    docEditor.WriteMessage("\nErro ao exportar IFC: " + ex.Message);
                }
            }

            private bool IsCorridorSolidLayer(string layerName)
            {
                if (string.IsNullOrEmpty(layerName))
                {
                    return false;
                }

                if (layerName.StartsWith("COR", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (layerName.StartsWith("SOLID_CORRIDOR_", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }

            public static string AbrirDialogoSelecaoArquivo(string filtro)
        {
            string caminhoArquivo = null;
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = filtro;
                dlg.Multiselect = false;
                dlg.Title = "Selecione o DWG de destino";
                if (dlg.ShowDialog() == DialogResult.OK) { caminhoArquivo = dlg.FileName; }
            }
            return caminhoArquivo;
        }

        

        private string GetCorridorNameFromLayer(string layerName)
        {
            if (string.IsNullOrEmpty(layerName))
            {
                return string.Empty;
            }

            // Ajuste esse parser conforme o padrão que você usa
            // Exemplo esperado: COR_<NOME_CORREDOR>_ALGUMA_COISA
            string corridorName = layerName;

            string upper = layerName.ToUpperInvariant();
            int idxCor = upper.IndexOf("COR_",
                StringComparison.OrdinalIgnoreCase);

            if (idxCor >= 0)
            {
                string temp = layerName.Substring(idxCor + 4);
                int idxNextUnderscore = temp.IndexOf("_", StringComparison.Ordinal);
                if (idxNextUnderscore > 0)
                {
                    corridorName = temp.Substring(0, idxNextUnderscore);
                }
                else
                {
                    corridorName = temp;
                }
            }

            return corridorName;
        }

        private string GetNameFromSolid(Solid3d solid)
        {
            if (solid == null)
            {
                return string.Empty;
            }

            string name = "SOLID_" + solid.Handle.ToString();
            return name;
        }

        private IfcElementBase CreateIfcElementFromLayer(string layerName)
        {
            if (string.IsNullOrEmpty(layerName))
            {
                IfcProxyElement proxy = new IfcProxyElement();
                return proxy;
            }

            string upper = layerName.ToUpperInvariant();

            if (upper.Contains("PAVIMENT") || upper.Contains("REVEST"))
            {
                IfcSlabElement slab = new IfcSlabElement();
                return slab;
            }

            if (upper.Contains("BASE") || upper.Contains("SUBLEITO") || upper.Contains("REFORCO"))
            {
                IfcSlabElement slab = new IfcSlabElement();
                return slab;
            }

            if (upper.Contains("DRENAG") || upper.Contains("SARJETA") || upper.Contains("VALA"))
            {
                IfcPipeSegmentElement pipe = new IfcPipeSegmentElement();
                return pipe;
            }

            if (upper.Contains("MURO") || upper.Contains("TALUDE"))
            {
                IfcWallElement wall = new IfcWallElement();
                return wall;
            }

            IfcProxyElement proxyElement = new IfcProxyElement();
            return proxyElement;
        }
    }

    public abstract class IfcElementBase
    {
        public string Name { get; set; }
        public string CorridorName { get; set; }
        public string LayerName { get; set; }

        public double Length { get; set; }
        public double Width { get; set; }
        public double Thickness { get; set; }
        public double Area { get; set; }
        public double Volume { get; set; }

        protected IfcElementBase()
        {
            this.Name = string.Empty;
            this.CorridorName = string.Empty;
            this.LayerName = string.Empty;
        }

        public abstract string GetIfcClassName();
    }

    public class IfcSlabElement : IfcElementBase
    {
        public override string GetIfcClassName()
        {
            return "IFCSLAB";
        }
    }

    public class IfcWallElement : IfcElementBase
    {
        public override string GetIfcClassName()
        {
            return "IFCWALL";
        }
    }

    public class IfcPipeSegmentElement : IfcElementBase
    {
        public override string GetIfcClassName()
        {
            return "IFCPIPESEGMENT";
        }
    }

    public class IfcProxyElement : IfcElementBase
    {
        public override string GetIfcClassName()
        {
            return "IFCBUILDINGELEMENTPROXY";
        }
    }

    public class IfcFileBuilder
    {
        private int _currentId;
        private List<string> _lines;

        public IfcFileBuilder()
        {
            this._currentId = 1;
            this._lines = new List<string>();
        }

        public string BuildIfcFile(List<IfcElementBase> elements, string projectName)
        {
            this._lines.Clear();
            this._currentId = 1;

            this.AddHeader(projectName);

            foreach (IfcElementBase element in elements)
            {
                this.AddIfcProduct(element);
            }

            this.AddFooter();

            string content = string.Join(Environment.NewLine, this._lines);
            return content;
        }

        private void AddHeader(string projectName)
        {
            string safeName = this.EscapeString(projectName);
            string nowStr = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

            this._lines.Add("ISO-10303-21;");
            this._lines.Add("HEADER;");
            this._lines.Add("FILE_DESCRIPTION(('ViewDefinition [CoordinationView_V2.0]'),'2;1');");
            this._lines.Add("FILE_NAME('" + safeName + "','" + nowStr + "',(''),('')," +
                            "'Autodesk Civil 3D IFC Exporter','AutomacoesCivil3D','');");
            this._lines.Add("FILE_SCHEMA(('IFC4'));");
            this._lines.Add("ENDSEC;");
            this._lines.Add("DATA;");
        }

        private void AddIfcProduct(IfcElementBase info)
        {
            int localPlacementId = this.GetNextId();
            int productDefShapeId = this.GetNextId();
            int elementId = this.GetNextId();
            int quantitySetId = this.GetNextId();

            // Local placement (placeholder)
            this._lines.Add("#" + localPlacementId.ToString(CultureInfo.InvariantCulture) +
                            "= IFLOCALPLACEMENT($,$);");

            // Product representation (placeholder, sem geometria)
            this._lines.Add("#" + productDefShapeId.ToString(CultureInfo.InvariantCulture) +
                            "= IFCPRODUCTDEFINITIONSHAPE($,$,());");

            string ifcName = this.EscapeString(info.Name);
            string ifcClassName = info.GetIfcClassName();

            string corridorName = string.IsNullOrEmpty(info.CorridorName)
                ? "CORRIDOR"
                : info.CorridorName;

            string qSetName = this.EscapeString("Qto_" + corridorName);

            string lengthStr = this.FormatMeasure(info.Length);
            string widthStr = this.FormatMeasure(info.Width);
            string thicknessStr = this.FormatMeasure(info.Thickness);
            string areaStr = this.FormatMeasure(info.Area);
            string volumeStr = this.FormatMeasure(info.Volume);

            int qLengthId = this.GetNextId();
            int qWidthId = this.GetNextId();
            int qThicknessId = this.GetNextId();
            int qAreaId = this.GetNextId();
            int qVolumeId = this.GetNextId();

            this._lines.Add("#" + qLengthId.ToString(CultureInfo.InvariantCulture) +
                            "= IFCQUANTITYLENGTH('Length',$,$," + lengthStr + ");");
            this._lines.Add("#" + qWidthId.ToString(CultureInfo.InvariantCulture) +
                            "= IFCQUANTITYLENGTH('Width',$,$," + widthStr + ");");
            this._lines.Add("#" + qThicknessId.ToString(CultureInfo.InvariantCulture) +
                            "= IFCQUANTITYLENGTH('Thickness',$,$," + thicknessStr + ");");
            this._lines.Add("#" + qAreaId.ToString(CultureInfo.InvariantCulture) +
                            "= IFCQUANTITYAREA('Area',$,$," + areaStr + ");");
            this._lines.Add("#" + qVolumeId.ToString(CultureInfo.InvariantCulture) +
                            "= IFCQUANTITYVOLUME('Volume',$,$," + volumeStr + ");");

            this._lines.Add("#" + quantitySetId.ToString(CultureInfo.InvariantCulture) +
                            "= IFCELEMENTQUANTITY($,'" + qSetName + "',$,$,(" +
                            "#" + qLengthId.ToString(CultureInfo.InvariantCulture) + "," +
                            "#" + qWidthId.ToString(CultureInfo.InvariantCulture) + "," +
                            "#" + qThicknessId.ToString(CultureInfo.InvariantCulture) + "," +
                            "#" + qAreaId.ToString(CultureInfo.InvariantCulture) + "," +
                            "#" + qVolumeId.ToString(CultureInfo.InvariantCulture) + "));");

            string ifcType = string.IsNullOrEmpty(ifcClassName)
                ? "IFCBUILDINGELEMENTPROXY"
                : ifcClassName;

            this._lines.Add("#" + elementId.ToString(CultureInfo.InvariantCulture) +
                            "= " + ifcType +
                            "($,'" + ifcName + "',$,$," +
                            "#" + localPlacementId.ToString(CultureInfo.InvariantCulture) + "," +
                            "#" + productDefShapeId.ToString(CultureInfo.InvariantCulture) + ",$,$);");

            int relDefinesByPropertiesId = this.GetNextId();

            this._lines.Add("#" + relDefinesByPropertiesId.ToString(CultureInfo.InvariantCulture) +
                            "= IFCRELDEFINESBYPROPERTIES($,$,$,$,(#" +
                            elementId.ToString(CultureInfo.InvariantCulture) + "),#" +
                            quantitySetId.ToString(CultureInfo.InvariantCulture) + ");");
        }

        private void AddFooter()
        {
            this._lines.Add("ENDSEC;");
            this._lines.Add("END-ISO-10303-21;");
        }

        private int GetNextId()
        {
            int value = this._currentId;
            this._currentId++;
            return value;
        }

        private string EscapeString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            string escaped = value.Replace("'", "''");
            return escaped;
        }

        private string FormatMeasure(double value)
        {
            string formatted = value.ToString("0.########", CultureInfo.InvariantCulture);
            return formatted;
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
                        Path.GetFullPath(d.Name).Equals(alvo, StringComparison.OrdinalIgnoreCase))
                        return d;
                }
                catch { }
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


        
    }
}