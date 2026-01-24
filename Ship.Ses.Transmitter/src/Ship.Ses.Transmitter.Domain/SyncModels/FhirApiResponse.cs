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
        [JsonConverter(typeof(FlexibleBundleConverter))]
        public List<PdsBundleItem>? Data { get; set; }
        [JsonPropertyName("transactionId")]
        public string transactionId { get; set; } = string.Empty; 

        public string? Raw { get; set; }
    }
    public sealed class PdsBundleItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
        [JsonPropertyName("transactionId")]
        public string? TransactionId { get; init; }
        [JsonPropertyName("status")]
        public string? Status { get; init; }
        [JsonPropertyName("message")]
        public string? Message { get; init; }
    }

}
