using AutoJMS.Data;
using System;

namespace AutoJMS.FullStack.Services
{
    public sealed class FullStackOrderStateEngine
    {
        public string DeriveState(WaybillDbModel row, bool isInCurrentInventory)
        {
            if (row == null) return "Unknown";

            string action = row.ThaoTacCuoi ?? string.Empty;
            string status = row.TrangThaiHienTai ?? string.Empty;

            if (!isInCurrentInventory)
            {
                if (IsClosed(action, status)) return "Closed";
                return "LeftInventory";
            }

            if (IsClosed(action, status)) return "Delivered";
            if (ContainsAny(action, "Xác nhận chuyển hoàn thành công", "Đã trả lại cho người gửi")) return "Closed";
            if (ContainsAny(action, "In đơn chuyển hoàn", "Yêu cầu trả hàng", "Xác nhận chuyển hoàn")) return "WaitingReturn";
            if (ContainsAny(action, "Chuyển hoàn", "Returning")) return "Returning";
            if (ContainsAny(action, "vấn đề", "Kiện vấn đề") || !string.IsNullOrWhiteSpace(row.NguyenNhanKienVanDe) && row.NguyenNhanKienVanDe != "empty") return "DeliveryFailed";
            if (ContainsAny(action, "Đang phát hàng", "Quét phát hàng", "Giao lại hàng")) return "OutForDelivery";
            if (ContainsAny(action, "Xuống hàng kiện đến", "Xuống kiện", "卸车到件", "到件")) return "PendingDeliveryScan";

            if (DateTime.TryParse(row.ThoiGianThaoTac, out var lastAction))
            {
                var stoppedDays = (DateTime.Now - lastAction).TotalDays;
                if (stoppedDays >= 7) return "LostRisk";
                if (stoppedDays >= 3) return "StoppedLongTime";
            }

            if (string.IsNullOrWhiteSpace(action) || action == "empty") return "NewArrival";
            return "Unknown";
        }

        private static bool IsClosed(string action, string status) =>
            ContainsAny(action, "Ký nhận", "签收") || ContainsAny(status, "Ký nhận", "签收");

        private static bool ContainsAny(string value, params string[] needles)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            foreach (var needle in needles)
            {
                if (value.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
