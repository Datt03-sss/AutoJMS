using AutoJMS.FullStack.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace AutoJMS.FullStack.Services
{
    public sealed class WaybillJourneyParseMismatchException : Exception
    {
        public WaybillJourneyParseMismatchException(string message) : base(message)
        {
        }
    }

    public sealed class WaybillJourneyJsonParser : IWaybillJourneyParser
    {
        public WaybillJourneyViewModel ParseDetails(string waybillNo, string rawJson)
        {
            waybillNo = NormalizeWaybill(waybillNo);
            if (string.IsNullOrWhiteSpace(waybillNo) || string.IsNullOrWhiteSpace(rawJson))
                return new WaybillJourneyViewModel { WaybillNo = waybillNo };

            using var document = JsonDocument.Parse(rawJson);
            var details = GetMatchedDetails(document.RootElement, waybillNo, out var dataCount, out var matchedKeyword);
            var rows = new List<WaybillJourneyRow>();

            foreach (var detail in details)
            {
                var scanTime = PickText(detail, "scanTime", "eventTime", "operationTime", "createTime");
                var scanTypeName = PickText(detail, "scanTypeName", "operationName", "operationTypeName", "scanType");
                var description = PickDescription(detail, scanTypeName);
                var scanNetworkName = PickText(detail, "scanNetworkName", "networkName", "siteName", "scanSite");
                var scanNetworkCode = PickText(detail, "scanNetworkCode", "networkCode", "siteCode");
                var packageNumber = PickText(detail, "packageNumber", "packageNo", "bagNo", "containerCode");
                var taskCode = PickText(detail, "taskCode", "taskNo");
                var scanSource = PickText(detail, "remark6", "scanSource", "source", "deviceName");
                var imgType = GetNullableInt(detail, "imgType");

                rows.Add(new WaybillJourneyRow
                {
                    ActionTime = ParseNullableDate(scanTime),
                    ActionType = JourneyTextNormalizer.NormalizeActionType(scanTypeName),
                    Description = CleanText(description),
                    SiteInfo = BuildSite(scanNetworkName, scanNetworkCode),
                    ContainerCode = FirstNonEmpty(packageNumber, taskCode),
                    ScanSource = JourneyTextNormalizer.NormalizeScanSource(scanSource),
                    Weight = Clean(PickText(detail, "weight")),
                    ConvertedWeight = Clean(PickText(detail, "convertedWeight", "volume")),
                    AttachmentText = imgType.HasValue ? "Xem" : "--",
                    Severity = ClassifyJourneySeverity(detail),
                    SiteCode = Clean(scanNetworkCode),
                    SiteName = Clean(scanNetworkName),
                    OperatorCode = Clean(PickText(detail, "scanByCode", "operatorCode", "employeeCode")),
                    OperatorName = Clean(PickText(detail, "scanByName", "operatorName", "employeeName")),
                    PackageNumber = Clean(packageNumber),
                    TaskCode = Clean(taskCode),
                    ImgType = imgType,
                    RawJson = detail.GetRawText()
                });
            }

            rows = rows
                .OrderByDescending(x => x.ActionTime ?? DateTime.MinValue)
                .ToList();
            for (var i = 0; i < rows.Count; i++)
                rows[i].Stt = i + 1;

            return new WaybillJourneyViewModel
            {
                WaybillNo = waybillNo,
                Rows = rows,
                MatchedKeyword = matchedKeyword,
                DataCount = dataCount,
                Source = "Fresh API"
            };
        }

        public WaybillJourneyParseMetadata ReadMetadata(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                return new WaybillJourneyParseMetadata();

            using var document = JsonDocument.Parse(rawJson);
            var eventCount = 0;
            var dataCount = 0;
            if (TryGetProperty(document.RootElement, "data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                dataCount = data.GetArrayLength();
                foreach (var item in data.EnumerateArray())
                {
                    if (TryGetProperty(item, "details", out var details) && details.ValueKind == JsonValueKind.Array)
                        eventCount += details.GetArrayLength();
                }
            }

            return new WaybillJourneyParseMetadata
            {
                ResponseCode = GetNullableInt(document.RootElement, "code"),
                ResponseMessage = Clean(PickText(document.RootElement, "msg", "message"), fallback: string.Empty),
                EventCount = eventCount,
                DataCount = dataCount
            };
        }

        private static IReadOnlyList<JsonElement> GetMatchedDetails(
            JsonElement root,
            string waybillNo,
            out int dataCount,
            out string matchedKeyword)
        {
            matchedKeyword = string.Empty;
            dataCount = 0;

            if (!TryGetProperty(root, "data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
                return Array.Empty<JsonElement>();

            dataCount = data.GetArrayLength();
            foreach (var item in data.EnumerateArray())
            {
                var keyword = NormalizeWaybill(PickText(item, "keyword", "billCode", "waybillNo"));
                var details = TryGetProperty(item, "details", out var detailArray) && detailArray.ValueKind == JsonValueKind.Array
                    ? detailArray.EnumerateArray().ToList()
                    : new List<JsonElement>();

                var keywordMatches = !string.IsNullOrWhiteSpace(keyword) &&
                    string.Equals(keyword, waybillNo, StringComparison.OrdinalIgnoreCase);
                var detailMatches = details.Any(detail => DetailMatchesWaybill(detail, waybillNo));

                if (keywordMatches || detailMatches)
                {
                    matchedKeyword = string.IsNullOrWhiteSpace(keyword) ? waybillNo : keyword;
                    return details;
                }
            }

            throw new WaybillJourneyParseMismatchException("Response không khớp mã vận đơn đang chọn.");
        }

        private static bool DetailMatchesWaybill(JsonElement detail, string waybillNo)
        {
            var values = new[]
            {
                PickText(detail, "waybillNo"),
                PickText(detail, "billCode"),
                PickText(detail, "keyword")
            };

            return values.Any(value =>
                string.Equals(NormalizeWaybill(value), waybillNo, StringComparison.OrdinalIgnoreCase));
        }

        private static string PickDescription(JsonElement detail, string scanTypeName)
        {
            var backendText = PickText(
                detail,
                "descriptionVi",
                "descVi",
                "contentVi",
                "trackContentVi",
                "description",
                "content",
                "trackContent",
                "message",
                "waybillTrackingContent",
                "operationDesc",
                "operationName",
                "scanTypeName");

            if (Clean(backendText) != "--")
                return backendText;

            var action = JourneyTextNormalizer.NormalizeActionType(scanTypeName);
            var site = BuildSite(PickText(detail, "scanNetworkName", "siteName"), PickText(detail, "scanNetworkCode", "siteCode"));
            var operatorName = Clean(PickText(detail, "scanByName", "operatorName", "employeeName"));
            if (site != "--" && operatorName != "--")
                return $"{site} | {action} | {operatorName}";
            if (site != "--")
                return $"{site} | {action}";
            return action == "--" ? "Thao tác vận chuyển" : action;
        }

        private static string ClassifyJourneySeverity(JsonElement detail)
        {
            var text = $"{PickText(detail, "scanTypeName")} {PickText(detail, "code")} {PickText(detail, "remark1")} {PickText(detail, "waybillTrackingContent")} {PickText(detail, "description")} {PickText(detail, "content")}";
            if (ContainsAny(text, "问题", "kiện vấn đề", "Từ chối", "Không liên lạc", "thất bại"))
                return "Exception";
            if (ContainsAny(text, "派件", "đang phát", "giao"))
                return "Delivery";
            if (ContainsAny(text, "chuyển hoàn", "hoàn", "退件", "转退"))
                return "Return";
            if (ContainsAny(text, "库存", "kiểm tra hàng tồn kho"))
                return "Inventory";
            if (ContainsAny(text, "揽收", "Đã lấy hàng"))
                return "Pickup";
            if (ContainsAny(text, "gọi", "thông tin"))
                return "Info";
            return "Scan";
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return values.Any(x => text.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private static string PickText(JsonElement element, params string[] names)
        {
            foreach (var name in names ?? Array.Empty<string>())
            {
                if (!TryGetProperty(element, name, out var value))
                    continue;

                var text = value.ValueKind switch
                {
                    JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                    JsonValueKind.String => value.GetString(),
                    _ => value.ToString()
                };

                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }

            return string.Empty;
        }

        private static int? GetNullableInt(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out var value))
                return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                return number;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                return number;
            return null;
        }

        private static DateTime? ParseNullableDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (DateTime.TryParseExact(
                    value.Trim(),
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out var exact))
                return exact;
            if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var local))
                return local;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var invariant))
                return invariant;
            return null;
        }

        private static string BuildSite(string siteName, string siteCode)
        {
            siteName = Clean(siteName);
            siteCode = Clean(siteCode);
            if (siteName == "--" && siteCode == "--") return "--";
            if (siteName == "--") return siteCode;
            if (siteCode == "--") return siteName;
            return $"{siteName} | {siteCode}";
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values ?? Array.Empty<string>())
            {
                var clean = Clean(value);
                if (clean != "--")
                    return clean;
            }

            return "--";
        }

        private static string CleanText(string value)
        {
            var clean = Clean(value);
            if (clean == "--")
                return clean;
            return string.Join(" ", clean.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }

        private static string Clean(string value, string fallback = "--")
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "empty", StringComparison.OrdinalIgnoreCase))
                return fallback;
            return value.Trim();
        }

        private static string NormalizeWaybill(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
        }
    }
}
