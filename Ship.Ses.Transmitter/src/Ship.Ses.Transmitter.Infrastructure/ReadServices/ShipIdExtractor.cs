using System;
using System.Text.Json;

namespace Ship.Ses.Transmitter.Infrastructure.ReadServices
{
    /// <summary>
    /// Extracts the SHIP identifier from a SHIP resource/status response so the probe-driven flow can persist
    /// the same <c>shipId</c> the real SHIP callback would carry. The value is read from the FHIR resource's
    /// <c>identifier[]</c> array: the entry whose <c>type.coding[].code</c> is <c>SHIP_ID</c> supplies the
    /// <c>value</c>.
    /// <para>
    /// The response envelope is <c>{ status, code, message, data, transactionId }</c>; the resource with the
    /// identifiers lives under <c>data</c>. Parsing is defensive — any missing/oddly-shaped field yields
    /// <c>null</c> rather than throwing.
    /// </para>
    /// </summary>
    public static class ShipIdExtractor
    {
        /// <summary>The <c>type.coding[].code</c> that marks the SHIP identifier.</summary>
        public const string ShipIdCode = "SHIP_ID";

        /// <summary>Returns the SHIP identifier value from the raw response JSON, or <c>null</c> if absent.</summary>
        public static string? TryExtractShipId(string? rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                var root = doc.RootElement;

                // The resource lives under "data"; fall back to the root in case the raw body is the resource itself.
                var resource = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data)
                    ? data
                    : root;

                return FromResourceOrBundle(resource);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // "data" may be the resource object, or an array/Bundle of them — scan whichever we get.
        private static string? FromResourceOrBundle(JsonElement resource)
        {
            switch (resource.ValueKind)
            {
                case JsonValueKind.Object:
                    return FromResource(resource);
                case JsonValueKind.Array:
                    foreach (var item in resource.EnumerateArray())
                    {
                        var found = FromResource(item);
                        if (found is not null) return found;
                    }
                    return null;
                default:
                    return null;
            }
        }

        private static string? FromResource(JsonElement resource)
        {
            if (resource.ValueKind != JsonValueKind.Object ||
                !resource.TryGetProperty("identifier", out var identifiers) ||
                identifiers.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var identifier in identifiers.EnumerateArray())
            {
                if (identifier.ValueKind != JsonValueKind.Object)
                    continue;

                if (!IsShipIdType(identifier))
                    continue;

                if (identifier.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var shipId = value.GetString();
                    if (!string.IsNullOrWhiteSpace(shipId))
                        return shipId;
                }
            }

            return null;
        }

        // True if the identifier's type.coding[] contains a coding whose code is SHIP_ID.
        private static bool IsShipIdType(JsonElement identifier)
        {
            if (!identifier.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.Object ||
                !type.TryGetProperty("coding", out var codings) || codings.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var coding in codings.EnumerateArray())
            {
                if (coding.ValueKind == JsonValueKind.Object &&
                    coding.TryGetProperty("code", out var code) &&
                    code.ValueKind == JsonValueKind.String &&
                    string.Equals(code.GetString(), ShipIdCode, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
