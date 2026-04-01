using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Shopee;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services;

public class CadastroProdutoService
{
    private const string BaseImageUrl = "http://loja.umec.com.br/produtos";
    private const string OperacaoCadastro = "CADASTRO";
    private const string StatusErroResolvido = "R";
    private const string StatusPublicacaoAtiva = "A";
    private readonly IntegrationDbContext _db;
    private readonly ShopeeCatalogService _catalogService;
    private readonly PubShopeeErroService _erroService;
    private readonly ILogger<CadastroProdutoService> _logger;

    public CadastroProdutoService(
        IntegrationDbContext db,
        ShopeeCatalogService catalogService,
        PubShopeeErroService erroService,
        ILogger<CadastroProdutoService> logger)
    {
        _db = db;
        _catalogService = catalogService;
        _erroService = erroService;
        _logger = logger;
    }

    public async Task<List<CadastroShopeePendente>> BuscarPendentes(
        int syncShopeeId,
        string syncConta,
        CancellationToken cancellationToken)
    {
        return await _db.Produtos
            .AsNoTracking()
            .Where(x => x.Codigo.HasValue)
            .Where(x => !_db.PublicacoesShopee.Any(p =>
                p.Codigo.HasValue
                && p.Codigo.Value == x.Codigo!.Value
                && (p.AplCod == x.AplCod || (!p.AplCod.HasValue && !x.AplCod.HasValue))
                && p.SyncConta == syncConta
                && p.PubStatus == StatusPublicacaoAtiva))
            .Where(x => !_db.PublicacoesShopeeErro.Any(e =>
                e.Codigo == x.Codigo!.Value
                && (e.AplCod == x.AplCod || (!e.AplCod.HasValue && !x.AplCod.HasValue))
                && e.Operacao == OperacaoCadastro
                && (e.StatusProcesso == null || e.StatusProcesso != StatusErroResolvido)))
            .OrderBy(x => x.Id)
            .Select(x => new CadastroShopeePendente(x.Id, syncShopeeId, syncConta))
            .ToListAsync(cancellationToken);
    }

