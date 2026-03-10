namespace ShopeeIntegration.Infrastructure.Shopee;

public class ShopeeConfig
{
    public string BaseUrl { get; set; } = "";
    public string RefreshBaseUrl { get; set; } = "";
    public int PartnerId { get; set; }
    public string PartnerKey { get; set; } = "";
}