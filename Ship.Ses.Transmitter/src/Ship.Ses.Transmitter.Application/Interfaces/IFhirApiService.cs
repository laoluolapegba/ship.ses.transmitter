using Ship.Ses.Transmitter.Domain.Enums;
using Ship.Ses.Transmitter.Domain.SyncModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Application.Sync
{
    public interface IFhirApiService
    {
        public Task<FhirApiResponse> SendAsync(
            FhirOperation operation,
            string resourceType,
            string resourceId = null,
            string jsonPayload = null,
            string? callbackUrl = null,
            string? shipService = null,
            CancellationToken cancellationToken = default);
    }
}
