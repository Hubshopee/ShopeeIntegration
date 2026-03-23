using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Shopee;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services;

public class PedidoSyncService
{
    private sealed record ProdutoPedidoLookup(decimal? Codigo, decimal? PrecoVenda);

    private static readonly Dictionary<string, string> Ufs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["acre"] = "AC",
        ["alagoas"] = "AL",
        ["amapa"] = "AP",
        ["amazonas"] = "AM",
        ["bahia"] = "BA",
        ["ceara"] = "CE",
        ["distrito federal"] = "DF",
        ["espirito santo"] = "ES",
        ["goias"] = "GO",
        ["maranhao"] = "MA",
        ["mato grosso"] = "MT",
        ["mato grosso do sul"] = "MS",
        ["minas gerais"] = "MG",
        ["para"] = "PA",
        ["paraiba"] = "PB",
        ["parana"] = "PR",
        ["pernambuco"] = "PE",
        ["piaui"] = "PI",
        ["rio de janeiro"] = "RJ",
        ["rio grande do norte"] = "RN",
        ["rio grande do sul"] = "RS",
        ["rondonia"] = "RO",
        ["roraima"] = "RR",
        ["santa catarina"] = "SC",
        ["sao paulo"] = "SP",
        ["sergipe"] = "SE",
        ["tocantins"] = "TO"
    };

    private readonly IntegrationDbContext _db;
    private readonly ShopeeCatalogService _catalogService;
    private readonly ILogger<PedidoSyncService> _logger;

    public PedidoSyncService(
        IntegrationDbContext db,
        ShopeeCatalogService catalogService,
        ILogger<PedidoSyncService> logger)
    {
        _db = db;
        _catalogService = catalogService;
        _logger = logger;
    }

    public async Task<int> ProcessarPendentes(
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        bool cargaInicial,
        CancellationToken cancellationToken)
    {
        var sync = await _db.SyncShopee
            .OrderBy(x => x.Id)
            .FirstAsync(cancellationToken);

        var dataInicial = sync.SincDtPedidos ?? DateTime.Now.AddDays(-15);
        var dataFinal = DateTime.Now;

        _logger.LogInformation(
            "Buscando pedidos Shopee atualizados entre {inicio} e {fim}",
            dataInicial,
            dataFinal
        );

        var pedidosPendentes = await _catalogService.GetOrdersUpdatedSince(
            accessToken,
            partnerId,
            partnerKey,
            shopId,
            dataInicial,
            dataFinal,
            cancellationToken
        );

        if (pedidosPendentes.Count == 0)
        {
            sync.SincDtPedidos = dataFinal;
            await _db.SaveChangesAsync(cancellationToken);
            return 0;
        }

        var detalhes = await _catalogService.GetOrderDetails(
            accessToken,
            partnerId,
            partnerKey,
            shopId,
            pedidosPendentes.Select(x => x.OrderSn).ToArray(),
            cancellationToken
        );

        if (detalhes.Count == 0)
        {
            sync.SincDtPedidos = pedidosPendentes.Max(x => x.UpdateTime ?? x.CreateTime) ?? dataFinal;
            await _db.SaveChangesAsync(cancellationToken);
            return 0;
        }

        await UpsertPedidos(detalhes, cancellationToken);

        sync.SincDtPedidos = detalhes.Max(x => x.UpdateTime ?? x.CreateTime) ?? dataFinal;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Pedidos sincronizados: {quantidade} pedidos processados",
            detalhes.Count
        );

        return detalhes.Count;
    }

    private async Task UpsertPedidos(
        IReadOnlyList<ShopeeOrderDetail> detalhes,
        CancellationToken cancellationToken)
    {
        var orderIds = detalhes
            .Select(x => x.OrderSn)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existentes = await _db.PedidosMarketplace
            .Include(x => x.Itens)
            .Where(x => x.VPedOrderId != null && orderIds.Contains(x.VPedOrderId))
            .ToDictionaryAsync(x => x.VPedOrderId!, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var itemIds = detalhes
            .SelectMany(x => x.ItemList)
            .Select(x => x.ItemId)
            .Where(x => long.TryParse(x, out _))
            .Select(x => long.Parse(x!))
            .Distinct()
            .ToList();

        var skus = detalhes
            .SelectMany(x => x.ItemList)
            .SelectMany(x => new[] { x.ModelSku, x.ItemSku })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var produtos = await _db.Produtos
            .AsNoTracking()
            .Where(x =>
                (x.ItemId.HasValue && itemIds.Contains(x.ItemId.Value))
                || (!string.IsNullOrWhiteSpace(x.Sku) && skus.Contains(x.Sku)))
            .Select(x => new { x.ItemId, x.Sku, Lookup = new ProdutoPedidoLookup(x.Codigo, x.PrecoVenda) })
            .ToListAsync(cancellationToken);

        var produtoPorItemId = produtos
            .Where(x => x.ItemId.HasValue)
            .GroupBy(x => x.ItemId!.Value)
            .ToDictionary(x => x.Key, x => x.First().Lookup);

        var produtoPorSku = produtos
            .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
            .GroupBy(x => x.Sku!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Lookup, StringComparer.OrdinalIgnoreCase);

        foreach (var detalhe in detalhes)
        {
            var novoPedido = !existentes.TryGetValue(detalhe.OrderSn, out var pedido);

            if (novoPedido)
            {
                pedido = new PedidoMarketplace
                {
                    VPedOrderId = detalhe.OrderSn,
                    VPedDtInc = DateTime.Now,
                    VPedDtCreate = DateTime.Now
                };

                _db.PedidosMarketplace.Add(pedido);
                existentes[detalhe.OrderSn] = pedido;
            }

            MapearPedido(pedido!, detalhe);

            if (pedido!.Itens.Count > 0)
            {
                _db.PedidosMarketplaceItens.RemoveRange(pedido.Itens);
                pedido.Itens.Clear();
            }

            foreach (var item in detalhe.ItemList)
            {
                var produtoVinculado = ObterProdutoVinculado(item, produtoPorItemId, produtoPorSku);
                var quantidade = Math.Max(1, item.Quantity);
                var totalItem = decimal.Round(item.Price * quantidade, 2);

                pedido.Itens.Add(new PedidoMarketplaceItem
                {
                    VPedIProductId = item.ItemId,
                    VPedIQuantity = quantidade,
                    ProdCod = produtoVinculado?.Codigo,
                    VPedPrice = decimal.Round(item.Price, 2),
                    VPedRefId = item.ModelSku ?? item.ItemSku ?? item.ModelId,
                    VPedName = Truncar(item.ItemName, 500),
                    ItemVPrcVenda = produtoVinculado?.PrecoVenda,
                    VPedIModelId = item.ModelId,
                    VPedISku = item.ModelSku ?? item.ItemSku,
                    VPedIDiscount = item.Discount.HasValue ? decimal.Round(item.Discount.Value, 2) : null,
                    VPedITotal = totalItem
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static void MapearPedido(PedidoMarketplace pedido, ShopeeOrderDetail detalhe)
    {
        var nome = detalhe.Recipient?.Name?.Trim();
        var primeiroNome = ExtrairPrimeiroNome(nome);
        var sobrenome = ExtrairSobrenome(nome);
        var totalItens = detalhe.ItemList.Sum(x => x.Price * Math.Max(1, x.Quantity));
        var valorFrete = detalhe.ActualShippingFee ?? detalhe.EstimatedShippingFee;
        var valorTotal = detalhe.TotalAmount ?? totalItens + (valorFrete ?? 0m);
        var descontos = Math.Max(0m, totalItens + (valorFrete ?? 0m) - valorTotal);
        var primeiraTransportadora = detalhe.PackageList
            .Select(x => x.ShippingCarrier)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        var primeiroRastreio = detalhe.PackageList
            .Select(x => x.TrackingNumber)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        pedido.VPedCreationDate = detalhe.CreateTime;
        pedido.VPedVlrTot = decimal.Round(valorTotal, 2);
        pedido.VPedUserProfileId = detalhe.BuyerUserId;
        pedido.VPedFirstName = Truncar(primeiroNome, 50);
        pedido.VPedLastName = Truncar(sobrenome, 50);
        pedido.VPedPostalCode = SomenteNumeros(detalhe.Recipient?.ZipCode, 50);
        pedido.VPedCity = Truncar(detalhe.Recipient?.City, 50);
        pedido.VPedState = NormalizarUf(detalhe.Recipient?.State);
        pedido.VPedStreet = Truncar(detalhe.Recipient?.FullAddress, 100);
        pedido.VPedNeighborhood = Truncar(detalhe.Recipient?.Town ?? detalhe.Recipient?.District, 100);
        pedido.VPedComplement = Truncar(detalhe.Recipient?.District, 100);
        pedido.VPedVlrTotItens = decimal.Round(totalItens, 2);
        pedido.VPedVlrDiscounts = decimal.Round(descontos, 2);
        pedido.VPedVlrShipping = valorFrete.HasValue ? decimal.Round(valorFrete.Value, 2) : null;
        pedido.VPedPhone = Truncar(SomenteNumeros(detalhe.Recipient?.Phone, 50), 50);
        pedido.VPedDeliveryCompany = Truncar(primeiraTransportadora, 50);
        pedido.VPedRastreio = Truncar(primeiroRastreio, 20);
        pedido.VPedTrackingNumber = Truncar(primeiroRastreio, 50);
        pedido.VPedLastUpdateDate = detalhe.UpdateTime ?? detalhe.CreateTime;
        pedido.VPedOrderStatusMarketplace = Truncar(detalhe.OrderStatus, 50);
        pedido.VPedSyncAt = DateTime.Now;
        pedido.VPedSyncError = null;
        pedido.VPedSyncHash = MontarHash(detalhe);

        if (string.IsNullOrWhiteSpace(pedido.VPedStatus))
            pedido.VPedStatus = "A";
    }

    private static ProdutoPedidoLookup? ObterProdutoVinculado(
        ShopeeOrderItem item,
        IReadOnlyDictionary<long, ProdutoPedidoLookup> produtoPorItemId,
        IReadOnlyDictionary<string, ProdutoPedidoLookup> produtoPorSku)
    {
        if (long.TryParse(item.ItemId, out var itemId)
            && produtoPorItemId.TryGetValue(itemId, out var porItem))
        {
            return porItem;
        }

        if (!string.IsNullOrWhiteSpace(item.ModelSku)
            && produtoPorSku.TryGetValue(item.ModelSku, out var porModelSku))
        {
            return porModelSku;
        }

        if (!string.IsNullOrWhiteSpace(item.ItemSku)
            && produtoPorSku.TryGetValue(item.ItemSku, out var porSku))
        {
            return porSku;
        }

        return null;
    }

    private static string MontarHash(ShopeeOrderDetail detalhe)
    {
        var baseHash =
            $"{detalhe.OrderSn}|{detalhe.OrderStatus}|{detalhe.UpdateTime:O}|{detalhe.TotalAmount}|{detalhe.ItemList.Count}";

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(baseHash)));
    }

    private static string? ExtrairPrimeiroNome(string? nomeCompleto)
    {
        if (string.IsNullOrWhiteSpace(nomeCompleto))
            return null;

        var partes = nomeCompleto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return partes.Length == 0 ? nomeCompleto : partes[0];
    }

    private static string? ExtrairSobrenome(string? nomeCompleto)
    {
        if (string.IsNullOrWhiteSpace(nomeCompleto))
            return null;

        var partes = nomeCompleto.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (partes.Length <= 1)
            return null;

        return string.Join(' ', partes.Skip(1));
    }

    private static string? NormalizarUf(string? uf)
    {
        if (string.IsNullOrWhiteSpace(uf))
            return null;

        var valor = uf.Trim();

        if (valor.Length == 2)
            return valor.ToUpperInvariant();

        var chave = RemoverAcentos(valor).ToLowerInvariant();
        return Ufs.TryGetValue(chave, out var mapeada) ? mapeada : null;
    }

    private static string? SomenteNumeros(string? valor, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var numeros = new string(valor.Where(char.IsDigit).ToArray());
        return Truncar(numeros, maxLength);
    }

    private static string? Truncar(string? valor, int tamanhoMaximo)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return valor;

        return valor.Length <= tamanhoMaximo
            ? valor.Trim()
            : valor.Trim()[..tamanhoMaximo];
    }

    private static string RemoverAcentos(string valor)
    {
        var normalized = valor.Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray();
        return new string(chars).Normalize(System.Text.NormalizationForm.FormC);
    }
}
