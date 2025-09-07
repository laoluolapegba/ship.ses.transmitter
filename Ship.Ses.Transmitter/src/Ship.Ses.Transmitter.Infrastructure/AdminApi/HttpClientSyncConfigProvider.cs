using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Infrastructure.AdminApi.Models;
using Ship.Ses.Transmitter.Infrastructure.Configuration;
using Ship.Ses.Transmitter.Infrastructure.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.AdminApi
{
    public sealed class HttpClientSyncConfigProvider : IClientSyncConfigProvider
    {
        private readonly HttpClient _http;
        private readonly AdminTokenService _tokenService;
        private readonly ShipAdminApiOptions _opts;
        private readonly ILogger<HttpClientSyncConfigProvider> _log;

        public HttpClientSyncConfigProvider(
            IHttpClientFactory factory,
            AdminTokenService tokenService,
            IOptions<ShipAdminApiOptions> opts,
            ILogger<HttpClientSyncConfigProvider> log)
        {
            _http = factory.CreateClient("ShipAdminApi");
            _tokenService = tokenService;
            _opts = opts.Value;
            _log = log;
        }

        public bool _ignore; 

        public async Task<bool> IsClientActiveAsync(string clientId)
        {
            var cfg = await GetClientAsync(clientId, CancellationToken.None);
            return cfg?.IsActive ?? false;
        }

        public async Task<IEnumerable<string>> GetEnabledResourcesAsync(string clientId)
        {
            var cfg = await GetClientAsync(clientId, CancellationToken.None);
            return cfg?.EnabledResources ?? Enumerable.Empty<string>();
        }

        public async Task<bool> IsClientValidAsync(string clientId)
        {
            var cfg = await GetClientAsync(clientId, CancellationToken.None);
            return cfg is not null;
        }

        
        public Task<ClientConfigDto?> GetByFacilityAsync(string facilityId)
            => GetAsync<ClientConfigDto>($"/admin/clients/by-facility/{facilityId}", CancellationToken.None, treat404AsNull: true);

      

        private Task<ClientConfigDto?> GetClientAsync(string clientId, CancellationToken ct)
            => GetAsync<ClientConfigDto>($"/admin/clients/{clientId}", ct, treat404AsNull: true);

        private async Task<T?> GetAsync<T>(string path, CancellationToken ct, bool treat404AsNull = false)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _tokenService.GetAccessTokenAsync(ct));
            req.Headers.TryAddWithoutValidation("Traceparent", System.Diagnostics.Activity.Current?.Id);

            using var res = await _http.SendAsync(req, ct);
            if (treat404AsNull && res.StatusCode == System.Net.HttpStatusCode.NotFound) return default;

            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        }
    }
}
