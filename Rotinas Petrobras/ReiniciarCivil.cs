using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Exception = System.Exception;

namespace AutomacoesCivil3D
{

    public class ReiniciarCivil
    {
        [CommandMethod("ReiniciarCivil")]
        public static void Main(string[] args)
        {
            // Exemplo de uso:
            // RestartCivil3D(); 
            // Ou com um caminho específico se necessário:
            // RestartCivil3D(@"C:\Program Files\Autodesk\AutoCAD 2024\acad.exe");

            Console.WriteLine("Reiniciando Civil 3D...");
            RestartCivil3D();
            Console.WriteLine("Processo de reinício concluído.");
        }

        /// <summary>
        /// Encontra e encerra processos do Civil 3D e tenta iniciá-lo novamente.
        /// </summary>
        /// <param name="civil3DPath">Caminho opcional para o executável do Civil 3D. 
        /// Se nulo, tenta encontrar um caminho comum.</param>
        public static void RestartCivil3D(string civil3DPath = null)
        {
            // Nome comum do processo do AutoCAD/Civil 3D
            string processName = "acad";

            Console.WriteLine($"Procurando processos com o nome '{processName}'...");

            // Fecha todas as instâncias do Civil 3D que estiverem abertas
            Process[] civil3DProcesses = Process.GetProcessesByName(processName);

            if (civil3DProcesses.Length > 0)
            {
                Console.WriteLine($"Encontradas {civil3DProcesses.Length} instâncias do Civil 3D. Encerrando...");
                foreach (var process in civil3DProcesses)
                {
                    try
                    {
                        Console.WriteLine($"Encerrando processo ID: {process.Id}");
                        // process.CloseMainWindow(); // Tenta fechar graciosamente primeiro (pode pedir para salvar)
                        // Se CloseMainWindow não funcionar ou não for desejado, use Kill
                        process.Kill(); // Encerramento abrupto
                        process.WaitForExit(10000); // Espera até 10 segundos pelo encerramento
                        if (!process.HasExited)
                        {
                            Console.WriteLine($"Processo ID {process.Id} não encerrou após esperar. Pode precisar de intervenção manual ou permissões elevadas.");
                        }
                        else
                        {
                            Console.WriteLine($"Processo ID {process.Id} encerrado.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro ao tentar encerrar o processo ID {process.Id}: {ex.Message}");
                        // Continua para o próximo processo
                    }
                }
                Console.WriteLine("Tentativa de encerrar processos concluída.");
            }
            else
            {
                Console.WriteLine($"Nenhuma instância de '{processName}' encontrada em execução.");
            }

            // Determina o caminho do executável para iniciar
            string pathToStart = civil3DPath;
            if (string.IsNullOrEmpty(pathToStart))
            {
                // Tenta encontrar um caminho comum ou padrão
                // Este é um exemplo, o caminho exato pode variar por versão e instalação
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                pathToStart = Path.Combine(programFiles, @"Autodesk\AutoCAD 2025\acad.exe"); // Exemplo para Civil 3D 2025

                // Adicione aqui lógica para encontrar o caminho correto se necessário, 
                // talvez lendo do registro (e.g., HKEY_LOCAL_MACHINE\SOFTWARE\Autodesk\AutoCAD\<Rxx.x>\AcadLocation)
                // ou verificando múltiplas versões comuns.

                Console.WriteLine($"Usando caminho padrão ou detectado: {pathToStart}");
            }

            // Verifica se o arquivo executável existe antes de tentar iniciar
            if (!File.Exists(pathToStart))
            {
                Console.WriteLine($"ERRO: O arquivo executável não foi encontrado no caminho: {pathToStart}");
                Console.WriteLine("Por favor, verifique o caminho e as permissões.");
                return; // Sai da função se o executável não existe
            }

            // Inicia novamente o Civil 3D
            try
            {
                Console.WriteLine($"Iniciando Civil 3D a partir de: {pathToStart}");
                Process.Start(pathToStart);
                Console.WriteLine("Comando de início executado. O Civil 3D deve abrir em breve.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERRO ao tentar iniciar o Civil 3D: {ex.Message}");
                Console.WriteLine("Verifique se você tem permissões para iniciar processos e se o caminho está correto.");
            }
        }

    }
}
