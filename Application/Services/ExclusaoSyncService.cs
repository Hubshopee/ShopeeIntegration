using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Shopee;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class ExclusaoSyncService
{
    private const string OperacaoExclusao = "EXCLUSAO";
    private const string StatusPublicacaoAtiva = "A";
    private const string StatusPublicacaoExcluida = "E";

    private readonly IntegrationDbContext _db;
    private readonly ShopeeCatalogService _catalogService;
    private readonly TokenSyncService _tokenSyncService;
    private readonly PubShopeeErroService _erroService;

    public ExclusaoSyncService(
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

    public async Task<List<Produto>> BuscarPendentes(CancellationToken cancellationToken)
    {
        var checkpoint = await _db.SyncShopee.MinAsync(x => x.SincDtExclusao, cancellationToken);

        return await _db.Produtos
            .Where(x =>
                x.Codigo.HasValue
                && x.DataExclusao != null
                && (!checkpoint.HasValue || x.DataExclusao > checkpoint)
                && _db.PublicacoesShopee.Any(p =>
                    p.Codigo.HasValue
                    && p.Codigo.Value == x.Codigo!.Value
                    && (p.AplCod == x.AplCod || (!p.AplCod.HasValue && !x.AplCod.HasValue))
                    && p.ItemId.HasValue
                    && p.PubStatus == StatusPublicacaoAtiva))
            .OrderBy(x => x.DataExclusao)
            .ToListAsync(cancellationToken);
    }

    public async Task AtualizarCheckpoint(
        List<Produto> produtos,
        CancellationToken cancellationToken,
        DateTime? dataReferencia = null)
    {
        var ultimaData = dataReferencia ?? DateTime.Now;

        var sincronizacoes = await _db.SyncShopee.ToListAsync(cancellationToken);

        foreach (var sync in sincronizacoes)
            sync.SincDtExclusao = ultimaData;

        await _db.SaveChangesAsync(cancellationToken);
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
        {
            produto.DataExclusao = dataAtualizacao;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        Exception? erro = null;

        foreach (var publicacao in publicacoes)
        {
            try
            {
                var accessToken = await _tokenSyncService.ObterTokenValido(publicacao.SyncId, cancellationToken);

                await _catalogService.DeleteItem(
                    accessToken,
                    publicacao.PartnerId,
                    publicacao.ClientSecret,
                    publicacao.ShopId,
                    new ShopeeDeleteItemRequest
                    {
                        ItemId = publicacao.ItemId
                    },
                    cancellationToken
                );

                publicacao.Publicacao.PubStatus = StatusPublicacaoExcluida;

                await _erroService.ResolverErro(
                    produto.Codigo.Value,
                    produto.AplCod,
                    publicacao.SyncConta,
                    OperacaoExclusao,
                    cancellationToken);
            }
            catch (Exception ex) when (EhItemNaoEncontrado(ex))
            {
                publicacao.Publicacao.PubStatus = StatusPublicacaoExcluida;

                await _erroService.ResolverErro(
                    produto.Codigo.Value,
                    produto.AplCod,
                    publicacao.SyncConta,
                    OperacaoExclusao,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                erro ??= ex;

                await _erroService.RegistrarErro(
                    produto,
                    publicacao.SyncConta,
                    OperacaoExclusao,
                    publicacao.ItemId,
                    ex.Message,
                    cancellationToken);
            }
        }

        if (erro == null)
            produto.DataExclusao = dataAtualizacao;

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
                    pub,
                    sync.Id,
                    sync.SyncConta!,
                    sync.PartnerId!.Value,
                    sync.ClientSecret!,
                    sync.ShopId!.Value,
                    pub.ItemId!.Value))
            .ToListAsync(cancellationToken);
    }

    private sealed record PublicacaoConta(PubShopee Publicacao, int SyncId, string SyncConta, int PartnerId, string ClientSecret, int ShopId, int ItemId);

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
