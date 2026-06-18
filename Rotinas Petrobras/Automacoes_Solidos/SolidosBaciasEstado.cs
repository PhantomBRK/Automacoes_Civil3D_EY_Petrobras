using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using Autodesk.AutoCAD.DatabaseServices;

namespace AutomacoesCivil3D
{
    // Modelo de persistência para o par SOL_DESCONECTAR_BACIAS / SOL_RECONECTAR_BACIAS.
    // Salvo num JSON ao lado do .dwg com nome "<dwg>.solidos-bacias.json".
    //
    // Ao desconectar TODAS as bacias do desenho, gravamos aqui o mapeamento
    // dispositivo -> bacias, em handles (hexadecimal). Sem esse mapeamento o SOLIDOS
    // não tem como saber qual bacia voltava em qual dispositivo, então a reconexão
    // depende inteiramente deste arquivo.
    public sealed class SolidosBaciasEstado
    {
        public const int CurrentVersion = 1;

        [JsonPropertyName("version")]
        public int Version { get; set; } = CurrentVersion;

        [JsonPropertyName("dwg")]
        public string DwgPath { get; set; } = "";

        [JsonPropertyName("dispositivos")]
        public List<DispositivoBacias> Dispositivos { get; set; } = new List<DispositivoBacias>();

        public sealed class DispositivoBacias
        {
            [JsonPropertyName("device_handle")]
            public string DeviceHandle { get; set; }

            [JsonPropertyName("device_name")]
            public string DeviceName { get; set; }

            // Handles (string hexadecimal) das bacias que estavam conectadas ao dispositivo.
            [JsonPropertyName("bacias_handles")]
            public List<string> BaciasHandles { get; set; } = new List<string>();
        }

        // Caminho do arquivo de estado para um DWG (mesma pasta, mesmo nome base + ".solidos-bacias.json").
        // Retorna null se o DWG ainda não foi salvo em disco (não tem path absoluto).
        public static string ResolverCaminhoEstado(string dwgFullPath)
        {
            if (string.IsNullOrWhiteSpace(dwgFullPath)) return null;
            if (!Path.IsPathRooted(dwgFullPath)) return null;
            string folder = Path.GetDirectoryName(dwgFullPath);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
            string fileName = Path.GetFileNameWithoutExtension(dwgFullPath) + ".solidos-bacias.json";
            return Path.Combine(folder, fileName);
        }

        public static SolidosBaciasEstado Carregar(string estadoPath)
        {
            if (string.IsNullOrEmpty(estadoPath) || !File.Exists(estadoPath))
            {
                return new SolidosBaciasEstado { DwgPath = "" };
            }

            try
            {
                string json = File.ReadAllText(estadoPath);
                JsonSerializerOptions opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                SolidosBaciasEstado estado = JsonSerializer.Deserialize<SolidosBaciasEstado>(json, opt);
                if (estado == null) return new SolidosBaciasEstado();
                if (estado.Dispositivos == null) estado.Dispositivos = new List<DispositivoBacias>();
                return estado;
            }
            catch
            {
                // Arquivo corrompido: ignora e retorna novo.
                return new SolidosBaciasEstado();
            }
        }

        public void Salvar(string estadoPath)
        {
            if (string.IsNullOrEmpty(estadoPath)) return;
            JsonSerializerOptions opt = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string json = JsonSerializer.Serialize(this, opt);
            File.WriteAllText(estadoPath, json);
        }

        // Registra (ou faz merge) das bacias de um dispositivo.
        // Se o dispositivo já existir no estado, faz a UNIÃO dos handles de bacia — assim,
        // rodar o desconectar mais de uma vez antes de reconectar não perde o mapeamento
        // gravado na primeira passada.
        public void RegistrarOuMesclar(string deviceHandle, string deviceName, IEnumerable<string> baciasHandles)
        {
            if (string.IsNullOrEmpty(deviceHandle)) return;
            List<string> novas = (baciasHandles ?? Enumerable.Empty<string>())
                .Where(h => !string.IsNullOrEmpty(h))
                .ToList();
            if (novas.Count == 0) return;

            DispositivoBacias existente = Dispositivos.FirstOrDefault(
                d => string.Equals(d.DeviceHandle, deviceHandle, StringComparison.OrdinalIgnoreCase));

            if (existente == null)
            {
                Dispositivos.Add(new DispositivoBacias
                {
                    DeviceHandle = deviceHandle,
                    DeviceName = deviceName,
                    BaciasHandles = novas
                });
                return;
            }

            existente.DeviceName = deviceName ?? existente.DeviceName;
            foreach (string h in novas)
            {
                if (!existente.BaciasHandles.Any(x => string.Equals(x, h, StringComparison.OrdinalIgnoreCase)))
                    existente.BaciasHandles.Add(h);
            }
        }

        // Reaproveita os conversores de handle já existentes no estado de incêndio,
        // que são genéricos (não dependem do modelo de dados daquele comando).
        public static string ObjectIdToHandle(ObjectId id)
            => SolidosVazaoIncendioEstado.ObjectIdToHandle(id);

        public static bool TryHandleToObjectId(Database db, string handleHex, out ObjectId id)
            => SolidosVazaoIncendioEstado.TryHandleToObjectId(db, handleHex, out id);
    }
}
