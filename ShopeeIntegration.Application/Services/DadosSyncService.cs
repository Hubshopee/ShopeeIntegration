using Microsoft.EntityFrameworkCore;
using ShopeeIntegration.Domain.Entities;
using ShopeeIntegration.Infrastructure.Persistence;

namespace ShopeeIntegration.Application.Services;

public class DadosSyncService
{
    private readonly IntegrationDbContext _db;

    public DadosSyncService(IntegrationDbContext db)
    {
        _db = db;
    }

    public async Task<List<Produto>> BuscarPendentes()
    {
        var sync = await _db.SyncShopee
            .OrderBy(x => x.Id)
            .FirstAsync();

        return await _db.Produtos
            .Where(x => x.DataDados != null && x.DataDados > sync.SincDtPreco)
            .OrderBy(x => x.DataDados)
            .ToListAsync();
    }
}