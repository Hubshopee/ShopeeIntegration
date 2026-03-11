using System.Text.Json.Serialization;

namespace Domain.Entities;

public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = "";

    [JsonPropertyName("expire_in")]
    public int ExpireIn { get; set; }
}