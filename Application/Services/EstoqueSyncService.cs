using Microsoft.EntityFrameworkCore;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Shopee;

namespace Application.Services;

public class EstoqueSyncService
{
    private readonly IntegrationDbContext _db;
    private readonly ShopeeCatalogService _catalogService;

    public EstoqueSyncService(IntegrationDbContext db, ShopeeCatalogService catalogService)
    {
        _db = db;
        _catalogService = catalogService;
    }

    public async Task<List<Produto>> BuscarPendentes(CancellationToken cancellationToken, bool ignorarCheckpoint = false)
    {
        var sync = await _db.SyncShopee
            .OrderBy(x => x.Id)
            .FirstAsync(cancellationToken);

        if (ignorarCheckpoint)
        {
            return await _db.Produtos
                .Where(x =>
                    x.ItemId != null
                    && x.DataEstoque != null
                    && (!sync.SincDtEst.HasValue || x.DataEstoque > sync.SincDtEst))
                .OrderBy(x => x.DataEstoque)
                .ToListAsync(cancellationToken);
        }

        return await _db.Produtos
            .Where(x => x.ItemId != null && x.DataEstoque != null && x.DataEstoque > sync.SincDtEst)
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

        var sync = await _db.SyncShopee
            .OrderBy(x => x.Id)
            .FirstAsync(cancellationToken);

        sync.SincDtEst = ultimaData!.Value;

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ProcessarPendentes(
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        CancellationToken cancellationToken)
    {
        var produtos = await BuscarPendentes(cancellationToken);

        foreach (var produto in produtos)
        {
            if (!produto.ItemId.HasValue)
                continue;

            await _catalogService.UpdateStock(
                accessToken,
                partnerId,
                partnerKey,
                shopId,
                new ShopeeUpdateStockRequest
                {
                    ItemId = produto.ItemId.Value,
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
        }

        if (produtos.Count > 0)
            await AtualizarCheckpoint(produtos, cancellationToken);

        return produtos.Count;
    }

    public async Task ProcessarProduto(
        int produtoId,
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        CancellationToken cancellationToken)
    {
        var produto = await _db.Produtos
            .FirstAsync(x => x.Id == produtoId, cancellationToken);

        if (!produto.ItemId.HasValue)
            return;

        var dataAtualizacao = DateTime.Now;

        await _catalogService.UpdateStock(
            accessToken,
            partnerId,
            partnerKey,
            shopId,
            new ShopeeUpdateStockRequest
            {
                ItemId = produto.ItemId.Value,
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

        produto.DataEstoque = dataAtualizacao;
        await _db.SaveChangesAsync(cancellationToken);
    }
}
