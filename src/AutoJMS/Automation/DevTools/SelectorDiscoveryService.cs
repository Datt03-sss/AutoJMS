#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AutoJMS.Automation.DevTools
{
    public sealed class SelectorDiscoveryService
    {
        private static readonly Regex GeneratedIdPattern = new(@"(\d{4,}|[a-f0-9]{8,})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public SelectorDiscoveryResult Discover(DomSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var result = new SelectorDiscoveryResult
            {
                CapturedAtUtc = DateTime.UtcNow,
                SurfaceName = snapshot.SurfaceName,
                Url = snapshot.Url
            };

            foreach (var element in snapshot.Inputs.Concat(snapshot.Buttons).Concat(snapshot.Selects))
            {
                AddCandidates(result.Candidates, snapshot.Url, element);
            }

            result.Candidates = result.Candidates
                .OrderByDescending(x => x.Confidence)
                .ThenBy(x => x.ElementKind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Selector, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return result;
        }

        private static void AddCandidates(List<SelectorCandidate> candidates, string url, DomElementInfo element)
        {
            string tag = string.IsNullOrWhiteSpace(element.TagName) ? NormalizeKindToTag(element.Kind) : element.TagName;
            string label = FirstNonEmpty(element.NearbyLabel, element.AriaLabel, element.Placeholder, element.Text);

            if (!string.IsNullOrWhiteSpace(element.Id) && !LooksGenerated(element.Id))
            {
                Add(candidates, element, url, "CssId", $"#{EscapeCssIdentifier(element.Id)}", "", 0.96, "Stable id.");
            }

            if (!string.IsNullOrWhiteSpace(element.Name))
            {
                Add(candidates, element, url, "CssName", $"{tag}[name=\"{EscapeCssString(element.Name)}\"]", "", 0.90, "Name attribute.");
            }

            if (!string.IsNullOrWhiteSpace(element.Placeholder))
            {
                Add(candidates, element, url, "CssPlaceholder", $"{tag}[placeholder=\"{EscapeCssString(element.Placeholder)}\"]", "", 0.86, "Placeholder text.");
            }

            if (!string.IsNullOrWhiteSpace(element.AriaLabel))
            {
                Add(candidates, element, url, "CssAriaLabel", $"{tag}[aria-label=\"{EscapeCssString(element.AriaLabel)}\"]", "", 0.84, "ARIA label.");
            }

            if (!string.IsNullOrWhiteSpace(element.NearbyLabel))
            {
                string axisTag = element.Kind.Equals("button", StringComparison.OrdinalIgnoreCase) ? "button" : "input";
                Add(candidates, element, url, "XPathLabelNearby", "", $"//label[contains(normalize-space(.), \"{EscapeXPathString(element.NearbyLabel)}\")]/following::{axisTag}[1]", 0.80, "Nearby label text.");
            }

            AddElementUiCandidates(candidates, url, element, tag);

            if (!string.IsNullOrWhiteSpace(element.Text) && element.Kind.Equals("button", StringComparison.OrdinalIgnoreCase))
            {
                Add(candidates, element, url, "XPathButtonText", "", $"//button[contains(normalize-space(.), \"{EscapeXPathString(element.Text)}\")]", 0.78, "Button visible text.");
            }

            if (!string.IsNullOrWhiteSpace(element.CssSelector) && !IsGlobalSelector(element.CssSelector))
            {
                Add(candidates, element, url, "CssPath", element.CssSelector, "", 0.68, "DOM-derived path. Verify stability before automation.");
            }

            if (!string.IsNullOrWhiteSpace(element.XPath))
            {
                Add(candidates, element, url, "XPathFallback", "", element.XPath, 0.50, "Fallback only. Prefer stable CSS selector if available.");
            }
        }

        private static void AddElementUiCandidates(List<SelectorCandidate> candidates, string url, DomElementInfo element, string tag)
        {
            if (element.ElementUiClasses == null || element.ElementUiClasses.Count == 0) return;

            bool hasInputInner = element.ElementUiClasses.Any(x => x.Equals("el-input__inner", StringComparison.OrdinalIgnoreCase));
            bool hasButtonPrimary = element.ElementUiClasses.Any(x => x.Equals("el-button--primary", StringComparison.OrdinalIgnoreCase));
            bool hasSelect = element.ElementUiClasses.Any(x => x.Equals("el-select", StringComparison.OrdinalIgnoreCase));

            if (hasInputInner && !string.IsNullOrWhiteSpace(element.Placeholder))
            {
                Add(candidates, element, url, "ElementUiScoped", $".el-input__inner[placeholder=\"{EscapeCssString(element.Placeholder)}\"]", "", 0.82, "Element UI input with placeholder.");
            }
            else if (hasInputInner && !string.IsNullOrWhiteSpace(element.NearbyLabel))
            {
                Add(candidates, element, url, "XPathElementUiLabel", "", $"//*[contains(@class,'el-form-item')][.//*[contains(normalize-space(.), \"{EscapeXPathString(element.NearbyLabel)}\")]]//input[contains(@class,'el-input__inner')]", 0.77, "Element UI input scoped by form label.");
            }

            if (hasButtonPrimary)
            {
                Add(candidates, element, url, "ElementUiButton", "button.el-button--primary", "", 0.62, "Element UI primary button. Scope further if multiple buttons exist.");
            }

            if (hasSelect || tag.Equals("select", StringComparison.OrdinalIgnoreCase))
            {
                Add(candidates, element, url, "ElementUiSelect", ".el-select .el-input__inner", "", 0.60, "Element UI select input. Scope further if multiple selects exist.");
            }
        }

        private static void Add(
            List<SelectorCandidate> candidates,
            DomElementInfo element,
            string url,
            string selectorType,
            string selector,
            string xpath,
            double confidence,
            string reason)
        {
            if (string.IsNullOrWhiteSpace(selector) && string.IsNullOrWhiteSpace(xpath)) return;

            candidates.Add(new SelectorCandidate
            {
                ElementKind = element.Kind,
                ElementText = FirstNonEmpty(element.Text, element.Placeholder, element.AriaLabel, element.NearbyLabel),
                SelectorType = selectorType,
                Selector = selector,
                XPath = xpath,
                Confidence = confidence,
                Reason = reason,
                NearbyLabel = element.NearbyLabel,
                Url = url
            });
        }

        private static bool LooksGenerated(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            return GeneratedIdPattern.IsMatch(value) || value.StartsWith("el-id-", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGlobalSelector(string selector)
        {
            if (string.IsNullOrWhiteSpace(selector)) return true;
            string s = selector.Trim();
            return s.Equals("input", StringComparison.OrdinalIgnoreCase)
                || s.Equals("button", StringComparison.OrdinalIgnoreCase)
                || s.Equals("select", StringComparison.OrdinalIgnoreCase)
                || s.Equals("textarea", StringComparison.OrdinalIgnoreCase)
                || s.Equals(".el-input__inner", StringComparison.OrdinalIgnoreCase)
                || s.Equals(".el-button", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeKindToTag(string kind)
        {
            return kind.Equals("button", StringComparison.OrdinalIgnoreCase) ? "button"
                : kind.Equals("select", StringComparison.OrdinalIgnoreCase) ? "select"
                : "input";
        }

        private static string EscapeCssString(string value)
        {
            return (value ?? "").Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        }

        private static string EscapeCssIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            if (Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_-]*$")) return value;
            return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        }

        private static string EscapeXPathString(string value)
        {
            return (value ?? "").Replace("\"", "'", StringComparison.Ordinal);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return "";
        }
    }
}
