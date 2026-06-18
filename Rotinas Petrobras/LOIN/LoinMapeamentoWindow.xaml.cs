using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using CriaProfiles;
// Aliases — o projeto tem UseWindowsForms=true, gerando ambiguidade com WPF
using OpenFileDialog  = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog  = Microsoft.Win32.SaveFileDialog;
using MessageBox      = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage  = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace AutomacoesCivil3D
{
    internal partial class LoinMapeamentoWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _filtroTexto = "";
        private string _novaEntradaCamada = "";
        private LoinLinha? _linhaSelecionada;
        private LoinItemMapeamento? _itemSelecionado;
        private OrigemFiltroItem? _filtroOrigem;
        private readonly string _caminhoConfig;
        private readonly ICollectionView _mapeamentosFiltrados;

        public ObservableCollection<LoinLinha>          LinhasLoin          { get; } = new();
        public ObservableCollection<LoinItemMapeamento> Mapeamentos         { get; } = new();
        public ObservableCollection<OrigemFiltroItem>   OrigensDisponiveis  { get; } = new();

        public ICollectionView MapeamentosFiltrados => _mapeamentosFiltrados;

        public string FiltroTexto
        {
            get => _filtroTexto;
            set { _filtroTexto = value; OnPropertyChanged(); _mapeamentosFiltrados.Refresh(); }
        }

        public OrigemFiltroItem? FiltroOrigem
        {
            get => _filtroOrigem;
            set { _filtroOrigem = value; OnPropertyChanged(); _mapeamentosFiltrados.Refresh(); }
        }

        public string NovaEntradaCamada
        {
            get => _novaEntradaCamada;
            set { _novaEntradaCamada = value; OnPropertyChanged(); }
        }

        public LoinLinha? LinhaSelecionada
        {
            get => _linhaSelecionada;
            set
            {
                _linhaSelecionada = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LinhaSelecionadaVisivel));
            }
        }

        public LoinItemMapeamento? ItemSelecionado
        {
            get => _itemSelecionado;
            set { _itemSelecionado = value; OnPropertyChanged(); }
        }

        public string EstatisticasMapeamento
        {
            get
            {
                int total  = Mapeamentos.Count;
                int mapped = Mapeamentos.Count(m => m.Mapeado);
                if (total == 0) return "Nenhuma entrada carregada";
                int nao = total - mapped;
                return $"{mapped}/{total} mapeados" + (nao > 0 ? $"  •  {nao} sem mapeamento" : "");
            }
        }

        // Visibilidade do painel de atributos da linha LOIN selecionada
        // (qualificado para evitar conflito com Autodesk.AutoCAD.DatabaseServices.Visibility)
        public System.Windows.Visibility LinhaSelecionadaVisivel
            => _linhaSelecionada != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public string CaminhoConfigExibido => _caminhoConfig;

        // -------- Construtor --------

        internal LoinMapeamentoWindow(LoinMapeamentoConfig config, string caminhoConfig)
        {
            _caminhoConfig = caminhoConfig;
            InitializeComponent();
            DataContext = this;

            _mapeamentosFiltrados = CollectionViewSource.GetDefaultView(Mapeamentos);
            _mapeamentosFiltrados.Filter = FiltrarMapeamento;

            Mapeamentos.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(EstatisticasMapeamento));
                AtualizarOrigensDisponiveis();
            };

            AtualizarOrigensDisponiveis();
            CarregarDoConfig(config);
        }

        // -------- Filtro --------

        private bool FiltrarMapeamento(object obj)
        {
            if (obj is not LoinItemMapeamento item) return false;

            // Filtro por origem (ComboBox). Item "Todas" não filtra nada.
            if (_filtroOrigem != null && !_filtroOrigem.IsTodas)
            {
                if (!string.Equals(item.Origem, _filtroOrigem.Origem, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (string.IsNullOrWhiteSpace(_filtroTexto)) return true;

            string f = _filtroTexto;
            return item.Camada.Contains(f, StringComparison.OrdinalIgnoreCase)
                || item.Origem.Contains(f, StringComparison.OrdinalIgnoreCase)
                || (item.LoinLinhaSelecionada?.DisplayLabel?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        // Reconstrói a lista do ComboBox a partir das origens reais em Mapeamentos.
        // Inclui contagem por origem ("Layer (45)") para dar pista visual do volume.
        // Preserva seleção atual quando a origem ainda existe.
        private void AtualizarOrigensDisponiveis()
        {
            var grupos = Mapeamentos
                .Where(m => !string.IsNullOrWhiteSpace(m.Origem))
                .GroupBy(m => m.Origem, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new OrigemFiltroItem
                {
                    Origem     = g.Key,
                    Quantidade = g.Count(),
                    IsTodas    = false
                })
                .ToList();

            int total = Mapeamentos.Count;
            string? origemAtual = _filtroOrigem?.IsTodas == false ? _filtroOrigem.Origem : null;

            OrigensDisponiveis.Clear();
            OrigensDisponiveis.Add(new OrigemFiltroItem
            {
                Origem     = "",
                Quantidade = total,
                IsTodas    = true
            });
            foreach (var g in grupos)
                OrigensDisponiveis.Add(g);

            OrigemFiltroItem? destino = origemAtual == null
                ? OrigensDisponiveis[0]
                : OrigensDisponiveis.FirstOrDefault(o =>
                    !o.IsTodas && string.Equals(o.Origem, origemAtual, StringComparison.OrdinalIgnoreCase))
                  ?? OrigensDisponiveis[0];

            if (!ReferenceEquals(destino, _filtroOrigem))
                FiltroOrigem = destino;
        }

        // -------- Carga e geração de config --------

        private void CarregarDoConfig(LoinMapeamentoConfig config)
        {
            LinhasLoin.Clear();
            foreach (var dto in config.TabelaLoin)
                LinhasLoin.Add(DtoParaLinha(dto));

            Mapeamentos.Clear();
            foreach (var dto in config.Mapeamentos)
            {
                LoinLinha? linha = LinhasLoin.FirstOrDefault(l => l.Id == dto.LoinLinhaId);
                Mapeamentos.Add(new LoinItemMapeamento
                {
                    Camada               = dto.Camada,
                    Origem               = dto.Origem,
                    LinhasDisponiveis    = LinhasLoin,
                    LoinLinhaSelecionada = linha
                });
            }
        }

        private LoinMapeamentoConfig GerarConfig()
        {
            var config = new LoinMapeamentoConfig();
            config.TabelaLoin   = LinhasLoin.Select(LinhaParaDto).ToList();
            config.Mapeamentos  = Mapeamentos.Select(m => new LoinItemMapeamentoDto
            {
                Camada      = m.Camada,
                Origem      = m.Origem,
                LoinLinhaId = m.LoinLinhaId
            }).ToList();
            return config;
        }

        // -------- Painel LOIN: importação --------

        private void ImportarCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "CSV|*.csv|Todos|*.*",
                Title  = "Importar Tabela LOIN (CSV)"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                AdicionarLinhasImportadas(LoinMapeamentoService.ImportarCsv(dlg.FileName));
            }
            catch (Exception ex)
            {
                MsgErro($"Erro ao importar CSV:\n{ex.Message}", "Importar CSV");
            }
        }

        private void ImportarExcel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Excel|*.xlsx;*.xlsm|Todos|*.*",
                Title  = "Importar Tabela LOIN (Excel)"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                AdicionarLinhasImportadas(LoinMapeamentoService.ImportarExcel(dlg.FileName));
            }
            catch (Exception ex)
            {
                MsgErro($"Erro ao importar Excel:\n{ex.Message}", "Importar Excel");
            }
        }

        private void AdicionarLinhasImportadas(LoinImportResult resultado)
        {
            int adicionados = 0, atualizados = 0;
            foreach (var dto in resultado.Linhas)
            {
                var existente = LinhasLoin.FirstOrDefault(l => l.Id == dto.Id);
                if (existente != null)
                {
                    existente.Elemento              = dto.Elemento;
                    existente.Disciplina            = dto.Disciplina;
                    existente.Fase                  = dto.Fase;
                    existente.LoG                   = dto.LoG;
                    existente.LoI                   = dto.LoI;
                    existente.AtributosObrigatorios = dto.AtributosObrigatorios;
                    existente.AtributosOpcionais    = dto.AtributosOpcionais;
                    existente.Observacao            = dto.Observacao;
                    existente.Cor                   = ResolverCor(dto.Cor, dto.Observacao);
                    existente.IfcClass              = ResolverObs(dto.IfcClass,       dto.Observacao, "IFC");
                    existente.PredefinedType        = ResolverObs(dto.PredefinedType, dto.Observacao, "TYPE");
                    existente.SourceSheet           = dto.SourceSheet;
                    existente.SourceRow             = dto.SourceRow;
                    atualizados++;
                }
                else
                {
                    LinhasLoin.Add(DtoParaLinha(dto));
                    adicionados++;
                }
            }

            // Auto-mapeamentos vindos da planilha (coluna LAYER preenchida).
            // Evita duplicar entradas existentes com mesma Camada+Origem.
            int autoMapeados = 0;
            foreach (var m in resultado.Mapeamentos)
            {
                if (string.IsNullOrWhiteSpace(m.Camada)) continue;
                LoinLinha? linha = LinhasLoin.FirstOrDefault(l => l.Id == m.LoinLinhaId);
                if (linha == null) continue;

                var existente = Mapeamentos.FirstOrDefault(x =>
                    string.Equals(x.Camada, m.Camada, StringComparison.OrdinalIgnoreCase)
                    && x.Origem == m.Origem);

                if (existente != null)
                {
                    existente.LoinLinhaSelecionada = linha;
                }
                else
                {
                    Mapeamentos.Add(new LoinItemMapeamento
                    {
                        Camada               = m.Camada,
                        Origem               = m.Origem,
                        LinhasDisponiveis    = LinhasLoin,
                        LoinLinhaSelecionada = linha
                    });
                    autoMapeados++;
                }
            }
            OnPropertyChanged(nameof(EstatisticasMapeamento));

            string msg = $"Importação concluída.\n" +
                         $"  • {adicionados} linha(s) LOIN adicionada(s)\n" +
                         $"  • {atualizados} linha(s) atualizada(s)";
            if (autoMapeados > 0)
                msg += $"\n  • {autoMapeados} mapeamento(s) auto-criado(s) a partir da coluna LAYER";

            MessageBox.Show(msg, "Importar LOIN", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // -------- Painel LOIN: CRUD --------

        private void AdicionarLinha_Click(object sender, RoutedEventArgs e)
        {
            int proxId = LinhasLoin.Count + 1;
            var nova   = new LoinLinha { Id = $"L-{proxId:D2}" };
            LinhasLoin.Add(nova);
            GridLoin.ScrollIntoView(nova);
            GridLoin.SelectedItem = nova;
        }

        private void RemoverLinha_Click(object sender, RoutedEventArgs e)
        {
            if (LinhaSelecionada == null) return;
            if (MessageBox.Show($"Remover linha [{LinhaSelecionada.Id}] {LinhaSelecionada.Elemento}?\n" +
                                "Mapeamentos que referenciam esta linha serão desmapeados.",
                                "Remover linha LOIN", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            // Desmapeia itens que apontavam para esta linha
            foreach (var item in Mapeamentos.Where(m => m.LoinLinhaSelecionada == LinhaSelecionada).ToList())
                item.LoinLinhaSelecionada = null;

            LinhasLoin.Remove(LinhaSelecionada);
            OnPropertyChanged(nameof(EstatisticasMapeamento));
        }

        // -------- Painel Mapeamento: cargas --------

        private void CarregarLayers_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var db = Manager.DocData;
                int adicionadas = 0;

                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    foreach (ObjectId id in lt)
                    {
                        LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        string nome = ltr.Name;
                        if (Mapeamentos.Any(m => m.Camada == nome && m.Origem == "Layer")) continue;
                        Mapeamentos.Add(new LoinItemMapeamento
                        {
                            Camada            = nome,
                            Origem            = "Layer",
                            LinhasDisponiveis = LinhasLoin
                        });
                        adicionadas++;
                    }
                }

                OnPropertyChanged(nameof(EstatisticasMapeamento));
                Manager.DocEditor.WriteMessage($"\n{adicionadas} layer(s) adicionada(s) ao mapeamento LOIN.");
            }
            catch (Exception ex)
            {
                MsgErro($"Erro ao carregar layers:\n{ex.Message}", "Carregar Layers");
            }
        }

        // Carrega os codes reais usados pelos corredores Civil 3D do desenho ativo
        // (Shape codes, Link codes e Point codes), assim como faz LoinCodeSetStyleCorredores.
        // Inclui também entradas dos Code Set Styles já registrados no documento.
        private void CarregarCodes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CivilDocument? civilDoc = Manager.DocCivil;
                if (civilDoc == null)
                {
                    MessageBox.Show("Nenhum documento Civil 3D ativo.", "Carregar Codes",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Document doc = Manager.DocCad;
                Database db  = Manager.DocData;

                // Coletas separadas para refletir a origem real no grid
                var shapeCodes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                var linkCodes  = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                var pointCodes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                var styleCodes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                var avisos     = new List<string>();
                int corredores = 0;

                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    // 1) Codes dos corredores
                    foreach (ObjectId corridorId in civilDoc.CorridorCollection)
                    {
                        Corridor? corridor = tr.GetObject(corridorId, OpenMode.ForRead, false) as Corridor;
                        if (corridor == null || corridor.IsReferenceObject) continue;

                        corredores++;
                        ColetarCodes(shapeCodes, () => corridor.GetShapeCodes(), corridor.Name, "shape", avisos);
                        ColetarCodes(linkCodes,  () => corridor.GetLinkCodes(),  corridor.Name, "link",  avisos);
                        ColetarCodes(pointCodes, () => corridor.GetPointCodes(), corridor.Name, "point", avisos);
                    }

                    // 2) Codes já registrados em Code Set Styles do documento
                    try
                    {
                        foreach (ObjectId cssId in civilDoc.Styles.CodeSetStyles)
                        {
                            CodeSetStyle? css = tr.GetObject(cssId, OpenMode.ForRead, false) as CodeSetStyle;
                            if (css == null) continue;
                            foreach (CodeSetStyleItem item in css)
                            {
                                if (!string.IsNullOrWhiteSpace(item.Code))
                                    styleCodes.Add(item.Code.Trim());
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        avisos.Add($"Falha ao ler Code Set Styles: {ex.Message}");
                    }

                    tr.Commit();
                }

                int novosShape = AdicionarCodesAoGrid(shapeCodes, "Corridor-Shape");
                int novosLink  = AdicionarCodesAoGrid(linkCodes,  "Corridor-Link");
                int novosPoint = AdicionarCodesAoGrid(pointCodes, "Corridor-Point");
                int novosCss   = AdicionarCodesAoGrid(styleCodes, "Code Set Style");

                int totalCodes = shapeCodes.Count + linkCodes.Count + pointCodes.Count + styleCodes.Count;
                int totalNovos = novosShape + novosLink + novosPoint + novosCss;

                OnPropertyChanged(nameof(EstatisticasMapeamento));

                string msg = $"{corredores} corredor(es) inspecionado(s).\n" +
                             $"Codes únicos coletados:\n" +
                             $"  • Shape:  {shapeCodes.Count}  ({novosShape} novos)\n" +
                             $"  • Link:   {linkCodes.Count}  ({novosLink} novos)\n" +
                             $"  • Point:  {pointCodes.Count}  ({novosPoint} novos)\n" +
                             $"  • CodeSet Styles: {styleCodes.Count}  ({novosCss} novos)\n" +
                             $"\nTotal: {totalNovos} nova(s) entrada(s) de {totalCodes} code(s).";

                if (avisos.Count > 0)
                    msg += "\n\nAvisos:\n  • " + string.Join("\n  • ", avisos.Take(5));

                if (totalCodes == 0)
                {
                    msg = "Nenhum code style encontrado.\n\n" +
                          "Verifique se o desenho possui corredores Civil 3D " +
                          "(não-referência) ou Code Set Styles criados.";
                }

                MessageBox.Show(msg, "Carregar Codes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MsgErro($"Erro ao carregar codes:\n{ex.Message}", "Carregar Codes");
            }
        }

        private static void ColetarCodes(
            ICollection<string> destino, Func<string[]> getter, string nomeCorredor, string tipo, List<string> avisos)
        {
            try
            {
                foreach (string code in getter() ?? Array.Empty<string>())
                    if (!string.IsNullOrWhiteSpace(code))
                        destino.Add(code.Trim());
            }
            catch (Exception ex)
            {
                avisos.Add($"Falha ao ler {tipo} codes do corredor '{nomeCorredor}': {ex.Message}");
            }
        }

        private int AdicionarCodesAoGrid(IEnumerable<string> codes, string origem)
        {
            int novos = 0;
            foreach (string code in codes)
            {
                if (Mapeamentos.Any(m => m.Camada == code && m.Origem == origem)) continue;
                Mapeamentos.Add(new LoinItemMapeamento
                {
                    Camada            = code,
                    Origem            = origem,
                    LinhasDisponiveis = LinhasLoin
                });
                novos++;
            }
            return novos;
        }

        // -------- Painel Mapeamento: entradas manuais --------

        private void AdicionarEntradaManual_Click(object sender, RoutedEventArgs e)
        {
            string nome = NovaEntradaCamada.Trim();
            if (string.IsNullOrEmpty(nome)) return;

            if (Mapeamentos.Any(m => m.Camada == nome && m.Origem == "Manual"))
            {
                MessageBox.Show($"Entrada '{nome}' já existe.", "Adicionar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var novoItem = new LoinItemMapeamento
            {
                Camada            = nome,
                Origem            = "Manual",
                LinhasDisponiveis = LinhasLoin
            };
            Mapeamentos.Add(novoItem);
            GridMapeamento.ScrollIntoView(novoItem);
            NovaEntradaCamada = "";
            OnPropertyChanged(nameof(EstatisticasMapeamento));
        }

        private void RemoverEntrada_Click(object sender, RoutedEventArgs e)
        {
            if (ItemSelecionado == null) return;
            Mapeamentos.Remove(ItemSelecionado);
            OnPropertyChanged(nameof(EstatisticasMapeamento));
        }

        private void LimparSemMapeamento_Click(object sender, RoutedEventArgs e)
        {
            var naoMapeados = Mapeamentos.Where(m => !m.Mapeado).ToList();
            if (naoMapeados.Count == 0)
            {
                MessageBox.Show("Todos os itens já estão mapeados.", "Limpar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show($"Remover {naoMapeados.Count} entrada(s) sem linha LOIN associada?",
                                "Confirmar remoção", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            foreach (var item in naoMapeados)
                Mapeamentos.Remove(item);
            OnPropertyChanged(nameof(EstatisticasMapeamento));
        }

        // Auto-ajusta a largura das colunas dos dois DataGrids ao conteúdo.
        // Preserva o comportamento Star (colunas que ocupam o espaço restante).
        private void AutoAjustarColunas_Click(object sender, RoutedEventArgs e)
        {
            AutoFitColumns(GridMapeamento);
            AutoFitColumns(GridLoin);
        }

        private static void AutoFitColumns(System.Windows.Controls.DataGrid grid)
        {
            if (grid == null) return;

            foreach (var col in grid.Columns)
            {
                bool eraStar = col.Width.IsStar;
                bool resizable = col.CanUserResize;
                if (!resizable) continue;

                // Trick WPF: setar Auto força o engine a medir cells + header
                col.Width = new System.Windows.Controls.DataGridLength(
                    1, System.Windows.Controls.DataGridLengthUnitType.Auto);

                // Trava no valor calculado para o usuário ainda poder arrastar manualmente
                col.Width = new System.Windows.Controls.DataGridLength(col.ActualWidth);

                // Restaura comportamento Star (coluna "Linha LOIN") para continuar preenchendo
                if (eraStar)
                    col.Width = new System.Windows.Controls.DataGridLength(
                        1, System.Windows.Controls.DataGridLengthUnitType.Star);
            }
        }

        private void ExportarCsvMapeamento_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter   = "CSV|*.csv",
                Title    = "Exportar Mapeamento LOIN",
                FileName = "loin_mapeamento_export.csv"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                LoinMapeamentoService.ExportarCsvMapeamento(dlg.FileName, Mapeamentos);
                MessageBox.Show("Exportação concluída.", "Exportar CSV", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MsgErro($"Erro ao exportar:\n{ex.Message}", "Exportar CSV");
            }
        }

        // -------- Footer --------

        private void SalvarMapeamento_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoinMapeamentoService.Salvar(_caminhoConfig, GerarConfig());
                Manager.DocEditor.WriteMessage($"\nMapeamento LOIN salvo em: {_caminhoConfig}");
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MsgErro($"Erro ao salvar:\n{ex.Message}", "Salvar Mapeamento");
            }
        }

        private void Cancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // -------- Helpers --------

        private static LoinLinha DtoParaLinha(LoinLinhaDto d) => new()
        {
            Id                    = d.Id,
            Elemento              = d.Elemento,
            Disciplina            = d.Disciplina,
            Fase                  = d.Fase,
            LoG                   = d.LoG,
            LoI                   = d.LoI,
            AtributosObrigatorios = d.AtributosObrigatorios,
            AtributosOpcionais    = d.AtributosOpcionais,
            Observacao            = d.Observacao,
            Cor                   = ResolverCor(d.Cor, d.Observacao),
            IfcClass              = ResolverObs(d.IfcClass,       d.Observacao, "IFC"),
            PredefinedType        = ResolverObs(d.PredefinedType, d.Observacao, "TYPE"),
            SourceSheet           = d.SourceSheet,
            SourceRow             = d.SourceRow
        };

        private static LoinLinhaDto LinhaParaDto(LoinLinha l) => new()
        {
            Id                    = l.Id,
            Elemento              = l.Elemento,
            Disciplina            = l.Disciplina,
            Fase                  = l.Fase,
            LoG                   = l.LoG,
            LoI                   = l.LoI,
            AtributosObrigatorios = l.AtributosObrigatorios,
            AtributosOpcionais    = l.AtributosOpcionais,
            Observacao            = l.Observacao,
            Cor                   = l.Cor,
            IfcClass              = l.IfcClass,
            PredefinedType        = l.PredefinedType,
            SourceSheet           = l.SourceSheet,
            SourceRow             = l.SourceRow
        };

        // Retrocompat: JSONs gerados antes do campo Cor guardam a cor em Observacao
        // como "COR=AMARELO". Resolve preferindo o campo tipado.
        private static string ResolverCor(string? corDireta, string? observacao)
            => ResolverObs(corDireta, observacao, "COR");

        // Mesma lógica para qualquer chave Observacao no formato "CHAVE=valor; CHAVE2=valor2".
        // Usado para Cor, IFC, TYPE — todas migradas de Observacao para campos tipados.
        private static string ResolverObs(string? direto, string? observacao, string chave)
        {
            if (!string.IsNullOrWhiteSpace(direto))
                return direto!.Trim();

            if (string.IsNullOrWhiteSpace(observacao))
                return "";

            string prefixo = chave + "=";
            foreach (string part in observacao.Split(';'))
            {
                string p = part.Trim();
                if (p.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
                    return p.Substring(prefixo.Length).Trim();
            }
            return "";
        }

        private static void MsgErro(string msg, string titulo)
            => MessageBox.Show(msg, titulo, MessageBoxButton.OK, MessageBoxImage.Warning);

        protected void OnPropertyChanged([CallerMemberName] string? prop = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    // Item exibido no ComboBox de filtro de origem.
    // IsTodas=true representa "sem filtro"; demais itens filtram por Origem exata.
    internal sealed class OrigemFiltroItem
    {
        public string Origem     { get; init; } = "";
        public int    Quantidade { get; init; }
        public bool   IsTodas    { get; init; }

        public string Rotulo => IsTodas
            ? $"Todas as origens ({Quantidade})"
            : $"{Origem}  ({Quantidade})";

        public override string ToString() => Rotulo;
    }
}
