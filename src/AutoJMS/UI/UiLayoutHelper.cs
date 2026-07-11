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
                tabControl.ItemSize = new Size(120, 40); // Taller tabs for better click target
                tabControl.Padding = new Point(0, 0);
            }
        }

        private static void ConfigureHomeTab(Main form)
        {
            // Make Home Navigation Bar a clean card
            var navBar = FindControl<UIPanel>(form, "tabHome_navBar");
            if (navBar != null)
            {
                navBar.Height = 50;
                navBar.Padding = new Padding(10, 5, 10, 5);
                navBar.Margin = new Padding(0);
                navBar.Radius = 0; // Flat against top
            }
        }

        private static void ConfigureDkchTab(Main form)
        {
            // Simplify left panel
            var pnlLeft = FindControl<UIPanel>(form, "tabHome_pnlLeft");
            if (pnlLeft != null)
            {
                pnlLeft.Padding = new Padding(10);
                pnlLeft.Width = 350; // Give it consistent width
                pnlLeft.Radius = 0;
            }

            // Make the data source panel a distinct card
            var dataSrc = FindControl<UITitlePanel>(form, "tabDKCH_dataSrc");
            if (dataSrc != null)
            {
                dataSrc.Margin = new Padding(0, 0, 0, 10);
                dataSrc.TitleHeight = 35;
                dataSrc.Padding = new Padding(10);
            }

            // SplitContainer optimization
            var splitContainer = FindControl<SplitContainer>(form, "splitContainer1");
            if (splitContainer != null)
            {
                splitContainer.SplitterWidth = 5;
            }
        }

        private static void ConfigureTrackingTab(Main form)
        {
            // Transform toolbar into an action bar
            var actionArea = FindControl<UITableLayoutPanel>(form, "uiTableLayoutPanel2");
            if (actionArea != null)
            {
                actionArea.Margin = new Padding(10);
            }

            // DataGrid optimization
            var grid = FindControl<UIDataGridView>(form, "tabTracking_dataView");
            if (grid != null)
            {
                grid.Margin = new Padding(10, 0, 10, 10);
                grid.RowTemplate.Height = 40; // Taller rows for readability
            }
        }

        private static void ConfigurePrintTab(Main form)
        {
            // Ensure main layout is clean
            var mainLayout = FindControl<UITableLayoutPanel>(form, "uiTableLayoutPanel6");
            if (mainLayout != null)
            {
                mainLayout.Padding = new Padding(10);
                mainLayout.ColumnStyles[1].Width = 500; // Preview area width
            }

            // Print functions tab control
            var printFunc = FindControl<UITabControl>(form, "tabPrint_printFunc");
            if (printFunc != null)
            {
                printFunc.ItemSize = new Size(130, 35);
            }

            // Grid
            var grid = FindControl<UIDataGridView>(form, "tabPrint_dataView");
            if (grid != null)
            {
                grid.RowTemplate.Height = 40;
            }
        }

        private static void ConfigureAboutTab(Main form)
        {
            // Clean up the main layout
            var mainLayout = FindControl<UITableLayoutPanel>(form, "uiTableLayoutPanel5");
            if (mainLayout != null)
            {
                // Ensure the center column has a good absolute size, but allows flexibility
                mainLayout.ColumnStyles[1].Width = 450;
                mainLayout.RowStyles[1].Height = 600;
            }

            var aboutCard = FindControl<UIPanel>(form, "uiPanel8");
            if (aboutCard != null)
            {
                aboutCard.Padding = new Padding(20);
            }
        }

        // Helper to find controls by name recursively
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
