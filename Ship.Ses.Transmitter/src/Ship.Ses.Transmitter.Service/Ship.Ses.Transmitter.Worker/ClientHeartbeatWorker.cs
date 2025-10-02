using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Infrastructure.Configuration;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Worker
{
    public sealed class ClientHeartbeatWorker : BackgroundService
    {
        private readonly IHeartbeatClient _hb;
        private readonly ILogger<ClientHeartbeatWorker> _log;
        private readonly string _clientId;
        private readonly bool _useShipAdminApi;
        private readonly ShipAdminApiOptions _apiOpts;

        public ClientHeartbeatWorker(
            IHeartbeatClient hb,
            IOptions<SeSClientOptions> clientOpts,
            IOptions<ShipAdminApiOptions> apiOpts,
            ILogger<ClientHeartbeatWorker> log)
        {
            _hb = hb;
            _log = log;
            _apiOpts = apiOpts.Value;

            var o = clientOpts.Value ?? throw new ArgumentNullException(nameof(clientOpts));
            _clientId = o.ClientId ?? throw new ArgumentNullException(nameof(o.ClientId));
            _useShipAdminApi = o.UseShipAdminApi;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_useShipAdminApi)
            {
                _log.LogInformation("ClientHeartbeatWorker disabled (UseShipAdminApi=false).");
                return;
            }

            var interval = _apiOpts.Heartbeat.Interval;
            _log.LogInformation("ClientHeartbeatWorker starting. ClientId={ClientId}, Interval={Interval}s, Path={Path}",
                _clientId, interval.TotalSeconds, _apiOpts.Heartbeat.PathTemplate);

            using var timer = new PeriodicTimer(interval);
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await _hb.SendAsync(_clientId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "❌ Heartbeat send threw an exception.");
                }
            }

            _log.LogInformation("ClientHeartbeatWorker stopped.");
        }
    }


}
