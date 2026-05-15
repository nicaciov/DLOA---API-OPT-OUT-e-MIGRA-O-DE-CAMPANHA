using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Models.Responses;
using Microsoft.Extensions.Caching.Memory;

namespace DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Services;

/// <summary>
/// Gerencia a autenticação OAuth 2.0 com a plataforma Interplayers.
/// Implementa cache do token com margem de segurança de 5 minutos antes do vencimento.
/// </summary>
public class InterplayersAuthService
{
    private const string CacheKey = "interplayers_access_token";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;
    private readonly ILogger<InterplayersAuthService> _logger;

    public InterplayersAuthService(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        IMemoryCache cache,
        ILogger<InterplayersAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Retorna um Bearer token válido. Usa cache em memória;
    /// renova automaticamente 5 minutos antes do vencimento.
    /// </summary>
    public async Task<string> GetTokenAsync()
    {
        if (_cache.TryGetValue(CacheKey, out string? cached) && cached != null)
        {
            _logger.LogDebug("Token Interplayers obtido do cache.");
            return cached;
        }

        _logger.LogInformation("Token Interplayers expirado ou ausente. Renovando...");

        var token = await FetchNewTokenAsync();

        // Subtrai 5 minutos do TTL para renovar antes do vencimento real
        _cache.Set(CacheKey, token.AccessToken, TimeSpan.FromSeconds(token.ExpiresIn - 300));

        _logger.LogInformation("Token Interplayers renovado. Válido por {Seconds}s.", token.ExpiresIn - 300);

        return token.AccessToken;
    }

    private async Task<TokenResponse> FetchNewTokenAsync()
    {
        var tokenUrl = _config["Interplayers:TokenUrl"]
            ?? throw new InvalidOperationException("Interplayers:TokenUrl não configurado.");

        var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id",
                _config["Interplayers:ClientId"] ?? throw new InvalidOperationException("Interplayers:ClientId não configurado.")),
            new KeyValuePair<string, string>("client_secret",
                _config["Interplayers:ClientSecret"] ?? throw new InvalidOperationException("Interplayers:ClientSecret não configurado.")),
            new KeyValuePair<string, string>("scope",
                _config["Interplayers:Scope"] ?? throw new InvalidOperationException("Interplayers:Scope não configurado."))
        });

        var client = _httpClientFactory.CreateClient("interplayers_auth");

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(tokenUrl, body);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Falha de conexão ao endpoint de token da Interplayers.");
            throw new InterplayersAuthException("Não foi possível conectar ao servidor de autenticação da Interplayers.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var raw = await response.Content.ReadAsStringAsync();
            _logger.LogError("Autenticação Interplayers falhou. Status: {Status}. Body: {Body}",
                (int)response.StatusCode, raw);
            throw new InterplayersAuthException(
                $"Autenticação Interplayers retornou {(int)response.StatusCode}. Verifique as credenciais.");
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>()
            ?? throw new InterplayersAuthException("Resposta do token está vazia ou mal formada.");

        return token;
    }
}

/// <summary>Exceção específica para falhas de autenticação com a Interplayers.</summary>
public class InterplayersAuthException : Exception
{
    public InterplayersAuthException(string message) : base(message) { }
    public InterplayersAuthException(string message, Exception inner) : base(message, inner) { }
}
