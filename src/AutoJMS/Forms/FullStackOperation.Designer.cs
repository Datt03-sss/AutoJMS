namespace AutoJMS
{
    partial class FullStackOperation
    {
        // FULLSTACK UI IS CODE-FIRST.
        // Do not edit this form with WinForms Designer.
        // Runtime layout is built from FullStackOperation.*.cs code-first partials.

        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Cell fonts are not components, so nothing else releases them.
                DisposeThoiHieuFonts();
                components?.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            // 
            // FullStackOperation
            // 
            ClientSize = new Size(800, 480);
            Name = "FullStackOperation";
            // ZoomScaleRect bỏ: thuộc tính của UIForm, dùng cho cơ chế tự co giãn
            // riêng của SunnyUI. Form chuẩn co giãn bằng AutoScaleMode.
            ResumeLayout(false);
            // Intentionally empty. FullStackOperation builds all UI in code.
        }
    }
}
