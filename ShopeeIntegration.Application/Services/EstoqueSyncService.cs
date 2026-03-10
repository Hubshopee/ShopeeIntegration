using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopeeIntegration.Infrastructure.Persistence;

namespace ShopeeIntegration.Application.Services;

public class TokenSyncService
{
    private readonly IntegrationDbContext _db;
    private readonly ILogger<TokenSyncService> _logger;

    public TokenSyncService(
        IntegrationDbContext db,
        ILogger<TokenSyncService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<string> ObterTokenValido()
    {
        var sync = await _db.SyncShopee
            .OrderBy(x => x.Id)
            .FirstAsync();

        if (string.IsNullOrWhiteSpace(sync.SincAccessToken))
            throw new Exception("SINCACCESSTOKEN não está preenchido na SINCSHOPEE.");

        _logger.LogInformation(
            "Usando access token salvo no banco. PartnerId:{partnerId} ShopId:{shopId}",
            sync.PartnerId,
            sync.ShopId
        );

        return sync.SincAccessToken;
    }
}