using Ship.Ses.Transmitter.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Domain.Sync
{
    public interface IFhirApiService
    {
        Task<HttpResponseMessage> SendAsync(
            FhirOperation operation,
            string resourceType,
            string resourceId = null,
            string jsonPayload = null,
            CancellationToken cancellationToken = default);
    }
}
