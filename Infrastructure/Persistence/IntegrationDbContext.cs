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
            entity.Property(x => x.Estoque).HasColumnName("ESTOQUE");
            entity.Property(x => x.DataInclusao).HasColumnName("DATA_INCLUSAO");
            entity.Property(x => x.DataEstoque).HasColumnName("DATA_ESTOQUE");
            entity.Property(x => x.DataDados).HasColumnName("DATA_DADOS");
            entity.Property(x => x.DataPreco).HasColumnName("DATA_PRECO");
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
        });
    }
}