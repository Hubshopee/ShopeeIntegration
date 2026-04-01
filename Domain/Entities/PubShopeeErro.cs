namespace Domain.Entities;

public class PubShopeeErro
{
    public int Id { get; set; }
    public decimal Codigo { get; set; }
    public decimal? AplCod { get; set; }
    public string? SyncConta { get; set; }
    public string? Operacao { get; set; }
    public string? MensagemErro { get; set; }
    public DateTime DtError { get; set; }
    public string? ItemId { get; set; }
    public string? StatusProcesso { get; set; }
}
