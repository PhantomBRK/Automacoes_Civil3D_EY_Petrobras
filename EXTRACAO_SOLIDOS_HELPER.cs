using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
// Se for usar também Civil 3D em outras partes:
// using Autodesk.Civil;
// using Autodesk.Civil.DatabaseServices;

namespace AutomacoesCivil3D
{
    public static class PolylinePointInPolygon
    {
        // Versão com Tolerance (compatível com seu uso anterior)
        public static bool IsInside(Polyline pl, Point2d pt, Tolerance tol, double chordTol = 0.25)
        {
            return IsInside(pl, pt, tol.EqualPoint, chordTol);
        }

        // Versão direta com double de tolerância de borda
        public static bool IsInside(Polyline pl, Point2d pt, double boundaryTol = 1e-6, double chordTol = 0.25)
        {
            if (pl == null) { throw new ArgumentNullException(nameof(pl)); }
            if (chordTol <= 0.0) { chordTol = 0.25; }

            // 1) Rejeição rápida por extents (2D)
            Extents3d extents3d = pl.GeometricExtents;
            if (pt.X < extents3d.MinPoint.X - boundaryTol || pt.X > extents3d.MaxPoint.X + boundaryTol ||
                pt.Y < extents3d.MinPoint.Y - boundaryTol || pt.Y > extents3d.MaxPoint.Y + boundaryTol)
            {
                return false;
            }

            // 2) Se estiver na borda (dentro da tolerância), considere "dentro" quando includeBoundary=true
            Point3d test3d = new Point3d(pt.X, pt.Y, 0.0);
            Point3d closest3d = pl.GetClosestPointTo(test3d, false);
            double dEdge = closest3d.DistanceTo(test3d);
            if (dEdge <= boundaryTol)
            {
                return true;
            }

            // 3) Tesselar a polyline (linhas: direto; arcos: subdivididos por chordTol)
            List<Point2d> ring = BuildRing2D(pl, chordTol);

            // 4) Ray casting (regra par/ímpar)
            bool inside = false;
            int m = ring.Count;
            for (int i = 0, j = m - 1; i < m; j = i++)
            {
                Point2d vi = ring[i];
                Point2d vj = ring[j];

                bool condY = (vi.Y > pt.Y) != (vj.Y > pt.Y);
                if (condY)
                {
                    double denom = (vj.Y - vi.Y);
                    if (Math.Abs(denom) < 1e-20) { denom = (vj.Y >= vi.Y ? +1e-20 : -1e-20); }

                    double xInt = (vj.X - vi.X) * (pt.Y - vi.Y) / denom + vi.X;
                    if (pt.X < xInt) { inside = !inside; }
                }
            }
            return inside;
        }

        // Overload: “ilhotas” (furos) — ponto é dentro se dentro do invólucro externo e fora de todos os furos
        public static bool IsInside(Polyline outer, IList<Polyline> holes, Point2d pt, double boundaryTol = 1e-6, double chordTol = 0.25)
        {
            bool inOuter = IsInside(outer, pt, boundaryTol, chordTol);
            if (!inOuter) { return false; }
            if (holes != null)
            {
                foreach (Polyline hole in holes)
                {
                    if (hole != null && IsInside(hole, pt, boundaryTol, chordTol))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        // Constrói o anel 2D tesselando arcos respeitando o bulge
        private static List<Point2d> BuildRing2D(Polyline pl, double chordTol)
        {
            List<Point2d> pts = new List<Point2d>(pl.NumberOfVertices * 2);

            int n = pl.NumberOfVertices;
            for (int i = 0; i < n; i++)
            {
                SegmentType segType = pl.GetSegmentType(i);

                if (segType == SegmentType.Line)
                {
                    LineSegment2d ls = pl.GetLineSegment2dAt(i);
                    if (pts.Count == 0)
                    {
                        pts.Add(ls.StartPoint);
                    }
                    pts.Add(ls.EndPoint);
                }
                else if (segType == SegmentType.Arc)
                {
                    CircularArc2d arc = pl.GetArcSegment2dAt(i);

                    // Ângulo absoluto (sempre positivo)
                    double sweep = Math.Abs(NormalizeDeltaAngle(arc.StartAngle, arc.EndAngle, arc.IsClockWise));
                    double length = sweep * arc.Radius;

                    int steps = Math.Max(2, (int)Math.Ceiling(length / Math.Max(chordTol, 1e-6)));

                    // Orientação conforme sentido do arco
                    for (int s = 0; s <= steps; s++)
                    {
                        double t = (double)s / (double)steps;
                        double ang = InterpAngleAlongArc(arc.StartAngle, arc.EndAngle, arc.IsClockWise, t);

                        Vector2d dir = new Vector2d(Math.Cos(ang), Math.Sin(ang));
                        Point2d p = arc.Center + dir * arc.Radius;

                        if (pts.Count == 0 || p.GetDistanceTo(pts[pts.Count - 1]) > 1e-12)
                        {
                            pts.Add(p);
                        }
                    }
                }
            }

            // Remover duplicação do último com o primeiro
            if (pts.Count >= 2 && pts[0].GetDistanceTo(pts[pts.Count - 1]) <= 1e-12)
            {
                pts.RemoveAt(pts.Count - 1);
            }

            return pts;
        }

        // Normaliza a variação angular considerando sentido horário/anti-horário
        private static double NormalizeDeltaAngle(double angStart, double angEnd, bool isClockwise)
        {
            double twoPi = Math.PI * 2.0;
            double a0 = NormalizeAngle(angStart);
            double a1 = NormalizeAngle(angEnd);

            double delta = a1 - a0;
            if (isClockwise)
            {
                if (delta > 0) { delta -= twoPi; }
                delta = Math.Abs(delta);
            }
            else
            {
                if (delta < 0) { delta += twoPi; }
            }
            return delta;
        }

        private static double NormalizeAngle(double ang)
        {
            double twoPi = Math.PI * 2.0;
            double a = ang % twoPi;
            if (a < 0) { a += twoPi; }
            return a;
        }

        // Interpola ângulo ao longo do arco no sentido correto (t ∈ [0,1])
        private static double InterpAngleAlongArc(double angStart, double angEnd, bool isClockwise, double t)
        {
            double twoPi = Math.PI * 2.0;

            double a0 = NormalizeAngle(angStart);
            double a1 = NormalizeAngle(angEnd);

            double delta = a1 - a0;
            if (isClockwise)
            {
                if (delta > 0) { delta -= twoPi; }
                // a variação é negativa; andar no sentido horário
                return NormalizeAngle(a0 + t * delta);
            }
            else
            {
                if (delta < 0) { delta += twoPi; }
                // variação positiva anti-horária
                return NormalizeAngle(a0 + t * delta);
            }
        }
    }
}