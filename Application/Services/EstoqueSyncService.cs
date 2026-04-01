using Microsoft.EntityFrameworkCore;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Shopee;

namespace Application.Services;

public class EstoqueSyncService
{
    private const string OperacaoEstoque = "ESTOQUE";
    private const string StatusPublicacaoAtiva = "A";

    private readonly IntegrationDbContext _db;
    private readonly ShopeeCatalogService _catalogService;
    private readonly TokenSyncService _tokenSyncService;
    private readonly PubShopeeErroService _erroService;

    public EstoqueSyncService(
        IntegrationDbContext db,
        ShopeeCatalogService catalogService,
        TokenSyncService tokenSyncService,
        PubShopeeErroService erroService)
    {
        _db = db;
        _catalogService = catalogService;
        _tokenSyncService = tokenSyncService;
        _erroService = erroService;
    }

    public async Task<List<Produto>> BuscarPendentes(CancellationToken cancellationToken, bool ignorarCheckpoint = false)
    {
        var checkpoint = ignorarCheckpoint
            ? null
            : await _db.SyncShopee.MinAsync(x => x.SincDtEst, cancellationToken);

        return await _db.Produtos
            .Where(x => x.Codigo.HasValue && x.DataEstoque != null)
            .Where(x => !checkpoint.HasValue || x.DataEstoque > checkpoint)
            .Where(x => _db.PublicacoesShopee.Any(p =>
                p.Codigo.HasValue
                && p.Codigo.Value == x.Codigo!.Value
                && (p.AplCod == x.AplCod || (!p.AplCod.HasValue && !x.AplCod.HasValue))
                && p.ItemId.HasValue
                && p.PubStatus == StatusPublicacaoAtiva))
            .OrderBy(x => x.DataEstoque)
            .ToListAsync(cancellationToken);
    }

    public async Task AtualizarCheckpoint(
        List<Produto> produtos,
        CancellationToken cancellationToken,
        bool usarDataAtual = false,
        DateTime? dataReferencia = null)
    {
        var ultimaData = usarDataAtual
            ? dataReferencia ?? DateTime.Now
            : produtos.Max(x => x.DataEstoque);

        if (!usarDataAtual && !ultimaData.HasValue)
            return;

        var sincronizacoes = await _db.SyncShopee.ToListAsync(cancellationToken);

        foreach (var sync in sincronizacoes)
            sync.SincDtEst = ultimaData!.Value;

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ProcessarPendentes(
        CancellationToken cancellationToken)
    {
        var produtos = await BuscarPendentes(cancellationToken);

        foreach (var produto in produtos)
            await ProcessarProduto(produto.Id, null, cancellationToken);

        if (produtos.Count > 0)
            await AtualizarCheckpoint(produtos, cancellationToken);

        return produtos.Count;
    }

    public async Task ProcessarProduto(
        int produtoId,
        DateTime? dataSincronizacao,
        CancellationToken cancellationToken)
    {
        var produto = await _db.Produtos
            .FirstAsync(x => x.Id == produtoId, cancellationToken);

        if (!produto.Codigo.HasValue)
            return;

        var dataAtualizacao = dataSincronizacao ?? DateTime.Now;
        var publicacoes = await BuscarPublicacoes(produto.Codigo.Value, produto.AplCod, cancellationToken);

        if (publicacoes.Count == 0)
            return;

        Exception? erro = null;

        foreach (var publicacao in publicacoes)
        {
            try
            {
                var accessToken = await _tokenSyncService.ObterTokenValido(publicacao.SyncId, cancellationToken);

                await _catalogService.UpdateStock(
                    accessToken,
                    publicacao.PartnerId,
                    publicacao.ClientSecret,
                    publicacao.ShopId,
                    new ShopeeUpdateStockRequest
                    {
                        ItemId = publicacao.ItemId,
                        Stock = produto.Estoque ?? 0,
                        SellerStock =
                        [
                            new ShopeeSellerStockRequest
                            {
                                Stock = produto.Estoque ?? 0
                            }
                        ]
                    },
                    cancellationToken
                );

                await _erroService.ResolverErro(
                    produto.Codigo.Value,
                    produto.AplCod,
                    publicacao.SyncConta,
                    OperacaoEstoque,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                erro ??= ex;

                await _erroService.RegistrarErro(
                    produto,
                    publicacao.SyncConta,
                    OperacaoEstoque,
                    publicacao.ItemId,
                    ex.Message,
                    cancellationToken);
            }
        }

        if (erro == null)
            produto.DataEstoque = dataAtualizacao;

        await _db.SaveChangesAsync(cancellationToken);

        if (erro != null)
            throw erro;
    }

    private Task<List<PublicacaoConta>> BuscarPublicacoes(decimal codigo, decimal? aplCod, CancellationToken cancellationToken)
    {
        return _db.PublicacoesShopee
            .Where(x =>
                x.Codigo.HasValue
                && x.Codigo.Value == codigo
                && (x.AplCod == aplCod || (!x.AplCod.HasValue && !aplCod.HasValue))
                && x.ItemId.HasValue
                && x.PubStatus == StatusPublicacaoAtiva)
            .Join(
                _db.SyncShopee.Where(x =>
                    !string.IsNullOrWhiteSpace(x.SyncConta)
                    && x.PartnerId.HasValue
                    && !string.IsNullOrWhiteSpace(x.ClientSecret)
                    && x.ShopId.HasValue),
                pub => pub.SyncConta,
                sync => sync.SyncConta,
                (pub, sync) => new PublicacaoConta(
                    sync.Id,
                    sync.SyncConta!,
                    sync.PartnerId!.Value,
                    sync.ClientSecret!,
                    sync.ShopId!.Value,
                    pub.ItemId!.Value))
            .ToListAsync(cancellationToken);
    }

    private sealed record PublicacaoConta(int SyncId, string SyncConta, int PartnerId, string ClientSecret, int ShopId, int ItemId);
}
