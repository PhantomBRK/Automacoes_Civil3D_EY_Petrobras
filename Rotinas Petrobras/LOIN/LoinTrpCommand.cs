using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;
using AcadException = Autodesk.AutoCAD.Runtime.Exception;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutomacoesCivil3D
{
    // ========================================================================
    // Comandos AutoCAD da disciplina Terraplenagem do pipeline LOIN.
    //
    // Não substituem nenhum comando existente — todos com prefixo LOIN_TRP_.
    // A.1 (corredor com sub-assembly de TRP) reusa _EXSOLIDOSCORR_LOIN.
    // ========================================================================
    public sealed class LoinTrpCommand
    {
        // -------------------------------------------------------------------
        // Comando A.2 — gera Solid3d entre 2 TinSurfaces para uma camada
        // de terraplenagem específica, com volume preciso via Civil 3D.
        //
        // Fluxo interativo:
        //   1. Pede upper surface
        //   2. Pede lower surface
        //   3. Pede keyword da camada
        //   4. Chama LoinTrpExtratorSuperficie.Extrair
        //   5. Reporta no Editor
        // -------------------------------------------------------------------
        [CommandMethod("_LOIN_TRP_EXTRAIR_SUPERFICIE", CommandFlags.Modal)]
        public void LoinTrpExtrairSuperficie()
        {
            Document doc = Manager.DocCad;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                ObjectId upperId = PromptSurface(ed, "Selecione a superfície SUPERIOR (topo da camada/terreno existente em corte): ");
                if (upperId.IsNull) return;

                ObjectId lowerId = PromptSurface(ed, "Selecione a superfície INFERIOR (base da camada/fundo do corte): ");
                if (lowerId.IsNull) return;

                CamadaTrp? camada = PromptCamada(ed);
                if (camada == null) return;

                LoinTrpExtratorSuperficie.ExtracaoResult resultado;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var p = new LoinTrpExtratorSuperficie.ExtracaoParams
                    {
                        UpperSurfaceId  = upperId,
                        LowerSurfaceId  = lowerId,
                        Camada          = camada.Value
                    };

                    resultado = LoinTrpExtratorSuperficie.Extrair(db, tr, p);

                    if (resultado.Sucesso)
                        tr.Commit();
                    else
                        tr.Abort();
                }

                ed.WriteMessage("\n[LOIN-TRP] " + resultado.Resumo);
                foreach (string aviso in resultado.Avisos)
                    ed.WriteMessage("\n[LOIN-TRP] ⚠ " + aviso);

                if (resultado.Sucesso)
                {
                    ed.WriteMessage(
                        "\n[LOIN-TRP] Solid3d criado no ModelSpace. " +
                        "Aplique o Pset via LOINMAP + _EXSOLIDOSCORR_LOIN ou diretamente via PSet definitions.");
                }
            }
            catch (AcadException ex)
            {
                ed.WriteMessage("\n[LOIN-TRP] Erro AutoCAD: " + ex.Message);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n[LOIN-TRP] Erro: " + ex.Message);
            }
        }

        // -------------------------------------------------------------------
        // Comando de validação — varre Solid3d em layers TRP_* do ModelSpace,
        // monta lista de LoinTrpQuantitativo a partir do volume + camada
        // inferida do nome do layer, e chama LoinTrpValidador.
        //
        // Limitação v1: derivação de camada é por nome do layer (convenção
        // TRP_<CAMADA>). Quando o usuário criou layers customizados via LOINMAP,
        // a camada não é inferida — fica no balde "outros" e é ignorada no balanço.
        // -------------------------------------------------------------------
        [CommandMethod("_LOIN_TRP_VALIDAR", CommandFlags.Modal)]
        public void LoinTrpValidar()
        {
            Document doc = Manager.DocCad;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                List<LoinTrpQuantitativo> quants = new List<LoinTrpQuantitativo>();
                int totalSolidos = 0;
                int solidosTrp   = 0;
                int solidosSemCamada = 0;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId id in ms)
                    {
                        if (id.ObjectClass.Name != "AcDb3dSolid") continue;
                        totalSolidos++;

                        Solid3d sol = tr.GetObject(id, OpenMode.ForRead) as Solid3d;
                        if (sol == null) continue;

                        string layer = sol.Layer ?? string.Empty;
                        if (!layer.StartsWith("TRP_", StringComparison.OrdinalIgnoreCase)) continue;
                        solidosTrp++;

                        CamadaTrp? camada = CamadaDoLayer(layer);
                        if (camada == null)
                        {
                            solidosSemCamada++;
                            continue;
                        }

                        // Volume da geometria via MassProperties. Para A.1 isso
                        // é o volume real; para A.2 (gerado pelo extrator) é o
                        // volume do caixote representacional — não bate exatamente
                        // com o NetVolume do Pset (que tem o valor preciso do
                        // Civil 3D). Em v2 ler do Pset diretamente quando existir.
                        double vol = 0;
                        try { vol = sol.MassProperties.Volume; } catch { }

                        quants.Add(new LoinTrpQuantitativo
                        {
                            Camada = camada.Value,
                            VolumeM3 = vol,
                            // OrigemGeometria não é inferível só do Solid3d — fica
                            // em branco. Dupla contagem só é detectada se o caller
                            // popular essa info explicitamente em v2.
                            OrigemGeometria = string.Empty
                        });
                    }
                    tr.Commit();
                }

                ed.WriteMessage($"\n[LOIN-TRP] Sólidos no ModelSpace: {totalSolidos} | em layers TRP_*: {solidosTrp} | sem camada inferida: {solidosSemCamada}");

                LoinTrpValidador.Relatorio rel = LoinTrpValidador.Validar(quants);
                ed.WriteMessage("\n" + rel.Formatado());
            }
            catch (AcadException ex)
            {
                ed.WriteMessage("\n[LOIN-TRP] Erro AutoCAD: " + ex.Message);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n[LOIN-TRP] Erro: " + ex.Message);
            }
        }

        // -------------------------------------------------------------------
        // Helpers privados
        // -------------------------------------------------------------------

        private static ObjectId PromptSurface(Editor ed, string mensagem)
        {
            var peo = new PromptEntityOptions("\n" + mensagem)
            {
                AllowNone = false
            };
            peo.SetRejectMessage("\nEntidade não é uma superfície Civil 3D. Tente novamente.");
            peo.AddAllowedClass(typeof(TinSurface), exactMatch: false);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return ObjectId.Null;

            // Confirma que é mesmo TinSurface (não Surface base ou outro tipo)
            using (Transaction tr = ed.Document.Database.TransactionManager.StartTransaction())
            {
                CivilSurface s = tr.GetObject(per.ObjectId, OpenMode.ForRead) as CivilSurface;
                tr.Commit();
                if (!(s is TinSurface))
                {
                    ed.WriteMessage("\n[LOIN-TRP] A entidade selecionada não é TinSurface.");
                    return ObjectId.Null;
                }
            }
            return per.ObjectId;
        }

        private static CamadaTrp? PromptCamada(Editor ed)
        {
            var pko = new PromptKeywordOptions("\nCamada [Subleito/REforco/REGularizacao/ATCorpo/ATCoroamento/CSolo/CRocha]: ");
            pko.AllowNone = false;
            pko.Keywords.Add("Subleito");
            pko.Keywords.Add("REforco");
            pko.Keywords.Add("REGularizacao");
            pko.Keywords.Add("ATCorpo");
            pko.Keywords.Add("ATCoroamento");
            pko.Keywords.Add("CSolo");
            pko.Keywords.Add("CRocha");

            PromptResult pr = ed.GetKeywords(pko);
            if (pr.Status != PromptStatus.OK) return null;

            return pr.StringResult switch
            {
                "Subleito"      => CamadaTrp.Subleito,
                "REforco"       => CamadaTrp.ReforcoSubleito,
                "REGularizacao" => CamadaTrp.Regularizacao,
                "ATCorpo"       => CamadaTrp.AterroCorpo,
                "ATCoroamento"  => CamadaTrp.AterroCoroamento,
                "CSolo"         => CamadaTrp.CorteSolo,
                "CRocha"        => CamadaTrp.CorteRocha,
                _               => null
            };
        }

        // Mesmas chaves do LayerFallback do extrator, em sentido reverso.
        // Mantido aqui em vez de exposto público no catálogo porque é convenção
        // local — outros consumidores podem mapear de outra forma.
        private static CamadaTrp? CamadaDoLayer(string layer)
        {
            if (string.IsNullOrWhiteSpace(layer)) return null;
            string l = layer.ToUpperInvariant();
            if (l == "TRP_SUBLEITO")            return CamadaTrp.Subleito;
            if (l == "TRP_REFORCO_SUBLEITO")    return CamadaTrp.ReforcoSubleito;
            if (l == "TRP_REGULARIZACAO")       return CamadaTrp.Regularizacao;
            if (l == "TRP_ATERRO_CORPO")        return CamadaTrp.AterroCorpo;
            if (l == "TRP_ATERRO_COROAMENTO")   return CamadaTrp.AterroCoroamento;
            if (l == "TRP_CORTE_SOLO")          return CamadaTrp.CorteSolo;
            if (l == "TRP_CORTE_ROCHA")         return CamadaTrp.CorteRocha;
            return null;
        }
    }
}
