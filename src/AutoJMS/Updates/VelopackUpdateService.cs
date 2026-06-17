#nullable enable
using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Velopack;
using Velopack.Sources;

namespace AutoJMS
{
    /// <summary>
    /// Handles in-app updates via Velopack.
    ///
    /// Control plane vs binary hosting:
    ///   - Supabase Storage hosts the small <c>version-latest.json</c> manifest
    ///     (the "control plane"): which version, which channel, which provider.
    ///   - GitHub Releases hosts the large Velopack binaries (RELEASES / .nupkg /
    ///     Setup.exe) because Supabase free plan rejects files &gt; 50 MB.
    ///
    /// When the manifest says <c>provider=github</c>, this service uses Velopack
    /// <see cref="GithubSource"/> to check/download/apply DIRECTLY — it never
    /// opens the GitHub web page. If the manifest is missing or says supabase,
    /// it falls back to the legacy <see cref="SimpleWebSource"/> Supabase feed.
    ///
    /// Only works inside a Velopack install layout (UpdateManager.IsInstalled).
    /// </summary>
    public sealed class VelopackUpdateService
    {
        // Legacy Supabase Storage feed root (used only when provider != github).
        private const string SupabaseStorageBase =
            "https://bnsnnrlwfzxemmizknwy.supabase.co/storage/v1/object/public/autojms-modules/releases";

        private readonly string _channel;
        private readonly Func<CancellationToken, Task>? _prepareForUpdate;
        private readonly Func<string?, string?, string, bool>? _confirmDowngrade;
        private VersionChannel? _lastResolvedChannel;
        private string? _lastManifestUrl;

        /// <param name="channel">"stable" or "beta".</param>
        /// <param name="prepareForUpdate">
        /// Callback invoked right before applying the update — used to stop
        /// heartbeat/license timers, ZaloService, tracking loop, print queue,
        /// FullStack realtime, WebView2, etc.
        /// </param>
        public VelopackUpdateService(
            string channel = "stable",
            Func<CancellationToken, Task>? prepareForUpdate = null,
            Func<string?, string?, string, bool>? confirmDowngrade = null)
        {
            _channel = string.IsNullOrWhiteSpace(channel) ? "stable" : channel.Trim().ToLowerInvariant();
            _prepareForUpdate = prepareForUpdate;
            _confirmDowngrade = confirmDowngrade;
        }

        /// <summary>Legacy Supabase Velopack feed URL for the current channel.</summary>
        public string FeedUrl => $"{SupabaseStorageBase}/{_channel}";

