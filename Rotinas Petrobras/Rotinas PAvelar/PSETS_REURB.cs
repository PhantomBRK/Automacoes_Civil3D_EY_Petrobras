/*/ Referências:
// - Autodesk.AutoCAD.ApplicationServices
// - Autodesk.AutoCAD.DatabaseServices
// - Autodesk.AutoCAD.EditorInput
// - Autodesk.AutoCAD.Runtime
// - Autodesk.Civil.ApplicationServices
// - Autodesk.Civil.DatabaseServices
// - Autodesk.Aec.PropertyData
// - Autodesk.Aec.PropertyData.DatabaseServices
// - HtmlAgilityPack (NuGet)
// - System.Net.Http

using Autodesk.Aec.DatabaseServices;
using Autodesk.Aec.PropertyData;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using HtmlAgilityPack;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using DataType = Autodesk.Aec.PropertyData.DataType;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using ObjectIdCollection = Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection;

namespace AutomacoesCivil3D
{
    public class ReurbHtmlToPset
    {
        private const string PSET_NAME = "RELATORIO DE REGULARIZAÇÃO FUNDIÁRIA";

        [CommandMethod("REURB_IMPORT_APLICAR")]
        public static async void ImportarAplicar()
        {
            Document civilDoc = Manager.DocCad;
            CivilDocument civilDb = Manager.DocCivil;
            Editor docEditor = Manager.DocEditor;
            Database db = Manager.DocData;

            PromptStringOptions pso = new PromptStringOptions("\nURL do relatório: ") { AllowSpaces = true };
            PromptResult pr = docEditor.GetString(pso);
            if (pr.Status != PromptStatus.OK) return;
            string url = pr.StringResult;

            Dictionary<string, string> dados = await BaixarEExtrair(url);
            if (dados.Count == 0) { docEditor.WriteMessage("\nNada extraído."); return; }

            PromptEntityOptions peo = new PromptEntityOptions("\nSelecione o objeto destino:");
            PromptEntityResult per = docEditor.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;
            ObjectId alvoId = per.ObjectId;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Garantir definição do Pset
                PropertySetDefinition psetDef = GarantirPsetDefinition(db, tr, PSET_NAME);

                // Garantir propriedades com sua lógica (SetToStandard + SubSetDatabaseDefaults + DefaultData)
                //HashSet<string> existentes = ObterNomesPropriedades(psetDef, tr, db);
                foreach (KeyValuePair<string, string> kv in dados)
                {
                    string titulo = kv.Key;
                    if (!existentes.Contains(titulo))
                    {
                        PropertyDefinition pdNew = new PropertyDefinition();
                        pdNew.SetToStandard(db);
                        pdNew.SubSetDatabaseDefaults(db);
                        pdNew.Name = titulo;
                        pdNew.Description = titulo;
                        pdNew.DataType = DataType.Text;
                        pdNew.DefaultData = " - ";

                        psetDef.Definitions.Add(pdNew);
                    }
                }

                // Garantir PropertySet na entidade
                Entity br = (Entity)tr.GetObject(alvoId, OpenMode.ForRead);
                ObjectId psId = PropertyDataServices.GetPropertySet(br, psetDef.ObjectId);
                if (psId.IsNull)
                {
                    PropertyDataServices.AddPropertySet(br, psetDef.ObjectId);
                    psId = PropertyDataServices.GetPropertySet(br, psetDef.ObjectId);
                }

                // Escrever valores
                PropertySet ps = (PropertySet)tr.GetObject(psId, OpenMode.ForWrite);
                foreach (KeyValuePair<string, string> kv in dados)
                {
                    string titulo = kv.Key;
                    string valor = kv.Value ?? string.Empty;

                    int pid = ps.PropertyNameToId(titulo);
                    if (pid >= 0)
                    {
                        ps.SetAt(pid, valor);
                    }
                }

                tr.Commit();
            }

            docEditor.WriteMessage("\nPset garantido e valores aplicados.");
        }

        // ===== HTML → Dicionário com chaves do PSET =====
        private static async Task<Dictionary<string, string>> BaixarEExtrair(string url)
        {
            Dictionary<string, string> saida = new Dictionary<string, string>();

            using (HttpClient http = new HttpClient())
            {
                string html = await http.GetStringAsync(url);
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Captura pares em <td>/<th>
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

                // Captura <div><strong>Rótulo</strong> Valor</div>
                IEnumerable<HtmlNode> blocks = doc.DocumentNode.SelectNodes("//div[strong]") ?? Enumerable.Empty<HtmlNode>();
                foreach (HtmlNode n in blocks)
                {
                    string rot = Limpar(n.SelectSingleNode(".//strong")?.InnerText ?? "");
                    string val = Limpar(n.InnerText.Replace(rot, ""));
                    if (rot.Length > 0 && Valido(val) && !bruto.ContainsKey(rot))
                    {
                        bruto.Add(rot, val);
                    }
                }

                // Mapa rótulo da página → chave do PSET
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

        // ===== utilitários =====
        private static PropertySetDefinition GarantirPsetDefinition(Database db, Transaction tr, string nome)
        {
            
            DictionaryPropertySetDefinitions dictionary = new DictionaryPropertySetDefinitions(db);

            if (!dictionary.Has(nome, tr))
            {
                ObjectId novoId = dictionary.GetAt(nome);
                PropertySetDefinition defExistente = (PropertySetDefinition)tr.GetObject(novoId, OpenMode.ForRead);
                if (defExistente != null && defExistente.Name == nome) return defExistente;
            }
            

            PropertySetDefinition defNovo = new PropertySetDefinition();
            defNovo.SetToStandard(db);
            defNovo.AlternateName = nome;

            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
            string chave = nome;
            int i = 1;
            while (nod.Contains(chave)) { chave = nome + "_" + i.ToString(); i++; }
            nod.SetAt(chave, defNovo);
            tr.AddNewlyCreatedDBObject(defNovo, true);
            return defNovo;
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
*/