using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows.Data;
using System;
using System.IO;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

namespace AutomacoesCivil3D
{
    public class Program
    {
        [CommandMethod("InstalarTemplate")]
        public static void InstalarTemplateComando()
        {
            //Main();          
           
        }

        public static void Main()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            string basePath = @"C:\ProgramData\Autodesk\ApplicationPlugins\RotinasPetrobras.bundle\Resources";
            Console.WriteLine("Pasta e arquivos copiados com sucesso!");

            string roamingPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            string[] destinationPaths = {
                Path.Combine(localPath, @"Autodesk\C3D 2025\enu\Template\DE-0000.00-0000-DRE.dwt"),
                Path.Combine(localPath, @"Autodesk\C3D 2025\enu\Template\DE-0000.00-0000-TRP_PAV.dwt"),
                Path.Combine(roamingPath, @"Autodesk\C3D 2025\enu\Support\c3d.cuix"),
                Path.Combine(roamingPath, @"Autodesk\C3D 2025\enu\Support\Profiles\C3D_Brazil\Profile.aws"),
                Path.Combine(roamingPath, @"Autodesk\C3D 2025\enu\Support\Profiles\FixedProfile.aws"),
                Path.Combine(programDataPath, @"Autodesk\C3D 2025\enu\ContentLibrary\Templates"),        
                Path.Combine(roamingPath, @"Autodesk\C3D 2025\enu\Plotters\Plot Styles\MULTIPROJETOS.ctb"),
                Path.Combine(roamingPath, @"Autodesk\C3D 2025\enu\Plotters\Plot Styles\ECIA_ARQCIV.ctb"),
                Path.Combine(roamingPath, @"Autodesk\C3D 2025\enu\Plotters\Plot Styles\Padrao_Petrobras.ctb"),               
                Path.Combine(programDataPath, @"Autodesk\C3D 2025\enu\Pipes Catalog\Lista de Peças Drenagem Petrobrás"),
                Path.Combine(programDataPath, @"Autodesk\C3D 2025\enu\Pipes Catalog\BRA Metric Pipes"),



            };

            try
            {
                foreach (var destPath in destinationPaths)
                {
                    //CopyLastItem(basePath, destPath);
                }
                ed.WriteMessage("\nPasta e arquivos copiados com sucesso!");
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\nErro ao copiar a pasta: {ex.Message}");
            }
        }

        public static void CopyLastItem(string sourceBase, string destinationPath)
        {
            string itemToCopy = Path.GetFileName(destinationPath);
            string sourcePath = Path.Combine(sourceBase, itemToCopy);

            if (Directory.Exists(sourcePath))
            {
                Directory.CreateDirectory(destinationPath);
                //CopyDirectory(sourcePath, destinationPath);
            }
            else if (File.Exists(sourcePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                File.Copy(sourcePath, destinationPath, true);
            }
            else
            {
                throw new FileNotFoundException($"Item não encontrado: {sourcePath}");
            }
        }

        public static void CopyDirectory(string sourceDir, string destinationDir)
        {
            var dirSource = new DirectoryInfo(sourceDir);
            if (!dirSource.Exists)
            {
                throw new DirectoryNotFoundException($"Diretório de origem não encontrado: {sourceDir}");
            }

            Directory.CreateDirectory(destinationDir);
            var files = dirSource.GetFiles();
            foreach (var file in files)
            {
                string tempPath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(tempPath, true);
            }

            var subDirs = dirSource.GetDirectories();
            foreach (var subDir in subDirs)
            {
                string tempPath = Path.Combine(destinationDir, subDir.Name);
                //CopyDirectory(subDir.FullName, tempPath);
            } 




        }
    }


    /// <summary>
    /// Classe para gerenciar a ativação de workspaces no Civil 3D.
    /// </summary>
    public class WorkspaceActivator
    {
        /// <summary>
        /// Define o workspace ativo do Civil 3D pelo seu nome.
        /// </summary>
        /// <param name="Interface Rotinas Petrobrás">O nome exato do workspace a ser ativado.</param>
        // [CommandMethod("ActivateCivilWorkspace")] // Exemplo de como expor esta funcionalidade como um comando AutoCAD
        public void SetCivil3DWorkspace(string workspaceName)
        {
            // Verifica se o nome do workspace foi fornecido
            if (string.IsNullOrWhiteSpace(workspaceName))
            {
                Application.ShowAlertDialog("Erro: O nome do workspace não pode ser vazio.");
                return;
            }

            Document acDoc = null;
            try
            {
                // Obtém o documento ativo no AutoCAD.
                // É crucial ter um documento aberto para executar comandos.
                acDoc = Application.DocumentManager.MdiActiveDocument;

                if (acDoc == null)
                {
                    Application.ShowAlertDialog("Erro: Nenhum documento ativo encontrado.");
                    return;
                }

                // Para garantir a segurança em operações que podem interagir com o documento
                // a partir de threads que não são a thread do documento principal,
                // é uma boa prática usar DocumentLock. Embora para SendStringToExecute
                // chamado de um CommandMethod, geralmente não seja estritamente necessário,
                // é um padrão seguro.
                // using (acDoc.LockDocument())
                // {
                // Constrói a string de comando para o AutoCAD.
                // O prefixo '_' garante que o comando funcione independentemente do idioma do AutoCAD.
                // As aspas duplas "" são necessárias caso o nome do workspace contenha espaços.
                // O espaço no final da string é essencial para que SendStringToExecute
                // processe o comando como se o usuário tivesse pressionado Enter.
                string command = $"_.workspace ";

                // Envia a string de comando para a linha de comando do AutoCAD para execução.
                // O primeiro 'true' faz com que o comando seja ecoado na linha de comando.
                // Os parâmetros 'false' subsequentes controlam o escopo e a execução como script.
                acDoc.SendStringToExecute(command, true, false, false);
                acDoc.SendStringToExecute("C ", true, false, false);
                acDoc.SendStringToExecute("Interface Rotinas Petrobrás", true, false, false);
                acDoc.SendStringToExecute("\n", true, false, false);




                //acDoc.SendStringToExecute(" ", false, false, false);


                // Nota: SendStringToExecute enfileira o comando. A execução real
                // ocorre posteriormente na thread principal do AutoCAD.
                // Não há um retorno imediato indicando sucesso ou falha da execução do comando em si.
                // } // Fim do using DocumentLock

                // Opcional: Feedback visual que o comando foi enviado
                // Application.ShowAlertDialog($"Comando para definir workspace '{workspaceName}' enviado.");

            }
            catch (System.Exception ex) // Captura exceções gerais do sistema
            {
                // Em caso de erro, exibe uma mensagem para o usuário e loga o erro para depuração.
                string errorMessage = $"Erro ao tentar definir o workspace '{workspaceName}': {ex.Message}";
                System.Diagnostics.Trace.WriteLine(errorMessage); // Log para fins de desenvolvimento/depuração
                Application.ShowAlertDialog(errorMessage); // Alerta para o usuário final
            }
        }

        // Exemplo de como chamar este método a partir de outro comando ou lógica
        
        [CommandMethod("TrocarWS")]
        public static void SwitchToDesignWorkspace()
        {
            WorkspaceActivator activator = new WorkspaceActivator();
            // Substitua "Civil 3D" pelo nome exato do workspace desejado
            //activator.SetCivil3DWorkspace("Interface Rotinas Petrobrás");
        }
        
    }




}