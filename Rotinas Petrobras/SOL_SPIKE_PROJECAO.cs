using System;
using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using AcRxException = Autodesk.AutoCAD.Runtime.Exception;
using CivSectionView = Autodesk.Civil.DatabaseServices.SectionView;

namespace AutomacoesCivil3D
{
    /// <summary>
    /// Projeção de dispositivos (bueiro + alas + dissipador) numa Section View.
    ///
    /// A API .NET do Civil 3D NÃO expõe criação de projeção em section view
    /// (só estilo/label). Então orquestramos o comando nativo <c>_SPROJECTION</c>:
    /// seleciona os DISPOSITIVOS e DEPOIS a SECTION VIEW.
    ///
    /// Dois drivers pra testar qual o _SPROJECTION aceita:
    ///   SOL_SPIKE_PROJECAO     -> Editor.Command, passando os dois como SelectionSet.
    ///   SOL_SPIKE_PROJECAO_PF  -> pickfirst (SetImpliedSelection) p/ os dispositivos
    ///                             + Editor.Command só com a section view.
    /// </summary>
    public class SpikeProjecaoSecao
    {
        // ----------------------------------------------------------------------
        // DRIVER A (principal): Editor.Command com SelectionSet dos dispositivos
        // e da section view.
        // ----------------------------------------------------------------------
        [CommandMethod("SOL_SPIKE_PROJECAO")]
        public static void SolSpikeProjecao()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            if (!SelecionarDispositivos(ed, out ObjectId[] dispositivos)) return;
            if (!SelecionarSectionView(ed, out ObjectId sectionViewId)) return;

            ProjetarDispositivosNaSecao(ed, dispositivos, sectionViewId);
        }

        // ----------------------------------------------------------------------
        // DRIVER B (plano B): dispositivos via pickfirst, section view via Command.
        // ----------------------------------------------------------------------
        [CommandMethod("SOL_SPIKE_PROJECAO_PF")]
        public static void SolSpikeProjecaoPickFirst()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            if (!SelecionarDispositivos(ed, out ObjectId[] dispositivos)) return;
            if (!SelecionarSectionView(ed, out ObjectId sectionViewId)) return;

            ProjetarDispositivosNaSecaoPickFirst(ed, dispositivos, sectionViewId);
        }

        // ----------------------------------------------------------------------
        // MÉTODO REUTILIZÁVEL A — vai pro comando completo depois.
        // ----------------------------------------------------------------------
        public static bool ProjetarDispositivosNaSecao(Editor ed, ObjectId[] dispositivos, ObjectId sectionViewId)
        {
            if (!Validar(ed, dispositivos, sectionViewId)) return false;

            SelectionSet ssDisp = SelectionSet.FromObjectIds(dispositivos);
            SelectionSet ssSv = SelectionSet.FromObjectIds(new[] { sectionViewId });

            ed.WriteMessage(
                $"\n[PROJ-A] _SPROJECTION: {dispositivos.Length} dispositivo(s) -> section view {sectionViewId.Handle}");
            ed.WriteMessage("\n[PROJ-A] Tokens: \"_SPROJECTION\", <ssDispositivos>, \"\", <ssSectionView>, \"\"");
            try
            {
                var sw = Stopwatch.StartNew();
                // Seleciona objetos (dispositivos) -> ENTER -> seleciona a section view -> ENTER
                ed.Command("_SPROJECTION", ssDisp, "", ssSv, "");
                sw.Stop();
                ed.WriteMessage($"\n[PROJ-A] OK: sem excecao em {sw.ElapsedMilliseconds} ms. Confira a projecao na section view.");
                return true;
            }
            catch (AcRxException ex)
            {
                ed.WriteMessage($"\n[PROJ-A] FALHA (AcRx): ErrorStatus={ex.ErrorStatus} | {ex.Message}");
                return false;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[PROJ-A] FALHA ({ex.GetType().Name}): {ex.Message}");
                return false;
            }
        }

        // ----------------------------------------------------------------------
        // MÉTODO REUTILIZÁVEL B — pickfirst para os dispositivos.
        // ----------------------------------------------------------------------
        public static bool ProjetarDispositivosNaSecaoPickFirst(Editor ed, ObjectId[] dispositivos, ObjectId sectionViewId)
        {
            if (!Validar(ed, dispositivos, sectionViewId)) return false;

            SelectionSet ssSv = SelectionSet.FromObjectIds(new[] { sectionViewId });

            ed.WriteMessage(
                $"\n[PROJ-B] pickfirst + _SPROJECTION: {dispositivos.Length} dispositivo(s) -> section view {sectionViewId.Handle}");
            try
            {
                // Deixa os dispositivos "pre-selecionados" (pickfirst) e roda o comando,
                // que entao so deveria pedir a section view.
                ed.SetImpliedSelection(dispositivos);
                var sw = Stopwatch.StartNew();
                ed.Command("_SPROJECTION", ssSv, "");
                sw.Stop();
                ed.WriteMessage($"\n[PROJ-B] OK: sem excecao em {sw.ElapsedMilliseconds} ms. Confira a projecao na section view.");
                return true;
            }
            catch (AcRxException ex)
            {
                ed.WriteMessage($"\n[PROJ-B] FALHA (AcRx): ErrorStatus={ex.ErrorStatus} | {ex.Message}");
                return false;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[PROJ-B] FALHA ({ex.GetType().Name}): {ex.Message}");
                return false;
            }
        }

        // ----------------------------------------------------------------------
        // Helpers de seleção (dispositivos PRIMEIRO, section view DEPOIS)
        // ----------------------------------------------------------------------
        private static bool SelecionarDispositivos(Editor ed, out ObjectId[] dispositivos)
        {
            dispositivos = Array.Empty<ObjectId>();
            var opt = new PromptSelectionOptions
            {
                MessageForAdding = "\n[SPIKE] Selecione os DISPOSITIVOS a projetar (bueiro, alas, dissipador) e ENTER: "
            };
            PromptSelectionResult r = ed.GetSelection(opt);
            if (r.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n[SPIKE] Cancelado na selecao dos dispositivos.");
                return false;
            }
            dispositivos = r.Value.GetObjectIds();
            return dispositivos.Length > 0;
        }

        private static bool SelecionarSectionView(Editor ed, out ObjectId sectionViewId)
        {
            sectionViewId = ObjectId.Null;
            var opt = new PromptEntityOptions("\n[SPIKE] Selecione a SECTION VIEW de destino: ");
            opt.SetRejectMessage("\n[SPIKE] Nao e uma Section View. Clique na moldura/grade da secao.");
            opt.AddAllowedClass(typeof(CivSectionView), exactMatch: false);
            PromptEntityResult r = ed.GetEntity(opt);
            if (r.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n[SPIKE] Cancelado na selecao da section view.");
                return false;
            }
            sectionViewId = r.ObjectId;
            return true;
        }

        private static bool Validar(Editor ed, ObjectId[] dispositivos, ObjectId sectionViewId)
        {
            if (dispositivos == null || dispositivos.Length == 0)
            {
                ed.WriteMessage("\n[PROJ] Nenhum dispositivo informado.");
                return false;
            }
            if (sectionViewId.IsNull)
            {
                ed.WriteMessage("\n[PROJ] Section view nula.");
                return false;
            }
            return true;
        }
    }
}
