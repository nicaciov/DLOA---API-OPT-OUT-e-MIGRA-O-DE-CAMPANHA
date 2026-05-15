namespace DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Middleware;

/// <summary>
/// Middleware de autenticação por API Key.
/// Todas as requisições ao gateway devem incluir o header X-Api-Key.
/// Configurado em appsettings.json: Gateway:ApiKey
/// </summary>
public class ApiKeyMiddleware
{
    private const string ApiKeyHeader = "X-Api-Key";
    private readonly RequestDelegate _next;
    private readonly IConfiguration _config;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration config, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _config = config;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Libera Swagger em desenvolvimento
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var receivedKey))
        {
            _logger.LogWarning("Requisição sem API Key de {Ip}.", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "API Key ausente.", header = ApiKeyHeader });
            return;
        }

        var configuredKey = _config["Gateway:ApiKey"];
        if (string.IsNullOrEmpty(configuredKey) || receivedKey != configuredKey)
        {
            _logger.LogWarning("API Key inválida de {Ip}.", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = "API Key inválida." });
            return;
        }

        await _next(context);
    }
}
