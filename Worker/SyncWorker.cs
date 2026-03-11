using Microsoft.Extensions.DependencyInjection;
using Application.Services;
using Infrastructure.Persistence;

namespace Worker;

public class SyncWorker : BackgroundService
{
    private readonly ILogger<SyncWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public SyncWorker(ILogger<SyncWorker> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Shopee Worker iniciado");

        if (!await VerificarConexaoBanco(stoppingToken))
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecutarRotinas(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro na execuÃ§Ã£o das rotinas");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
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

            _logger.LogInformation("ConexÃ£o com SQL Server OK");
            return true;
        }
        catch
        {
            _logger.LogError("Falha ao conectar no SQL Server");
            return false;
        }
    }

    private async Task ExecutarRotinas(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var tokenService = scope.ServiceProvider.GetRequiredService<TokenSyncService>();
        var cadastroProdutoService = scope.ServiceProvider.GetRequiredService<CadastroProdutoService>();
        var estoqueService = scope.ServiceProvider.GetRequiredService<EstoqueSyncService>();
        var precoService = scope.ServiceProvider.GetRequiredService<PrecoSyncService>();
        var dadosService = scope.ServiceProvider.GetRequiredService<DadosSyncService>();
        var catalogoNoturnoService = scope.ServiceProvider.GetRequiredService<CatalogoNoturnoSyncService>();

        var accessToken = await tokenService.ObterTokenValido();

        await cadastroProdutoService.ProcessarPendentes(accessToken, stoppingToken);

        if (DeveExecutarEstoque())
        {
            var produtosEstoque = await estoqueService.BuscarPendentes();
            _logger.LogInformation("Estoque pendente: {total}", produtosEstoque.Count);

            // enviar estoque com accessToken aqui

            if (produtosEstoque.Any())
                await estoqueService.AtualizarCheckpoint(produtosEstoque);
        }

        if (DeveExecutarRotinaNoturna())
        {
            var produtosPreco = await precoService.BuscarPendentes();
            var produtosDados = await dadosService.BuscarPendentes();

            _logger.LogInformation("PreÃ§o pendente: {total}", produtosPreco.Count);
            _logger.LogInformation("Dados pendente: {total}", produtosDados.Count);

            // enviar preÃ§o e dados com accessToken aqui

            if (produtosPreco.Any() || produtosDados.Any())
                await catalogoNoturnoService.AtualizarCheckpoint(produtosPreco, produtosDados);
        }
    }

    private static bool DeveExecutarEstoque()
    {
        return DateTime.Now.Minute < 5;
    }

    private static bool DeveExecutarRotinaNoturna()
    {
        var agora = DateTime.Now;
        return agora.Hour == 23 && agora.Minute < 5;
    }
}
