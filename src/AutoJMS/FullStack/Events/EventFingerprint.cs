using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AutoJMS.FullStack.Events
{
    /// <summary>
    /// Deterministic SHA256 fingerprint over the semantic identity of an event.
    /// Two machines observing the same underlying JMS state MUST produce the same
    /// fingerprint, so observer metadata (client id, observed_at, event id) is
    /// intentionally excluded from the hash.
    /// </summary>
    public static class EventFingerprint
    {
        /// <param name="waybillNo">Normalized waybill (upper, trimmed).</param>
        /// <param name="eventType">One of FullStackEventTypes.</param>
        /// <param name="eventTime">The observed event time (UTC).</param>
        /// <param name="semanticPayload">
        /// Canonical, order-stable representation of the meaningful payload
        /// (e.g. "action=Quét phát|status=...|site=..."). Must NOT include
        /// observer metadata or volatile fields like raw JSON timestamps.
        /// </param>
        public static string Compute(string waybillNo, string eventType, DateTime eventTime, string semanticPayload)
        {
            string canonical = string.Join("|", new[]
            {
                (waybillNo ?? "").Trim().ToUpperInvariant(),
                (eventType ?? "").Trim(),
                eventTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                (semanticPayload ?? "").Trim()
            });

            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
