using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Domain.SyncModels
{
    public class FhirApiResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;  // e.g. "success", "error"

        [JsonPropertyName("code")]
        public int Code { get; set; }  // e.g. 200, 400

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public JsonElement? Data { get; set; }  // Optional payload returned by API
        [JsonPropertyName("transactionId")]
        public string transactionId { get; set; } = string.Empty; 

        public string? Raw { get; set; }
    }


}
