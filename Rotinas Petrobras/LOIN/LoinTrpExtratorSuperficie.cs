using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
// Qualificações explícitas para tipos ambíguos entre AutoCAD e Civil 3D / System.Drawing
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Region = Autodesk.AutoCAD.DatabaseServices.Region;
using Solid3d = Autodesk.AutoCAD.DatabaseServices.Solid3d;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using BlockTable = Autodesk.AutoCAD.DatabaseServices.BlockTable;
using BlockTableRecord = Autodesk.AutoCAD.DatabaseServices.BlockTableRecord;
using LayerTable = Autodesk.AutoCAD.DatabaseServices.LayerTable;
using LayerTableRecord = Autodesk.AutoCAD.DatabaseServices.LayerTableRecord;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using OpenMode = Autodesk.AutoCAD.DatabaseServices.OpenMode;
using Transaction = Autodesk.AutoCAD.DatabaseServices.Transaction;
using Database = Autodesk.AutoCAD.DatabaseServices.Database;
using DBObjectCollection = Autodesk.AutoCAD.DatabaseServices.DBObjectCollection;
using SweepOptions = Autodesk.AutoCAD.DatabaseServices.SweepOptions;
using Extents3d = Autodesk.AutoCAD.DatabaseServices.Extents3d;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace AutomacoesCivil3D
{
    // ========================================================================
    // Caminho A.2 do pipeline LOIN-TRP: gera um Solid3d entre duas TinSurfaces
    // (superior e inferior), representando uma camada de terraplenagem nos
    // trechos onde não há sub-assembly de TRP no corredor (não dá para usar A.1).
    //
    // Estratégia híbrida:
    //   - Volume PRECISO: calculado pelo Civil 3D via TinVolumeSurface
    //     (cut/fill exato entre as duas superfícies). Disponível no
    //     ExtracaoResult.VolumeM3 — quem aplica o valor no Pset do
    //     sólido é o pipeline LOIN (LOIN_APLICAR_SELECAO ou _EXSOLIDOSCORR_LOIN),
    //     usando a estrutura Pset_A/B/C/D derivada da planilha LOIN.
    //   - Geometria REPRESENTACIONAL: extrusão do boundary do volume surface
    //     por altura média. Solid3d serve para visualização e bind do Pset,
    //     não para auditoria geométrica (o número auditado é o VolumeM3).
    //
    // A.2 NÃO substitui A.1 — coexistem. O validador (LoinTrpValidador) detecta
    // sobreposição de estaqueamento entre os dois caminhos para evitar dupla
    // contagem no balanço corte/aterro.
    //
    // O extrator é stateless e não persiste nada por conta própria além do
    // Solid3d resultante na transação do caller. O TinVolumeSurface auxiliar
    // é criado e DELETADO ao final (ou marcado para erase) — não polui o
    // workspace de superfícies do usuário.
    // ========================================================================
    public static class LoinTrpExtratorSuperficie
    {
        // Tolerância para considerar duas surfaces "coincidentes" — abaixo disso
        // não gera sólido (não há volume relevante para representar).
        private const double VolumeMinimoM3 = 0.01;
        private const double EspessuraMinimaM = 0.001;

        public sealed class ExtracaoParams
        {
            // Surface superior — para camadas de ATERRO é o topo da camada;
            // para CORTE é o terreno existente antes da escavação.
            public ObjectId UpperSurfaceId { get; init; }

            // Surface inferior — para ATERRO é a base (terreno ou camada inferior);
            // para CORTE é o fundo da escavação.
            public ObjectId LowerSurfaceId { get; init; }

            // Camada canônica (do LoinTrpCatalogo) — define IfcClass, layer
            // sugerida e Pset a aplicar.
            public CamadaTrp Camada { get; init; }

            // Layer onde o Solid3d será criado. Se vazio, usa fallback
            // gerado a partir da camada (ex: "TRP_ATERRO_CORPO").
            public string LayerDestino { get; init; } = string.Empty;

            // Opcionais — preenchem campos do Pset_Rodoviario / Pset_Pavimentacao
            // quando a aplicação de Pset é chamada (não feita aqui — caller faz).
            public string AlinhamentoNome { get; init; } = string.Empty;
            public string EstacaInicial { get; init; } = string.Empty;
            public string EstacaFinal { get; init; } = string.Empty;
        }

        public sealed class ExtracaoResult
        {
            public ObjectId SolidId { get; init; }
            public double VolumeM3 { get; init; }
            public double AreaM2 { get; init; }
            public double EspessuraMediaM { get; init; }
            public double ZMedioInferior { get; init; }
            public double ZMedioSuperior { get; init; }
            public CamadaTrp Camada { get; init; }
            public string Resumo { get; init; } = string.Empty;
            public List<string> Avisos { get; } = new List<string>();

            public bool Sucesso => !SolidId.IsNull && VolumeM3 >= VolumeMinimoM3;
        }

        // Ponto de entrada. Recebe a transação ATIVA do caller — não faz commit
        // nem rollback. Em caso de erro, lança exceção e a transação do caller
        // pode ser abortada/recuperada.
        public static ExtracaoResult Extrair(Database db, Transaction tr, ExtracaoParams p)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (p.UpperSurfaceId.IsNull || p.LowerSurfaceId.IsNull)
                throw new ArgumentException("UpperSurfaceId e LowerSurfaceId são obrigatórios.");

            LoinTrpCamadaSpec? spec = LoinTrpCatalogo.Camadas.FirstOrDefault(c => c.Camada == p.Camada);
            if (spec == null)
                throw new InvalidOperationException("Camada não está no catálogo: " + p.Camada);

            ExtracaoResult result;

            TinSurface upper = (TinSurface)tr.GetObject(p.UpperSurfaceId, OpenMode.ForRead);
            TinSurface lower = (TinSurface)tr.GetObject(p.LowerSurfaceId, OpenMode.ForRead);

            // 1. Gera (e mantém para apagar depois) o TinVolumeSurface auxiliar.
            ObjectId volumeSurfaceId = CriarVolumeSurfaceAuxiliar(db, tr, upper, lower, spec);

            try
            {
                TinVolumeSurface volSurface =
                    (TinVolumeSurface)tr.GetObject(volumeSurfaceId, OpenMode.ForRead);

                // 2. Volume preciso vindo do Civil 3D.
                //    Para ATERRO usamos FillVolume; para CORTE usamos CutVolume.
                //    Os dois são positivos (Civil 3D normaliza).
                double volumeM3 = spec.EAterro
                    ? volSurface.GetVolumeProperties().UnadjustedFillVolume
                    : volSurface.GetVolumeProperties().UnadjustedCutVolume;

                if (volumeM3 < VolumeMinimoM3)
                {
                    return new ExtracaoResult
                    {
                        Camada = p.Camada,
                        VolumeM3 = volumeM3,
                        Resumo = "Volume abaixo do mínimo (" + volumeM3.ToString("F3") + " m³) — sólido não gerado."
                    };
                }

                // 3. Boundary do volume surface — vira a base do sólido.
                //    Usamos GetBorder que retorna o contorno externo (Point3dCollection).
                Polyline footprint = ExtrairBoundaryComoPolyline(volSurface);
                if (footprint == null || footprint.NumberOfVertices < 3)
                    throw new InvalidOperationException(
                        "Não foi possível extrair boundary do TinVolumeSurface — superfícies sem sobreposição planar válida?");

                double areaM2 = Math.Abs(footprint.Area);
                if (areaM2 < 0.001)
                    throw new InvalidOperationException("Boundary de área zero — superfícies podem ser idênticas.");

                // 4. Espessura média = Volume / Área. É uma média volumétrica,
                //    não simples — preserva a equivalência V = A * h_média.
                double espessuraMedia = volumeM3 / areaM2;
                if (espessuraMedia < EspessuraMinimaM)
                {
                    footprint.Dispose();
                    return new ExtracaoResult
                    {
                        Camada = p.Camada,
                        VolumeM3 = volumeM3,
                        AreaM2 = areaM2,
                        Resumo = "Espessura média < " + EspessuraMinimaM + " m — sólido não gerado."
                    };
                }

                // 5. Z médio das duas surfaces no centroid do footprint — para
                //    posicionar o sólido na elevação correta.
                Point3d centroid = AproxCentroide(footprint);
                double zUpper = AmostrarZ(upper, centroid);
                double zLower = AmostrarZ(lower, centroid);

                // Se a amostragem falhar (centroide fora da surface — pode acontecer
                // em footprints côncavos), cai para o midpoint da BoundingBox.
                if (double.IsNaN(zUpper) || double.IsNaN(zLower))
                {
                    Extents3d ext = footprint.GeometricExtents;
                    Point3d mid = new Point3d(
                        (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                        (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                        0.0);
                    zUpper = AmostrarZ(upper, mid);
                    zLower = AmostrarZ(lower, mid);
                }

                // Z base do sólido = média dos Z inferiores. Sólido sobe `espessuraMedia`.
                double zBase = (zUpper + zLower) / 2.0 - espessuraMedia / 2.0;

                // 6. Reposiciona footprint para zBase.
                Polyline footprintAtZ = ReposicionarParaZ(footprint, zBase);
                footprint.Dispose();

                // 7. Extruda.
                Solid3d solid = new Solid3d();
                using (DBObjectCollection curves = new DBObjectCollection())
                {
                    curves.Add(footprintAtZ);
                    DBObjectCollection regions = Region.CreateFromCurves(curves);
                    if (regions.Count == 0)
                        throw new InvalidOperationException("Region.CreateFromCurves falhou — footprint mal-formado.");
                    using Region region = (Region)regions[0];
                    solid.CreateExtrudedSolid(region, new Vector3d(0, 0, espessuraMedia), new SweepOptions());
                }
                footprintAtZ.Dispose();

                // 8. Layer + cor da camada.
                string layer = string.IsNullOrWhiteSpace(p.LayerDestino)
                    ? LayerFallback(p.Camada)
                    : p.LayerDestino;
                GarantirLayer(db, tr, layer);
                solid.Layer = layer;

                // 9. Adiciona ao ModelSpace.
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                ObjectId solidId = ms.AppendEntity(solid);
                tr.AddNewlyCreatedDBObject(solid, true);

                result = new ExtracaoResult
                {
                    SolidId = solidId,
                    VolumeM3 = volumeM3,
                    AreaM2 = areaM2,
                    EspessuraMediaM = espessuraMedia,
                    ZMedioInferior = zLower,
                    ZMedioSuperior = zUpper,
                    Camada = p.Camada,
                    Resumo = $"{spec.NomePortugues} | Vol={volumeM3:F2} m³ | Área={areaM2:F2} m² | Esp={espessuraMedia:F3} m"
                };

                if (Math.Abs(zUpper - zLower) > espessuraMedia * 5)
                    result.Avisos.Add("Espessura média representativa pouco fiel — terreno muito acidentado. " +
                                      "Volume no Pset (NetVolume) está correto; geometria é aproximada.");

                return result;
            }
            finally
            {
                // 10. Sempre apaga o TinVolumeSurface auxiliar — ele só existiu
                //     para calcular volume; manter polui a árvore de surfaces.
                try
                {
                    DBObject volObj = tr.GetObject(volumeSurfaceId, OpenMode.ForWrite, false, true);
                    volObj?.Erase(true);
                }
                catch
                {
                    // Falha em apagar não-fatal — surface fica visível mas o
                    // resultado já está calculado.
                }
            }
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        // Cria um TinVolumeSurface com um nome único + sufixo da camada/timestamp.
        // A API real do Civil 3D 2026 aceita 3 args: (name, baseId, comparisonId).
        // O estilo é inferido do default do CivilDocument — não precisamos passar.
        private static ObjectId CriarVolumeSurfaceAuxiliar(
            Database db, Transaction tr, TinSurface upper, TinSurface lower, LoinTrpCamadaSpec spec)
        {
            string nome = "_TRP_AUX_" + spec.Camada + "_" +
                          DateTime.Now.ToString("HHmmssfff");

            // Ordem: base = lower (inferior), comparison = upper (superior).
            // Volume Fill = onde upper > lower; Volume Cut = onde lower > upper.
            return TinVolumeSurface.Create(nome, lower.Id, upper.Id);
        }

        // Footprint do sólido para extrusão.
        //
        // LIMITAÇÃO CONHECIDA (v1): usa o BoundingBox (Extents3d) da volume
        // surface como retângulo de footprint, em vez do contorno irregular
        // real da TIN. Isso superestima a área quando a TIN tem formato
        // irregular (faz com que A_retângulo > A_TIN, mas a NetVolume no Pset
        // continua correta pq vem do GetVolumeProperties direto do Civil 3D).
        // A geometria do Solid3d fica caixote retangular — só representacional.
        //
        // TODO v2: usar ExtractBorder / Boundary collection da TinSurface
        // upper original para preservar contorno real. A API exata varia
        // entre versões do Civil 3D — em 2026 precisa de teste empírico.
        private static Polyline ExtrairBoundaryComoPolyline(CivilSurface surface)
        {
            Extents3d ext;
            try { ext = surface.GeometricExtents; }
            catch { return null; }

            Polyline pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(ext.MinPoint.X, ext.MinPoint.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(ext.MaxPoint.X, ext.MinPoint.Y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(ext.MaxPoint.X, ext.MaxPoint.Y), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(ext.MinPoint.X, ext.MaxPoint.Y), 0, 0, 0);
            pl.Closed = true;
            return pl;
        }

        // Centroide aproximado da polyline pelo método dos vértices.
        // Suficiente para encontrar um ponto interno para amostragem de Z;
        // não precisa ser o centroide geométrico exato.
        private static Point3d AproxCentroide(Polyline pl)
        {
            double sx = 0, sy = 0;
            int n = pl.NumberOfVertices;
            for (int i = 0; i < n; i++)
            {
                Point2d p = pl.GetPoint2dAt(i);
                sx += p.X;
                sy += p.Y;
            }
            return new Point3d(sx / n, sy / n, 0);
        }

        // Amostra Z da TinSurface em (x, y). Retorna NaN se o ponto cair fora.
        private static double AmostrarZ(TinSurface surface, Point3d pt)
        {
            try
            {
                return surface.FindElevationAtXY(pt.X, pt.Y);
            }
            catch
            {
                return double.NaN;
            }
        }

        // Cria nova Polyline em Z constante. A original fica intocada (será disposed).
        private static Polyline ReposicionarParaZ(Polyline original, double z)
        {
            Polyline novo = new Polyline();
            novo.Normal = Vector3d.ZAxis;
            novo.Elevation = z;
            for (int i = 0; i < original.NumberOfVertices; i++)
            {
                Point2d p = original.GetPoint2dAt(i);
                novo.AddVertexAt(i, p, 0, 0, 0);
            }
            novo.Closed = true;
            return novo;
        }

        // Garante que o layer exista no DWG. Cria com cor ACI 7 (branca) se não existir.
        // Cor real da camada virá depois via Code Set Style (LoinCodeSetStyleCorredores)
        // ou via aplicação direta do mapeamento LOIN — não é responsabilidade do extrator.
        private static void GarantirLayer(Database db, Transaction tr, string nome)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(nome)) return;
            lt.UpgradeOpen();
            using LayerTableRecord ltr = new LayerTableRecord
            {
                Name = nome,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7)
            };
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        // Layer-fallback quando o caller não passa LayerDestino. Convenção:
        // "TRP_<CAMADA>" em caps. Não é norma — só estrutura para não jogar
        // tudo no layer "0".
        private static string LayerFallback(CamadaTrp camada) => camada switch
        {
            CamadaTrp.Subleito          => "TRP_SUBLEITO",
            CamadaTrp.ReforcoSubleito   => "TRP_REFORCO_SUBLEITO",
            CamadaTrp.Regularizacao     => "TRP_REGULARIZACAO",
            CamadaTrp.AterroCorpo       => "TRP_ATERRO_CORPO",
            CamadaTrp.AterroCoroamento  => "TRP_ATERRO_COROAMENTO",
            CamadaTrp.CorteSolo         => "TRP_CORTE_SOLO",
            CamadaTrp.CorteRocha        => "TRP_CORTE_ROCHA",
            _                           => "TRP_GENERICO"
        };
    }
}
