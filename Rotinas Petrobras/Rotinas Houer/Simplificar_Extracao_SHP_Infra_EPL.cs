using System;
using System.Collections.Generic;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Label = Autodesk.Civil.DatabaseServices.Label;
using Color = Autodesk.AutoCAD.Colors.Color;
using Autodesk.AutoCAD.Colors;

namespace AutomacoesCivil3D.Rotinas_Houer
{
    public class Simplificar_Extracao_SHP_Infra_EPL
    {
        [CommandMethod("SIMPL_DOMINIO_INFRA")]
        public void SimplificarDominiosParaInfraworks()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database db = civilDoc.Database;

            try
            {
                // Tolerância inicial em metros
                PromptDoubleOptions pdoTol = new PromptDoubleOptions(
                    "\nTolerância inicial em metros para simplificação [ex: 0.20]: ");
                pdoTol.AllowZero = false;
                pdoTol.AllowNegative = false;
                pdoTol.DefaultValue = 0.20;
                PromptDoubleResult resTol = docEditor.GetDouble(pdoTol);
                if (resTol.Status != PromptStatus.OK)
                {
                    return;
                }
                double toleranciaInicial = resTol.Value;

                // Máximo de vértices (InfraWorks ~1000)
                PromptIntegerOptions pioMax = new PromptIntegerOptions(
                    "\nNúmero máximo de vértices permitidos no polígono: ");
                pioMax.AllowZero = false;
                pioMax.AllowNegative = false;
                pioMax.DefaultValue = 1000;
                PromptIntegerResult resMax = docEditor.GetInteger(pioMax);
                if (resMax.Status != PromptStatus.OK)
                {
                    return;
                }
                int maxVertices = resMax.Value;

                // Seleção das polilinhas fechadas
                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\nSelecione as polilinhas fechadas da faixa de domínio:";

                TypedValue[] tv =
                {
                    new TypedValue((int)DxfCode.Operator, "<OR"),
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                    new TypedValue((int)DxfCode.Operator, "OR>")
                };
                SelectionFilter filtro = new SelectionFilter(tv);
                PromptSelectionResult psr = docEditor.GetSelection(pso, filtro);
                if (psr.Status != PromptStatus.OK)
                {
                    return;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    string layerSimplif = "INFRA_DOMINIO_SIMPLIF";

                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (!lt.Has(layerSimplif))
                    {
                        lt.UpgradeOpen();
                        LayerTableRecord ltr = new LayerTableRecord();
                        ltr.Name = layerSimplif;
                        ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
                        ObjectId ltrId = lt.Add(ltr);
                        tr.AddNewlyCreatedDBObject(ltr, true);
                    }

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    foreach (SelectedObject so in psr.Value)
                    {
                        if (so == null)
                        {
                            continue;
                        }

                        Polyline plOriginal = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Polyline;
                        if (plOriginal == null)
                        {
                            continue;
                        }

                        if (!plOriginal.Closed)
                        {
                            continue;
                        }

                        // Cria cópia da polyline no layer simplificado
                        Polyline plCopia = new Polyline();
                        plCopia.SetDatabaseDefaults();
                        plCopia.Layer = layerSimplif;

                        int n = plOriginal.NumberOfVertices;
                        for (int i = 0; i < n; i++)
                        {
                            Point2d pt = plOriginal.GetPoint2dAt(i);
                            // Aqui ignoro bulge para não complicar (InfraWorks já vai receber segmentos retos)
                            plCopia.AddVertexAt(plCopia.NumberOfVertices, pt, 0.0, 0.0, 0.0);
                        }
                        plCopia.Closed = true;

                        ObjectId copiaId = ms.AppendEntity(plCopia);
                        tr.AddNewlyCreatedDBObject(plCopia, true);

                        // Simplifica a cópia até bater o limite
                        SimplificarPolyline(plCopia, maxVertices, toleranciaInicial);
                    }

                    tr.Commit();
                }

                docEditor.WriteMessage(
                    "\nPolilinhas copiadas para 'INFRA_DOMINIO_SIMPLIF' e simplificadas para exportação.");
            }
            catch (Exception ex)
            {
                docEditor.WriteMessage("\nErro em SIMPL_DOMINIO_INFRA: " + ex.Message);
            }
        }