    public async Task ProcessarProduto(
        int produtoId,
        string syncConta,
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        CancellationToken cancellationToken)
    {
        var produto = await _db.Produtos
            .FirstAsync(x => x.Id == produtoId, cancellationToken);

        if (!produto.Codigo.HasValue)
            return;

        if (await _db.PublicacoesShopee.AnyAsync(
                x => x.Codigo.HasValue
                    && x.Codigo.Value == produto.Codigo.Value
                    && (x.AplCod == produto.AplCod || (!x.AplCod.HasValue && !produto.AplCod.HasValue))
                    && x.SyncConta == syncConta
                    && x.PubStatus == StatusPublicacaoAtiva,
                cancellationToken))
            return;

        if (await _db.PublicacoesShopeeErro.AnyAsync(
                x => x.Codigo == produto.Codigo.Value
                    && (x.AplCod == produto.AplCod || (!x.AplCod.HasValue && !produto.AplCod.HasValue))
                    && x.Operacao == OperacaoCadastro
                    && (x.StatusProcesso == null || x.StatusProcesso != StatusErroResolvido),
                cancellationToken))
        {
            _logger.LogInformation(
                "Produto Codigo:{codigo} ignorado no cadastro da conta {conta} porque possui erro pendente de cadastro para o produto.",
                produto.Codigo,
                syncConta
            );
            return;
        }

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
                syncConta,
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
            await _erroService.RegistrarErro(
                produto,
                syncConta,
                OperacaoCadastro,
                null,
                TratarMensagemErro(ex),
                cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private async Task CadastrarProduto(
        Produto produto,
        string syncConta,
        List<ImagemFtp> imagens,
        List<Atributo> atributos,
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        CancellationToken cancellationToken)
    {
        var camposObrigatoriosAusentes = ObterCamposObrigatoriosAusentes(produto);

        if (camposObrigatoriosAusentes.Count > 0)
            throw new Exception($"Campos obrigatorios vazios ou faltando: {string.Join(", ", camposObrigatoriosAusentes)}.");

        var titulo = Truncar(
            produto.Titulo ?? produto.Descricao ?? $"Produto {produto.Codigo}",
            120
        );

        var descricao = MontarDescricao(produto, titulo);
        var codigo = produto.Codigo.GetValueOrDefault();
        var preco = produto.PrecoVenda.GetValueOrDefault();
        var estoque = produto.Estoque.GetValueOrDefault();
        var pesoKg = decimal.Round(produto.Peso.GetValueOrDefault() / 1000m, 3);
        var categoriaId = produto.CategoriaId.GetValueOrDefault();
        var channelIdConfigurado = produto.ChannelId.GetValueOrDefault();
        var profundidade = produto.Profundidade.GetValueOrDefault();
        var largura = produto.Largura.GetValueOrDefault();
        var altura = produto.Altura.GetValueOrDefault();

        var imageUrls = MontarUrlsImagem(codigo, imagens);

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
            channelIdConfigurado,
            cancellationToken
        );

        var request = new ShopeeCreateItemRequest
        {
            ItemName = titulo,
            Description = descricao,
            ItemSku = produto.Sku,
            GtinCode = string.IsNullOrWhiteSpace(produto.Gtin) ? null : produto.Gtin.Trim(),
            Ncm = NormalizarNcm(produto.Ncm),
            Origin = string.IsNullOrWhiteSpace(produto.Origem) ? null : produto.Origem.Trim(),
            CategoryId = categoriaId,
            Weight = pesoKg,
            Dimension = new ShopeeDimensionRequest
            {
                PackageLength = NormalizarDimensao(profundidade),
                PackageWidth = NormalizarDimensao(largura),
                PackageHeight = NormalizarDimensao(altura)
            },
            Price = preco,
            OriginalPrice = preco,
            Stock = estoque,
            SellerStock =
            [
                new ShopeeSellerStockRequest
                {
                    Stock = estoque
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

        var publicacao = await _db.PublicacoesShopee
            .FirstOrDefaultAsync(
                x => x.Codigo.HasValue
                    && x.Codigo.Value == produto.Codigo.Value
                    && (x.AplCod == produto.AplCod || (!x.AplCod.HasValue && !produto.AplCod.HasValue))
                    && x.SyncConta == syncConta,
                cancellationToken);

        if (publicacao == null)
        {
            publicacao = new PubShopee
            {
                Codigo = produto.Codigo.Value,
                AplCod = produto.AplCod,
                SyncConta = syncConta
            };

            await _db.PublicacoesShopee.AddAsync(publicacao, cancellationToken);
        }

        publicacao.ItemId = NormalizarItemIdPublicacao(itemId);
        publicacao.AplCod = produto.AplCod;
        publicacao.PubDtInc = DateTime.Now;
        publicacao.PubStatus = StatusPublicacaoAtiva;
        produto.DataInclusao = DateTime.Now;
        produto.DataEstoque = null;
        produto.DataPreco = null;
        produto.DataDados = null;

        await _erroService.ResolverErro(
            produto.Codigo.Value,
            produto.AplCod,
            syncConta,
            OperacaoCadastro,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Produto cadastrado Codigo:{codigo} Conta:{conta} ItemId:{itemId}",
            produto.Codigo,
            syncConta,
            itemId
        );
    }

    private static int NormalizarItemIdPublicacao(long itemId)
    {
        if (itemId > int.MaxValue || itemId < int.MinValue)
            throw new Exception("ITEMID retornado pela Shopee excede o limite atual da tabela PUBSHOPEE.");

        return (int)itemId;
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

    private static List<string> ObterCamposObrigatoriosAusentes(Produto produto)
    {
        var campos = new List<string>();

        if (!produto.Codigo.HasValue || produto.Codigo.Value <= 0)
            campos.Add("CODIGO");

        if (string.IsNullOrWhiteSpace(produto.Sku))
            campos.Add("SKU");

        if (string.IsNullOrWhiteSpace(produto.Fabricante))
            campos.Add("FABRICANTE");

        if (!produto.Peso.HasValue || produto.Peso.Value <= 0)
            campos.Add("PESO");

        if (!produto.PrecoVenda.HasValue || produto.PrecoVenda.Value <= 0)
            campos.Add("PRECO_VENDA");

        if (!produto.Estoque.HasValue)
            campos.Add("ESTOQUE");

        if (string.IsNullOrWhiteSpace(produto.DescricaoDetalhada))
            campos.Add("DESCRICAO_DETALHADA");

        if (string.IsNullOrWhiteSpace(produto.Fornecedor))
            campos.Add("FORNECEDOR");

        if (!produto.Largura.HasValue || produto.Largura.Value <= 0)
            campos.Add("LARGURA");

        if (!produto.Altura.HasValue || produto.Altura.Value <= 0)
            campos.Add("ALTURA");

        if (!produto.Profundidade.HasValue || produto.Profundidade.Value <= 0)
            campos.Add("PROFUNDIDADE");

        if (string.IsNullOrWhiteSpace(produto.Gtin))
            campos.Add("GTIN");

        if (!produto.CategoriaId.HasValue || produto.CategoriaId.Value <= 0)
            campos.Add("CATEGORIA_ID");

        if (!produto.MarcaId.HasValue || produto.MarcaId.Value <= 0)
            campos.Add("MARCA_ID");

        if (!produto.ChannelId.HasValue || produto.ChannelId.Value <= 0)
            campos.Add("CHANNEL_ID");

        return campos;
    }

    private static string? NormalizarNcm(string? ncm)
    {
        if (string.IsNullOrWhiteSpace(ncm))
            return null;

        var digits = new string(ncm.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? null : digits;
    }

    private static string TratarMensagemErro(Exception ex)
    {
        var message = ex.ToString();

        if (message.Contains("item.duplicated", StringComparison.OrdinalIgnoreCase)
            || message.Contains("product is duplicated", StringComparison.OrdinalIgnoreCase))
        {
            return Truncar(
                "Produto duplicado na Shopee. Altere o anuncio existente ou remova o duplicado antes de cadastrar novamente.",
                2000
            );
        }

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

    public sealed record CadastroShopeePendente(int ProdutoId, int SyncShopeeId, string SyncConta);
}
