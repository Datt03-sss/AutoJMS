using Microsoft.Web.WebView2.WinForms;
using Sunny.UI;
using AutoJMS.FullStack.UI.OperationCenter;
using System.Threading;
using System.Windows.Forms;

namespace AutoJMS
{
    public partial class FullStackOperation
    {
        private UITabControl uiTabControl1;
        private TabPage tabDash;
        private UITableLayoutPanel uiTableLayoutPanel4;
        private UIPanel uiPanel10;
        private UIPanel _filterBarPanel;
        private Panel _queueNavPanel;
        private Panel _leftContextPanel;
        private Panel _rightIntelligencePanel;
        private UIComboBox tabDash_timeUpdateData;
        private UISymbolLabel tabDash_lblLastUpdate;
        private UISymbolButton tabDash_updateData;
        private UIComboBox tabDash_dataSource;
        private UIComboBox tabDash_statusSelect;
        private UITabControl uiTabControl2;
        private TabPage tabPage3;
        private UIDataGridView tabDash_dataGridView;
        private TabPage tabPage4;
        private UIDataGridView uiDataGridView2;
        private TabPage tabChat;
        private UITableLayoutPanel uiTableLayoutPanel3;
        private WebView2 tabChat_webViewZalo;
        private UITableLayoutPanel tabChat_leftPanel;
        private UIDataGridView tabChat_dataGrid;
        private UIPanel uiPanel15;
        private UITableLayoutPanel uiTableLayoutPanel17;
        private UIComboBox tabChat_statusSelect;
        private UILabel uiLabel5;
        private UISymbolButton tabChat_btnReload;
        private UIPanel uiPanel4;
        private UILinkLabel tabChat_userName;
        private UIAvatar tabChat_userAvatar;
        private UIPanel uiPanel6;
        private UITableLayoutPanel uiTableLayoutPanel19;
        private UISymbolButton tabChat_btnStart;
        private UITableLayoutPanel uiTableLayoutPanel20;
        private UIComboBox tabChat_timeSelect;
        private UILabel uiLabel3;
        private UIPanel uiPanel7;
        private UITableLayoutPanel uiTableLayoutPanel16;
        private UILabel tabChat_hasXNCH;
        private UILabel tabChat_hasKVD;
        private UILabel tabChat_sumFollow;
        private UIPanel uiPanel5;

        private FlowLayoutPanel _dashQuickFilterPanel;
        private string _dashQuickFilter = string.Empty;
        private Label _dashQueueInsightLabel;
        private QueueSidebarControl _operationQueueSidebar;
        private WaybillDetailPanel _operationDetailPanel;
        private StatusFooterControl _operationStatusFooter;
        private GridFilterToolbarControl _operationGridFilterToolbar;
        private Panel _operationGridHost;
        private Panel _operationInventoryWorkspace;
        private Panel _waybillJourneyWorkspace;
        private UIDataGridView _waybillJourneyGrid;
        private Label _waybillJourneyTitle;
        private Label _waybillJourneyWaybillLabel;
        private Label _waybillJourneyStatusLabel;
        private Button _waybillJourneyCacheButton;
        private Button _waybillJourneyRawJsonButton;
        private Button _waybillJourneyBackButton;
        private string _activeJourneyWaybillNo = string.Empty;
        private CancellationTokenSource _journeyLoadCts;
        private FlowLayoutPanel _operationMiniMetricStrip;
        private TableLayoutPanel _operationFocusStrip;
        private KpiCardControl _kpiTotalInventory;
        private KpiCardControl _kpiInbound;
        private KpiCardControl _kpiDelivery;
        private KpiCardControl _kpiBacklog;
        private KpiCardControl _kpiReturn;
        private KpiCardControl _kpiInventoryCheck;
        private KpiCardControl _kpiCustomerService;
        private KpiCardControl _kpiStationHalt;
        private KpiCardControl _kpiStarred;
        private Label _operationHeaderTitle;
        private Label _operationHeaderStatus;
        private UISymbolButton _operationRefreshLocalButton;
    }
}

