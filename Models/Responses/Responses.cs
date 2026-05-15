namespace DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Models.Responses;

/// <summary>Resposta padrão do gateway para operações bem-sucedidas.</summary>
public class GatewaySuccessResponse
{
    public string Description { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Resposta padrão de erro do gateway.</summary>
public class GatewayErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string? Details { get; set; }
    public int StatusCode { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Resposta do endpoint de token da Interplayers.</summary>
public class TokenResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("expires_on")]
    public long ExpiresOn { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("not_before")]
    public long NotBefore { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("resource")]
    public string? Resource { get; set; }
}

/// <summary>Resposta de erro da Interplayers (estrutura padrão deles).</summary>
public class InterplayersErrorResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("Data")]
    public InterplayersErrorData? Data { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("statusCode")]
    public int? StatusCode { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class InterplayersErrorData
{
    [System.Text.Json.Serialization.JsonPropertyName("error")]
    public string? Error { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("errorDescription")]
    public string? ErrorDescription { get; set; }
}
