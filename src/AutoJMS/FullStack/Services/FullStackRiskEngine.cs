using AutoJMS.Data;
using System;
using System.Collections.Generic;

namespace AutoJMS.FullStack.Services
{
    public sealed class FullStackRiskResult
    {
        public int Score { get; set; }
        public string Level { get; set; } = "LOW";
        public string Reasons { get; set; } = string.Empty;
    }

    public sealed class FullStackRiskEngine
    {
        public FullStackRiskResult Evaluate(WaybillDbModel row, string state, DateTime? firstSeenAt, bool isInCurrentInventory)
        {
            var reasons = new List<string>();
            int score = 0;
            var now = DateTime.Now;

            DateTime? lastActionAt = TryParse(row?.ThoiGianThaoTac);
            double inventoryHours = firstSeenAt.HasValue ? (now - firstSeenAt.Value).TotalHours : 0;
            double stoppedDays = lastActionAt.HasValue ? (now - lastActionAt.Value).TotalDays : 0;

            if (!isInCurrentInventory)
            {
                score += 10;
                reasons.Add("Đã rời tồn kho");
            }

            if (inventoryHours > 24 && state == "PendingDeliveryScan")
            {
                score += 35;
                reasons.Add("Tồn >24h chưa quét phát");
            }

            if (inventoryHours > 48 && !IsFinal(row))
            {
                score += 45;
                reasons.Add("Tồn >48h chưa xử lý xong");
            }

            if (stoppedDays >= 1 && !IsFinal(row))
            {
                score += 20;
                reasons.Add("Hành trình đứng >1 ngày");
            }

            if (stoppedDays >= 3 && !IsFinal(row))
            {
                score += 35;
                reasons.Add("Hành trình đứng >3 ngày");
            }

            if (stoppedDays >= 7 && !IsFinal(row))
            {
                score += 60;
                reasons.Add("Hành trình đứng >7 ngày");
            }

            if (state == "DeliveryFailed")
            {
                score += 40;
                reasons.Add("Giao thất bại/kiện vấn đề");
            }

            if (state == "WaitingReturn")
            {
                score += 25;
                reasons.Add("Chờ hoàn cần xử lý");
            }

            if (state == "LostRisk")
            {
                score += 60;
                reasons.Add("Nguy cơ thất lạc");
            }

            string level = score >= 90 ? "CRITICAL" : score >= 60 ? "HIGH" : score >= 30 ? "MEDIUM" : "LOW";
            return new FullStackRiskResult
            {
                Score = Math.Min(score, 100),
                Level = level,
                Reasons = string.Join("; ", reasons)
            };
        }

        private static DateTime? TryParse(string value) =>
            DateTime.TryParse(value, out var dt) ? dt : null;

        private static bool IsFinal(WaybillDbModel row)
        {
            string action = row?.ThaoTacCuoi ?? string.Empty;
            return action.Contains("Ký nhận", StringComparison.OrdinalIgnoreCase)
                || action.Contains("Xác nhận chuyển hoàn thành công", StringComparison.OrdinalIgnoreCase)
                || action.Contains("Đã trả lại cho người gửi", StringComparison.OrdinalIgnoreCase);
        }
    }
}
