using Microsoft.EntityFrameworkCore;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Shopee;

namespace Application.Services;

public class PrecoSyncService
{
    private readonly IntegrationDbContext _db;
    private readonly ShopeeCatalogService _catalogService;

    public PrecoSyncService(IntegrationDbContext db, ShopeeCatalogService catalogService)
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
                    && x.DataPreco != null
                    && (!sync.SincDtDados.HasValue || x.DataPreco > sync.SincDtDados))
                .OrderBy(x => x.DataPreco)
                .ToListAsync(cancellationToken);
        }

        return await _db.Produtos
            .Where(x => x.ItemId != null && x.DataPreco != null && x.DataPreco > sync.SincDtPreco)
            .OrderBy(x => x.DataPreco)
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
            : produtos.Max(x => x.DataPreco);

        if (!usarDataAtual && !ultimaData.HasValue)
            return;

        var sync = await _db.SyncShopee
            .OrderBy(x => x.Id)
            .FirstAsync(cancellationToken);

        sync.SincDtPreco = ultimaData!.Value;

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

            var preco = produto.PrecoVenda ?? produto.PrecoVarejo ?? produto.PrecoPadrao;

            if (!preco.HasValue || preco <= 0)
                continue;

            await _catalogService.UpdatePrice(
                accessToken,
                partnerId,
                partnerKey,
                shopId,
                new ShopeeUpdatePriceRequest
                {
                    ItemId = produto.ItemId.Value,
                    Price = preco.Value,
                    OriginalPrice = preco.Value
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

        var preco = produto.PrecoVenda ?? produto.PrecoVarejo ?? produto.PrecoPadrao;

        if (!preco.HasValue || preco <= 0)
            return;

        var dataAtualizacao = DateTime.Now;

        await _catalogService.UpdatePrice(
            accessToken,
            partnerId,
            partnerKey,
            shopId,
            new ShopeeUpdatePriceRequest
            {
                ItemId = produto.ItemId.Value,
                Price = preco.Value,
                OriginalPrice = preco.Value
            },
            cancellationToken
        );

        produto.DataPreco = dataAtualizacao;
        await _db.SaveChangesAsync(cancellationToken);
    }
}
