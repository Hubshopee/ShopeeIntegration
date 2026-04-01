namespace Domain.Entities;

public class Produto
{
    public int Id { get; set; }
    public decimal? Codigo { get; set; }
    public decimal? AplCod { get; set; }
    public string? Sku { get; set; }
    public string? Descricao { get; set; }
    public string? Fabricante { get; set; }
    public string? Veiculo { get; set; }
    public int? AnoInicial { get; set; }
    public int? AnoFinal { get; set; }
    public int? Peso { get; set; }
    public string? CodigosOriginais { get; set; }
    public decimal? PrecoCusto { get; set; }
    public decimal? PrecoVenda { get; set; }
    public int? Estoque { get; set; }
    public string? DescricaoDetalhada { get; set; }
    public string? Fornecedor { get; set; }
    public DateTime? DataInclusao { get; set; }
    public string? InfoCompl { get; set; }
    public decimal? Largura { get; set; }
    public decimal? Altura { get; set; }
    public decimal? Profundidade { get; set; }
    public string? Unidade { get; set; }
    public int? QtdeEmbalagem { get; set; }
    public string? CodigoReferencia { get; set; }
    public DateTime? DataEstoque { get; set; }
    public DateTime? DataDados { get; set; }
    public decimal? PrecoVarejo { get; set; }
    public decimal? PrecoPadrao { get; set; }
    public string? Excluir { get; set; }
    public string? Gtin { get; set; }
    public DateTime? DataExclusao { get; set; }
    public DateTime? DataPreco { get; set; }
    public string? Ncm { get; set; }
    public string? Origem { get; set; }
    public string? Titulo { get; set; }
    public long? CategoriaId { get; set; }
    public long? MarcaId { get; set; }
    public long? ChannelId { get; set; }
}
