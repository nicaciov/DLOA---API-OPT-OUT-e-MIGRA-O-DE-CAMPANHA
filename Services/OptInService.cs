using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Infrastructure;
using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Models;
using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Models.Requests;
using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Models.Responses;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Services;

/// <summary>
/// Serviço responsável por processar opt-outs e atualizar preferências de
/// comunicação no Logix via API da Interplayers.
///
/// Tarefa 1 — regras de negócio implementadas:
///   channel_opt_out + whatsapp  → whatsApp = "N", demais canais = "S"
///   channel_opt_out + email     → email = "N", demais canais = "S"
///   global_opt_out              → todos os canais = "N"
///
/// Nota: Todos os 7 campos (incluindo informativeMaterial) são sempre enviados
/// para evitar o erro LR52 da Interplayers.
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
    public async Task<ServiceResult> ProcessOptOutAsync(OptOutRequest request, string idempotencyKey, string? requestIp = null)
    {
        // --- Idempotência ---
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
            return ServiceResult.Failure("Nenhum identificador válido informado. Informe ConsumerId.", 400);
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

        // --- Aplicar regras de negócio ---
        var payload = BuildOptInPayload(request);

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
    /// Monta o payload com todos os 7 campos sempre preenchidos.
    ///
    /// channel_opt_out → canal solicitado = "N", todos os outros = "S"
    /// global_opt_out  → todos os canais = "N" (incluindo informativeMaterial)
    /// </summary>
    private static InterplayersOptInPayload BuildOptInPayload(OptOutRequest request)
    {
        if (request.OptOutType == "global_opt_out")
        {
            return new InterplayersOptInPayload
            {
                InformativeMaterial = "N",
                WhatsApp = "N",
                Email = "N",
                Phone = "N",
                Sms = "N",
                Push = "N",
                Mail = "N"
            };
        }

        // channel_opt_out — canal solicitado = "N", todos os outros = "S"
        var channel = request.Channel?.ToLowerInvariant();
        return channel switch
        {
            "whatsapp" => new InterplayersOptInPayload
            {
                InformativeMaterial = "S",
                WhatsApp = "N",
                Email = "S",
                Phone = "S",
                Sms = "S",
                Push = "S",
                Mail = "S"
            },
            "email" => new InterplayersOptInPayload
            {
                InformativeMaterial = "S",
                WhatsApp = "S",
                Email = "N",
                Phone = "S",
                Sms = "S",
                Push = "S",
                Mail = "S"
            },
            "sms" => new InterplayersOptInPayload
            {
                InformativeMaterial = "S",
                WhatsApp = "S",
                Email = "S",
                Phone = "S",
                Sms = "N",
                Push = "S",
                Mail = "S"
            },
            "phone" => new InterplayersOptInPayload
            {
                InformativeMaterial = "S",
                WhatsApp = "S",
                Email = "S",
                Phone = "N",
                Sms = "S",
                Push = "S",
                Mail = "S"
            },
            "push" => new InterplayersOptInPayload
            {
                InformativeMaterial = "S",
                WhatsApp = "S",
                Email = "S",
                Phone = "S",
                Sms = "S",
                Push = "N",
                Mail = "S"
            },
            "mail" => new InterplayersOptInPayload
            {
                InformativeMaterial = "S",
                WhatsApp = "S",
                Email = "S",
                Phone = "S",
                Sms = "S",
                Push = "S",
                Mail = "N"
            },
            _ => throw new ArgumentException($"Canal inválido: '{request.Channel}'.")
        };
    }

    /// <summary>
    /// Resolve o identificador do consumidor a partir do request.
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
            "\n║              REQUISIÇÃO → INTERPLAYERS               ║" +
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
            _logger.LogError(ex, "Falha de conexão com Interplayers (opt-ins).");
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
            "\n║             RESPOSTA ← INTERPLAYERS                  ║" +
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
}