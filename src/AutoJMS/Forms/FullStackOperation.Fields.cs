using Microsoft.Web.WebView2.WinForms;
using AutoJMS.UI.DesignSystem;
using AutoJMS.FullStack.UI.OperationCenter;
using System.Threading;
using System.Windows.Forms;

namespace AutoJMS
{
    public partial class FullStackOperation
    {
        private ATabControl uiTabControl1;
        private TopNavigation uiTabControl1Strip;
        private TabPage tabDash;
        // Panel thường chứ không phải APanel: hai dải này tự tô nền mang nghĩa
        // (màu thương hiệu tối / nền trắng có đúng một nét kẻ dưới).
        private Panel uiPanel10;
        private Panel _filterBarPanel;
        private Panel _queueNavPanel;
        private Panel _leftContextPanel;
        private Panel _rightIntelligencePanel;
        private AComboBox tabDash_timeUpdateData;
        private Label tabDash_lblLastUpdate;
        private AButton tabDash_updateData;
        private AComboBox tabDash_dataSource;
        private AComboBox tabDash_statusSelect;
        // Lưới tab ẩn (Visible = false, không bao giờ add vào form) — chỉ dùng làm chỗ
        // giữ TabPage. Không cần ATabControl + TopNavigation vì nó không hiện bao giờ.
        private TabControl uiTabControl2;
        private TabPage tabPage3;
        private DataGridView tabDash_dataGridView;
        private TabPage tabPage4;
        private DataGridView uiDataGridView2;
        private TabPage tabChat;
        private TableLayoutPanel uiTableLayoutPanel3;
        private WebView2 tabChat_webViewZalo;
        private TableLayoutPanel tabChat_leftPanel;
        private DataGridView tabChat_dataGrid;
        private APanel uiPanel15;
        private TableLayoutPanel uiTableLayoutPanel17;
        private AComboBox tabChat_statusSelect;
        private Label uiLabel5;
        private AButton tabChat_btnReload;
        private APanel uiPanel4;
        private LinkLabel tabChat_userName;
        private Label tabChat_userAvatar;
        private APanel uiPanel6;
        private TableLayoutPanel uiTableLayoutPanel19;
        private AButton tabChat_btnStart;
        private TableLayoutPanel uiTableLayoutPanel20;
        private AComboBox tabChat_timeSelect;
        private Label uiLabel3;
        private APanel uiPanel7;
        private TableLayoutPanel uiTableLayoutPanel16;
        private Label tabChat_hasXNCH;
        private Label tabChat_hasKVD;
        private Label tabChat_sumFollow;
        private APanel uiPanel5;

        private FlowLayoutPanel _dashQuickFilterPanel;
        private string _dashQuickFilter = string.Empty;
        private Label _dashQueueInsightLabel;
        // Ba control OperationCenter này ĐƯỢC ĐỌC (UpdateOperationCenterChrome,
        // UpdateOperationQueues, UpdateSelectedOperationDetailFromGrid) nhưng CHƯA CHỖ NÀO GÁN
        // — phần dựng UI của OperationCenter còn dở. Mọi chỗ đọc đều đã guard null nên app
        // không crash, chỉ là ba panel này chưa bao giờ hiện ra.
        // Gán null tường minh để tắt CS0649 mà không xoá field (xoá là vỡ các chỗ đọc trên).
        private QueueSidebarControl _operationQueueSidebar = null;
        private WaybillDetailPanel _operationDetailPanel = null;
        private StatusFooterControl _operationStatusFooter = null;
        private GridFilterToolbarControl _operationGridFilterToolbar;
        private Panel _operationGridHost;
        private Panel _operationInventoryWorkspace;
        private Panel _waybillJourneyWorkspace;
        private DataGridView _waybillJourneyGrid;
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
        private AButton _operationRefreshLocalButton;
    }
}

