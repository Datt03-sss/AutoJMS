using System.Drawing;
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

    // Ba test trên đều PASS trong khi từng icon trong app là một ô vuông rỗng:
    // font nạp đúng, codepoint đúng, nhưng Draw() vẽ bằng TextRenderer (GDI) còn
    // PrivateFontCollection chỉ tồn tại với GDI+, nên GDI thay font mặc định và
    // mọi codepoint PUA ra .notdef. Cách duy nhất thấy được là vẽ thật rồi so pixel.
    //
    // 0xF8FF nằm trong PUA nhưng ngoài cmap của lucide.ttf (hết ở 0xE78C), nên nó
    // LUÔN là .notdef. Glyph thật khác .notdef thì font đã tới được GDI; giống nhau
    // nghĩa là cả hai đang ra cùng một ô vuông.
    [Fact]
    public void Draw_RendersRealGlyph_NotNotdefBox()
    {
        var real = RenderToBytes(ASymbols.Check);
        var notdef = RenderToBytes(0xF8FF);

        Assert.False(real.SequenceEqual(notdef),
            "ASymbols.Check vẽ ra đúng hình mà một codepoint không có trong font vẽ ra. " +
            "GDI không thấy font 'lucide' — kiểm AddFontMemResourceEx trong ASymbols.LoadLucideFont.");
    }

    private static byte[] RenderToBytes(int symbol)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            ASymbols.Draw(g, symbol, 24, Color.Black, new Rectangle(0, 0, 32, 32));
        }

        var bytes = new byte[32 * 32];
        for (int y = 0, i = 0; y < 32; y++)
            for (int x = 0; x < 32; x++, i++)
                bytes[i] = bmp.GetPixel(x, y).R;
        return bytes;
    }
}
