using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Application.Sync;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Domain.Sync;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using Serilog.Context;

namespace Ship.Ses.Transmitter.Worker
{


    public sealed class ResourcesFhirSyncWorker : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<ResourcesFhirSyncWorker> _logger;
        private readonly IClientSyncConfigProvider _config;
        private readonly ISyncMetricsWriter _writer;
        private readonly string _clientId;

        // Tunables (bind from config if you like)
        private readonly TimeSpan _loopDelay = TimeSpan.FromMinutes(1);
        private readonly TimeSpan _errorBackoff = TimeSpan.FromSeconds(30);
        private readonly TimeSpan _pollUntilEnabled = TimeSpan.FromSeconds(10);
        private readonly int _maxDop = 4; // parallel resources per loop

        // Resource registry: "Patient" -> typeof(PatientSyncRecord)
        private readonly Dictionary<string, Type> _resourceMap;

        public ResourcesFhirSyncWorker(
            IServiceProvider sp,
            IOptions<SeSClientOptions> opts,
            ILogger<ResourcesFhirSyncWorker> logger,
            IClientSyncConfigProvider config,
            ISyncMetricsWriter writer)
        {
            _sp = sp;
            _logger = logger;
            _config = config;
            _writer = writer;

            var o = opts?.Value ?? throw new ArgumentNullException(nameof(opts));
            _clientId = o.ClientId ?? throw new ArgumentNullException(nameof(o.ClientId));

            _resourceMap = DiscoverFhirRecordTypes();
            _logger.LogInformation("Discovered {Count} FHIR record types: {Resources}",
                _resourceMap.Count, string.Join(", ", _resourceMap.Keys.OrderBy(x => x)));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting Generic Resources FHIR Sync Worker…");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        if (!await _config.IsClientActiveAsync(_clientId))
                        {
                            _logger.LogWarning("⛔ Client {ClientId} not active. Waiting…", _clientId);
                            await _writer.WriteStatusAsync(BuildStatus("Stopped", "Deactivated at server"));
                            await WaitUntilClientActiveAsync(stoppingToken);
                            continue;
                        }

                        // Server-enabled resources for this client
                        var enabled = await _config.GetEnabledResourcesAsync(_clientId);

                        // Intersect with types we actually have
                        var runnable = enabled
                            .Where(r => _resourceMap.ContainsKey(r))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        if (runnable.Count == 0)
                        {
                            _logger.LogInformation("⏸️ No enabled resources with matching types. Polling…");
                            await _writer.WriteStatusAsync(BuildStatus("Stopped", "No enabled resources"));
                            await Task.Delay(_pollUntilEnabled, stoppingToken);
                            continue;
                        }

                        await _writer.WriteStatusAsync(BuildStatus("Running"));
                        _logger.LogInformation("Running sync for: {Resources}", string.Join(", ", runnable));

                        // Run per resource with bounded parallelism
                        var throttler = new SemaphoreSlim(_maxDop);
                        var tasks = new List<Task>(runnable.Count);

                        foreach (var res in runnable)
                        {
                            await throttler.WaitAsync(stoppingToken);
                            tasks.Add(Task.Run(async () =>
                            {
                                try { await ProcessOneResourceAsync(res, stoppingToken); }
                                finally { throttler.Release(); }
                            }, stoppingToken));
                        }

                        await Task.WhenAll(tasks);

                        await Task.Delay(_loopDelay, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("🛑 Host stop signal received. Stopping worker for {ClientId}.", _clientId);
                        await _writer.WriteStatusAsync(BuildStatus("Stopped", "Host stop signal"));
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Unhandled exception in AllResourcesFhirSyncWorker. Backing off…");
                        await _writer.WriteStatusAsync(BuildStatus("Error", ex.Message));
                        await Task.Delay(_errorBackoff, stoppingToken);
                    }
                }
            }
            finally
            {
                _logger.LogInformation("🛑 Generic FHIR Sync Worker stopped.");
            }
        }

        private async Task ProcessOneResourceAsync(string resourceName, CancellationToken token)
        {
            if (!await _config.IsClientActiveAsync(_clientId))
            {
                _logger.LogWarning("⛔ Client {ClientId} deactivated before {Resource} run. Skipping…",
                    _clientId, resourceName);
                return;
            }

            if (!_resourceMap.TryGetValue(resourceName, out var tRecord))
            {
                _logger.LogWarning("⚠️ No record type found for {Resource}. Skipping…", resourceName);
                return;
            }

            using var scope = _sp.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IFhirSyncService>();

            var correlationId = Guid.NewGuid().ToString();
            using (LogContext.PushProperty("CorrelationId", correlationId))
            using (LogContext.PushProperty("ResourceType", resourceName))
            {
                _logger.LogInformation("🔎 Fetching pending {Resource} records…", resourceName);

                var result = await CallGenericProcessAsync(svc, tRecord, resourceName, token);

                _logger.LogInformation("✅ {Resource} synced: Total={Total}, Synced={Synced}, Failed={Failed}",
                    resourceName, result.Total, result.Synced, result.Failed);
            }
        }

        // Bridge to IFhirSyncService.ProcessPendingRecordsAsync<T>(token)
        private static Task<SyncResultDto> CallGenericProcessAsync(
            IFhirSyncService svc, Type recordType, string resourceName, CancellationToken token)
        {
            var mi = typeof(IFhirSyncService).GetMethod(nameof(IFhirSyncService.ProcessPendingRecordsAsync))!;
            var closed = mi.MakeGenericMethod(recordType);
            var taskObj = closed.Invoke(svc, new object?[] { token, resourceName })!;
            return (Task<SyncResultDto>)taskObj;
        }

        private async Task WaitUntilClientActiveAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (await _config.IsClientActiveAsync(_clientId))
                    return;
                await Task.Delay(_pollUntilEnabled, token);
            }
        }

        private static Dictionary<string, Type> DiscoverFhirRecordTypes()
        {
            var baseType = typeof(FhirSyncRecord);
            var dict = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

            var types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => SafeGetTypes(a))
                .Where(t => !t.IsAbstract
                            && baseType.IsAssignableFrom(t)
                            && t.GetConstructor(Type.EmptyTypes) is not null); // satisfies new()

            foreach (var t in types)
            {
                // Prefer attribute if present
                var attrs = t.GetCustomAttributes<FhirResourceAttribute>(false);
                if (attrs?.Any() == true)
                {
                    foreach (var attr in attrs)
                    {
                        if (string.IsNullOrWhiteSpace(attr.ResourceName))
                            continue;
                        dict[attr.ResourceName] = t;
                    }
                    continue;
                }

                // Fall back to {Resource}SyncRecord → {Resource}
                var n = t.Name;
                var resource = n.EndsWith("SyncRecord", StringComparison.OrdinalIgnoreCase)
                    ? n[..^"SyncRecord".Length]
                    : n;
                dict[resource] = t;
            }

            return dict;

            static IEnumerable<Type> SafeGetTypes(Assembly a)
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            }
        }

        private SyncClientStatus BuildStatus(string status, string? lastError = null)
        {
            var batchId = status == "Running"
                ? $"batch-all-{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}"
                : "-";

            var dataToHash = $"{_clientId}-ALL-{DateTime.UtcNow:O}-{status}";
            using var sha256 = SHA256.Create();
            var sig = Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(dataToHash))).ToLowerInvariant();

            return new SyncClientStatus
            {
                ClientId = _clientId,
                Status = status,
                LastCheckIn = DateTime.UtcNow,
                LastSyncedAt = DateTime.UtcNow,
                TotalSynced = 0,
                TotalFailed = 0,
                CurrentBatchId = batchId,
                LastError = lastError,
                IpAddress = GetLocalIpAddress(),
                Hostname = Dns.GetHostName(),
                Version = "1.0.0",
                SignatureHash = sig,
                UpdatedAt = DateTime.UtcNow
            };
        }

        private static string GetLocalIpAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return ip.ToString();
            }
            catch { }
            return "Unknown";
        }
    }


}
