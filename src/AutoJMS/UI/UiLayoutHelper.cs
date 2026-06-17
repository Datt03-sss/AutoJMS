using System;
using System.Drawing;
using System.Windows.Forms;
using Sunny.UI;

namespace AutoJMS.UI
{
    public static class UiLayoutHelper
    {
        public static void Configure(Main form)
        {
            if (form == null) return;

            form.SuspendLayout();

            ConfigureMainForm(form);
            ConfigureHomeTab(form);
            ConfigureDkchTab(form);
            ConfigureTrackingTab(form);
            ConfigurePrintTab(form);
            ConfigureAboutTab(form);

            form.ResumeLayout(false);
        }

        private static void ConfigureMainForm(Main form)
        {
            form.MinimumSize = new Size(1024, 700);

            // TabControl styling to remove borders and make it cleaner
            if (form.Controls.Find("tabControl", true).Length > 0 && form.Controls.Find("tabControl", true)[0] is UITabControl tabControl)
            {
                tabControl.ItemSize = new Size(135, 42); // Balanced size for tabs
                tabControl.Padding = new Point(0, 0);
            }
        }

        private static void ConfigureHomeTab(Main form)
        {
            // Make Home Navigation Bar a clean card with bottom border only
            var navBar = FindControl<UIPanel>(form, "tabHome_navBar");
            if (navBar != null)
            {
                navBar.Height = 46;
                navBar.Padding = new Padding(12, 4, 12, 4);
                navBar.Margin = new Padding(0);
                navBar.Radius = 0; // Flat against top
                navBar.RectSides = ToolStripStatusLabelBorderSides.Bottom;
                navBar.RectColor = AppPalette.SubtleBorder;
                navBar.FillColor = AppPalette.CardBackground;
            }
        }

        private static void ConfigureDkchTab(Main form)
        {
            var pnlLeft = FindControl<UIPanel>(form, "tabHome_pnlLeft");
            var dataSrc = FindControl<UITitlePanel>(form, "tabDKCH_dataSrc");
            var ctrlPanel = FindControl<UITitlePanel>(form, "uiTitlePanel1");
            var newBill = FindControl<UITitlePanel>(form, "uiTitlePanel2");

            if (pnlLeft != null && dataSrc != null && ctrlPanel != null && newBill != null)
            {
                pnlLeft.SuspendLayout();
                pnlLeft.Width = 340; // Balanced width
                pnlLeft.Padding = new Padding(12);
                pnlLeft.Radius = 0;
                pnlLeft.RectSides = ToolStripStatusLabelBorderSides.Right; // Border against webview
                pnlLeft.RectColor = AppPalette.SubtleBorder;
                pnlLeft.FillColor = AppPalette.AppBackground;

                // Re-layout children programmatically using a TableLayoutPanel for modern card spacing
                pnlLeft.Controls.Clear();

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 3,
                    BackColor = Color.Transparent,
                    Margin = new Padding(0)
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 165F)); // Data source
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 135F)); // Control Panel
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // New Bill input list

                dataSrc.Dock = DockStyle.Fill;
                dataSrc.Margin = new Padding(0, 0, 0, 12);
                dataSrc.TitleHeight = 30;
                dataSrc.Padding = new Padding(8);

                ctrlPanel.Dock = DockStyle.Fill;
                ctrlPanel.Margin = new Padding(0, 0, 0, 12);
                ctrlPanel.TitleHeight = 30;
                ctrlPanel.Padding = new Padding(8);

                newBill.Dock = DockStyle.Fill;
                newBill.Margin = new Padding(0);
                newBill.TitleHeight = 30;
                newBill.Padding = new Padding(8);

                layout.Controls.Add(dataSrc, 0, 0);
                layout.Controls.Add(ctrlPanel, 0, 1);
                layout.Controls.Add(newBill, 0, 2);

                pnlLeft.Controls.Add(layout);
                pnlLeft.ResumeLayout(true);
            }

            var splitContainer = FindControl<SplitContainer>(form, "splitContainer1");
            if (splitContainer != null)
            {
                splitContainer.SplitterWidth = 6;
            }
        }

        private static void ConfigureTrackingTab(Main form)
        {
            var actionArea = FindControl<UITableLayoutPanel>(form, "uiTableLayoutPanel2");
            if (actionArea != null)
            {
                actionArea.Padding = new Padding(12);
            }

            // Style toolbar as a clean action card
            var toolbar = FindControl<UIFlowLayoutPanel>(form, "uiFlowLayoutPanel1");
            if (toolbar != null)
            {
                toolbar.Padding = new Padding(8, 4, 8, 4);
                toolbar.Radius = 8;
                toolbar.FillColor = AppPalette.CardBackground;
                toolbar.RectColor = AppPalette.SubtleBorder;
                toolbar.Height = 48;
                toolbar.Margin = new Padding(0, 0, 0, 12);
            }

            var grid = FindControl<UIDataGridView>(form, "tabTracking_dataView");
            if (grid != null)
            {
                grid.RowTemplate.Height = 38; // Taller rows
                grid.Margin = new Padding(0, 10, 0, 0);
            }
        }

        private static void ConfigurePrintTab(Main form)
        {
            var mainLayout = FindControl<UITableLayoutPanel>(form, "uiTableLayoutPanel6");
            if (mainLayout != null)
            {
                mainLayout.Padding = new Padding(12);
                mainLayout.ColumnStyles[1].Width = 560; // Preview area width
            }

            var printFunc = FindControl<UITabControl>(form, "tabPrint_printFunc");
            if (printFunc != null)
            {
                printFunc.ItemSize = new Size(130, 36);
                printFunc.Margin = new Padding(0, 0, 0, 12);
            }

            var grid = FindControl<UIDataGridView>(form, "tabPrint_dataView");
            if (grid != null)
            {
                grid.RowTemplate.Height = 38;
            }

            var previewPanel = FindControl<UIPanel>(form, "uiPanel13");
            if (previewPanel != null)
            {
                previewPanel.Padding = new Padding(8);
                previewPanel.Radius = 8;
                previewPanel.FillColor = AppPalette.CardBackground;
                previewPanel.RectColor = AppPalette.SubtleBorder;
            }

            var printActionBar = FindControl<UIPanel>(form, "uiPanel18");
            if (printActionBar != null)
            {
                printActionBar.Padding = new Padding(6);
                printActionBar.Radius = 8;
                printActionBar.FillColor = AppPalette.CardBackground;
                printActionBar.RectColor = AppPalette.SubtleBorder;
                printActionBar.Margin = new Padding(0, 0, 0, 12);
            }
        }

        private static void ConfigureAboutTab(Main form)
        {
            var mainLayout = FindControl<UITableLayoutPanel>(form, "uiTableLayoutPanel5");
            if (mainLayout != null)
            {
                mainLayout.ColumnStyles[1].Width = 480;
                mainLayout.RowStyles[1].Height = 650;
            }

            var aboutCard = FindControl<UIPanel>(form, "uiPanel8");
            if (aboutCard != null)
            {
                aboutCard.Padding = new Padding(24);
                aboutCard.Radius = 12;
                aboutCard.FillColor = AppPalette.CardBackground;
                aboutCard.RectColor = AppPalette.SubtleBorder;
            }
        }

        private static T FindControl<T>(Control parent, string name) where T : Control
        {
            var found = parent.Controls.Find(name, true);
            if (found.Length > 0 && found[0] is T ctrl)
            {
                return ctrl;
            }
            return null;
        }
    }
}
