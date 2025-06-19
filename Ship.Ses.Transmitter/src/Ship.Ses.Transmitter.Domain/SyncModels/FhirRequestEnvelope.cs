using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Domain.SyncModels
{
    public class FhirRequestEnvelope
    {
        [JsonPropertyName("callbackUrl")]
        public string? CallbackUrl { get; set; }

        [JsonPropertyName("data")]
        public JsonElement Data { get; set; }
    }

}
