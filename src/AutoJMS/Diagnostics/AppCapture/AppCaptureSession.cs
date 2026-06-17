#nullable enable
using System;
using System.IO;

namespace AutoJMS.Diagnostics.AppCapture
{
    public sealed class AppCaptureSession
    {
        public string SessionId { get; init; } = "";
        public DateTimeOffset StartedAt { get; init; }
        public string RootDirectory { get; init; } = "";
        public string ApiBodiesDirectory => Path.Combine(RootDirectory, "api-bodies");
        public string DomDirectory => Path.Combine(RootDirectory, "dom");
        public string ReportsDirectory => Path.Combine(RootDirectory, "reports");
    }
}

