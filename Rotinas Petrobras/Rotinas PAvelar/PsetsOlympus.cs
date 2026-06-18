// Usings essenciais e aliases:
using Autodesk.AutoCAD.ApplicationServices;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using HtmlAgilityPack;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace AutomacoesCivil3D
{
    public class ReurbImporter
    {
        private const string PSET_NAME = "RELATORIO DE REGULARIZAÇÃO FUNDIÁRIA";

        [CommandMethod("REURB_IMPORT")]
        public static async void ImportarReurb()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database db = Manager.DocData;

            try
            {
                PromptStringOptions pso = new PromptStringOptions("\nURL do relatório REURB: ") { AllowSpaces = true };
                PromptResult pr = docEditor.GetString(pso);
                if (pr.Status != PromptStatus.OK) return;

                string url = pr.StringResult;
                Dictionary<string, string> dados = await BaixarEExtrair(url);

                if (dados.Count == 0)
                {
                    docEditor.WriteMessage("\nNada extraído.");
                    return;
                }

                // Mostra resumo
                docEditor.WriteMessage($"\nCampos extraídos: {dados.Count}");

                // Seleciona entidade alvo e aplica com sua rotina existente:
                PromptEntityOptions peo = new PromptEntityOptions("\nSelecione o objeto que receberá o Pset: ");
                PromptEntityResult per = docEditor.GetEntity(peo);
                if (per.Status != PromptStatus.OK) return;

                // >>> AQUI CHAME SUA ROTINA DE ESCRITA DE PSETS <<<
                // Exemplo esperado: AplicarPsets(per.ObjectId, PSET_NAME, dados);
                docEditor.WriteMessage($"\nPronto. Passe 'dados' para sua rotina que grava no Pset \"{PSET_NAME}\".");
            }
            catch (Exception ex)
            {
                Editor ed = Manager.DocEditor;
                ed.WriteMessage($"\nErro: {ex.Message}");
            }
        }

        private static async Task<Dictionary<string, string>> BaixarEExtrair(string url)
        {
            Dictionary<string, string> saida = new Dictionary<string, string>();

            using (HttpClient http = new HttpClient())
            {
                string html = await http.GetStringAsync(url);

                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Coleta células de tabelas
                IEnumerable<HtmlNode> tds = doc.DocumentNode.SelectNodes("//td|//th") ?? Enumerable.Empty<HtmlNode>();
                List<string> cel = tds.Select(n => Limpar(n.InnerText)).Where(s => s.Length > 0).ToList();

                Dictionary<string, string> bruto = new Dictionary<string, string>();
                for (int i = 0; i + 1 < cel.Count; i++)
                {
                    string a = cel[i];
                    string b = cel[i + 1];

                    bool rotulo = a.EndsWith(":") || (a.Length <= 30 && a.ToUpperInvariant() == a);
                    if (rotulo && Valido(b))
                    {
                        string chave = a.TrimEnd(':').Trim();
                        if (!bruto.ContainsKey(chave)) bruto.Add(chave, b);
                    }
                }

                // Blocos <div><strong>Rótulo</strong> Valor</div>
                IEnumerable<HtmlNode> blocks = doc.DocumentNode.SelectNodes("//div[strong]") ?? Enumerable.Empty<HtmlNode>();
                foreach (HtmlNode n in blocks)
                {
                    string rot = Limpar(n.SelectSingleNode(".//strong")?.InnerText ?? "");
                    string val = Limpar(n.InnerText.Replace(rot, ""));
                    if (rot.Length > 0 && Valido(val))
                    {
                        if (!bruto.ContainsKey(rot)) bruto.Add(rot, val);
                    }
                }

                // Mapa: rótulos da página -> chaves do PSET
                Dictionary<string, string> mapa = new Dictionary<string, string>
                {
                    { "CÓD", "COD" },
                    { "REFERÊNCIA", "Referencia" },
                    { "PREFEITURA", "Prefeitura" },
                    { "BAIRRO", "Bairro" },
                    { "QUADRA", "Quadra" },
                    { "LOTE", "Lote" },
                    { "Data", "Visita_Data" },
                    { "Hora", "Visita_Hora" },
                    { "Status", "Visita_Status" },
                    { "MUNICÍPIO", "Municipio" },
                    { "LOTEAMENTO", "Loteamento" },
                    { "NOME", "Nome_Titular" },
                    { "CPF", "CPF_Titular" },
                    { "RG", "RG_Titular" },
                    { "UF", "UF_RG" },
                    { "DATA EXPEDIÇÃO", "Data_Emissao_RG" },
                    { "DATA NASCIMENTO", "Data_Nascimento" },
                    { "SEXO", "Sexo" },
                    { "ESTADO CIVIL", "Estado_Civil" },
                    { "NACIONALIDADE", "Nacionalidade" },
                    { "PROFISSÃO", "Profissao" },
                    { "RENDA COMPROVADA", "Renda_Comprovada" },
                    { "RENDA NÃO COMPROVADA", "Renda_Nao_Comprovada" }
                };

                foreach (KeyValuePair<string, string> kv in mapa)
                {
                    KeyValuePair<string, string>? hit = bruto.FirstOrDefault(p => Normalizar(p.Key) == Normalizar(kv.Key));
                    if (hit.HasValue) { saida[kv.Value] = hit.Value.Value; }
                }
            }

            return saida;
        }

        private static string Limpar(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            string r = HtmlEntity.DeEntitize(s).Trim();
            r = Regex.Replace(r, @"\s+", " ");
            return r;
        }
        private static bool Valido(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (s == "-" || s.Equals("N/A", System.StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
        private static string Normalizar(string s)
        {
            string t = s.ToUpperInvariant().Trim().Replace(":", "");
            t = t.Replace("Ç", "C").Replace("Ã", "A").Replace("Â", "A").Replace("Á", "A").Replace("À", "A")
                 .Replace("É", "E").Replace("Ê", "E").Replace("Í", "I").Replace("Ó", "O").Replace("Ô", "O")
                 .Replace("Ú", "U").Replace("Ü", "U");
            return t;
        }
    }
}
