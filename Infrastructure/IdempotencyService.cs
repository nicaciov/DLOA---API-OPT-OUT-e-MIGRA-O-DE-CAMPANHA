using Microsoft.Extensions.Caching.Memory;

namespace DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Infrastructure;

/// <summary>
/// Gerencia idempotência de requisições.
/// Previne que a mesma operação seja executada duas vezes com a mesma chave.
///
/// Em produção, substituir IMemoryCache por IDistributedCache (Redis)
/// para suportar múltiplas instâncias do serviço.
/// </summary>
public class IdempotencyService
{
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;
    private readonly ILogger<IdempotencyService> _logger;

    public IdempotencyService(IMemoryCache cache, IConfiguration config, ILogger<IdempotencyService> logger)
    {
        _cache = cache;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Verifica se uma chave já foi processada.
    /// </summary>
    /// <returns>true se a chave JÁ foi processada (requisição duplicada).</returns>
    public bool IsAlreadyProcessed(string key)
    {
        return _cache.TryGetValue(BuildCacheKey(key), out _);
    }

    /// <summary>
    /// Registra uma chave como processada.
    /// TTL configurável em appsettings.json (Gateway:IdempotencyTtlMinutes).
    /// </summary>
    public void MarkAsProcessed(string key)
    {
        var ttlMinutes = _config.GetValue("Gateway:IdempotencyTtlMinutes", 60);
        _cache.Set(BuildCacheKey(key), true, TimeSpan.FromMinutes(ttlMinutes));
        _logger.LogDebug("Idempotency key registrada: {Key} (TTL: {Ttl}min)", key, ttlMinutes);
    }

    private static string BuildCacheKey(string key) => $"idempotency:{key}";
}
