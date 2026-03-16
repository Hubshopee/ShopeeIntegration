using Microsoft.EntityFrameworkCore;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Shopee;

namespace Application.Services;

public class DadosSyncService
{
    private readonly IntegrationDbContext _db;
    private readonly ShopeeCatalogService _catalogService;

    public DadosSyncService(IntegrationDbContext db, ShopeeCatalogService catalogService)
    {
        _db = db;
        _catalogService = catalogService;
    }

    public async Task<List<Produto>> BuscarPendentes(CancellationToken cancellationToken, bool ignorarCheckpoint = false)
    {
        var sync = await _db.SyncShopee
            .OrderBy(x => x.Id)
            .FirstAsync(cancellationToken);

        if (ignorarCheckpoint)
        {
            return await _db.Produtos
                .Where(x =>
                    x.ItemId != null
                    && x.DataDados != null
                    && (!sync.SincDtDados.HasValue || x.DataDados > sync.SincDtDados))
                .OrderBy(x => x.DataDados)
                .ToListAsync(cancellationToken);
        }

        return await _db.Produtos
            .Where(x => x.ItemId != null && x.DataDados != null && x.DataDados > sync.SincDtDados)
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

        var sync = await _db.SyncShopee
            .OrderBy(x => x.Id)
            .FirstAsync(cancellationToken);

        sync.SincDtDados = ultimaData!.Value;

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ProcessarPendentes(
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        CancellationToken cancellationToken)
    {
        var produtos = await BuscarPendentes(cancellationToken);
        var codigos = produtos
            .Where(x => x.Codigo.HasValue)
            .Select(x => x.Codigo!.Value)
            .Distinct()
            .ToList();
        var atributos = await _db.Atributos
            .Where(x => x.ProdCod.HasValue && codigos.Contains(x.ProdCod.Value))
            .OrderBy(x => x.Ordem)
            .ToListAsync(cancellationToken);

        foreach (var produto in produtos)
        {
            if (!produto.ItemId.HasValue)
                continue;

            if (!produto.Peso.HasValue || produto.Peso <= 0)
                continue;

            if (!produto.Largura.HasValue || !produto.Altura.HasValue || !produto.Profundidade.HasValue)
                continue;

            var titulo = Truncar(
                produto.Titulo ?? produto.Descricao ?? $"Produto {produto.Codigo}",
                120
            );

            var request = new ShopeeUpdateItemRequest
            {
                ItemId = produto.ItemId.Value,
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
                AttributeList = MontarAtributos(atributos.Where(x => x.ProdCod == produto.Codigo).ToList())
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

            await _catalogService.UpdateItem(
                accessToken,
                partnerId,
                partnerKey,
                shopId,
                request,
                cancellationToken
            );
        }

        if (produtos.Count > 0)
            await AtualizarCheckpoint(produtos, cancellationToken);

        return produtos.Count;
    }

    public async Task ProcessarProduto(
        int produtoId,
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        DateTime? dataSincronizacao,
        CancellationToken cancellationToken)
    {
        var produto = await _db.Produtos
            .FirstAsync(x => x.Id == produtoId, cancellationToken);

        if (!produto.ItemId.HasValue)
            return;

        if (!produto.Peso.HasValue || produto.Peso <= 0)
            return;

        if (!produto.Largura.HasValue || !produto.Altura.HasValue || !produto.Profundidade.HasValue)
            return;

        var dataAtualizacao = dataSincronizacao ?? DateTime.Now;

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
            ItemId = produto.ItemId.Value,
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

        await _catalogService.UpdateItem(
            accessToken,
            partnerId,
            partnerKey,
            shopId,
            request,
            cancellationToken
        );

        produto.DataDados = dataAtualizacao;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string MontarDescricao(Produto produto, string titulo)
    {
        var descricao = produto.DescricaoDetalhada
            ?? produto.DescricaoShopee
            ?? produto.Descricao
            ?? titulo;

        descricao = NormalizarDescricao(descricao);

        if (descricao.Length >= 10)
            return descricao;

        var complemento = produto.DescricaoDetalhada
            ?? produto.DescricaoShopee
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
