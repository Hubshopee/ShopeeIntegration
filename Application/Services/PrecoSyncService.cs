using Microsoft.EntityFrameworkCore;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Shopee;

namespace Application.Services;

public class PrecoSyncService
{
    private const string OperacaoPreco = "PRECO";
    private const string StatusPublicacaoAtiva = "A";

    private readonly IntegrationDbContext _db;
    private readonly ShopeeCatalogService _catalogService;
    private readonly TokenSyncService _tokenSyncService;
    private readonly PubShopeeErroService _erroService;

    public PrecoSyncService(
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

    public async Task<List<Produto>> BuscarPendentes(CancellationToken cancellationToken, bool ignorarCheckpoint = false)
    {
        var checkpoint = ignorarCheckpoint
            ? null
            : await _db.SyncShopee.MinAsync(x => x.SincDtPreco, cancellationToken);

        return await _db.Produtos
            .Where(x => x.Codigo.HasValue && x.DataPreco != null)
            .Where(x => !checkpoint.HasValue || x.DataPreco > checkpoint)
            .Where(x => _db.PublicacoesShopee.Any(p =>
                p.Codigo.HasValue
                && p.Codigo.Value == x.Codigo!.Value
                && (p.AplCod == x.AplCod || (!p.AplCod.HasValue && !x.AplCod.HasValue))
                && p.ItemId.HasValue
                && p.PubStatus == StatusPublicacaoAtiva))
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

        var sincronizacoes = await _db.SyncShopee.ToListAsync(cancellationToken);

        foreach (var sync in sincronizacoes)
            sync.SincDtPreco = ultimaData!.Value;

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ProcessarPendentes(
        CancellationToken cancellationToken)
    {
        var produtos = await BuscarPendentes(cancellationToken);

        foreach (var produto in produtos)
            await ProcessarProduto(produto.Id, null, cancellationToken);

        if (produtos.Count > 0)
            await AtualizarCheckpoint(produtos, cancellationToken);

        return produtos.Count;
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
            return;

        var preco = produto.PrecoVenda ?? produto.PrecoVarejo ?? produto.PrecoPadrao;

        if (!preco.HasValue || preco <= 0)
        {
            await RegistrarErroValidacao(
                produto,
                publicacoes,
                "Produto sem preco valido. Revise PRECO_VENDA, PRECO_VAREJO ou PRECO_PADRAO.",
                cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            throw new Exception("Produto sem preco valido para atualizacao.");
        }

        Exception? erro = null;

        foreach (var publicacao in publicacoes)
        {
            try
            {
                var accessToken = await _tokenSyncService.ObterTokenValido(publicacao.SyncId, cancellationToken);

                await _catalogService.UpdatePrice(
                    accessToken,
                    publicacao.PartnerId,
                    publicacao.ClientSecret,
                    publicacao.ShopId,
                    new ShopeeUpdatePriceRequest
                    {
                        ItemId = publicacao.ItemId,
                        Price = preco.Value,
                        OriginalPrice = preco.Value
                    },
                    cancellationToken
                );

                await _erroService.ResolverErro(
                    produto.Codigo.Value,
                    produto.AplCod,
                    publicacao.SyncConta,
                    OperacaoPreco,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                erro ??= ex;

                await _erroService.RegistrarErro(
                    produto,
                    publicacao.SyncConta,
                    OperacaoPreco,
                    publicacao.ItemId,
                    ex.Message,
                    cancellationToken);
            }
        }

        if (erro == null)
            produto.DataPreco = dataAtualizacao;

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
                    sync.Id,
                    sync.SyncConta!,
                    sync.PartnerId!.Value,
                    sync.ClientSecret!,
                    sync.ShopId!.Value,
                    pub.ItemId!.Value))
            .ToListAsync(cancellationToken);
    }

    private async Task RegistrarErroValidacao(
        Produto produto,
        List<PublicacaoConta> publicacoes,
        string mensagemErro,
        CancellationToken cancellationToken)
    {
        foreach (var publicacao in publicacoes)
        {
            await _erroService.RegistrarErro(
                produto,
                publicacao.SyncConta,
                OperacaoPreco,
                publicacao.ItemId,
                mensagemErro,
                cancellationToken);
        }
    }

    private sealed record PublicacaoConta(int SyncId, string SyncConta, int PartnerId, string ClientSecret, int ShopId, int ItemId);
}
