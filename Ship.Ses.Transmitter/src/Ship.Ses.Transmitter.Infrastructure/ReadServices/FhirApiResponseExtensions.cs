using Ship.Ses.Transmitter.Domain.SyncModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.ReadServices
{
    public static class FhirApiResponseExtensions
    {
        //public static bool IsSuccess202(this FhirApiResponse r) =>
        //    r is not null &&
        //    string.Equals(r.Status, "success", StringComparison.OrdinalIgnoreCase) &&
        //    r.Code == 202;

        //public static bool IsBundle(this FhirApiResponse r) =>
        //    r?.Data is JsonElement e && e.ValueKind == JsonValueKind.Array;

        //public static bool TryGetBundleItems(this FhirApiResponse r, out List<PdsBundleItem> items)
        //{
        //    items = new();
        //    if (r?.Data is not JsonElement e || e.ValueKind != JsonValueKind.Array) return false;

        //    foreach (var el in e.EnumerateArray())
        //    {
        //        // tolerate missing fields gracefully
        //        string? id = null, txn = null, status = null, message = null;

        //        if (el.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
        //            id = idEl.GetString();

        //        if (el.TryGetProperty("transactionId", out var tEl) && tEl.ValueKind == JsonValueKind.String)
        //            txn = tEl.GetString();

        //        if (el.TryGetProperty("status", out var sEl) && sEl.ValueKind == JsonValueKind.String)
        //            status = sEl.GetString();

        //        if (el.TryGetProperty("message", out var mEl) && mEl.ValueKind == JsonValueKind.String)
        //            message = mEl.GetString();

        //        items.Add(new PdsBundleItem { Id = id, TransactionId = txn, Status = status, Message = message });
        //    }
        //    return true;
        //}

        ///// <summary>Prefer per-item txn if bundle; else top-level transactionId.</summary>
        //public static string? GetPreferredTransactionId(this FhirApiResponse r)
        //{
        //    if (r.TryGetBundleItems(out var items) && items.Count > 0)
        //        return items[0].TransactionId;
        //    return string.IsNullOrWhiteSpace(r?.transactionId) ? null : r!.transactionId;
        //}
    }
}
