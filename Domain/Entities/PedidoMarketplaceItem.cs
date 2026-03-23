namespace Domain.Entities;

public class PedidoMarketplaceItem
{
    public int VPedICod { get; set; }
    public int? VPedCod { get; set; }
    public string? VPedIProductId { get; set; }
    public int? VPedIQuantity { get; set; }
    public decimal? ProdCod { get; set; }
    public decimal? VPedPrice { get; set; }
    public string? VPedRefId { get; set; }
    public string? VPedName { get; set; }
    public decimal? ItemVPrcVenda { get; set; }
    public string? VPedIModelId { get; set; }
    public string? VPedISku { get; set; }
    public decimal? VPedIDiscount { get; set; }
    public decimal? VPedITotal { get; set; }

    public PedidoMarketplace? Pedido { get; set; }
}
