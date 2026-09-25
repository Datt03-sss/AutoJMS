using System;
using System.IO;
using System.Text;
using Xunit;

namespace AutoJMS.Tests
{
    /// <summary>
    /// AppPaths.AtomicWriteAllText thay cho File.WriteAllText ở đường ghi license.dat.
    ///
    /// Phần chống mất điện (Flush(flushToDisk: true)) KHÔNG test được bằng unit test -
    /// muốn chứng minh phải cắt nguồn thật. Những gì test được và dễ vỡ khi ai đó sửa lại
    /// hàm này thì kiểm hết: hai nhánh Move/Replace, không bỏ quên file .tmp, và mã hoá ký
    /// tự phải y hệt File.WriteAllText (UTF-8 KHÔNG BOM) - thêm BOM là hỏng base64 DPAPI.
    /// </summary>
    public sealed class AppPathsAtomicWriteTests
    {
        [Fact]
        public void GhiLanDau_TaoDungNoiDung_VaKhongDeLaiFileTam()
        {
            RunInTempDir(dir =>
            {
                var path = Path.Combine(dir, "license.dat");

                AppPaths.AtomicWriteAllText(path, "ban-ghi-dau-tien");

                Assert.Equal("ban-ghi-dau-tien", File.ReadAllText(path));
                Assert.False(File.Exists(path + ".tmp"));
            });
        }

        [Fact]
        public void GhiDe_ThayNoiDungCu_VaKhongDeLaiFileTam()
        {
            RunInTempDir(dir =>
            {
                var path = Path.Combine(dir, "license.dat");
                File.WriteAllText(path, "ban-ghi-cu-dai-hon-ban-moi");

                // Nhánh File.Replace - khác hẳn nhánh File.Move ở lần ghi đầu.
                AppPaths.AtomicWriteAllText(path, "moi");

                Assert.Equal("moi", File.ReadAllText(path));
                Assert.False(File.Exists(path + ".tmp"));
            });
        }

        [Fact]
        public void GhiRaDungByte_UTF8_KhongBOM()
        {
            RunInTempDir(dir =>
            {
                var path = Path.Combine(dir, "license.dat");
                const string content = "tiếng Việt có dấu||HWID";

                AppPaths.AtomicWriteAllText(path, content);

                Assert.Equal(new UTF8Encoding(false).GetBytes(content), File.ReadAllBytes(path));
            });
        }

        [Fact]
        public void TuTaoThuMucChuaCo()
        {
            RunInTempDir(dir =>
            {
                var path = Path.Combine(dir, "secure", "license.dat");

                AppPaths.AtomicWriteAllText(path, "x");

                Assert.Equal("x", File.ReadAllText(path));
            });
        }

        private static void RunInTempDir(Action<string> body)
        {
            var dir = Path.Combine(Path.GetTempPath(), "AutoJMS.AtomicWrite." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                body(dir);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }
    }
}
