using Microsoft.Extensions.Options;
using ShopeeIntegration.Domain.Entities;
using System.Net.Http.Json;
using System.Text.Json;

namespace ShopeeIntegration.Infrastructure.Shopee;

public class ShopeeAuthService
{
    private readonly HttpClient _http;
    private readonly ShopeeConfig _config;

    public ShopeeAuthService(HttpClient http, IOptions<ShopeeConfig> config)
    {
        _http = http;
        _config = config.Value;
    }

    public async Task<TokenResponse> GetAccessToken(string code, int shopId)
    {
        var path = "/api/v2/auth/token/get";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sign = ShopeeSigner.Generate(_config.PartnerKey, $"{_config.PartnerId}{path}{timestamp}");

        var url =
            $"{_config.BaseUrl}{path}" +
            $"?partner_id={_config.PartnerId}" +
            $"&timestamp={timestamp}" +
            $"&sign={sign}";

        var response = await _http.PostAsJsonAsync(url, new
        {
            code,
            shop_id = shopId,
            partner_id = _config.PartnerId
        });

        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Shopee token get falhou. Status: {(int)response.StatusCode}. Resposta: {body}");

        return JsonSerializer.Deserialize<TokenResponse>(body)
            ?? throw new Exception("A resposta da Shopee veio vazia ao obter access token.");
    }

    public async Task<TokenResponse> RefreshToken(string refreshToken, int shopId)
    {
        var path = "/api/v2/auth/access_token/get";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sign = ShopeeSigner.Generate(_config.PartnerKey, $"{_config.PartnerId}{path}{timestamp}");

        var url =
            $"{_config.RefreshBaseUrl}{path}" +
            $"?partner_id={_config.PartnerId}" +
            $"&timestamp={timestamp}" +
            $"&sign={sign}";

        var response = await _http.PostAsJsonAsync(url, new
        {
            refresh_token = refreshToken,
            shop_id = shopId,
            partner_id = _config.PartnerId
        });

        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Shopee refresh token falhou. Status: {(int)response.StatusCode}. Resposta: {body}");

        return JsonSerializer.Deserialize<TokenResponse>(body)
            ?? throw new Exception("A resposta da Shopee veio vazia ao renovar token.");
    }
}