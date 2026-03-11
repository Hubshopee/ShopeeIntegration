using Microsoft.EntityFrameworkCore;
using ShopeeIntegration.Domain.Entities;
using ShopeeIntegration.Infrastructure.Persistence;

namespace ShopeeIntegration.Application.Services;

public class EstoqueSyncService
{
    private readonly IntegrationDbContext _db;

    public EstoqueSyncService(IntegrationDbContext db)
    {
        _db = db;
    }

    public async Task<List<Produto>> BuscarPendentes()
    {
        var sync = await _db.SyncShopee
            .OrderBy(x => x.Id)
            .FirstAsync();

        return await _db.Produtos
            .Where(x => x.DataEstoque != null && x.DataEstoque > sync.SincDtEst)
            .OrderBy(x => x.DataEstoque)
            .ToListAsync();
    }

    public async Task AtualizarCheckpoint(List<Produto> produtos)
    {
        var ultimaData = produtos.Max(x => x.DataEstoque);

        if (!ultimaData.HasValue)
            return;

        var sync = await _db.SyncShopee
            .OrderBy(x => x.Id)
            .FirstAsync();

        sync.SincDtEst = ultimaData.Value;

        await _db.SaveChangesAsync();
    }
}
