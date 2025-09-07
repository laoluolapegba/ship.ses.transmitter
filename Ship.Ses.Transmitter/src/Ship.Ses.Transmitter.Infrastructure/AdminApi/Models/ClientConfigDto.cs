using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.AdminApi.Models
{
    public sealed class ClientConfigDto
    {
        public required string ClientId { get; init; }
        public required string FacilityId { get; init; }
        public required string ClientName { get; init; }
        public bool IsActive { get; init; }
        public List<string> EnabledResources { get; init; } = new();
        public DateTime? LastSeen { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? UpdatedAt { get; init; }
    }
}