        /// <summary>
        /// Resolve the Velopack <see cref="IUpdateSource"/> for this channel by
        /// reading the Supabase <c>version-latest.json</c> control-plane manifest.
        /// Returns a <see cref="GithubSource"/> when provider=github, otherwise a
        /// <see cref="SimpleWebSource"/> against the legacy Supabase feed.
        /// </summary>
        private async Task<IUpdateSource> ResolveSourceAsync(CancellationToken ct)
        {
            _lastResolvedChannel = null;
            _lastManifestUrl = null;

            VersionChannel? ch = null;
            try
            {
                var xml = await new UpdateXmlManifestService(AppConfig.Current.UpdateXmlUrl)
                    .FetchAsync(ct)
                    .ConfigureAwait(false);
                if (xml?.Channels != null &&
                    xml.Channels.TryGetValue(_channel, out var xmlChannel))
                {
                    ch = xmlChannel;
                    _lastManifestUrl = AppConfig.Current.UpdateXmlUrl;
                }
                else
                {
                    AppLogger.Warning($"VelopackUpdateService: channel '{_channel}' not found in update.xml. Trying version-latest.json.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLogger.Warning($"VelopackUpdateService: could not read update.xml ({ex.Message}). Trying version-latest.json.");
            }

            try
            {
                if (ch == null)
                {
                    var manifestSvc = Program.SupabaseManifest;
                    if (manifestSvc != null)
                    {
                        var latest = await manifestSvc.FetchVersionLatestAsync(ct).ConfigureAwait(false);
                        if (latest?.Channels != null &&
                            latest.Channels.TryGetValue(_channel, out var exactChannel))
                        {
                            ch = exactChannel;
                            _lastManifestUrl = ResolveSupabaseVersionLatestUrl(manifestSvc);
                        }
                        else
                        {
                            AppLogger.Warning($"VelopackUpdateService: channel '{_channel}' not found in version-latest.json. Falling back to legacy Supabase feed.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"VelopackUpdateService: could not read version-latest.json ({ex.Message}). Falling back to Supabase feed.");
            }

            if (ch != null && ch.IsGithubProvider)
            {
                string? repoUrl = !string.IsNullOrWhiteSpace(ch.GithubRepoUrl)
                    ? ch.GithubRepoUrl
                    : (!string.IsNullOrWhiteSpace(ch.GithubRepo) ? $"https://github.com/{ch.GithubRepo}" : null);

                if (string.IsNullOrWhiteSpace(repoUrl))
                {
                    AppLogger.Warning("VelopackUpdateService: provider=github but no repo URL — falling back to Supabase feed.");
                }
                else
                {
                    AppLogger.Info($"VelopackUpdateService: provider=github, repo={repoUrl}, channel={_channel}, prerelease={ch.Prerelease}, tag={ch.Tag}");
                    _lastResolvedChannel = ch;
                    // GithubSource(repoUrl, accessToken, prerelease, downloader)
                    // Public repo → no token. Velopack reads the Releases API and
                    // downloads assets directly; it does NOT open a browser.
                    return new GithubSource(repoUrl, null, ch.Prerelease, null);
                }
            }

            if (ch != null && !string.IsNullOrWhiteSpace(ch.VelopackFeedUrl))
            {
                AppLogger.Info($"VelopackUpdateService: provider=supabase/static feed={ch.VelopackFeedUrl}");
                _lastResolvedChannel = ch;
                _lastManifestUrl ??= ch.VelopackFeedUrl;
                return new SimpleWebSource(ch.VelopackFeedUrl);
            }

            AppLogger.Info($"VelopackUpdateService: provider=supabase (legacy), feed={FeedUrl}");
            _lastResolvedChannel = ch;
            _lastManifestUrl ??= FeedUrl;
            return new SimpleWebSource(FeedUrl);
        }

        private async Task<UpdateManager> CreateManagerAsync(CancellationToken ct, bool allowVersionDowngrade = false)
        {
            var source = await ResolveSourceAsync(ct).ConfigureAwait(false);
            var options = new UpdateOptions
            {
                ExplicitChannel = _channel,
                AllowVersionDowngrade = allowVersionDowngrade
            };
            return new UpdateManager(source, options);
        }

        /// <summary>
        /// Full interactive flow: check → confirm → download (with progress) →
        /// prepare → apply & restart. Never opens a browser.
        /// </summary>
        public async Task CheckAndUpdateAsync(
            IProgress<int>? downloadProgress = null,
            CancellationToken ct = default)
        {
            UpdateManager mgr;
            try
            {
                mgr = await CreateManagerAsync(ct).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppLogger.Error("VelopackUpdateService: failed to create UpdateManager", ex);
                ShowError($"Không thể khởi tạo trình cập nhật.\n\n{ex.Message}");
                return;
            }

            if (!mgr.IsInstalled)
            {
                AppLogger.Warning("VelopackUpdateService: app is not running in a Velopack layout.");
                LogUpdateContext(mgr, updateAvailable: false, noUpdateReason: "NO_UPDATE_BECAUSE_RUNNING_UNPACKAGED_DEBUG_BUILD");
                ShowInfo("Ứng dụng chưa được cài bằng Velopack, không thể update trực tiếp.");
                return;
            }

            string? currentVersion = mgr.CurrentVersion?.ToString();
            string? targetVersion = _lastResolvedChannel?.Version;
            if (ShouldRequestDowngradeConfirmation(currentVersion, targetVersion))
            {
                AppLogger.Warning($"VelopackUpdateService: downgrade confirmation required, current={currentVersion}, channel={_channel}, target={targetVersion}");

                if (!ConfirmDowngrade(currentVersion, targetVersion))
                {
                    AppLogger.Info($"VelopackUpdateService: downgrade cancelled by user, channel={_channel}, target={targetVersion}");
                    return;
                }

                AppLogger.Info($"VelopackUpdateService: downgrade allowed by user, channel={_channel}, target={targetVersion}");
                mgr = await CreateManagerAsync(ct, allowVersionDowngrade: true).ConfigureAwait(true);
            }

            LogUpdateContext(mgr);

            UpdateInfo? updateInfo;
            try
            {
                updateInfo = await mgr.CheckForUpdatesAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppLogger.Error("VelopackUpdateService: check for updates failed", ex);
                ShowError($"Không thể kiểm tra cập nhật.\n\n{ex.Message}");
                return;
            }

            if (updateInfo == null)
            {
                string reason = await DiagnoseNoUpdateReasonAsync(mgr, ct).ConfigureAwait(true);
                LogUpdateContext(mgr, updateAvailable: false, noUpdateReason: reason);
                AppLogger.Info($"VelopackUpdateService: no update available for channel={_channel}, reason={reason}");
                ShowInfo("Bạn đang dùng phiên bản mới nhất.");
                return;
            }

            string newVersion = updateInfo.TargetFullRelease?.Version?.ToString() ?? "mới";
            LogUpdateContext(mgr, updateAvailable: true);
            var confirm = MessageBox.Show(
                $"Có bản cập nhật mới: v{newVersion}\n\nBạn có muốn cập nhật ngay không?",
                "AutoJMS Update",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                AppLogger.Info($"VelopackUpdateService: user declined update v{newVersion} on channel={_channel}");
                return;
            }

            try
            {
                await mgr.DownloadUpdatesAsync(
                    updateInfo,
                    p => downloadProgress?.Report(p),
                    ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                AppLogger.Info("VelopackUpdateService: download cancelled by user.");
                return;
            }
            catch (Exception ex)
            {
                AppLogger.Error("VelopackUpdateService: download failed", ex);
                ShowError($"Tải bản cập nhật thất bại.\n\n{ex.Message}");
                return;
            }

            // Stop running services before the process is replaced/restarted.
            try
            {
                if (_prepareForUpdate != null)
                    await _prepareForUpdate(ct).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"VelopackUpdateService: prepare-for-update reported: {ex.Message}");
            }

            try
            {
                AppLogger.Info($"VelopackUpdateService: applying update v{newVersion} and restarting.");
                mgr.ApplyUpdatesAndRestart(updateInfo);
            }
            catch (Exception ex)
            {
                AppLogger.Error("VelopackUpdateService: apply & restart failed", ex);
                ShowError($"Áp dụng bản cập nhật thất bại.\n\n{ex.Message}");
            }
        }

        /// <summary>Silent check (no UI). Returns the new version string, or null if none/error.</summary>
        public async Task<string?> CheckOnlyAsync(CancellationToken ct = default)
        {
            try
            {
                var mgr = await CreateManagerAsync(ct).ConfigureAwait(false);
                if (!mgr.IsInstalled)
                {
                    LogUpdateContext(mgr, updateAvailable: false, noUpdateReason: "NO_UPDATE_BECAUSE_RUNNING_UNPACKAGED_DEBUG_BUILD");
                    return null;
                }

                LogUpdateContext(mgr);
                var info = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
                LogUpdateContext(
                    mgr,
                    updateAvailable: info != null,
                    noUpdateReason: info == null
                        ? await DiagnoseNoUpdateReasonAsync(mgr, ct).ConfigureAwait(false)
                        : null);
                return info?.TargetFullRelease?.Version?.ToString();
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"VelopackUpdateService: silent check failed: {ex.Message}");
                return null;
            }
        }

        private static void ShowInfo(string message) =>
            MessageBox.Show(message, "AutoJMS", MessageBoxButtons.OK, MessageBoxIcon.Information);

        private static void ShowError(string message) =>
            MessageBox.Show(message, "AutoJMS Update", MessageBoxButtons.OK, MessageBoxIcon.Error);

        private void LogUpdateContext(
            UpdateManager? mgr,
            bool? updateAvailable = null,
            string? noUpdateReason = null)
        {
            var ch = _lastResolvedChannel;
            string provider = ch == null
                ? "UNKNOWN"
                : string.IsNullOrWhiteSpace(ch.Provider)
                ? (ch?.IsGithubProvider == true ? "github" : "supabase")
                : ch.Provider;

            AppLogger.Info($"[Update] currentVersion={mgr?.CurrentVersion?.ToString() ?? "UNKNOWN"}");
            AppLogger.Info($"[Update] currentAssemblyVersion={GetCurrentAssemblyVersion()}");
            AppLogger.Info($"[Update] selectedChannel={_channel}");
            AppLogger.Info($"[Update] manifestUrl={_lastManifestUrl ?? "UNKNOWN"}");
            AppLogger.Info($"[Update] provider={provider}");
            AppLogger.Info($"[Update] latestVersion={ch?.Version ?? "UNKNOWN"}");
            AppLogger.Info($"[Update] displayVersion={ch?.DisplayVersion ?? "UNKNOWN"}");
            AppLogger.Info($"[Update] githubRepoUrl={GetGithubRepoUrl(ch)}");
            AppLogger.Info($"[Update] tag={ch?.Tag ?? "UNKNOWN"}");
            AppLogger.Info($"[Update] prerelease={ch?.Prerelease.ToString() ?? "UNKNOWN"}");

            if (updateAvailable.HasValue)
                AppLogger.Info($"[Update] Velopack update available={updateAvailable.Value}");

            if (!string.IsNullOrWhiteSpace(noUpdateReason))
                AppLogger.Info($"[Update] no update reason={noUpdateReason}");
        }

        private async Task<string> DiagnoseNoUpdateReasonAsync(UpdateManager mgr, CancellationToken ct)
        {
            if (!mgr.IsInstalled)
                return "NO_UPDATE_BECAUSE_RUNNING_UNPACKAGED_DEBUG_BUILD";

            var ch = _lastResolvedChannel;
            if (ch == null)
                return "NO_UPDATE_BECAUSE_MANIFEST_CHANNEL_MISSING";

            if (string.Equals(_channel, "stable", StringComparison.OrdinalIgnoreCase) &&
                (ch.Prerelease || IsPrereleaseVersion(ch.Version)))
            {
                return "NO_UPDATE_BECAUSE_CHANNEL_STABLE_AND_LATEST_IS_BETA";
            }

            string? currentVersion = mgr.CurrentVersion?.ToString();
            if (!string.IsNullOrWhiteSpace(currentVersion) &&
                TryParseSemVer(currentVersion, out var current) &&
                TryParseSemVer(ch.Version, out var latest) &&
                CompareSemVer(latest, current) <= 0)
            {
                return "NO_UPDATE_BECAUSE_LATEST_VERSION_NOT_GREATER";
            }

            if (ch.IsGithubProvider)
                return await DiagnoseGithubReleaseAsync(ch, ct).ConfigureAwait(false);

            return "NO_UPDATE_REASON_UNKNOWN";
        }

        private static async Task<string> DiagnoseGithubReleaseAsync(VersionChannel ch, CancellationToken ct)
        {
            string repo = GetGithubRepoSlug(ch);
            string tag = ch.Tag ?? "";
            if (string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(tag))
                return "NO_UPDATE_BECAUSE_GITHUB_RELEASES_EMPTY";

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("AutoJMS-Update-Diagnostics");
                using var response = await http.GetAsync(
                    $"https://api.github.com/repos/{repo}/releases/tags/{Uri.EscapeDataString(tag)}",
                    ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return "NO_UPDATE_BECAUSE_GITHUB_RELEASES_EMPTY";

                string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("assets", out var assets) ||
                    assets.ValueKind != JsonValueKind.Array ||
                    assets.GetArrayLength() == 0)
                {
                    return "NO_UPDATE_BECAUSE_GITHUB_RELEASES_EMPTY";
                }

                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameProp) &&
                        string.Equals(nameProp.GetString(), "RELEASES", StringComparison.OrdinalIgnoreCase))
                    {
                        return "NO_UPDATE_REASON_UNKNOWN";
                    }
                }

                return "NO_UPDATE_BECAUSE_RELEASES_FILE_MISSING";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLogger.Warning($"VelopackUpdateService: GitHub release diagnostics failed: {ex.Message}");
                return "NO_UPDATE_REASON_UNKNOWN";
            }
        }

        private static string GetCurrentAssemblyVersion()
        {
            var asm = typeof(VelopackUpdateService).Assembly;
            return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? asm.GetName().Version?.ToString()
                ?? "UNKNOWN";
        }

        private static string GetGithubRepoUrl(VersionChannel? ch)
        {
            if (ch == null) return "UNKNOWN";
            if (!string.IsNullOrWhiteSpace(ch.GithubRepoUrl)) return ch.GithubRepoUrl;
            if (!string.IsNullOrWhiteSpace(ch.GithubRepo)) return $"https://github.com/{ch.GithubRepo}";
            return "UNKNOWN";
        }

        private static string GetGithubRepoSlug(VersionChannel ch)
        {
            if (!string.IsNullOrWhiteSpace(ch.GithubRepo))
                return ch.GithubRepo.Trim().Trim('/');

            string repoUrl = ch.GithubRepoUrl ?? "";
            const string prefix = "https://github.com/";
            string trimmed = repoUrl.Trim().TrimEnd('/');
            return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? trimmed[prefix.Length..]
                : trimmed;
        }

        private static string ResolveSupabaseVersionLatestUrl(SupabaseManifestService manifestSvc)
        {
            string? path = manifestSvc.Urls?.VersionLatest;
            if (string.IsNullOrWhiteSpace(path)) return "UNKNOWN";
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return path;
            return $"{manifestSvc.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
        }

        private static bool IsPrereleaseVersion(string? version) =>
            !string.IsNullOrWhiteSpace(version) && version.Contains('-', StringComparison.Ordinal);

        private bool ConfirmDowngrade(string? currentVersion, string? targetVersion)
        {
            if (_confirmDowngrade != null)
            {
                try
                {
                    return _confirmDowngrade(currentVersion, targetVersion, _channel);
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"VelopackUpdateService: custom downgrade confirmation failed: {ex.Message}");
                    return false;
                }
            }

            var allowDowngrade = MessageBox.Show(
                $"Kênh {_channel} hiện có version thấp hơn version đang cài.\n\n" +
                $"Đang cài: {currentVersion ?? "UNKNOWN"}\n" +
                $"Kênh {_channel}: {targetVersion ?? "UNKNOWN"}\n\n" +
                "Bạn có muốn downgrade để chuyển kênh không?",
                "Xác nhận downgrade",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            return allowDowngrade == DialogResult.Yes;
        }

        private static bool ShouldRequestDowngradeConfirmation(string? currentVersion, string? targetVersion)
        {
            if (string.IsNullOrWhiteSpace(currentVersion) || string.IsNullOrWhiteSpace(targetVersion))
                return false;

            if (!TryParseSemVer(currentVersion, out var current) ||
                !TryParseSemVer(targetVersion, out var target))
                return false;

            return CompareSemVer(target, current) < 0;
        }

        private static bool TryParseSemVer(string value, out (int Major, int Minor, int Patch, string PreLabel, int PreNumber) version)
        {
            version = default;
            string clean = (value ?? "").Trim().TrimStart('v', 'V');
            int plus = clean.IndexOf('+');
            if (plus >= 0) clean = clean[..plus];

            string main = clean;
            string pre = "";
            int dash = clean.IndexOf('-');
            if (dash >= 0)
            {
                main = clean[..dash];
                pre = clean[(dash + 1)..];
            }

            var parts = main.Split('.');
            if (parts.Length != 3 ||
                !int.TryParse(parts[0], out int major) ||
                !int.TryParse(parts[1], out int minor) ||
                !int.TryParse(parts[2], out int patch))
            {
                return false;
            }

            string preLabel = "";
            int preNumber = 0;
            if (!string.IsNullOrWhiteSpace(pre))
            {
                var preParts = pre.Split('.');
                preLabel = preParts[0];
                if (preParts.Length > 1) int.TryParse(preParts[1], out preNumber);
            }

            version = (major, minor, patch, preLabel, preNumber);
            return true;
        }

        private static int CompareSemVer(
            (int Major, int Minor, int Patch, string PreLabel, int PreNumber) left,
            (int Major, int Minor, int Patch, string PreLabel, int PreNumber) right)
        {
            int cmp = left.Major.CompareTo(right.Major);
            if (cmp != 0) return cmp;
            cmp = left.Minor.CompareTo(right.Minor);
            if (cmp != 0) return cmp;
            cmp = left.Patch.CompareTo(right.Patch);
            if (cmp != 0) return cmp;

            bool leftPre = !string.IsNullOrWhiteSpace(left.PreLabel);
            bool rightPre = !string.IsNullOrWhiteSpace(right.PreLabel);
            if (!leftPre && !rightPre) return 0;
            if (!leftPre) return 1;
            if (!rightPre) return -1;

            cmp = string.Compare(left.PreLabel, right.PreLabel, StringComparison.OrdinalIgnoreCase);
            if (cmp != 0) return cmp;
            return left.PreNumber.CompareTo(right.PreNumber);
        }
    }
}
