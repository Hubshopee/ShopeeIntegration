using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShopeeIntegration.Application.Services;
using ShopeeIntegration.Infrastructure.Persistence;
using ShopeeIntegration.Infrastructure.Shopee;
using ShopeeIntegration.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<ShopeeConfig>(
    builder.Configuration.GetSection("Shopee")
);

var connection = builder.Configuration.GetConnectionString("SqlServer");

if (string.IsNullOrWhiteSpace(connection))
    throw new Exception("A connection string 'SqlServer' não foi encontrada no appsettings.json.");

builder.Services.AddDbContext<IntegrationDbContext>(options =>
    options.UseSqlServer(connection, sql =>
    {
        sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
    }));

builder.Services.AddHttpClient<ShopeeClient>((service, client) =>
{
    var config = service.GetRequiredService<IOptions<ShopeeConfig>>().Value;
    client.BaseAddress = new Uri(config.BaseUrl);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});

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

builder.Services.AddSingleton<ShopeeRequestLimiter>();
builder.Services.AddSingleton<TokenService>();

builder.Services.AddScoped<TokenSyncService>();
builder.Services.AddScoped<EstoqueSyncService>();
builder.Services.AddScoped<PrecoSyncService>();
builder.Services.AddScoped<DadosSyncService>();
builder.Services.AddScoped<CatalogoNoturnoSyncService>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
