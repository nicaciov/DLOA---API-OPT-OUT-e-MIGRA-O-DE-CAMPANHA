using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Models.Requests;
using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Models.Responses;
using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Services;
using Microsoft.AspNetCore.Mvc;

namespace DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Controller;

/// <summary>
/// Controller de Migração de Campanhas.
/// Tarefa 2: segmentação de pacientes PDV via pesquisa no WhatsApp.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class CampaignController : ControllerBase
{
    private readonly CampaignMigrationService _migrationService;
    private readonly ILogger<CampaignController> _logger;

    public CampaignController(CampaignMigrationService migrationService, ILogger<CampaignController> logger)
    {
        _migrationService = migrationService;
        _logger = logger;
    }

    /// <summary>
    /// Migra um consumidor para uma nova campanha (segmentação por indicação).
    /// </summary>
    /// <remarks>
    /// Usado pelo fluxo do WhatsApp quando o paciente PDV informa sua indicação
    /// na pesquisa de segmentação.
    ///
    /// O bot mapeia a resposta do paciente (ex: "1", "Indicação A") para o
    /// `newCampaignId` correspondente antes de chamar este endpoint.
    ///
    /// **Exemplo de mapeamento (a definir com o time de Atendimento):**
    /// - Indicação A → newCampaignId = "8"
    /// - Indicação B → newCampaignId = "9"
    /// - Indicação C → newCampaignId = "10"
    ///
    /// Para idempotência, envie o header `Idempotency-Key`.
    /// </remarks>
    /// <param name="request">Dados da migração.</param>
    /// <response code="200">Campanha migrada com sucesso.</response>
    /// <response code="400">Dados inválidos ou campanha não localizada no Logix.</response>
    /// <response code="401">API Key ausente ou inválida.</response>
    /// <response code="404">Consumidor não encontrado no Logix.</response>
    /// <response code="502">Erro de comunicação com a Interplayers.</response>
    [HttpPatch("migrate")]
    [ProducesResponseType(typeof(GatewaySuccessResponse), 200)]
    [ProducesResponseType(typeof(GatewayErrorResponse), 400)]
    [ProducesResponseType(typeof(GatewayErrorResponse), 404)]
    [ProducesResponseType(typeof(GatewayErrorResponse), 502)]
    public async Task<IActionResult> Migrate([FromBody] CampaignMigrateRequest request)
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
        var result = await _migrationService.MigrateCampaignAsync(request, ip);

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
