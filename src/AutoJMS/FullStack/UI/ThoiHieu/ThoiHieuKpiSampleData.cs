using System;
using System.Collections.Generic;

namespace AutoJMS.FullStack.UI.ThoiHieu
{
    public static class ThoiHieuKpiSampleData
    {
        public static ThoiHieuKpiSheetData Create()
        {
            string[] names =
            {
                "Bùi Việt Anh",
                "Cáp Tuấn Anh",
                "Đào Văn Long",
                "Đặng Thị Hồng",
                "Đặng Thị Ngọc Hoa NVCP",
                "Đinh Hoàng Anh Đức",
                "Đỗ Xuân Nghị",
                "Hà A Trình",
                "Lê Quang Vinh",
                "Lê Thế Anh NVCP",
                "Mai Văn Quyền",
                "Ngô Thị Yến NVCP",
                "Nguyễn Anh Tuấn NVCP",
                "Nguyễn Duy Tuyến",
                "Nguyễn Đức Tiến",
                "Nguyễn Quốc Bảo Lâm",
                "Nguyễn Tùng Lâm",
                "Phạm Minh Trí",
                "Phạm Quang Trung",
                "Phạm Tuấn Anh",
                "Trần Tiến Anh",
                "Trần Thị Phương Anh",
                "Trần Văn Hùng",
                "Trương Thị Mai NVCP",
                "Vi Ngọc Kỳ",
                "Vũ Kiều Oanh",
                "Vũ Ngọc Hải",
                "Vũ Thị Duyên",
                "Vũ Thị Hồng Mến NVCP"
            };

            int[] delivery =
            {
                110, 105, 103, 36, 75, 83, 121, 114, 123, 103,
                98, 111, 124, 106, 74, 136, 93, 27, 70, 93,
                155, 89, 109, 105, 103, 147, 130, 123, 136
            };

            int[] required91 =
            {
                100, 96, 94, 33, 68, 76, 110, 104, 112, 94,
                89, 101, 113, 97, 67, 124, 85, 25, 64, 85,
                141, 81, 99, 96, 94, 134, 118, 112, 124
            };

            int[] required90 =
            {
                99, 95, 93, 32, 68, 75, 109, 103, 111, 93,
                88, 100, 112, 95, 67, 122, 84, 24, 63, 84,
                140, 80, 98, 95, 93, 132, 117, 111, 122
            };

            int[] signed =
            {
                107, 89, 93, 34, 67, 70, 105, 106, 108, 92,
                98, 106, 103, 103, 67, 117, 84, 0, 69, 83,
                138, 86, 95, 95, 100, 131, 120, 118, 133
            };

            var data = new ThoiHieuKpiSheetData
            {
                SiteCode = "214A02",
                Summary = new ThoiHieuKpiSummary
                {
                    NewOrders = 0,
                    ScanTttcCount = 0,
                    NotArrivedCount = 0,
                    TransferCount = 0,
                    NeedDeliveryCount = 3002,
                    NeedKpi91Count = 2732,
                    NeedKpi90Count = 2702
                }
            };

            for (int i = 0; i < names.Length; i++)
            {
                data.Rows.Add(new ThoiHieuEmployeeRow
                {
                    Stt = i + 1,
                    SupervisorName = "Vũ Đức Toàn",
                    SiteCode = "214A02",
                    EmployeeCode = "",
                    EmployeeName = names[i],
                    DeliveryCount = delivery[i],
                    Required91 = required91[i],
                    Required90 = required90[i],
                    SignedCount = signed[i],
                    SignedRate = delivery[i] > 0 ? signed[i] / (decimal)delivery[i] : 0m,
                    HourlySigned = CreateHourlyPattern(i, signed[i])
                });
            }

            return data;
        }

        private static Dictionary<int, int?> CreateHourlyPattern(int rowIndex, int signedCount)
        {
            var hours = new Dictionary<int, int?>();
            for (int hour = 8; hour <= 24; hour++)
            {
                if (signedCount <= 0)
                {
                    hours[hour] = null;
                    continue;
                }

                bool blank = ((rowIndex + hour) % 5 == 0) || ((rowIndex * 3 + hour) % 11 == 0);
                if (blank)
                {
                    hours[hour] = null;
                    continue;
                }

                int baseValue = ((rowIndex + 1) * (hour - 6) * 7 + hour * 3) % 14 + 1;
                if (hour == 20 && rowIndex % 3 == 0) baseValue = 22 + rowIndex % 18;
                if (hour == 21 && rowIndex % 4 == 0) baseValue = 18 + rowIndex % 25;
                if (hour == 18 && rowIndex % 5 == 0) baseValue = 20 + rowIndex % 30;
                if (hour == 20 && rowIndex is 24 or 25) baseValue = 52 + rowIndex % 11;
                if (hour == 21 && rowIndex is 1 or 22) baseValue = 60 + rowIndex % 6;

                hours[hour] = Math.Min(baseValue, Math.Max(1, signedCount));
            }

            return hours;
        }
    }
}
