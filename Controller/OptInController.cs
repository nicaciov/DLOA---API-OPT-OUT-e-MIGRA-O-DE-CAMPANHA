using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Models.Requests;
using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Models.Responses;
using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Services;
using Microsoft.AspNetCore.Mvc;

namespace DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Controller;

/// <summary>
/// Controller de Opt-In/Opt-Out.
/// Tarefa 1: gerencia preferências de comunicação do paciente no Logix.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class OptInController : ControllerBase
{
    private readonly OptInService _optInService;
    private readonly ILogger<OptInController> _logger;

    public OptInController(OptInService optInService, ILogger<OptInController> logger)
    {
        _optInService = optInService;
        _logger = logger;
    }

    /// <summary>
    /// Processa opt-out de comunicação para um paciente.
    /// </summary>
    /// <remarks>
    /// Regras de negócio aplicadas automaticamente:
    ///
    /// **channel_opt_out + channel = "whatsapp"**
    /// → whatsApp = N | email = mantém
    ///
    /// **channel_opt_out + channel = "email"**
    /// → email = N | whatsApp = mantém
    ///
    /// **global_opt_out**
    /// → todos os canais = N (whatsApp, email, sms, phone, push, mail)
    ///
    /// Para idempotência, envie o header `Idempotency-Key` ou o campo `idempotencyKey` no body.
    /// Requisições repetidas com a mesma chave retornam 200 sem reprocessar.
    /// </remarks>
    /// <param name="request">Dados do opt-out.</param>
    /// <response code="200">Opt-out processado com sucesso.</response>
    /// <response code="400">Dados inválidos ou ausentes.</response>
    /// <response code="401">API Key ausente ou inválida.</response>
    /// <response code="404">Consumidor não encontrado no Logix.</response>
    /// <response code="502">Erro de comunicação com a Interplayers.</response>
    [HttpPatch("opt-out")]
    [ProducesResponseType(typeof(GatewaySuccessResponse), 200)]
    [ProducesResponseType(typeof(GatewayErrorResponse), 400)]
    [ProducesResponseType(typeof(GatewayErrorResponse), 404)]
    [ProducesResponseType(typeof(GatewayErrorResponse), 502)]
    public async Task<IActionResult> OptOut([FromBody] OptOutRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(new GatewayErrorResponse
            {
                Error = "Dados inválidos.",
                Details = string.Join("; ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)),
                StatusCode = 400
            });

        // Idempotency-Key pode vir via header ou body
        if (Request.Headers.TryGetValue("Idempotency-Key", out var headerKey) &&
            string.IsNullOrEmpty(request.IdempotencyKey))
        {
            request.IdempotencyKey = headerKey.ToString();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _optInService.ProcessOptOutAsync(request, ip);

        if (!result.IsSuccess)
            return StatusCode(result.HttpStatusCode, new GatewayErrorResponse
            {
                Error = result.Message,
                StatusCode = result.HttpStatusCode
            });

        return Ok(new GatewaySuccessResponse
        {
            Description = result.Message,
            IdempotencyKey = result.IdempotencyKey
        });
    }
}
