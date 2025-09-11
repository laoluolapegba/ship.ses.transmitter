//using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Serilog;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Application.Sync;
using Ship.Ses.Transmitter.Infrastructure.AdminApi;
using Ship.Ses.Transmitter.Infrastructure.Configuration;
using Ship.Ses.Transmitter.Infrastructure.Http;
using Ship.Ses.Transmitter.Infrastructure.Installers;
using Ship.Ses.Transmitter.Infrastructure.Persistance.Configuration.Domain.Sync;
using Ship.Ses.Transmitter.Infrastructure.Persistance.MySql;
using Ship.Ses.Transmitter.Infrastructure.Persistance.Sync;
using Ship.Ses.Transmitter.Infrastructure.Security;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using Ship.Ses.Transmitter.Worker;
using static Org.BouncyCastle.Math.EC.ECCurve;

var builder = Host.CreateApplicationBuilder(args);
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

builder.Logging.ClearProviders();
//  Configure Serilog with ElasticSearch & CorrelationId
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithCorrelationId()    
    .CreateLogger();
builder.Logging.AddSerilog();


builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

builder.Services
    .AddOptions<FhirApiSettings>()
    .Bind(builder.Configuration.GetSection("FhirApi"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "FhirApi:BaseUrl is required")
    .Validate(o => o.TimeoutSeconds > 0, "FhirApi:TimeoutSeconds must be > 0")
    .ValidateOnStart();

builder.Services.AddHttpClient("EmrCallback")
    .ConfigureHttpClient(c =>
    {
        c.Timeout = TimeSpan.FromSeconds(15);
    });


builder.Services
    .AddOptions<AppSettings>()
    .Bind(builder.Configuration.GetSection("AppSettings"))
    .ValidateDataAnnotations()
    .Validate(o =>
        o.ShipServerSqlDb is not null &&
        !string.IsNullOrWhiteSpace(o.ShipServerSqlDb.DbType) &&
        o.EmrDb is not null &&
        !string.IsNullOrWhiteSpace(o.EmrDb.DbType),
        "Both ShipServerSqlDb and EmrDb must be configured")
    .ValidateOnStart();

builder.Services.Configure<SeSClientOptions>(builder.Configuration.GetSection("SeSClient"));
builder.Services.Configure<ShipAdminApiOptions>(builder.Configuration.GetSection("ShipAdminApi"));
builder.Services.Configure<ShipAdminAuthOptions>(builder.Configuration.GetSection("ShipAdminAuth"));

var seSClientOpts = builder.Configuration.GetSection("SeSClient").Get<SeSClientOptions>() ?? new SeSClientOptions();
if (seSClientOpts.UseShipAdminApi)
{

    // ---- Admin API path ----
    builder.Services.AddHttpClient("ShipAdminApi", (sp, client) =>
    {
        var o = sp.GetRequiredService<IOptions<ShipAdminApiOptions>>().Value;
        client.BaseAddress = new Uri(o.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(o.RequestTimeoutSeconds);
    })
    .AddPolicyHandler((IServiceProvider sp, HttpRequestMessage _req) =>
    {
        var o = sp.GetRequiredService<IOptions<ShipAdminApiOptions>>().Value;
        return PollyPolicies.CreateStandardRetry(o);
    });

    builder.Services.AddSingleton<IClientSyncConfigProvider, HttpClientSyncConfigProvider>();
    builder.Services.AddSingleton<ISyncMetricsWriter, HttpSyncMetricsWriter>();
    builder.Services.AddSingleton<IHeartbeatClient, HttpHeartbeatClient>();
    

    // IMPORTANT: Do NOT register ShipServerDbContext here if it was only used for admin reads/writes
    // (Keep any other DB contexts that are used elsewhere)
    Log.Information("FeatureFlag: Using SHIP Admin API adapters (HTTP).");
}
else
{
    // Register DbContext
   
    builder.Services.AddDbContext<ShipServerDbContext>(opts =>
    {
        var app = builder.Configuration.GetSection("AppSettings").Get<AppSettings>()!;
        UseProviderWithSchema(opts, app.ShipServerSqlDb);
    });


    // Register  EF-based adapters
    builder.Services.AddScoped<IClientSyncConfigProvider, EfClientSyncConfigProvider>();
    builder.Services.AddScoped<ISyncMetricsWriter, MySqlSyncMetricsWriter>();

    Log.Information("FeatureFlag: Using direct DB adapters (EF/MySQL).");
}

static void UseProviderWithSchema(DbContextOptionsBuilder opts, DatabaseSettings db)
{
    var kind = db.DbType.Trim().ToLowerInvariant();
    var schema = string.IsNullOrWhiteSpace(db.Schema) ? null : db.Schema;

    switch (kind)
    {
        case "postgres":
        case "postgresql":
            var cs = schema is null ? db.ConnectionString : $"{db.ConnectionString};Search Path={schema}";
            opts.UseNpgsql(cs);
            break;

        case "sqlserver":
            opts.UseSqlServer(db.ConnectionString);
            break;

        case "mysql":
            opts.UseMySQL(db.ConnectionString);
            break;

        default:
            throw new InvalidOperationException($"Unsupported DbType: {db.DbType}");
    }
}

builder.Services.AddPooledDbContextFactory<ExtractorStagingDbContext>(opts =>
{
    var app = builder.Configuration.GetSection("AppSettings").Get<AppSettings>()!;
    UseProviderWithSchema(opts, app.EmrDb);
});

//Register this guy to report/ update the emr staging db 
builder.Services.AddScoped<IStagingUpdateWriter, StagingUpdateWriter>();

builder.Services.Configure<AppSettings>(builder.Configuration.GetSection(nameof(AppSettings)));

builder.Services.AddAppServices(builder.Configuration);

builder.Services
    .AddFhirApiClient(builder.Configuration)
    .AddSyncMetrics(builder.Configuration);

//  Register Background Workers for Each FHIR Resource Type

builder.Services.AddHostedService<PatientSyncWorker>();
builder.Services.AddHostedService<MetricsSyncReporterWorker>();
//builder.Services.AddHostedService<EncounterSyncWorker>();
builder.Services.AddHostedService<EmrCallbackWorker>();
builder.Services.AddHostedService<ClientHeartbeatWorker>();

var test = builder.Services.BuildServiceProvider().GetService<ISyncMetricsCollector>();
Console.WriteLine(test == null
    ? "❌ ISyncMetricsCollector not registered"
    : "✅ ISyncMetricsCollector is registered");

var app = builder.Build();


app.Run();

