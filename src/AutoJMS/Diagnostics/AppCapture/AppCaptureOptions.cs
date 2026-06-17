#nullable enable
using System;

namespace AutoJMS.Diagnostics.AppCapture
{
    public sealed class AppCaptureOptions
    {
        public bool Enabled { get; set; }
        public bool CaptureWebViewNetwork { get; set; } = true;
        public bool CaptureAppHttpClient { get; set; } = true;
        public bool CaptureConsole { get; set; } = true;
        public bool CaptureUserActions { get; set; } = true;
        public bool CaptureDomOnRouteChange { get; set; } = true;
        public bool CaptureDomOnError { get; set; } = true;
        public bool CaptureDomOnDemand { get; set; } = true;
        public bool CaptureDomContinuously { get; set; } = false;
        public int SlowApiThresholdMs { get; set; } = 1000;
        public int MaxResponseBodyBytes { get; set; } = 256_000;
        public int MaxRequestBodyBytes { get; set; } = 128_000;
        public int DomMinIntervalSeconds { get; set; } = 10;

        public static AppCaptureOptions FromRuntime(AppSettings? settings, string[]? args)
        {
            var options = new AppCaptureOptions();
            bool commandLineEnabled = false;
            if (args != null)
            {
                foreach (var arg in args)
                {
                    if (string.Equals(arg, "--capture-debug", StringComparison.OrdinalIgnoreCase))
                    {
                        commandLineEnabled = true;
                        break;
                    }
                }
            }

            string env = Environment.GetEnvironmentVariable("AUTOJMS_CAPTURE_DEBUG") ?? "";
            bool envEnabled = env.Equals("1", StringComparison.OrdinalIgnoreCase)
                || env.Equals("true", StringComparison.OrdinalIgnoreCase)
                || env.Equals("yes", StringComparison.OrdinalIgnoreCase);

#if DEBUG
            options.Enabled = true;
#else
            options.Enabled = false;
#endif
            options.Enabled = options.Enabled
                || commandLineEnabled
                || envEnabled
                || settings?.DebugCaptureEnabled == true;

            if (settings != null)
            {
                options.SlowApiThresholdMs = settings.DebugCaptureSlowApiThresholdMs > 0
                    ? settings.DebugCaptureSlowApiThresholdMs
                    : options.SlowApiThresholdMs;
                options.MaxResponseBodyBytes = settings.DebugCaptureMaxResponseBodyBytes > 0
                    ? settings.DebugCaptureMaxResponseBodyBytes
                    : options.MaxResponseBodyBytes;
                options.MaxRequestBodyBytes = settings.DebugCaptureMaxRequestBodyBytes > 0
                    ? settings.DebugCaptureMaxRequestBodyBytes
                    : options.MaxRequestBodyBytes;
            }

            return options;
        }
    }
}

