using System.ComponentModel.DataAnnotations;

namespace DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Models.Requests;

/// <summary>
/// Representa uma requisição de opt-out enviada ao gateway.
/// </summary>
public class OptOutRequest
{
    /// <summary>
    /// ID externo do consumidor no Logix (IdCons).
    /// </summary>
    public string? ConsumerId { get; set; }

    /// <summary>
    /// Tipo de opt-out.
    /// Valores aceitos: "channel_opt_out" | "global_opt_out"
    /// </summary>
    [Required(ErrorMessage = "OptOutType é obrigatório.")]
    public string OptOutType { get; set; } = string.Empty;

    /// <summary>
    /// Canal afetado quando OptOutType = "channel_opt_out".
    /// Valores aceitos: "whatsapp" | "email" | "sms" | "phone" | "push" | "mail"
    /// </summary>
    public string? Channel { get; set; }

    /// <summary>
    /// Origem da solicitação para fins de auditoria LGPD.
    /// Valores esperados: "WPP" | "EMAIL" | "PDV" | "MANUAL"
    /// </summary>
    [Required(ErrorMessage = "Origin é obrigatório para log de auditoria.")]
    public string Origin { get; set; } = string.Empty;
}

/// <summary>
/// Requisição de migração de campanha.
/// Tarefa 2: usado quando o paciente informa a indicação no fluxo do WhatsApp.
/// </summary>
public class CampaignMigrateRequest
{
    /// <summary>ID externo do consumidor no Logix (IdCons).</summary>
    [Required(ErrorMessage = "ConsumerId é obrigatório.")]
    public string ConsumerId { get; set; } = string.Empty;

    /// <summary>EAN do produto associado à campanha atual.</summary>
    [Required(ErrorMessage = "Ean é obrigatório.")]
    public string Ean { get; set; } = string.Empty;

    /// <summary>ID da campanha de destino (indicação do paciente).</summary>
    [Required(ErrorMessage = "NewCampaignId é obrigatório.")]
    public string NewCampaignId { get; set; } = string.Empty;

    /// <summary>
    /// Texto da indicação informada pelo paciente (ex: "Indicação A").
    /// Usado apenas para auditoria e log — não vai para a Interplayers.
    /// </summary>
    public string? Indication { get; set; }

    /// <summary>Origem da solicitação. Padrão: "WPP_FLOW".</summary>
    public string Origin { get; set; } = "WPP_FLOW";

    /// <summary>Chave de idempotência (opcional via body).</summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Payload interno enviado para o endpoint /opt-ins da Interplayers.
/// Todos os campos são sempre enviados para evitar o erro LR52.
/// </summary>
public class InterplayersOptInPayload
{
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    [System.Text.Json.Serialization.JsonPropertyName("informativeMaterial")]
    public string? InformativeMaterial { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    [System.Text.Json.Serialization.JsonPropertyName("mail")]
    public string? Mail { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    [System.Text.Json.Serialization.JsonPropertyName("email")]
    public string? Email { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    [System.Text.Json.Serialization.JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    [System.Text.Json.Serialization.JsonPropertyName("sms")]
    public string? Sms { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    [System.Text.Json.Serialization.JsonPropertyName("push")]
    public string? Push { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    [System.Text.Json.Serialization.JsonPropertyName("whatsApp")]
    public string? WhatsApp { get; set; }
}

/// <summary>
/// Payload enviado para o endpoint de migração de campanha da Interplayers.
/// </summary>
public class InterplayersMigrateCampaignPayload
{
    [System.Text.Json.Serialization.JsonPropertyName("product")]
    public MigrateProductPayload Product { get; set; } = new();
}

public class MigrateProductPayload
{
    [System.Text.Json.Serialization.JsonPropertyName("ean")]
    public string Ean { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("newCampaignId")]
    public string NewCampaignId { get; set; } = string.Empty;
}