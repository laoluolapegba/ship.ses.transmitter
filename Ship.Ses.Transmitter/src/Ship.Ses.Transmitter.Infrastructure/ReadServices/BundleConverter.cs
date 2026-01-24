using Ship.Ses.Transmitter.Domain.SyncModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.ReadServices
{
    public class FlexibleBundleConverter : JsonConverter<List<PdsBundleItem>?>
    {
        public override List<PdsBundleItem>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                try
                {
                    var list = JsonSerializer.Deserialize<List<PdsBundleItem>>(ref reader, options);
                    return list;
                }
                catch
                {
                    return null;
                }
            }

            // Fallback: skip unexpected value (e.g., a single object)
            using var doc = JsonDocument.ParseValue(ref reader);
            return null;
        }

        public override void Write(Utf8JsonWriter writer, List<PdsBundleItem>? value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}
