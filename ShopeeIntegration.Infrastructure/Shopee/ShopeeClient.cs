using Microsoft.Extensions.Options;

namespace ShopeeIntegration.Infrastructure.Shopee;

public class ShopeeClient
{
    private readonly HttpClient _http;
    private readonly ShopeeConfig _config;

    public ShopeeClient(HttpClient http, IOptions<ShopeeConfig> config)
    {
        _http = http;
        _config = config.Value;
    }

    public async Task<string> GetOrders(string accessToken, int shopId)
    {
        var path = "/api/v2/order/get_order_list";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var baseString = $"{_config.PartnerId}{path}{timestamp}{accessToken}{shopId}";
        var sign = ShopeeSigner.Generate(_config.PartnerKey, baseString);

        var url =
            $"{path}?partner_id={_config.PartnerId}" +
            $"&timestamp={timestamp}" +
            $"&access_token={accessToken}" +
            $"&shop_id={shopId}" +
            $"&sign={sign}";

        var response = await _http.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Exception($"Shopee get orders falhou. Status: {(int)response.StatusCode}. Resposta: {body}");
        }

        return await response.Content.ReadAsStringAsync();
    }
}