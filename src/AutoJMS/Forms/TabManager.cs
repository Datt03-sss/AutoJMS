using System.Collections.Generic;
using System.Windows.Forms;

namespace AutoJMS;

public class TabManager
{
    private readonly TabControl _tabControl;
    private readonly Dictionary<string, TabPage> _tabPages = new(System.StringComparer.OrdinalIgnoreCase);
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
    }

    public void ApplyTier(string tier, TierDefinitions definitions = null)
    {
        _currentTier = tier ?? "BASE";
        definitions ??= TierDefinitions.LoadFromFile();
        _tierConfig = definitions.GetTier(_currentTier);

        foreach (var kv in _tabPages)
        {
            bool show = _tierConfig.Tabs.Contains(kv.Key, System.StringComparer.OrdinalIgnoreCase);
            if (kv.Value != null && !kv.Value.IsDisposed)
            {
                kv.Value.Visible = show;
                kv.Value.Enabled = show;
            }
        }
    }

    public bool IsTabAllowed(string tabName)
    {
        return _tierConfig?.Tabs?.Contains(tabName, System.StringComparer.OrdinalIgnoreCase) == true;
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
