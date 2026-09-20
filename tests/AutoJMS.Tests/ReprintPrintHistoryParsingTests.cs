using Xunit;

namespace AutoJMS.Tests;

/// <summary>
/// Ghim tên trường của <c>expressPrint/listPage</c> — nguồn duy nhất cho Vùng 4 ("{mã bưu
/// cục} in lần {n}: {giờ} {ngày}") của tab "In lại đơn".
/// <para>
/// JSON dưới đây giữ nguyên HÌNH DẠNG của response thật nhưng đã thay số vận đơn và bỏ hết
/// trường người gửi/người nhận: repo này công khai, mà thứ cần ghim chỉ là tên trường.
/// </para>
/// </summary>
public sealed class ReprintPrintHistoryParsingTests
{
    private const string PrintListResponse = """
    {
        "code": 1,
        "msg": "1:Yêu cầu thành công",
        "data": {
            "records": [
                {
                    "waybillNo": "000000000000",
                    "isSign": 0,
                    "printsNumber": 1,
                    "printTime": "2026-09-20 20:46:30"
                }
            ],
            "total": 1,
            "size": 20,
            "current": 1,
            "pages": 1
        },
        "succ": true,
        "fail": false
    }
    """;

    [Fact]
    public void DocSoLanInVaGioInGanNhat()
    {
        int printCount = 0;
        string printTime = "";

        ReceiverContactService.MergePrintHistory(PrintListResponse, ref printCount, ref printTime);

        // Số lần in là "printsNumber", KHÔNG phải "printCount" — đọc nhầm thì ô luôn ra 0.
        Assert.Equal(1, printCount);
        Assert.Equal("2026-09-20 20:46:30", printTime);
    }

    /// <summary>
    /// JMS trả lỗi nghiệp vụ trong thân HTTP 200 với <c>data:null</c>. Khi đó phải giữ nguyên
    /// giá trị chỗ đứng tạm, vì đè bằng 0/rỗng là xoá trắng Vùng 4 trên bản in.
    /// </summary>
    [Fact]
    public void DataRongThiKhongDungToiGiaTriDangCo()
    {
        int printCount = 3;
        string printTime = "2026-09-19 08:00:00";

        ReceiverContactService.MergePrintHistory("""{"code":0,"msg":"loi","data":null}""", ref printCount, ref printTime);
        ReceiverContactService.MergePrintHistory("""{"data":{"records":[]}}""", ref printCount, ref printTime);
        ReceiverContactService.MergePrintHistory("", ref printCount, ref printTime);

        Assert.Equal(3, printCount);
        Assert.Equal("2026-09-19 08:00:00", printTime);
    }
}
