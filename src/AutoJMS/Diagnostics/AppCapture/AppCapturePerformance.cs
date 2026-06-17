#nullable enable
using System;
using System.Diagnostics;

namespace AutoJMS.Diagnostics.AppCapture
{
    public sealed class AppCapturePerformance : IDisposable
    {
        private readonly string _phase;
        private readonly string _operation;
        private readonly object? _context;
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private bool _disposed;

        public AppCapturePerformance(string phase, string operation, object? context = null)
        {
            _phase = phase;
            _operation = operation;
            _context = context;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _watch.Stop();
            AppCaptureManager.Instance.RecordPerformance(_phase, _operation, _watch.ElapsedMilliseconds, _context);
        }
    }
}

