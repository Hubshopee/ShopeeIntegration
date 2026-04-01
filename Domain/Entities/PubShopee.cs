namespace Domain.Entities;

public class PubShopee
{
    public int Id { get; set; }
    public decimal? Codigo { get; set; }
    public decimal? AplCod { get; set; }
    public string? SyncConta { get; set; }
    public int? ItemId { get; set; }
    public DateTime? PubDtInc { get; set; }
    public string? PubStatus { get; set; }
}
