using System.Text;

namespace AutoJMS
{
    /// <summary>
    /// Centralised provider for terms of use and privacy policy text.
    /// - <see cref="GetTermsText"/> returns the full 9-section document used in TermsDialog.
    /// - <see cref="GetTermsSummaryText"/> returns a short summary shown on the About card.
    /// </summary>
    public static class TermsContentProvider
    {
        public static string GetTermsText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("1. MỤC ĐÍCH SỬ DỤNG");
            sb.AppendLine();
            sb.AppendLine("AutoJMS là phần mềm hỗ trợ thao tác nghiệp vụ nội bộ, được thiết kế nhằm tăng hiệu suất xử lý công việc, giảm thao tác lặp lại và hỗ trợ người dùng theo dõi, xử lý, tự động hóa một số quy trình trong phạm vi được cấp quyền.");
            sb.AppendLine();
            sb.AppendLine("Phần mềm chỉ đóng vai trò công cụ hỗ trợ. Người dùng cần có trách nhiệm kiểm tra, xác nhận kết quả xử lý trước khi sử dụng cho công việc thực tế.");
            sb.AppendLine();
            sb.AppendLine("2. PHẠM VI SỬ DỤNG");
            sb.AppendLine();
            sb.AppendLine("Người dùng chỉ được sử dụng AutoJMS cho mục đích hợp lệ, đúng phạm vi công việc và đúng quyền hạn được cấp.");
            sb.AppendLine();
            sb.AppendLine("Nghiêm cấm sử dụng phần mềm cho các mục đích gây ảnh hưởng đến hệ thống, khai thác trái phép dữ liệu, can thiệp ngoài phạm vi cho phép, chia sẻ tài khoản, chia sẻ khóa bản quyền hoặc sử dụng phần mềm trên thiết bị không được ủy quyền.");
            sb.AppendLine();
            sb.AppendLine("3. TÀI KHOẢN, KHÓA BẢN QUYỀN VÀ THIẾT BỊ");
            sb.AppendLine();
            sb.AppendLine("Việc sử dụng AutoJMS có thể yêu cầu khóa bản quyền. Khóa bản quyền được cấp cho người dùng hoặc thiết bị cụ thể tùy theo chính sách sử dụng.");
            sb.AppendLine();
            sb.AppendLine("Người dùng không được sao chép, chuyển nhượng, chia sẻ hoặc bán lại khóa bản quyền nếu chưa được sự đồng ý của bên phát triển phần mềm. Trong trường hợp phát hiện sử dụng sai phạm, khóa bản quyền có thể bị tạm khóa hoặc thu hồi.");
            sb.AppendLine();
            sb.AppendLine("4. DỮ LIỆU NGƯỜI DÙNG");
            sb.AppendLine();
            sb.AppendLine("AutoJMS có thể lưu một số thiết lập cục bộ trên máy người dùng như cấu hình hiển thị, thư mục tải xuống, máy in mặc định, trạng thái làm việc gần nhất hoặc các tùy chọn cá nhân khác nhằm cải thiện trải nghiệm sử dụng.");
            sb.AppendLine();
            sb.AppendLine("AutoJMS không thu thập bất cứ thông tin gì nằm ngoài phạm vi cho phép.");
            sb.AppendLine();
            sb.AppendLine("5. BẢO MẬT VÀ XÁC THỰC");
            sb.AppendLine();
            sb.AppendLine("Người dùng không được chỉnh sửa, can thiệp, dịch ngược, bẻ khóa, vô hiệu hóa cơ chế bảo vệ hoặc thay đổi các thành phần của phần mềm nhằm vượt quyền sử dụng hoặc làm sai lệch hoạt động của ứng dụng.");
            sb.AppendLine();
            sb.AppendLine("6. CẬP NHẬT PHẦN MỀM");
            sb.AppendLine();
            sb.AppendLine("AutoJMS có thể cung cấp bản cập nhật nhằm sửa lỗi, cải thiện hiệu năng, nâng cao độ ổn định hoặc bổ sung tính năng mới.");
            sb.AppendLine();
            sb.AppendLine("Các bản cập nhật ổn định được khuyến nghị cho người dùng thông thường. Các bản beta chỉ dành cho mục đích thử nghiệm, có thể chứa lỗi phát sinh và chỉ nên sử dụng khi người dùng chấp nhận rủi ro trong quá trình trải nghiệm.");
            sb.AppendLine();
            sb.AppendLine("7. GIỚI HẠN TRÁCH NHIỆM");
            sb.AppendLine();
            sb.AppendLine("Phần mềm được cung cấp như một công cụ hỗ trợ nghiệp vụ. Bên phát triển không chịu trách nhiệm đối với các thiệt hại phát sinh từ việc người dùng sử dụng sai mục đích, thao tác sai, nhập sai dữ liệu, sử dụng phần mềm ngoài phạm vi cho phép hoặc không kiểm tra lại kết quả trước khi áp dụng vào công việc thực tế.");
            sb.AppendLine();
            sb.AppendLine("Trong mọi trường hợp, người dùng cần chủ động xác minh dữ liệu và chịu trách nhiệm đối với quyết định nghiệp vụ của mình.");
            sb.AppendLine();
            sb.AppendLine("8. THAY ĐỔI ĐIỀU KHOẢN");
            sb.AppendLine();
            sb.AppendLine("Điều khoản sử dụng và chính sách bảo mật có thể được cập nhật theo từng phiên bản phần mềm hoặc theo nhu cầu vận hành thực tế. Việc tiếp tục sử dụng AutoJMS sau khi có thay đổi đồng nghĩa với việc người dùng đồng ý với các nội dung đã được cập nhật.");
            sb.AppendLine();
            sb.AppendLine("9. LIÊN HỆ HỖ TRỢ");
            sb.AppendLine();
            sb.AppendLine("Nếu có câu hỏi, lỗi phát sinh hoặc cần hỗ trợ trong quá trình sử dụng AutoJMS, người dùng có thể liên hệ theo thông tin được hiển thị trong ứng dụng.");
            sb.AppendLine();
            sb.AppendLine("Thông tin hỗ trợ: Zalo: 0355520331 — FS: 01525852");
            return sb.ToString();
        }

        public static string GetTermsSummaryText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("AutoJMS là công cụ hỗ trợ xử lý nghiệp vụ nội bộ, giúp tăng tốc thao tác, giảm thao tác lặp lại và hỗ trợ người dùng trong quá trình vận hành.");
            sb.AppendLine();
            sb.AppendLine("Tóm tắt điều khoản:");
            sb.AppendLine("• Sử dụng phần mềm đúng mục đích, đúng phạm vi công việc và quyền được cấp.");
            sb.AppendLine("• Người dùng cần kiểm tra dữ liệu, kết quả xử lý trước khi áp dụng thực tế.");
            sb.AppendLine("• Không chia sẻ khóa bản quyền, tài khoản hoặc sử dụng phần mềm ngoài thiết bị được phép.");
            sb.AppendLine("• Một số thiết lập cục bộ có thể được lưu trên máy để cải thiện trải nghiệm sử dụng.");
            sb.AppendLine("• Bản cập nhật Beta có thể chưa ổn định và chỉ nên dùng khi người dùng chấp nhận thử nghiệm.");
            return sb.ToString();
        }
    }
}
