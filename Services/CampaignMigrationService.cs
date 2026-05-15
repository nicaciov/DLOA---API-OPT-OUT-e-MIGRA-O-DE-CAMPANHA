using System.Net.Http.Headers;
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
    /// </summary>
    public async Task<ServiceResult> MigrateCampaignAsync(CampaignMigrateRequest request, string? requestIp = null)
    {
        // --- Idempotência ---
        if (!string.IsNullOrEmpty(request.IdempotencyKey))
        {
            if (_idempotency.IsAlreadyProcessed(request.IdempotencyKey))
            {
                _logger.LogInformation("Migração idempotente ignorada. Key: {Key}", request.IdempotencyKey);
                return ServiceResult.Idempotent(request.IdempotencyKey);
            }
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
            "Migrando campanha. Consumer={ConsumerId} Ean={Ean} NewCampaign={NewCampaignId} Indication={Indication}",
            request.ConsumerId, request.Ean, request.NewCampaignId, request.Indication);

        var (httpStatus, errorCode, errorDescription) = await CallMigrateAsync(url, payload);
        var success = httpStatus == 200;

        // --- Auditoria ---
        await _auditLogger.LogAsync(new AuditEntry
        {
            ConsumerId = request.ConsumerId,
            Action = "campaign_migrate",
            Origin = request.Origin,
            Timestamp = DateTime.UtcNow,
            Success = success,
            InterplayersStatusCode = httpStatus,
            InterplayersErrorCode = errorCode,
            IdempotencyKey = request.IdempotencyKey,
            RequestIp = requestIp,
            AdditionalInfo = System.Text.Json.JsonSerializer.Serialize(new
            {
                ean = request.Ean,
                newCampaignId = request.NewCampaignId,
                indication = request.Indication
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

        if (!string.IsNullOrEmpty(request.IdempotencyKey))
            _idempotency.MarkAsProcessed(request.IdempotencyKey);

        return ServiceResult.Ok("Campanha migrada com sucesso.", request.IdempotencyKey);
    }

    private async Task<(int statusCode, string? errorCode, string? errorDescription)>
        CallMigrateAsync(string url, InterplayersMigrateCampaignPayload payload)
    {
        var token = await _authService.GetTokenAsync();
        var client = _httpClientFactory.CreateClient("interplayers");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

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

        var statusCode = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
            return (statusCode, null, null);

        try
        {
            var errorBody = await response.Content.ReadFromJsonAsync<InterplayersErrorResponse>();
            var code = errorBody?.Data?.Error ?? "UNKNOWN";
            var desc = errorBody?.Data?.ErrorDescription ?? "Erro desconhecido.";
            return (statusCode, code, desc);
        }
        catch
        {
            return (statusCode, "PARSE_ERROR", "Erro ao interpretar resposta da Interplayers.");
        }
    }
}
