using Microsoft.EntityFrameworkCore;
using Domain.Entities;
using Infrastructure.Persistence;

namespace Application.Services;

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