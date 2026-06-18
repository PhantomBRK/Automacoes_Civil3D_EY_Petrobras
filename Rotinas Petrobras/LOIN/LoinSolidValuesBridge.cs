using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using System;
using System.Collections.Generic;
using System.Globalization;
using CivilAssembly = Autodesk.Civil.DatabaseServices.Assembly;
using AcadOpenMode = Autodesk.AutoCAD.DatabaseServices.OpenMode;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace AutomacoesCivil3D
{
    // Snapshot dos valores que o LoinExportacaoSolidosCorredores precisa para
    // popular Pset_A/B/C novos.
    //
    // Estratégia de fontes (em ordem de prioridade):
    //
    //  (1) PSets NATIVOS do Civil (Corridor Identity, Corridor Model Information,
    //      Corridor Shape Information) — anexados automaticamente pelo
    //      corridor.ExportSolids() em todos os sólidos. Têm valores diretos:
    //      CodeName, StartStation, EndStation, RegionGuid, SubassemblyName,
    //      SubassemblyHandle, CorridorName, AssemblyName etc.
    //
    //  (2) PSet_C LEGACY / CANONICAL — populado por:
    //      - PropertySets.PSetSolid (rotina antiga), que escreve em nomes LEGACY
    //        ("Pset_C - Propriedades Fisicas dos Objetos e Elementos",
    //         "C - Propriedades Físicas dos Objetos e Elementos") imediatamente
    //        antes do nosso ApplyLoinPackage. Já traz Altura/Largura/Área/
    //        Comprimento/Volume/Inclinação/Diâmetro/Cota_de_* calculados a
    //        partir da subassembly + Solid3D.
    //      - LoinExportacaoSolidosCorredores (novo), que escreve no nome CANONICAL
    //        atual: "Pset_C - Propriedades Fisicas dos Objetos".
    //      O bridge lê de QUALQUER um — preferência para o canonical novo,
    //      fallback para os legacy.
    //
    //  (3) PSet_B LEGACY ("Pset_B - Informações dos Objetos e Elementos" ou
    //      "B - ...") — fórmulas que duplicam os nativos. Apenas como fallback.
    //
    //  (4) GeometricExtents — somente para CenterE/N/Z e como último recurso
    //      para MinZ/MaxZ se o Pset_C legacy não tem Cota_de_Fundo/Topo.
    //
    // Não calculamos NADA paralelo via ColetarParametrosPorGuidGenerico — o
    // PSetSolid legacy é a única fonte de cálculo e suas saídas no Pset_C legacy
    // são confiáveis. Isso evita inconsistência entre os dois Pset_C.
    internal sealed class LoinSolidValues
    {
        // Físicos — vindos do Pset_C LEGACY (populado pelo PSetSolid)
        public double Width { get; set; }
        public double Altura { get; set; }
        public double LengthMeters { get; set; }
        public double Area { get; set; }
        public double Volume { get; set; }
        public double Slope { get; set; }       // em %, já formatado pelo legacy
        public double Diametro { get; set; }
        public double CotaFundo { get; set; }
        public double CotaTopo { get; set; }

        // Geométricos — vindos do GeometricExtents do sólido (complemento)
        public double MinZ { get; set; }
        public double MaxZ { get; set; }
        public double CenterE { get; set; }
        public double CenterN { get; set; }
        public double CenterZ { get; set; }

        // Stationing (Corridor Identity)
        public double StartStation { get; set; } = double.NaN;
        public double EndStation { get; set; } = double.NaN;

        // Identidade (Corridor Identity / Corridor Model Information / Corridor Shape Information)
        public string CodeName { get; set; } = string.Empty;
        public string SubassemblyName { get; set; } = string.Empty;
        public string AssemblyName { get; set; } = string.Empty;
        public string CorridorName { get; set; } = string.Empty;
        public string RegionGuid { get; set; } = string.Empty;
        public string Lado { get; set; } = string.Empty;

        // Identidade derivada do Pset_B legacy (Formula-based)
        public string Material { get; set; } = string.Empty;
        public string Situacao { get; set; } = string.Empty;
        public string Disciplina { get; set; } = string.Empty;
        public string Localizacao { get; set; } = string.Empty;
        public string CodigoObjeto { get; set; } = string.Empty;
    }

    internal static class LoinSolidValuesBridge
    {
        // ---- PSets NATIVOS do Civil (1ª fonte) ----
        private const string PsetCorrId    = "Corridor Identity";
        private const string PsetCorrModel = "Corridor Model Information";
        private const string PsetCorrShape = "Corridor Shape Information";

        // ---- PSet_C — nome CANONICAL atual + LEGACY (2ª fonte) ----
        // Primeiro nome = canonical novo (sai do LoinExportacaoSolidosCorredores).
        // Demais = variantes legacy (saem do PSetSolid antigo, com/sem prefixo
        // "Pset_" e com/sem acentuação). Aceita todas para ler de DWGs criados
        // antes ou depois da renomeação.
        private static readonly string[] PsetCLegacyNames =
        {
            "Pset_C - Propriedades Fisicas dos Objetos",          // canonical novo
            "Pset_C - Propriedades Fisicas dos Objetos e Elementos",
            "Pset_C - Propriedades Físicas dos Objetos e Elementos",
            "C - Propriedades Fisicas dos Objetos e Elementos",
            "C - Propriedades Físicas dos Objetos e Elementos"
        };

        // ---- PSet_B LEGACY (3ª fonte) ----
        private static readonly string[] PsetBLegacyNames =
        {
            "Pset_B - Informações dos Objetos e Elementos",
            "Pset_B - Informacoes dos Objetos e Elementos",
            "B - Informações dos Objetos e Elementos",
            "B - Informacoes dos Objetos e Elementos"
        };

        public static LoinSolidValues Extract(Entity entity, Database db, Transaction tr)
        {
            LoinSolidValues v = new LoinSolidValues();
            DictionaryPropertySetDefinitions dict = new DictionaryPropertySetDefinitions(db);

            // (1) Nativos — identidade + stationing autoritativos.
            ReadFromNativePsets(entity, dict, tr, v);

            // (2) Pset_C legacy — físicas já calculadas pelo PSetSolid.
            ReadFromPsetCLegacy(entity, dict, tr, v);

            // (3) Pset_B legacy — disciplina/situação/material/etc (fallback).
            ReadFromPsetBLegacy(entity, dict, tr, v);

            // (3.5) API Civil 3D — fallback DIRETO para StartStation/EndStation
            // quando os Psets falham (propriedades formula que dependem do
            // Corridor não-presente, ou Pset Corridor Identity não anexado).
            // Itera CorridorCollection do CivilDocument e casa por RegionGuid /
            // AssemblyName / CorridorName. Read direto de BaselineRegion.StartStation
            // e BaselineRegion.EndStation — valores em metros, sem parsing.
            if (double.IsNaN(v.StartStation) || double.IsNaN(v.EndStation))
                TryReadStationsFromCivilApi(tr, v);

            // (4) GeometricExtents — centroide e fallback de cotas.
            try
            {
                Extents3d ext = entity.GeometricExtents;
                v.MinZ = ext.MinPoint.Z;
                v.MaxZ = ext.MaxPoint.Z;
                v.CenterE = (ext.MinPoint.X + ext.MaxPoint.X) / 2.0;
                v.CenterN = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0;
                v.CenterZ = (ext.MinPoint.Z + ext.MaxPoint.Z) / 2.0;

                // Se o Pset_C legacy não preencheu as cotas, usa bbox.
                if (v.CotaFundo == 0.0) v.CotaFundo = ext.MinPoint.Z;
                if (v.CotaTopo  == 0.0) v.CotaTopo  = ext.MaxPoint.Z;
            }
            catch { }

            return v;
        }

        // -------------------- (1) Nativos do Civil --------------------

        private static void ReadFromNativePsets(
            Entity entity, DictionaryPropertySetDefinitions dict, Transaction tr, LoinSolidValues v)
        {
            // Corridor Identity — RegionGuid, StartStation, EndStation, SubassemblyName, BaselineHandle, CorridorHandle
            PropertySet id = TryOpenForRead(entity, dict, tr, PsetCorrId);
            if (id != null)
            {
                v.RegionGuid      = FirstNonEmpty(v.RegionGuid,
                    ReadString(id, entity, "RegionGuid"),
                    ReadString(id, entity, "RegionGUID"),
                    ReadString(id, entity, "RegionName"));
                v.SubassemblyName = FirstNonEmpty(v.SubassemblyName,
                    ReadString(id, entity, "SubassemblyName"));
                v.AssemblyName    = FirstNonEmpty(v.AssemblyName,
                    ReadString(id, entity, "AssemblyName"));

                // Estações: o Pset "Corridor Identity" entrega como Real, mas
                // se o template aplicar formatação Station ("3+875.50") o ToString
                // vem formatado e TryParseInvariant falha. TryParseStationMeters
                // aceita ambos os formatos (número puro OU "km+resto" DNIT).
                if (TryParseStationMeters(ReadString(id, entity, "StartStation"), out double s))
                    v.StartStation = s;
                if (TryParseStationMeters(ReadString(id, entity, "EndStation"), out double e))
                    v.EndStation = e;
            }

            // Corridor Model Information — CorridorName
            PropertySet model = TryOpenForRead(entity, dict, tr, PsetCorrModel);
            if (model != null)
            {
                v.CorridorName = FirstNonEmpty(v.CorridorName,
                    ReadString(model, entity, "CorridorName"));
            }

            // Corridor Shape Information — CodeName, Side, AssemblyName, AssemblyStartStation
            PropertySet shape = TryOpenForRead(entity, dict, tr, PsetCorrShape);
            if (shape != null)
            {
                v.CodeName     = FirstNonEmpty(v.CodeName,
                    ReadString(shape, entity, "CodeName"));
                v.Lado         = FirstNonEmpty(v.Lado,
                    ReadString(shape, entity, "Side"));
                v.AssemblyName = FirstNonEmpty(v.AssemblyName,
                    ReadString(shape, entity, "AssemblyName"));
                v.CorridorName = FirstNonEmpty(v.CorridorName,
                    ReadString(shape, entity, "CorridorName"));

                // Fallback: se Corridor Identity não trouxe stationing, tenta aqui.
                // AssemblyStartStation / AssemblyEndStation são alternativos com
                // mesmo significado mas vindos do Corridor Shape Information.
                if (double.IsNaN(v.StartStation) &&
                    TryParseStationMeters(ReadString(shape, entity, "AssemblyStartStation"), out double s))
                    v.StartStation = s;
                if (double.IsNaN(v.EndStation) &&
                    TryParseStationMeters(ReadString(shape, entity, "AssemblyEndStation"), out double e))
                    v.EndStation = e;
            }
        }

        // -------------------- (2) Pset_C legacy --------------------

        private static void ReadFromPsetCLegacy(
            Entity entity, DictionaryPropertySetDefinitions dict, Transaction tr, LoinSolidValues v)
        {
            foreach (string psetName in PsetCLegacyNames)
            {
                PropertySet pset = TryOpenForRead(entity, dict, tr, psetName);
                if (pset == null) continue;

                // Lê cada propriedade com várias variantes de nome (acento/sem acento).
                ReadDouble(pset, entity, "Altura",         d => v.Altura       = First(v.Altura, d));
                ReadDouble(pset, entity, "Largura",        d => v.Width        = First(v.Width, d));
                ReadDouble(pset, entity, "Comprimento",    d => v.LengthMeters = First(v.LengthMeters, d));
                ReadDouble(pset, entity, "Área",           d => v.Area         = First(v.Area, d));
                ReadDouble(pset, entity, "Area",           d => v.Area         = First(v.Area, d));
                ReadDouble(pset, entity, "Volume",         d => v.Volume       = First(v.Volume, d));
                ReadDouble(pset, entity, "Inclinação",     d => v.Slope        = First(v.Slope, d));
                ReadDouble(pset, entity, "Inclinacao",     d => v.Slope        = First(v.Slope, d));
                ReadDouble(pset, entity, "Diâmetro",       d => v.Diametro     = First(v.Diametro, d));
                ReadDouble(pset, entity, "Diametro",       d => v.Diametro     = First(v.Diametro, d));
                ReadDouble(pset, entity, "Cota_de_Fundo",  d => v.CotaFundo    = First(v.CotaFundo, d));
                ReadDouble(pset, entity, "Cota de Fundo",  d => v.CotaFundo    = First(v.CotaFundo, d));
                ReadDouble(pset, entity, "Cota_de_Topo",   d => v.CotaTopo     = First(v.CotaTopo, d));
                ReadDouble(pset, entity, "Cota de Topo",   d => v.CotaTopo     = First(v.CotaTopo, d));
            }
        }

        // -------------------- (3) Pset_B legacy --------------------

        private static void ReadFromPsetBLegacy(
            Entity entity, DictionaryPropertySetDefinitions dict, Transaction tr, LoinSolidValues v)
        {
            foreach (string psetName in PsetBLegacyNames)
            {
                PropertySet pset = TryOpenForRead(entity, dict, tr, psetName);
                if (pset == null) continue;

                // Identidade que pode vir só do Pset_B legacy (com fórmulas Formula-based).
                v.CodeName        = FirstNonEmpty(v.CodeName,        ReadString(pset, entity, "CodeName"));
                v.SubassemblyName = FirstNonEmpty(v.SubassemblyName, ReadString(pset, entity, "SubassemblyName"));
                v.AssemblyName    = FirstNonEmpty(v.AssemblyName,    ReadString(pset, entity, "AssemblyName"));
                v.CorridorName    = FirstNonEmpty(
                    v.CorridorName,
                    ReadString(pset, entity, "NomeCorredorSolido"),
                    ReadString(pset, entity, "NomeCorredorSolidos"),
                    ReadString(pset, entity, "CorridorName"));
                v.RegionGuid      = FirstNonEmpty(
                    v.RegionGuid,
                    ReadString(pset, entity, "RegionName"),
                    ReadString(pset, entity, "RegionGUID"),
                    ReadString(pset, entity, "RegionGuid"));
                v.Lado            = FirstNonEmpty(v.Lado, ReadString(pset, entity, "Lado"), ReadString(pset, entity, "Side"));
                v.Disciplina      = FirstNonEmpty(v.Disciplina,  ReadString(pset, entity, "Disciplina"));
                v.Localizacao     = FirstNonEmpty(v.Localizacao, ReadString(pset, entity, "Localização"), ReadString(pset, entity, "Localizacao"));
                v.Situacao        = FirstNonEmpty(v.Situacao,    ReadString(pset, entity, "Situação"),    ReadString(pset, entity, "Situacao"));
                v.CodigoObjeto    = FirstNonEmpty(v.CodigoObjeto,
                    ReadString(pset, entity, "Código_do_Objeto"),
                    ReadString(pset, entity, "CodigoObjeto"));

                if (v.LengthMeters <= 0.0)
                {
                    string comprimentoStr = ReadString(pset, entity, "Comprimento");
                    if (TryParseInvariant(comprimentoStr, out double comp) && comp > 0.0)
                        v.LengthMeters = comp;
                }

                if (double.IsNaN(v.StartStation))
                {
                    foreach (string key in new[] { "Estaqueamento_Inicial", "EstaqueamentoInicial" })
                        if (TryParseStationMeters(ReadString(pset, entity, key), out double s)) { v.StartStation = s; break; }
                }
                if (double.IsNaN(v.EndStation))
                {
                    foreach (string key in new[] { "Estaqueamento_Final", "EstaqueamentoFinal" })
                        if (TryParseStationMeters(ReadString(pset, entity, key), out double s)) { v.EndStation = s; break; }
                }
            }
        }

        // -------------------- (3.5) API Civil 3D — fallback direto --------------------

        // Itera Corridor → Baseline → BaselineRegion e casa a região do sólido
        // por RegionGuid (preferido), AssemblyName, ou CorridorName + AssemblyName
        // — nessa ordem de robustez. Quando casa, lê region.StartStation / EndStation
        // direto da API (em metros, double), bypassando completamente o pipeline
        // de Pset/parse que pode falhar quando as propriedades são formula-based
        // e dependem do corredor original (caso comum em sólidos extraídos via
        // ExportSolids() onde "Corridor Identity" pode não anexar todas as props).
        //
        // Roda apenas se ainda não temos StartStation/EndStation depois das
        // 3 fontes de Pset. Custo: O(N corredores × M baselines × R regions),
        // chamado uma vez por sólido. Para projetos típicos (< 10 corredores,
        // ~5 baselines, ~20 regions cada), são ~1000 iterações por sólido —
        // aceitável dentro de uma transação já aberta.
        private static void TryReadStationsFromCivilApi(Transaction tr, LoinSolidValues v)
        {
            CivilDocument civilDoc;
            try { civilDoc = Manager.DocCivil; }
            catch { return; }
            if (civilDoc == null) return;

            string targetRegionGuid    = v.RegionGuid    ?? string.Empty;
            string targetCorridorName  = v.CorridorName  ?? string.Empty;
            string targetAssemblyName  = v.AssemblyName  ?? string.Empty;

            // Sem nenhum discriminador, não dá pra casar a região certa.
            if (string.IsNullOrWhiteSpace(targetRegionGuid) &&
                string.IsNullOrWhiteSpace(targetCorridorName) &&
                string.IsNullOrWhiteSpace(targetAssemblyName))
                return;

            try
            {
                foreach (ObjectId corrId in civilDoc.CorridorCollection)
                {
                    Corridor corridor;
                    try { corridor = tr.GetObject(corrId, AcadOpenMode.ForRead) as Corridor; }
                    catch { continue; }
                    if (corridor == null) continue;

                    // Se temos CorridorName e bate, força match estrito; senão
                    // continua iterando pra cobrir solid que cruzou corredor.
                    bool corridorNameMatches = string.IsNullOrWhiteSpace(targetCorridorName)
                                            || corridor.Name.Equals(targetCorridorName, StringComparison.OrdinalIgnoreCase)
                                            || corridor.Name.Contains(targetCorridorName, StringComparison.OrdinalIgnoreCase);

                    foreach (Baseline baseline in corridor.Baselines)
                    {
                        foreach (BaselineRegion region in baseline.BaselineRegions)
                        {
                            bool isMatch = false;

                            // (a) Match por RegionGuid — mais confiável quando disponível.
                            if (!string.IsNullOrWhiteSpace(targetRegionGuid))
                            {
                                string regGuid;
                                try { regGuid = region.RegionGUID.ToString(); }
                                catch { regGuid = string.Empty; }

                                if (regGuid.Equals(targetRegionGuid, StringComparison.OrdinalIgnoreCase) ||
                                    regGuid.Contains(targetRegionGuid, StringComparison.OrdinalIgnoreCase) ||
                                    targetRegionGuid.Contains(regGuid, StringComparison.OrdinalIgnoreCase))
                                    isMatch = true;
                            }

                            // (b) Match por AssemblyName + CorridorName (fallback quando
                            //     RegionGuid não está disponível ou não casou).
                            if (!isMatch && corridorNameMatches && !string.IsNullOrWhiteSpace(targetAssemblyName))
                            {
                                try
                                {
                                    CivilAssembly asm = tr.GetObject(region.AssemblyId, AcadOpenMode.ForRead) as CivilAssembly;
                                    if (asm != null &&
                                        asm.Name.Equals(targetAssemblyName, StringComparison.OrdinalIgnoreCase))
                                        isMatch = true;
                                }
                                catch { }
                            }

                            if (!isMatch) continue;

                            if (double.IsNaN(v.StartStation)) v.StartStation = region.StartStation;
                            if (double.IsNaN(v.EndStation))   v.EndStation   = region.EndStation;

                            // Já casou — pode haver mais regions com mesma assembly
                            // mas o solid pertence a UMA (pelo RegionGuid implícito);
                            // ficamos com a primeira que bater todos os critérios.
                            return;
                        }
                    }
                }
            }
            catch
            {
                // Não-fatal: se a API falhar, mantém NaN e o write devolve string vazia.
            }
        }

        // -------------------- helpers --------------------

        private static PropertySet TryOpenForRead(Entity entity, DictionaryPropertySetDefinitions dict, Transaction tr, string psetName)
        {
            try
            {
                if (!dict.Has(psetName, tr)) return null;
                ObjectId defId = dict.GetAt(psetName);
                if (defId.IsNull) return null;

                ObjectId psetId = PropertyDataServices.GetPropertySet(entity, defId);
                if (psetId.IsNull || psetId.IsErased) return null;

                return tr.GetObject(psetId, OpenMode.ForRead, false) as PropertySet;
            }
            catch
            {
                return null;
            }
        }

        private static string ReadString(PropertySet pset, Entity entity, string propName)
        {
            try
            {
                int id = pset.PropertyNameToId(propName);
                if (id == -1) return string.Empty;
                object raw = pset.GetAt(id, entity);
                return raw?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void ReadDouble(PropertySet pset, Entity entity, string propName, Action<double> assign)
        {
            string s = ReadString(pset, entity, propName);
            if (TryParseInvariant(s, out double d) && !double.IsNaN(d))
                assign(d);
        }

        private static bool TryParseInvariant(string s, out double v)
        {
            if (string.IsNullOrWhiteSpace(s)) { v = double.NaN; return false; }
            // Remove sufixos comuns (%, m, m², m³) e normaliza separador decimal.
            string normalized = s.Trim()
                .Replace("%", "")
                .Replace("m³", "")
                .Replace("m²", "")
                .Replace(",", ".")
                .Trim();
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        // Parser de estaca/station que aceita 2 formatos:
        //   (a) Número puro em metros: "1234.56" / "1234,56" — vem quando o
        //       Pset entrega Real direto (caso comum quando o template usa
        //       formato de exibição Standard/Decimal).
        //   (b) DNIT "km+metros": "3+875.50" / "3+875,50" — vem quando o
        //       template aplica formatação Station para as colunas StartStation
        //       e EndStation do Corridor Identity. O parser separa pela primeira
        //       ocorrência de '+', interpreta a esquerda como km e a direita
        //       como metros restantes, retornando o total em metros.
        // Também tolera prefixos comuns "km ", "EST.".
        private static bool TryParseStationMeters(string s, out double meters)
        {
            meters = double.NaN;
            if (string.IsNullOrWhiteSpace(s)) return false;

            string clean = s.Trim();

            // Remove prefixos textuais comuns.
            if (clean.StartsWith("EST.", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(4).Trim();
            else if (clean.StartsWith("km", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(2).Trim();

            // Normaliza decimal BR → invariante.
            clean = clean.Replace(",", ".").Trim();

            // (b) Formato DNIT "km+metros".
            int plus = clean.IndexOf('+');
            if (plus > 0 && plus < clean.Length - 1)
            {
                string kmPart = clean.Substring(0, plus).Trim();
                string restPart = clean.Substring(plus + 1).Trim();

                if (double.TryParse(kmPart,   NumberStyles.Float, CultureInfo.InvariantCulture, out double km) &&
                    double.TryParse(restPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double rest))
                {
                    meters = km * 1000.0 + rest;
                    return !double.IsNaN(meters);
                }
            }

            // (a) Número puro em metros.
            return double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out meters);
        }

        private static double First(double existing, double candidate)
            => existing > 0.0 ? existing : candidate;

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string v in values)
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            return string.Empty;
        }
    }
}
