using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.UI.ThoiHieu
{
    public static class ThoiHieuKpiImageExporter
    {
        public static Bitmap RenderFullBitmap(ThoiHieuKpiSheetData data, float scale = 1.0f)
        {
            using var renderer = new ThoiHieuKpiGridRenderer();
            return renderer.RenderFullBitmap(data, scale);
        }

        public static Task<string> ExportFullImageAsync(
            ThoiHieuKpiSheetData data,
            string outputDirectory,
            CancellationToken token,
            float scale = 1.0f)
        {
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();

                data ??= new ThoiHieuKpiSheetData();
                Directory.CreateDirectory(outputDirectory);

                string siteCode = SanitizeFilePart(string.IsNullOrWhiteSpace(data.SiteCode) ? "214A02" : data.SiteCode);
                string fileName = $"thoi-hieu-{siteCode}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png";
                string path = EnsureUniquePath(Path.Combine(outputDirectory, fileName));

                using var bitmap = RenderFullBitmap(data, scale);
                token.ThrowIfCancellationRequested();
                bitmap.Save(path, ImageFormat.Png);

                return path;
            }, token);
        }

        private static string SanitizeFilePart(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '-');
            }

            return value.Trim();
        }

        private static string EnsureUniquePath(string path)
        {
            if (!File.Exists(path)) return path;

            string directory = Path.GetDirectoryName(path) ?? "";
            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);

            for (int i = 1; i < 1000; i++)
            {
                string candidate = Path.Combine(directory, $"{name}-{i}{extension}");
                if (!File.Exists(candidate)) return candidate;
            }

            return Path.Combine(directory, $"{name}-{Guid.NewGuid():N}{extension}");
        }
    }
}
