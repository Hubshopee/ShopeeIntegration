namespace Domain.Entities;

public class Atributo
{
    public int Id { get; set; }
    public decimal? ProdCod { get; set; }
    public long AttributeId { get; set; }
    public string? AttributeName { get; set; }
    public long? ValueId { get; set; }
    public string? ValueName { get; set; }
    public int Ordem { get; set; }
}
