using System;
using System.IO;

namespace AutomacoesCivil3D
{
    /// <summary>
    /// Resolve, em runtime, a pasta de instalação do bundle do plugin
    /// (AutomacoesPetrobras.bundle) e suas subpastas de recurso.
    ///
    /// A DLL do plugin é deployada em
    ///     ...\ApplicationPlugins\AutomacoesPetrobras.bundle\Contents\AutomacoesCivil3D.dll
    /// então a raiz é descoberta a partir da localização do assembly (subindo de
    /// Contents até a pasta cujo nome termina em ".bundle"). Isso funciona quer o
    /// cliente instale em %AppData% quer em %ProgramData%\...\ApplicationPlugins.
    ///
    /// Use <see cref="Resource(string[])"/> para localizar arquivos que serão
    /// instalados JUNTO do plugin (templates de quantitativo, mapeamentos IFC, etc.),
    /// em vez de caminhos fixos no PC de quem desenvolveu.
    /// </summary>
    public static class BundlePaths
    {
        public const string BundleName = "AutomacoesPetrobras.bundle";

        private static readonly string _root = ResolveRoot();

        /// <summary>Raiz do bundle instalado (a pasta ...AutomacoesPetrobras.bundle).</summary>
        public static string Root => _root;

        /// <summary>Subpasta Contents (onde ficam os .dll/.json do plugin).</summary>
        public static string Contents => Path.Combine(_root, "Contents");

        /// <summary>Subpasta Resources (templates, planilhas, mapeamentos instalados).</summary>
        public static string Resources => Path.Combine(_root, "Resources");

        /// <summary>Subpasta interfaces (ribbons .cuix / .mnr).</summary>
        public static string Interfaces => Path.Combine(_root, "interfaces");

        /// <summary>
        /// Caminho completo de um recurso dentro de Resources, ex.:
        /// <c>BundlePaths.Resource("Quantitativos", "Drenagem_1 2.xlsx")</c>.
        /// </summary>
        public static string Resource(params string[] partes)
        {
            string full = Resources;
            if (partes != null)
            {
                foreach (var p in partes)
                {
                    if (!string.IsNullOrEmpty(p))
                        full = Path.Combine(full, p);
                }
            }
            return full;
        }

        private static string ResolveRoot()
        {
            // 1) A partir da localização do próprio assembly (caminho confiável dentro
            //    do Civil 3D; a DLL não é single-file, então Location é preenchido).
            try
            {
                string asmPath = typeof(BundlePaths).Assembly.Location;
                if (!string.IsNullOrEmpty(asmPath))
                {
                    DirectoryInfo dir = new DirectoryInfo(Path.GetDirectoryName(asmPath));

                    // Sobe até achar a pasta ".bundle" (no máximo poucos níveis).
                    for (int i = 0; i < 6 && dir != null; i++)
                    {
                        if (dir.Name.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
                            return dir.FullName;
                        dir = dir.Parent;
                    }

                    // Não achou ".bundle", mas o assembly está numa pasta Contents:
                    // a raiz do bundle é o pai de Contents.
                    DirectoryInfo contents = new DirectoryInfo(Path.GetDirectoryName(asmPath));
                    if (string.Equals(contents.Name, "Contents", StringComparison.OrdinalIgnoreCase)
                        && contents.Parent != null)
                    {
                        return contents.Parent.FullName;
                    }
                }
            }
            catch
            {
                // cai para o fallback
            }

            // 2) Fallback: caminho legado padrão de instalação por usuário.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk", "ApplicationPlugins", BundleName);
        }
    }
}
