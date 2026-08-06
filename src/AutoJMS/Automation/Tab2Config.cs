#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoJMS
{
    /// <summary>
    /// Dữ kiện của một lượt xử lý DKCH, dùng để (1) chọn case trong tab2config.json và
    /// (2) thay placeholder trong result/actRecommend.
    /// </summary>
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public sealed class DkchResultContext
    {
        /// <summary>"beforeSave" hoặc "afterSave".</summary>
        public string Phase { get; set; } = "beforeSave";

        /// <summary>
        /// pending | blocked | skipped | skippedInSession | success | failed | unverified |
        /// noData | modeSwitch | modeSwitchFailed | error
        /// </summary>
        public string Outcome { get; set; } = "";

        /// <summary>"DKCH1" hoặc "DKCH2" — mode đang dùng khi bấm Lưu.</summary>
        public string Mode { get; set; } = "DKCH1";

        public string Waybill { get; set; } = "";
        public string JmsCode { get; set; } = "";
        public string JmsMessage { get; set; } = "";
        public string JmsRawMessage { get; set; } = "";
        public string Ratio { get; set; } = "";
        public string ErrorMessage { get; set; } = "";

        public int RegisterCount { get; set; }
        public int RedeliverCount { get; set; }
        public int DispatchCount { get; set; }

        public string LastAction { get; set; } = "";
        public string LastActionType { get; set; } = "";
        public string LastActionTime { get; set; } = "";

        /// <summary>Ghi chú/nguyên nhân của thao tác cuối cùng (remark1) — dòng 2 của ô kết quả.</summary>
        public string LastActionNote { get; set; } = "";
        public string ProblemScanTime { get; set; } = "";
        public string ProblemScanReason { get; set; } = "";

        /// <summary>
        /// true khi đã đọc được lịch sử hành trình — chỉ khi đó số lần ĐKCH / phát lại mới có nghĩa
        /// (nếu không sẽ hiển thị "0 · 0" gây hiểu nhầm).
        /// </summary>
        public bool HasJourney { get; set; }

        public bool HasRegistered => RegisterCount > 0;
        public bool HasRedelivered => RedeliverCount > 0;
    }

    /// <summary>Kết quả sau khi tra tab2config.json: các dòng để hiển thị.</summary>
    public sealed class DkchResultText
    {
        public string CaseId { get; set; } = "";
        public string Result { get; set; } = "";
        public string ActRecommend { get; set; } = "";

        /// <summary>Số lần ĐKCH + số lần phát lại, gộp trên 1 dòng. Rỗng thì không vẽ.</summary>
        public string Stats { get; set; } = "";
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public sealed class Tab2Match
    {
        [JsonPropertyName("phase")] public string Phase { get; set; } = "any";
        [JsonPropertyName("outcomes")] public List<string> Outcomes { get; set; } = new();
        [JsonPropertyName("modes")] public List<string> Modes { get; set; } = new();
        [JsonPropertyName("jmsCodes")] public List<string> JmsCodes { get; set; } = new();
        [JsonPropertyName("msgContains")] public List<string> MsgContains { get; set; } = new();
        [JsonPropertyName("registered")] public string Registered { get; set; } = "any";
        [JsonPropertyName("redelivered")] public string Redelivered { get; set; } = "any";
        [JsonPropertyName("lastActionContains")] public List<string> LastActionContains { get; set; } = new();
        [JsonPropertyName("problemScanAfter")] public string ProblemScanAfter { get; set; } = "";
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public sealed class Tab2Case
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        [JsonPropertyName("group")] public string Group { get; set; } = "";
        [JsonPropertyName("note")] public string Note { get; set; } = "";
        [JsonPropertyName("match")] public Tab2Match Match { get; set; } = new();
        [JsonPropertyName("result")] public string Result { get; set; } = "";
        [JsonPropertyName("actRecommend")] public string ActRecommend { get; set; } = "";
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public sealed class Tab2Fallback
    {
        [JsonPropertyName("result")] public string Result { get; set; } = "{message}";
        [JsonPropertyName("actRecommend")] public string ActRecommend { get; set; } = "";
    }

    /// <summary>
    /// Cấu hình nội dung ô kết quả tabDKCH_result, đọc từ <c>modules/tab2config.json</c>.
    /// <para>
    /// Ưu tiên bản người dùng sửa được ở <c>{InstallRoot}\AppData\modules\tab2config.json</c>;
    /// nếu chưa có thì đọc bản ship kèm app ở <c>{InstallDir}\modules\tab2config.json</c>.
    /// Phải có fallback này vì <c>AppPaths.MigrateBundledDataIfNeeded</c> chỉ copy thư mục
    /// modules khi <c>modules-cache.json</c> chưa tồn tại — máy đã chạy app một lần sẽ không
    /// bao giờ được copy file mới.
    /// </para>
    /// </summary>
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public sealed class Tab2Config
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
        [JsonPropertyName("actionPrefix")] public string ActionPrefix { get; set; } = "→ ";

        /// <summary>
        /// Dòng thống kê cuối ô kết quả — cả hai con số nằm trên CÙNG 1 dòng. Rỗng thì không vẽ.
        /// Chỉ vẽ khi đã đọc được lịch sử hành trình.
        /// </summary>
        [JsonPropertyName("statsLine")]
        public string StatsLine { get; set; } = "Số lần đã ĐKCH: {registerCount}   ·   Số lần phát lại: {redeliverCount}";

        /// <summary>
        /// Nhãn của dropdown "Loại đơn" trên trang JMS, theo từng mode.
        /// <para>
        /// JMS ĐÃ TỪNG đổi nhãn này (DKCH1: "Chuyển hoàn" → "Từ chối"). Vì vậy nó nằm ở đây chứ
        /// không hardcode trong code: mỗi mode nhận một DANH SÁCH nhãn, app thử lần lượt và dùng
        /// nhãn nào đang có trên trang. Nhờ vậy JMS đổi tên là chỉ cần sửa file này, không build lại;
        /// và để cả tên cũ lẫn tên mới thì bản build hiện tại chạy được ở cả hai phía.
        /// </para>
        /// </summary>
        [JsonPropertyName("dropdownOptions")]
        public Dictionary<string, List<string>> DropdownOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Nhãn/tooltip của nút "Lưu và thêm mới". Trang JMS chạy được ở tiếng Việt VÀ tiếng Trung
        /// nên phải liệt kê cả hai; app khớp theo <c>title</c> hoặc chữ hiển thị trên nút.
        /// </summary>
        [JsonPropertyName("saveButtonTitles")]
        public List<string> SaveButtonTitles { get; set; } = new();

        /// <summary>
        /// Các panel cần thu gọn trước khi điền mã (khớp theo chuỗi con của tiêu đề panel).
        /// Liệt kê cả tiếng Việt và tiếng Trung.
        /// </summary>
        [JsonPropertyName("collapseHeaders")]
        public List<string> CollapseHeaders { get; set; } = new();

        /// <summary>
        /// Từ khoá nhận diện thông điệp JMS, theo nhóm: <c>needDkch1</c>, <c>needDkch2</c>,
        /// <c>noData</c>. Mỗi nhóm là danh sách chuỗi con, liệt kê cả tiếng Việt và tiếng Trung.
        /// </summary>
        [JsonPropertyName("jmsMessages")]
        public Dictionary<string, List<string>> JmsMessages { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("cases")] public List<Tab2Case> Cases { get; set; } = new();
        [JsonPropertyName("fallback")] public Tab2Fallback Fallback { get; set; } = new();

        /// <summary>
        /// Nhãn mặc định nếu file cấu hình thiếu — giữ CẢ HAI ngôn ngữ và cả tên cũ.
        /// 退回 = "Từ chối"/"Chuyển hoàn"; 二次退件 = "Chuyển hoàn lần 2".
        /// </summary>
        private static readonly Dictionary<string, string[]> DefaultDropdownOptions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["DKCH1"] = new[] { "Từ chối", "Chuyển hoàn", "退回" },
                ["DKCH2"] = new[] { "Chuyển hoàn lần 2", "Từ chối lần 2", "二次退件" }
            };

        private static readonly string[] DefaultSaveButtonTitles =
            { "Lưu và thêm mới", "保存并新增" };

        private static readonly string[] DefaultCollapseHeaders =
        {
            "Thông tin người gửi", "hóa đơn gốc", "đơn hàng mới",
            "原单收寄件人信息", "新单收寄件人信息"
        };

        private static readonly Dictionary<string, string[]> DefaultJmsMessages =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["needDkch1"] = new[] { "Chưa có", "hoàn lần 2", "二次退件", "已登记" },
                ["needDkch2"] = new[] { "再次登记" },
                ["noData"] = new[]
                {
                    "không có dữ liệu", "Vận đơn không tồn tại",
                    "没有数据", "无数据", "运单不存在", "单号不存在"
                }
            };

        /// <summary>Nhãn nút Lưu cần thử, luôn có ít nhất bản mặc định 2 ngôn ngữ.</summary>
        public List<string> SaveButtonTitleList() => Merge(SaveButtonTitles, DefaultSaveButtonTitles);

        /// <summary>Tiêu đề panel cần thu gọn, luôn có ít nhất bản mặc định 2 ngôn ngữ.</summary>
        public List<string> CollapseHeaderList() => Merge(CollapseHeaders, DefaultCollapseHeaders);

        /// <summary>
        /// Từ khoá nhận diện thông điệp JMS cho nhóm <paramref name="key"/>.
        /// Cấu hình được GỘP với bản mặc định chứ không thay thế: người dùng thêm chuỗi mới cho
        /// ngôn ngữ/phiên bản JMS khác mà không vô tình làm mất các chuỗi đã hoạt động.
        /// </summary>
        public List<string> MessageKeys(string key)
        {
            List<string>? configured = null;
            if (JmsMessages != null) JmsMessages.TryGetValue(key ?? "", out configured);

            DefaultJmsMessages.TryGetValue(key ?? "", out var defaults);
            return Merge(configured, defaults ?? Array.Empty<string>());
        }

        /// <summary>Dựng lại Dictionary với comparer không phân biệt chữ hoa/thường.</summary>
        private static Dictionary<string, List<string>> Normalize(Dictionary<string, List<string>> source)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (source == null) return result;

            foreach (var kv in source)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                result[kv.Key.Trim()] = kv.Value ?? new List<string>();
            }
            return result;
        }

        private static List<string> Merge(List<string>? configured, string[] defaults)
        {
            var result = new List<string>();
            void Add(IEnumerable<string> items)
            {
                if (items == null) return;
                foreach (var raw in items)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    string v = raw.Trim();
                    if (!result.Any(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase)))
                        result.Add(v);
                }
            }

            Add(configured);   // cấu hình đứng trước để giữ thứ tự ưu tiên của người dùng
            Add(defaults);
            return result;
        }

        /// <summary>
        /// Danh sách nhãn dropdown cần thử cho mode <paramref name="modeKey"/> ("DKCH1"/"DKCH2"),
        /// theo đúng thứ tự trong cấu hình. Không bao giờ trả về danh sách rỗng.
        /// </summary>
        public List<string> DropdownOptionsFor(string modeKey)
        {
            string key = string.Equals(modeKey, "DKCH2", StringComparison.OrdinalIgnoreCase) ? "DKCH2" : "DKCH1";

            List<string>? configured = null;
            if (DropdownOptions != null) DropdownOptions.TryGetValue(key, out configured);

            // GỘP với mặc định (giống 3 khoá còn lại) chứ không thay thế: điền nhãn mới cho một
            // ngôn ngữ mà không vô tình làm mất nhãn của ngôn ngữ kia.
            return Merge(configured, DefaultDropdownOptions[key]);
        }

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static readonly object Gate = new object();
        private static Tab2Config? _current;

        private static string UserPath => Path.Combine(AppPaths.ModulesCacheDir, "modules", "tab2config.json");
        private static string BundledPath => AppPaths.InstallResource(Path.Combine("modules", "tab2config.json"));

        /// <summary>Bản đang dùng (đọc 1 lần rồi cache). Không bao giờ trả null.</summary>
        public static Tab2Config Current
        {
            get
            {
                lock (Gate) { return _current ??= Load(); }
            }
        }

        /// <summary>Buộc đọc lại file — dùng khi người dùng vừa sửa tab2config.json.</summary>
        public static void Reload()
        {
            lock (Gate) { _current = null; }
        }

        /// <summary>
        /// Thứ tự đọc: file nào có thời điểm SỬA MỚI HƠN thì thắng.
        /// <para>
        /// Bản trước LUÔN ưu tiên bản ở AppData. Hệ quả: máy đã từng chạy app sẽ giữ mãi bản
        /// AppData cũ, mọi thay đổi ship kèm bản build mới bị bỏ qua âm thầm — đúng lỗi "Newbie trả
        /// sai kết quả" (bản cũ có <c>result: "{lastAction}"</c> nên dòng 3 lặp lại dòng 1, và lấy
        /// đề xuất của case success thay vì case lỗi).
        /// </para>
        /// <para>
        /// So theo thời điểm sửa giải quyết cả hai chiều: ship build mới → file trong InstallDir mới
        /// hơn nên thắng; người dùng tự sửa bản AppData → bản đó mới hơn nên thắng.
        /// </para>
        /// </summary>
        private static IEnumerable<string> CandidatePathsNewestFirst()
        {
            var found = new List<(string Path, DateTime Stamp)>();

            foreach (var path in new[] { UserPath, BundledPath })
            {
                try
                {
                    if (File.Exists(path)) found.Add((path, File.GetLastWriteTimeUtc(path)));
                }
                catch { /* đường dẫn không đọc được thì bỏ qua */ }
            }

            return found.OrderByDescending(x => x.Stamp).Select(x => x.Path);
        }

        private static Tab2Config Load()
        {
            foreach (var path in CandidatePathsNewestFirst())
            {
                try
                {
                    if (!File.Exists(path)) continue;

                    var cfg = JsonSerializer.Deserialize<Tab2Config>(File.ReadAllText(path), JsonOpts);
                    if (cfg == null) continue;

                    cfg.Cases ??= new List<Tab2Case>();
                    cfg.Fallback ??= new Tab2Fallback();
                    foreach (var c in cfg.Cases) c.Match ??= new Tab2Match();

                    // System.Text.Json THAY THẾ instance Dictionary nên comparer OrdinalIgnoreCase
                    // khai báo ở field bị mất. Dựng lại để "dkch1"/"nodata" trong file người dùng
                    // vẫn khớp, không im lặng bỏ qua.
                    cfg.DropdownOptions = Normalize(cfg.DropdownOptions);
                    cfg.JmsMessages = Normalize(cfg.JmsMessages);
                    cfg.SaveButtonTitles ??= new List<string>();
                    cfg.CollapseHeaders ??= new List<string>();

                    AppLogger.Info($"Tab2Config: loaded {cfg.Cases.Count} case(s) from {path} " +
                                   $"(sửa lúc {File.GetLastWriteTime(path):yyyy-MM-dd HH:mm:ss})");
                    return cfg;
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"Tab2Config: load failed ({path}): {ex.Message}");
                }
            }

            AppLogger.Warning("Tab2Config: no config found, using built-in defaults.");
            return Default();
        }

        /// <summary>
        /// Mặc định tối thiểu khi thiếu file — giữ app hoạt động chứ không để ô kết quả trắng.
        /// </summary>
        public static Tab2Config Default() => new Tab2Config
        {
            Cases = new List<Tab2Case>
            {
                new Tab2Case { Id = "success", Result = "Đã đăng ký chuyển hoàn.",
                    ActRecommend = "Kiểm tồn kho.",
                    Match = new Tab2Match { Outcomes = { "success" } } },
                new Tab2Case { Id = "blocked-violation",
                    Result = "Chưa quét kiện vấn đề, vi phạm tự ý chuyển hoàn, chặn đăng ký chuyển hoàn.",
                    Match = new Tab2Match { Outcomes = { "blockedViolation" } } },
                new Tab2Case { Id = "blocked", Result = "Chưa quét kiện vấn đề, chặn đăng ký chuyển hoàn.",
                    Match = new Tab2Match { Outcomes = { "blocked" } } },
                new Tab2Case { Id = "failed", Result = "{message}",
                    Match = new Tab2Match { Outcomes = { "failed" } } }
            }
        };

        /// <summary>Chọn case đầu tiên khớp rồi thay placeholder. Không khớp thì dùng fallback.</summary>
        public DkchResultText Resolve(DkchResultContext ctx)
        {
            if (ctx == null) return new DkchResultText();

            string stats = ctx.HasJourney ? Format(StatsLine, ctx) : "";

            foreach (var c in Cases)
            {
                if (c == null || !c.Enabled) continue;
                if (!Matches(c.Match, ctx)) continue;

                return new DkchResultText
                {
                    CaseId = c.Id ?? "",
                    Result = Format(c.Result, ctx),
                    ActRecommend = Format(c.ActRecommend, ctx),
                    Stats = stats
                };
            }

            return new DkchResultText
            {
                CaseId = "fallback",
                Result = Format(Fallback.Result, ctx),
                ActRecommend = Format(Fallback.ActRecommend, ctx),
                Stats = stats
            };
        }

        internal static bool Matches(Tab2Match m, DkchResultContext ctx)
        {
            if (m == null) return true;

            if (!IsAny(m.Phase) && !Eq(m.Phase, ctx.Phase)) return false;

            if (HasAny(m.Outcomes) &&
                !m.Outcomes.Any(o => Eq(o, ctx.Outcome))) return false;

            if (HasAny(m.Modes) &&
                !m.Modes.Any(x => Eq(x, ctx.Mode))) return false;

            if (HasAny(m.JmsCodes) &&
                !m.JmsCodes.Any(x => Eq(x, ctx.JmsCode))) return false;

            if (HasAny(m.MsgContains) &&
                !m.MsgContains.Any(x => Contains(ctx.JmsRawMessage, x) || Contains(ctx.JmsMessage, x)))
                return false;

            if (!MatchesTriState(m.Registered, ctx.HasRegistered)) return false;
            if (!MatchesTriState(m.Redelivered, ctx.HasRedelivered)) return false;

            if (HasAny(m.LastActionContains) &&
                !m.LastActionContains.Any(x => Contains(ctx.LastActionType, x) || Contains(ctx.LastAction, x)))
                return false;

            if (!string.IsNullOrWhiteSpace(m.ProblemScanAfter) &&
                !IsAfterTimeOfDay(ctx.ProblemScanTime, m.ProblemScanAfter)) return false;

            return true;
        }

        private static bool MatchesTriState(string rule, bool actual)
        {
            if (IsAny(rule)) return true;
            if (Eq(rule, "yes") || Eq(rule, "true")) return actual;
            if (Eq(rule, "no") || Eq(rule, "false")) return !actual;
            return true;    // giá trị lạ → không lọc, tránh chặn oan vì cấu hình sai
        }

        /// <summary>true nếu <paramref name="timestamp"/> có giờ muộn hơn mốc "HH:mm".</summary>
        internal static bool IsAfterTimeOfDay(string timestamp, string cutoff)
        {
            if (string.IsNullOrWhiteSpace(timestamp)) return false;
            if (!DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var when))
                return false;
            if (!TimeSpan.TryParse(cutoff, CultureInfo.InvariantCulture, out var limit))
                return false;
            return when.TimeOfDay > limit;
        }

        internal static string Format(string template, DkchResultContext ctx)
        {
            if (string.IsNullOrEmpty(template)) return "";

            string message = !string.IsNullOrWhiteSpace(ctx.JmsMessage) ? ctx.JmsMessage : ctx.JmsRawMessage;

            return template
                .Replace("{waybill}", ctx.Waybill ?? "")
                .Replace("{ratio}", ctx.Ratio ?? "")
                .Replace("{message}", message ?? "")
                .Replace("{rawMessage}", ctx.JmsRawMessage ?? "")
                .Replace("{code}", ctx.JmsCode ?? "")
                .Replace("{mode}", ctx.Mode ?? "")
                .Replace("{lastAction}", ctx.LastAction ?? "")
                .Replace("{lastActionType}", ctx.LastActionType ?? "")
                .Replace("{lastActionTime}", ctx.LastActionTime ?? "")
                .Replace("{registerCount}", ctx.RegisterCount.ToString(CultureInfo.InvariantCulture))
                .Replace("{redeliverCount}", ctx.RedeliverCount.ToString(CultureInfo.InvariantCulture))
                .Replace("{dispatchCount}", ctx.DispatchCount.ToString(CultureInfo.InvariantCulture))
                .Replace("{problemScanTime}", ctx.ProblemScanTime ?? "")
                .Replace("{problemScanReason}", ctx.ProblemScanReason ?? "")
                .Replace("{errorMessage}", ctx.ErrorMessage ?? "")
                .Trim();
        }

        private static bool IsAny(string? s)
            => string.IsNullOrWhiteSpace(s) || Eq(s, "any") || Eq(s, "*");

        private static bool HasAny(List<string>? list)
            => list != null && list.Any(x => !string.IsNullOrWhiteSpace(x));

        private static bool Eq(string? a, string? b)
            => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        private static bool Contains(string? haystack, string? needle)
            => !string.IsNullOrEmpty(haystack) && !string.IsNullOrWhiteSpace(needle)
               && haystack.IndexOf(needle.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
