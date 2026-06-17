#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.Diagnostics.AppCapture
{
    public sealed class AppCaptureReportGenerator
    {
        public async Task GenerateAsync(AppCaptureManager manager, CancellationToken cancellationToken)
        {
            var session = manager.CurrentSession;
            if (session == null) return;

            Directory.CreateDirectory(session.ReportsDirectory);
            await WriteSessionSummaryAsync(session, cancellationToken).ConfigureAwait(false);
            await WriteSlowApiReportAsync(session, manager.Options.SlowApiThresholdMs, cancellationToken).ConfigureAwait(false);
            await WriteAgentAnalysisAsync(session, cancellationToken).ConfigureAwait(false);
        }

        private static async Task WriteSessionSummaryAsync(AppCaptureSession session, CancellationToken ct)
        {
            string content =
$@"# AppCapture Session Summary

Session: `{session.SessionId}`
Started: `{session.StartedAt:yyyy-MM-dd HH:mm:ss zzz}`
Path: `{session.RootDirectory}`

Primary files:

- `timeline.ndjson`
- `user-actions.ndjson`
- `app-http.ndjson`
- `webview-network.ndjson`
- `webview-console.ndjson`
- `webview-route.ndjson`
- `performance.ndjson`
- `errors.ndjson`
- `api-bodies/`
- `dom/`
";
            await File.WriteAllTextAsync(Path.Combine(session.ReportsDirectory, "session-summary.md"), content, Encoding.UTF8, ct).ConfigureAwait(false);
        }

        private static async Task WriteSlowApiReportAsync(AppCaptureSession session, int thresholdMs, CancellationToken ct)
        {
            string performancePath = Path.Combine(session.RootDirectory, "performance.ndjson");
            var slowLines = File.Exists(performancePath)
                ? File.ReadLines(performancePath)
                    .Where(line => line.Contains("\"isSlow\":true", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("\"isSlow\": true", StringComparison.OrdinalIgnoreCase))
                    .Take(100)
                    .ToList()
                : new List<string>();

            var sb = new StringBuilder();
            sb.AppendLine("# Slow API / Performance Report");
            sb.AppendLine();
            sb.AppendLine($"Threshold: `{thresholdMs} ms`");
            sb.AppendLine();
            if (slowLines.Count == 0)
            {
                sb.AppendLine("No slow performance events captured yet.");
            }
            else
            {
                sb.AppendLine("Slow events:");
                sb.AppendLine();
                foreach (var line in slowLines)
                    sb.AppendLine("- `" + Truncate(line, 500) + "`");
            }

            await File.WriteAllTextAsync(Path.Combine(session.ReportsDirectory, "slow-api-report.md"), sb.ToString(), Encoding.UTF8, ct).ConfigureAwait(false);
        }

        private static async Task WriteAgentAnalysisAsync(AppCaptureSession session, CancellationToken ct)
        {
            string content =
$@"# Agent Analysis

Use this report with the NDJSON files in the parent folder.

Questions this capture is designed to answer:

- User did what, when, on which tab/control?
- Which WebView route was active?
- Which WebView network calls happened?
- Which app `HttpClient` API calls happened?
- Which request/response bodies were saved under `api-bodies/`?
- Which API or UI phase was slow?
- Which console errors happened?
- Which DOM snapshots are available under `dom/`?

Recommended reading order:

1. `timeline.ndjson`
2. `errors.ndjson`
3. `performance.ndjson`
4. `app-http.ndjson`
5. `webview-network.ndjson`
6. `webview-route.ndjson`
7. `webview-console.ndjson`
8. `dom/`
";
            await File.WriteAllTextAsync(Path.Combine(session.ReportsDirectory, "agent-analysis.md"), content, Encoding.UTF8, ct).ConfigureAwait(false);
        }

        private static string Truncate(string value, int max)
            => string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max) + "...";
    }
}
