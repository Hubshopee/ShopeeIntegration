using Microsoft.EntityFrameworkCore;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Shopee;

namespace Application.Services;

public class DadosSyncService
{
    private const string OperacaoDados = "DADOS";
    private const string StatusPublicacaoAtiva = "A";

    private readonly IntegrationDbContext _db;
    private readonly ShopeeCatalogService _catalogService;
    private readonly TokenSyncService _tokenSyncService;
    private readonly PubShopeeErroService _erroService;

    public DadosSyncService(
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
            : await _db.SyncShopee.MinAsync(x => x.SincDtDados, cancellationToken);

        return await _db.Produtos
            .Where(x => x.Codigo.HasValue && x.DataDados != null)
            .Where(x => !checkpoint.HasValue || x.DataDados > checkpoint)
            .Where(x => _db.PublicacoesShopee.Any(p =>
                p.Codigo.HasValue
                && p.Codigo.Value == x.Codigo!.Value
                && (p.AplCod == x.AplCod || (!p.AplCod.HasValue && !x.AplCod.HasValue))
                && p.ItemId.HasValue
                && p.PubStatus == StatusPublicacaoAtiva))
            .OrderBy(x => x.DataDados)
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
            : produtos.Max(x => x.DataDados);

        if (!usarDataAtual && !ultimaData.HasValue)
            return;

        var sincronizacoes = await _db.SyncShopee.ToListAsync(cancellationToken);

        foreach (var sync in sincronizacoes)
            sync.SincDtDados = ultimaData!.Value;

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

        if (!produto.Peso.HasValue || produto.Peso <= 0)
        {
            await RegistrarErroValidacao(
                produto,
                publicacoes,
                "Produto sem peso valido. Preencha o campo PESO para atualizar na Shopee.",
                cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            throw new Exception("Produto sem peso valido para atualizacao.");
        }

        if (!produto.Largura.HasValue || !produto.Altura.HasValue || !produto.Profundidade.HasValue)
        {
            await RegistrarErroValidacao(
                produto,
                publicacoes,
                "Produto sem dimensoes completas. Preencha LARGURA, ALTURA e PROFUNDIDADE.",
                cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            throw new Exception("Produto sem dimensoes completas para atualizacao.");
        }

        var atributos = produto.Codigo.HasValue
            ? await _db.Atributos
                .Where(x => x.ProdCod == produto.Codigo)
                .OrderBy(x => x.Ordem)
                .ToListAsync(cancellationToken)
            : [];

        var titulo = Truncar(
            produto.Titulo ?? produto.Descricao ?? $"Produto {produto.Codigo}",
            120
        );

        var request = new ShopeeUpdateItemRequest
        {
            ItemName = titulo,
            Description = MontarDescricao(produto, titulo),
            ItemSku = produto.Sku,
            GtinCode = string.IsNullOrWhiteSpace(produto.Gtin) ? null : produto.Gtin.Trim(),
            Ncm = NormalizarNcm(produto.Ncm),
            Origin = string.IsNullOrWhiteSpace(produto.Origem) ? null : produto.Origem.Trim(),
            Weight = decimal.Round(produto.Peso.Value / 1000m, 3),
            Dimension = new ShopeeDimensionRequest
            {
                PackageLength = NormalizarDimensao(produto.Profundidade.Value),
                PackageWidth = NormalizarDimensao(produto.Largura.Value),
                PackageHeight = NormalizarDimensao(produto.Altura.Value)
            },
            AttributeList = MontarAtributos(atributos)
        };

        if (produto.MarcaId.HasValue && produto.MarcaId.Value > 0)
        {
            request.Brand = new ShopeeBrandRequest
            {
                BrandId = produto.MarcaId.Value,
                OriginalBrandName = string.IsNullOrWhiteSpace(produto.Fornecedor) ? null : produto.Fornecedor.Trim()
            };
        }
        else if (!string.IsNullOrWhiteSpace(produto.Fornecedor))
        {
            request.Brand = new ShopeeBrandRequest
            {
                OriginalBrandName = produto.Fornecedor.Trim()
            };
        }

        Exception? erro = null;

        foreach (var publicacao in publicacoes)
        {
            try
            {
                var accessToken = await _tokenSyncService.ObterTokenValido(publicacao.SyncId, cancellationToken);
                request.ItemId = publicacao.ItemId;

                await _catalogService.UpdateItem(
                    accessToken,
                    publicacao.PartnerId,
                    publicacao.ClientSecret,
                    publicacao.ShopId,
                    request,
                    cancellationToken
                );

                await _erroService.ResolverErro(
                    produto.Codigo.Value,
                    produto.AplCod,
                    publicacao.SyncConta,
                    OperacaoDados,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                erro ??= ex;

                await _erroService.RegistrarErro(
                    produto,
                    publicacao.SyncConta,
                    OperacaoDados,
                    publicacao.ItemId,
                    ex.Message,
                    cancellationToken);
            }
        }

        if (erro == null)
            produto.DataDados = dataAtualizacao;

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
                OperacaoDados,
                publicacao.ItemId,
                mensagemErro,
                cancellationToken);
        }
    }

    private sealed record PublicacaoConta(int SyncId, string SyncConta, int PartnerId, string ClientSecret, int ShopId, int ItemId);

    private static string MontarDescricao(Produto produto, string titulo)
    {
        var descricao = produto.DescricaoDetalhada
            ?? produto.Descricao
            ?? titulo;

        descricao = NormalizarDescricao(descricao);

        if (descricao.Length >= 10)
            return descricao;

        var complemento = produto.DescricaoDetalhada
            ?? produto.Descricao
            ?? titulo;

        var descricaoNormalizada = $"{titulo} {complemento}".Trim();

        if (descricaoNormalizada.Length >= 10)
            return Truncar(descricaoNormalizada, 5000);

        return Truncar($"{titulo} produto original", 5000);
    }

    private static string NormalizarDescricao(string texto)
    {
        return texto
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private static List<ShopeeAttributeRequest> MontarAtributos(List<Atributo> atributos)
    {
        return atributos
            .GroupBy(x => x.AttributeId)
            .Select(group => new ShopeeAttributeRequest
            {
                AttributeId = group.Key,
                AttributeValueList = group
                    .Select(x => new ShopeeAttributeValueRequest
                    {
                        ValueId = x.ValueId,
                        OriginalValueName = string.IsNullOrWhiteSpace(x.ValueName) ? null : x.ValueName
                    })
                    .ToList()
            })
            .ToList();
    }

    private static decimal NormalizarDimensao(decimal valor)
    {
        if (valor <= 0)
            return 0;

        if (valor < 1)
            return decimal.Round(valor * 100m, 0, MidpointRounding.AwayFromZero);

        return decimal.Round(valor, 0, MidpointRounding.AwayFromZero);
    }

    private static string? NormalizarNcm(string? ncm)
    {
        if (string.IsNullOrWhiteSpace(ncm))
            return null;

        var digits = new string(ncm.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? null : digits;
    }

    private static string Truncar(string texto, int maximo)
    {
        if (texto.Length <= maximo)
            return texto;

        return texto[..maximo];
    }
}
