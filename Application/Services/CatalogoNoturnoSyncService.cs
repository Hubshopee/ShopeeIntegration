using Microsoft.EntityFrameworkCore;
using Domain.Entities;
using Infrastructure.Persistence;

namespace Application.Services;

public class CatalogoNoturnoSyncService
{
    private readonly IntegrationDbContext _db;

    public CatalogoNoturnoSyncService(IntegrationDbContext db)
    {
        _db = db;
    }

    public async Task AtualizarCheckpoint(List<Produto> precos, List<Produto> dados)
    {
        var maiorPreco = precos.Max(x => x.DataPreco);
        var maiorDados = dados.Max(x => x.DataDados);

        DateTime? ultimaData = null;

        if (maiorPreco.HasValue && maiorDados.HasValue)
            ultimaData = maiorPreco > maiorDados ? maiorPreco : maiorDados;
        else if (maiorPreco.HasValue)
            ultimaData = maiorPreco;
        else if (maiorDados.HasValue)
            ultimaData = maiorDados;

        if (!ultimaData.HasValue)
            return;

        var sync = await _db.SyncShopee
            .OrderBy(x => x.Id)
            .FirstAsync();

        sync.SincDtPreco = ultimaData.Value;

        await _db.SaveChangesAsync();
    }
}