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
                // ThemeChanged là event tĩnh: không gỡ thì form đã đóng bị giữ sống tới hết tiến trình.
                _contentThemeHook?.Dispose();
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
            ResumeLayout(false);
            // Intentionally empty. FullStackOperation builds all UI in code.
        }
    }
}
