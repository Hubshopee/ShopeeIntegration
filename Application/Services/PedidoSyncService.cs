using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Shopee;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services;

public class PedidoSyncService
{
    private sealed record ProdutoPedidoLookup(decimal? Codigo, decimal? PrecoVenda);
    private sealed record EnderecoPedido(string? Street, string? Number, string? Neighborhood, string? Complement);

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
        var dataCheckpoint = DateTime.Now;

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
            sync.SincDtPedidos = dataCheckpoint;
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
            sync.SincDtPedidos = dataCheckpoint;
            await _db.SaveChangesAsync(cancellationToken);
            return 0;
        }

        await UpsertPedidos(detalhes, cancellationToken);

        sync.SincDtPedidos = dataCheckpoint;
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
        var endereco = SepararEndereco(detalhe.Recipient);
        var enderecoMascarado = EnderecoMascarado(endereco, detalhe.Recipient);
        var primeiroNome = ExtrairPrimeiroNome(nome);
        var sobrenome = ExtrairSobrenome(nome);
        var totalItens = detalhe.ItemList.Sum(x => x.Price * Math.Max(1, x.Quantity));
        var valorFrete = ObterValorFrete(detalhe);
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

        if (!enderecoMascarado || string.IsNullOrWhiteSpace(pedido.VPedStreet))
            pedido.VPedStreet = Truncar(endereco.Street, 100);

        if (!enderecoMascarado || string.IsNullOrWhiteSpace(pedido.VPedNumber))
            pedido.VPedNumber = Truncar(endereco.Number, 50);

        if (!enderecoMascarado || string.IsNullOrWhiteSpace(pedido.VPedNeighborhood))
            pedido.VPedNeighborhood = Truncar(endereco.Neighborhood, 100);

        if (!enderecoMascarado || string.IsNullOrWhiteSpace(pedido.VPedComplement))
            pedido.VPedComplement = Truncar(endereco.Complement, 100);

        pedido.VPedVlrTotItens = decimal.Round(totalItens, 2);
        pedido.VPedVlrDiscounts = descontos > 0m
            ? decimal.Round(descontos, 2)
            : null;
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

    private static decimal? ObterValorFrete(ShopeeOrderDetail detalhe)
    {
        if (detalhe.ActualShippingFee.HasValue && detalhe.ActualShippingFee.Value > 0)
            return detalhe.ActualShippingFee.Value;

        if (detalhe.EstimatedShippingFee.HasValue && detalhe.EstimatedShippingFee.Value > 0)
            return detalhe.EstimatedShippingFee.Value;

        return detalhe.ActualShippingFee ?? detalhe.EstimatedShippingFee;
    }

    private static bool EnderecoMascarado(EnderecoPedido endereco, ShopeeRecipientAddress? recipient)
    {
        var fullAddress = NormalizarTextoPedido(recipient?.FullAddress);

        return ContemMascara(fullAddress)
            || ContemMascara(endereco.Street)
            || ContemMascara(endereco.Number)
            || ContemMascara(endereco.Neighborhood)
            || ContemMascara(endereco.Complement);
    }

    private static EnderecoPedido SepararEndereco(ShopeeRecipientAddress? recipient)
    {
        var fullAddress = NormalizarTextoPedido(recipient?.FullAddress);
        var preferredNeighborhood = NormalizarTextoPedido(recipient?.District ?? recipient?.Town);
        var city = NormalizarTextoPedido(recipient?.City);
        var state = NormalizarTextoPedido(recipient?.State);
        var zipCode = SomenteNumeros(recipient?.ZipCode, 20);

        if (string.IsNullOrWhiteSpace(fullAddress))
        {
            return new EnderecoPedido(
                null,
                null,
                preferredNeighborhood,
                null
            );
        }

        var partes = fullAddress
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizarTextoPedido)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        while (partes.Count > 0)
        {
            var ultimaParte = partes[^1];
            var ultimaParteNumeros = SomenteNumeros(ultimaParte, 20);

            if (!string.IsNullOrWhiteSpace(zipCode) && ultimaParteNumeros == zipCode)
            {
                partes.RemoveAt(partes.Count - 1);
                continue;
            }

            if (TextoEquivalente(ultimaParte, city) || TextoEquivalente(ultimaParte, state) || TextoEquivalente(NormalizarUf(ultimaParte), NormalizarUf(state)))
            {
                partes.RemoveAt(partes.Count - 1);
                continue;
            }

            break;
        }

        if (partes.Count == 0)
        {
            return new EnderecoPedido(
                Truncar(fullAddress, 100),
                null,
                preferredNeighborhood,
                null
            );
        }

        var street = partes[0];
        string? number = null;
        var indiceInicioComplemento = 1;

        if (partes.Count > 1 && PareceNumeroEndereco(partes[1]))
        {
            number = partes[1];
            indiceInicioComplemento = 2;
        }
        else
        {
            (street, number) = SepararNumeroNoFim(street);
        }

        var restantes = partes.Skip(indiceInicioComplemento).ToList();
        string? neighborhood = preferredNeighborhood;

        if (!string.IsNullOrWhiteSpace(neighborhood))
        {
            var indiceBairro = restantes.FindIndex(x => TextoEquivalente(x, neighborhood));

            if (indiceBairro >= 0)
                restantes.RemoveAt(indiceBairro);
        }
        else if (restantes.Count > 0)
        {
            neighborhood = restantes[0];
            restantes.RemoveAt(0);
        }

        var complement = restantes.Count > 0
            ? string.Join(", ", restantes)
            : null;

        return new EnderecoPedido(
            street,
            number,
            neighborhood,
            complement
        );
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
        valor = NormalizarTextoPedido(valor);

        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var numeros = new string(valor.Where(char.IsDigit).ToArray());
        return Truncar(numeros, maxLength);
    }

    private static string? Truncar(string? valor, int tamanhoMaximo)
    {
        valor = NormalizarTextoPedido(valor);

        if (string.IsNullOrWhiteSpace(valor))
            return valor;

        return valor.Length <= tamanhoMaximo
            ? valor.Trim()
            : valor.Trim()[..tamanhoMaximo];
    }

    private static string? NormalizarTextoPedido(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return valor;

        var normalizado = valor.Trim();
        var decodificado = System.Net.WebUtility.UrlDecode(normalizado);

        if (!string.IsNullOrWhiteSpace(decodificado))
            normalizado = decodificado;

        return normalizado
            .Replace("\u00A0", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static bool PareceNumeroEndereco(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return false;

        var texto = valor.Trim();

        if (string.Equals(texto, "S/N", StringComparison.OrdinalIgnoreCase))
            return true;

        return System.Text.RegularExpressions.Regex.IsMatch(texto, @"^\d+[A-Za-z0-9\-\/]*$");
    }

    private static (string? Street, string? Number) SepararNumeroNoFim(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return (valor, null);

        var match = System.Text.RegularExpressions.Regex.Match(valor, @"^(.*?)[,\s]+(\d+[A-Za-z0-9\-\/]*)$");

        if (!match.Success)
            return (valor, null);

        return (
            match.Groups[1].Value.Trim(),
            match.Groups[2].Value.Trim()
        );
    }

    private static bool TextoEquivalente(string? esquerda, string? direita)
    {
        if (string.IsNullOrWhiteSpace(esquerda) || string.IsNullOrWhiteSpace(direita))
            return false;

        return string.Equals(
            RemoverAcentos(esquerda).Trim(),
            RemoverAcentos(direita).Trim(),
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool ContemMascara(string? valor)
    {
        return !string.IsNullOrWhiteSpace(valor) && valor.Contains('*');
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
