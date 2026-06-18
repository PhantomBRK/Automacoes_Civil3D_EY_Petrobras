using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Autodesk.AutoCAD.DatabaseServices;

using SOLIDOS;

namespace AutomacoesCivil3D
{
    // Lista de DNs disponíveis em todos os tubos SOLIDOS deste projeto, conforme
    // ValueOptions da DynamicProperty "Catalogo" no .sbd:
    //   100;150;200;250;300;350;400;450;500;600;700;800;900;1000;1100;1200
    // "Catalogo" é STRING editável; "Diametro" é derivado (Array.IndexOf na lista
    // acima + divisão por 1000), portanto read-only. Escrita sempre via Catalogo.
    public static class CatalogoTuboPadrao
    {
        public static readonly IReadOnlyList<int> DNsMm = new List<int>
        {
            100, 150, 200, 250, 300, 350, 400, 450, 500,
            600, 700, 800, 900, 1000, 1100, 1200
        };

        public static IReadOnlyList<double> DNsM { get; } =
            DNsMm.Select(d => d / 1000.0).ToList();

        public static int DnMmMaisProximo(double dEmMetros)
        {
            int alvo = (int)Math.Round(dEmMetros * 1000.0);
            int melhor = DNsMm[0];
            int distMin = Math.Abs(melhor - alvo);
            foreach (int dn in DNsMm)
            {
                int d = Math.Abs(dn - alvo);
                if (d < distMin)
                {
                    distMin = d;
                    melhor = dn;
                }
            }
            return melhor;
        }

        // Seta Catalogo como STRING (formato exato dos ValueOptions).
        //
        // IMPORTANTE: usa o overload SetNodeParams(ObjectId, Dictionary) que retorna
        // void (NÃO o overload de 3 args que retorna bool). O bool daquele overload
        // significa "houve commit/mudança", NÃO "gravação bem-sucedida" — usá-lo como
        // sinal de sucesso fazia a rotina pular todos os tubos já no DN alvo.
        // Validação real é por releitura.
        public static bool TrySetCatalogo(ObjectId tuboId, int dnMm)
        {
            if (!DNsMm.Contains(dnMm))
            {
                return false;
            }
            var dic = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Catalogo"] = dnMm.ToString(CultureInfo.InvariantCulture)
            };
            SolidosAPI.SetNodeParams(tuboId, dic);   // overload void
            int? lido = LerCatalogoMm(tuboId);
            // Sucesso = releitura bate, OU já estava no DN alvo (idempotente).
            return lido == dnMm;
        }

        // Lê o DN atual do tubo (mm) a partir da DynamicProperty "Catalogo" (string)
        // ou, em fallback, de "Diametro" (double, metros).
        public static int? LerCatalogoMm(ObjectId tuboId)
        {
            try
            {
                Type ty = null;
                object cat = SolidosAPI.GetNodeParam(tuboId, "Catalogo", null, ref ty);
                if (cat != null)
                {
                    string s = (cat as string ?? cat.ToString()).Trim();
                    // Remove prefixos eventuais ("DN", "U", "Ø") e fica só com dígitos.
                    var digits = new string(s.Where(char.IsDigit).ToArray());
                    if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mm) && mm > 0)
                        return mm;
                }

                Type ty2 = null;
                object dia = SolidosAPI.GetNodeParam(tuboId, "Diametro", null, ref ty2);
                if (dia is double d && d > 0) return (int)Math.Round(d * 1000.0);
            }
            catch { /* ignora */ }
            return null;
        }
    }
}
