using System.Text.Json;
using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Models;

namespace DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Infrastructure;

/// <summary>
/// Serviço de log de auditoria para conformidade com LGPD.
/// Grava cada operação de opt-out e migração de campanha em arquivo dedicado
/// com rotação diária, separado dos logs de aplicação.
/// </summary>
public class AuditLogger
{
    private readonly ILogger<AuditLogger> _logger;
    private readonly IConfiguration _config;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AuditLogger(ILogger<AuditLogger> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Persiste uma entrada de auditoria.
    /// Grava em arquivo JSONL (uma linha por evento) com rotação diária.
    /// Erros de gravação são logados mas não propagados — auditoria nunca deve derrubar a operação.
    /// </summary>
    public async Task LogAsync(AuditEntry entry)
    {
        try
        {
            var line = JsonSerializer.Serialize(entry, _jsonOptions);

            // Log estruturado no Serilog (vai para o output padrão também)
            _logger.LogInformation("[AUDIT] Action={Action} Consumer={ConsumerId} Origin={Origin} " +
                                   "Success={Success} StatusCode={StatusCode} IdempotencyKey={IdempotencyKey}",
                entry.Action, entry.ConsumerId, entry.Origin,
                entry.Success, entry.InterplayersStatusCode, entry.IdempotencyKey);

            // Grava no arquivo de auditoria dedicado
            var logPath = BuildAuditFilePath();
            EnsureDirectoryExists(logPath);

            await _fileLock.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(logPath, line + Environment.NewLine);
            }
            finally
            {
                _fileLock.Release();
            }
        }
        catch (Exception ex)
        {
            // Auditoria nunca derruba a operação
            _logger.LogError(ex, "Falha ao gravar log de auditoria para Consumer={ConsumerId}.", entry.ConsumerId);
        }
    }

    private string BuildAuditFilePath()
    {
        var basePath = _config["Audit:LogPath"] ?? "logs/audit-.log";
        // Substitui o marcador de rotação pelo timestamp do dia
        return basePath.Replace("-.", $"-{DateTime.UtcNow:yyyyMMdd}.");
    }

    private static void EnsureDirectoryExists(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}
