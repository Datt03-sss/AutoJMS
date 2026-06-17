#nullable enable
using System;
using System.Collections.Generic;

namespace AutoJMS.Automation.DevTools
{
    public enum WebDebugRouteStateKind
    {
        Unknown,
        NotLoggedIn,
        WrongPage,
        Loading,
        Ready,
        Error
    }

    public sealed class WebDebugRouteState
    {
        public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
        public string SurfaceName { get; set; } = "";
        public string RouteName { get; set; } = "";
        public WebDebugRouteStateKind State { get; set; } = WebDebugRouteStateKind.Unknown;
        public string CurrentUrl { get; set; } = "";
        public string ExpectedRouteFragment { get; set; } = "";
        public string Detail { get; set; } = "";
        public Dictionary<string, string> Signals { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class NetworkCaptureEntry
    {
        public string RequestId { get; set; } = "";
        public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? FinishedAtUtc { get; set; }
        public string Method { get; set; } = "";
        public string Url { get; set; } = "";
        public string Host { get; set; } = "";
        public string EndpointKind { get; set; } = "Other";
        public string ResourceType { get; set; } = "";
        public Dictionary<string, string> RequestHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string RequestBody { get; set; } = "";
        public bool RequestBodyTruncated { get; set; }
        public int? ResponseStatus { get; set; }
        public string ResponseMimeType { get; set; } = "";
        public Dictionary<string, string> ResponseHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string ResponseBody { get; set; } = "";
        public bool ResponseBodyTruncated { get; set; }
        public string BodyFetchError { get; set; } = "";
        public string FailureText { get; set; } = "";
    }

    public sealed class DomSnapshot
    {
        public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
        public string SurfaceName { get; set; } = "";
        public string Url { get; set; } = "";
        public string Title { get; set; } = "";
        public string Pathname { get; set; } = "";
        public string Hash { get; set; } = "";
        public string BodyTextSample { get; set; } = "";
        public List<DomElementInfo> Inputs { get; set; } = new();
        public List<DomElementInfo> Buttons { get; set; } = new();
        public List<DomElementInfo> Selects { get; set; } = new();
    }

    public sealed class DomElementInfo
    {
        public string Kind { get; set; } = "";
        public string TagName { get; set; } = "";
        public string Text { get; set; } = "";
        public string Placeholder { get; set; } = "";
        public string Name { get; set; } = "";
        public string Id { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string Type { get; set; } = "";
        public string Role { get; set; } = "";
        public string AriaLabel { get; set; } = "";
        public string NearbyLabel { get; set; } = "";
        public List<string> ElementUiClasses { get; set; } = new();
        public string CssSelector { get; set; } = "";
        public string XPath { get; set; } = "";
        public bool IsVisible { get; set; }
    }

    public sealed class SelectorDiscoveryResult
    {
        public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
        public string SurfaceName { get; set; } = "";
        public string Url { get; set; } = "";
        public List<SelectorCandidate> Candidates { get; set; } = new();
    }

    public sealed class SelectorCandidate
    {
        public string ElementKind { get; set; } = "";
        public string ElementText { get; set; } = "";
        public string SelectorType { get; set; } = "";
        public string Selector { get; set; } = "";
        public string XPath { get; set; } = "";
        public double Confidence { get; set; }
        public string Reason { get; set; } = "";
        public string NearbyLabel { get; set; } = "";
        public string Url { get; set; } = "";
    }

    public sealed class LocalStorageKeysSnapshot
    {
        public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
        public string SurfaceName { get; set; } = "";
        public string Url { get; set; } = "";
        public List<string> LocalStorageKeys { get; set; } = new();
        public List<string> SessionStorageKeys { get; set; } = new();
    }

    public sealed class ConsoleMessageCapture
    {
        public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
        public string Source { get; set; } = "";
        public string Level { get; set; } = "";
        public string Text { get; set; } = "";
        public string Url { get; set; } = "";
        public int LineNumber { get; set; }
    }

    public sealed class WebDebugExportResult
    {
        public string DirectoryPath { get; set; } = "";
        public List<string> Files { get; set; } = new();
    }
}
