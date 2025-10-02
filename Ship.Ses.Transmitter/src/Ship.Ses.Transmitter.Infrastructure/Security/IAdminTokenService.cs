using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Security
{
    public interface IAdminTokenService
    {
        Task<string> GetAccessTokenAsync(CancellationToken ct = default);
    }
}
