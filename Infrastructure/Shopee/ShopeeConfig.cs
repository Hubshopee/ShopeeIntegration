namespace Infrastructure.Shopee;

public class ShopeeConfig
{
    public string BaseUrl { get; set; } = "";
    public int SyncShopeeId { get; set; } = 1;
    public bool EnableRefreshTokenCheck { get; set; } = false;
}
