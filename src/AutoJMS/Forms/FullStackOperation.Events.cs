namespace AutoJMS
{
    public partial class FullStackOperation
    {
        private void BindCodeFirstEvents()
        {
            Load += FullStackOperation_Load;
            FormClosing += FullStackOperation_FormClosing;

            tabDash_updateData.Click += tabDash_updateData_Click;
            tabDash_statusSelect.SelectedIndexChanged += tabDash_statusSelect_SelectedIndexChanged;
            tabChat_statusSelect.SelectedIndexChanged += tabChat_statusSelect_SelectedIndexChanged;
            tabDash_dataSource.SelectedIndexChanged += tabDash_dataSource_SelectedIndexChanged;
            tabDash_timeUpdateData.SelectedIndexChanged += tabDash_timeUpdateData_SelectedIndexChanged;

            tabChat_btnStart.Click += tabChat_btnStart_Click;
            tabChat_btnReload.Click += tabChat_btnReload_Click;

            uiTabControl1.SelectedIndexChanged += uiTabControl1_SelectedIndexChanged;

            tabDash_dataGridView.CellFormatting += tabDash_dataGridView_CellFormatting;
            uiDataGridView2.CellFormatting += uiDataGridView2_CellFormatting;
            tabChat_dataGrid.CellFormatting += FullStackGrid_CellFormatting;
        }

        private void WireCodeFirstEvents()
        {
            BindCodeFirstEvents();
        }
    }
}
