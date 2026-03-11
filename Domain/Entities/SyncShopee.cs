namespace Domain.Entities;

public class SyncShopee
{
    public int Id { get; set; }
    public DateTime? SincDtRefToken { get; set; }
    public string? SincAccessToken { get; set; }
    public string? SincRefreshToken { get; set; }
    public int? PartnerId { get; set; }
    public string? ClientSecret { get; set; }
    public int? ShopId { get; set; }
    public DateTime? SincDtEst { get; set; }
    public DateTime? SincDtPreco { get; set; }
}