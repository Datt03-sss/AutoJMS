using System.Reflection;
using AutoJMS.UI.DesignSystem;
using Xunit;

namespace AutoJMS.Tests;

// ASymbols.Draw không bao giờ ném lỗi: lucide.ttf mất khỏi assembly thì nó im lặng
// rơi về Segoe MDL2 Assets và vẽ tiếp bằng bảng mã KHÁC — mọi nút trong app ra glyph
// sai mà Release build vẫn 0 Warning. Repo đã có bài học "5 gate PASS trong khi app
// hỏng ngay lúc mở"; đây là gate cho đúng chỗ đó.
public sealed class ASymbolsFontTests
{
    [Fact]
    public void EmbeddedLucideFont_Loads_NotMdl2Fallback()
    {
        Assert.True(ASymbols.IsEmbeddedFontLoaded,
            $"lucide.ttf không nạp được, đang vẽ icon bằng '{ASymbols.IconFontFamily}'. " +
            "Kiểm EmbeddedResource trong AutoJMS.csproj và tên resource trong ASymbols.cs.");
        Assert.Equal("lucide", ASymbols.IconFontFamily);
    }

    // Tên resource là một chuỗi hằng: gõ sai hay đổi namespace/đường dẫn file thì
    // GetManifestResourceStream trả null, và cái null đó bị catch nuốt mất.
    [Fact]
    public void FontResource_IsEmbeddedUnderExpectedName()
    {
        var names = typeof(ASymbols).Assembly.GetManifestResourceNames();
        Assert.Contains("AutoJMS.Resources.Fonts.lucide.ttf", names);
    }

    // Mọi hằng icon phải là codepoint trong Private Use Area của font. 0 nghĩa là ai
    // đó thêm hằng mà quên tra cmap; số ngoài PUA nghĩa là chép từ bộ icon khác.
    [Fact]
    public void AllSymbolConstants_AreInPrivateUseArea()
    {
        var fields = typeof(ASymbols).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(int) && f.Name != nameof(ASymbols.None))
            .ToArray();

        Assert.NotEmpty(fields);
        foreach (var f in fields)
        {
            int cp = (int)f.GetRawConstantValue();
            Assert.True(cp >= 0xE000 && cp <= 0xF8FF,
                $"ASymbols.{f.Name} = 0x{cp:X4} nằm ngoài Private Use Area (0xE000–0xF8FF).");
        }
    }
}
