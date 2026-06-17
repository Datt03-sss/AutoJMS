using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AutoJMS;

public sealed class TabPrintReprintState
{
    public string WaybillNo { get; set; } = "";
    public DateTime? InitialPrintedAt { get; set; }
    public int AutoJmsReprintCount { get; set; }
    public DateTime? LastReprintAt { get; set; }

    public bool HasInitialPrint => InitialPrintedAt.HasValue;
}

public sealed class TabPrintReprintDecision
{
    public bool IsReprint { get; init; }
    public bool CanPrint { get; init; }
    public int AutoJmsReprintCount { get; init; }
    public string BlockMessage { get; init; } = "";
}

public sealed class TabPrintReprintPolicy
{
    public const int MaxReprintsPerWaybill = 5;

    public bool CanReprint(int autoJmsReprintCount)
        => CanReprint(autoJmsReprintCount, MaxReprintsPerWaybill);

    public bool CanReprint(int autoJmsReprintCount, int maxReprints)
        => autoJmsReprintCount < Math.Max(0, maxReprints);

    public TabPrintReprintDecision Evaluate(TabPrintReprintState state, int jmsPrintCount)
        => Evaluate(state, jmsPrintCount, MaxReprintsPerWaybill);

    public TabPrintReprintDecision Evaluate(TabPrintReprintState state, int jmsPrintCount, int maxReprints)
    {
        state ??= new TabPrintReprintState();
        bool isReprint = jmsPrintCount > 0 || state.HasInitialPrint;
        int limit = Math.Max(0, maxReprints);
        bool canPrint = !isReprint || CanReprint(state.AutoJmsReprintCount, limit);

        return new TabPrintReprintDecision
        {
            IsReprint = isReprint,
            CanPrint = canPrint,
            AutoJmsReprintCount = state.AutoJmsReprintCount,
            BlockMessage = canPrint
                ? ""
                : $"Mã vận đơn này đã in lại {limit} lần trong AutoJMS.\r\nMuốn in thêm, vui lòng in thủ công trên hệ thống JMS."
        };
    }
}

public sealed class TabPrintReprintStore
{
    private readonly string _path;
    private readonly object _sync = new();
    private readonly Dictionary<string, TabPrintReprintState> _states = new(StringComparer.OrdinalIgnoreCase);

    public TabPrintReprintStore()
        : this(Path.Combine(AppPaths.LogsDir, "tab-print-reprints.tsv"))
    {
    }

    public TabPrintReprintStore(string path)
    {
        _path = path;
        Load();
    }

    public TabPrintReprintState Get(string waybillNo)
    {
        string normalized = Normalize(waybillNo);
        lock (_sync)
        {
            if (_states.TryGetValue(normalized, out var state))
                return Clone(state);

            return new TabPrintReprintState { WaybillNo = normalized };
        }
    }

    public void RecordSuccessfulPrint(string waybillNo, bool isReprint)
    {
        string normalized = Normalize(waybillNo);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        lock (_sync)
        {
            if (!_states.TryGetValue(normalized, out var state))
            {
                state = new TabPrintReprintState { WaybillNo = normalized };
                _states[normalized] = state;
            }

            DateTime now = DateTime.Now;
            state.InitialPrintedAt ??= now;
            if (isReprint)
            {
                state.AutoJmsReprintCount = Math.Max(0, state.AutoJmsReprintCount) + 1;
                state.LastReprintAt = now;
            }

            SaveLocked();
        }
    }

    private void Load()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? AppPaths.LogsDir);
            if (!File.Exists(_path))
                return;

            foreach (string line in File.ReadLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("waybill_no\t", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] parts = line.Split('\t');
                if (parts.Length < 4)
                    continue;

                string waybillNo = Normalize(parts[0]);
                if (string.IsNullOrWhiteSpace(waybillNo))
                    continue;

                _states[waybillNo] = new TabPrintReprintState
                {
                    WaybillNo = waybillNo,
                    InitialPrintedAt = ParseDate(parts[1]),
                    AutoJmsReprintCount = int.TryParse(parts[2], out int count) ? Math.Max(0, count) : 0,
                    LastReprintAt = ParseDate(parts[3])
                };
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"[TabPrintReprint] load failed: {ex.Message}");
        }
    }

    private void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? AppPaths.LogsDir);
            string tmp = _path + ".tmp";
            var lines = new List<string>
            {
                "waybill_no\tinitial_printed_at\tautojms_reprint_count\tlast_reprint_at"
            };

            lines.AddRange(_states.Values
                .OrderBy(x => x.WaybillNo, StringComparer.OrdinalIgnoreCase)
                .Select(x => string.Join("\t",
                    x.WaybillNo,
                    FormatDate(x.InitialPrintedAt),
                    x.AutoJmsReprintCount.ToString(CultureInfo.InvariantCulture),
                    FormatDate(x.LastReprintAt))));

            File.WriteAllLines(tmp, lines);
            if (File.Exists(_path))
                File.Replace(tmp, _path, null);
            else
                File.Move(tmp, _path);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"[TabPrintReprint] save failed: {ex.Message}");
        }
    }

    private static TabPrintReprintState Clone(TabPrintReprintState state)
        => new()
        {
            WaybillNo = state.WaybillNo,
            InitialPrintedAt = state.InitialPrintedAt,
            AutoJmsReprintCount = state.AutoJmsReprintCount,
            LastReprintAt = state.LastReprintAt
        };

    private static string Normalize(string waybillNo)
    {
        string normalized = (waybillNo ?? "").Trim().ToUpperInvariant();
        int hyphen = normalized.IndexOf('-');
        return hyphen > 0 ? normalized.Substring(0, hyphen) : normalized;
    }

    private static DateTime? ParseDate(string value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;

    private static string FormatDate(DateTime? value)
        => value?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";
}
