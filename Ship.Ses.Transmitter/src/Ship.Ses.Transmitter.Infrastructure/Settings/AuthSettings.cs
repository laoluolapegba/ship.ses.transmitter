using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Settings
{
    public class AuthSettings
    {
        public string TokenEndpoint { get; set; } = default!;
        public string ClientId { get; set; } = default!;
        public string ClientSecret { get; set; } = default!;
        public string GrantType { get; set; } = "client_credentials";
        public string Scope { get; set; } = default!;
    }

}
