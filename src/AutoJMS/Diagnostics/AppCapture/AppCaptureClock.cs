#nullable enable
using System;
using System.Diagnostics;

namespace AutoJMS.Diagnostics.AppCapture
{
    public sealed class AppCaptureClock
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
        public long ElapsedMs => _stopwatch.ElapsedMilliseconds;
    }
}

