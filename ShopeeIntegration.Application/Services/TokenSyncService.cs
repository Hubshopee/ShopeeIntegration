using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopeeIntegration.Infrastructure.Persistence;
using ShopeeIntegration.Infrastructure.Shopee;

namespace ShopeeIntegration.Application.Services;

public class TokenSyncService
{
    private readonly IntegrationDbContext _db;
    private readonly ShopeeAuthService _auth;
    private readonly ILogger<TokenSyncService> _logger;

    public TokenSyncService(
        IntegrationDbContext db,
        ShopeeAuthService auth,
        ILogger<TokenSyncService> logger)
    {
        _db = db;
        _auth = auth;
        _logger = logger;
    }

    public async Task<string> ObterTokenValido()
    {
        var sync = await _db.SyncShopee
            .OrderBy(x => x.Id)
            .FirstAsync();

        _logger.LogInformation(
            "Token sync PartnerId:{partnerId} ShopId:{shopId} UltimoRefresh:{data}",
            sync.PartnerId,
            sync.ShopId,
            sync.SincDtRefToken
        );

        if (string.IsNullOrWhiteSpace(sync.SincAccessToken))
            throw new Exception("SINCACCESSTOKEN nao esta preenchido na SINCSHOPEE.");

        if (!sync.SincDtRefToken.HasValue)
        {
            sync.SincDtRefToken = DateTime.Now;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Token existente encontrado, data de refresh inicializada");
            return sync.SincAccessToken;
        }

        if (sync.SincDtRefToken.Value > DateTime.Now.AddHours(-4))
        {
            _logger.LogInformation("Token ainda valido, refresh nao necessario");
            return sync.SincAccessToken;
        }

        if (string.IsNullOrWhiteSpace(sync.SincRefreshToken))
            throw new Exception("SINCREFRESHTOKEN nao esta preenchido na SINCSHOPEE.");

        if (!sync.PartnerId.HasValue)
            throw new Exception("PARTNERID nao esta preenchido na SINCSHOPEE.");

        if (string.IsNullOrWhiteSpace(sync.ClientSecret))
            throw new Exception("CLIENTSECRET nao esta preenchido na SINCSHOPEE.");

        if (!sync.ShopId.HasValue)
            throw new Exception("SHOPID nao esta preenchido na SINCSHOPEE.");

        _logger.LogInformation("Iniciando refresh do token");

        var token = await _auth.RefreshToken(
            sync.SincRefreshToken,
            sync.PartnerId.Value,
            sync.ClientSecret,
            sync.ShopId.Value
        );

        sync.SincAccessToken = token.AccessToken;
        sync.SincRefreshToken = token.RefreshToken;
        sync.SincDtRefToken = DateTime.Now;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Token atualizado com sucesso");

        return sync.SincAccessToken;
    }
}
