using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Infrastructure;
using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Models;
using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Models.Requests;
using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Models.Responses;

namespace DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Services;

/// <summary>
/// Serviço responsável pela migração de campanha no Logix.
///
/// Tarefa 2 — fluxo:
///   Paciente PDV sem indicação responde pesquisa no WhatsApp
///   → Bot envia a indicação para este serviço
///   → Serviço migra o consumidor para a campanha correspondente
///   → Segmentação fica disponível nas réguas de comunicação
/// </summary>
public class CampaignMigrationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly InterplayersAuthService _authService;
    private readonly IConfiguration _config;
    private readonly AuditLogger _auditLogger;
    private readonly IdempotencyService _idempotency;
    private readonly ILogger<CampaignMigrationService> _logger;

    private static readonly char[] _chars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

    public CampaignMigrationService(
        IHttpClientFactory httpClientFactory,
        InterplayersAuthService authService,
        IConfiguration config,
        AuditLogger auditLogger,
        IdempotencyService idempotency,
        ILogger<CampaignMigrationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _authService = authService;
        _config = config;
        _auditLogger = auditLogger;
        _idempotency = idempotency;
        _logger = logger;
    }

    /// <summary>
    /// Migra um consumidor para uma nova campanha com base na indicação informada.
    /// A chave de idempotência é gerada automaticamente — não deve ser enviada no payload.
    /// </summary>
    public async Task<ServiceResult> MigrateCampaignAsync(CampaignMigrateRequest request, string? requestIp = null)
    {
        // --- Gera IdempotencyKey automaticamente ---
        var idempotencyKey = GerarChaveIdempotencia(10);
        _logger.LogInformation("IdempotencyKey gerado: {Key}", idempotencyKey);

        // --- Idempotência ---
        if (_idempotency.IsAlreadyProcessed(idempotencyKey))
        {
            _logger.LogInformation("Migração idempotente ignorada. Key: {Key}", idempotencyKey);
            return ServiceResult.Idempotent(idempotencyKey);
        }

        // --- Validações básicas ---
        if (string.IsNullOrEmpty(request.ConsumerId))
            return ServiceResult.Failure("ConsumerId é obrigatório.", 400);

        if (string.IsNullOrEmpty(request.Ean))
            return ServiceResult.Failure("Ean é obrigatório.", 400);

        if (string.IsNullOrEmpty(request.NewCampaignId))
            return ServiceResult.Failure("NewCampaignId é obrigatório.", 400);

        var adminId = _config["Interplayers:AdministratorId"];
        var baseUrl = _config["Interplayers:BaseUrlAdhesion"];
        var url = $"{baseUrl}/v2/Adhesion/administrators/{adminId}/consumers/{request.ConsumerId}/products/migrate-campaign";

        var payload = new InterplayersMigrateCampaignPayload
        {
            Product = new MigrateProductPayload
            {
                Ean = request.Ean,
                NewCampaignId = request.NewCampaignId
            }
        };

        _logger.LogInformation(
            "Migrando campanha. Consumer={ConsumerId} Ean={Ean} NewCampaign={NewCampaignId}",
            request.ConsumerId, request.Ean, request.NewCampaignId);

        var (httpStatus, errorCode, errorDescription) = await CallMigrateAsync(url, payload);
        var success = httpStatus == 200;

        // --- Auditoria ---
        await _auditLogger.LogAsync(new AuditEntry
        {
            ConsumerId = request.ConsumerId,
            Action = "campaign_migrate", 
            Timestamp = DateTime.UtcNow,
            Success = success,
            InterplayersStatusCode = httpStatus,
            InterplayersErrorCode = errorCode,
            IdempotencyKey = idempotencyKey,
            RequestIp = requestIp,
            AdditionalInfo = JsonSerializer.Serialize(new
            {
                ean = request.Ean,
                newCampaignId = request.NewCampaignId
            })
        });

        if (!success)
        {
            _logger.LogWarning("Migração falhou. Status={Status} Code={Code} Desc={Desc}",
                httpStatus, errorCode, errorDescription);

            return httpStatus switch
            {
                404 => ServiceResult.Failure($"Consumidor não encontrado. ({errorCode})", 404),
                400 => ServiceResult.Failure($"Campanha inválida: {errorDescription} ({errorCode})", 400),
                _ => ServiceResult.Failure($"Erro ao migrar campanha: {errorDescription} ({errorCode})", 502)
            };
        }

        _idempotency.MarkAsProcessed(idempotencyKey);

        return ServiceResult.Ok("Campanha migrada com sucesso.", idempotencyKey);
    }

    // ─── Privados ────────────────────────────────────────────────────────────

    private async Task<(int statusCode, string? errorCode, string? errorDescription)>
        CallMigrateAsync(string url, InterplayersMigrateCampaignPayload payload)
    {
        var token = await _authService.GetTokenAsync();
        var client = _httpClientFactory.CreateClient("interplayers");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // --- Log completo da requisição (evidência) ---
        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        var tokenPreview = token.Length > 20
            ? $"{token[..20]}...{token[^10..]}"
            : token;

        _logger.LogInformation(
            "\n╔══════════════════════════════════════════════════════╗" +
            "\n║         REQUISIÇÃO MIGRAÇÃO → INTERPLAYERS           ║" +
            "\n╚══════════════════════════════════════════════════════╝" +
            "\nMétodo  : PATCH" +
            "\nURL     : {Url}" +
            "\nHeaders :" +
            "\n  Authorization : Bearer {TokenPreview}" +
            "\n  Content-Type  : application/json" +
            "\nBody    :\n{Payload}" +
            "\n\nCURL equivalente:" +
            "\ncurl -X PATCH \"{Url}\" \\" +
            "\n  -H \"Authorization: Bearer {Token}\" \\" +
            "\n  -H \"Content-Type: application/json\" \\" +
            "\n  -d '{PayloadCurl}'",
            url, tokenPreview, payloadJson,
            url, token, payloadJson.Replace("'", "'\\''"));
        // -----------------------------------------------

        HttpResponseMessage response;
        try
        {
            response = await client.PatchAsJsonAsync(url, payload);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Falha de conexão com Interplayers (migrate-campaign).");
            return (502, "CONN_ERROR", "Falha de conexão com a Interplayers.");
        }

        // --- Log completo da resposta (evidência) ---
        var responseBody = await response.Content.ReadAsStringAsync();
        var formattedBody = responseBody;
        try
        {
            if (!string.IsNullOrEmpty(responseBody))
            {
                var doc = JsonDocument.Parse(responseBody);
                formattedBody = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            }
        }
        catch { /* mantém body original se não for JSON */ }

        var responseHeaders = string.Join("\n  ", response.Headers
            .Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"));

        _logger.LogInformation(
            "\n╔══════════════════════════════════════════════════════╗" +
            "\n║          RESPOSTA MIGRAÇÃO ← INTERPLAYERS            ║" +
            "\n╚══════════════════════════════════════════════════════╝" +
            "\nStatus  : {StatusCode} {ReasonPhrase}" +
            "\nHeaders :\n  {Headers}" +
            "\nBody    :\n{Body}",
            (int)response.StatusCode,
            response.ReasonPhrase,
            string.IsNullOrEmpty(responseHeaders) ? "(nenhum)" : responseHeaders,
            string.IsNullOrEmpty(formattedBody) ? "(vazio)" : formattedBody);
        // -----------------------------------------------

        var statusCode = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
            return (statusCode, null, null);

        try
        {
            if (string.IsNullOrEmpty(responseBody))
            {
                _logger.LogWarning("Interplayers retornou resposta vazia. Status={Status}", statusCode);
                return (statusCode, "EMPTY_RESPONSE", "Interplayers retornou resposta vazia.");
            }

            var errorBody = JsonSerializer.Deserialize<InterplayersErrorResponse>(responseBody);
            var code = errorBody?.Data?.Error ?? errorBody?.Message ?? "UNKNOWN";
            var desc = errorBody?.Data?.ErrorDescription ?? errorBody?.Message ?? "Erro desconhecido.";
            return (statusCode, code, desc);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Erro ao fazer parse JSON da resposta de erro. Status={Status}", statusCode);
            return (statusCode, "PARSE_ERROR", "Erro ao interpretar resposta da Interplayers.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar resposta de erro. Status={Status}", statusCode);
            return (statusCode, "UNKNOWN_ERROR", "Erro ao processar resposta da Interplayers.");
        }
    }

    /// <summary>
    /// Gera uma chave de idempotência alfanumérica aleatória de N caracteres.
    /// </summary>
    private static string GerarChaveIdempotencia(int tamanho)
    {
        var random = new Random();
        return new string(Enumerable.Range(0, tamanho)
            .Select(_ => _chars[random.Next(_chars.Length)])
            .ToArray());
    }
}