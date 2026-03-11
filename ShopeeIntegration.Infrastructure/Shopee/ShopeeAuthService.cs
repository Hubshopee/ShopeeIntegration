using Microsoft.Extensions.Options;
using ShopeeIntegration.Domain.Entities;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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

    public async Task<TokenResponse> RefreshToken(string refreshToken, int partnerId, string partnerKey, int shopId)
    {
        var path = "/api/v2/auth/access_token/get";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sign = ShopeeSigner.Generate(partnerKey, $"{partnerId}{path}{timestamp}");
        var payload = new RefreshTokenRequest
        {
            PartnerId = partnerId,
            ShopId = shopId,
            RefreshToken = refreshToken
        };

        return await EnviarRequisicaoAuth(path, partnerId, timestamp, sign, payload, "refresh token");
    }

    private async Task<TokenResponse> EnviarRequisicaoAuth(
        string path,
        int partnerId,
        long timestamp,
        string sign,
        object payload,
        string operacao)
    {
        var url = MontarUrl(path, partnerId, timestamp, sign);
        var json = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        request.Version = new Version(1, 1);
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        request.Headers.ExpectContinue = false;

        var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Shopee {operacao} falhou. URL: {url}. Status: {(int)response.StatusCode}. Resposta: {body}");

        return ExtrairTokenResponse(body, operacao);
    }

    private string MontarUrl(string path, int partnerId, long timestamp, string sign)
    {
        return
            $"{_config.BaseUrl.TrimEnd('/')}{path}" +
            $"?partner_id={partnerId}" +
            $"&timestamp={timestamp}" +
            $"&sign={sign}";
    }

    private static TokenResponse ExtrairTokenResponse(string body, string operacao)
    {
        var root = JsonNode.Parse(body)
            ?? throw new Exception($"A resposta da Shopee veio vazia ao executar {operacao}.");

        var responseNode = root["response"] ?? root;
        var token = responseNode.Deserialize<TokenResponse>();

        return token ?? throw new Exception($"Nao foi possivel interpretar a resposta da Shopee ao executar {operacao}. Body: {body}");
    }

    private sealed class RefreshTokenRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("partner_id")]
        public int PartnerId { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("shop_id")]
        public int ShopId { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string RefreshToken { get; init; } = "";
    }
}
