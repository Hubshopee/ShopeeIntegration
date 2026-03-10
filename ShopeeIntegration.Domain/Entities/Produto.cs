namespace ShopeeIntegration.Domain.Entities;

public class Produto
{
    public int Id { get; set; }
    public decimal? Codigo { get; set; }
    public string? Sku { get; set; }
    public string? Descricao { get; set; }
    public int? Estoque { get; set; }
    public DateTime? DataInclusao { get; set; }
    public DateTime? DataEstoque { get; set; }
    public DateTime? DataDados { get; set; }
    public DateTime? DataPreco { get; set; }
}