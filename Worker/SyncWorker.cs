using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Worker;

public class SyncWorker : BackgroundService
{
    private static DateTime? _ultimaExecucaoEstoque;
    private static DateTime? _ultimaExecucaoPreco;
    private static DateTime? _ultimaExecucaoExclusao;
    private static DateTime? _ultimaExecucaoDados;
    private static bool _cargaInicialExecutada;
    private static readonly SemaphoreSlim _execucaoLock = new(1, 1);

    private readonly ILogger<SyncWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SyncScheduleOptions _options;

    public SyncWorker(
        ILogger<SyncWorker> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<SyncScheduleOptions> options)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Shopee Worker iniciado");
        LogarAgendaConfigurada();

        if (!await VerificarConexaoBanco(stoppingToken))
            return;

        await ValidarTokenNaInicializacao(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecutarRotinas(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro na execucao das rotinas");
            }

            await Task.Delay(TimeSpan.FromMinutes(ObterIntervaloLoopMinutos()), stoppingToken);
        }
    }

    private async Task<bool> VerificarConexaoBanco(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();

        try
        {
            var conectado = await db.Database.CanConnectAsync(stoppingToken);

            if (!conectado)
            {
                _logger.LogError("Falha ao conectar no SQL Server");
                return false;
            }

            _logger.LogInformation("Conexao com SQL Server OK");
            return true;
        }
        catch
        {
            _logger.LogError("Falha ao conectar no SQL Server");
            return false;
        }
    }

    private async Task ValidarTokenNaInicializacao(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenSyncService>();

        _logger.LogInformation("Validando token na inicializacao do worker");
        await tokenService.ObterTokenValido();
    }

    private void LogarAgendaConfigurada()
    {
        _logger.LogInformation(
            "Modo de execucao: teste {teste}, logs de loop {logsLoop}, intervalo efetivo do loop {intervalo} minutos",
            _options.TestMode ? "ATIVO" : "DESATIVADO",
            _options.ShowLoopLogs ? "ATIVADOS" : "DESATIVADOS",
            ObterIntervaloLoopMinutos()
        );
        _logger.LogInformation(
            "Agendamento configurado: loop a cada {loop} minutos, novos produtos a partir das {cadastro}:00, estoque a cada hora nos primeiros {janelaEstoque} minutos, preco as {preco}:00 nos primeiros {janelaPreco} minutos, exclusao as {exclusao}:00 nos primeiros {janelaExclusao} minutos, dados as {dados}:00 nos primeiros {janelaDados} minutos, concorrencia global {paraleloGlobal}, delay global {delayGlobal}ms, buffer da fila {buffer}",
            ObterIntervaloLoopMinutos(),
            _options.CadastroHoraInicial,
            _options.EstoqueJanelaMinutos,
            _options.PrecoHora,
            _options.PrecoJanelaMinutos,
            _options.ExclusaoHora,
            _options.ExclusaoJanelaMinutos,
            _options.DadosHora,
            _options.DadosJanelaMinutos,
            _options.MaxParallelRequests,
            _options.DelayBetweenRequestsMs,
            _options.QueueBufferSize
        );
        _logger.LogInformation(
            "Concorrencia por fila: cadastro {cadastroParalelo}/{cadastroDelay}ms, estoque {estoqueParalelo}/{estoqueDelay}ms, preco {precoParalelo}/{precoDelay}ms, exclusao {exclusaoParalelo}/{exclusaoDelay}ms, dados {dadosParalelo}/{dadosDelay}ms",
            _options.CadastroMaxParallelRequests,
            _options.CadastroDelayBetweenRequestsMs,
            _options.EstoqueMaxParallelRequests,
            _options.EstoqueDelayBetweenRequestsMs,
            _options.PrecoMaxParallelRequests,
            _options.PrecoDelayBetweenRequestsMs,
            _options.ExclusaoMaxParallelRequests,
            _options.ExclusaoDelayBetweenRequestsMs,
            _options.DadosMaxParallelRequests,
            _options.DadosDelayBetweenRequestsMs
        );
    }

    private async Task ExecutarRotinas(CancellationToken stoppingToken)
    {
        if (!await _execucaoLock.WaitAsync(0, stoppingToken))
        {
            if (_options.ShowLoopLogs)
                _logger.LogInformation("Ciclo anterior ainda em andamento, novo loop ignorado");

            return;
        }

        if (_options.ShowLoopLogs)
            _logger.LogInformation("Iniciando ciclo do worker em {horario}", DateTime.Now);

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var cadastroProdutoService = scope.ServiceProvider.GetRequiredService<CadastroProdutoService>();
            var estoqueService = scope.ServiceProvider.GetRequiredService<EstoqueSyncService>();
            var precoService = scope.ServiceProvider.GetRequiredService<PrecoSyncService>();
            var exclusaoService = scope.ServiceProvider.GetRequiredService<ExclusaoSyncService>();
            var dadosService = scope.ServiceProvider.GetRequiredService<DadosSyncService>();
            var db = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();

            var sync = await db.SyncShopee
                .OrderBy(x => x.Id)
                .FirstAsync(stoppingToken);

            if (!sync.PartnerId.HasValue)
                throw new Exception("PARTNERID nao esta preenchido na SINCSHOPEE.");

            if (string.IsNullOrWhiteSpace(sync.ClientSecret))
                throw new Exception("CLIENTSECRET nao esta preenchido na SINCSHOPEE.");

            if (!sync.ShopId.HasValue)
                throw new Exception("SHOPID nao esta preenchido na SINCSHOPEE.");

            var executarCargaInicial = !_cargaInicialExecutada;

            var filaCadastro = (executarCargaInicial || DeveExecutarCadastro())
                ? (await cadastroProdutoService.BuscarPendentes(stoppingToken))
                    .Select(x => CriarItemFila(TipoFila.Cadastro, x))
                    .ToList()
                : [];

            var filaExclusao = (executarCargaInicial || DeveExecutarExclusao())
                ? (await exclusaoService.BuscarPendentes(stoppingToken))
                    .Select(x => CriarItemFila(TipoFila.Exclusao, x))
                    .ToList()
                : [];

            var produtosEmExclusao = filaExclusao
                .Select(x => x.ProdutoId)
                .ToHashSet();

            var filaEstoque = (executarCargaInicial || DeveExecutarEstoque())
                ? (await estoqueService.BuscarPendentes(stoppingToken))
                    .Where(x => !produtosEmExclusao.Contains(x.Id))
                    .Select(x => CriarItemFila(TipoFila.Estoque, x))
                    .ToList()
                : [];

            var filaPreco = (executarCargaInicial || DeveExecutarPreco())
                ? (await precoService.BuscarPendentes(stoppingToken))
                    .Where(x => !produtosEmExclusao.Contains(x.Id))
                    .Select(x => CriarItemFila(TipoFila.Preco, x))
                    .ToList()
                : [];

            var filaDados = (executarCargaInicial || DeveExecutarDados())
                ? (await dadosService.BuscarPendentes(stoppingToken))
                    .Where(x => !produtosEmExclusao.Contains(x.Id))
                    .Select(x => CriarItemFila(TipoFila.Dados, x))
                    .ToList()
                : [];

            if (executarCargaInicial)
            {
                _logger.LogInformation(
                    "Carga inicial ativada: novos produtos, estoque, preco, exclusao e dados serao verificados agora antes de seguir a agenda configurada"
                );
            }

            if (_options.ShowLoopLogs || filaCadastro.Count > 0 || filaEstoque.Count > 0 || filaPreco.Count > 0 || filaExclusao.Count > 0 || filaDados.Count > 0)
            {
                _logger.LogInformation(
                    "Filas carregadas: cadastro {cadastro}, estoque {estoque}, preco {preco}, exclusao {exclusao}, dados {dados}",
                    filaCadastro.Count,
                    filaEstoque.Count,
                    filaPreco.Count,
                    filaExclusao.Count,
                    filaDados.Count
                );
            }

            var fila = MontarFilaRoundRobin(filaEstoque, filaPreco, filaExclusao, filaDados, filaCadastro);

            if (fila.Count == 0)
            {
                if (_options.ShowLoopLogs)
                    _logger.LogInformation("Nenhum item pendente neste ciclo");

                return;
            }

            var resultados = await ProcessarFila(
                fila,
                sync.PartnerId.Value,
                sync.ClientSecret,
                sync.ShopId.Value,
                stoppingToken
            );
            var marcoCargaInicial = executarCargaInicial ? DateTime.Now : (DateTime?)null;

            using var checkpointScope = _scopeFactory.CreateScope();
            var estoqueCheckpointService = checkpointScope.ServiceProvider.GetRequiredService<EstoqueSyncService>();
            var precoCheckpointService = checkpointScope.ServiceProvider.GetRequiredService<PrecoSyncService>();
            var exclusaoCheckpointService = checkpointScope.ServiceProvider.GetRequiredService<ExclusaoSyncService>();
            var dadosCheckpointService = checkpointScope.ServiceProvider.GetRequiredService<DadosSyncService>();

            var estoqueProcessado = resultados.Where(x => x.Tipo == TipoFila.Estoque).ToList();
            var precoProcessado = resultados.Where(x => x.Tipo == TipoFila.Preco).ToList();
            var exclusoesProcessadas = resultados.Where(x => x.Tipo == TipoFila.Exclusao).ToList();
            var dadosProcessados = resultados.Where(x => x.Tipo == TipoFila.Dados).ToList();

            if (estoqueProcessado.Count > 0)
            {
                await estoqueCheckpointService.AtualizarCheckpoint(
                    estoqueProcessado.Select(x => new Produto { DataEstoque = x.DataReferencia }).ToList(),
                    stoppingToken,
                    executarCargaInicial,
                    marcoCargaInicial
                );
                _ultimaExecucaoEstoque = DateTime.Now;
            }

            if (precoProcessado.Count > 0)
            {
                await precoCheckpointService.AtualizarCheckpoint(
                    precoProcessado.Select(x => new Produto { DataPreco = x.DataReferencia }).ToList(),
                    stoppingToken,
                    executarCargaInicial,
                    marcoCargaInicial
                );
                _ultimaExecucaoPreco = DateTime.Now;
            }

            if (exclusoesProcessadas.Count > 0)
            {
                await exclusaoCheckpointService.AtualizarCheckpoint(
                    exclusoesProcessadas.Select(x => new Produto { DataExclusao = x.DataReferencia }).ToList(),
                    stoppingToken,
                    marcoCargaInicial
                );
                _ultimaExecucaoExclusao = DateTime.Now;
            }

            if (dadosProcessados.Count > 0)
            {
                await dadosCheckpointService.AtualizarCheckpoint(
                    dadosProcessados.Select(x => new Produto { DataDados = x.DataReferencia }).ToList(),
                    stoppingToken,
                    executarCargaInicial,
                    marcoCargaInicial
                );
                _ultimaExecucaoDados = DateTime.Now;
            }

            if (_options.ShowLoopLogs || resultados.Count > 0)
            {
                _logger.LogInformation(
                    "Processamento concluido: cadastro {cadastro}, estoque {estoque}, preco {preco}, exclusao {exclusao}, dados {dados}",
                    resultados.Count(x => x.Tipo == TipoFila.Cadastro),
                    estoqueProcessado.Count,
                    precoProcessado.Count,
                    exclusoesProcessadas.Count,
                    dadosProcessados.Count
                );
            }

            if (executarCargaInicial)
                _cargaInicialExecutada = true;
        }
        finally
        {
            _execucaoLock.Release();
        }
    }

    private async Task<List<ResultadoFila>> ProcessarFila(
        List<ItemFila> fila,
        int partnerId,
        string partnerKey,
        int shopId,
        CancellationToken cancellationToken)
    {
        var resultados = new ConcurrentBag<ResultadoFila>();
        var tokenLock = new SemaphoreSlim(1, 1);
        var ultimaSaida = DateTime.MinValue;
        var totalProcessados = 0;
        var totalFalhas = 0;
        var capacidadeFila = Math.Max(Math.Max(1, _options.QueueBufferSize), Math.Max(1, _options.MaxParallelRequests) * 2);
        var rateLock = new SemaphoreSlim(1, 1);
        var semaforosPorTipo = new Dictionary<TipoFila, SemaphoreSlim>
        {
            [TipoFila.Cadastro] = new SemaphoreSlim(Math.Max(1, _options.CadastroMaxParallelRequests), Math.Max(1, _options.CadastroMaxParallelRequests)),
            [TipoFila.Estoque] = new SemaphoreSlim(Math.Max(1, _options.EstoqueMaxParallelRequests), Math.Max(1, _options.EstoqueMaxParallelRequests)),
            [TipoFila.Preco] = new SemaphoreSlim(Math.Max(1, _options.PrecoMaxParallelRequests), Math.Max(1, _options.PrecoMaxParallelRequests)),
            [TipoFila.Exclusao] = new SemaphoreSlim(Math.Max(1, _options.ExclusaoMaxParallelRequests), Math.Max(1, _options.ExclusaoMaxParallelRequests)),
            [TipoFila.Dados] = new SemaphoreSlim(Math.Max(1, _options.DadosMaxParallelRequests), Math.Max(1, _options.DadosMaxParallelRequests))
        };
        var rateLocksPorTipo = new Dictionary<TipoFila, SemaphoreSlim>
        {
            [TipoFila.Cadastro] = new SemaphoreSlim(1, 1),
            [TipoFila.Estoque] = new SemaphoreSlim(1, 1),
            [TipoFila.Preco] = new SemaphoreSlim(1, 1),
            [TipoFila.Exclusao] = new SemaphoreSlim(1, 1),
            [TipoFila.Dados] = new SemaphoreSlim(1, 1)
        };
        var ultimaSaidaPorTipo = new ConcurrentDictionary<TipoFila, DateTime>();
        var canal = Channel.CreateBounded<ItemFila>(new BoundedChannelOptions(capacidadeFila)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false
        });

        var produtores = Task.Run(async () =>
        {
            foreach (var item in fila)
            {
                try
                {
                    await canal.Writer.WriteAsync(item, cancellationToken);
                }
                catch (Exception ex)
                {
                    canal.Writer.TryComplete(ex);
                    throw;
                }
            }

            canal.Writer.TryComplete();
        }, cancellationToken);

        var consumidores = Enumerable.Range(0, Math.Max(1, _options.MaxParallelRequests))
            .Select(_ => Task.Run(async () =>
            {
                await foreach (var item in canal.Reader.ReadAllAsync(cancellationToken))
                {
                    var bloqueioTipoAdquirido = false;

                    try
                    {
                        await semaforosPorTipo[item.Tipo].WaitAsync(cancellationToken);
                        bloqueioTipoAdquirido = true;
                        using var scope = _scopeFactory.CreateScope();
                        var tokenService = scope.ServiceProvider.GetRequiredService<TokenSyncService>();

                        await rateLock.WaitAsync(cancellationToken);
                        try
                        {
                            var espera = TimeSpan.FromMilliseconds(Math.Max(0, _options.DelayBetweenRequestsMs));
                            var proximaSaida = ultimaSaida + espera;
                            var agora = DateTime.UtcNow;

                            if (proximaSaida > agora)
                                await Task.Delay(proximaSaida - agora, cancellationToken);

                            ultimaSaida = DateTime.UtcNow;
                        }
                        finally
                        {
                            rateLock.Release();
                        }

                        await rateLocksPorTipo[item.Tipo].WaitAsync(cancellationToken);
                        try
                        {
                            var esperaTipo = TimeSpan.FromMilliseconds(ObterDelayPorTipo(item.Tipo));
                            var ultimaTipo = ultimaSaidaPorTipo.TryGetValue(item.Tipo, out var saidaTipo)
                                ? saidaTipo
                                : DateTime.MinValue;
                            var proximaSaidaTipo = ultimaTipo + esperaTipo;
                            var agoraTipo = DateTime.UtcNow;

                            if (proximaSaidaTipo > agoraTipo)
                                await Task.Delay(proximaSaidaTipo - agoraTipo, cancellationToken);

                            ultimaSaidaPorTipo[item.Tipo] = DateTime.UtcNow;
                        }
                        finally
                        {
                            rateLocksPorTipo[item.Tipo].Release();
                        }

                        string accessToken;
                        await tokenLock.WaitAsync(cancellationToken);
                        try
                        {
                            accessToken = await tokenService.ObterTokenValido();
                        }
                        finally
                        {
                            tokenLock.Release();
                        }

                        var dataSincronizacao = DateTime.Now;

                        switch (item.Tipo)
                        {
                            case TipoFila.Cadastro:
                                await scope.ServiceProvider.GetRequiredService<CadastroProdutoService>()
                                    .ProcessarProduto(item.ProdutoId, accessToken, partnerId, partnerKey, shopId, cancellationToken);
                                break;
                            case TipoFila.Estoque:
                                await scope.ServiceProvider.GetRequiredService<EstoqueSyncService>()
                                    .ProcessarProduto(item.ProdutoId, accessToken, partnerId, partnerKey, shopId, dataSincronizacao, cancellationToken);
                                break;
                            case TipoFila.Preco:
                                await scope.ServiceProvider.GetRequiredService<PrecoSyncService>()
                                    .ProcessarProduto(item.ProdutoId, accessToken, partnerId, partnerKey, shopId, dataSincronizacao, cancellationToken);
                                break;
                            case TipoFila.Exclusao:
                                await scope.ServiceProvider.GetRequiredService<ExclusaoSyncService>()
                                    .ProcessarProduto(item.ProdutoId, accessToken, partnerId, partnerKey, shopId, dataSincronizacao, cancellationToken);
                                break;
                            case TipoFila.Dados:
                                await scope.ServiceProvider.GetRequiredService<DadosSyncService>()
                                    .ProcessarProduto(item.ProdutoId, accessToken, partnerId, partnerKey, shopId, dataSincronizacao, cancellationToken);
                                break;
                        }

                        resultados.Add(new ResultadoFila(item.Tipo, dataSincronizacao));
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref totalFalhas);
                        await LogarFalhaItemFila(item, ex, cancellationToken);
                    }
                    finally
                    {
                        if (bloqueioTipoAdquirido && semaforosPorTipo.TryGetValue(item.Tipo, out var semaforoTipo))
                            semaforoTipo.Release();

                        var processados = Interlocked.Increment(ref totalProcessados);

                        if (_options.ShowLoopLogs && (processados % 250 == 0 || processados == fila.Count))
                        {
                            _logger.LogInformation(
                                "Fila em andamento: {processados}/{total} processados, {sucesso} com sucesso, {falhas} com falha",
                                processados,
                                fila.Count,
                                resultados.Count,
                                Volatile.Read(ref totalFalhas)
                            );
                        }
                    }
                }
            }, cancellationToken))
            .ToArray();

        await produtores;
        await Task.WhenAll(consumidores);

        if (_options.ShowLoopLogs || totalFalhas > 0)
        {
            _logger.LogInformation(
                "Fila finalizada: {total} itens, {sucesso} com sucesso, {falhas} com falha",
                fila.Count,
                resultados.Count,
                totalFalhas
            );
        }

        return resultados.ToList();
    }

    private int ObterIntervaloLoopMinutos()
    {
        if (_options.TestMode)
            return Math.Max(1, _options.TestLoopIntervalMinutes);

        return Math.Max(1, _options.LoopIntervalMinutes);
    }

    private async Task LogarFalhaItemFila(ItemFila item, Exception ex, CancellationToken cancellationToken)
    {
        if (EhProdutoDuplicadoNaShopee(ex))
        {
            await LogarProdutoDuplicado(item, cancellationToken);
            return;
        }

        if (!EhProdutoNaoEncontradoNaShopee(ex))
        {
            _logger.LogError(ex, "Falha ao processar item da fila Tipo:{tipo} ProdutoId:{produtoId}", item.Tipo, item.ProdutoId);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        var produto = await db.Produtos
            .AsNoTracking()
            .Where(x => x.Id == item.ProdutoId)
            .Select(x => new { x.Id, x.Codigo, x.ItemId })
            .FirstOrDefaultAsync(cancellationToken);

        var codigo = produto?.Codigo?.ToString() ?? item.ProdutoId.ToString();
        var acao = item.Tipo switch
        {
            TipoFila.Estoque => "atualizacao de estoque",
            TipoFila.Preco => "atualizacao de preco",
            TipoFila.Exclusao => "exclusao",
            TipoFila.Dados => "atualizacao de dados",
            TipoFila.Cadastro => "processamento",
            _ => "processamento"
        };

        _logger.LogWarning(
            "Produto {codigo} nao encontrado na Shopee para {acao}. ItemId local: {itemId}",
            codigo,
            acao,
            produto?.ItemId
        );
    }

    private static bool EhProdutoNaoEncontradoNaShopee(Exception ex)
    {
        var message = ex.ToString();

        return message.Contains("item_not_found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("item not found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("item not exist", StringComparison.OrdinalIgnoreCase)
            || message.Contains("item_id invalid", StringComparison.OrdinalIgnoreCase)
            || message.Contains("product.error_item_not_found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("product not found", StringComparison.OrdinalIgnoreCase);
    }

    private static bool EhProdutoDuplicadoNaShopee(Exception ex)
    {
        var message = ex.ToString();

        return message.Contains("item.duplicated", StringComparison.OrdinalIgnoreCase)
            || message.Contains("product is duplicated", StringComparison.OrdinalIgnoreCase)
            || message.Contains("product.error_busi", StringComparison.OrdinalIgnoreCase)
               && message.Contains("duplicat", StringComparison.OrdinalIgnoreCase);
    }

    private async Task LogarProdutoDuplicado(ItemFila item, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        var produto = await db.Produtos
            .AsNoTracking()
            .Where(x => x.Id == item.ProdutoId)
            .Select(x => new { x.Id, x.Codigo, x.ItemId })
            .FirstOrDefaultAsync(cancellationToken);

        var codigo = produto?.Codigo?.ToString() ?? item.ProdutoId.ToString();

        _logger.LogWarning(
            "Produto {codigo} ja existe na Shopee em formato duplicado. Revise o anuncio existente antes de cadastrar novamente.",
            codigo
        );
    }

    private int ObterDelayPorTipo(TipoFila tipo)
    {
        return tipo switch
        {
            TipoFila.Cadastro => Math.Max(0, _options.CadastroDelayBetweenRequestsMs),
            TipoFila.Estoque => Math.Max(0, _options.EstoqueDelayBetweenRequestsMs),
            TipoFila.Preco => Math.Max(0, _options.PrecoDelayBetweenRequestsMs),
            TipoFila.Exclusao => Math.Max(0, _options.ExclusaoDelayBetweenRequestsMs),
            TipoFila.Dados => Math.Max(0, _options.DadosDelayBetweenRequestsMs),
            _ => 0
        };
    }

    private static List<ItemFila> MontarFilaRoundRobin(
        List<ItemFila> estoque,
        List<ItemFila> preco,
        List<ItemFila> exclusao,
        List<ItemFila> dados,
        List<ItemFila> cadastro)
    {
        var filas = new[]
        {
            new Queue<ItemFila>(estoque),
            new Queue<ItemFila>(preco),
            new Queue<ItemFila>(exclusao),
            new Queue<ItemFila>(dados),
            new Queue<ItemFila>(cadastro)
        };

        var filaFinal = new List<ItemFila>();

        while (filas.Any(x => x.Count > 0))
        {
            foreach (var fila in filas)
            {
                if (fila.Count > 0)
                    filaFinal.Add(fila.Dequeue());
            }
        }

        return filaFinal;
    }

    private static ItemFila CriarItemFila(TipoFila tipo, Produto produto)
    {
        var dataReferencia = tipo switch
        {
            TipoFila.Estoque => produto.DataEstoque,
            TipoFila.Preco => produto.DataPreco,
            TipoFila.Exclusao => produto.DataExclusao,
            TipoFila.Dados => produto.DataDados,
            _ => null
        };

        return new ItemFila(tipo, produto.Id, dataReferencia);
    }

    private bool DeveExecutarCadastro()
    {
        if (_options.TestMode)
            return true;

        return DateTime.Now.Hour >= _options.CadastroHoraInicial;
    }

    private bool DeveExecutarEstoque()
    {
        if (_options.TestMode)
            return true;

        var agora = DateTime.Now;

        if (agora.Minute >= _options.EstoqueJanelaMinutos)
            return false;

        return !_ultimaExecucaoEstoque.HasValue
            || _ultimaExecucaoEstoque.Value.Date != agora.Date
            || _ultimaExecucaoEstoque.Value.Hour != agora.Hour;
    }

    private bool DeveExecutarPreco()
    {
        if (_options.TestMode)
            return true;

        var agora = DateTime.Now;

        if (agora.Hour != _options.PrecoHora || agora.Minute >= _options.PrecoJanelaMinutos)
            return false;

        return !_ultimaExecucaoPreco.HasValue
            || _ultimaExecucaoPreco.Value.Date != agora.Date;
    }

    private bool DeveExecutarDados()
    {
        if (_options.TestMode)
            return true;

        var agora = DateTime.Now;

        if (agora.Hour != _options.DadosHora || agora.Minute >= _options.DadosJanelaMinutos)
            return false;

        return !_ultimaExecucaoDados.HasValue
            || _ultimaExecucaoDados.Value.Date != agora.Date;
    }

    private bool DeveExecutarExclusao()
    {
        if (_options.TestMode)
            return true;

        var agora = DateTime.Now;

        if (agora.Hour != _options.ExclusaoHora || agora.Minute >= _options.ExclusaoJanelaMinutos)
            return false;

        return !_ultimaExecucaoExclusao.HasValue
            || _ultimaExecucaoExclusao.Value.Date != agora.Date;
    }

    private enum TipoFila
    {
        Estoque,
        Preco,
        Exclusao,
        Dados,
        Cadastro
    }

    private sealed record ItemFila(TipoFila Tipo, int ProdutoId, DateTime? DataReferencia);

    private sealed record ResultadoFila(TipoFila Tipo, DateTime? DataReferencia);
}
