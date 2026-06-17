#nullable enable
using AutoJMS.Automation.DevTools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.Diagnostics
{
    public sealed class WebDebugExportService
    {
        private static readonly JsonSerializerOptions JsonOptions = AppConfig.CreateJsonOptions();

        public async Task<WebDebugExportResult> ExportAsync(WebViewDevToolsInspector inspector, CancellationToken token)
        {
            if (inspector == null) throw new ArgumentNullException(nameof(inspector));
            token.ThrowIfCancellationRequested();

            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string directory = Path.Combine(
                AppPaths.UserDataDir,
                "debug",
                "webview-captures",
                $"{timestamp}-{SanitizePathPart(inspector.SurfaceName)}");

            Directory.CreateDirectory(directory);

            var result = new WebDebugExportResult { DirectoryPath = directory };
            var domSnapshot = await inspector.CaptureDomSnapshotAsync(token);
            var selectorCandidates = inspector.DiscoverSelectors(domSnapshot);
            var routeState = await inspector.DetectRouteAsync(token);
            var localStorageKeys = await inspector.CaptureLocalStorageKeysAsync(token);
            var networkCapture = inspector.Network.GetSnapshot();
            var consoleErrors = inspector.GetConsoleErrorsSnapshot();

            await WriteJsonAsync(result, directory, "dom-snapshot.json", domSnapshot, token);
            await WriteJsonAsync(result, directory, "network-capture.json", networkCapture, token);
            await WriteJsonAsync(result, directory, "selector-candidates.json", selectorCandidates, token);
            await WriteJsonAsync(result, directory, "route-state.json", routeState, token);
            await WriteJsonAsync(result, directory, "local-storage-keys.json", localStorageKeys, token);
            await WriteJsonAsync(result, directory, "console-errors.json", consoleErrors, token);
            await WriteReadmeAsync(result, directory, inspector.SurfaceName, token);
            return result;
        }

        private static async Task WriteJsonAsync(WebDebugExportResult result, string directory, string fileName, object payload, CancellationToken token)
        {
            string path = Path.Combine(directory, fileName);
            string json = JsonSerializer.Serialize(payload, JsonOptions);
            json = TokenRedactor.RedactText(json);
            await File.WriteAllTextAsync(path, json, Encoding.UTF8, token);
            result.Files.Add(path);
        }

        private static async Task WriteReadmeAsync(WebDebugExportResult result, string directory, string surfaceName, CancellationToken token)
        {
            string path = Path.Combine(directory, "README.md");
            string content =
$@"# AutoJMS WebView2 DevTools Capture

Surface: `{surfaceName}`
Captured: `{DateTime.Now:yyyy-MM-dd HH:mm:ss}`

Files:

- `dom-snapshot.json`: current route, title, body text sample, visible inputs/buttons/selects.
- `network-capture.json`: captured WebView2 network requests/responses with sensitive headers/body values redacted.
- `selector-candidates.json`: ranked selector candidates. Verify candidates in DevTools before changing automation.
- `route-state.json`: detected page state for route guards.
- `local-storage-keys.json`: storage key names only, no values.
- `console-errors.json`: console/log warnings and errors with sensitive values redacted.

Rules:

- Do not commit this capture bundle.
- Do not paste raw production tokens, cookies, or private payloads into prompts.
- Use selector candidates as evidence, not as final truth. Prefer stable id/name/placeholder/aria/label-scoped selectors.
- If a selector must be hardcoded temporarily, add a TODO to move it into runtime selector config.
";
            await File.WriteAllTextAsync(path, content, Encoding.UTF8, token);
            result.Files.Add(path);
        }

        private static string SanitizePathPart(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "webview";
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value.Trim();
        }
    }
}
