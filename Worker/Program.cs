using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Application.Services;
using Infrastructure.Persistence;
using Infrastructure.Shopee;
using Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<ShopeeConfig>(
    builder.Configuration.GetSection("Shopee")
);
builder.Services.Configure<SyncScheduleOptions>(
    builder.Configuration.GetSection("SyncSchedule")
);

var connection = builder.Configuration.GetConnectionString("SqlServer");

if (string.IsNullOrWhiteSpace(connection))
    throw new Exception("A connection string 'SqlServer' nÃ£o foi encontrada no appsettings.json.");

builder.Services.AddDbContext<IntegrationDbContext>(options =>
    options.UseSqlServer(connection, sql =>
    {
        sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
    }));

builder.Services.AddHttpClient<ShopeeAuthService>()
.ConfigureHttpClient((service, client) =>
{
    var config = service.GetRequiredService<IOptions<ShopeeConfig>>().Value;
    client.BaseAddress = new Uri(config.BaseUrl);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});

builder.Services.AddHttpClient<ShopeeCatalogService>()
.ConfigureHttpClient((service, client) =>
{
    var config = service.GetRequiredService<IOptions<ShopeeConfig>>().Value;
    client.BaseAddress = new Uri(config.BaseUrl);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});

builder.Services.AddScoped<TokenSyncService>();
builder.Services.AddScoped<CadastroProdutoService>();
builder.Services.AddScoped<EstoqueSyncService>();
builder.Services.AddScoped<PrecoSyncService>();
builder.Services.AddScoped<ExclusaoSyncService>();
builder.Services.AddScoped<DadosSyncService>();
builder.Services.AddScoped<PedidoSyncService>();
builder.Services.AddScoped<CatalogoNoturnoSyncService>();

builder.Services.AddHostedService<SyncWorker>();

var host = builder.Build();
host.Run();
