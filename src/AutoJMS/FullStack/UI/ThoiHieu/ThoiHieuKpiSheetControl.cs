using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.FullStack.UI.ThoiHieu
{
    public enum ThoiHieuViewMode
    {
        NormalScroll,
        FitWidth,
        FitOnePage
    }

    public sealed class ThoiHieuKpiSheetControl : ScrollableControl
    {
        private const float MinimumViewScale = 0.55f;

        private readonly ThoiHieuKpiGridRenderer _renderer = new();
        private ThoiHieuKpiSheetData _data = new();
        private ThoiHieuViewMode _viewMode = ThoiHieuViewMode.NormalScroll;
        private float _viewScale = 1.0f;
        private string _fitWarning = string.Empty;

        public event EventHandler<string> ViewWarningChanged;

        public ThoiHieuKpiSheetControl()
        {
            DoubleBuffered = true;
            AutoScroll = true;
            BackColor = Color.White;
            Font = new Font("Tahoma", 9F, FontStyle.Regular);
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
            true);
        }

        public ThoiHieuViewMode ViewMode => _viewMode;

        public float ViewScale => _viewScale;

        public string FitWarning => _fitWarning;

        public void SetData(ThoiHieuKpiSheetData data)
        {
            _data = data ?? new ThoiHieuKpiSheetData();
            RefreshView();
        }

        public void RefreshView()
        {
            RecalculateViewScale();
            Invalidate();
        }

        public void SetViewMode(ThoiHieuViewMode mode)
        {
            _viewMode = mode;
            RefreshView();
        }

        public void FitWidth() => SetViewMode(ThoiHieuViewMode.FitWidth);

        public void FitOnePage() => SetViewMode(ThoiHieuViewMode.FitOnePage);

        public void ResetZoom() => SetViewMode(ThoiHieuViewMode.NormalScroll);

        public Bitmap RenderFullBitmap(float scale = 1.0f)
        {
            return _renderer.RenderFullBitmap(_data, scale);
        }

        public Size GetFullSheetSize() => _renderer.Measure(_data);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            _renderer.Draw(e.Graphics, ClientRectangle, new Point(-AutoScrollPosition.X, -AutoScrollPosition.Y), _data, _viewScale);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RefreshView();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _renderer.Dispose();
            }

            base.Dispose(disposing);
        }

        private void RecalculateViewScale()
        {
            Size fullSize = _renderer.Measure(_data);
            int availableWidth = Math.Max(1, ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
            int availableHeight = Math.Max(1, ClientSize.Height - SystemInformation.HorizontalScrollBarHeight - 2);
            float requestedScale = 1.0f;
            string warning = string.Empty;

            if (_viewMode == ThoiHieuViewMode.FitWidth)
            {
                requestedScale = availableWidth / (float)Math.Max(1, fullSize.Width);
            }
            else if (_viewMode == ThoiHieuViewMode.FitOnePage)
            {
                float scaleX = availableWidth / (float)Math.Max(1, fullSize.Width);
                float scaleY = availableHeight / (float)Math.Max(1, fullSize.Height);
                requestedScale = Math.Min(scaleX, scaleY);

                if (requestedScale < MinimumViewScale)
                {
                    warning = "Số lượng nhân viên quá lớn để hiển thị rõ trong một trang. Vui lòng dùng Xuất ảnh đầy đủ.";
                }
            }

            _viewScale = _viewMode == ThoiHieuViewMode.NormalScroll
                ? 1.0f
                : Math.Clamp(requestedScale, MinimumViewScale, 1.0f);

            AutoScrollMinSize = new Size(
                Math.Max(1, (int)Math.Ceiling(fullSize.Width * _viewScale)),
                Math.Max(1, (int)Math.Ceiling(fullSize.Height * _viewScale)));

            if (!string.Equals(_fitWarning, warning, StringComparison.Ordinal))
            {
                _fitWarning = warning;
                ViewWarningChanged?.Invoke(this, _fitWarning);
            }
        }
    }
}
