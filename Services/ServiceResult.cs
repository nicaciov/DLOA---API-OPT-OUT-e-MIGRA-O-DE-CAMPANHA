namespace DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Services;

/// <summary>
/// Resultado padronizado retornado pelos serviços.
/// Evita exceções para fluxo normal de erros de negócio.
/// </summary>
public class ServiceResult
{
    public bool IsSuccess { get; private set; }
    public bool IsIdempotent { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public int HttpStatusCode { get; private set; }
    public string? IdempotencyKey { get; private set; }

    private ServiceResult() { }

    public static ServiceResult Ok(string message, string? idempotencyKey = null) =>
        new()
        {
            IsSuccess = true,
            Message = message,
            HttpStatusCode = 200,
            IdempotencyKey = idempotencyKey
        };

    public static ServiceResult Failure(string message, int httpStatus = 500) =>
        new()
        {
            IsSuccess = false,
            Message = message,
            HttpStatusCode = httpStatus
        };

    public static ServiceResult Idempotent(string idempotencyKey) =>
        new()
        {
            IsSuccess = true,
            IsIdempotent = true,
            Message = "Requisição já processada anteriormente (idempotente).",
            HttpStatusCode = 200,
            IdempotencyKey = idempotencyKey
        };
}
