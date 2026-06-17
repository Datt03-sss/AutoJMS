using AutoJMS.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Services
{
    public sealed class FullStackExportService
    {
        public string ExportDirectory => Path.Combine(AppPaths.UserDataDir, "FullStack", "Exports");

        public Task<string> ExportWaybillsCsvAsync(
            IEnumerable<WaybillDbModel> rows,
            string reportName,
            CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                Directory.CreateDirectory(ExportDirectory);
                string safeName = MakeSafeFileName(string.IsNullOrWhiteSpace(reportName) ? "operation-report" : reportName);
                string path = Path.Combine(ExportDirectory, $"{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

                using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
                writer.WriteLine("waybill_no,state,last_action,last_action_time,employee,site,kvd_reason,receiver,address,print_count");
                foreach (var row in rows ?? Array.Empty<WaybillDbModel>())
                {
                    ct.ThrowIfCancellationRequested();
                    writer.WriteLine(string.Join(",",
                        Csv(row.WaybillNo),
                        Csv(row.TrangThaiHienTai),
                        Csv(row.ThaoTacCuoi),
                        Csv(row.ThoiGianThaoTac),
                        Csv(row.NguoiThaoTac),
                        Csv(row.BuuCucThaoTac),
                        Csv(row.NguyenNhanKienVanDe),
                        Csv(row.NhanVienNhanHang),
                        Csv(row.DiaChiNhanHang),
                        row.PrintCount.ToString()));
                }

                return path;
            }, ct);
        }

        private static string Csv(string value)
        {
            value ??= string.Empty;
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        private static string MakeSafeFileName(string value)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '-');
            return value.Trim();
        }
    }
}
