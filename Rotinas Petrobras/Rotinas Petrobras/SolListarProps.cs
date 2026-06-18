using System;
using System.Collections;
using System.IO;
using System.Text;
using AutomacoesCivil3D;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using SOLIDOS;

namespace RotinasPetrobras.Quantitativos
{
    /// <summary>
    /// SOL_LISTAR_PROPS: diagnóstico. Pede pra selecionar UM dispositivo SOLIDOS
    /// (caixa, tubo, conexão...) e despeja todas as propriedades (nome = valor [tipo])
    /// no editor e num .txt na Área de Trabalho. Usado para descobrir os nomes exatos
    /// das propriedades antes de escrever rotinas de extração.
    /// </summary>
    public partial class SolQuantTubos
    {
        [CommandMethod("SOL_LISTAR_PROPS")]
        public void ListarProps()
        {
            var doc = Manager.DocCad;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            var peo = new PromptEntityOptions("\nSelecione um dispositivo SOLIDOS (caixa/tubo/conexão):");
            peo.SetRejectMessage("\nNão é uma entidade válida.");
            peo.AllowNone = false;
            var per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            var sb = new StringBuilder();
            try
            {
                using (doc.LockDocument())
                using (var t = db.TransactionManager.StartTransaction())
                {
                    ObjectId id = per.ObjectId;
                    sb.AppendLine("=== SOL_LISTAR_PROPS ===");
                    sb.AppendLine($"Classe ARX: {id.ObjectClass?.Name}");
                    sb.AppendLine($"Handle:     {id.Handle}");
                    sb.AppendLine($"Name='{LerString(id, "Name")}'  Family='{LerString(id, "Family")}'  SubType='{LerString(id, "SubType")}'");
                    sb.AppendLine("---- Propriedades (nome = valor [tipo]) ----");

                    object props = null;
                    try { props = SolidosAPI.ListProperties(id); }
                    catch (System.Exception ex) { sb.AppendLine($"[ListProperties lançou: {ex.Message}]"); }

                    if (props is IEnumerable en)
                    {
                        foreach (var p in en)
                        {
                            string nome = p?.ToString();
                            if (string.IsNullOrWhiteSpace(nome)) continue;

                            Type pt = null;
                            object val = null;
                            try { val = SolidosAPI.GetNodeParam(id, nome, null, ref pt); }
                            catch (System.Exception ex) { val = $"<erro: {ex.Message}>"; }

                            string valStr = val == null ? "<null>" : val.ToString();
                            sb.AppendLine($"{nome} = {valStr}   [{pt?.Name ?? "?"}]");
                        }
                    }
                    else
                    {
                        sb.AppendLine("[ListProperties não retornou lista enumerável]");
                    }

                    t.Commit();
                }

                string outPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "solidos_props_dump.txt");
                File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));

                ed.WriteMessage("\n" + sb.ToString());
                ed.WriteMessage($"\n[SOL_LISTAR_PROPS] Salvo em: {outPath}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[SOL_LISTAR_PROPS] ERRO: {ex.Message}");
            }
        }
    }
}
