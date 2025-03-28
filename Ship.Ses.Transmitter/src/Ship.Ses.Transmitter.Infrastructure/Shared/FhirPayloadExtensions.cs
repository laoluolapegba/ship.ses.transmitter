using MongoDB.Bson.IO;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Shared
{
    public static class FhirPayloadExtensions
    {
        public static string ToCleanJson(this BsonDocument document)
        {
            return document.ToJson(new JsonWriterSettings
            {
                OutputMode = JsonOutputMode.CanonicalExtendedJson // Use Canonical or Relaxed if needed
            });
        }
    }
}
