using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AutoJMS
{
    public class RuntimeConfig
    {
        [JsonPropertyName("selectors")]
        public Dictionary<string, Dictionary<string, string>> Selectors { get; set; } = new();

        [JsonPropertyName("apiEndpoints")]
        public Dictionary<string, string> ApiEndpoints { get; set; } = new();

        [JsonPropertyName("tracking")]
        public TrackingCfg Tracking { get; set; } = new();

        [JsonPropertyName("print")]
        public PrintCfg Print { get; set; } = new();

        [JsonPropertyName("workflow")]
        public WorkflowCfg Workflow { get; set; } = new();

        [JsonPropertyName("timeoutRetry")]
        public TimeoutRetryCfg TimeoutRetry { get; set; } = new();
    }

    public class TrackingCfg
    {
        [JsonPropertyName("intervalMinutes")]
        public int IntervalMinutes { get; set; } = 30;

        [JsonPropertyName("slaWarningHours")]
        public double SlaWarningHours { get; set; } = 2.0;

        [JsonPropertyName("slaCriticalHours")]
        public double SlaCriticalHours { get; set; } = 0.5;

        [JsonPropertyName("autoRefreshIntervalMs")]
        public int AutoRefreshIntervalMs { get; set; } = 60000;
    }

    public class PrintCfg
    {
        [JsonPropertyName("keepRecentPdfCount")]
        public int KeepRecentPdfCount { get; set; } = 500;

        [JsonPropertyName("printLogRetentionDays")]
        public int PrintLogRetentionDays { get; set; } = 3;

        [JsonPropertyName("defaultPaperWidth")]
        public int DefaultPaperWidth { get; set; } = 762;

        [JsonPropertyName("defaultPaperHeight")]
        public int DefaultPaperHeight { get; set; } = 762;
    }

    public class WorkflowCfg
    {
        [JsonPropertyName("dkchTimeoutMs")]
        public int DkchTimeoutMs { get; set; } = 30000;

        [JsonPropertyName("searchTimeoutMs")]
        public int SearchTimeoutMs { get; set; } = 15000;

        [JsonPropertyName("autoStartDkch")]
        public bool AutoStartDkch { get; set; } = false;
    }

    public class TimeoutRetryCfg
    {
        [JsonPropertyName("defaultTimeoutMs")]
        public int DefaultTimeoutMs { get; set; } = 30000;

        [JsonPropertyName("maxRetries")]
        public int MaxRetries { get; set; } = 3;

        [JsonPropertyName("retryDelayMs")]
        public int RetryDelayMs { get; set; } = 2000;
    }
}
