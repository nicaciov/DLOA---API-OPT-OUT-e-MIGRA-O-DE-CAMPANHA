namespace DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Models;

/// <summary>
/// Registro de auditoria para fins de LGPD.
/// Toda operação de opt-out ou migração de campanha gera uma entrada.
/// </summary>
public class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>ConsumerId usado na chamada à Interplayers.</summary>
    public string ConsumerId { get; set; } = string.Empty;

    /// <summary>Identificador original recebido (pode ser phone/email antes do lookup).</summary>
    public string? OriginalIdentifier { get; set; }

    /// <summary>Tipo do identificador original: "id" | "phone" | "email".</summary>
    public string? IdentifierType { get; set; }

    /// <summary>Ação realizada: "channel_opt_out" | "global_opt_out" | "campaign_migrate".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Canal afetado (para channel_opt_out).</summary>
    public string? Channel { get; set; }

    /// <summary>Origem da requisição: "WPP" | "EMAIL" | "PDV" | "WPP_FLOW" | etc.</summary>
    public string Origin { get; set; } = string.Empty;

    /// <summary>Momento UTC da operação.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Se a operação foi bem-sucedida na Interplayers.</summary>
    public bool Success { get; set; }

    /// <summary>Código HTTP retornado pela Interplayers.</summary>
    public int? InterplayersStatusCode { get; set; }

    /// <summary>Código de erro retornado pela Interplayers (ex: LR04, MP76).</summary>
    public string? InterplayersErrorCode { get; set; }

    /// <summary>Chave de idempotência usada, se houver.</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>IP de origem da requisição ao gateway.</summary>
    public string? RequestIp { get; set; }

    /// <summary>Detalhes adicionais (serializado como JSON quando necessário).</summary>
    public string? AdditionalInfo { get; set; }
}
