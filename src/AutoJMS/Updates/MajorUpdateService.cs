using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Velopack;
using Velopack.Sources;

namespace AutoJMS
{
    public class MajorUpdateService
    {
        private readonly SupabaseManifestService _manifestService;
        private string _channel;

        public MajorUpdateService(SupabaseManifestService manifestService, SupabaseReleasesConfig releases = null, string channel = "stable")
        {
            _manifestService = manifestService;
            _channel = channel ?? "stable";
        }

        public void SetChannel(string channel) => _channel = channel ?? "stable";

        /// <summary>
        /// Build the Velopack update source for a channel. GitHub Releases when
        /// provider=github (download direct, no browser), else the legacy
        /// Supabase Storage feed via SimpleWebSource.
        /// </summary>
        private static IUpdateSource BuildSource(VersionChannel ch)
        {
            if (ch != null && ch.IsGithubProvider)
            {
                string repoUrl = !string.IsNullOrWhiteSpace(ch.GithubRepoUrl)
                    ? ch.GithubRepoUrl
                    : (!string.IsNullOrWhiteSpace(ch.GithubRepo) ? $"https://github.com/{ch.GithubRepo}" : null);

                if (!string.IsNullOrWhiteSpace(repoUrl))
                {
                    AppLogger.Info($"MajorUpdateService: provider=github, repo={repoUrl}, prerelease={ch.Prerelease}, tag={ch.Tag}");
                    return new GithubSource(repoUrl, null, ch.Prerelease, null);
                }
                AppLogger.Warning("MajorUpdateService: provider=github but no repo URL — falling back to Supabase feed.");
            }

            if (ch == null || string.IsNullOrWhiteSpace(ch.VelopackFeedUrl))
                throw new InvalidOperationException("Channel has no GithubSource and no Velopack RELEASES feed folder.");

            AppLogger.Info($"MajorUpdateService: provider=supabase (legacy), feed={ch?.VelopackFeedUrl}");
            return new SimpleWebSource(new Uri(ch.VelopackFeedUrl));
        }

        public async Task<(VersionChannel channel, bool hasUpdate)> CheckForUpdateAsync(CancellationToken ct = default)
        {
            try
            {
                var latest = await _manifestService.FetchVersionLatestAsync(ct);
                if (latest == null || latest.Channels == null || latest.Channels.Count == 0)
                {
                    AppLogger.Info("MajorUpdateService: no version-latest manifest available");
                    return (null, false);
                }

                if (!latest.Channels.TryGetValue(_channel, out var ch) ||
                    ch == null ||
                    string.IsNullOrWhiteSpace(ch.Version))
                {
                    AppLogger.Info($"MajorUpdateService: no channel '{_channel}' or missing version");
                    return (null, false);
                }

                // A channel must point to SOMETHING: a github repo or a legacy feed.
                if (!ch.IsGithubProvider && string.IsNullOrWhiteSpace(ch.VelopackFeedUrl))
                {
                    AppLogger.Info($"MajorUpdateService: channel '{_channel}' has neither github provider nor velopackFeedUrl");
                    return (null, false);
                }

                try
                {
                    var source = BuildSource(ch);
                    var manager = new UpdateManager(source, new UpdateOptions { ExplicitChannel = _channel });
                    var updateInfo = await manager.CheckForUpdatesAsync();

                    bool hasUpdate = updateInfo != null;
                    AppLogger.Info($"MajorUpdateService: channel={_channel}, provider={(ch.IsGithubProvider ? "github" : "supabase")}, hasUpdate={hasUpdate}");
                    return (ch, hasUpdate);
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"MajorUpdateService: Velopack check failed: {ex.Message}");
                    return (ch, false);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"MajorUpdateService: check failed. {ex.Message}");
                return (null, false);
            }
        }

        public async Task<(bool success, string message)> DownloadAndApplyAsync(
            VersionChannel channelInfo, IProgress<string> progress = null, CancellationToken ct = default)
        {
            if (channelInfo == null)
                return (false, "Không có thông tin cập nhật.");
            if (!channelInfo.IsGithubProvider && string.IsNullOrWhiteSpace(channelInfo.VelopackFeedUrl))
                return (false, "Không có đường dẫn cập nhật.");

            try
            {
                progress?.Report("Đang kiểm tra bản cập nhật...");

                var source = BuildSource(channelInfo);
                var manager = new UpdateManager(source, new UpdateOptions { ExplicitChannel = _channel });

                var updateInfo = await manager.CheckForUpdatesAsync();
                if (updateInfo == null)
                    return (false, "Không có bản cập nhật mới.");

                progress?.Report("Đang tải bản cập nhật...");
                await manager.DownloadUpdatesAsync(updateInfo);

                progress?.Report("Đang áp dụng cập nhật...");
                manager.ApplyUpdatesAndRestart(updateInfo);

                return (true, "Đang cập nhật và khởi động lại...");
            }
            catch (OperationCanceledException)
            {
                return (false, "Đã hủy cập nhật.");
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi cập nhật: {ex.Message}");
            }
        }
    }
}
