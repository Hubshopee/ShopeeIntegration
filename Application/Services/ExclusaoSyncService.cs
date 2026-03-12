using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Shopee;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class ExclusaoSyncService
{
    private readonly IntegrationDbContext _db;
    private readonly ShopeeCatalogService _catalogService;

    public ExclusaoSyncService(IntegrationDbContext db, ShopeeCatalogService catalogService)
    {
        _db = db;
        _catalogService = catalogService;
    }

    public async Task<List<Produto>> BuscarPendentes(CancellationToken cancellationToken)
    {
        var sync = await _db.SyncShopee
            .OrderBy(x => x.Id)
            .FirstAsync(cancellationToken);

        return await _db.Produtos
            .Where(x =>
                x.ItemId != null
                && x.DataExclusao != null
                && (!sync.SincDtExclusao.HasValue || x.DataExclusao > sync.SincDtExclusao))
            .OrderBy(x => x.DataExclusao)
            .ToListAsync(cancellationToken);
    }

    public async Task AtualizarCheckpoint(
        List<Produto> produtos,
        CancellationToken cancellationToken,
        DateTime? dataReferencia = null)
    {
        var ultimaData = dataReferencia ?? DateTime.Now;

        var sync = await _db.SyncShopee
            .OrderBy(x => x.Id)
            .FirstAsync(cancellationToken);

        sync.SincDtExclusao = ultimaData;

        await _db.SaveChangesAsync(cancellationToken);
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

        try
        {
            await _catalogService.DeleteItem(
                accessToken,
                partnerId,
                partnerKey,
                shopId,
                new ShopeeDeleteItemRequest
                {
                    ItemId = produto.ItemId.Value
                },
                cancellationToken
            );
        }
        catch (Exception ex) when (EhItemNaoEncontrado(ex))
        {
        }

        produto.ItemId = null;
        produto.DataExclusao = dataAtualizacao;
        produto.Status = "EXCLUIDO";
        produto.Erro = null;

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static bool EhItemNaoEncontrado(Exception ex)
    {
        var message = ex.ToString();

        return message.Contains("item_not_found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("item not found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("item not exist", StringComparison.OrdinalIgnoreCase)
            || message.Contains("item_id invalid", StringComparison.OrdinalIgnoreCase)
            || message.Contains("product.error_item_not_found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("product not found", StringComparison.OrdinalIgnoreCase);
    }
}
