using System;
using System.Collections.Generic;

namespace Ship.Ses.Transmitter.Domain.Sync
{
    /// <summary>
    /// The canonical <see cref="StatusEvent.Status"/> vocabulary. SHIP/MPI reports a <b>final outcome</b> as
    /// one of the terminal statuses below; the EMR should receive that outcome regardless of whether it was a
    /// success. <see cref="Pending"/> is a transmitter-internal placeholder (seeded on send while awaiting the
    /// callback) — it is <b>not</b> an MPI outcome and is never delivered to the EMR.
    /// </summary>
    public static class ShipCallbackStatus
    {
        /// <summary>Awaiting a SHIP callback — non-terminal, not delivered to the EMR.</summary>
        public const string Pending = "PENDING";

        // ── MPI-defined terminal outcomes ──
        public const string Success = "SUCCESS";
        public const string Error = "ERROR";
        public const string Rejected = "REJECTED";
        public const string Conflict = "CONFLICT";
        public const string Duplicate = "DUPLICATE";

        /// <summary>
        /// The MPI-defined terminal outcomes. Any status in this set is a final result and is eligible for
        /// EMR callback delivery. (Ordinal, case-sensitive — statuses are written in this exact casing.)
        /// </summary>
        public static readonly IReadOnlyCollection<string> Terminal = new[]
        {
            Success, Error, Rejected, Conflict, Duplicate
        };

        private static readonly HashSet<string> TerminalSet = new(Terminal, StringComparer.Ordinal);

        /// <summary>True if <paramref name="status"/> is a terminal MPI outcome (eligible for EMR callback).</summary>
        public static bool IsTerminal(string? status) =>
            !string.IsNullOrEmpty(status) && TerminalSet.Contains(status);
    }
}
