using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Worker
{
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Serilog.Context;
    using Ship.Ses.Transmitter.Application.Interfaces;
    using Ship.Ses.Transmitter.Application.Sync;
    using Ship.Ses.Transmitter.Domain.Patients;
    using Ship.Ses.Transmitter.Domain.Queue;
    using Ship.Ses.Transmitter.Domain.Sync;
    using Ship.Ses.Transmitter.Infrastructure.Settings;
    using System.Net;
    using System.Security.Cryptography;
    using System.Text;

    public sealed class ResourcesFhirSyncWorker : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<ResourcesFhirSyncWorker> _logger;
        private readonly IClientSyncConfigProvider _config;
        private readonly ISyncMetricsWriter _writer;
        private readonly string _clientId;

        private readonly TimeSpan _loopDelay = TimeSpan.FromMinutes(1);
        private readonly TimeSpan _errorBackoff = TimeSpan.FromSeconds(30);
        private readonly TimeSpan _pollUntilEnabled = TimeSpan.FromSeconds(10);
        private readonly TimeSpan _midRunPoll = TimeSpan.FromSeconds(5);

        public ResourcesFhirSyncWorker(
            IServiceProvider sp,
            IOptions<SeSClientOptions> clientOptions,
            ILogger<ResourcesFhirSyncWorker> logger,
            IClientSyncConfigProvider config,
            ISyncMetricsWriter writer)
        {
            _sp = sp ?? throw new ArgumentNullException(nameof(sp));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));

            var opts = clientOptions?.Value ?? throw new ArgumentNullException(nameof(clientOptions));
            _clientId = opts.ClientId ?? throw new ArgumentNullException(nameof(opts.ClientId));

            _logger.LogInformation("ResourcesFhirSyncWorker starting with mode: {Mode}",
                opts.UseShipAdminApi ? "AdminAPI" : "DirectDB");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("▶️ Starting Resources FHIR Sync Worker (client={ClientId})…", _clientId);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("🔁 Sync loop begin (client={ClientId})", _clientId);

                    using var scope = _sp.CreateScope();
                    var syncService = scope.ServiceProvider.GetRequiredService<IFhirSyncService>();

                    // 1️⃣ Self-disable if client not active
                    if (!await _config.IsClientActiveAsync(_clientId))
                    {
                        _logger.LogWarning("⛔ Client {ClientId} not active. Pausing & polling…", _clientId);
                        await SafeWriteStatusAsync(BuildStatus("Stopped", "Deactivated at server"));
                        await WaitUntilClientActiveAsync(stoppingToken);
                        continue;
                    }

                    // 2️⃣ Mark running
                    await SafeWriteStatusAsync(BuildStatus("Running"));
                    _logger.LogInformation("🟢 Sync status=Running (client={ClientId})", _clientId);

                    // 3️⃣ Mid-run deactivation monitor (linked CTS)
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    var linkedToken = linkedCts.Token;
                    var deactivatedByServer = false;
                    var monitorTask = MonitorDeactivationAsync(_clientId, linkedCts, () => deactivatedByServer = true, stoppingToken);

                    try
                    {
                        // 4️⃣ Process both known pools
                        await ProcessPoolAsync<PatientSyncRecord>(syncService, linkedToken);
                        await ProcessPoolAsync<GenericResourceSyncRecord>(syncService, linkedToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("🛑 Host stop signal; stopping worker (client={ClientId}).", _clientId);
                        await SafeWriteStatusAsync(BuildStatus("Stopped", "Host stop signal"));
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        var reason = deactivatedByServer ? "Deactivated at server (mid-run)" : "Operation canceled";
                        _logger.LogWarning("🛑 Cancellation during processing (client={ClientId}). {Reason}", _clientId, reason);
                        await SafeWriteStatusAsync(BuildStatus("Stopped", reason));

                        await WaitUntilClientActiveAsync(stoppingToken);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Unhandled exception in ResourcesFhirSyncWorker. Backing off…");
                        await SafeWriteStatusAsync(BuildStatus("Error", ex.Message));
                        await Task.Delay(_errorBackoff, stoppingToken);
                        continue;
                    }
                    finally
                    {
                        try { linkedCts.Cancel(); await monitorTask; } catch { /* ignore */ }
                    }

                    // 5️⃣ Sleep between loops
                    _logger.LogInformation("⏲️ Loop complete. Sleeping {Seconds}s…", (int)_loopDelay.TotalSeconds);
                    await Task.Delay(_loopDelay, stoppingToken);
                }
            }
            finally
            {
                _logger.LogInformation("⏹️ Resources FHIR Sync Worker stopped (client={ClientId}).", _clientId);
            }
        }

        private async Task ProcessPoolAsync<T>(IFhirSyncService svc, CancellationToken token)
            where T : FhirSyncRecord, new()
        {
            var recordType = typeof(T).Name;
            var collection = new T().CollectionName;

            // Re-check active before each pool
            if (!await _config.IsClientActiveAsync(_clientId))
            {
                _logger.LogWarning("⛔ Client {ClientId} deactivated before {RecordType}. Skipping…",
                    _clientId, recordType);
                return;
            }

            var correlationId = Guid.NewGuid().ToString();
            using (LogContext.PushProperty("CorrelationId", correlationId))
            using (LogContext.PushProperty("RecordType", recordType))
            {
                _logger.LogInformation("🔎 Processing pending {RecordType} records from {Collection}…",
                    recordType, collection);

                var result = await svc.ProcessPendingRecordsAsync<T>(token);

                _logger.LogInformation("✅ {RecordType} processed from {Collection}: Total={Total}, Synced={Synced}, Failed={Failed}",
                    recordType, collection, result.Total, result.Synced, result.Failed);
            }
        }

        private async Task WaitUntilClientActiveAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                bool active;
                try
                {
                    using var scope = _sp.CreateScope();
                    var cfg = scope.ServiceProvider.GetRequiredService<IClientSyncConfigProvider>();
                    active = await cfg.IsClientActiveAsync(_clientId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "⚠️ Error checking client active status; will retry.");
                    active = false;
                }

                if (active)
                {
                    _logger.LogInformation("✅ Client {ClientId} is active again. Resuming…", _clientId);
                    return;
                }

                _logger.LogInformation("⏸️ Still inactive; retrying in {Seconds}s…", (int)_pollUntilEnabled.TotalSeconds);
                await Task.Delay(_pollUntilEnabled, stoppingToken);
            }
        }

        private async Task MonitorDeactivationAsync(
            string clientId,
            CancellationTokenSource toCancel,
            Action markDeactivated,
            CancellationToken hostToken)
        {
            try
            {
                while (!hostToken.IsCancellationRequested && !toCancel.IsCancellationRequested)
                {
                    using var scope = _sp.CreateScope();
                    var cfg = scope.ServiceProvider.GetRequiredService<IClientSyncConfigProvider>();
                    var active = await cfg.IsClientActiveAsync(clientId);

                    if (!active)
                    {
                        _logger.LogWarning("⛔ Server deactivated client {ClientId} during run. Cancelling work…", clientId);
                        markDeactivated();
                        toCancel.Cancel();
                        return;
                    }

                    await Task.Delay(_midRunPoll, hostToken);
                }
            }
            catch (OperationCanceledException)
            {
                // normal on shutdown/cancel
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "⚠️ MonitorDeactivationAsync error; continuing without mid-run checks.");
            }
        }

        private async Task SafeWriteStatusAsync(SyncClientStatus status)
        {
            try { await _writer.WriteStatusAsync(status); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to write sync status {Status}.", status.Status);
            }
        }

        private SyncClientStatus BuildStatus(string status, string? lastError = null)
        {
            _logger.LogDebug("Building client status for {ClientId} = {Status}…", _clientId, status);

            var batchId = status == "Running"
                ? $"batch-{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}"
                : "-";

            var dataToHash = $"{_clientId}-{DateTime.UtcNow:O}-{status}";
            using var sha256 = SHA256.Create();
            var signatureHash = Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(dataToHash))).ToLowerInvariant();

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
                SignatureHash = signatureHash,
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
