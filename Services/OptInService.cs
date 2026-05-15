using System.Net.Http.Headers;
using System.Text.Json;
using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Infrastructure;
using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Models;
using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Models.Requests;
using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Models.Responses;

namespace DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Services;

/// <summary>
/// Serviço responsável por processar opt-outs e atualizar preferências de
/// comunicação no Logix via API da Interplayers.
///
/// Tarefa 1 — regras de negócio implementadas:
///   channel_opt_out + whatsapp  → whatsApp = "N", demais canais mantidos
///   channel_opt_out + email     → email = "N", demais canais mantidos
///   global_opt_out              → todos os canais = "N"
/// </summary>
public class OptInService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly InterplayersAuthService _authService;
    private readonly IConfiguration _config;
    private readonly AuditLogger _auditLogger;
    private readonly IdempotencyService _idempotency;
    private readonly ILogger<OptInService> _logger;

    public OptInService(
        IHttpClientFactory httpClientFactory,
        InterplayersAuthService authService,
        IConfiguration config,
        AuditLogger auditLogger,
        IdempotencyService idempotency,
        ILogger<OptInService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _authService = authService;
        _config = config;
        _auditLogger = auditLogger;
        _idempotency = idempotency;
        _logger = logger;
    }

    /// <summary>
    /// Processa um opt-out aplicando as regras de negócio e chamando a Interplayers.
    /// </summary>
    /// <returns>ServiceResult indicando sucesso ou erro.</returns>
    public async Task<ServiceResult> ProcessOptOutAsync(OptOutRequest request, string? requestIp = null)
    {
        // --- Idempotência ---
        var idempotencyKey = request.IdempotencyKey;
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            if (_idempotency.IsAlreadyProcessed(idempotencyKey))
            {
                _logger.LogInformation("Requisição idempotente ignorada. Key: {Key}", idempotencyKey);
                return ServiceResult.Idempotent(idempotencyKey);
            }
        }

        // --- Resolver ConsumerId ---
        var (consumerId, identifierType) = ResolveConsumerId(request);
        if (string.IsNullOrEmpty(consumerId))
        {
            return ServiceResult.Failure("Nenhum identificador válido informado. Informe ConsumerId, Phone ou Email.", 400);
        }

        // --- Validar tipo de opt-out ---
        if (request.OptOutType != "channel_opt_out" && request.OptOutType != "global_opt_out")
        {
            return ServiceResult.Failure(
                $"OptOutType inválido: '{request.OptOutType}'. Use 'channel_opt_out' ou 'global_opt_out'.", 400);
        }

        if (request.OptOutType == "channel_opt_out" && string.IsNullOrEmpty(request.Channel))
        {
            return ServiceResult.Failure("Channel é obrigatório quando OptOutType = 'channel_opt_out'.", 400);
        }

        // --- Buscar estado atual do consumidor (necessário para channel_opt_out) ---
        var currentOptIns = await GetCurrentOptInsAsync(consumerId);
        if (currentOptIns == null)
        {
            _logger.LogWarning("Consumidor {ConsumerId} não encontrado ou sem dados de preferência.", consumerId);
            return ServiceResult.Failure("Consumidor não encontrado na base Interplayers.", 404);
        }

        // --- Aplicar regras de negócio ---
        var payload = BuildOptInPayload(request, currentOptIns);

        // --- Chamar Interplayers ---
        var adminId = _config["Interplayers:AdministratorId"];
        var baseUrl = _config["Interplayers:BaseUrlRegistration"];
        var url = $"{baseUrl}/v2/Registrations/administrators/{adminId}/consumers/external/{consumerId}/opt-ins";

        _logger.LogInformation("Enviando opt-out para Interplayers. Consumer={ConsumerId} Type={Type} Channel={Channel}",
            consumerId, request.OptOutType, request.Channel);

        var (httpStatus, errorCode, errorDescription) = await CallInterplayersAsync(url, payload);

        var success = httpStatus == 200;

        // --- Auditoria LGPD ---
        await _auditLogger.LogAsync(new AuditEntry
        {
            ConsumerId = consumerId,
            OriginalIdentifier = request.ConsumerId,
            IdentifierType = identifierType,
            Action = request.OptOutType,
            Channel = request.Channel,
            Origin = request.Origin,
            Timestamp = DateTime.UtcNow,
            Success = success,
            InterplayersStatusCode = httpStatus,
            InterplayersErrorCode = errorCode,
            IdempotencyKey = idempotencyKey,
            RequestIp = requestIp
        });

        if (!success)
        {
            _logger.LogWarning("Interplayers retornou erro. Status={Status} Code={Code} Desc={Desc}",
                httpStatus, errorCode, errorDescription);

            return httpStatus == 404
                ? ServiceResult.Failure($"Consumidor não encontrado na base Interplayers. ({errorCode})", 404)
                : ServiceResult.Failure($"Erro ao atualizar preferências: {errorDescription} ({errorCode})", 502);
        }

        // Marca idempotência após sucesso
        if (!string.IsNullOrEmpty(idempotencyKey))
            _idempotency.MarkAsProcessed(idempotencyKey);

        return ServiceResult.Ok("Opt-out processado com sucesso.", idempotencyKey);
    }

    // ─── Privados ────────────────────────────────────────────────────────────

    /// <summary>
    /// Busca o estado atual de opt-ins do consumidor na Interplayers.
    /// Necessário para manter outros canais intactos em channel_opt_out.
    /// </summary>
    private async Task<InterplayersOptInPayload?> GetCurrentOptInsAsync(string consumerId)
    {
        try
        {
            var token = await _authService.GetTokenAsync();
            var client = _httpClientFactory.CreateClient("interplayers");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var adminId = _config["Interplayers:AdministratorId"];
            var baseUrl = _config["Interplayers:BaseUrlRegistration"];
            var url = $"{baseUrl}/v2/Registrations/administrators/{adminId}/consumers/external/{consumerId}";

            _logger.LogInformation("Buscando opt-ins atuais. URL={Url}", url);

            var response = await client.GetAsync(url);
            var statusCode = (int)response.StatusCode;

            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Falha ao buscar opt-ins atuais. Status={Status} Body={Body}", 
                    statusCode, content);
                return null;
            }

            if (string.IsNullOrEmpty(content))
            {
                _logger.LogWarning("Resposta vazia ao buscar opt-ins do consumidor {ConsumerId}", consumerId);
                return null;
            }

            _logger.LogDebug("Resposta de opt-ins: {Content}", content);

            // TODO: Ajuste conforme a estrutura real da resposta da Interplayers
            var result = System.Text.Json.JsonSerializer.Deserialize<dynamic>(content);
            
            // Exemplo: extrair do objeto aninhado
            return new InterplayersOptInPayload
            {
                WhatsApp = GetFieldValue(result, "whatsApp") ?? "Y",
                Email = GetFieldValue(result, "email") ?? "Y",
                Phone = GetFieldValue(result, "phone") ?? "Y",
                Sms = GetFieldValue(result, "sms") ?? "Y",
                Push = GetFieldValue(result, "push") ?? "Y",
                Mail = GetFieldValue(result, "mail") ?? "Y"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar opt-ins atuais do consumidor {ConsumerId}", consumerId);
            return null;
        }
    }

    private static string? GetFieldValue(dynamic obj, string fieldName)
    {
        try
        {
            var value = obj?[fieldName]?.ToString();
            return value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Regras de negócio:
    /// - channel_opt_out whatsapp → whatsApp = N (outros mantêm valor atual)
    /// - channel_opt_out email    → email = N (outros mantêm valor atual)
    /// - global_opt_out           → todos = N
    /// </summary>
    private static InterplayersOptInPayload BuildOptInPayload(OptOutRequest request, InterplayersOptInPayload currentState)
    {
        if (request.OptOutType == "global_opt_out")
        {
            return new InterplayersOptInPayload
            {
                WhatsApp = "N",
                Email = "N",
                Phone = "N",
                Sms = "N",
                Push = "N",
                Mail = "N"
            };
        }

        // channel_opt_out — desabilita apenas o canal solicitado, mantém outros
        var channel = request.Channel?.ToLowerInvariant();
        return channel switch
        {
            "whatsapp" => new InterplayersOptInPayload
            {
                WhatsApp = "N",
                Email = currentState.Email,
                Phone = currentState.Phone,
                Sms = currentState.Sms,
                Push = currentState.Push,
                Mail = currentState.Mail
            },
            "email" => new InterplayersOptInPayload
            {
                WhatsApp = currentState.WhatsApp,
                Email = "N",
                Phone = currentState.Phone,
                Sms = currentState.Sms,
                Push = currentState.Push,
                Mail = currentState.Mail
            },
            "sms" => new InterplayersOptInPayload
            {
                WhatsApp = currentState.WhatsApp,
                Email = currentState.Email,
                Phone = currentState.Phone,
                Sms = "N",
                Push = currentState.Push,
                Mail = currentState.Mail
            },
            "phone" => new InterplayersOptInPayload
            {
                WhatsApp = currentState.WhatsApp,
                Email = currentState.Email,
                Phone = "N",
                Sms = currentState.Sms,
                Push = currentState.Push,
                Mail = currentState.Mail
            },
            "push" => new InterplayersOptInPayload
            {
                WhatsApp = currentState.WhatsApp,
                Email = currentState.Email,
                Phone = currentState.Phone,
                Sms = currentState.Sms,
                Push = "N",
                Mail = currentState.Mail
            },
            "mail" => new InterplayersOptInPayload
            {
                WhatsApp = currentState.WhatsApp,
                Email = currentState.Email,
                Phone = currentState.Phone,
                Sms = currentState.Sms,
                Push = currentState.Push,
                Mail = "N"
            },
            _ => throw new ArgumentException($"Canal inválido: '{request.Channel}'.")
        };
    }

    /// <summary>
    /// Resolve o identificador do consumidor.
    /// Prioridade: ConsumerId → Phone → Email.
    ///
    /// NOTA: lookup por phone/email precisa de endpoint da Interplayers ou
    /// consulta local — implemente ResolveByPhoneAsync / ResolveByEmailAsync
    /// conforme disponibilidade da API.
    /// </summary>
    private static (string? consumerId, string identifierType) ResolveConsumerId(OptOutRequest request)
    {
        if (!string.IsNullOrEmpty(request.ConsumerId))
            return (request.ConsumerId, "id");

        return (null, "unknown");
    }

    private async Task<(int statusCode, string? errorCode, string? errorDescription)>
        CallInterplayersAsync(string url, InterplayersOptInPayload payload)
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
            _logger.LogError(ex, "Falha de conexão com Interplayers (opt-ins).");
            return (502, "CONN_ERROR", "Falha de conexão com a Interplayers.");
        }

        var statusCode = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
            return (statusCode, null, null);

        // Tenta extrair código de erro da Interplayers
        try
        {
            var contentLength = response.Content.Headers.ContentLength ?? 0;
            if (contentLength == 0)
            {
                _logger.LogWarning("Interplayers retornou resposta vazia. Status={Status}", statusCode);
                return (statusCode, "EMPTY_RESPONSE", "Interplayers retornou resposta vazia.");
            }

            var errorBody = await response.Content.ReadFromJsonAsync<InterplayersErrorResponse>();
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
}
