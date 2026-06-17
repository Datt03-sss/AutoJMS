#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.Diagnostics.AppCapture
{
    public sealed class AppCaptureWriter : IDisposable
    {
        private readonly BlockingCollection<WriteItem> _queue = new(new ConcurrentQueue<WriteItem>());
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _worker;
        private readonly JsonSerializerOptions _jsonOptions = AppConfig.CreateJsonOptions();
        private int _bodyCounter;
        private int _domCounter;
        private volatile bool _disabled;

        public AppCaptureWriter(AppCaptureSession session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Directory.CreateDirectory(Session.RootDirectory);
            Directory.CreateDirectory(Session.ApiBodiesDirectory);
            Directory.CreateDirectory(Session.DomDirectory);
            Directory.CreateDirectory(Session.ReportsDirectory);
            _worker = Task.Run(ProcessAsync);
        }

        public AppCaptureSession Session { get; }

        public void WriteNdjson(string relativeFile, object payload)
        {
            if (_disabled || string.IsNullOrWhiteSpace(relativeFile)) return;
            Enqueue(new WriteItem
            {
                RelativePath = relativeFile,
                Text = JsonSerializer.Serialize(payload, _jsonOptions) + Environment.NewLine,
                Append = true
            });
        }

        public string WriteBody(string prefix, string extension, string text)
        {
            if (_disabled) return "";
            int id = Interlocked.Increment(ref _bodyCounter);
            string safePrefix = string.IsNullOrWhiteSpace(prefix) ? "body" : Sanitize(prefix);
            string safeExtension = string.IsNullOrWhiteSpace(extension) ? ".txt" : extension.StartsWith(".") ? extension : "." + extension;
            string relative = Path.Combine("api-bodies", $"{safePrefix}_{id:000000}{safeExtension}");
            Enqueue(new WriteItem
            {
                RelativePath = relative,
                Text = AppCaptureRedactor.RedactText(text),
                Append = false
            });
            return relative.Replace('\\', '/');
        }

        public string WriteDomSnapshot(string source, string html)
        {
            if (_disabled) return "";
            int id = Interlocked.Increment(ref _domCounter);
            string safeSource = Sanitize(string.IsNullOrWhiteSpace(source) ? "webview" : source.ToLowerInvariant());
            string relative = Path.Combine("dom", $"{safeSource}_snapshot_{id:000000}.html.gz");
            Enqueue(new WriteItem
            {
                RelativePath = relative,
                Text = AppCaptureRedactor.RedactText(html),
                Append = false,
                Gzip = true
            });
            return relative.Replace('\\', '/');
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _queue.CompleteAdding();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                await _worker.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch { }
        }

        private void Enqueue(WriteItem item)
        {
            if (_disabled) return;
            try { _queue.Add(item); }
            catch { }
        }

        private async Task ProcessAsync()
        {
            try
            {
                foreach (var item in _queue.GetConsumingEnumerable(_cts.Token))
                {
                    try
                    {
                        string path = Path.Combine(Session.RootDirectory, item.RelativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Session.RootDirectory);
                        if (item.Gzip)
                        {
                            await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                            await using var gzip = new GZipStream(file, CompressionLevel.Fastest);
                            byte[] bytes = Encoding.UTF8.GetBytes(item.Text ?? "");
                            await gzip.WriteAsync(bytes, 0, bytes.Length, _cts.Token).ConfigureAwait(false);
                        }
                        else if (item.Append)
                        {
                            await File.AppendAllTextAsync(path, item.Text ?? "", Encoding.UTF8, _cts.Token).ConfigureAwait(false);
                        }
                        else
                        {
                            await File.WriteAllTextAsync(path, item.Text ?? "", Encoding.UTF8, _cts.Token).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _disabled = true;
                        AppLogger.Warning($"[AppCapture] writer disabled: {ex.Message}");
                    }
                }
            }
            catch { }
        }

        private static string Sanitize(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value.Trim();
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _queue.Dispose(); } catch { }
            try { _cts.Dispose(); } catch { }
        }

        private sealed class WriteItem
        {
            public string RelativePath { get; set; } = "";
            public string Text { get; set; } = "";
            public bool Append { get; set; }
            public bool Gzip { get; set; }
        }
    }
}

