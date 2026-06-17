#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.Diagnostics.AppCapture
{
    public interface IAppCaptureManager
    {
        bool IsEnabled { get; }
        AppCaptureSession? CurrentSession { get; }
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
        void RecordEvent(AppCaptureEvent evt);
        void RecordError(Exception ex, string source, object? context = null);
        void RecordPerformance(string phase, string operation, long elapsedMs, object? context = null);
    }

    public sealed class AppCaptureManager : IAppCaptureManager
    {
        private readonly object _sync = new();
        private AppCaptureWriter? _writer;
        private AppCaptureClock _clock = new();
        private bool _started;

        private AppCaptureManager()
        {
        }

        public static AppCaptureManager Instance { get; } = new();
        public AppCaptureOptions Options { get; private set; } = new();
        public bool IsEnabled => Options.Enabled && _writer != null;
        public AppCaptureSession? CurrentSession { get; private set; }

        public void Configure(AppCaptureOptions options)
        {
            Options = options ?? new AppCaptureOptions();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (!Options.Enabled)
            {
                AppLogger.Info("[AppCapture] Enabled=False");
                return;
            }

            lock (_sync)
            {
                if (_started) return;
                _started = true;
                _clock = new AppCaptureClock();
                string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                string root = Path.Combine(AppPaths.UserDataDir, "debug-captures", "app_capture", stamp);
                CurrentSession = new AppCaptureSession
                {
                    SessionId = $"{stamp}-{RandomNumberGenerator.GetInt32(1000, 9999)}",
                    StartedAt = _clock.StartedAt,
                    RootDirectory = root
                };
                _writer = new AppCaptureWriter(CurrentSession);
            }

            await WriteManifestAsync(cancellationToken).ConfigureAwait(false);
            RecordEvent(new AppCaptureEvent
            {
                Category = "app.startup",
                Source = "Program",
                EventName = "AppCapture.Started",
                Data = new Dictionary<string, object?>
                {
                    ["sessionPath"] = CurrentSession?.RootDirectory,
                    ["debugBuild"] = IsDebugBuild(),
                    ["slowApiThresholdMs"] = Options.SlowApiThresholdMs
                }
            });
            AppLogger.Info($"[AppCapture] Enabled=True SessionPath={CurrentSession?.RootDirectory}");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            AppCaptureWriter? writer;
            lock (_sync)
            {
                writer = _writer;
                if (writer == null) return;
            }

            try
            {
                RecordEvent(AppCaptureEvent.Create("app.startup", "Program", "AppCapture.Stopping"));
                await writer.StopAsync(cancellationToken).ConfigureAwait(false);
                await new AppCaptureReportGenerator().GenerateAsync(this, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[AppCapture] stop failed: {ex.Message}");
            }
            finally
            {
                lock (_sync)
                {
                    _writer = null;
                    _started = false;
                }
            }
        }

        public void RecordEvent(AppCaptureEvent evt)
        {
            if (!IsEnabled || evt == null) return;
            try
            {
                evt.Ts = DateTimeOffset.Now;
                evt.ElapsedMsFromStart = _clock.ElapsedMs;
                evt.Route = AppCaptureRedactor.RedactText(evt.Route);
                evt.WaybillNo = AppCaptureRedactor.RedactText(evt.WaybillNo);
                _writer?.WriteNdjson("timeline.ndjson", evt);
                string categoryFile = evt.Category switch
                {
                    "user.action" => "user-actions.ndjson",
                    "app.http" => "app-http.ndjson",
                    "webview.network" => "webview-network.ndjson",
                    "webview.console" => "webview-console.ndjson",
                    "webview.route" => "webview-route.ndjson",
                    "performance" => "performance.ndjson",
                    "error" => "errors.ndjson",
                    _ => ""
                };
                if (!string.IsNullOrWhiteSpace(categoryFile))
                    _writer?.WriteNdjson(categoryFile, evt);
            }
            catch { }
        }

        public void RecordError(Exception ex, string source, object? context = null)
        {
            if (ex == null) return;
            RecordEvent(new AppCaptureEvent
            {
                Category = "error",
                Source = source ?? "",
                EventName = ex.GetType().Name,
                Data = new Dictionary<string, object?>
                {
                    ["message"] = AppCaptureRedactor.RedactText(ex.Message),
                    ["stackTrace"] = AppCaptureRedactor.RedactText(ex.StackTrace ?? ""),
                    ["context"] = context
                }
            });
        }

        public void RecordPerformance(string phase, string operation, long elapsedMs, object? context = null)
        {
            var evt = new AppCaptureEvent
            {
                Category = "performance",
                Source = phase ?? "",
                EventName = operation ?? "",
                DurationMs = elapsedMs,
                Data = new Dictionary<string, object?>
                {
                    ["phase"] = phase,
                    ["operation"] = operation,
                    ["elapsedMs"] = elapsedMs,
                    ["isSlow"] = elapsedMs >= Options.SlowApiThresholdMs,
                    ["context"] = context
                }
            };
            RecordEvent(evt);
        }

        public string SaveBody(string prefix, string extension, string body)
            => IsEnabled ? _writer?.WriteBody(prefix, extension, body) ?? "" : "";

        public string SaveDomSnapshot(string source, string html)
            => IsEnabled ? _writer?.WriteDomSnapshot(source, html) ?? "" : "";

        private async Task WriteManifestAsync(CancellationToken cancellationToken)
        {
            if (CurrentSession == null) return;
            string path = Path.Combine(CurrentSession.RootDirectory, "manifest.json");
            Directory.CreateDirectory(CurrentSession.RootDirectory);
            var manifest = new
            {
                CurrentSession.SessionId,
                startedAt = CurrentSession.StartedAt,
                appVersion = AppVersion.Current,
                installRoot = AppPaths.InstallRoot,
                appData = AppPaths.UserDataDir,
                options = Options
            };
            string json = JsonSerializer.Serialize(manifest, AppConfig.CreateJsonOptions());
            await File.WriteAllTextAsync(path, AppCaptureRedactor.RedactText(json), cancellationToken).ConfigureAwait(false);
        }

        private static bool IsDebugBuild()
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
}
