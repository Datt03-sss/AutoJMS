using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace AutoJMS;

public class TabManager
{
    private readonly TabControl _tabControl;
    private readonly Dictionary<string, TabPage> _tabPages = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Vị trí của từng tab lúc đăng ký. Giữ lại để khi một tab bị ẩn rồi hiện lại thì
    /// về đúng chỗ cũ trong thứ tự thiết kế — ABOUT vẫn là tab cuối cùng.
    /// </summary>
    private readonly Dictionary<string, int> _designOrder = new(System.StringComparer.OrdinalIgnoreCase);

    private TierConfig _tierConfig;
    private string _currentTier = "BASE";

    public TabManager(TabControl tabControl)
    {
        _tabControl = tabControl;
    }

    public string CurrentTier => _currentTier;

    public void RegisterTab(string name, TabPage page)
    {
        _tabPages[name] = page;
        if (page == null) return;

        int index = _tabControl?.TabPages.IndexOf(page) ?? -1;
        _designOrder[name] = index >= 0 ? index : _designOrder.Count;
    }

    /// <summary>
    /// Ẩn/hiện tab theo <b>giao</b> của hai nguồn:
    /// <list type="number">
    ///   <item>danh sách <c>tabs</c> của tier trong <c>tier-definitions.json</c>;</item>
    ///   <item>entitlement đang có hiệu lực (<see cref="TierRuntimePolicy.Current"/>).</item>
    /// </list>
    /// Chỉ được THU HẸP, không được mở thêm: <c>tier-definitions.json</c> nằm trong thư
    /// mục cài đặt do người dùng chọn nên ghi được, không đủ thẩm quyền mở một tab mà
    /// entitlement đã tắt.
    /// </summary>
    public void ApplyTier(string tier, TierDefinitions definitions = null)
    {
        _currentTier = tier ?? "BASE";
        definitions ??= TierDefinitions.LoadFromFile();
        _tierConfig = definitions.GetTier(_currentTier);

        foreach (var kv in _tabPages.OrderBy(p => DesignOrderOf(p.Key)).ToList())
        {
            var page = kv.Value;
            if (page == null || page.IsDisposed) continue;

            bool show = IsTabAllowed(kv.Key);
            bool present = _tabControl.TabPages.IndexOf(page) >= 0;

            if (show)
            {
                if (!present)
                    _tabControl.TabPages.Insert(InsertIndexFor(kv.Key), page);
                page.Enabled = true;
            }
            else
            {
                // `TabPage.Visible = false` KHÔNG gỡ tab khỏi TabControl — đó là no-op của
                // WinForms, và là lý do việc phân quyền tab trước đây không có hiệu lực.
                // Phải Remove thật thì tab mới biến mất.
                if (present)
                {
                    _tabControl.TabPages.Remove(page);
                    AppLogger.Info($"[Tier] Ẩn tab {kv.Key} cho tier={_currentTier}.");
                }
                page.Enabled = false;
            }
        }
    }

    /// <summary>Vị trí chèn giữ đúng thứ tự thiết kế so với các tab đang hiển thị.</summary>
    private int InsertIndexFor(string name)
    {
        int order = DesignOrderOf(name);
        int index = 0;

        foreach (var kv in _tabPages)
        {
            if (string.Equals(kv.Key, name, System.StringComparison.OrdinalIgnoreCase)) continue;
            if (kv.Value == null || kv.Value.IsDisposed) continue;
            if (DesignOrderOf(kv.Key) >= order) continue;
            if (_tabControl.TabPages.IndexOf(kv.Value) >= 0) index++;
        }

        return System.Math.Min(index, _tabControl.TabPages.Count);
    }

    private int DesignOrderOf(string name)
        => _designOrder.TryGetValue(name, out int order) ? order : int.MaxValue;

    public bool IsTabAllowed(string tabName)
    {
        if (_tierConfig?.Tabs?.Contains(tabName, System.StringComparer.OrdinalIgnoreCase) != true)
            return false;

        // Kill switch của runtime policy (`tabs.tracking` / `tabs.print`). Trước đây hai cờ
        // này được tính ra rồi bỏ đó, không nơi nào đọc — nên tắt chúng không có tác dụng gì.
        var policy = TierRuntimePolicy.Current;
        if (string.Equals(tabName, "TRACKING", System.StringComparison.OrdinalIgnoreCase))
            return policy.AllowManualTracking;
        if (string.Equals(tabName, "PRINT", System.StringComparison.OrdinalIgnoreCase))
            return policy.AllowManualPrint;

        return true;
    }

    public TabPage CreateDynamicTab(string tabName, Control content)
    {
        var page = new TabPage(tabName)
        {
            Text = tabName,
            Name = "tabPlugin_" + tabName.Replace(" ", ""),
            UseVisualStyleBackColor = true
        };
        if (content != null)
        {
            content.Dock = DockStyle.Fill;
            page.Controls.Add(content);
        }
        _tabControl.TabPages.Add(page);
        RegisterTab(tabName, page);
        return page;
    }
}
