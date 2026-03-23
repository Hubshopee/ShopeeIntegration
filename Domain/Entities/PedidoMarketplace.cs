namespace Domain.Entities;

public class PedidoMarketplace
{
    public int VPedCod { get; set; }
    public DateTime? VPedCreationDate { get; set; }
    public string? VPedOrderId { get; set; }
    public decimal? VPedVlrTot { get; set; }
    public string? VPedUserProfileId { get; set; }
    public string? VPedFirstName { get; set; }
    public string? VPedLastName { get; set; }
    public string? VPedEmail { get; set; }
    public string? VPedDocument { get; set; }
    public string? VPedPostalCode { get; set; }
    public string? VPedCity { get; set; }
    public string? VPedState { get; set; }
    public string? VPedStreet { get; set; }
    public string? VPedNumber { get; set; }
    public string? VPedNeighborhood { get; set; }
    public string? VPedComplement { get; set; }
    public string? VPedReference { get; set; }
    public decimal? VPedVlrTotItens { get; set; }
    public decimal? VPedVlrDiscounts { get; set; }
    public decimal? VPedVlrShipping { get; set; }
    public decimal? VPedVlrTax { get; set; }
    public string? VPedPhone { get; set; }
    public string? VPedStatus { get; set; }
    public DateTime? VPedDtInc { get; set; }
    public decimal? PedCod { get; set; }
    public string? VPedNf { get; set; }
    public int? ConCod { get; set; }
    public decimal? NfCod { get; set; }
    public string? VPedMotivo { get; set; }
    public string? VPedCompleto { get; set; }
    public string? VPedCombine { get; set; }
    public decimal? CliCod { get; set; }
    public string? VPedDocumentType { get; set; }
    public string? VPedStateInscription { get; set; }
    public string? VPedEtiqueta { get; set; }
    public string? VPedDeliveryCompany { get; set; }
    public string? VPedRastreio { get; set; }
    public string? VPedFatEnv { get; set; }
    public DateTime? VPedDtRastreio { get; set; }
    public DateTime? VPedDtFatEnv { get; set; }
    public byte[]? VPedArqCxml { get; set; }
    public DateTime? VPedDtArq { get; set; }
    public decimal? UsuCodReimp { get; set; }
    public string? VPedMotReimp { get; set; }
    public DateTime? VPedDtReimp { get; set; }
    public decimal? UsuCodEtiq { get; set; }
    public DateTime? VPedDtEtiq { get; set; }
    public byte[]? VPedArqEti { get; set; }
    public DateTime? VPedDtArqEti { get; set; }
    public decimal? UsuCodInc { get; set; }
    public DateTime? VPedDtReady { get; set; }
    public DateTime? VPedDtCreate { get; set; }
    public string? VPedTrackingNumber { get; set; }
    public DateTime? VPedLastUpdateDate { get; set; }
    public string? VPedOrderStatusMarketplace { get; set; }
    public DateTime? VPedSyncAt { get; set; }
    public string? VPedSyncError { get; set; }
    public string? VPedSyncHash { get; set; }

    public ICollection<PedidoMarketplaceItem> Itens { get; set; } = new List<PedidoMarketplaceItem>();
}
