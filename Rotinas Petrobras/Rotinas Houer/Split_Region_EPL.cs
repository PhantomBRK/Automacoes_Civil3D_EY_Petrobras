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

namespace AutomacoesCivil3D
{
    public class CorridorSplitRegionByStation
    {
        [CommandMethod("SPLITREG_ESTACA")]
        public void SplitRegionByStation()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database db = civilDoc.Database;

            try
            {
                // 1) Pergunta a estaca/estação
                PromptDoubleOptions pdo = new PromptDoubleOptions(
                    "\nInforme a estaca/estação onde deseja dividir a região:");
                pdo.AllowNegative = false;
                pdo.AllowZero = false;

                PromptDoubleResult pdr = docEditor.GetDouble(pdo);
                if (pdr.Status != PromptStatus.OK)
                {
                    return;
                }

                double estaca = pdr.Value;

                // 2) Seleciona o corredor
                PromptEntityOptions peo = new PromptEntityOptions(
                    "\nSelecione o corredor a ser dividido:");
                peo.SetRejectMessage("\nSelecione apenas objetos do tipo Corridor.");
                peo.AddAllowedClass(typeof(Corridor), true);

                PromptEntityResult per = docEditor.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                {
                    return;
                }

                using (DocumentLock docLock = civilDoc.LockDocument())
                using (Transaction trans = db.TransactionManager.StartTransaction())
                {
                    Corridor corridor = (Corridor)trans.GetObject(
                        per.ObjectId, OpenMode.ForWrite);

                    bool encontrouRegiao = false;

                    // Percorre todos os baselines do corredor
                    foreach (Baseline baseline in corridor.Baselines)
                    {
                        BaselineRegionCollection regioes = baseline.BaselineRegions;

                        foreach (BaselineRegion reg in regioes)
                        {
                            double inicio = reg.StartStation;
                            double fim = reg.EndStation;

                            docEditor.WriteMessage($"\nVerificando região no baseline \"{baseline.Name}\" " +
                                $"de {inicio:0.00}–{fim:0.00}...");

                            // Estação dentro da região
                            if (estaca > inicio && estaca < fim)
                            {
                                try
                                {
                                    // Split da região no ponto informado
                                    BaselineRegion novaRegiao = reg.Split(estaca);

                                    docEditor.WriteMessage(
                                        $"\nRegião dividida no baseline \"{baseline.Name}\" " +
                                        $"de {inicio:0.00}–{fim:0.00} em {estaca:0.00}.");

                                    encontrouRegiao = true;
                                }
                                catch (System.ArgumentException)
                                {
                                    docEditor.WriteMessage(
                                        "\nNão foi possível dividir a região nessa estaca." +
                                        "\nO Civil 3D exige que o split esteja pelo menos " +
                                        "0.01 acima do início e 0.01 abaixo do fim da região.");
                                    encontrouRegiao = true;
                                }

                                break; // já achou a região certa neste baseline
                            }
                        }

                        if (encontrouRegiao)
                        {
                            break;
                        }
                    }

                    if (!encontrouRegiao)
                    {
                        docEditor.WriteMessage(
                            "\nNenhuma região de corredor contém a estaca informada.");
                    }
                    else
                    {
                        // Rebuild do corredor após alteração das regiões
                        corridor.Rebuild();
                    }

                    trans.Commit();
                }
            }
            catch (Exception ex)
            {
                docEditor.WriteMessage(
                    "\nErro na rotina SPLITREG_ESTACA: " + ex.Message);
            }
        }
    }
}
