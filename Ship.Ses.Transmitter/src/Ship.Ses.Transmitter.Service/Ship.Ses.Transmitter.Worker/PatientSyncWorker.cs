using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MySqlX.XDevAPI;
using OpenTelemetry.Trace;
using Serilog.Context;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Application.Sync;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Domain.Sync;
using Ship.Ses.Transmitter.Infrastructure.ReadServices;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Worker
{
    public class PatientSyncWorker : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<PatientSyncWorker> _logger;
        //private readonly IClientSyncConfigProvider _configProvider;
        private readonly string _clientId;

        public PatientSyncWorker(
            IServiceProvider sp,
            IOptions<SeSClientOptions> clientOptions,
            ILogger<PatientSyncWorker> logger)
        {
            _sp = sp ?? throw new ArgumentNullException(nameof(sp));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clientId = clientOptions?.Value.ClientId ?? throw new ArgumentNullException(nameof(clientOptions));

        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting Patient Sync Worker...");

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("PatientSyncWorker: Beginning sync loop");

                // 
                using var scope = _sp.CreateScope();
                var configProvider = scope.ServiceProvider.GetRequiredService<IClientSyncConfigProvider>();
                var writer = scope.ServiceProvider.GetRequiredService<ISyncMetricsWriter>();
                var syncService = scope.ServiceProvider.GetRequiredService<IFhirSyncService>();

                // Check server-side activation before starting work
                if (!await configProvider.IsClientActiveAsync(_clientId))
                {
                    _logger.LogWarning("⛔ Sync client {ClientId} is not active. Stopping worker.", _clientId);
                    await writer.WriteStatusAsync(BuildStatus("Stopped", lastError: "Deactivated at server"));
                    break; // stop the worker
                }

                var enabledResources = await configProvider.GetEnabledResourcesAsync(_clientId);
                if (!enabledResources.Contains("Patient", StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("⏸️ Patient sync disabled for client {ClientId}. Stopping worker.", _clientId);
                    await writer.WriteStatusAsync(BuildStatus("Stopped", lastError: "Patient sync disabled at server"));
                    break; // stop the worker
                }

                //Mark as running
                var runningStatus = BuildStatus("Running");
                try
                {
                    await writer.WriteStatusAsync(runningStatus);
                    _logger.LogInformation("Sync status set to Running for {ClientId}", _clientId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to write Running status");
                    // non-fatal; continue
                }

                // Create a linked token + start a monitor that cancels if server deactivates mid-run
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var linkedToken = linkedCts.Token;

                var deactivatedByServer = false;
                var monitorTask = MonitorDeactivationAsync(_clientId, linkedCts, () => deactivatedByServer = true, stoppingToken);

                try
                {
                    var correlationId = Guid.NewGuid().ToString();
                    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
                    {
                        var result = await syncService.ProcessPendingRecordsAsync<PatientSyncRecord>(linkedToken);

                        _logger.LogInformation("Synced Patient records: Total={Total}, Synced={Synced}, Failed={Failed}",
                            result.Total, result.Synced, result.Failed);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Host is stopping
                    _logger.LogInformation("🛑 Host stop signal received. Stopping worker {ClientId}.", _clientId);
                    await writer.WriteStatusAsync(BuildStatus("Stopped", lastError: "Host stop signal"));
                    break;
                }
                catch (OperationCanceledException)
                {
                    // Likely canceled by server deactivation via monitor
                    var reason = deactivatedByServer ? "Deactivated at server (mid-run)" : "Operation canceled";
                    _logger.LogWarning("🛑 Cancellation during processing for {ClientId}. Reason: {Reason}", _clientId, reason);
                    await writer.WriteStatusAsync(BuildStatus("Stopped", lastError: reason));
                    break;
                }
                catch (Exception ex)
                {
                    // Unhandled failure -> mark Stopped and exit (so an orchestrator can restart)
                    _logger.LogError(ex, "❌ Unhandled exception in PatientSyncWorker");
                    await writer.WriteStatusAsync(BuildStatus("Stopped", lastError: ex.Message));
                    break;
                }
                finally
                {
                    try
                    {
                        linkedCts.Cancel(); // stop the monitor loop
                        await monitorTask;
                    }
                    catch { /* ignore */ }
                }

                // Small delay before next loop, unless stopping was requested
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            _logger.LogInformation("🛑 Patient Sync Worker stopped.");
        }
        private SyncClientStatus BuildStatus(string status, string? lastError = null)
        {
            return new SyncClientStatus
            {
                ClientId = _clientId,
                Status = status,
                LastCheckIn = DateTime.UtcNow,
                LastSyncedAt = DateTime.UtcNow,
                TotalSynced = 0,
                TotalFailed = 0,
                CurrentBatchId = "-",
                LastError = lastError,
                IpAddress = GetLocalIpAddress(),
                Hostname = Dns.GetHostName(),
                Version = "1.0.0",
                SignatureHash = Guid.NewGuid().ToString(),
                UpdatedAt = DateTime.UtcNow
            };
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

                    await Task.Delay(TimeSpan.FromSeconds(5), hostToken);
                }
            }
            catch (OperationCanceledException)
            {
                // host stopping or token cancelled; just exit
            }
            catch (Exception ex)
            {
                // Monitor failures shouldn't kill the worker; log and exit the monitor
                _logger.LogError(ex, "MonitorDeactivationAsync error; continuing without mid-run deactivation checks.");
            }
        }

        private string GetLocalIpAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return ip.ToString();
                }
            }
            catch { }
            return "Unknown";
        }
    }

}