        private static void SimplificarPolyline(Polyline pl, int maxVertices, double toleranciaInicial)
        {
            if (pl == null)
            {
                return;
            }

            double tolerancia = toleranciaInicial;
            bool finalizado = false;

            while (!finalizado)
            {
                int count = pl.NumberOfVertices;
                if (count <= maxVertices)
                {
                    break;
                }

                List<Point2d> pts = new List<Point2d>();
                for (int i = 0; i < count; i++)
                {
                    Point2d p = pl.GetPoint2dAt(i);
                    pts.Add(p);
                }

                List<Point2d> simplificado = DouglasPeucker(pts, tolerancia);

                if (simplificado.Count < 3)
                {
                    // Não faz sentido um polígono com menos de 3 vértices
                    break;
                }

                pl.UpgradeOpen();

                for (int i = pl.NumberOfVertices - 1; i >= 0; i--)
                {
                    pl.RemoveVertexAt(i);
                }

                for (int i = 0; i < simplificado.Count; i++)
                {
                    Point2d p = simplificado[i];
                    pl.AddVertexAt(i, p, 0.0, 0.0, 0.0);
                }

                pl.Closed = true;

                if (pl.NumberOfVertices <= maxVertices)
                {
                    finalizado = true;
                }
                else
                {
                    // Aumenta tolerância progressivamente
                    tolerancia *= 1.5;
                    if (tolerancia > toleranciaInicial * 20.0)
                    {
                        // trava de segurança para não detonar a geometria
                        finalizado = true;
                    }
                }
            }
        }

        private static List<Point2d> DouglasPeucker(List<Point2d> pontos, double epsilon)
        {
            if (pontos == null)
            {
                throw new ArgumentNullException("pontos");
            }

            if (pontos.Count < 3)
            {
                return new List<Point2d>(pontos);
            }

            int primeiro = 0;
            int ultimo = pontos.Count - 1;

            List<int> indicesManter = new List<int>();
            indicesManter.Add(primeiro);
            indicesManter.Add(ultimo);

            DouglasPeuckerRecursivo(pontos, primeiro, ultimo, epsilon, indicesManter);

            indicesManter.Sort();

            List<Point2d> resultado = new List<Point2d>();
            foreach (int idx in indicesManter)
            {
                resultado.Add(pontos[idx]);
            }

            return resultado;
        }

        private static void DouglasPeuckerRecursivo(
            List<Point2d> pontos,
            int primeiro,
            int ultimo,
            double epsilon,
            List<int> indicesManter)
        {
            if (ultimo <= primeiro + 1)
            {
                return;
            }

            double distMax = 0.0;
            int idxMax = primeiro;

            Point2d p0 = pontos[primeiro];
            Point2d p1 = pontos[ultimo];

            for (int i = primeiro + 1; i < ultimo; i++)
            {
                double dist = DistanciaPerpendicular(pontos[i], p0, p1);
                if (dist > distMax)
                {
                    distMax = dist;
                    idxMax = i;
                }
            }

            if (distMax > epsilon)
            {
                indicesManter.Add(idxMax);
                DouglasPeuckerRecursivo(pontos, primeiro, idxMax, epsilon, indicesManter);
                DouglasPeuckerRecursivo(pontos, idxMax, ultimo, epsilon, indicesManter);
            }
        }

        private static double DistanciaPerpendicular(Point2d p, Point2d l0, Point2d l1)
        {
            if (l0.IsEqualTo(l1))
            {
                return p.GetDistanceTo(l0);
            }

            double dx = l1.X - l0.X;
            double dy = l1.Y - l0.Y;

            double numerador = Math.Abs(dy * p.X - dx * p.Y + l1.X * l0.Y - l1.Y * l0.X);
            double denominador = Math.Sqrt(dx * dx + dy * dy);

            return numerador / denominador;
        }
    }
}
