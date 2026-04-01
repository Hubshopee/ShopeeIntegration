using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class PubShopeeErroService
{
    private const string StatusErroPendente = "P";
    private const string StatusErroResolvido = "R";
    private readonly IntegrationDbContext _db;

    public PubShopeeErroService(IntegrationDbContext db)
    {
        _db = db;
    }

    public Task RegistrarErro(
        Produto produto,
        string syncConta,
        string operacao,
        int? itemId,
        string mensagemErro,
        CancellationToken cancellationToken)
    {
        if (!produto.Codigo.HasValue)
            return Task.CompletedTask;

        return RegistrarErro(
            produto.Codigo.Value,
            produto.AplCod,
            syncConta,
            operacao,
            itemId?.ToString(),
            mensagemErro,
            cancellationToken);
    }

    public async Task RegistrarErro(
        decimal codigo,
        decimal? aplCod,
        string syncConta,
        string operacao,
        string? itemId,
        string mensagemErro,
        CancellationToken cancellationToken)
    {
        var erro = await _db.PublicacoesShopeeErro
            .FirstOrDefaultAsync(
                x => x.Codigo == codigo
                    && (x.AplCod == aplCod || (!x.AplCod.HasValue && !aplCod.HasValue))
                    && x.SyncConta == syncConta
                    && x.Operacao == operacao
                    && (x.StatusProcesso == null || x.StatusProcesso != StatusErroResolvido),
                cancellationToken);

        if (erro == null)
        {
            erro = new PubShopeeErro
            {
                Codigo = codigo,
                AplCod = aplCod,
                SyncConta = syncConta,
                Operacao = operacao
            };

            await _db.PublicacoesShopeeErro.AddAsync(erro, cancellationToken);
        }

        erro.ItemId = itemId;
        erro.MensagemErro = Truncar(mensagemErro, 1000);
        erro.DtError = DateTime.Now;
        erro.StatusProcesso = StatusErroPendente;
    }

    public async Task ResolverErro(
        decimal codigo,
        decimal? aplCod,
        string syncConta,
        string operacao,
        CancellationToken cancellationToken)
    {
        var erros = await _db.PublicacoesShopeeErro
            .Where(x =>
                x.Codigo == codigo
                && (x.AplCod == aplCod || (!x.AplCod.HasValue && !aplCod.HasValue))
                && x.SyncConta == syncConta
                && x.Operacao == operacao
                && (x.StatusProcesso == null || x.StatusProcesso != StatusErroResolvido))
            .ToListAsync(cancellationToken);

        foreach (var erro in erros)
            erro.StatusProcesso = StatusErroResolvido;
    }

    private static string Truncar(string texto, int maximo)
    {
        if (texto.Length <= maximo)
            return texto;

        return texto[..maximo];
    }
}
