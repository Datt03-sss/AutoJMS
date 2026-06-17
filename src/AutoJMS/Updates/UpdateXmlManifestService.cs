#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using AutoJMS.Diagnostics.AppCapture;

namespace AutoJMS
{
    /// <summary>
    /// Reads the lightweight GitHub raw update.xml used by the About dialog.
    /// This XML is UI/control metadata only; Velopack binaries are still resolved
    /// through GithubSource or a real RELEASES feed folder.
    /// </summary>
    public sealed class UpdateXmlManifestService
    {
        private readonly string _manifestUrl;
        private readonly HttpClient _http;

        public UpdateXmlManifestService(string? manifestUrl = null)
        {
            _manifestUrl = string.IsNullOrWhiteSpace(manifestUrl)
                ? AppRuntimeConfig.DefaultUpdateXmlUrl
                : manifestUrl.Trim();
            _http = new HttpClient(new AppHttpCaptureHandler(new HttpClientHandler(), "UpdateXmlManifestService")) { Timeout = TimeSpan.FromSeconds(15) };
        }

        public async Task<VersionLatest?> FetchAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                string xml = await _http.GetStringAsync(_manifestUrl, cancellationToken).ConfigureAwait(false);
                var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
                return await ParseAsync(doc, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"UpdateXmlManifestService: fetch {_manifestUrl} failed: {ex.Message}");
                return null;
            }
        }

        private async Task<VersionLatest> ParseAsync(XDocument doc, CancellationToken cancellationToken)
        {
            var root = doc.Root;
            var latest = new VersionLatest
            {
                SchemaVersion = ParseInt(Attr(root, "schemaVersion"), 2),
                UpdatedAt = Text(root, "updatedAt")
            };

            string githubRepoUrl = Text(root, "githubRepo");
            string githubRepo = ToGithubRepoSlug(githubRepoUrl);
            var channels = root?.Element("channels")?.Elements("channel");
            if (channels == null)
                return latest;

            foreach (var item in channels)
            {
                string name = Attr(item, "name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                bool enabled = ParseBool(Attr(item, "enabled"), true);
                if (!enabled)
                {
                    AppLogger.Warning($"UpdateXmlManifestService: channel '{name}' is disabled in update.xml");
                    continue;
                }

                var channel = new VersionChannel
                {
                    Version = Text(item, "velopackVersion"),
                    DisplayVersion = Text(item, "displayVersion"),
                    InternalBuild = Text(item, "internalBuild"),
                    VelopackChannel = name.Trim().ToLowerInvariant(),
                    Provider = "github",
                    GithubRepoUrl = githubRepoUrl,
                    GithubRepo = githubRepo,
                    Tag = FirstNonEmpty(Text(item, "releaseTag"), Text(item, "tag")),
                    SetupUrl = Text(item, "setupUrl"),
                    VelopackSetupUrl = Text(item, "velopackSetupUrl"),
                    ReleaseNotesUrl = Text(item, "releaseNotesUrl"),
                    VelopackFeedUrl = Text(item, "velopackFeedUrl"),
                    Prerelease = ParseBool(Attr(item, "prerelease"), false),
                    Mandatory = ParseBool(Text(item, "mandatory"), false),
                    ManualOnly = ParseBool(Text(item, "manualOnly"), true),
                    ReleaseNotes = NormalizeNotes(Text(item, "releaseNotes"))
                };

                if (string.IsNullOrWhiteSpace(channel.ReleaseNotes) &&
                    !string.IsNullOrWhiteSpace(channel.ReleaseNotesUrl))
                {
                    channel.ReleaseNotes = await FetchReleaseNotesAsync(channel.ReleaseNotesUrl, cancellationToken)
                        .ConfigureAwait(false);
                }

                WarnIfMissing(name, channel);
                latest.Channels[channel.VelopackChannel] = channel;
            }

            return latest;
        }

        private async Task<string> FetchReleaseNotesAsync(string url, CancellationToken cancellationToken)
        {
            try
            {
                return NormalizeNotes(await _http.GetStringAsync(url, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLogger.Warning($"UpdateXmlManifestService: releaseNotesUrl fetch failed: {ex.Message}");
                return "";
            }
        }

        private static void WarnIfMissing(string channelName, VersionChannel channel)
        {
            if (string.IsNullOrWhiteSpace(channel.Version))
                AppLogger.Warning($"UpdateXmlManifestService: channel '{channelName}' missing velopackVersion");
            if (string.IsNullOrWhiteSpace(channel.DisplayVersion))
                AppLogger.Warning($"UpdateXmlManifestService: channel '{channelName}' missing displayVersion");
            if (string.IsNullOrWhiteSpace(channel.InternalBuild))
                AppLogger.Warning($"UpdateXmlManifestService: channel '{channelName}' missing internalBuild");
            if (string.IsNullOrWhiteSpace(channel.ReleaseNotes))
                AppLogger.Warning($"UpdateXmlManifestService: channel '{channelName}' missing releaseNotes");
        }

        private static string Text(XElement? parent, string name)
            => parent?.Element(name)?.Value?.Trim() ?? "";

        private static string Attr(XElement? element, string name)
            => element?.Attribute(name)?.Value?.Trim() ?? "";

        private static bool ParseBool(string value, bool fallback)
            => bool.TryParse(value, out bool parsed) ? parsed : fallback;

        private static int ParseInt(string value, int fallback)
            => int.TryParse(value, out int parsed) ? parsed : fallback;

        private static string NormalizeNotes(string value)
            => string.IsNullOrWhiteSpace(value)
                ? ""
                : value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static string ToGithubRepoSlug(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "";

            const string prefix = "https://github.com/";
            string trimmed = url.Trim().TrimEnd('/');
            return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? trimmed[prefix.Length..]
                : trimmed;
        }
    }
}
