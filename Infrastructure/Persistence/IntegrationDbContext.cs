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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Produto>(entity =>
        {
            entity.ToTable("PRODUTO");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("ID");
            entity.Property(x => x.Codigo).HasColumnName("CODIGO").HasPrecision(7, 0);
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
            entity.Property(x => x.DescricaoShopee).HasColumnName("DESCRICAO_SHOPEE");
            entity.Property(x => x.CategoriaId).HasColumnName("CATEGORIA_ID");
            entity.Property(x => x.MarcaId).HasColumnName("MARCA_ID");
            entity.Property(x => x.ChannelId).HasColumnName("CHANNEL_ID");
            entity.Property(x => x.ItemId).HasColumnName("ITEM_ID");
            entity.Property(x => x.Status).HasColumnName("STATUS");
            entity.Property(x => x.Erro).HasColumnName("ERRO");
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
            entity.Property(x => x.SincDtRefToken).HasColumnName("SINCDTREFTOKEN");
            entity.Property(x => x.SincAccessToken).HasColumnName("SINCACCESSTOKEN");
            entity.Property(x => x.SincRefreshToken).HasColumnName("SINCREFRESHTOKEN");
            entity.Property(x => x.PartnerId).HasColumnName("PARTNERID");
            entity.Property(x => x.ClientSecret).HasColumnName("CLIENTSECRET");
            entity.Property(x => x.ShopId).HasColumnName("SHOPID");
            entity.Property(x => x.SincDtEst).HasColumnName("SINCDTEST");
            entity.Property(x => x.SincDtPreco).HasColumnName("SINCDTPRECO");
            entity.Property(x => x.SincDtDados).HasColumnName("SINCDTDADOS");
        });
    }
}
