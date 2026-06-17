using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AutoJMS.FullStack.Services
{
    public static class JourneyTextNormalizer
    {
        private static readonly Dictionary<string, string> ActionTypeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["快件签收"] = "Ký nhận",
            ["派件电联"] = "Lịch sử cuộc gọi-phát",
            ["出仓扫描"] = "Quét phát hàng",
            ["问题件扫描"] = "Quét kiện vấn đề",
            ["库存盘点"] = "Kiểm tra hàng tồn kho",
            ["退件登记"] = "Đăng ký chuyển hoàn",
            ["登记转退"] = "Đăng ký chuyển hoàn",
            ["正在退件"] = "Đang chuyển hoàn",
            ["转退"] = "Đang chuyển hoàn",
            ["打印退件"] = "In đơn chuyển hoàn"
        };

        private static readonly Dictionary<string, string> ScanSourceMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PC端"] = "JMS-PC",
            ["巴枪app"] = "APP IData",
            ["移动端"] = "APP",
            ["APP IData"] = "APP IData",
            ["JMS-PC"] = "JMS-PC"
        };

        public static string NormalizeActionType(string raw)
        {
            var text = Clean(raw);
            if (text == "--")
                return "--";

            if (ActionTypeMap.TryGetValue(text, out var direct))
                return direct;

            foreach (var pair in ActionTypeMap)
            {
                if (text.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return pair.Value;
            }

            return ContainsChinese(text) ? "Thao tác vận chuyển" : text;
        }

        public static string NormalizeScanSource(string raw)
        {
            var text = Clean(raw);
            if (text == "--")
                return "--";

            if (ScanSourceMap.TryGetValue(text, out var direct))
                return direct;

            foreach (var pair in ScanSourceMap)
            {
                if (text.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return pair.Value;
            }

            return ContainsChinese(text) ? "--" : text;
        }

        public static string NormalizeDescription(string raw)
        {
            var text = Clean(raw);
            if (text == "--")
                return "--";

            foreach (var pair in ActionTypeMap)
                text = text.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase);

            foreach (var pair in ScanSourceMap)
                text = text.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase);

            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "empty", StringComparison.OrdinalIgnoreCase))
                return "--";
            return Regex.Replace(value.Trim(), @"\s+", " ");
        }

        private static bool ContainsChinese(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            foreach (var ch in text)
            {
                if ((ch >= '\u3400' && ch <= '\u4DBF') ||
                    (ch >= '\u4E00' && ch <= '\u9FFF') ||
                    (ch >= '\uF900' && ch <= '\uFAFF'))
                    return true;
            }

            return false;
        }
    }
}
