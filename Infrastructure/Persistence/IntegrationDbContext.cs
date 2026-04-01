using Microsoft.EntityFrameworkCore;
using Domain.Entities;

namespace Infrastructure.Persistence;

public class IntegrationDbContext : DbContext
{
    public IntegrationDbContext(DbContextOptions<IntegrationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<ImagemFtp> ImagensFtp => Set<ImagemFtp>();
    public DbSet<Atributo> Atributos => Set<Atributo>();
    public DbSet<SyncShopee> SyncShopee => Set<SyncShopee>();
    public DbSet<PubShopee> PublicacoesShopee => Set<PubShopee>();
    public DbSet<PubShopeeErro> PublicacoesShopeeErro => Set<PubShopeeErro>();
    public DbSet<PedidoMarketplace> PedidosMarketplace => Set<PedidoMarketplace>();
    public DbSet<PedidoMarketplaceItem> PedidosMarketplaceItens => Set<PedidoMarketplaceItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Produto>(entity =>
        {
            entity.ToTable("PRODUTO");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("ID");
            entity.Property(x => x.Codigo).HasColumnName("CODIGO").HasPrecision(7, 0);
            entity.Property(x => x.AplCod).HasColumnName("APLCOD").HasPrecision(7, 0);
            entity.Property(x => x.Sku).HasColumnName("SKU");
            entity.Property(x => x.Descricao).HasColumnName("DESCRICAO");
            entity.Property(x => x.Fabricante).HasColumnName("FABRICANTE");
            entity.Property(x => x.Veiculo).HasColumnName("VEICULO");
            entity.Property(x => x.AnoInicial).HasColumnName("ANO_INICIAL");
            entity.Property(x => x.AnoFinal).HasColumnName("ANO_FINAL");
            entity.Property(x => x.Peso).HasColumnName("PESO").HasPrecision(18, 3);
            entity.Property(x => x.CodigosOriginais).HasColumnName("CODIGOS_ORIGINAIS");
            entity.Property(x => x.PrecoCusto).HasColumnName("PRECO_CUSTO").HasPrecision(18, 2);
            entity.Property(x => x.PrecoVenda).HasColumnName("PRECO_VENDA").HasPrecision(18, 2);
            entity.Property(x => x.Estoque).HasColumnName("ESTOQUE");
            entity.Property(x => x.DescricaoDetalhada).HasColumnName("DESCRICAO_DETALHADA");
            entity.Property(x => x.Fornecedor).HasColumnName("FORNECEDOR");
            entity.Property(x => x.DataInclusao).HasColumnName("DATA_INCLUSAO");
            entity.Property(x => x.InfoCompl).HasColumnName("INFO_COMPL");
            entity.Property(x => x.Largura).HasColumnName("LARGURA").HasPrecision(18, 2);
            entity.Property(x => x.Altura).HasColumnName("ALTURA").HasPrecision(18, 2);
            entity.Property(x => x.Profundidade).HasColumnName("PROFUNDIDADE").HasPrecision(18, 2);
            entity.Property(x => x.Unidade).HasColumnName("UNIDADE");
            entity.Property(x => x.QtdeEmbalagem).HasColumnName("QTDE_EMBALAGEM");
            entity.Property(x => x.CodigoReferencia).HasColumnName("CODIGO_REFERENCIA");
            entity.Property(x => x.DataEstoque).HasColumnName("DATA_ESTOQUE");
            entity.Property(x => x.DataDados).HasColumnName("DATA_DADOS");
            entity.Property(x => x.PrecoVarejo).HasColumnName("PRECO_VAREJO").HasPrecision(18, 2);
            entity.Property(x => x.PrecoPadrao).HasColumnName("PRECO_PADRAO").HasPrecision(18, 2);
            entity.Property(x => x.Excluir).HasColumnName("EXCLUIR");
            entity.Property(x => x.Gtin).HasColumnName("GTIN");
            entity.Property(x => x.DataExclusao).HasColumnName("DATA_EXCLUSAO");
            entity.Property(x => x.DataPreco).HasColumnName("DATA_PRECO");
            entity.Property(x => x.Ncm).HasColumnName("NCM");
            entity.Property(x => x.Origem).HasColumnName("ORIGEM");
            entity.Property(x => x.Titulo).HasColumnName("TITULO");
            entity.Property(x => x.CategoriaId).HasColumnName("CATEGORIA_ID");
            entity.Property(x => x.MarcaId).HasColumnName("MARCA_ID");
            entity.Property(x => x.ChannelId).HasColumnName("CHANNEL_ID");
        });

        modelBuilder.Entity<ImagemFtp>(entity =>
        {
            entity.ToTable("IMAGENS_FTP");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("ID");
            entity.Property(x => x.ProdCod).HasColumnName("PRODCOD").HasPrecision(7, 0);
            entity.Property(x => x.Letras).HasColumnName("LETRAS");
            entity.Property(x => x.ComCod).HasColumnName("COMCOD");
            entity.Property(x => x.CDescCod).HasColumnName("CDESCCOD");
        });

        modelBuilder.Entity<Atributo>(entity =>
        {
            entity.ToTable("ATRIBUTO");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("ID");
            entity.Property(x => x.ProdCod).HasColumnName("PRODCOD").HasPrecision(7, 0);
            entity.Property(x => x.AttributeId).HasColumnName("ATTRIBUTE_ID");
            entity.Property(x => x.AttributeName).HasColumnName("ATTRIBUTE_NAME");
            entity.Property(x => x.ValueId).HasColumnName("VALUE_ID");
            entity.Property(x => x.ValueName).HasColumnName("VALUE_NAME");
            entity.Property(x => x.Ordem).HasColumnName("ORDEM");
        });

        modelBuilder.Entity<SyncShopee>(entity =>
        {
            entity.ToTable("SINCSHOPEE");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("ID");
            entity.Property(x => x.SyncConta).HasColumnName("SINCCONTA");
            entity.Property(x => x.SincDtRefToken).HasColumnName("SINCDTREFTOKEN");
            entity.Property(x => x.SincAccessToken).HasColumnName("SINCACCESSTOKEN");
            entity.Property(x => x.SincRefreshToken).HasColumnName("SINCREFRESHTOKEN");
            entity.Property(x => x.PartnerId).HasColumnName("PARTNERID");
            entity.Property(x => x.ClientSecret).HasColumnName("CLIENTSECRET");
            entity.Property(x => x.ShopId).HasColumnName("SHOPID");
            entity.Property(x => x.SincDtEst).HasColumnName("SINCDTEST");
            entity.Property(x => x.SincDtPreco).HasColumnName("SINCDTPRECO");
            entity.Property(x => x.SincDtDados).HasColumnName("SINCDTDADOS");
            entity.Property(x => x.SincDtExclusao).HasColumnName("SINCDTEXCLUSAO");
            entity.Property(x => x.SincDtPedidos).HasColumnName("SINCDTPEDIDOS");
        });

        modelBuilder.Entity<PubShopee>(entity =>
        {
            entity.ToTable("PUBSHOPEE");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("ID");
            entity.Property(x => x.Codigo).HasColumnName("CODIGO").HasPrecision(7, 0);
            entity.Property(x => x.AplCod).HasColumnName("APLCOD").HasPrecision(7, 0);
            entity.Property(x => x.SyncConta).HasColumnName("SINCCONTA");
            entity.Property(x => x.ItemId).HasColumnName("ITEMID");
            entity.Property(x => x.PubDtInc).HasColumnName("PUBDTINC");
            entity.Property(x => x.PubStatus).HasColumnName("PUBSTATUS");
        });

        modelBuilder.Entity<PubShopeeErro>(entity =>
        {
            entity.ToTable("PUBSHOPEEERRO");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("ID");
            entity.Property(x => x.Codigo).HasColumnName("CODIGO").HasPrecision(7, 0);
            entity.Property(x => x.AplCod).HasColumnName("APLCOD").HasPrecision(7, 0);
            entity.Property(x => x.SyncConta).HasColumnName("SINCCONTA");
            entity.Property(x => x.Operacao).HasColumnName("OPERACAO");
            entity.Property(x => x.MensagemErro).HasColumnName("MENSAGEMERRO");
            entity.Property(x => x.DtError).HasColumnName("DTERROR");
            entity.Property(x => x.ItemId).HasColumnName("ITEMID");
            entity.Property(x => x.StatusProcesso).HasColumnName("STATUSPROCESSO");
        });

        modelBuilder.Entity<PedidoMarketplace>(entity =>
        {
            entity.ToTable("VTPEDIDO");
            entity.HasKey(x => x.VPedCod);

            entity.Property(x => x.VPedCod).HasColumnName("VPEDCOD");
            entity.Property(x => x.VPedCreationDate).HasColumnName("VPEDCREATIONDATE");
            entity.Property(x => x.VPedOrderId).HasColumnName("VPEDORDERID");
            entity.Property(x => x.VPedVlrTot).HasColumnName("VPEDVLRTOT").HasPrecision(12, 2);
            entity.Property(x => x.VPedUserProfileId).HasColumnName("VPEDUSERPROFILEID");
            entity.Property(x => x.VPedFirstName).HasColumnName("VPEDFIRSTNAME");
            entity.Property(x => x.VPedLastName).HasColumnName("VPEDLASTNAME");
            entity.Property(x => x.VPedEmail).HasColumnName("VPEDEMAIL");
            entity.Property(x => x.VPedDocument).HasColumnName("VPEDDOCUMENT");
            entity.Property(x => x.VPedPostalCode).HasColumnName("VPEDPOSTALCODE");
            entity.Property(x => x.VPedCity).HasColumnName("VPEDCITY");
            entity.Property(x => x.VPedState).HasColumnName("VPEDSTATE");
            entity.Property(x => x.VPedStreet).HasColumnName("VPEDSTREET");
            entity.Property(x => x.VPedNumber).HasColumnName("VPEDNUMBER");
            entity.Property(x => x.VPedNeighborhood).HasColumnName("VPEDNEIGHBORHOOD");
            entity.Property(x => x.VPedComplement).HasColumnName("VPEDCOMPLEMENT");
            entity.Property(x => x.VPedReference).HasColumnName("VPEDREFERENCE");
            entity.Property(x => x.VPedVlrTotItens).HasColumnName("VPEDVLRTOTITENS").HasPrecision(12, 2);
            entity.Property(x => x.VPedVlrDiscounts).HasColumnName("VPEDVLRDISCOUNTS").HasPrecision(12, 2);
            entity.Property(x => x.VPedVlrShipping).HasColumnName("VPEDVLRSHIPPING").HasPrecision(12, 2);
            entity.Property(x => x.VPedVlrTax).HasColumnName("VPEDVLRTAX").HasPrecision(12, 2);
            entity.Property(x => x.VPedPhone).HasColumnName("VPEDPHONE");
            entity.Property(x => x.VPedStatus).HasColumnName("VPEDSTATUS");
            entity.Property(x => x.VPedDtInc).HasColumnName("VPEDDTINC");
            entity.Property(x => x.PedCod).HasColumnName("PEDCOD").HasPrecision(12, 0);
            entity.Property(x => x.VPedNf).HasColumnName("VPEDNF");
            entity.Property(x => x.ConCod).HasColumnName("CONCOD");
            entity.Property(x => x.NfCod).HasColumnName("NFCOD").HasPrecision(7, 0);
            entity.Property(x => x.VPedMotivo).HasColumnName("VPEDMOTIVO");
            entity.Property(x => x.VPedCompleto).HasColumnName("VPEDCOMPLETO");
            entity.Property(x => x.VPedCombine).HasColumnName("VPEDCOMBINE");
            entity.Property(x => x.CliCod).HasColumnName("CLICOD").HasPrecision(5, 0);
            entity.Property(x => x.VPedDocumentType).HasColumnName("VPEDDOCUMENTTYPE");
            entity.Property(x => x.VPedStateInscription).HasColumnName("VPEDSTATEINSCRIPTION");
            entity.Property(x => x.VPedEtiqueta).HasColumnName("VPEDETIQUETA");
            entity.Property(x => x.VPedDeliveryCompany).HasColumnName("VPEDDELIVERYCOMPANY");
            entity.Property(x => x.VPedRastreio).HasColumnName("VPEDRASTREIO");
            entity.Property(x => x.VPedFatEnv).HasColumnName("VPEDFATENV");
            entity.Property(x => x.VPedDtRastreio).HasColumnName("VPEDDTRASTREIO");
            entity.Property(x => x.VPedDtFatEnv).HasColumnName("VPEDDTFATENV");
            entity.Property(x => x.VPedArqCxml).HasColumnName("VPEDARQCXML");
            entity.Property(x => x.VPedDtArq).HasColumnName("VPEDDTARQ");
            entity.Property(x => x.UsuCodReimp).HasColumnName("USUCODREIMP").HasPrecision(5, 0);
            entity.Property(x => x.VPedMotReimp).HasColumnName("VPEDMOTREIMP");
            entity.Property(x => x.VPedDtReimp).HasColumnName("VPEDDTREIMP");
            entity.Property(x => x.UsuCodEtiq).HasColumnName("USUCODETIQ").HasPrecision(5, 0);
            entity.Property(x => x.VPedDtEtiq).HasColumnName("VPEDDTETIQ");
            entity.Property(x => x.VPedArqEti).HasColumnName("VPEDARQETI");
            entity.Property(x => x.VPedDtArqEti).HasColumnName("VPEDDTARQETI");
            entity.Property(x => x.UsuCodInc).HasColumnName("USUCODINC").HasPrecision(5, 0);
            entity.Property(x => x.VPedDtReady).HasColumnName("VPEDDTREADY");
            entity.Property(x => x.VPedDtCreate).HasColumnName("VPEDDTCREATE");
            entity.Property(x => x.VPedTrackingNumber).HasColumnName("VPEDTRACKINGNUMBER");
            entity.Property(x => x.VPedLastUpdateDate).HasColumnName("VPEDLASTUPDATEDATE");
            entity.Property(x => x.VPedOrderStatusMarketplace).HasColumnName("VPEDORDERSTATUSMARKETPLACE");
            entity.Property(x => x.VPedSyncAt).HasColumnName("VPEDSYNCAT");
            entity.Property(x => x.VPedSyncError).HasColumnName("VPEDSYNCERROR");
            entity.Property(x => x.VPedSyncHash).HasColumnName("VPEDSYNCHASH");

            entity.HasMany(x => x.Itens)
                .WithOne(x => x.Pedido)
                .HasForeignKey(x => x.VPedCod)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PedidoMarketplaceItem>(entity =>
        {
            entity.ToTable("VTPEDIDOITEM");
            entity.HasKey(x => x.VPedICod);

            entity.Property(x => x.VPedICod).HasColumnName("VPEDICOD");
            entity.Property(x => x.VPedCod).HasColumnName("VPEDCOD");
            entity.Property(x => x.VPedIProductId).HasColumnName("VPEDIPRODUCTID");
            entity.Property(x => x.VPedIQuantity).HasColumnName("VPEDIQUANTITY");
            entity.Property(x => x.ProdCod).HasColumnName("PRODCOD").HasPrecision(7, 0);
            entity.Property(x => x.VPedPrice).HasColumnName("VPEDPRICE").HasPrecision(12, 2);
            entity.Property(x => x.VPedRefId).HasColumnName("VPEDREFID");
            entity.Property(x => x.VPedName).HasColumnName("VPEDNAME");
            entity.Property(x => x.ItemVPrcVenda).HasColumnName("ITEMVPRCVENDA").HasPrecision(11, 2);
            entity.Property(x => x.VPedIModelId).HasColumnName("VPEDIMODELID");
            entity.Property(x => x.VPedISku).HasColumnName("VPEDISKU");
            entity.Property(x => x.VPedIDiscount).HasColumnName("VPEDIDISCOUNT").HasPrecision(12, 2);
            entity.Property(x => x.VPedITotal).HasColumnName("VPEDITOTAL").HasPrecision(12, 2);
        });
    }
}
