using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AutomacoesCivil3D
{
    // Dados de projeto que populam o "Pset_A - Dados de Projeto" em todos os
    // sólidos extraídos dos corredores. Preenchido via janela LOIN_DADOS_PROJETO
    // (LOIN_PROJ), persistido em loin_projeto.json ao lado do DWG.
    internal sealed class LoinProjeto : ObservableObject
    {
        private string _autor = "";
        private string _contratante = "";
        private string _data = "";
        private string _disciplina = "";
        private string _faseProjeto = "";
        private string _localizacao = "";
        private string _nomeProjeto = "";
        private string _nomeProjetoAlt = "";
        private string _sistemaCoordenada = "";

        public string Autor              { get => _autor;              set => SetProperty(ref _autor, value); }
        public string Contratante        { get => _contratante;        set => SetProperty(ref _contratante, value); }
        public string Data               { get => _data;               set => SetProperty(ref _data, value); }
        public string Disciplina         { get => _disciplina;         set => SetProperty(ref _disciplina, value); }
        public string FaseProjeto        { get => _faseProjeto;        set => SetProperty(ref _faseProjeto, value); }
        public string Localizacao        { get => _localizacao;        set => SetProperty(ref _localizacao, value); }
        public string NomeProjeto        { get => _nomeProjeto;        set => SetProperty(ref _nomeProjeto, value); }
        public string NomeProjetoAlt     { get => _nomeProjetoAlt;     set => SetProperty(ref _nomeProjetoAlt, value); }
        public string SistemaCoordenada  { get => _sistemaCoordenada;  set => SetProperty(ref _sistemaCoordenada, value); }
    }

    internal sealed class LoinProjetoDto
    {
        public string Autor              { get; set; } = "";
        public string Contratante        { get; set; } = "";
        public string Data               { get; set; } = "";
        public string Disciplina         { get; set; } = "";
        public string FaseProjeto        { get; set; } = "";
        public string Localizacao        { get; set; } = "";
        public string NomeProjeto        { get; set; } = "";
        public string NomeProjetoAlt     { get; set; } = "";
        public string SistemaCoordenada  { get; set; } = "";
        public DateTime UltimaAlteracao  { get; set; } = DateTime.Now;
    }

    internal static class LoinProjetoService
    {
        private const string FileName = "loin_projeto.json";

        private static readonly JsonSerializerOptions _opts = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // Mesma estratégia do LoinMapeamentoService: pasta do DWG quando salvo,
        // bundle (%AppData%) quando o desenho ainda não tem caminho em disco.
        public static string ResolverCaminhoConfig(string? caminhoDrawing = null)
        {
            string pasta = string.IsNullOrEmpty(caminhoDrawing)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Autodesk", "ApplicationPlugins", "AutomacoesPetrobras.bundle")
                : (Path.GetDirectoryName(caminhoDrawing) ?? ".");

            Directory.CreateDirectory(pasta);
            return Path.Combine(pasta, FileName);
        }

        public static LoinProjetoDto Carregar(string caminho)
        {
            if (!File.Exists(caminho))
                return new LoinProjetoDto();

            try
            {
                string json = File.ReadAllText(caminho, Encoding.UTF8);
                return JsonSerializer.Deserialize<LoinProjetoDto>(json, _opts) ?? new LoinProjetoDto();
            }
            catch
            {
                return new LoinProjetoDto();
            }
        }

        public static void Salvar(string caminho, LoinProjetoDto dto)
        {
            dto.UltimaAlteracao = DateTime.Now;
            File.WriteAllText(caminho, JsonSerializer.Serialize(dto, _opts), Encoding.UTF8);
        }

        public static LoinProjeto DtoParaVm(LoinProjetoDto d) => new()
        {
            Autor             = d.Autor,
            Contratante       = d.Contratante,
            Data              = d.Data,
            Disciplina        = d.Disciplina,
            FaseProjeto       = d.FaseProjeto,
            Localizacao       = d.Localizacao,
            NomeProjeto       = d.NomeProjeto,
            NomeProjetoAlt    = d.NomeProjetoAlt,
            SistemaCoordenada = d.SistemaCoordenada
        };

        public static LoinProjetoDto VmParaDto(LoinProjeto v) => new()
        {
            Autor             = v.Autor,
            Contratante       = v.Contratante,
            Data              = v.Data,
            Disciplina        = v.Disciplina,
            FaseProjeto       = v.FaseProjeto,
            Localizacao       = v.Localizacao,
            NomeProjeto       = v.NomeProjeto,
            NomeProjetoAlt    = v.NomeProjetoAlt,
            SistemaCoordenada = v.SistemaCoordenada
        };
    }
}
