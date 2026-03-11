using Microsoft.EntityFrameworkCore;
using Domain.Entities;
using Infrastructure.Persistence;

namespace Application.Services;

public class PrecoSyncService
{
    private readonly IntegrationDbContext _db;

    public PrecoSyncService(IntegrationDbContext db)
    {
        _db = db;
    }

    public async Task<List<Produto>> BuscarPendentes()
    {
        var sync = await _db.SyncShopee
            .OrderBy(x => x.Id)
            .FirstAsync();

        return await _db.Produtos
            .Where(x => x.DataPreco != null && x.DataPreco > sync.SincDtPreco)
            .OrderBy(x => x.DataPreco)
            .ToListAsync();
    }
}