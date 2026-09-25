using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutoJMS.UI.DesignSystem;


namespace AutoJMS
{
    public partial class frmLogin : Form
    {
        public string EnteredKey { get; private set; } = "";
        private string _myHwid;
        public frmLogin(string hwid)
        {
            InitializeComponent();

            _myHwid = hwid;
            txt_hwid.Text = _myHwid;
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
        }

        private void txt_hwid_ButtonClick(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(txt_hwid.Text))
            {
                Clipboard.SetText(_myHwid);
                AToast.Show(this, "Đã copy mã yêu cầu vào bộ nhớ tạm!");
            }
            else
            {
                AToast.Warning(this, "Ô dữ liệu đang trống, không có gì để copy!");
            }
        }

        private async void btn_activate_Click(object sender, EventArgs e)
        {
            // Chữ gợi ý nay là PlaceholderText của TextBox nên không bao giờ lọt vào .Text
            // được nữa; vế "key == watermarkText" cũ vì vậy thành thừa, IsNullOrWhiteSpace
            // chặn đúng y hệt trường hợp ô trống.
            string key = txt_key.Text.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                MessageBox.Show("Vui lòng nhập Key kích hoạt để tiếp tục!", "Cảnh báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string originalText = btn_active.Text;

            btn_active.Enabled = false;
            btn_active.Text = "Đang kết nối...";

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

                var verifyResult = await LicenseApiService.VerifyLicenseSecureAsync(key, Program.HWID, cts.Token);

                if (verifyResult.Success)
                {
                    EnteredKey = key;
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                else
                {
                    MessageBox.Show(string.IsNullOrWhiteSpace(verifyResult.Message) ? "Kết nối thất bại" : verifyResult.Message, "Kết nối thất bại", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show("Đường truyền mạng đang khônng ổn định.\nVui lòng thử lại sau!", "Lỗi kết nối", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi hệ thống:\n" + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btn_active.Enabled = true;
                btn_active.Text = originalText;
            }
        }

    }

}