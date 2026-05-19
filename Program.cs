using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Infrastructure;
using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Middleware;
using DLOA___API_OPT_OUT_e_MIGRAÇÃO_DE_CAMPANHA.Services;
using Microsoft.OpenApi.Models;
using Serilog;

// ─── Bootstrap ────────────────────────────────────────────────────────────────

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .AddEnvironmentVariables()
        .Build())
    .CreateLogger();

try
{
    Log.Information("Iniciando InterplayersGateway...");

    var builder = WebApplication.CreateBuilder(args);

    // ─── Serilog ──────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, config) =>
        config.ReadFrom.Configuration(ctx.Configuration));

    // ─── Controllers + ModelState automático ──────────────────────────────────
    builder.Services.AddControllers();

    // ─── Memory Cache (token + idempotência) ──────────────────────────────────
    builder.Services.AddMemoryCache();

    // ─── HttpClients ──────────────────────────────────────────────────────────
    builder.Services.AddHttpClient("interplayers", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });

    builder.Services.AddHttpClient("interplayers_auth", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(15);
    });

    // ─── Serviços de domínio ──────────────────────────────────────────────────
    builder.Services.AddSingleton<InterplayersAuthService>();
    builder.Services.AddSingleton<IdempotencyService>();
    builder.Services.AddSingleton<AuditLogger>();
    builder.Services.AddScoped<OptInService>();
    builder.Services.AddScoped<CampaignMigrationService>();

    // ─── Swagger / OpenAPI ────────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Interplayers Gateway API",
            Version = "v1",
            Description = "Gateway para encapsulamento das APIs da plataforma Interplayers Loyalty.\n\n" +
                          "**Tarefa 1:** Opt-out de canais de comunicação (LGPD)\n\n" +
                          "**Tarefa 2:** Migração de campanha para segmentação de pacientes PDV",
            Contact = new OpenApiContact
            {
                Name = "Em caso de dúvidas, entre em contato com: vitor.nicacio@dloa.com.br",
                Email = "vitor.nicacio@dloa.com.br"
            }
        });

        // Autenticação via API Key no Swagger
        c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-Api-Key",
            Description = "API Key para acesso ao gateway."
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "ApiKey"
                    }
                },
                Array.Empty<string>()
            }
        });

        // Inclui comentários XML no Swagger
        var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
            c.IncludeXmlComments(xmlPath);
    });

    // ─── Health Check ─────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks();

    // ─── CORS (ajustar origins em produção) ───────────────────────────────────
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("GatewayPolicy", policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .WithMethods("GET", "POST", "PATCH");
        });
    });

    // ─── Build ────────────────────────────────────────────────────────────────
    var app = builder.Build();

    // ─── Pipeline ─────────────────────────────────────────────────────────────
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Interplayers Gateway v1");
        c.RoutePrefix = "swagger"; // acesso em /swagger/index.html
    });

    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
    });

    // app.UseMiddleware<ApiKeyMiddleware>();

    app.UseCors("GatewayPolicy");

    app.MapControllers();

    app.MapHealthChecks("/health");

    Log.Information("InterplayersGateway iniciado com sucesso.");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "InterplayersGateway encerrado inesperadamente.");
}
finally
{
    Log.CloseAndFlush();
}
