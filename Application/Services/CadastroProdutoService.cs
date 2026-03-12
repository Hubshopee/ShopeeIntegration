using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Shopee;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services;

public class CadastroProdutoService
{
    private const string BaseImageUrl = "http://loja.umec.com.br/produtos";

    private readonly IntegrationDbContext _db;
    private readonly ShopeeCatalogService _catalogService;
    private readonly ILogger<CadastroProdutoService> _logger;

    public CadastroProdutoService(
        IntegrationDbContext db,
        ShopeeCatalogService catalogService,
        ILogger<CadastroProdutoService> logger)
    {
        _db = db;
        _catalogService = catalogService;
        _logger = logger;
    }

    public async Task<int> ProcessarPendentes(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var sync = await _db.SyncShopee
            .OrderBy(x => x.Id)
            .FirstAsync(cancellationToken);

        if (!sync.PartnerId.HasValue)
            throw new Exception("PARTNERID nao esta preenchido na SINCSHOPEE.");

        if (string.IsNullOrWhiteSpace(sync.ClientSecret))
            throw new Exception("CLIENTSECRET nao esta preenchido na SINCSHOPEE.");

        if (!sync.ShopId.HasValue)
            throw new Exception("SHOPID nao esta preenchido na SINCSHOPEE.");

        var produtos = await _db.Produtos
            .Where(x => x.DataInclusao == null)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Cadastro pendente: {total}", produtos.Count);

        if (produtos.Count == 0)
            return 0;

        var codigos = produtos
            .Where(x => x.Codigo.HasValue)
            .Select(x => x.Codigo!.Value)
            .Distinct()
            .ToList();

        var imagens = await _db.ImagensFtp
            .Where(x => x.ProdCod.HasValue && codigos.Contains(x.ProdCod.Value))
            .ToListAsync(cancellationToken);

        var atributos = await _db.Atributos
            .Where(x => x.ProdCod.HasValue && codigos.Contains(x.ProdCod.Value))
            .OrderBy(x => x.Ordem)
            .ToListAsync(cancellationToken);

        foreach (var produto in produtos)
        {
            try
            {
                await CadastrarProduto(
                    produto,
                    imagens.Where(x => x.ProdCod == produto.Codigo).ToList(),
                    atributos.Where(x => x.ProdCod == produto.Codigo).ToList(),
                    accessToken,
                    sync.PartnerId.Value,
                    sync.ClientSecret,
                    sync.ShopId.Value,
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                produto.Status = "ERRO";
                produto.Erro = TratarMensagemErro(ex);
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogError(ex, "Erro ao cadastrar produto Codigo:{codigo}", produto.Codigo);
            }
        }

        return produtos.Count;
    }

    public async Task<List<Produto>> BuscarPendentes(CancellationToken cancellationToken)
    {
        return await _db.Produtos
            .Where(x => x.DataInclusao == null)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task ProcessarProduto(
        int produtoId,
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        CancellationToken cancellationToken)
    {
        var produto = await _db.Produtos
            .FirstAsync(x => x.Id == produtoId, cancellationToken);

        try
        {
            var imagens = produto.Codigo.HasValue
                ? await _db.ImagensFtp
                    .Where(x => x.ProdCod == produto.Codigo)
                    .ToListAsync(cancellationToken)
                : [];

            var atributos = produto.Codigo.HasValue
                ? await _db.Atributos
                    .Where(x => x.ProdCod == produto.Codigo)
                    .OrderBy(x => x.Ordem)
                    .ToListAsync(cancellationToken)
                : [];

            await CadastrarProduto(
                produto,
                imagens,
                atributos,
                accessToken,
                partnerId,
                partnerKey,
                shopId,
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            produto.Status = "ERRO";
            produto.Erro = TratarMensagemErro(ex);
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private async Task CadastrarProduto(
        Produto produto,
        List<ImagemFtp> imagens,
        List<Atributo> atributos,
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        CancellationToken cancellationToken)
    {
        if (!produto.Codigo.HasValue)
            throw new Exception("Produto sem CODIGO.");

        if (!produto.CategoriaId.HasValue)
            throw new Exception("Produto sem CATEGORIA_ID.");

        var titulo = Truncar(
            produto.Titulo ?? produto.Descricao ?? $"Produto {produto.Codigo}",
            120
        );

        var descricao = MontarDescricao(produto, titulo);

        var preco = produto.PrecoVenda
            ?? produto.PrecoVarejo
            ?? produto.PrecoPadrao;

        if (!preco.HasValue || preco <= 0)
            throw new Exception("Produto sem preco valido para cadastro.");

        if (!produto.Peso.HasValue || produto.Peso <= 0)
            throw new Exception("Produto sem peso valido para cadastro.");

        var pesoKg = decimal.Round(produto.Peso.Value / 1000m, 3);

        if (!produto.Largura.HasValue || !produto.Altura.HasValue || !produto.Profundidade.HasValue)
            throw new Exception("Produto sem dimensoes completas para cadastro.");

        var imageUrls = MontarUrlsImagem(produto.Codigo.Value, imagens);

        if (imageUrls.Count == 0)
            throw new Exception("Produto sem imagens para cadastro.");

        var imageIds = await _catalogService.UploadImages(
            accessToken,
            partnerId,
            partnerKey,
            shopId,
            imageUrls,
            cancellationToken
        );

        var channelId = await _catalogService.ResolveLogisticsChannelId(
            accessToken,
            partnerId,
            partnerKey,
            shopId,
            produto.ChannelId,
            cancellationToken
        );

        var request = new ShopeeCreateItemRequest
        {
            ItemName = titulo,
            Description = descricao,
            ItemSku = produto.Sku,
            GtinCode = string.IsNullOrWhiteSpace(produto.Gtin) ? null : produto.Gtin.Trim(),
            CategoryId = produto.CategoriaId.Value,
            Weight = pesoKg,
            Dimension = new ShopeeDimensionRequest
            {
                PackageLength = NormalizarDimensao(produto.Profundidade.Value),
                PackageWidth = NormalizarDimensao(produto.Largura.Value),
                PackageHeight = NormalizarDimensao(produto.Altura.Value)
            },
            Price = preco.Value,
            OriginalPrice = preco.Value,
            Stock = produto.Estoque ?? 0,
            SellerStock =
            [
                new ShopeeSellerStockRequest
                {
                    Stock = produto.Estoque ?? 0
                }
            ],
            Image = new ShopeeImageRequest
            {
                ImageIdList = imageIds.ToList()
            },
            LogisticInfo =
            [
                new ShopeeLogisticInfoRequest
                {
                    LogisticId = channelId,
                    Enabled = true
                }
            ],
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

        var itemId = await _catalogService.CreateItem(
            accessToken,
            partnerId,
            partnerKey,
            shopId,
            request,
            cancellationToken
        );

        produto.ItemId = itemId;
        produto.DataInclusao = DateTime.Now;
        produto.DataEstoque = null;
        produto.DataPreco = null;
        produto.DataDados = null;
        produto.Status = "CADASTRADO";
        produto.Erro = null;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Produto cadastrado Codigo:{codigo} ItemId:{itemId}", produto.Codigo, itemId);
    }

    private static List<string> MontarUrlsImagem(decimal codigo, List<ImagemFtp> imagens)
    {
        var codigoTexto = ((int)codigo).ToString();

        return imagens
            .Where(x => !string.IsNullOrWhiteSpace(x.Letras))
            .SelectMany(x => x.Letras!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(sufixo => $"{BaseImageUrl}/{codigoTexto}_{sufixo}.jpg")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private static string MontarDescricao(Produto produto, string titulo)
    {
        var descricao = produto.DescricaoDetalhada
            ?? produto.DescricaoShopee
            ?? produto.Descricao
            ?? titulo;

        descricao = descricao.Trim();

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

    private static decimal NormalizarDimensao(decimal valor)
    {
        if (valor <= 0)
            return 0;

        if (valor < 1)
            return decimal.Round(valor * 100m, 0, MidpointRounding.AwayFromZero);

        return decimal.Round(valor, 0, MidpointRounding.AwayFromZero);
    }

    private static string Truncar(string texto, int maximo)
    {
        if (texto.Length <= maximo)
            return texto;

        return texto[..maximo];
    }

    private static string TratarMensagemErro(Exception ex)
    {
        var message = ex.ToString();

        if (message.Contains("product.error_invalid_logistic_info", StringComparison.OrdinalIgnoreCase)
            || message.Contains("logistics.no.valid.channel", StringComparison.OrdinalIgnoreCase))
        {
            return Truncar(
                "Canal logistico habilitado na loja, mas nao elegivel para cadastro de produto no sandbox da Shopee.",
                2000
            );
        }

        if (message.Contains("product.error_invalid_brand", StringComparison.OrdinalIgnoreCase))
        {
            return Truncar(
                "A categoria exige uma marca valida para o produto na Shopee.",
                2000
            );
        }

        if (message.Contains("product.error_invalid_category", StringComparison.OrdinalIgnoreCase))
        {
            return Truncar(
                "Categoria invalida para a loja ou ambiente Shopee configurado.",
                2000
            );
        }

        if (message.Contains("Produto sem imagens para cadastro", StringComparison.OrdinalIgnoreCase))
        {
            return Truncar(
                "Produto sem imagens. Preencha a IMAGENS_FTP para permitir o cadastro na Shopee.",
                2000
            );
        }

        if (message.Contains("Produto sem CATEGORIA_ID", StringComparison.OrdinalIgnoreCase))
        {
            return Truncar(
                "Produto sem categoria. Preencha o campo CATEGORIA_ID para cadastrar na Shopee.",
                2000
            );
        }

        if (message.Contains("Produto sem preco valido", StringComparison.OrdinalIgnoreCase))
        {
            return Truncar(
                "Produto sem preco valido. Revise PRECO_VENDA, PRECO_VAREJO ou PRECO_PADRAO.",
                2000
            );
        }

        if (message.Contains("Produto sem peso valido", StringComparison.OrdinalIgnoreCase))
        {
            return Truncar(
                "Produto sem peso valido. Preencha o campo PESO para cadastrar na Shopee.",
                2000
            );
        }

        if (message.Contains("Produto sem dimensoes completas", StringComparison.OrdinalIgnoreCase))
        {
            return Truncar(
                "Produto sem dimensoes completas. Preencha LARGURA, ALTURA e PROFUNDIDADE.",
                2000
            );
        }

        if (message.Contains("Produto sem CODIGO", StringComparison.OrdinalIgnoreCase))
        {
            return Truncar(
                "Produto sem codigo. Preencha o campo CODIGO para cadastrar na Shopee.",
                2000
            );
        }

        return Truncar(ex.Message, 2000);
    }
}
