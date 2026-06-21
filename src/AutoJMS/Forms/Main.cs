using AutoJMS.Data;
using AutoJMS.Automation.DevTools;
using AutoJMS.Diagnostics;
using AutoJMS.Diagnostics.AppCapture;
using AutoJMS.ModuleSystem;
using Microsoft.Web.WebView2.Core;
using PdfiumViewer;
using Sunny.UI;
using System.Drawing.Printing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Size = System.Drawing.Size;

namespace AutoJMS
{
    public partial class Main : UIForm
    {
        private static string JmsHomeUrl => AppConfig.Current.JmsBaseUrl.TrimEnd('/');
        private static string AppsScriptUrl => AppConfig.Current.AppsScriptUrl;

        private AppSettings _settings;
        private UserSettingsService _userSettings;
        public static string CapturedAuthToken = "";

        // ================= TIER & TAB MANAGEMENT =================
        public readonly string CurrentTier;
        private readonly TabManager _tabManager;
        private TierRuntimePolicy _tierPolicy;
        private FullStackOperation _fullStackForm;
        private bool _ultraLaunched;
        private bool _isShowingFullStackForm;
        private bool _isSyncingZoomFactor;
        private CancellationTokenSource _zoomSaveCts;

        // ================= CÁC DỊCH VỤ CỐT LÕI =================
        public static ITrackingService _trackingService;
        private DkchManager _dkchManager;
        private IPrintService _printService;
        public ZaloChatService _zaloChatService;

        // ================= BACKGROUND SYNC =================
        private readonly CancellationTokenSource _appCts = new();
        private readonly SemaphoreSlim _syncGate = new(1, 1);
        private List<WaybillDbModel> _cloudData = new();
        private List<string> _dashStatusCache = new();
        private List<string> _chatStatusCache = new();
        private string _cloudDataHashVersion = string.Empty;
        private string _chatDataHashVersion = string.Empty;
        private string _dashGridHashVersion = string.Empty;
        private string _chatGridHashVersion = string.Empty;
        private List<WaybillDbModel> _lastDashSourceData = new();
        private List<WaybillDbModel> _lastChatSourceData = new();
        private string _dashStatusHashVersion = string.Empty;
        private string _chatStatusHashVersion = string.Empty;
        private string _lastSourceWaybillsHash = string.Empty;
        private List<string> _lastSourceWaybillsSnapshot = new();
        private readonly Dictionary<string, string> _sourceRowFingerprintCache = new(StringComparer.OrdinalIgnoreCase);
        private string _cloudSourceFingerprintHash = string.Empty;
        private string _phatLaiSourceFingerprintHash = string.Empty;
        private readonly string _syncCacheFilePath = Path.Combine(
            AppPaths.CacheDir,
            $"sync-cache-{SupabaseDbService.MachineId}.json");
        private readonly System.Windows.Forms.Timer _autoSyncTimer = new();
        private DateTime _lastSyncAttemptAtUtc = DateTime.MinValue;
        private DateTime _lastSuccessfulSyncAtUtc = DateTime.MinValue;
        private DateTime _lastAutoLeaseHeartbeatAtUtc = DateTime.MinValue;
        private readonly TimeSpan _manualSyncCooldown = TimeSpan.FromMinutes(3);
        private readonly TimeSpan _leaseHeartbeatRefresh = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _leaseDuration = TimeSpan.FromMinutes(30);
        private readonly TimeSpan _autoSyncWindow = TimeSpan.FromMinutes(30);

        // ================= CỜ TRẠNG THÁI =================
        public bool _isZaloLoaded = false;
        private bool _isDkchNeedReload = true;
        private bool _isHomeNeedReload = false;
        private readonly object _authTokenLock = new object();
        private CancellationTokenSource _authTokenSaveCts;
        private System.Windows.Forms.Timer _dkchUiStateTimer;
        private bool _isDkchStarting = false;
        private readonly SemaphoreSlim _dkchManualInputGate = new(1, 1);
        private bool _webDebugInspectorEnabled = false;
        private bool _webDebugExportRunning = false;
        private WebViewDevToolsInspector _homeWebDebugInspector = null;
        private WebViewDevToolsInspector _dkchWebDebugInspector = null;
        private WebViewDevToolsInspector _printWebDebugInspector = null;
        private readonly WebDebugExportService _webDebugExportService = new();
        private readonly UserActionCaptureService _appUserActionCapture = new(AppCaptureManager.Instance);
        private WebViewCaptureService _homeAppCapture = null;
        private WebViewCaptureService _dkchAppCapture = null;

        // ================= BỘ NHỚ IN ẤN & GIAO DIỆN =================
        private readonly Dictionary<string, (DateTime LastTime, int Count)> _printedHistory = new(StringComparer.OrdinalIgnoreCase);
        private readonly TabPrintReprintPolicy _tabPrintReprintPolicy = new();
        private readonly TabPrintReprintStore _tabPrintReprintStore = new();
        private readonly Dictionary<string, (string PdfPath, DateTime CreatedAt)> _lastPrintedPdfBySignature = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (string PdfUrl, DateTime CreatedAt)> _lastPdfUrlBySignature = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (string PdfPath, DateTime CreatedAt)> _lastPrintedPdfByUrl = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PrintJobCacheEntry> _printJobCacheBySignature = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Task<PrintJobCacheEntry>> _printJobPreloadTasks = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _printJobCacheLock = new();
        private static readonly TimeSpan PrintPdfCacheTtl = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan PrintJobCacheTtl = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan PrintPdfUrlCacheTtl = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan PrintPdfUrlTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan PrintPdfDownloadTimeout = TimeSpan.FromSeconds(75);
        private static readonly TimeSpan PrintPdfFirstAttemptTimeout = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan PrintPdfRetryAttemptTimeout = TimeSpan.FromSeconds(12);
        private const int PrintPdfDownloadMaxAttempts = 6;
        private static readonly System.Net.Http.HttpClient PrintPdfHttpClient = CreatePrintPdfHttpClient();
        private readonly SemaphoreSlim _printLock = new(1, 1);
        private readonly ISiteContextProvider _siteContextProvider = new SiteContextProvider();
        private IPrinterPreflightService _printerPreflightService;
        private IPrinterMaintenanceService _printerMaintenanceService;
        private CurrentPrintAttempt _currentPrintAttempt;
        private System.Windows.Forms.Timer hideTimer = null;
        private string _downloadFolderPath = AppPaths.DownloadsDir;

        private static readonly Regex DkchWaybillRegex = new Regex("^[A-Za-z0-9]{1,20}$", RegexOptions.Compiled);
        private const string DkchRoutePath = "app/operatingPlatformIndex/returnAndForwardMaintainAddSite";
        private static string DkchTargetUrl => AppConfig.Current.BuildJmsUrl(DkchRoutePath);
        private static readonly TimeSpan DkchReadyTimeout = TimeSpan.FromSeconds(15);
        private const string CHROME_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        private UILabel lblNetworkStatus;
        private NetworkStatus _currentNetworkStatus = NetworkStatus.Online;

        public Main(string tier = "BASE")
        {
            CurrentTier = tier ?? "BASE";
            _tabManager = new TabManager(tabControl);

            // Resolve what this tier is allowed to run in the background.
            // Every startup/background entry point consults this policy instead
            // of doing ad-hoc tier string checks.
            _tierPolicy = Program.RuntimePolicy != null
                ? TierRuntimePolicy.Resolve(Program.RuntimePolicy, CurrentTier)
                : TierRuntimePolicy.Resolve(CurrentTier);

            InitializeComponent();
            AlignLeftPanelControls();
            tabHome_urlBar.KeyDown += TabHome_urlBar_KeyDown;

            // Register all built-in tabs with the TabManager
            _tabManager.RegisterTab("HOME", tabHome);
            _tabManager.RegisterTab("DKCH", tabDKCH);
            _tabManager.RegisterTab("TRACKING", tabTracking);
            _tabManager.RegisterTab("PRINT", tabPrint);
            _tabManager.RegisterTab("ABOUT", tabAbout);
            _tabManager.ApplyTier(CurrentTier);
            _userSettings = new UserSettingsService();
            _settings = _userSettings.Current;

            // Resolve and apply UI theme
            if (Enum.TryParse<UI.ThemeMode>(_settings.Theme, out var themeMode))
            {
                UI.AppTheme.CurrentTheme = themeMode;
            }
            UI.AppTheme.Apply(this);

            tabPrint_AutoMode.Active = _settings.PrintDefaultAutoPrint;
            _printerPreflightService = new PrinterPreflightService(() => _settings);
            _printerMaintenanceService = new PrinterMaintenanceService(_printerPreflightService);

            // Subscribe to JMS auth expiry — clear local copy + stop background tasks
            JmsAuthStateService.AuthExpired += OnJmsAuthExpired;

            // Provide a refresh hook so JmsAuthStateService can pull a fresh token
            // from the WebView2 localStorage before declaring our cached token expired.
            JmsAuthStateService.WebViewTokenRefresher = GetTokenFromAnyWebViewAsync;

            // Wire the high-level token orchestrator (priority resolution +
            // force-refresh + "really expired" notification). Storage still
            // lives in JmsAuthStateService; these are just providers.
            JmsAuthTokenService.WebViewTokenReader = GetTokenFromJmsWebViewAsync;
            JmsAuthTokenService.ConfigTokenProvider = () => _settings?.LastAuthToken;
            JmsAuthTokenService.ReallyExpiredCallback = NotifyJmsLoginRequired;

            JmsAuthStateService.TokenUpdated += token =>
            {
                _settings.LastAuthToken = token;
                CapturedAuthToken = token;
                _ = SaveAuthTokenDebouncedAsync();
            };
            LoadSourceFingerprintCache();

            File.WriteAllText("debug.log", "App started\n");
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                File.WriteAllText("crash.log", e.ExceptionObject.ToString());
            };

            // UI styling
            tabDKCH_inputNewBill.Font = new System.Drawing.Font("Segoe UI Semibold", 12f, System.Drawing.FontStyle.Bold);
            tabDKCH_inputNewBill.WordWrap = false;
            tabDKCH_newBillDone.WordWrap = false;

            string sharedFolder = AppPaths.BrowserDataDir;
            var sharedProps = new Microsoft.Web.WebView2.WinForms.CoreWebView2CreationProperties()
            {
                UserDataFolder = sharedFolder
            };

            // (tabDash and tabChat bindings moved to FullStackOperation)
            // The auto-sync timer drives background inventory/database tracking.
            // It is ULTRA-only: for BASE we never start it (and the tick guards
            // again defensively).
            _autoSyncTimer.Interval = 1000;
            _autoSyncTimer.Tick += async (s, e) => await HandleAutoSyncTickAsync(_appCts.Token);
            if (_tierPolicy.EnableBackgroundAutoSync)
            {
                _autoSyncTimer.Start();
            }
            else
            {
                AppLogger.Info($"AutoSync timer not started for {_tierPolicy.Tier}.");
            }

            var doubleBufferPropertyInfo = tabTracking_dataView.GetType().GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            doubleBufferPropertyInfo?.SetValue(tabTracking_dataView, true, null);

            // ================= CẤU HÌNH WEBVIEW =================
            tabHome_webView.CreationProperties = sharedProps;
            tabDKCH_webView.CreationProperties = sharedProps;
            tabPrint_printPreview.CreationProperties = sharedProps;

            tabDKCH_sheetName.SelectedItem = _settings.DefaultSheet;
            tabDKCH_useSheet.Active = _settings.UseSheetByDefault;
            tabDKCH_numRow.Value = _settings.DefaultRowCount;

            // ================= GẮN EVENT UI =================
            tabTracking_inputWaybill.KeyDown += tabTracking_inputWaybill_KeyDown;
            tabPrint_inputWaybill.KeyDown += tabPrint_inputWaybill_KeyDown;
            tabPrint_btnSelectAll.CheckedChanged += tabPrint_btnSelectAll_CheckedChanged;
            tabPrint_printFunc.SelectedIndexChanged += TabPrint_printFunc_SelectedIndexChanged;
            tabHome_webView.NavigationCompleted += tabHome_WebView_NavigationCompleted;

            // DKCH buttons
            tabDKCH_btnDKCH1.Visible = true;
            tabDKCH_btnDKCH2.Visible = true;
            tabDKCH_btnStop.Visible = false;
            tabDKCH_btnDKCH1.Enabled = false;
            tabDKCH_btnDKCH2.Enabled = false;
            UpdateDkchButtonsByState(false);

            _dkchUiStateTimer = new System.Windows.Forms.Timer();
            _dkchUiStateTimer.Interval = 300;
            _dkchUiStateTimer.Tick += (s, e) => UpdateDkchButtonsByState((_dkchManager?.IsRunning == true) || _isDkchStarting);
            _dkchUiStateTimer.Start();

            CheckForIllegalCrossThreadCalls = false;
            this.KeyPreview = true;
            InitializeAppCaptureUserActions();

            _dkchManager = new DkchManager();
            _dkchManager.OnSaveCountChanged += (count) => tabDKCH_countSave.Text = $" OK: " + count.ToString();
            _dkchManager.OnTrackingHistoryChanged += (history) =>
            {
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() => FormatNowTracking(history ?? "Không có dữ liệu")));
                }
                else
                {
                    FormatNowTracking(history ?? "Không có dữ liệu");
                }
            };
            _dkchManager.OnWaybillCompleted += AppendDoneWaybill;

        }

        private void InitializeAppCaptureUserActions()
        {
            if (!AppCaptureManager.Instance.IsEnabled) return;
            _appUserActionCapture.CaptureFormShown(this, "Main");
            _appUserActionCapture.CaptureTabControl(tabControl, "Main");
            _appUserActionCapture.CaptureTextEnter(tabHome_urlBar, "tabHome", "UrlBar.Enter", () => tabHome_urlBar?.Text ?? "");
            _appUserActionCapture.CaptureButton(tabHome_btnBack, "tabHome", "Back.Click");
            _appUserActionCapture.CaptureButton(tabHome_btnForward, "tabHome", "Forward.Click");
            _appUserActionCapture.CaptureButton(tabHome_btnReload, "tabHome", "Reload.Click");
            _appUserActionCapture.CaptureButton(tabHome_btnHome, "tabHome", "Home.Click");
            _appUserActionCapture.CaptureButton(tabDKCH_btnDKCH1, "tabDKCH", "DKCH1.Click");
            _appUserActionCapture.CaptureButton(tabDKCH_btnDKCH2, "tabDKCH", "DKCH2.Click");
            _appUserActionCapture.CaptureButton(tabDKCH_btnStop, "tabDKCH", "Stop.Click");
            _appUserActionCapture.CaptureTextEnter(tabDKCH_inputNewBill, "tabDKCH", "WaybillInput.Enter", () => tabDKCH_inputNewBill?.Text ?? "");
            _appUserActionCapture.CaptureButton(tabTracking_btnSearch, "tabTracking", "Search.Click");
            _appUserActionCapture.CaptureTextEnter(tabTracking_inputWaybill, "tabTracking", "WaybillInput.Enter", () => tabTracking_inputWaybill?.Text ?? "");
            _appUserActionCapture.CaptureButton(tabPrint_btnTimKiem, "tabPrint", "Search.Click");
            _appUserActionCapture.CaptureButton(tabPrint_btnPrint, "tabPrint", "Print.Click");
            _appUserActionCapture.CaptureTextEnter(tabPrint_inputWaybill, "tabPrint", "WaybillInput.Enter", () => tabPrint_inputWaybill?.Text ?? "");
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_webDebugInspectorEnabled && keyData == (Keys.Control | Keys.Shift | Keys.F12))
            {
                _ = ExportActiveWebDebugBundleAsync();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private async Task InitializeWebDebugInspectorsAsync(CancellationToken token)
        {
#if DEBUG
            if (!IsWebDebugInspectorFlagEnabled()) return;

            try
            {
                _homeWebDebugInspector = new WebViewDevToolsInspector("HOME");
                _dkchWebDebugInspector = new WebViewDevToolsInspector("DKCH");
                _printWebDebugInspector = new WebViewDevToolsInspector("PRINT");

                await _homeWebDebugInspector.AttachAsync(tabHome_webView, token);
                await _dkchWebDebugInspector.AttachAsync(tabDKCH_webView, token);
                await _printWebDebugInspector.AttachAsync(tabPrint_printPreview, token);

                _webDebugInspectorEnabled = true;
                AppLogger.Info($"[WebDebugInspector] enabled hotkey=Ctrl+Shift+F12 outputRoot='{Path.Combine(AppPaths.UserDataDir, "debug", "webview-captures")}'");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _webDebugInspectorEnabled = false;
                AppLogger.Warning($"[WebDebugInspector] failed to initialize: {ex.Message}");
            }
#else
            await Task.CompletedTask;
#endif
        }

        private async Task InitializeAppCaptureWebViewsAsync(CancellationToken token)
        {
            if (!AppCaptureManager.Instance.IsEnabled) return;
            try
            {
                _homeAppCapture = new WebViewCaptureService(AppCaptureManager.Instance);
                _dkchAppCapture = new WebViewCaptureService(AppCaptureManager.Instance);
                await _homeAppCapture.AttachAsync(tabHome_webView, "tabHome", token);
                await _dkchAppCapture.AttachAsync(tabDKCH_webView, "tabDKCH", token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppCaptureManager.Instance.RecordError(ex, "Main.InitializeAppCaptureWebViews");
                AppLogger.Warning($"[AppCapture] WebView attach failed: {ex.Message}");
            }
        }

        private static bool IsWebDebugInspectorFlagEnabled()
        {
#if DEBUG
            string env = Environment.GetEnvironmentVariable("AUTOJMS_ENABLE_WEB_DEBUG_INSPECTOR") ?? "";
            if (env.Equals("1", StringComparison.OrdinalIgnoreCase)
                || env.Equals("true", StringComparison.OrdinalIgnoreCase)
                || env.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string flagPath = Path.Combine(AppPaths.UserDataDir, "enable-web-debug-inspector.flag");
            return File.Exists(flagPath);
#else
            return false;
#endif
        }

        private WebViewDevToolsInspector GetActiveWebDebugInspector()
        {
            if (tabControl?.SelectedTab == tabDKCH) return _dkchWebDebugInspector;
            if (tabControl?.SelectedTab == tabPrint) return _printWebDebugInspector;
            return _homeWebDebugInspector;
        }

        private async Task ExportActiveWebDebugBundleAsync()
        {
            if (_webDebugExportRunning) return;

            var inspector = GetActiveWebDebugInspector();
            if (inspector == null || !inspector.IsAttached)
            {
                UIMessageTip.ShowWarning("WebView inspector chưa sẵn sàng.");
                return;
            }

            _webDebugExportRunning = true;
            try
            {
                var result = await _webDebugExportService.ExportAsync(inspector, _appCts.Token);
                AppLogger.Info($"[WebDebugInspector] export surface={inspector.SurfaceName} dir='{result.DirectoryPath}' files={result.Files.Count}");
                UIMessageTip.Show("Đã export WebView debug bundle.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppLogger.Error("[WebDebugInspector] export failed", ex);
                UIMessageTip.ShowError("Export WebView debug thất bại: " + ex.Message);
            }
            finally
            {
                _webDebugExportRunning = false;
            }
        }

        private void DisposeWebDebugInspectors()
        {
            try { _homeWebDebugInspector?.Dispose(); } catch { }
            try { _dkchWebDebugInspector?.Dispose(); } catch { }
            try { _printWebDebugInspector?.Dispose(); } catch { }
        }

        private void DisposeAppCaptureWebViews()
        {
            try { _homeAppCapture?.Dispose(); } catch { }
            try { _dkchAppCapture?.Dispose(); } catch { }
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            InitNetworkUI();

            string versionString = AppVersion.Current;

            if (tabAbout_lblVersion != null)
            {
                tabAbout_lblVersion.Text = $"Phiên bản hiện tại: v{versionString}";
            }

            InitializeAboutSummary();

            // Re-apply theme to style dynamically created controls
            if (Enum.TryParse<UI.ThemeMode>(_settings.Theme, out var themeModeOnLoad))
            {
                UI.AppTheme.CurrentTheme = themeModeOnLoad;
            }
            UI.AppTheme.Apply(this);

            if (this.IsDisposed) return;

            _ = tabHome_webView.Handle;
            _ = tabDKCH_webView.Handle;
            _ = tabPrint_printPreview.Handle;

            try
            {
                await tabHome_webView.EnsureCoreWebView2Async(null);
                await tabDKCH_webView.EnsureCoreWebView2Async(null);
                await tabPrint_printPreview.EnsureCoreWebView2Async(null);

                tabHome_webView.CoreWebView2.Settings.UserAgent = CHROME_USER_AGENT;
                tabDKCH_webView.CoreWebView2.Settings.UserAgent = CHROME_USER_AGENT;
                tabPrint_printPreview.CoreWebView2.Settings.UserAgent = CHROME_USER_AGENT;

                tabHome_webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = true;
                tabHome_webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = true;
                tabDKCH_webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = true;
                tabDKCH_webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = true;
                RegisterZoomSyncHandlers();
                ApplyZoomFactor();

                tabHome_webView.CoreWebView2.Navigate(JmsHomeUrl);
                tabDKCH_webView.CoreWebView2.Navigate(JmsHomeUrl);

                tabHome_webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Fetch);
                tabHome_webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.XmlHttpRequest);
                tabHome_webView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

                tabDKCH_webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Fetch);
                tabDKCH_webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.XmlHttpRequest);
                tabDKCH_webView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;
                tabDKCH_webView.CoreWebView2.NavigationCompleted += (s, args) => { if (args.IsSuccess) ApplyZoomFactor(); };

                await InitializeWebDebugInspectorsAsync(_appCts.Token);
                await InitializeAppCaptureWebViewsAsync(_appCts.Token);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khởi tạo trình duyệt: " + ex.Message);
            }

            // ================= KHỞI TẠO SERVICE =================
            _trackingService = new WaybillTrackingService(tabTracking_dataView, tabTracking_process);
            _printService = new PrintService(
                tabPrint_dataView,
                _trackingService,
                new PrintSafetyGuard(),
                () => JmsAuthStateService.CurrentToken,
                _siteContextProvider);
            tabPrint_dataView.Visible = false;
            tabPrint_dataView.DataBindingComplete += (s, e) =>
            {
                if (tabPrint_dataView.Columns.Contains("Select"))
                {
                    tabPrint_dataView.Columns["Select"].HeaderText = "Chọn";
                    tabPrint_dataView.Columns["Select"].Width = 50;
                }
            };

            _printService.OnPrintStatsChanged += (selectedCount, totalCount) =>
            {
                this.Invoke((MethodInvoker)delegate
                {
                    tabPrint_countSelect.Text = "Đang chọn:" + selectedCount.ToString();
                    tabPrint_countSum.Text = "Tổng:" + totalCount.ToString();
                });
            };
            _printService.OnPrintSafetyBlocked += HandlePrintSafetyBlocked;
            InitializeTabPrintPrinterControls();

            // Grids moved to FullStackOperation
            ApplyStandardGridSettings(tabTracking_dataView);
            ApplyStandardGridSettings(tabPrint_dataView);

            _dkchManager.SetWebView(tabDKCH_webView);
            _dkchManager.SetTrackingService(_trackingService);
            _dkchManager.SetSettingsGetter(() => (
                useSheet: tabDKCH_useSheet.Active,
                sheetName: tabDKCH_sheetName.Text,
                rowCount: (int)tabDKCH_numRow.Value
            ));
            _dkchManager.StartDaemon();

            tabDKCH_btnDKCH1.Enabled = true;
            tabDKCH_btnDKCH2.Enabled = true;

            await WebViewHost.InitAsync(tabDKCH_webView);
            ApplyZoomFactor();

            await WebViewHost.NavigateAsync(_settings.DefaultUrl);
            await Task.Delay(2000);
            await RefreshAuthTokenAsync();

            // Validate existing token: if LastAuthToken exists but RefreshAuthTokenAsync returned nothing, clear it
            if (!JmsAuthStateService.HasToken && !string.IsNullOrWhiteSpace(_settings.LastAuthToken))
            {
                bool? valid = await JmsAuthStateService.ValidateTokenAsync(_settings.LastAuthToken);
                if (valid == true)
                {
                    JmsAuthStateService.SetToken(_settings.LastAuthToken);
                    CapturedAuthToken = _settings.LastAuthToken;
                    AuthStateService.Instance.SetToken(_settings.LastAuthToken);
                }
                else if (valid == false)
                {
                    AppLogger.Warning("Stored LastAuthToken is expired — clearing.");
                    _settings.LastAuthToken = "";
                    CapturedAuthToken = "";
                    _userSettings.Save(_settings);
                }
            }

            // Background startup sync (inventory fetch + database tracking) is
            // ULTRA-only. For BASE we skip it entirely — the TRACKING and PRINT
            // tabs still work on demand via explicit user actions.
            if (_tierPolicy.EnableStartupInventorySync || _tierPolicy.EnableStartupDatabaseTracking)
            {
                _ = RunStartupSyncAsync(_appCts.Token);
            }
            else
            {
                AppLogger.Info($"Background inventory sync disabled for {_tierPolicy.Tier}.");
                AppLogger.Info($"Background database tracking disabled for {_tierPolicy.Tier}.");
            }

            AppLogger.Info("Startup tối thiểu hoàn tất. Database/Google Sheet chỉ chạy khi người dùng thao tác.");
        }

        // ======================================================================================
        // HỆ THỐNG ĐỒNG BỘ THỦ CÔNG (SUPABASE CLOUD)
        // ======================================================================================

        private async Task RunStartupSyncAsync(CancellationToken ct)
        {
            // Belt-and-suspenders: background startup sync is ULTRA-only.
            if (_tierPolicy == null ||
                !(_tierPolicy.EnableStartupInventorySync || _tierPolicy.EnableStartupDatabaseTracking))
            {
                AppLogger.Info($"RunStartupSync skipped for {_tierPolicy?.Tier ?? "BASE"}.");
                return;
            }

            if (!await _syncGate.WaitAsync(0, ct)) return;
            try
            {
                _lastSyncAttemptAtUtc = DateTime.UtcNow;
                await ExecuteSyncWorkflowAsync(ct, forceRefreshLease: true, sourceHint: "CLOUD");
            }
            finally
            {
                _syncGate.Release();
            }
        }

        private async Task HandleAutoSyncTickAsync(CancellationToken ct)
        {
            // Defensive guard: even if the timer somehow runs, BASE never does
            // background auto-sync.
            if (_tierPolicy == null || !_tierPolicy.EnableBackgroundAutoSync) return;

            if (_syncGate.CurrentCount == 0) return;
            if (!ShouldRunAutoSyncNow()) return;
            if (!await _syncGate.WaitAsync(0, ct)) return;

            try
            {
                var slot = GetCurrentAutoSyncSlotUtc(DateTime.UtcNow);
                if (!slot.HasValue) return;
                await ExecuteSyncWorkflowAsync(ct, forceRefreshLease: false, sourceHint: "CLOUD");
                _lastSuccessfulSyncAtUtc = DateTime.UtcNow;
            }
            finally
            {
                _syncGate.Release();
            }
        }

        private async Task ExecuteSyncWorkflowAsync(CancellationToken ct, bool forceRefreshLease, string sourceHint)
        {
            await SupabaseDbService.InitializeAsync();

            bool lockAcquired = false;
            try
            {
                lockAcquired = await SupabaseDbService.TryAcquireInventoryLeaseAsync((int)_leaseDuration.TotalSeconds);
                if (!lockAcquired)
                {
                    AppLogger.Info("[Sync] Không lấy được lock, bỏ qua lần update này.");
                    return;
                }

                var dataSourceOption = string.IsNullOrWhiteSpace(sourceHint) ? "CLOUD" : sourceHint;
                List<string> sourceWaybills = dataSourceOption == "PHATLAI"
                    ? await ZaloChatService.GetWaybillsFromPhatLaiAsync()
                    : await InventorySyncService.FetchInventoryWaybillsManualAsync(ct);

                if (sourceWaybills.Count > 0)
                {
                    var currentSourceHash = BuildStringListHash(sourceWaybills);
                    if (!string.Equals(_lastSourceWaybillsHash, currentSourceHash, StringComparison.Ordinal))
                    {
                        var normalizedWaybills = sourceWaybills.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        var newWaybills = normalizedWaybills.Except(_lastSourceWaybillsSnapshot, StringComparer.OrdinalIgnoreCase).ToList();
                        if (newWaybills.Count > 0)
                        {
                            await SupabaseDbService.UpsertNewWaybillsOnlyAsync(newWaybills);
                            await DatabaseTracking.RunBackgroundTrackingAsync(newWaybills, dataSourceOption, ct);
                            await SupabaseDbService.UpdateInventorySyncHeartbeatAsync(SupabaseDbService.MachineId);
                        }

                        _lastSourceWaybillsHash = currentSourceHash;
                        _lastSourceWaybillsSnapshot = normalizedWaybills;
                    }
                }

                if (ct.IsCancellationRequested) return;

                _cloudData = await SupabaseDbService.GetActiveWaybillsAsync();
                _cloudDataHashVersion = string.Empty;
                _dashGridHashVersion = string.Empty;
                _chatGridHashVersion = string.Empty;
                _dashStatusHashVersion = string.Empty;
                _chatStatusHashVersion = string.Empty;
                SaveSourceFingerprintCache();
                _lastSuccessfulSyncAtUtc = DateTime.UtcNow;
            }
            finally
            {
                if (lockAcquired)
                {
                    try { await SupabaseDbService.ReleaseInventoryLeaseAsync(); } catch { }
                }
            }
        }

        private bool CanRunManualSync()
        {
            var now = DateTime.UtcNow;
            if (_lastSyncAttemptAtUtc != DateTime.MinValue && now - _lastSyncAttemptAtUtc < _manualSyncCooldown)
            {
                AppLogger.Info("[Sync] Bỏ qua do chống spam update thủ công.");
                return false;
            }
            return true;
        }

        private bool ShouldRunAutoSyncNow()
        {
            var slot = GetCurrentAutoSyncSlotUtc(DateTime.UtcNow);
            return slot.HasValue && (_lastSuccessfulSyncAtUtc == DateTime.MinValue || _lastSuccessfulSyncAtUtc < slot.Value);
        }

        private static DateTime? GetCurrentAutoSyncSlotUtc(DateTime nowUtc)
        {
            if (nowUtc.Hour < 8 || nowUtc.Hour > 23) return null;
            if (nowUtc.Hour == 23 && nowUtc.Minute > 30) return null;

            int minute = nowUtc.Minute >= 30 ? 30 : 0;
            return new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, minute, 0, DateTimeKind.Utc);
        }

        // UI updates for tabDash and tabChat removed. Handled by FullStackOperation.

        private static string BuildStringListHash(IEnumerable<string> values)
        {
            if (values == null) return string.Empty;
            return ComputeStableHash(string.Join("|", values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).OrderBy(v => v, StringComparer.OrdinalIgnoreCase)));
        }

        private static string ComputeStableHash(string value)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
            return Convert.ToHexString(bytes);
        }

        private void LoadSourceFingerprintCache()
        {
            try
            {
                if (!File.Exists(_syncCacheFilePath)) return;
                var json = File.ReadAllText(_syncCacheFilePath);
                var snapshot = JsonSerializer.Deserialize<SourceFingerprintSnapshot>(json, AppConfig.CreateJsonOptions());
                if (snapshot == null) return;

                _cloudSourceFingerprintHash = snapshot.CloudHash ?? string.Empty;
                _phatLaiSourceFingerprintHash = snapshot.PhatLaiHash ?? string.Empty;
                _lastSourceWaybillsHash = snapshot.LastSourceWaybillsHash ?? string.Empty;
                _lastSourceWaybillsSnapshot = snapshot.LastSourceWaybills ?? new List<string>();
                if (snapshot.RowFingerprints != null)
                {
                    _sourceRowFingerprintCache.Clear();
                    foreach (var kv in snapshot.RowFingerprints)
                    {
                        _sourceRowFingerprintCache[kv.Key] = kv.Value;
                    }
                }
            }
            catch { }
        }

        private void SaveSourceFingerprintCache()
        {
            try
            {
                var dir = Path.GetDirectoryName(_syncCacheFilePath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var snapshot = new SourceFingerprintSnapshot
                {
                    CloudHash = _cloudSourceFingerprintHash,
                    PhatLaiHash = _phatLaiSourceFingerprintHash,
                    LastSourceWaybillsHash = _lastSourceWaybillsHash,
                    LastSourceWaybills = _lastSourceWaybillsSnapshot,
                    RowFingerprints = new Dictionary<string, string>(_sourceRowFingerprintCache, StringComparer.OrdinalIgnoreCase)
                };

                File.WriteAllText(_syncCacheFilePath, JsonSerializer.Serialize(snapshot, AppConfig.CreateJsonOptions()));
            }
            catch { }
        }

        private sealed class SourceFingerprintSnapshot
        {
            public string CloudHash { get; set; }
            public string PhatLaiHash { get; set; }
            public string LastSourceWaybillsHash { get; set; }
            public List<string> LastSourceWaybills { get; set; }
            public Dictionary<string, string> RowFingerprints { get; set; }
        }

        private void ApplyStandardGridSettings(DataGridView grid)
        {
            if (grid == null) return;
            grid.ReadOnly = true;
            grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.AllowUserToResizeColumns = true;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.MultiSelect = true;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = grid == tabTracking_dataView
                ? DataGridViewAutoSizeColumnsMode.None
                : DataGridViewAutoSizeColumnsMode.DisplayedCells;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.RowTemplate.Height = 27;
            grid.ColumnHeadersHeight = 34;
            float gridFontSize = grid == tabTracking_dataView ? 8.5F : 7.5F;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", gridFontSize, FontStyle.Bold);
            grid.DefaultCellStyle.Font = new Font("Segoe UI", gridFontSize, FontStyle.Regular);
            grid.DataError -= MainGrid_DataError;
            grid.DataError += MainGrid_DataError;
            if (grid is Sunny.UI.UIDataGridView uiGrid)
            {
                uiGrid.StripeOddColor = System.Drawing.Color.White;
                uiGrid.StripeEvenColor = System.Drawing.Color.White;
            }
        }

        private void MainGrid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
            e.Cancel = true;
            AppLogger.Warning(
                "[Main] DataGridView data error suppressed " +
                $"grid={(sender as DataGridView)?.Name ?? "-"}; row={e.RowIndex}; col={e.ColumnIndex}; " +
                $"context={e.Context}; message={e.Exception?.Message}");
        }

        // ======================================================================================
        // QUẢN LÝ APP & MẠNG 
        // ======================================================================================

        private bool _isExiting = false;

        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_isExiting) return;

            if (e.CloseReason == CloseReason.UserClosing)
            {
                bool confirm = ShowCustomExitDialog();
                if (!confirm)
                {
                    e.Cancel = true;
                    return;
                }
            }

            _isExiting = true;

            this.Hide();
            _appCts.Cancel();
            DisposeAppCaptureWebViews();
            DisposeWebDebugInspectors();

            Task.Run(async () =>
            {
                try
                {
                    await SupabaseDbService.ReleaseInventoryLeaseAsync();
                    // Nếu bạn có dùng khóa tracking thì mở dòng dưới
                    // await SupabaseDbService.ReleaseLeaseAsync("tracking_worker"); 
                }
                catch { }
            }).Wait(1000);
        }

        private bool ShowCustomExitDialog()
        {
            using (UIForm form = new UIForm())
            {
                form.Text = "Đóng ứng dụng";
                form.ClientSize = new System.Drawing.Size(450, 220);
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular);

                UILabel lblMsg = new UILabel();
                lblMsg.Text = "Cứ ngỡ cống hiến trăm năm...\nAi ngờ 5h00.pm";
                lblMsg.Font = new System.Drawing.Font("Tahoma", 13F, System.Drawing.FontStyle.Regular);
                lblMsg.Location = new System.Drawing.Point(20, 60);
                lblMsg.Size = new System.Drawing.Size(410, 70);
                lblMsg.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
                form.Controls.Add(lblMsg);

                UIButton btnYes = new UIButton();
                btnYes.Text = "Thoát ngay";
                btnYes.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
                btnYes.Size = new System.Drawing.Size(140, 40);
                btnYes.Location = new System.Drawing.Point(60, 150);
                btnYes.DialogResult = DialogResult.Yes;
                btnYes.FillColor = System.Drawing.Color.IndianRed;
                btnYes.RectColor = System.Drawing.Color.IndianRed;
                btnYes.FillHoverColor = System.Drawing.Color.Red;
                btnYes.RectHoverColor = System.Drawing.Color.DarkRed;
                btnYes.FillPressColor = System.Drawing.Color.Maroon;
                btnYes.RectPressColor = System.Drawing.Color.Maroon;
                form.Controls.Add(btnYes);

                UIButton btnNo = new UIButton();
                btnNo.Text = "Hủy bỏ";
                btnNo.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
                btnNo.Size = new System.Drawing.Size(140, 40);
                btnNo.Location = new System.Drawing.Point(250, 150);
                btnNo.DialogResult = DialogResult.No;
                form.Controls.Add(btnNo);

                return form.ShowDialog() == DialogResult.Yes;
            }
        }

        private void InitNetworkUI()
        {
            lblNetworkStatus = new UILabel();
            lblNetworkStatus.Name = "lblNetworkStatus";
            lblNetworkStatus.AutoSize = true;
            lblNetworkStatus.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold);
            lblNetworkStatus.BackColor = Color.Transparent;
            lblNetworkStatus.Parent = this;
            lblNetworkStatus.BringToFront();
            lblNetworkStatus.BringToFront();
            this.Controls.Add(lblNetworkStatus);

            UpdateNetworkUI(NetworkStatus.Online);
            NetworkState.OnChanged += UpdateNetworkUI;
            this.SizeChanged += (s, e) => RepositionNetworkLabel();
        }

        private void RepositionNetworkLabel()
        {
            if (lblNetworkStatus != null)
                lblNetworkStatus.Location = new Point(this.Width - lblNetworkStatus.Width - 100, 9);
        }

        private void UpdateNetworkUI(NetworkStatus status)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateNetworkUI(status)));
                return;
            }

            _currentNetworkStatus = status;

            bool isRed = UI.AppTheme.CurrentTheme == UI.ThemeMode.Red;
            bool isDark = UI.AppTheme.CurrentTheme == UI.ThemeMode.Dark;

            switch (status)
            {
                case NetworkStatus.Online:
                    lblNetworkStatus.Text = "● Online";
                    if (isRed) lblNetworkStatus.ForeColor = Color.White;
                    else if (isDark) lblNetworkStatus.ForeColor = Color.LimeGreen;
                    else lblNetworkStatus.ForeColor = Color.FromArgb(0, 240, 100);
                    break;
                case NetworkStatus.Unstable:
                    lblNetworkStatus.Text = "● Mạng chậm";
                    if (isRed) lblNetworkStatus.ForeColor = Color.White;
                    else if (isDark) lblNetworkStatus.ForeColor = Color.Yellow;
                    else lblNetworkStatus.ForeColor = Color.FromArgb(253, 224, 71);
                    break;
                case NetworkStatus.Offline:
                default:
                    lblNetworkStatus.Text = "● Mất kết nối";
                    if (isRed) lblNetworkStatus.ForeColor = Color.Black;
                    else if (isDark) lblNetworkStatus.ForeColor = Color.Red;
                    else lblNetworkStatus.ForeColor = Color.FromArgb(252, 115, 115);
                    break;
            }

            RepositionNetworkLabel();
        }


        private void AlignLeftPanelControls()
        {
            // DATA section inputs alignment
            if (tabDKCH_sheetName != null && tabDKCH_numRow != null && tabDKCH_useSheet != null)
            {
                tabDKCH_sheetName.Margin = new Padding(10, 0, 0, 0);
                tabDKCH_numRow.Margin = new Padding(10, 0, 0, 0);
                tabDKCH_useSheet.Margin = new Padding(12, 0, 0, 0);

                tabDKCH_sheetName.Size = new Size(115, 28);
                tabDKCH_numRow.Size = new Size(115, 28);
            }

            // CONTROL section buttons full width & matching height alignment
            if (tabDKCH_Home != null && tabDKCH_btnDKCH1 != null && tabDKCH_btnDKCH2 != null && uiTableLayoutPanel9 != null)
            {
                tabDKCH_Home.Dock = DockStyle.Fill;
                tabDKCH_Home.Margin = new Padding(3, 3, 3, 3);
                tabDKCH_Home.Size = new Size(258, 32);

                tabDKCH_btnDKCH1.Margin = new Padding(3, 4, 3, 4);
                tabDKCH_btnDKCH2.Margin = new Padding(3, 4, 3, 4);

                tabDKCH_btnDKCH1.Height = 32;
                tabDKCH_btnDKCH2.Height = 32;

                if (uiTableLayoutPanel9.RowStyles.Count > 0)
                {
                    uiTableLayoutPanel9.RowStyles[0] = new RowStyle(SizeType.Absolute, 38F);
                }
            }
        }

        private void tabAbout_btnTerms_Click(object sender, EventArgs e)
        {
            using var dialog = new TermsDialog();
            dialog.ShowDialog(this);
        }

        private void InitializeAboutSummary()
        {
            if (tabAbout == null || tabAbout.IsDisposed) return;

            // Guard: avoid double-inject if OnLoad runs twice
            foreach (Control existing in tabAbout.Controls)
            {
                if (existing.Name == "tabAbout_summaryPanel" || existing.Name == "tabAbout_themePanel")
                    return;
            }

            // Restructure uiTableLayoutPanel22 rows so contact stays at the bottom
            // and there is a stretchy middle row that can host the summary card.
            // Original 7 rows (Absolute) sum to 385px in a ~700px panel, leaving the
            // contact stuck in the middle. We add a Theme selector row and a Percent-100 spacer row,
            // shifting the link/contact controls down.
            if (uiTableLayoutPanel22 != null && uiTableLayoutPanel22.RowStyles.Count >= 6)
            {
                // Re-place link and contact down by two rows
                uiTableLayoutPanel22.SetRow(uiLinkLabel1, 7);
                uiTableLayoutPanel22.SetRow(uiLabel22, 8);

                if (uiTableLayoutPanel22.RowStyles.Count == 7)
                {
                    uiTableLayoutPanel22.RowStyles.Insert(5, new RowStyle(SizeType.Absolute, 50F)); // Theme selector row
                    uiTableLayoutPanel22.RowStyles.Insert(6, new RowStyle(SizeType.Percent, 100F)); // Stretchy summary row
                    uiTableLayoutPanel22.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
                    uiTableLayoutPanel22.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
                }
                uiTableLayoutPanel22.RowCount = 9;
            }

            // Create theme selector panel
            var themePanel = new Panel
            {
                Name = "tabAbout_themePanel",
                Height = 40,
                Dock = DockStyle.Fill,
                Margin = new Padding(10, 0, 10, 0),
                BackColor = Color.Transparent
            };

            var lblTheme = new UILabel
            {
                Name = "tabAbout_lblTheme",
                Text = "Giao diện (Theme):",
                Size = new Size(130, 30),
                Location = new Point(105, 5),
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };

            var cboTheme = new UIComboBox
            {
                Name = "tabAbout_cboTheme",
                Size = new Size(150, 30),
                Location = new Point(245, 5),
                DropDownStyle = UIDropDownStyle.DropDownList,
                Radius = 6
            };
            cboTheme.Items.Add("Light");
            cboTheme.Items.Add("Red");
            cboTheme.Items.Add("Dark");
            cboTheme.SelectedItem = _settings.Theme ?? "Light";

            cboTheme.SelectedIndexChanged += (s, ev) =>
            {
                string selectedTheme = cboTheme.SelectedItem?.ToString() ?? "Light";
                if (selectedTheme != _settings.Theme)
                {
                    _settings.Theme = selectedTheme;
                    _userSettings.Save(_settings);

                    if (Enum.TryParse<UI.ThemeMode>(selectedTheme, out var mode))
                    {
                        UI.AppTheme.CurrentTheme = mode;
                        UI.AppTheme.Apply(this);
                        this.Invalidate(true);
                        this.Update();
                    }
                }
            };

            themePanel.Controls.Add(lblTheme);
            themePanel.Controls.Add(cboTheme);

            if (uiTableLayoutPanel22 != null)
            {
                uiTableLayoutPanel22.Controls.Add(themePanel, 0, 5);
            }

            // Place the summary card inside the new stretchy row (row 6)
            var summaryPanel = new Sunny.UI.UIPanel
            {
                Name = "tabAbout_summaryPanel",
                BackColor = Color.Transparent,
                FillColor = UI.AppTheme.Colors.CardBackground,
                RectColor = UI.AppTheme.Colors.SubtleBorder,
                Radius = 8,
                Padding = new Padding(16, 12, 16, 12),
                Dock = DockStyle.Fill,
                Margin = new Padding(10, 8, 10, 8)
            };

            var title = new Sunny.UI.UILabel
            {
                Name = "tabAbout_summaryTitle",
                Text = "Tóm tắt điều khoản",
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                ForeColor = UI.AppTheme.Colors.TextPrimary,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Top,
                Height = 26
            };

            var body = new Sunny.UI.UILabel
            {
                Name = "tabAbout_summaryBody",
                Text = TermsContentProvider.GetTermsSummaryText(),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                ForeColor = UI.AppTheme.Colors.TextSecondary,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.TopLeft,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 6, 0, 0)
            };

            summaryPanel.Controls.Add(body);
            summaryPanel.Controls.Add(title);

            if (uiTableLayoutPanel22 != null)
            {
                uiTableLayoutPanel22.Controls.Add(summaryPanel, 0, 6);
            }
            else
            {
                summaryPanel.Location = new Point(30, 320);
                tabAbout.Controls.Add(summaryPanel);
            }
        }

        private async void tabAbout_btnCheckUpdate_Click(object sender, EventArgs e)
        {
            tabAbout_btnCheckUpdate.Enabled = false;
            string originalText = tabAbout_btnCheckUpdate.Text;

            try
            {
                tabAbout_btnCheckUpdate.Text = "Đang tải thông tin...";

                var latest = await FetchUpdateManifestForDialogAsync(_appCts.Token);
                var stableLatest = GetExactUpdateChannel(latest, "stable");
                var betaLatest = GetExactUpdateChannel(latest, "beta");
                var currentVersion = AppVersion.Current;

                AppLogger.Info($"[Update] currentVersion={currentVersion}");
                AppLogger.Info($"[Update] currentAssemblyVersion={GetCurrentAssemblyVersionForUpdateLog()}");
                AppLogger.Info($"[Update] stableLatest={DescribeUpdateChannel(stableLatest)}");
                AppLogger.Info($"[Update] betaLatest={DescribeUpdateChannel(betaLatest)}");
                AppLogger.Info(
                    $"UpdateChannelDialog: current={currentVersion}, " +
                    $"stableLatest={DescribeUpdateChannel(stableLatest)}, " +
                    $"betaLatest={DescribeUpdateChannel(betaLatest)}");

                using var dialog = new UpdateChannelDialog(currentVersion, stableLatest, betaLatest);
                if (dialog.ShowDialog(this) != DialogResult.OK ||
                    dialog.Choice == UpdateChannelChoice.Cancel)
                {
                    AppLogger.Info("UpdateChannelDialog: result=Cancel");
                    return;
                }

                string channel = dialog.Choice == UpdateChannelChoice.Beta ? "beta" : "stable";
                var selectedLatest = channel == "beta" ? betaLatest : stableLatest;

                LogSelectedUpdateChannel(channel, selectedLatest);
                AppLogger.Action(
                    $"UpdateChannelDialog: selectedChannel={channel}, " +
                    $"current={currentVersion}, selectedLatest={DescribeUpdateChannel(selectedLatest)}");

                var updateSvc = new VelopackUpdateService(
                    channel,
                    PrepareForUpdateAsync,
                    (installedVersion, targetVersion, selectedChannel) =>
                    {
                        AppLogger.Info(
                            $"UpdateChannelDialog: downgradePrompt channel={selectedChannel}, " +
                            $"installed={installedVersion ?? "UNKNOWN"}, target={targetVersion ?? "UNKNOWN"}");
                        bool accepted = UpdateChannelDialog.ConfirmDowngrade(
                            this,
                            installedVersion,
                            targetVersion,
                            selectedLatest);
                        AppLogger.Info($"UpdateChannelDialog: downgradeResult={(accepted ? "Allow" : "Cancel")}");
                        return accepted;
                    });

                var progress = new System.Progress<int>(pct =>
                {
                    if (!this.IsDisposed)
                        tabAbout_btnCheckUpdate.Text = $"Đang tải... {pct}%";
                });

                tabAbout_btnCheckUpdate.Text = "Đang kiểm tra...";
                await updateSvc.CheckAndUpdateAsync(progress, _appCts.Token);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể kiểm tra cập nhật.\n\n{ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (!this.IsDisposed)
                {
                    tabAbout_btnCheckUpdate.Enabled = true;
                    tabAbout_btnCheckUpdate.Text = originalText;
                }
            }
        }

        private static async Task<VersionLatest> FetchUpdateManifestForDialogAsync(CancellationToken token)
        {
            try
            {
                var xmlService = new UpdateXmlManifestService(AppConfig.Current.UpdateXmlUrl);
                var xmlManifest = await xmlService.FetchAsync(token);
                if (xmlManifest?.Channels != null && xmlManifest.Channels.Count > 0)
                {
                    AppLogger.Info($"[Update] manifestUrl={AppConfig.Current.UpdateXmlUrl}");
                    return xmlManifest;
                }

                AppLogger.Warning("UpdateChannelDialog: update.xml returned no channels; falling back to version-latest.json.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"UpdateChannelDialog: fetch update.xml failed: {ex.Message}");
            }

            if (Program.SupabaseManifest == null)
            {
                AppLogger.Warning("UpdateChannelDialog: Program.SupabaseManifest is null; showing UNKNOWN channel metadata.");
                return null;
            }

            try
            {
                var latest = await Program.SupabaseManifest.FetchVersionLatestAsync(token);
                AppLogger.Info($"[Update] manifestUrl={ResolveSupabaseVersionLatestUrl()}");
                return latest;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"UpdateChannelDialog: fetch version-latest failed: {ex.Message}");
                return null;
            }
        }

        private static VersionChannel GetExactUpdateChannel(VersionLatest latest, string channel)
        {
            if (latest?.Channels == null) return null;
            return latest.Channels.TryGetValue(channel, out var value) ? value : null;
        }

        private static string DescribeUpdateChannel(VersionChannel channel)
        {
            if (channel == null) return "UNKNOWN";
            return $"version={channel.Version ?? "UNKNOWN"}, display={channel.DisplayVersion ?? "UNKNOWN"}, internal={channel.InternalBuild ?? "UNKNOWN"}, tag={channel.Tag ?? "UNKNOWN"}, prerelease={channel.Prerelease}";
        }

        private static void LogSelectedUpdateChannel(string channel, VersionChannel selectedLatest)
        {
            AppLogger.Info($"[Update] selectedChannel={channel}");
            AppLogger.Info($"[Update] provider={selectedLatest?.Provider ?? "UNKNOWN"}");
            AppLogger.Info($"[Update] latestVersion={selectedLatest?.Version ?? "UNKNOWN"}");
            AppLogger.Info($"[Update] displayVersion={selectedLatest?.DisplayVersion ?? "UNKNOWN"}");
            AppLogger.Info($"[Update] githubRepoUrl={GetGithubRepoUrlForUpdateLog(selectedLatest)}");
            AppLogger.Info($"[Update] tag={selectedLatest?.Tag ?? "UNKNOWN"}");
            AppLogger.Info($"[Update] prerelease={selectedLatest?.Prerelease.ToString() ?? "UNKNOWN"}");
        }

        private static string GetGithubRepoUrlForUpdateLog(VersionChannel channel)
        {
            if (channel == null) return "UNKNOWN";
            if (!string.IsNullOrWhiteSpace(channel.GithubRepoUrl)) return channel.GithubRepoUrl;
            if (!string.IsNullOrWhiteSpace(channel.GithubRepo)) return $"https://github.com/{channel.GithubRepo}";
            return "UNKNOWN";
        }

        private static string GetCurrentAssemblyVersionForUpdateLog()
        {
            var asm = typeof(Main).Assembly;
            return asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion
                ?? asm.GetName().Version?.ToString()
                ?? "UNKNOWN";
        }

        private static string ResolveSupabaseVersionLatestUrl()
        {
            var manifestSvc = Program.SupabaseManifest;
            string path = manifestSvc?.Urls?.VersionLatest;
            if (string.IsNullOrWhiteSpace(path)) return "UNKNOWN";
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return path;
            return $"{manifestSvc.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
        }

        /// <summary>
        /// Stops all running services/loops before Velopack replaces the app binaries.
        /// Called by VelopackUpdateService right before ApplyUpdatesAndRestart.
        /// </summary>
        private async Task PrepareForUpdateAsync(CancellationToken ct)
        {
            AppLogger.Info("PrepareForUpdate: stopping services before applying update...");

            try
            {
                // 1. Stop background sync loop + license/heartbeat-driven timers
                _appCts.Cancel();
            }
            catch (Exception ex) { AppLogger.Warning($"PrepareForUpdate: cancel CTS: {ex.Message}"); }

            try
            {
                // 2. Stop auto-sync timer
                if (_autoSyncTimer != null)
                {
                    _autoSyncTimer.Stop();
                    _autoSyncTimer.Dispose();
                }
            }
            catch (Exception ex) { AppLogger.Warning($"PrepareForUpdate: stop autoSyncTimer: {ex.Message}"); }

            try
            {
                // 3. Stop ZaloService auto-reminder (in FullStack form)
                _zaloChatService?.StopAutoReminder();
            }
            catch (Exception ex) { AppLogger.Warning($"PrepareForUpdate: stop ZaloService: {ex.Message}"); }

            try
            {
                // 4. Close FullStackOperation realtime form (tracking/print/realtime)
                if (_fullStackForm != null && !_fullStackForm.IsDisposed)
                {
                    _fullStackForm.Close();
                    _fullStackForm.Dispose();
                    _fullStackForm = null;
                }
            }
            catch (Exception ex) { AppLogger.Warning($"PrepareForUpdate: close FullStack: {ex.Message}"); }

            try
            {
                // 5. Release Supabase inventory lease
                await SupabaseDbService.ReleaseInventoryLeaseAsync();
            }
            catch (Exception ex) { AppLogger.Warning($"PrepareForUpdate: release lease: {ex.Message}"); }

            try
            {
                // 6. Dispose WebView2 instances so files are not locked
                DisposeWebView(tabHome_webView);
                DisposeWebView(tabDKCH_webView);
                DisposeWebView(tabPrint_printPreview);
            }
            catch (Exception ex) { AppLogger.Warning($"PrepareForUpdate: dispose WebView: {ex.Message}"); }

            // Give pending operations a moment to settle
            try { await Task.Delay(800, CancellationToken.None); } catch { }

            AppLogger.Info("PrepareForUpdate: services stopped, ready to apply update.");
        }

        private static void DisposeWebView(Microsoft.Web.WebView2.WinForms.WebView2 wv)
        {
            try
            {
                if (wv == null) return;
                wv.CoreWebView2?.Stop();
                wv.Dispose();
            }
            catch { }
        }

        private void OnJmsAuthExpired()
        {
            // Clear local state
            lock (_authTokenLock)
            {
                CapturedAuthToken = "";
                _settings.LastAuthToken = "";
            }
            _userSettings.Save(_settings);

            // Stop DKCH manager if running
            _dkchManager?.Stop();

            // Log only — DO NOT show a MessageBox. The token may simply be a
            // false-positive expiry (server flap, transient 401, etc.) and an
            // intrusive popup interrupts the user. Keep this silent and let
            // the next API call re-trigger the captured-token capture flow.
            AppLogger.Warning("JMS auth token expired/invalidated — background tasks stopped, awaiting fresh token capture.");
        }

        private void btn_Download_Click(object sender, EventArgs e)
        {
            try
            {
                if (!Directory.Exists(_downloadFolderPath)) Directory.CreateDirectory(_downloadFolderPath);
                Process.Start(new ProcessStartInfo { FileName = _downloadFolderPath, UseShellExecute = true, Verb = "open" });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể mở thư mục: " + ex.Message);
            }
        }

        // ======================================================================================
        // CÁC HÀM WEBVIEW, TOKEN & TAB ĐIỀU HƯỚNG
        // ======================================================================================

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Pre-create FullStackOperation in background (no Show)
            this.BeginInvoke(new Action(() =>
            {
                if (!_ultraLaunched)
                {
                    _ultraLaunched = true;
                    PreCreateFullStackForm();
                }
            }));
        }

        private void PreCreateFullStackForm()
        {
            // Bypassed tier check temporarily for owner to test from tabHome
            /*
            if (_tierPolicy == null || !_tierPolicy.EnableFullStackOperation)
            {
                AppLogger.Info($"FullStackOperation disabled for {_tierPolicy?.Tier ?? "BASE"} — not pre-created.");
                return;
            }
            */

            try
            {
                _fullStackForm = new FullStackOperation();
                _fullStackForm.FormClosed += (s, e2) => _fullStackForm = null;
                AppLogger.Info("FullStackOperation pre-created in background — type DASH to show");
            }
            catch (Exception ex)
            {
                AppLogger.Error("FullStack pre-create failed", ex);
            }
        }

        private void ShowFullStackForm()
        {
            // Bypassed tier check temporarily for owner to test from tabHome
            /*
            if (_tierPolicy == null || !_tierPolicy.EnableFullStackOperation)
            {
                AppLogger.Info($"ShowFullStackForm ignored for {_tierPolicy?.Tier ?? "BASE"} — ULTRA only.");
                return;
            }
            */

            if (_isShowingFullStackForm)
                return;

            _isShowingFullStackForm = true;
            try
            {
                if (_fullStackForm == null || _fullStackForm.IsDisposed)
                {
                    Cursor.Current = Cursors.WaitCursor;
                    PreCreateFullStackForm();
                    if (_fullStackForm == null) return;
                }

                _fullStackForm.StartPosition = FormStartPosition.CenterScreen;
                _fullStackForm.WindowState = FormWindowState.Normal;
                _fullStackForm.ShowInTaskbar = true;
                _fullStackForm.BackColor = System.Drawing.Color.LightBlue;

                if (!_fullStackForm.Visible)
                    _fullStackForm.Show(this);

                _fullStackForm.WindowState = FormWindowState.Maximized;
                _fullStackForm.Activate();
                _fullStackForm.BringToFront();
                AppLogger.Info("FullStackOperationForm shown via DASH command");
            }
            catch (Exception ex)
            {
                AppLogger.Error("FullStack show failed", ex);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
                _isShowingFullStackForm = false;
            }
        }

        private void TabHome_urlBar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && tabHome_urlBar.Text.Trim().Equals("DASH", StringComparison.OrdinalIgnoreCase))
            {
                e.SuppressKeyPress = true;
                ShowFullStackForm();
                tabHome_urlBar.Text = "";
            }
        }

        private async void tabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;
            try
            {
                if (tabControl.SelectedTab == tabHome)
                {
                    if (_isHomeNeedReload && tabHome_webView != null && tabHome_webView.CoreWebView2 != null)
                    {
                        tabHome_webView.CoreWebView2.Reload();
                        _isHomeNeedReload = false;
                    }
                }
                else if (tabControl.SelectedTab == tabDKCH)
                {
                    if (_isDkchNeedReload && tabDKCH_webView != null && tabDKCH_webView.CoreWebView2 != null)
                    {
                        tabDKCH_webView.CoreWebView2.Reload();
                        _isDkchNeedReload = false;
                    }
                }
                else if (tabControl.SelectedTab == tabTracking)
                {
                    if (string.IsNullOrEmpty(Main.CapturedAuthToken)) await RefreshAuthTokenAsync();
                    if (tabTracking_btnSearch.Enabled)
                    {
                        if (tabTracking_process != null && !tabTracking_process.IsDisposed)
                        {
                            tabTracking_process.Value = 0;
                            tabTracking_process.Visible = false;
                        }
                    }
                }
                // tabChat and tabDash views moved to FullStackOperation form
            }
            catch (Exception ex) { MessageBox.Show("Lỗi xử lý Tab: " + ex.Message); }
        }

        // Candidate HTTP header names the JMS frontend may attach the session
        // token to. Kept in sync with the localStorage keys probed by
        // RefreshAuthTokenAsync so passive capture and recovery never diverge.
        private static readonly string[] AuthTokenHeaderNames =
            { "authToken", "YL_TOKEN", "token", "accessToken" };

        /// <summary>
        /// Apply a freshly-discovered token to every singleton that caches it,
        /// but only when it actually changed. Returns true if it was applied.
        /// Thread-safe; safe to call from the WebView2 resource thread.
        /// </summary>
        private bool ApplyCapturedToken(string token, string source)
        {
            // Never accept a license JWT as the JMS authToken.
            if (JmsAuthTokenService.LooksLikeJwt(token))
            {
                AppLogger.Warning($"Ignored JWT-looking value from {source} — not a JMS authToken.");
                return false;
            }

            // Hard gate: a real JMS authToken is exactly 32 hex chars.
            if (!JmsAuthTokenService.IsValidJmsToken(token))
            {
                AppLogger.Warning($"Ignored non-token value from {source} (len={token?.Length ?? 0}) — not 32-hex.");
                return false;
            }

            lock (_authTokenLock)
            {
                if (string.Equals(_settings.LastAuthToken, token, StringComparison.Ordinal)
                    && CapturedAuthToken == token)
                {
                    return false; // unchanged
                }
                _settings.LastAuthToken = token;
                CapturedAuthToken = token;
            }

            AuthStateService.Instance.SetToken(token);
            JmsAuthStateService.SetToken(token);
            AppLogger.Info($"Auth token captured from {source} (len={token.Length}), authToken={TokenRedactor.MaskToken(token)}");
            return true;
        }

        private void CoreWebView2_WebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                // Probe every known header name — the JMS frontend has shipped the
                // session token under different headers across versions.
                string token = null;
                foreach (var name in AuthTokenHeaderNames)
                {
                    if (e.Request.Headers.Contains(name))
                    {
                        var v = e.Request.Headers.GetHeader(name);
                        if (!string.IsNullOrEmpty(v) && v.Length > 20) { token = v; break; }
                    }
                }

                if (!string.IsNullOrEmpty(token) && ApplyCapturedToken(token, "WebView2 request header"))
                {
                    _ = SaveAuthTokenDebouncedAsync();
                    if (this.InvokeRequired)
                    {
                        this.Invoke(new Action(() =>
                        {
                            if (tabControl.SelectedTab == tabHome) _isDkchNeedReload = true;
                            else if (tabControl.SelectedTab == tabDKCH) _isHomeNeedReload = true;
                        }));
                    }
                    else
                    {
                        if (tabControl.SelectedTab == tabHome) _isDkchNeedReload = true;
                        else if (tabControl.SelectedTab == tabDKCH) _isHomeNeedReload = true;
                    }
                }
            }
            catch { }
        }

        private async Task SaveAuthTokenDebouncedAsync()
        {
            CancellationTokenSource currentCts;
            lock (_authTokenLock)
            {
                _authTokenSaveCts?.Cancel();
                _authTokenSaveCts?.Dispose();
                _authTokenSaveCts = new CancellationTokenSource();
                currentCts = _authTokenSaveCts;
            }
            try
            {
                await Task.Delay(400, currentCts.Token);
                await SettingsManager.SaveAsync(CloneSettingsSnapshot());
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        public async Task RefreshAuthTokenAsync()
        {
            // WebView2 is UI-thread affine. If we're on a background thread
            // (e.g. a parallel tracking batch), marshal the whole read onto the
            // UI thread to avoid "CoreWebView2 can only be accessed from the UI
            // thread."
            if (this.IsHandleCreated && this.InvokeRequired)
            {
                await UiThread.InvokeOnUiAsync(this, async () => { await RefreshAuthTokenCoreAsync(); return true; });
                return;
            }
            await RefreshAuthTokenCoreAsync();
        }

        private async Task RefreshAuthTokenCoreAsync()
        {
            // Try each WebView2 in order — whichever one is logged in to JMS.
            // The user might be on Home or DKCH tab; only one needs a valid session.
            var candidates = new[]
            {
                tabHome_webView,
                tabDKCH_webView,
                tabPrint_printPreview
            };

            // The JMS authToken is a 32-char hex string. We probe known keys
            // first, then sessionStorage, then scan — but EVERY candidate must
            // pass the 32-hex test before we accept it. This prevents capturing
            // unrelated values like yl_ce_sdk_user_id (len=25) or GUIDs.
            const string js = @"(function() {
                var HEX32 = /^[a-fA-F0-9]{32}$/;
                function ok(v){ return typeof v === 'string' && HEX32.test(v); }

                function pick(store) {
                    if (!store) return null;
                    var keys = ['YL_TOKEN','authToken','token','accessToken','jms_token'];
                    for (var i = 0; i < keys.length; i++) {
                        var v = store.getItem(keys[i]);
                        if (ok(v)) return { key: keys[i], value: v };
                    }
                    return null;
                }
                var hit = pick(window.localStorage) || pick(window.sessionStorage);

                if (!hit) {
                    // userData fallback: accept uuid ONLY if it is 32-hex.
                    try {
                        var ud = (window.localStorage && localStorage.getItem('userData'))
                              || (window.sessionStorage && sessionStorage.getItem('userData'));
                        if (ud) {
                            var obj = JSON.parse(ud);
                            if (obj && ok(obj.uuid)) hit = { key: 'userData.uuid', value: obj.uuid };
                        }
                    } catch(e){}
                }

                if (!hit) {
                    // Last resort: scan storage for ANY 32-hex value. The strict
                    // shape means we won't grab ids/flags by accident.
                    try {
                        for (var i = 0; i < localStorage.length; i++) {
                            var k = localStorage.key(i);
                            var v = localStorage.getItem(k);
                            if (ok(v)) { hit = { key: 'scan:' + k, value: v }; break; }
                        }
                    } catch(e){}
                }

                return JSON.stringify({
                    found: !!hit,
                    key:   hit ? hit.key   : '',
                    value: hit ? hit.value : ''
                });
            })();";

            foreach (var wv in candidates)
            {
                if (wv?.CoreWebView2 == null) continue;
                if (!IsJmsOriginWebView(wv)) continue;   // only read JS on a jms.jtexpress.vn page
                try
                {
                    string result = await wv.ExecuteScriptAsync(js);
                    if (string.IsNullOrEmpty(result) || result == "null") continue;

                    string unescaped = JsonSerializer.Deserialize<string>(result);
                    if (string.IsNullOrEmpty(unescaped)) continue;

                    using var doc = JsonDocument.Parse(unescaped);
                    var root = doc.RootElement;
                    if (!root.GetProperty("found").GetBoolean()) continue;

                    string freshToken = root.GetProperty("value").GetString() ?? "";
                    string foundKey = root.TryGetProperty("key", out var kp) ? (kp.GetString() ?? "") : "";

                    // Hard gate: only a 32-hex value is a real JMS authToken.
                    if (!JmsAuthTokenService.IsValidJmsToken(freshToken))
                    {
                        AppLogger.Warning($"RefreshAuthToken: ignored '{foundKey}' (len={freshToken.Length}) — not 32-hex.");
                        continue;
                    }

                    // Push to all the singletons that hold a copy (deduped + logged).
                    ApplyCapturedToken(freshToken, $"WebView2 storage '{foundKey}'");
                    return;
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"RefreshAuthTokenAsync: WebView2 read failed ({ex.Message}). Trying next.");
                }
            }

            AppLogger.Warning("RefreshAuthTokenAsync: no token found in any JMS WebView2 localStorage/sessionStorage.");
        }

        /// <summary>
        /// True if the WebView2 is currently showing a page on the JMS origin
        /// (jms.jtexpress.vn or jmsgw.jtexpress.vn). We only read localStorage
        /// from a JMS page — other origins won't have the JMS authToken and
        /// reading them could surface unrelated values.
        /// </summary>
        private static bool IsJmsOriginWebView(Microsoft.Web.WebView2.WinForms.WebView2 wv)
        {
            try
            {
                string src = wv?.CoreWebView2?.Source;
                if (string.IsNullOrEmpty(src)) return false;
                if (!Uri.TryCreate(src, UriKind.Absolute, out var uri)) return false;
                return uri.Host.EndsWith("jtexpress.vn", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// Reader used by JmsAuthTokenService: refresh the token from a JMS
        /// WebView2 and return the freshest usable token (or null).
        /// RefreshAuthTokenAsync self-marshals to the UI thread.
        /// </summary>
        public async Task<string> GetTokenFromJmsWebViewAsync()
        {
            await RefreshAuthTokenAsync();
            return JmsAuthStateService.HasToken ? JmsAuthStateService.CurrentToken : null;
        }

        // Throttle the "please re-login" toast so a burst of 401s can't spam it.
        private DateTime _lastLoginPromptUtc = DateTime.MinValue;

        /// <summary>
        /// Called only when the token is truly expired after a forced refresh
        /// and one retry. Non-blocking, throttled, never steals focus.
        /// </summary>
        private void NotifyJmsLoginRequired()
        {
            if ((DateTime.UtcNow - _lastLoginPromptUtc).TotalSeconds < 30) return;
            _lastLoginPromptUtc = DateTime.UtcNow;

            try
            {
                Action show = () =>
                {
                    try
                    {
                        Sunny.UI.UIMessageTip.ShowWarning(
                            "Phiên đăng nhập JMS đã hết hạn. Vui lòng đăng nhập lại trong tab HOME.");
                    }
                    catch { }
                };
                if (this.IsHandleCreated && this.InvokeRequired) this.BeginInvoke(show);
                else show();
            }
            catch { }
        }

        /// <summary>
        /// Pull a fresh authToken from any logged-in WebView2 and return it.
        /// Used by JmsAuthStateService.WebViewTokenRefresher to recover before
        /// declaring a token expired. Returns null/empty on failure.
        /// </summary>
        public async Task<string> GetTokenFromAnyWebViewAsync()
        {
            await RefreshAuthTokenAsync();
            return JmsAuthStateService.HasToken ? JmsAuthStateService.CurrentToken : null;
        }

        private void tabHome_WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (tabHome_webView.CoreWebView2 != null)
            {
                tabHome_btnBack.Enabled = tabHome_webView.CoreWebView2.CanGoBack;
                tabHome_btnForward.Enabled = tabHome_webView.CoreWebView2.CanGoForward;
            }
        }

        private void btnBack_Click(object sender, EventArgs e)
        {
            if (tabHome_webView?.CoreWebView2 != null && tabHome_webView.CoreWebView2.CanGoBack)
                tabHome_webView.CoreWebView2.GoBack();
        }

        private void btnForward_Click(object sender, EventArgs e)
        {
            if (tabHome_webView?.CoreWebView2 != null && tabHome_webView.CoreWebView2.CanGoForward)
                tabHome_webView.CoreWebView2.GoForward();
        }

        private void btnReload_Click(object sender, EventArgs e)
        {
            if (tabHome_webView?.CoreWebView2 != null)
            {
                tabHome_webView.CoreWebView2.Reload();
                tabHome_webView.CoreWebView2.Navigate(JmsHomeUrl);
            }
        }

        private void btnHome_Click(object sender, EventArgs e)
        {
            if (tabHome_webView?.CoreWebView2 != null)
                tabHome_webView.CoreWebView2.Navigate(JmsHomeUrl);
        }

        // ======================================================================================
        // TAB ZALO CHAT
        // ======================================================================================

        // tabChat buttons click events moved to FullStackOperation form

        // ======================================================================================
        // TAB DKCH
        // ======================================================================================

        private async void tabDKCH_btnDKCH1_Click(object sender, EventArgs e)
        {
            if (_dkchManager.IsRunning || _isDkchStarting) return;
            _isDkchStarting = true;
            UpdateDkchButtonsByState(true);
            try
            {
                if (!await EnsureDkchPageReadyAsync(_appCts.Token))
                {
                    UpdateDkchButtonsByState(false);
                    return;
                }
                await _dkchManager.StartAsync("DKCH1");
                await RefreshAuthTokenAsync();
                UpdateDkchButtonsByState(_dkchManager.IsRunning);
            }
            catch (Exception ex)
            {
                _dkchManager.Stop();
                UpdateDkchButtonsByState(false);
                UIMessageTip.ShowError("Không thể khởi động DKCH1: " + ex.Message);
            }
            finally
            {
                _isDkchStarting = false;
                UpdateDkchButtonsByState(_dkchManager.IsRunning);
            }
        }

        private async void tabDKCH_btnDKCH2_Click(object sender, EventArgs e)
        {
            if (_dkchManager.IsRunning || _isDkchStarting) return;
            _isDkchStarting = true;
            UpdateDkchButtonsByState(true);
            try
            {
                if (!await EnsureDkchPageReadyAsync(_appCts.Token))
                {
                    UpdateDkchButtonsByState(false);
                    return;
                }
                await _dkchManager.StartAsync("DKCH2");
                UpdateDkchButtonsByState(_dkchManager.IsRunning);
            }
            catch (Exception ex)
            {
                _dkchManager.Stop();
                UpdateDkchButtonsByState(false);
                UIMessageTip.ShowError("Không thể khởi động DKCH2: " + ex.Message);
            }
            finally
            {
                _isDkchStarting = false;
                UpdateDkchButtonsByState(_dkchManager.IsRunning);
            }
        }

        private void tabDKCH_btnStop_Click(object sender, EventArgs e)
        {
            _isDkchStarting = false;
            _dkchManager.Stop();
            UpdateDkchButtonsByState(false);
        }

        private async void btn_Refresh_Click(object sender, EventArgs e)
        {
            _dkchManager.Stop();
            await WebViewHost.NavigateAsync(JmsHomeUrl);
        }

        private void UpdateDkchButtonsByState(bool isRunning)
        {
            if (tabDKCH_btnDKCH1 == null || tabDKCH_btnDKCH2 == null || tabDKCH_btnStop == null) return;
            tabDKCH_btnDKCH1.Visible = !isRunning;
            tabDKCH_btnDKCH2.Visible = !isRunning;
            tabDKCH_btnDKCH1.Enabled = !isRunning;
            tabDKCH_btnDKCH2.Enabled = !isRunning;
            tabDKCH_btnStop.Visible = isRunning;
            tabDKCH_btnStop.Enabled = isRunning;
            if (isRunning) tabDKCH_btnStop.BringToFront();
        }

        private enum DkchPageState
        {
            WrongPage,
            Navigating,
            Loading,
            Ready,
            NotLoggedIn,
            Error
        }

        private sealed class DkchPageProbe
        {
            public DkchPageState State { get; set; }
            public string Url { get; set; } = "";
            public string Detail { get; set; } = "";
        }

        private async Task<bool> EnsureDkchPageReadyAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (this.IsHandleCreated && this.InvokeRequired)
            {
                return await UiThread.InvokeOnUiAsync(this, async () => await EnsureDkchPageReadyAsync(token));
            }

            var initial = await ProbeDkchPageAsync(token);
            LogDkchPageState("EnsureDkchPageReady", initial);

            if (initial.State == DkchPageState.Ready)
            {
                await RefreshAuthTokenAsync();
                return true;
            }

            if (initial.State == DkchPageState.NotLoggedIn)
            {
                ShowDkchGuardMessage("JMS chưa đăng nhập. Vui lòng đăng nhập rồi chạy DKCH.");
                return false;
            }

            await NavigateToDkchAsync(token);
            bool ready = await WaitUntilDkchReadyAsync(DkchReadyTimeout, token);
            if (!ready)
            {
                var final = await ProbeDkchPageAsync(token);
                LogDkchPageState("EnsureDkchPageReady.Result", final);
                if (final.State == DkchPageState.NotLoggedIn)
                {
                    ShowDkchGuardMessage("JMS chưa đăng nhập. Không chạy DKCH.");
                }
                else
                {
                    ShowDkchGuardMessage("Không mở được form DKCH hoặc form chưa sẵn sàng.");
                }
                return false;
            }

            await RefreshAuthTokenAsync();
            AppLogger.Info("[DKCH Guard] action=RunAutomation result=Ready");
            return true;
        }

        private async Task<bool> IsDkchPageReadyAsync(CancellationToken token)
        {
            return (await ProbeDkchPageAsync(token)).State == DkchPageState.Ready;
        }

        private async Task NavigateToDkchAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (this.IsHandleCreated && this.InvokeRequired)
            {
                await UiThread.InvokeOnUiAsync(this, async () => { await NavigateToDkchAsync(token); return true; });
                return;
            }

            if (tabDKCH_webView?.CoreWebView2 == null)
                throw new InvalidOperationException("DKCH WebView2 chưa sẵn sàng.");

            string targetUrl = DkchTargetUrl;
            AppLogger.Info($"[DKCH Guard] state=Navigating action=NavigateToDkch currentUrl='{GetDkchCurrentUrl()}' targetUrl='{targetUrl}'");
            tabDKCH_webView.CoreWebView2.Navigate(targetUrl);
            await Task.Delay(300, token);
        }

        private async Task<bool> WaitUntilDkchReadyAsync(TimeSpan timeout, CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            DkchPageState? lastState = null;

            while (sw.Elapsed < timeout)
            {
                token.ThrowIfCancellationRequested();
                var probe = await ProbeDkchPageAsync(token);
                if (lastState != probe.State)
                {
                    LogDkchPageState("WaitDkchReady", probe);
                    lastState = probe.State;
                }

                if (probe.State == DkchPageState.Ready) return true;
                if (probe.State == DkchPageState.NotLoggedIn) return false;

                await Task.Delay(300, token);
            }

            return await IsDkchPageReadyAsync(token);
        }

        private async Task<DkchPageProbe> ProbeDkchPageAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (this.IsHandleCreated && this.InvokeRequired)
            {
                return await UiThread.InvokeOnUiAsync(this, async () => await ProbeDkchPageAsync(token));
            }

            string currentUrl = GetDkchCurrentUrl();
            if (tabDKCH_webView?.CoreWebView2 == null)
            {
                return new DkchPageProbe { State = DkchPageState.Loading, Url = currentUrl, Detail = "CoreWebView2=null" };
            }

            if (string.IsNullOrWhiteSpace(currentUrl) || currentUrl == "about:blank")
            {
                return new DkchPageProbe { State = DkchPageState.Loading, Url = currentUrl, Detail = "blank-url" };
            }

            if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var uri) ||
                !uri.Host.EndsWith("jtexpress.vn", StringComparison.OrdinalIgnoreCase))
            {
                return new DkchPageProbe { State = DkchPageState.WrongPage, Url = currentUrl, Detail = "non-jms-host" };
            }

            try
            {
                // TODO(runtime-config): move these DKCH readiness markers next to WebViewAutomation selectors when DKCH selectors become configurable.
                const string js = @"
(() => {
  const href = String(location.href || '');
  const lowerHref = href.toLowerCase();
  const bodyText = String((document.body && document.body.innerText) || '').toLowerCase();
  const hasRoute = lowerHref.includes('returnandforwardmaintainaddsite');
  const hasPasswordInput = !!document.querySelector('input[type=""password""]');
  const hasLoginText = bodyText.includes('login') || bodyText.includes('đăng nhập') || bodyText.includes('dang nhap') || bodyText.includes('登录');
  const container = document.querySelector('div[id^=""el-collapse-content-""]');
  const waybillInput = container ? container.querySelector('input') : null;
  const dropdown = document.querySelector('.el-select .el-input__inner');
  const searchButton = container ? container.querySelector('button.el-button--primary') : document.querySelector('button.el-button--primary');
  const hasDkchText = bodyText.includes('chuyển hoàn') || bodyText.includes('chuyen hoan') || bodyText.includes('đăng ký chuyển hoàn') || bodyText.includes('dang ky chuyen hoan');
  return JSON.stringify({
    href,
    hasRoute,
    hasPasswordInput,
    hasLoginText,
    hasDkchText,
    hasWaybillInput: !!waybillInput,
    hasDropdown: !!dropdown,
    hasSearchButton: !!searchButton
  });
})();";

                string raw = await tabDKCH_webView.ExecuteScriptAsync(js);
                string json = UnwrapWebViewJsonString(raw);
                if (string.IsNullOrWhiteSpace(json))
                    return new DkchPageProbe { State = DkchPageState.Loading, Url = currentUrl, Detail = "empty-dom-probe" };

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                bool hasRoute = root.TryGetProperty("hasRoute", out var routeEl) && routeEl.GetBoolean();
                bool hasPassword = root.TryGetProperty("hasPasswordInput", out var pwdEl) && pwdEl.GetBoolean();
                bool hasLoginText = root.TryGetProperty("hasLoginText", out var loginEl) && loginEl.GetBoolean();
                bool hasDkchText = root.TryGetProperty("hasDkchText", out var textEl) && textEl.GetBoolean();
                bool hasWaybillInput = root.TryGetProperty("hasWaybillInput", out var inputEl) && inputEl.GetBoolean();
                bool hasDropdown = root.TryGetProperty("hasDropdown", out var dropdownEl) && dropdownEl.GetBoolean();
                bool hasSearchButton = root.TryGetProperty("hasSearchButton", out var buttonEl) && buttonEl.GetBoolean();

                if (!hasRoute && (hasPassword || hasLoginText))
                {
                    return new DkchPageProbe { State = DkchPageState.NotLoggedIn, Url = currentUrl, Detail = "login-marker" };
                }

                if (!hasRoute)
                {
                    return new DkchPageProbe { State = DkchPageState.WrongPage, Url = currentUrl, Detail = "route-mismatch" };
                }

                if (hasWaybillInput && hasDropdown && hasSearchButton && hasDkchText)
                {
                    return new DkchPageProbe { State = DkchPageState.Ready, Url = currentUrl, Detail = "route+form-marker" };
                }

                if (hasPassword || hasLoginText)
                {
                    return new DkchPageProbe { State = DkchPageState.NotLoggedIn, Url = currentUrl, Detail = "route-login-marker" };
                }

                return new DkchPageProbe
                {
                    State = DkchPageState.Loading,
                    Url = currentUrl,
                    Detail = $"route={hasRoute};input={hasWaybillInput};dropdown={hasDropdown};button={hasSearchButton};text={hasDkchText}"
                };
            }
            catch (Exception ex)
            {
                return new DkchPageProbe { State = DkchPageState.Error, Url = currentUrl, Detail = ex.Message };
            }
        }

        private static string UnwrapWebViewJsonString(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw == "null") return "";
            try
            {
                return JsonSerializer.Deserialize<string>(raw) ?? "";
            }
            catch
            {
                return raw.Trim('"');
            }
        }

        private string GetDkchCurrentUrl()
        {
            try
            {
                return tabDKCH_webView?.CoreWebView2?.Source
                    ?? tabDKCH_webView?.Source?.ToString()
                    ?? "";
            }
            catch { return ""; }
        }

        private void LogDkchPageState(string action, DkchPageProbe probe)
        {
            AppLogger.Info($"[DKCH Guard] state={probe.State} action={action} currentUrl='{probe.Url}' detail='{probe.Detail}'");
        }

        private static List<string> ParseDkchWaybills(string text, out int invalidCount)
        {
            invalidCount = 0;
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string waybill = raw.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(waybill)) continue;
                if (!IsValidDkchWaybill(waybill))
                {
                    invalidCount++;
                    continue;
                }
                if (!result.Contains(waybill, StringComparer.OrdinalIgnoreCase))
                    result.Add(waybill);
            }
            return result;
        }

        private void ShowDkchGuardMessage(string message)
        {
            AppLogger.Warning($"[DKCH Guard] {message}");
            try { UIMessageTip.ShowWarning(message); } catch { }
        }

        private async void tabDKCH_inputNewBill_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Enter)
            {
                e.Handled = true;
                if (!await _dkchManualInputGate.WaitAsync(0))
                {
                    ShowDkchGuardMessage("DKCH đang kiểm tra trang, vui lòng chờ.");
                    return;
                }

                try
                {
                    var allLines = ParseDkchWaybills(tabDKCH_inputNewBill.Text, out int invalidCount);
                    if (allLines.Count == 0)
                    {
                        if (invalidCount > 0) ShowDkchGuardMessage("Mã vận đơn DKCH không hợp lệ.");
                        return;
                    }

                    if (_dkchManager?.IsRunning != true)
                    {
                        ShowDkchGuardMessage("Chưa bật DKCH1/DKCH2. Hãy chọn mode DKCH trước khi nhập mã.");
                        return;
                    }

                    if (invalidCount > 0)
                    {
                        ShowDkchGuardMessage($"Bỏ qua {invalidCount} mã không hợp lệ.");
                    }

                    if (!await EnsureDkchPageReadyAsync(_appCts.Token)) return;

                    tabDKCH_inputNewBill.Text = "";
                    foreach (var waybill in allLines)
                    {
                        AppendToNewBillDone(waybill);
                        AppLogger.Info($"[DKCH Guard] state=Ready action=RunAutomation result=Queued waybill={waybill}");
                        _dkchManager.AddPriorityWaybill(waybill);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    AppLogger.Error("[DKCH Guard] action=RunAutomation result=Error", ex);
                    ShowDkchGuardMessage("Không thể chạy DKCH: " + ex.Message);
                }
                finally
                {
                    _dkchManualInputGate.Release();
                }
            }
        }

        private void AppendToNewBillDone(string waybill)
        {
            if (tabDKCH_newBillDone == null || tabDKCH_newBillDone.IsDisposed) return;
            if (tabDKCH_newBillDone.InvokeRequired)
            {
                tabDKCH_newBillDone.Invoke(new Action(() => AppendToNewBillDone(waybill)));
                return;
            }
            var currentLines = tabDKCH_newBillDone.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
            if (!currentLines.Any(x => string.Equals(x, waybill, StringComparison.OrdinalIgnoreCase)))
            {
                currentLines.Add(waybill);
            }
            tabDKCH_newBillDone.Text = string.Join(Environment.NewLine, currentLines);
            tabDKCH_newBillDone.SelectionStart = tabDKCH_newBillDone.TextLength;
            tabDKCH_newBillDone.ScrollToCaret();
        }

        private void AppendDoneWaybill(string waybill)
        {
            if (tabDKCH_newBillDone == null || tabDKCH_newBillDone.IsDisposed) return;
            if (tabDKCH_newBillDone.InvokeRequired)
            {
                tabDKCH_newBillDone.Invoke(new Action(() => AppendDoneWaybill(waybill)));
                return;
            }
            var currentLines = tabDKCH_newBillDone.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => !string.Equals(x, waybill, StringComparison.OrdinalIgnoreCase)).ToList();
            tabDKCH_newBillDone.Text = string.Join(Environment.NewLine, currentLines);
            tabDKCH_newBillDone.SelectionStart = tabDKCH_newBillDone.TextLength;
            tabDKCH_newBillDone.ScrollToCaret();
        }

        private static bool IsValidDkchWaybill(string waybill)
        {
            if (string.IsNullOrWhiteSpace(waybill)) return false;
            return DkchWaybillRegex.IsMatch(waybill.Trim());
        }

        private void FormatNowTracking(string historyText)
        {
            tabDKCH_nowTracking.Clear();
            tabDKCH_nowTracking.Text = historyText;
            var formatRules = new Dictionary<string, System.Drawing.Color>
            {
                { "Đăng ký chuyển hoàn", System.Drawing.Color.Red },
                { "Đăng ký chuyển hoàn lần 2", System.Drawing.Color.Red },
                { "Quét kiện vấn đề", System.Drawing.Color.Red },
                { "Giao lại hàng", System.Drawing.Color.DodgerBlue },
                { "Ký nhận CPN", System.Drawing.Color.ForestGreen },
                { "Đang chuyển hoàn", System.Drawing.Color.DarkOrange },
                { "Xác nhận chuyển hoàn", System.Drawing.Color.DarkOrange }
            };

            foreach (var rule in formatRules)
            {
                int startIndex = 0;
                while (startIndex < tabDKCH_nowTracking.TextLength)
                {
                    int wordStartIndex = tabDKCH_nowTracking.Find(rule.Key, startIndex, RichTextBoxFinds.None);
                    if (wordStartIndex != -1)
                    {
                        tabDKCH_nowTracking.SelectionStart = wordStartIndex;
                        tabDKCH_nowTracking.SelectionLength = rule.Key.Length;
                        tabDKCH_nowTracking.SelectionColor = rule.Value;
                        tabDKCH_nowTracking.SelectionFont = new System.Drawing.Font(tabDKCH_nowTracking.Font, System.Drawing.FontStyle.Bold);
                        startIndex = wordStartIndex + rule.Key.Length;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            tabDKCH_nowTracking.SelectionStart = tabDKCH_nowTracking.TextLength;
            tabDKCH_nowTracking.SelectionLength = 0;
            tabDKCH_nowTracking.SelectionColor = tabDKCH_nowTracking.ForeColor;
            tabDKCH_nowTracking.SelectionFont = tabDKCH_nowTracking.Font;
        }

        private void ApplyZoomFactor()
        {
            var zoom = NormalizeZoomFactor(_settings?.ZoomFactor ?? 1.0);
            _isSyncingZoomFactor = true;
            try
            {
                ApplyZoomFactorToWebViews(zoom);
            }
            finally
            {
                _isSyncingZoomFactor = false;
            }
        }

        private void RegisterZoomSyncHandlers()
        {
            tabHome_webView.ZoomFactorChanged -= WebView_ZoomFactorChanged;
            tabDKCH_webView.ZoomFactorChanged -= WebView_ZoomFactorChanged;
            tabHome_webView.ZoomFactorChanged += WebView_ZoomFactorChanged;
            tabDKCH_webView.ZoomFactorChanged += WebView_ZoomFactorChanged;
        }

        private void WebView_ZoomFactorChanged(object sender, EventArgs e)
        {
            if (_isSyncingZoomFactor || sender is not Microsoft.Web.WebView2.WinForms.WebView2 source)
                return;

            var zoom = NormalizeZoomFactor(source.ZoomFactor);
            if (Math.Abs((_settings?.ZoomFactor ?? 1.0) - zoom) < 0.0001)
                return;

            _settings.ZoomFactor = zoom;
            _isSyncingZoomFactor = true;
            try
            {
                ApplyZoomFactorToWebViews(zoom);
            }
            finally
            {
                _isSyncingZoomFactor = false;
            }

            _ = SaveZoomFactorDebouncedAsync();
        }

        private void ApplyZoomFactorToWebViews(double zoom)
        {
            if (tabHome_webView?.CoreWebView2 != null)
            {
                SetWebViewZoomIfChanged(tabHome_webView, zoom);
            }

            if (tabDKCH_webView?.CoreWebView2 != null)
            {
                SetWebViewZoomIfChanged(tabDKCH_webView, zoom);
            }
        }

        private static void SetWebViewZoomIfChanged(Microsoft.Web.WebView2.WinForms.WebView2 webView, double zoom)
        {
            if (webView == null) return;
            if (Math.Abs(webView.ZoomFactor - zoom) < 0.0001) return;
            webView.ZoomFactor = zoom;
        }

        private static double NormalizeZoomFactor(double zoom)
        {
            if (double.IsNaN(zoom) || double.IsInfinity(zoom) || zoom <= 0)
                return 1.0;

            return Math.Max(0.25, Math.Min(5.0, zoom));
        }

        private async Task SaveZoomFactorDebouncedAsync()
        {
            CancellationTokenSource currentCts;
            lock (_authTokenLock)
            {
                _zoomSaveCts?.Cancel();
                _zoomSaveCts?.Dispose();
                _zoomSaveCts = new CancellationTokenSource();
                currentCts = _zoomSaveCts;
            }

            try
            {
                await Task.Delay(300, currentCts.Token);
                await SettingsManager.SaveAsync(CloneSettingsSnapshot());
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppLogger.Warning($"Lưu ZoomFactor thất bại: {ex.Message}");
            }
        }

        private AppSettings CloneSettingsSnapshot()
        {
            lock (_authTokenLock)
            {
                return new AppSettings
                {
                    ZoomFactor = _settings.ZoomFactor,
                    DefaultUrl = _settings.DefaultUrl,
                    LastAuthToken = _settings.LastAuthToken,
                    DownloadFolder = _settings.DownloadFolder,
                    DefaultSheet = _settings.DefaultSheet,
                    UseSheetByDefault = _settings.UseSheetByDefault,
                    AutoRefreshToken = _settings.AutoRefreshToken,
                    LastMode = _settings.LastMode,
                    Theme = _settings.Theme,
                    DefaultRowCount = _settings.DefaultRowCount,
                    PrinterName = _settings.PrinterName,
                    PaperWidth = _settings.PaperWidth,
                    PaperHeight = _settings.PaperHeight,
                    BlockWhenQueueHasErrorJob = _settings.BlockWhenQueueHasErrorJob,
                    BlockWhenPrinterPaused = _settings.BlockWhenPrinterPaused,
                    BlockWhenPrinterOffline = _settings.BlockWhenPrinterOffline,
                    PrintPaperWidthInch = _settings.PrintPaperWidthInch,
                    PrintPaperHeightInch = _settings.PrintPaperHeightInch,
                    PrinterPaperMode = _settings.PrinterPaperMode,
                    PrinterOriginalPaperName = _settings.PrinterOriginalPaperName,
                    PrinterOriginalSettingsBackup = _settings.PrinterOriginalSettingsBackup,
                    MiddleCode = _settings.MiddleCode,
                    MiddleCodeAliases = _settings.MiddleCodeAliases?.ToList() ?? new List<string>(),
                    MiddleCodeSegment2 = _settings.MiddleCodeSegment2,
                    AllowMiddleCodeSegment2Match = _settings.AllowMiddleCodeSegment2Match
                };
            }
        }

        // ======================================================================================
        // TAB TRACKING & UPLOAD BUMP
        // ======================================================================================

        private async void btnSearch_Click(object sender, EventArgs e)
        {
            string input = NormalizeWaybillInput(tabTracking_inputWaybill.Text);
            tabTracking_inputWaybill.Text = input;

            if (string.IsNullOrWhiteSpace(input))
            {
                UIMessageTip.ShowWarning("Chưa nhập mã vận đơn!");
                return;
            }

            try
            {
                AppLogger.Info($"Manual tracking started by user (tier={_tierPolicy?.Tier ?? CurrentTier}, count input).");
                tabTracking_btnSearch.Enabled = false;
                if (tabTracking_process != null)
                {
                    tabTracking_process.Value = 0;
                    tabTracking_process.Visible = true;
                }

                await _trackingService.SearchTrackingAsync(input);
                int tongSoDon = _trackingService.GetAllRows().Count;
                tabTracking_countSum.Text = tongSoDon.ToString("N0");
                _printService?.Reset();

                if (tongSoDon == 0)
                {
                    UIMessageTip.ShowWarning("Không tìm thấy vận đơn nào!");
                }
            }
            catch (Exception ex)
            {
                UIMessageTip.ShowError("Lỗi khi tra cứu: " + ex.Message);
            }
            finally
            {
                tabTracking_btnSearch.Enabled = true;
                UpdateWaybillCount();
                if (tabTracking_process != null)
                {
                    tabTracking_process.Value = tabTracking_process.Maximum;
                    tabTracking_process.Visible = false;
                }
            }
        }

        private void btn_Export_Click(object sender, EventArgs e) => _trackingService?.ExportToExcel();

        private void btn_Clear_Click(object sender, EventArgs e)
        {
            _trackingService?.ClearData();
            tabTracking_inputWaybill.Clear();
        }

        private void btn_Export_Spe_Click(object sender, EventArgs e) => _trackingService.ExportSpecial();

        private async void tabTracking_btnUpload_Click(object sender, EventArgs e)
        {
            tabTracking_btnUpload.Enabled = false;
            string oldText = tabTracking_btnUpload.Text;
            tabTracking_btnUpload.Text = "Đang đồng bộ...";
            try
            {
                DataGridView grid = tabTracking_dataView;
                if (grid.Rows.Count == 0 && grid.Columns.Count == 0)
                {
                    MessageBox.Show("Không có dữ liệu trên bảng để tải lên!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var sheetData = new List<IList<object>>();
                var headerRow = new List<object>();
                foreach (DataGridViewColumn col in grid.Columns) headerRow.Add(col.HeaderText);
                sheetData.Add(headerRow);
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow) continue;
                    var rowData = new List<object>();
                    foreach (DataGridViewCell cell in row.Cells) rowData.Add(cell.Value?.ToString() ?? "");
                    sheetData.Add(rowData);
                }
                string spreadsheetId = GoogleSheetService.DATA_SPREADSHEET_ID;
                string targetSheetName = "BUMP";
                await GoogleSheetService.ClearSheetAsync(spreadsheetId, targetSheetName);
                await GoogleSheetService.UpdateBumpSheetAsync(sheetData, spreadsheetId, $"{targetSheetName}!A1");
                MessageBox.Show("Đã tải lên thành công!", "Hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { tabTracking_btnUpload.Enabled = true; tabTracking_btnUpload.Text = oldText; }
        }

        private void tabTracking_inputWaybill_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.V)
            {
                e.SuppressKeyPress = true;
                if (Clipboard.ContainsText())
                {
                    string plainText = Clipboard.GetText(TextDataFormat.UnicodeText);
                    string cleaned = NormalizeWaybillInput(plainText);
                    if (!string.IsNullOrEmpty(cleaned))
                    {
                        tabTracking_inputWaybill.SelectedText = cleaned + Environment.NewLine;
                    }
                }
            }
        }

        private string NormalizeWaybillInput(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var parts = text.Split(new[] { '\r', '\n', ',', ';', '|', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => t.Trim().ToUpper())
                            .Where(t => t.Length >= 6)
                            .ToList();
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var waybillRegex = new System.Text.RegularExpressions.Regex(@"((8\d{11}|[A-Za-z][A-Za-z0-9]{4,17})(-\d{3})?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (var part in parts)
            {
                var matches = waybillRegex.Matches(part);
                if (matches.Count > 0)
                {
                    foreach (System.Text.RegularExpressions.Match m in matches)
                    {
                        string code = m.Value.ToUpper();
                        if (seen.Add(code)) result.Add(code);
                    }
                }
                else if (part.Length <= 25)
                {
                    if (seen.Add(part)) result.Add(part);
                }
            }
            return string.Join(Environment.NewLine, result);
        }

        private void UpdateWaybillCount()
        {
            if (tabDKCH_countSum == null) return;
            var uniqueCodes = tabTracking_inputWaybill.Text.Split(new[] { '\r', '\n', ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToUpper()).Where(x => x.Length > 5).Distinct(StringComparer.OrdinalIgnoreCase);
            tabDKCH_countSum.Text = $":Tổng: " + uniqueCodes.Count().ToString("N0");
        }


        // ======================================================================================
        // TAB PRINT
        // ======================================================================================

        private void print_LamMoi_Click(object sender, EventArgs e)
        {
            ClearPrintJobCaches();
            _printService.Reset();
            tabPrint_btnSelectAll.Checked = false;
            tabPrint_countSelect.Text = "Đang chọn: 0";
            tabPrint_countSum.Text = "Tổng: 0";
            if (tabPrint_inputWaybill != null)
            {
                tabPrint_inputWaybill.Text = "";
            }
            if (tabPrint_printPreview?.CoreWebView2 != null)
            {
                tabPrint_printPreview.CoreWebView2.Navigate("about:blank");
            }
        }

        private void TabPrint_printFunc_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_printService == null) return;

            ClearPrintJobCaches();
            if (tabPrint_printFunc.SelectedTab == tabPrint_inCH) _printService.SetMode(PrintMode.InHoan);
            else if (tabPrint_printFunc.SelectedTab == tabPrint_inCT) _printService.SetMode(PrintMode.InChuyenTiep);
            else if (tabPrint_printFunc.SelectedTab == tabPrint_inLaiDon) _printService.SetMode(PrintMode.InLaiDon);
            else if (tabPrint_printFunc.SelectedTab == tabPrint_inRV) _printService.SetMode(PrintMode.InReverse);

            tabPrint_btnSelectAll.Checked = false;
            tabPrint_inputWaybill.Text = "";
        }

        private void print_InChuyenHoan_Click(object sender, EventArgs e) => _printService.SetMode(PrintMode.InHoan);
        private void print_InChuyenTiep_Click(object sender, EventArgs e) => _printService.SetMode(PrintMode.InChuyenTiep);
        private void print_InLaiDon_Click(object sender, EventArgs e) => _printService.SetMode(PrintMode.InLaiDon);
        private void print_InReverse_Click(object sender, EventArgs e) => _printService.SetMode(PrintMode.InReverse);

        private string GetBaseWaybill(string waybill)
        {
            if (string.IsNullOrEmpty(waybill)) return "";
            int hyphenIndex = waybill.IndexOf('-');
            return hyphenIndex > 0 ? waybill.Substring(0, hyphenIndex).Trim().ToUpper() : waybill.Trim().ToUpper();
        }

        private async void tabPrint_inputWaybill_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (tabPrint_AutoMode.Active)
                {
                    string input = tabPrint_inputWaybill.Text.Trim();
                    if (string.IsNullOrWhiteSpace(input)) return;

                    ClearPrintStatusSnapshot();
                    await _printService.SearchAndLoadAsync(input, _printService.CurrentMode);
                    _printService.SelectAll(true);
                    tabPrint_btnSelectAll.Checked = true;
                    var preloadTask = QueuePreloadPrintJobForCurrentSelection("AutoModeSearch");
                    if (preloadTask != null)
                        await preloadTask;
                    await ExecutePrintAsync(true);
                    tabPrint_inputWaybill.Text = "";
                }
                else
                {
                    tabPrint_inputWaybill.AppendText(Environment.NewLine);
                    print_TimKiem_Click(null, null);
                }
            }
        }

        private async void print_TimKiem_Click(object sender, EventArgs e)
        {
            string input = tabPrint_inputWaybill.Text.Trim();
            if (string.IsNullOrWhiteSpace(input)) return;
            AppCaptureManager.Instance.RecordEvent(new AppCaptureEvent
            {
                Category = "print.flow",
                Source = "tabPrint",
                EventName = "SearchRequested",
                WaybillNo = input,
                CorrelationId = BuildPrintCorrelationId(input),
                Data = new Dictionary<string, object> { ["mode"] = _printService == null ? "" : _printService.CurrentMode.ToString() }
            });

            tabPrint_btnTimKiem.Enabled = false;
            try
            {
                ClearPrintStatusSnapshot();
                await _printService.SearchAndLoadAsync(input, _printService.CurrentMode);
                _printService.SelectAll(true);
                tabPrint_btnSelectAll.Checked = true;
                ShowPrintMessage("Đã xác minh, sẵn sàng in", false, 1500);
            }
            finally
            {
                tabPrint_btnTimKiem.Enabled = true;
            }
        }

        private async void tabPrint_btnPrint_Click(object sender, EventArgs e)
        {
            await ExecutePrintAsync(isAutoMode: false);
        }

        private async Task ExecutePrintAsync(bool isAutoMode)
        {
            if (_printService == null) return;
            var totalWatch = Stopwatch.StartNew();
            string printInputAtStart = tabPrint_inputWaybill?.Text?.Trim() ?? "";
            string printCorrelationId = BuildPrintCorrelationId(printInputAtStart);
            AppCaptureManager.Instance.RecordEvent(new AppCaptureEvent
            {
                Category = "print.flow",
                Source = "tabPrint",
                EventName = "PrintRequested",
                CorrelationId = printCorrelationId,
                WaybillNo = printInputAtStart,
                Data = new Dictionary<string, object> { ["isAutoMode"] = isAutoMode }
            });
            if (!await _printLock.WaitAsync(0))
            {
                if (!isAutoMode) ShowPrintMessage("Đang xử lý lệnh in hiện tại...", false, 2000);
                return;
            }
            bool printButtonChanged = false;
            try
            {
                var selected = _printService.GetSelectedWaybills();
                if (selected == null || selected.Count == 0)
                {
                    if (!isAutoMode) ShowPrintMessage("Chưa chọn vận đơn nào!", true);
                    return;
                }

                bool safeToPrint = await _printService.ValidateSelectedBeforePrintAsync(selected, tabPrint_inputWaybill.Text);
                AppCaptureManager.Instance.RecordPerformance("print.flow", "ValidateSelectedBeforePrint", totalWatch.ElapsedMilliseconds, new { selectedCount = selected.Count, input = printInputAtStart });
                if (!safeToPrint)
                {
                    tabPrint_btnSelectAll.Checked = false;
                    if (!isAutoMode) ShowPrintMessage("Không đủ điều kiện in an toàn.", true, 5000);
                    return;
                }

                selected = _printService.GetSelectedWaybills();
                if (selected == null || selected.Count == 0)
                {
                    ShowPrintMessage("Dữ liệu in đã bị xóa vì không đạt kiểm tra an toàn.", true, 5000);
                    return;
                }

                if (!SelectedMatchesCurrentInput(selected, tabPrint_inputWaybill.Text))
                {
                    _printService.Reset();
                    tabPrint_btnSelectAll.Checked = false;
                    ShowPrintMessage("Dữ liệu in không khớp mã vận đơn hiện tại.", true, 5000);
                    return;
                }

                List<string> originalOrder = ParseWaybillOrder(tabPrint_inputWaybill.Text);
                if (originalOrder.Count > 0)
                {
                    var orderMap = originalOrder.Select((wb, index) => new { wb, index }).GroupBy(x => x.wb, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().index, StringComparer.OrdinalIgnoreCase);
                    selected = selected.OrderBy(wb => orderMap.TryGetValue(wb, out int idx) ? idx : int.MaxValue).ToList();
                }
                SetPrintButtonState(false);
                printButtonChanged = true;
                int printType = 1;
                int applyTypeCode = (_printService.CurrentMode == PrintMode.InChuyenTiep) ? 2 : 4;
                var beforeStatus = _printService.GetLastPrintStatusSnapshots();

                selected = _printService.GetSelectedWaybills();
                if (selected == null || selected.Count == 0)
                {
                    ShowPrintMessage("Dữ liệu in đã bị xóa trước khi tạo PDF.", true, 5000);
                    return;
                }

                var reprintPlan = BuildTabPrintReprintPlan(selected, beforeStatus);
                var blockedReprint = reprintPlan
                    .FirstOrDefault(x => !x.Value.CanPrint);
                if (!string.IsNullOrWhiteSpace(blockedReprint.Key))
                {
                    ShowPrintMessage(blockedReprint.Value.BlockMessage, true, 8000);
                    AppLogger.Warning($"[TabPrintReprint] blocked waybill={blockedReprint.Key} autoJmsReprintCount={blockedReprint.Value.AutoJmsReprintCount}");
                    return;
                }

                int keepPdfs = 500;
                int keepLogsDays = 3;
                TryReadPrintConfig(out keepPdfs, out keepLogsDays);
                string firstWaybill = selected[0];
                string pdfCacheKey = BuildPrintPdfCacheKey(selected, printType, applyTypeCode);
                var ensureWatch = Stopwatch.StartNew();
                var printJob = await EnsureReadyToPrintAsync(
                    selected,
                    printType,
                    applyTypeCode,
                    keepPdfs,
                    firstWaybill,
                    pdfCacheKey,
                    "Print");
                ensureWatch.Stop();

                if (printJob == null || printJob.PdfBytes == null || printJob.PdfBytes.Length == 0)
                {
                    ShowPrintMessage("In thất bại.", true);
                    return;
                }

                if (tabPrint_printPreview?.CoreWebView2 != null
                    && !string.IsNullOrWhiteSpace(printJob.LocalPdfPath)
                    && File.Exists(printJob.LocalPdfPath))
                {
                    string fileUri = new Uri(printJob.LocalPdfPath).AbsoluteUri;
                    tabPrint_printPreview.CoreWebView2.Navigate($"{fileUri}#view=FitH&toolbar=0&navpanes=0&scrollbar=0");
                }

                try
                {
                    var submitResult = await SubmitPrintImmediatelyAsync(printJob, firstWaybill);
                    if (!submitResult.CompletedBySpooler)
                    {
                        AppLogger.Warning(
                            $"[PrintPerf] phase=PrintSubmitRejected waybill={firstWaybill} " +
                            $"printer={submitResult.PrinterName} beforeJobs={submitResult.SpoolerJobsBefore} " +
                            $"afterJobs={submitResult.SpoolerJobsAfter} printMs={submitResult.ElapsedMs} " +
                            $"document={submitResult.DocumentName} reason={submitResult.Reason}");
                        ShowPrintMessage("Lệnh in chưa hoàn tất trong hàng đợi máy in. Không ghi log đã in.", true, 6000);
                        return;
                    }

                    SavePrintSuccessLog(selected, beforeStatus, Array.Empty<PrintStatusSnapshot>(), keepLogsDays);
                    RecordTabPrintSuccess(selected, reprintPlan);
                    if (_currentPrintAttempt != null
                        && string.Equals(_currentPrintAttempt.WaybillNo, firstWaybill, StringComparison.OrdinalIgnoreCase))
                    {
                        _currentPrintAttempt.Printed = true;
                    }
                    RememberPrintedPdf(pdfCacheKey, printJob.LocalPdfPath);
                    _printService.SelectAll(false);
                    tabPrint_btnSelectAll.Checked = false;
                    ShowPrintMessage("Đã in, đang cập nhật trạng thái sau in...", false, 2500);
                    _printService.QueuePostPrintRefresh(selected, printType);
                    
                    AppLogger.Info($"[PrintPerf] phase=PrintTotal waybill={firstWaybill} totalMs={totalWatch.ElapsedMilliseconds}");
                    if (totalWatch.ElapsedMilliseconds > 2000)
                    {
                        AppLogger.Warning(
                            $"[PrintPerf][SLOW] phase=PrintTotalBreakdown waybill={firstWaybill} " +
                            $"ensureReadyMs={ensureWatch.ElapsedMilliseconds} printSubmitMs={submitResult.ElapsedMs} " +
                            $"totalMs={totalWatch.ElapsedMilliseconds} pdfBytes={printJob.PdfBytes.Length}");
                    }
                    AppCaptureManager.Instance.RecordPerformance(
                        "print.flow",
                        "PrintTotal",
                        totalWatch.ElapsedMilliseconds,
                        new { waybill = firstWaybill, ensureReadyMs = ensureWatch.ElapsedMilliseconds, printSubmitMs = submitResult.ElapsedMs });
                }
                catch (Exception ex)
                {
                    ShowPrintMessage($"Lỗi máy in: {ex.Message}", true, 5000);
                }
            }
            catch (Exception ex)
            {
                ShowPrintMessage($"Lỗi hệ thống: {ex.Message}", true);
            }
            finally
            {
                if (printButtonChanged)
                {
                    SetPrintButtonState(true);
                }
                _printLock.Release();
            }
        }

        private void ClearPrintStatusSnapshot()
        {
            if (tabPrint_messLable == null || tabPrint_messLable.IsDisposed)
                return;

            if (hideTimer != null)
            {
                hideTimer.Stop();
                hideTimer.Dispose();
                hideTimer = null;
            }

            tabPrint_messLable.Text = "";
            tabPrint_messLable.ForeColor = Color.Black;
        }

        private void ShowPrintStatusSnapshot(string phase, IReadOnlyList<PrintStatusSnapshot> snapshots, bool persist = false)
        {
            var snapshot = snapshots?.FirstOrDefault();
            if (snapshot == null)
            {
                AppLogger.Warning($"[PrintStatus] phase={phase} no-snapshot");
            }
        }

        private void ShowPrintApprovalSnapshot(string phase, IReadOnlyList<PrintApprovalInfo> approvals, IReadOnlyList<string> requestedWaybills)
        {
            var info = approvals?.FirstOrDefault();
            string requested = requestedWaybills == null ? "" : string.Join(",", requestedWaybills);
            if (info == null)
            {
                AppLogger.Warning($"[PrintApproval] phase={phase} no-records requested={requested}");
            }
        }

        private static string Dash(string value)
            => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

        private static PrintStatusSnapshot FindStatusSnapshot(IReadOnlyList<PrintStatusSnapshot> snapshots, string waybillNo)
        {
            if (snapshots == null || snapshots.Count == 0)
                return null;

            string normalized = GetBaseWaybillStatic(waybillNo);
            return snapshots.FirstOrDefault(x =>
                string.Equals(GetBaseWaybillStatic(x.InputWaybillNo), normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(GetBaseWaybillStatic(x.TrackingWaybillNo), normalized, StringComparison.OrdinalIgnoreCase));
        }

        private static int ResolveSuccessPrintCount(PrintStatusSnapshot after, PrintStatusSnapshot before)
        {
            if (after != null && after.PrintCount > 0)
                return after.PrintCount;

            if (before != null && before.PrintCount > 0)
                return before.PrintCount + 1;

            return 0;
        }

        private static string ResolveSuccessApplyStaff(PrintStatusSnapshot after, PrintStatusSnapshot before)
        {
            string staff = after?.ApplyStaffName;
            if (string.IsNullOrWhiteSpace(staff))
                staff = before?.ApplyStaffName;

            return string.IsNullOrWhiteSpace(staff) ? "--" : staff.Trim();
        }

        private string BuildPrintSuccessLogLine(
            string waybillNo,
            int printedOrderCount,
            int printCount,
            string applyStaffName)
        {
            var timeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var staff = string.IsNullOrWhiteSpace(applyStaffName) ? "--" : applyStaffName.Trim();
            return $"|{timeText}| Đã in {printedOrderCount} đơn | {waybillNo} | Số lần in: {printCount} | Người in: {staff} |";
        }

        private string BuildPrintPdfCacheKey(List<string> waybills, int printType, int applyTypeCode)
        {
            string order = string.Join("|", (waybills ?? new List<string>())
                .Select(x => (x ?? "").Trim().ToUpperInvariant())
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            return $"{_printService?.CurrentMode}|{printType}|{applyTypeCode}|{order}";
        }

        private void InitializeTabPrintPrinterControls()
        {
            if (uiPanel2 == null || uiPanel2.IsDisposed)
                return;

            if (uiPanel2.Controls.Find("tabPrint_btnClearPrinterJobs", false).Length > 0)
                return;

            var clearJobsButton = CreatePrinterActionButton("tabPrint_btnClearPrinterJobs", "Xóa job treo", 160);
            clearJobsButton.Click += async (_, _) => await ClearPrinterJobsFromUiAsync();

            var setPaperButton = CreatePrinterActionButton("tabPrint_btnSet3x3Paper", "Set 3\"x3\"", 275);
            setPaperButton.Click += (_, _) => SetAutoJmsPaperSize3x3();

            var unsetPaperButton = CreatePrinterActionButton("tabPrint_btnUnsetPaper", "Unset cỡ giấy", 365);
            unsetPaperButton.Click += (_, _) => RestoreOriginalPaperSize();

            uiPanel2.Controls.Add(clearJobsButton);
            uiPanel2.Controls.Add(setPaperButton);
            uiPanel2.Controls.Add(unsetPaperButton);
        }

        private static UIButton CreatePrinterActionButton(string name, string text, int left)
        {
            return new UIButton
            {
                Name = name,
                Text = text,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Size = new Size(text.Length > 10 ? 110 : 84, 28),
                Location = new Point(left, 4),
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                FillColor = Color.FromArgb(80, 160, 255),
                FillHoverColor = Color.FromArgb(64, 145, 245),
                RectColor = Color.FromArgb(80, 160, 255)
            };
        }

        private async Task ClearPrinterJobsFromUiAsync()
        {
            string printerName = ResolvePrintPrinterName();
            if (string.IsNullOrWhiteSpace(printerName))
            {
                ShowPrintMessage("Không tìm thấy máy in.", true, 5000);
                return;
            }

            var confirm = MessageBox.Show(
                $"Xóa toàn bộ job trong hàng đợi máy in '{printerName}'?",
                "Xóa job máy in",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
                return;

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var result = await _printerMaintenanceService.ClearStuckJobsAsync(printerName, timeout.Token);
                var status = await _printerMaintenanceService.RefreshStatusAsync(printerName, timeout.Token);
                AppLogger.Info($"[PrinterMaintenance] clearJobs printer={printerName} cleared={result.ClearedJobCount} failed={result.FailedJobCount} status={status.ReasonCode}");
                ShowPrintMessage(result.Message, !result.Success, 6000);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[PrinterMaintenance] clearJobs failed printer={printerName} error={ex.Message}");
                ShowPrintMessage("Không thể xóa job máy in. Hãy mở hàng đợi máy in hoặc chạy app với quyền phù hợp.", true, 8000);
            }
        }

        private void SetAutoJmsPaperSize3x3()
        {
            if (_settings == null)
                return;

            if (string.IsNullOrWhiteSpace(_settings.PrinterOriginalSettingsBackup))
            {
                _settings.PrinterOriginalSettingsBackup =
                    $"{_settings.PrinterPaperMode}|{_settings.PrintPaperWidthInch}|{_settings.PrintPaperHeightInch}";
                _settings.PrinterOriginalPaperName = _settings.PrinterPaperMode;
            }

            _settings.PrintPaperWidthInch = 3m;
            _settings.PrintPaperHeightInch = 3m;
            _settings.PrinterPaperMode = "AutoJMS_3x3";
            SavePrinterSettings();
            
            string defaultPrinter = new System.Drawing.Printing.PrinterSettings().PrinterName;
            AutoJMS.Utils.PrinterDevModeHelper.SetGlobalPaperSize(defaultPrinter, 300, 300); // 3x3 inches in hundredths of an inch
            
            AppLogger.Info("[PrinterPaper] mode=AutoJMS_3x3 widthInch=3 heightInch=3 scope=per-print-job");
            ShowPrintMessage("Đã thiết lập cỡ giấy thành công", false, 5000);
        }

        private void RestoreOriginalPaperSize()
        {
            if (_settings == null)
                return;

            decimal width = 4m;
            decimal height = 6m;
            string mode = "Original_4x6";

            _settings.PrintPaperWidthInch = width;
            _settings.PrintPaperHeightInch = height;
            _settings.PrinterPaperMode = mode;
            SavePrinterSettings();
            
            string defaultPrinter = new System.Drawing.Printing.PrinterSettings().PrinterName;
            AutoJMS.Utils.PrinterDevModeHelper.SetGlobalPaperSize(defaultPrinter, 400, 600); // 4x6 inches in hundredths of an inch
            
            AppLogger.Info($"[PrinterPaper] mode={mode} widthInch={width} heightInch={height} scope=per-print-job");
            ShowPrintMessage("Đã thiết lập cỡ giấy thành công", false, 5000);
        }

        private void SavePrinterSettings()
        {
            try
            {
                _userSettings.Save(_settings);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[PrinterPaper] save settings failed error={ex.Message}");
            }
        }

        private async Task<PrinterPreflightResult> RunPrinterPreflightAsync(
            string printerName,
            string waybillNo,
            string phase)
        {
            if (_settings?.EnablePrinterPreflight == false)
            {
                AppLogger.Info($"[PrintPerf] phase=PrinterPreflight waybill={waybillNo} skipped=true source=policy-disabled");
                return new PrinterPreflightResult
                {
                    CanPrint = true,
                    PrinterName = printerName ?? "",
                    ReasonCode = "PRINTER_PREFLIGHT_DISABLED",
                    StatusText = "Printer preflight disabled by runtime policy."
                };
            }

            var sw = Stopwatch.StartNew();
            var result = await _printerPreflightService.CheckAsync(printerName, _appCts.Token).ConfigureAwait(false);
            sw.Stop();

            AppLogger.Info(
                $"[PrintPerf] phase=PrinterPreflight waybill={waybillNo} printer={result.PrinterName} " +
                $"canPrint={result.CanPrint} reason={result.ReasonCode} queue={result.QueueJobCount} " +
                $"errorJobs={result.ErrorJobCount} ms={sw.ElapsedMilliseconds} source={phase}");
            AppCaptureManager.Instance.RecordPerformance(
                "print.flow",
                "PrinterPreflight",
                sw.ElapsedMilliseconds,
                new
                {
                    waybill = waybillNo,
                    printerName = result.PrinterName,
                    result.CanPrint,
                    result.ReasonCode,
                    result.QueueJobCount,
                    result.ErrorJobCount,
                    phase
                });

            if (!result.CanPrint)
                ShowPrintMessage(BuildPrinterPreflightUserMessage(result), true, 8000);

            return result;
        }

        private static string BuildPrinterPreflightUserMessage(PrinterPreflightResult result)
        {
            return result?.ReasonCode switch
            {
                "PRINTER_NOT_FOUND" => "Không tìm thấy máy in.",
                "PRINTER_OFFLINE" => "Máy in offline.",
                "PRINTER_PAUSED" => "Máy in đang paused.",
                "PRINTER_ERROR" => "Máy in đang lỗi.",
                "PRINTER_PAPER_OUT" => "Máy in hết giấy.",
                "PRINTER_QUEUE_HAS_ERROR_JOB" => "Hàng đợi có job lỗi/treo.",
                "PRINTER_CHECK_TIMEOUT" => "Kiểm tra máy in quá thời gian.",
                _ => "Máy in chưa sẵn sàng. Không gọi printWaybill."
            };
        }

        private void ApplyPrintPaperSettings(PrintDocument printDocument)
        {
            if (printDocument == null || _settings == null)
                return;

            string currentDefaultPrinter = new PrinterSettings().PrinterName;
            if (!string.Equals(printDocument.PrinterSettings.PrinterName, currentDefaultPrinter, StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Info($"[PrinterPaper] Bỏ qua override vì '{printDocument.PrinterSettings.PrinterName}' không phải là Default Printer.");
                return;
            }

            decimal widthInch = _settings.PrintPaperWidthInch <= 0 ? 3m : _settings.PrintPaperWidthInch;
            decimal heightInch = _settings.PrintPaperHeightInch <= 0 ? 3m : _settings.PrintPaperHeightInch;
            int width = Math.Max(1, (int)Math.Round(widthInch * 100m, MidpointRounding.AwayFromZero));
            int height = Math.Max(1, (int)Math.Round(heightInch * 100m, MidpointRounding.AwayFromZero));
            string mode = string.IsNullOrWhiteSpace(_settings.PrinterPaperMode) ? "AutoJMS" : _settings.PrinterPaperMode.Trim();

            try
            {
                var paperSize = new PaperSize($"{mode}_{widthInch:0.##}x{heightInch:0.##}", width, height);
                printDocument.DefaultPageSettings.PaperSize = paperSize;
                printDocument.PrinterSettings.DefaultPageSettings.PaperSize = paperSize;
                AppLogger.Info($"[PrinterPaper] apply mode={mode} widthInch={widthInch} heightInch={heightInch} width={width} height={height}");
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[PrinterPaper] apply PaperSize failed error={ex.Message}");
            }
        }

        private static string BuildPrintCorrelationId(string waybillText)
        {
            string waybill = string.Join("-", ParseWaybillOrder(waybillText).Take(3));
            if (string.IsNullOrWhiteSpace(waybill))
                waybill = "empty";
            return $"print-{waybill}-{DateTime.Now:yyyyMMdd-HHmmssfff}";
        }

        private void ClearPrintJobCaches()
        {
            lock (_printJobCacheLock)
            {
                _printJobCacheBySignature.Clear();
                _printJobPreloadTasks.Clear();
                _lastPdfUrlBySignature.Clear();
                _lastPrintedPdfBySignature.Clear();
                _lastPrintedPdfByUrl.Clear();
            }

            _currentPrintAttempt = null;
        }

        private string TryGetCachedPrintPdf(string cacheKey)
        {
            if (string.IsNullOrWhiteSpace(cacheKey))
                return "";

            (string PdfPath, DateTime CreatedAt) entry;
            lock (_printJobCacheLock)
            {
                if (!_lastPrintedPdfBySignature.TryGetValue(cacheKey, out entry))
                    return "";
            }

            if (DateTime.Now - entry.CreatedAt > PrintPdfCacheTtl)
                return "";

            return File.Exists(entry.PdfPath) ? entry.PdfPath : "";
        }

        private void RememberPrintedPdf(string cacheKey, string localPath)
        {
            if (string.IsNullOrWhiteSpace(cacheKey)
                || string.IsNullOrWhiteSpace(localPath)
                || !File.Exists(localPath))
            {
                return;
            }

            lock (_printJobCacheLock)
            {
                _lastPrintedPdfBySignature[cacheKey] = (localPath, DateTime.Now);
            }
        }

        private Task<PrintJobCacheEntry> QueuePreloadPrintJobForCurrentSelection(string reason)
        {
            try
            {
                if (_printService == null)
                    return null;

                var selected = _printService.GetSelectedWaybills();
                if (selected == null || selected.Count == 0)
                    return null;

                if (!SelectedMatchesCurrentInput(selected, tabPrint_inputWaybill.Text))
                    return null;

                int printType = 1;
                int applyTypeCode = (_printService.CurrentMode == PrintMode.InChuyenTiep) ? 2 : 4;
                int keepPdfs = 500;
                int keepLogsDays = 3;
                TryReadPrintConfig(out keepPdfs, out keepLogsDays);

                string firstWaybill = selected[0];
                string cacheKey = BuildPrintPdfCacheKey(selected, printType, applyTypeCode);
                if (TryGetCachedPrintJob(cacheKey, out var cached))
                {
                    AppLogger.Info($"[PrintPerf] phase=PreloadPdf waybill={firstWaybill} cacheHit=true ageMs={(long)(DateTime.Now - cached.CreatedAt).TotalMilliseconds} reason={reason}");
                    return Task.FromResult(cached);
                }

                lock (_printJobCacheLock)
                {
                    if (_printJobPreloadTasks.TryGetValue(cacheKey, out var existing) && !existing.IsCompleted)
                    {
                        AppLogger.Info($"[PrintPerf] phase=PreloadPdf waybill={firstWaybill} alreadyRunning=true reason={reason}");
                        return existing;
                    }
                }

                bool disablePrintUntilReady = tabPrint_AutoMode == null || !tabPrint_AutoMode.Active;
                if (disablePrintUntilReady)
                    SetPrintButtonState(false);

                ShowPrintMessage("Đang chuẩn bị bản in...", false, 1500);
                var selectedSnapshot = selected.ToList();
                var preloadTask = Task.Run(async () =>
                {
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var job = await EnsureReadyToPrintAsync(
                            selectedSnapshot,
                            printType,
                            applyTypeCode,
                            keepPdfs,
                            firstWaybill,
                            cacheKey,
                            "Preload").ConfigureAwait(false);

                        sw.Stop();
                        AppLogger.Info($"[PrintPerf] phase=PreloadPdf waybill={firstWaybill} totalMs={sw.ElapsedMilliseconds} success={job != null} reason={reason}");
                        if (IsHandleCreated && !IsDisposed)
                        {
                            BeginInvoke((MethodInvoker)(() =>
                            {
                                if (disablePrintUntilReady && _printLock.CurrentCount > 0)
                                    SetPrintButtonState(true);
                                if (job != null)
                                    ShowPrintMessage("Sẵn sàng in", false, 1500);
                                else
                                    ShowPrintMessage("Chưa chuẩn bị được bản in.", true, 2500);
                            }));
                        }

                        return job;
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        AppLogger.Warning($"[PrintPerf] phase=PreloadPdf waybill={firstWaybill} totalMs={sw.ElapsedMilliseconds} success=False error={ex.Message} reason={reason}");
                        if (IsHandleCreated && !IsDisposed)
                        {
                            BeginInvoke((MethodInvoker)(() =>
                            {
                                if (disablePrintUntilReady && _printLock.CurrentCount > 0)
                                    SetPrintButtonState(true);
                            }));
                        }
                        return null;
                    }
                });

                lock (_printJobCacheLock)
                {
                    _printJobPreloadTasks[cacheKey] = preloadTask;
                }
                return preloadTask;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[PrintPerf] phase=PreloadPdf enqueueFailed error={ex.Message}");
                return null;
            }
        }

        private async Task<PrintJobCacheEntry> EnsureReadyToPrintAsync(
            List<string> selected,
            int printType,
            int applyTypeCode,
            int keepPdfs,
            string firstWaybill,
            string pdfCacheKey,
            string source)
        {
            var job = await EnsureReadyToPrintCoreAsync(selected, printType, applyTypeCode, keepPdfs, firstWaybill, pdfCacheKey, source, null).ConfigureAwait(false);

            return job;
        }

        private async Task<PrintJobCacheEntry> EnsureReadyToPrintCoreAsync(
            List<string> selected,
            int printType,
            int applyTypeCode,
            int keepPdfs,
            string firstWaybill,
            string pdfCacheKey,
            string source,
            Action markApiCalled)
        {
            var totalWatch = Stopwatch.StartNew();
            bool cacheHit = false;

            string printerName = ResolvePrintPrinterName();
            var preflight = await RunPrinterPreflightAsync(printerName, firstWaybill, "BeforePrintWaybill")
                .ConfigureAwait(false);
            if (!preflight.CanPrint)
            {
                totalWatch.Stop();
                AppLogger.Warning(
                    $"[PrintPerf] phase=EnsureReady waybill={firstWaybill} cacheHit={cacheHit} " +
                    $"source={source} totalMs={totalWatch.ElapsedMilliseconds} error=printer-preflight-blocked reason={preflight.ReasonCode}");
                return null;
            }

            _currentPrintAttempt = new CurrentPrintAttempt { WaybillNo = firstWaybill };
            _currentPrintAttempt.MarkPrintWaybillRequested();
            var getPdfWatch = Stopwatch.StartNew();
            string pdfUrl = await GetPdfUrlViaCSharpAsync(selected, printType, applyTypeCode).ConfigureAwait(false);
            markApiCalled?.Invoke();
            getPdfWatch.Stop();
            if (_currentPrintAttempt != null)
                _currentPrintAttempt.PdfUrl = pdfUrl ?? "";
            AppLogger.Info($"[PrintPerf] phase=GetPdfUrl waybill={firstWaybill} elapsedMs={getPdfWatch.ElapsedMilliseconds} success={!string.IsNullOrWhiteSpace(pdfUrl)} source={source}");
            AppCaptureManager.Instance.RecordPerformance("print.flow", "GetPdfUrl", getPdfWatch.ElapsedMilliseconds, new { waybill = firstWaybill, success = !string.IsNullOrWhiteSpace(pdfUrl), source });

            if (!string.IsNullOrWhiteSpace(pdfUrl))
            {
                lock (_printJobCacheLock)
                {
                    _lastPdfUrlBySignature[pdfCacheKey] = (pdfUrl, DateTime.Now);
                }
            }

            if (string.IsNullOrWhiteSpace(pdfUrl))
            {
                totalWatch.Stop();
                AppLogger.Warning($"[PrintPerf] phase=EnsureReady waybill={firstWaybill} cacheHit={cacheHit} source={source} totalMs={totalWatch.ElapsedMilliseconds} error=no-pdf-url");
                return null;
            }

            string localPath = TryGetCachedPrintPdfByUrl(pdfUrl);
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                var job = await CreatePrintJobCacheEntryAsync(pdfCacheKey, firstWaybill, localPath).ConfigureAwait(false);
                RememberPrintJob(pdfCacheKey, job);
                cacheHit = true;
                totalWatch.Stop();
                AppLogger.Info($"[PrintPerf] phase=EnsureReady waybill={firstWaybill} cacheHit=true urlFileCache=true source={source} totalMs={totalWatch.ElapsedMilliseconds}");
                return job;
            }

            var downloadWatch = Stopwatch.StartNew();
            if (string.Equals(source, "Print", StringComparison.OrdinalIgnoreCase))
                ShowPrintMessage("JMS đã nhận lệnh in, đang chờ PDF...", false, 0);

            localPath = await DownloadPdfWithRetryAsync(pdfUrl, keepPdfs, firstWaybill).ConfigureAwait(false);
            downloadWatch.Stop();
            RememberPrintedPdfUrl(pdfUrl, localPath);
            AppLogger.Info($"[PrintPerf] phase=DownloadPdf waybill={firstWaybill} elapsedMs={downloadWatch.ElapsedMilliseconds} success={File.Exists(localPath)} source={source}");
            AppCaptureManager.Instance.RecordPerformance("print.flow", "DownloadPdf", downloadWatch.ElapsedMilliseconds, new { waybill = firstWaybill, success = File.Exists(localPath), source });

            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            {
                totalWatch.Stop();
                AppLogger.Warning($"[PrintPerf] phase=EnsureReady waybill={firstWaybill} cacheHit={cacheHit} source={source} totalMs={totalWatch.ElapsedMilliseconds} error=no-local-pdf");
                return null;
            }

            var created = await CreatePrintJobCacheEntryAsync(pdfCacheKey, firstWaybill, localPath).ConfigureAwait(false);
            RememberPrintJob(pdfCacheKey, created);
            if (_currentPrintAttempt != null
                && string.Equals(_currentPrintAttempt.WaybillNo, firstWaybill, StringComparison.OrdinalIgnoreCase)
                && created != null)
            {
                _currentPrintAttempt.TempPdfPath = created.LocalPdfPath;
                _currentPrintAttempt.PdfBytes = created.PdfBytes;
            }
            totalWatch.Stop();
            AppLogger.Info($"[PrintPerf] phase=EnsureReady waybill={firstWaybill} cacheHit=false source={source} totalMs={totalWatch.ElapsedMilliseconds}");
            return created;
        }

        private bool TryGetCachedPrintJob(string cacheKey, out PrintJobCacheEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(cacheKey))
                return false;

            PrintJobCacheEntry cached;
            lock (_printJobCacheLock)
            {
                if (!_printJobCacheBySignature.TryGetValue(cacheKey, out cached))
                    return false;
            }

            if (cached == null || cached.IsExpired || cached.PdfBytes == null || cached.PdfBytes.Length == 0)
            {
                lock (_printJobCacheLock)
                {
                    _printJobCacheBySignature.Remove(cacheKey);
                }
                return false;
            }

            entry = cached;
            return true;
        }

        private void RememberPrintJob(string cacheKey, PrintJobCacheEntry entry)
        {
            if (entry == null
                || string.IsNullOrWhiteSpace(cacheKey)
                || entry.PdfBytes == null
                || entry.PdfBytes.Length == 0)
            {
                return;
            }

            lock (_printJobCacheLock)
            {
                _printJobCacheBySignature[cacheKey] = entry;
            }
        }

        private static async Task<PrintJobCacheEntry> CreatePrintJobCacheEntryAsync(string cacheKey, string waybillNo, string localPath)
        {
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
                return null;

            byte[] bytes = await File.ReadAllBytesAsync(localPath).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0)
                return null;

            return new PrintJobCacheEntry
            {
                CacheKey = cacheKey ?? "",
                WaybillNo = waybillNo ?? "",
                PdfBytes = bytes,
                LocalPdfPath = localPath,
                CreatedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.Add(PrintJobCacheTtl),
                PdfHash = ComputeSha256(bytes)
            };
        }

        private static string ComputeSha256(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return "";

            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(bytes));
        }

        private async Task<PrintSubmitResult> SubmitPrintImmediatelyAsync(PrintJobCacheEntry job, string firstWaybill)
        {
            var printWatch = Stopwatch.StartNew();
            string printerName = ResolvePrintPrinterName();
            string documentName = BuildPrintDocumentName(firstWaybill);
            var preflight = await RunPrinterPreflightAsync(printerName, firstWaybill, "BeforeSubmitPrint")
                .ConfigureAwait(false);
            if (!preflight.CanPrint)
            {
                printWatch.Stop();
                return new PrintSubmitResult
                {
                    CompletedBySpooler = false,
                    ElapsedMs = printWatch.ElapsedMilliseconds,
                    PrinterName = printerName,
                    DocumentName = documentName,
                    SpoolerJobsBefore = preflight.QueueJobCount,
                    SpoolerJobsAfter = preflight.QueueJobCount,
                    Reason = preflight.ReasonCode
                };
            }

            var beforeSnapshots = GetPrinterJobSnapshots(printerName);
            int beforeJobs = beforeSnapshots?.Count ?? -1;
            var beforeJobNames = new HashSet<string>(
                beforeSnapshots?.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            var errorQueueJob = beforeSnapshots?.FirstOrDefault(IsPrinterJobError);
            if (errorQueueJob != null)
            {
                printWatch.Stop();
                string reason = "spooler-has-error-job";
                AppLogger.Warning(
                    $"[PrintPerf] phase=PrintSubmitBlocked waybill={firstWaybill} printer={printerName} " +
                    $"document={documentName} beforeJobs={beforeJobs} job={errorQueueJob.Name} " +
                    $"jobDocument={errorQueueJob.DocumentName} jobStatus={errorQueueJob.StatusText} reason={reason}");
                return new PrintSubmitResult
                {
                    CompletedBySpooler = false,
                    ElapsedMs = printWatch.ElapsedMilliseconds,
                    PrinterName = printerName,
                    DocumentName = documentName,
                    SpoolerJobsBefore = beforeJobs,
                    SpoolerJobsAfter = beforeJobs,
                    Reason = reason
                };
            }

            using var stream = new MemoryStream(job.PdfBytes, writable: false);
            using var document = PdfDocument.Load(stream);
            using var printDocument = document.CreatePrintDocument();
            printDocument.DocumentName = documentName;
            if (!string.IsNullOrEmpty(printerName) && printerName != "-1")
            {
                printDocument.PrinterSettings.PrinterName = printerName;
            }

            ApplyPrintPaperSettings(printDocument);
            printDocument.PrintController = new StandardPrintController();
            DateTime submittedAt = DateTime.Now;
            printDocument.Print();
            var spooler = await WaitForPrinterJobCompletedAsync(
                printerName,
                documentName,
                beforeJobNames,
                submittedAt,
                beforeJobs,
                firstWaybill,
                TimeSpan.FromSeconds(30)).ConfigureAwait(true);
            printWatch.Stop();
            int afterJobs = CountPrinterJobs(printerName);
            AppLogger.Info(
                $"[PrintPerf] phase=PrintSubmit waybill={firstWaybill} printMs={printWatch.ElapsedMilliseconds} " +
                $"completed={spooler.Completed} observed={spooler.Observed} printer={printerName} " +
                $"document={documentName} beforeJobs={beforeJobs} afterJobs={afterJobs} " +
                $"jobName={spooler.JobName} jobStatus={spooler.JobStatus} reason={spooler.Reason}");
            AppCaptureManager.Instance.RecordPerformance(
                "print.flow",
                "PrintDocument.Print",
                printWatch.ElapsedMilliseconds,
                new
                {
                    waybill = firstWaybill,
                    printerName,
                    documentName,
                    completed = spooler.Completed,
                    observed = spooler.Observed,
                    beforeJobs,
                    afterJobs,
                    spooler.Reason,
                    spooler.JobStatus
                });
            return new PrintSubmitResult
            {
                CompletedBySpooler = spooler.Completed,
                ElapsedMs = printWatch.ElapsedMilliseconds,
                PrinterName = printerName,
                DocumentName = documentName,
                SpoolerJobsBefore = beforeJobs,
                SpoolerJobsAfter = afterJobs,
                Reason = spooler.Reason
            };
        }

        private static string BuildPrintDocumentName(string waybillNo)
        {
            string safeWaybill = new string((waybillNo ?? "")
                .Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
                .ToArray());
            if (string.IsNullOrWhiteSpace(safeWaybill))
                safeWaybill = "unknown";

            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            return $"AutoJMS_{safeWaybill}_{DateTime.Now:HHmmssfff}_{suffix}";
        }

        private string ResolvePrintPrinterName()
        {
            string configured = string.IsNullOrWhiteSpace(_settings.PrinterName) ? "" : _settings.PrinterName.Trim();
            if (!string.IsNullOrWhiteSpace(configured) && configured != "-1")
                return configured;

            try
            {
                using var document = new PrintDocument();
                return document.PrinterSettings.PrinterName ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static async Task<PrintSpoolerWatchResult> WaitForPrinterJobCompletedAsync(
            string printerName,
            string documentName,
            IReadOnlySet<string> beforeJobNames,
            DateTime submittedAt,
            int beforeJobs,
            string waybillNo,
            TimeSpan timeout)
        {
            if (beforeJobs < 0)
            {
                AppLogger.Warning($"[PrintPerf] phase=SpoolerWatchSkipped waybill={waybillNo} printer={printerName} document={documentName} reason=spooler-count-before-failed");
                return new PrintSpoolerWatchResult { Reason = "spooler-count-before-failed" };
            }

            var sw = Stopwatch.StartNew();
            bool observed = false;
            string jobName = "";
            string jobStatus = "";
            int observedMax = beforeJobs;
            int polls = 0;
            while (sw.Elapsed < timeout)
            {
                polls++;
                var jobs = GetPrinterJobSnapshots(printerName);
                if (jobs == null)
                {
                    await Task.Delay(250).ConfigureAwait(false);
                    continue;
                }

                observedMax = Math.Max(observedMax, jobs.Count);
                var current = FindSubmittedPrintJob(jobs, documentName, beforeJobNames, submittedAt);
                if (current == null)
                {
                    if (observed)
                    {
                        return new PrintSpoolerWatchResult
                        {
                            Completed = true,
                            Observed = true,
                            Reason = "spooler-job-completed",
                            JobName = jobName,
                            JobStatus = jobStatus,
                            Polls = polls,
                            ElapsedMs = sw.ElapsedMilliseconds
                        };
                    }

                    await Task.Delay(250).ConfigureAwait(false);
                    continue;
                }

                observed = true;
                jobName = current.Name;
                jobStatus = current.StatusText;
                if (IsPrinterJobError(current))
                {
                    AppLogger.Warning(
                        $"[PrintPerf] phase=SpoolerJobError waybill={waybillNo} printer={printerName} " +
                        $"document={documentName} job={current.Name} status={current.StatusText}");
                    return new PrintSpoolerWatchResult
                    {
                        Completed = false,
                        Observed = true,
                        Reason = "spooler-job-error",
                        JobName = current.Name,
                        JobStatus = current.StatusText,
                        Polls = polls,
                        ElapsedMs = sw.ElapsedMilliseconds
                    };
                }

                await Task.Delay(250).ConfigureAwait(false);
            }

            string reason = observed ? "spooler-job-still-queued" : "spooler-job-not-observed";
            AppLogger.Warning(
                $"[PrintPerf] phase=SpoolerWatchTimeout waybill={waybillNo} printer={printerName} " +
                $"document={documentName} beforeJobs={beforeJobs} observedMax={observedMax} " +
                $"observed={observed} job={jobName} status={jobStatus} timeoutMs={(int)timeout.TotalMilliseconds}");
            return new PrintSpoolerWatchResult
            {
                Completed = false,
                Observed = observed,
                Reason = reason,
                JobName = jobName,
                JobStatus = jobStatus,
                Polls = polls,
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }

        private static PrintJobSnapshot FindSubmittedPrintJob(
            List<PrintJobSnapshot> jobs,
            string documentName,
            IReadOnlySet<string> beforeJobNames,
            DateTime submittedAt)
        {
            if (jobs == null || jobs.Count == 0)
                return null;

            var byDocument = jobs.FirstOrDefault(x =>
                string.Equals(x.DocumentName, documentName, StringComparison.OrdinalIgnoreCase));
            if (byDocument != null)
                return byDocument;

            DateTime earliest = submittedAt.AddSeconds(-2);
            return jobs
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .Where(x => beforeJobNames == null || !beforeJobNames.Contains(x.Name))
                .Where(x => !x.SubmittedAt.HasValue || x.SubmittedAt.Value >= earliest)
                .OrderByDescending(x => x.SubmittedAt ?? DateTime.MinValue)
                .FirstOrDefault();
        }

        private static int CountPrinterJobs(string printerName)
        {
            var jobs = GetPrinterJobSnapshots(printerName);
            return jobs?.Count ?? -1;
        }

        private static List<PrintJobSnapshot> GetPrinterJobSnapshots(string printerName)
        {
            try
            {
                string normalized = NormalizePrinterNameForWmi(printerName);
                using var searcher = new ManagementObjectSearcher("SELECT Name, Document, JobStatus, Status, TimeSubmitted FROM Win32_PrintJob");
                var jobs = new List<PrintJobSnapshot>();
                foreach (ManagementObject job in searcher.Get())
                {
                    string name = job["Name"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(normalized)
                        || name.StartsWith(normalized + ",", StringComparison.OrdinalIgnoreCase))
                    {
                        string document = job["Document"]?.ToString() ?? "";
                        string jobStatus = job["JobStatus"]?.ToString() ?? "";
                        string status = job["Status"]?.ToString() ?? "";
                        DateTime? submittedAt = TryParseWmiDateTime(job["TimeSubmitted"]?.ToString());
                        jobs.Add(new PrintJobSnapshot
                        {
                            Name = name,
                            DocumentName = document,
                            JobStatus = jobStatus,
                            Status = status,
                            SubmittedAt = submittedAt
                        });
                    }
                }

                return jobs;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[PrintPerf] phase=SpoolerCountFailed printer={printerName} error={ex.Message}");
                return null;
            }
        }

        private static PrintJobSnapshot FindOldestPrinterJob(List<PrintJobSnapshot> jobs, TimeSpan staleThreshold)
        {
            if (jobs == null || jobs.Count == 0)
                return null;

            DateTime cutoff = DateTime.Now.Subtract(staleThreshold);
            return jobs
                .Where(x => x.SubmittedAt.HasValue && x.SubmittedAt.Value < cutoff)
                .OrderBy(x => x.SubmittedAt.Value)
                .FirstOrDefault();
        }

        private static DateTime? TryParseWmiDateTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            try
            {
                return ManagementDateTimeConverter.ToDateTime(value);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsPrinterJobError(PrintJobSnapshot job)
        {
            string text = ((job.JobStatus ?? "") + " " + (job.Status ?? "")).ToLowerInvariant();
            return text.Contains("error")
                || text.Contains("offline")
                || text.Contains("paper")
                || text.Contains("paused")
                || text.Contains("blocked")
                || text.Contains("intervention")
                || text.Contains("stalled");
        }

        private static string NormalizePrinterNameForWmi(string printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName))
                return "";

            return printerName.Trim();
        }

        private sealed class PrintSubmitResult
        {
            public bool CompletedBySpooler { get; init; }
            public long ElapsedMs { get; init; }
            public string PrinterName { get; init; } = "";
            public string DocumentName { get; init; } = "";
            public int SpoolerJobsBefore { get; init; }
            public int SpoolerJobsAfter { get; init; }
            public string Reason { get; init; } = "";
        }

        private sealed class PrintSpoolerWatchResult
        {
            public bool Completed { get; init; }
            public bool Observed { get; init; }
            public string Reason { get; init; } = "";
            public string JobName { get; init; } = "";
            public string JobStatus { get; init; } = "";
            public int Polls { get; init; }
            public long ElapsedMs { get; init; }
        }

        private sealed class PrintJobSnapshot
        {
            public string Name { get; init; } = "";
            public string DocumentName { get; init; } = "";
            public string JobStatus { get; init; } = "";
            public string Status { get; init; } = "";
            public DateTime? SubmittedAt { get; init; }
            public string StatusText => string.Join(" | ", new[] { JobStatus, Status }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private string TryGetCachedPrintPdfByUrl(string pdfUrl)
        {
            if (string.IsNullOrWhiteSpace(pdfUrl))
                return "";

            string key = NormalizePrintPdfUrl(pdfUrl);
            (string PdfPath, DateTime CreatedAt) entry;
            lock (_printJobCacheLock)
            {
                if (!_lastPrintedPdfByUrl.TryGetValue(key, out entry))
                    return "";
            }

            if (DateTime.Now - entry.CreatedAt > PrintPdfCacheTtl)
                return "";

            return File.Exists(entry.PdfPath) ? entry.PdfPath : "";
        }

        private void RememberPrintedPdfUrl(string pdfUrl, string localPath)
        {
            if (string.IsNullOrWhiteSpace(pdfUrl)
                || string.IsNullOrWhiteSpace(localPath)
                || !File.Exists(localPath))
            {
                return;
            }

            lock (_printJobCacheLock)
            {
                _lastPrintedPdfByUrl[NormalizePrintPdfUrl(pdfUrl)] = (localPath, DateTime.Now);
            }
        }

        private static string NormalizePrintPdfUrl(string pdfUrl)
            => (pdfUrl ?? "").Trim();

        private static System.Net.Http.HttpClient CreatePrintPdfHttpClient()
        {
            var handler = new System.Net.Http.HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip
                                       | System.Net.DecompressionMethods.Deflate
                                       | System.Net.DecompressionMethods.Brotli
            };
            var captureHandler = new AppHttpCaptureHandler(handler, "PrintPdfHttpClient");
            var client = new System.Net.Http.HttpClient(captureHandler)
            {
                Timeout = PrintPdfDownloadTimeout
            };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", CHROME_USER_AGENT);
            return client;
        }

        private Dictionary<string, TabPrintReprintDecision> BuildTabPrintReprintPlan(
            List<string> waybills,
            IReadOnlyList<PrintStatusSnapshot> beforeStatus)
        {
            var result = new Dictionary<string, TabPrintReprintDecision>(StringComparer.OrdinalIgnoreCase);
            if (waybills == null || waybills.Count == 0)
                return result;

            foreach (string waybill in waybills)
            {
                string baseWaybill = GetBaseWaybillStatic(waybill);
                var beforeInfo = FindStatusSnapshot(beforeStatus, waybill);
                int jmsPrintCount = Math.Max(0, beforeInfo?.PrintCount ?? 0);
                var state = _tabPrintReprintStore.Get(baseWaybill);
                int maxReprints = Math.Max(0, _settings?.MaxAutoJmsReprintCount ?? TabPrintReprintPolicy.MaxReprintsPerWaybill);
                var decision = _tabPrintReprintPolicy.Evaluate(state, jmsPrintCount, maxReprints);
                result[baseWaybill] = decision;

                AppLogger.Info(
                    $"[TabPrintReprint] plan waybill={baseWaybill} jmsPrintCount={jmsPrintCount} " +
                    $"autoJmsReprintCount={decision.AutoJmsReprintCount} maxReprints={maxReprints} isReprint={decision.IsReprint} canPrint={decision.CanPrint}");
            }

            return result;
        }

        private void RecordTabPrintSuccess(
            List<string> waybills,
            IReadOnlyDictionary<string, TabPrintReprintDecision> reprintPlan)
        {
            if (waybills == null || waybills.Count == 0)
                return;

            foreach (string waybill in waybills)
            {
                string baseWaybill = GetBaseWaybillStatic(waybill);
                TabPrintReprintDecision decision = null;
                if (reprintPlan != null)
                    reprintPlan.TryGetValue(baseWaybill, out decision);
                bool isReprint = decision?.IsReprint == true;

                _tabPrintReprintStore.RecordSuccessfulPrint(baseWaybill, isReprint);

                int sessionCount = 0;
                if (_printedHistory.TryGetValue(baseWaybill, out var history))
                    sessionCount = history.Count;
                _printedHistory[baseWaybill] = (DateTime.Now, sessionCount + 1);
            }
        }

        private void HandlePrintSafetyBlocked(PrintSafetyResult result)
        {
            if (result == null) return;
            void Apply()
            {
                tabPrint_btnSelectAll.Checked = false;
                string message = string.IsNullOrWhiteSpace(result.UserMessage)
                    ? "Không xác minh được bưu cục hiện tại. Không in."
                    : result.UserMessage;
                ShowPrintMessage(message, true, 8000);
                AppLogger.Warning($"[PrintSafety] blocked input={result.InputWaybillNo} tracking={result.TrackingWaybillNo} middleCode={result.MiddleCode} maDoan2={result.MaDoan2} scanNetworkCode={result.MatchedScanNetworkCode} eventIndex={result.MatchedEventIndex} reason={result.ReasonCode} events={result.EventCount} hash={result.RawJsonHash}");
            }

            if (InvokeRequired) BeginInvoke((MethodInvoker)Apply);
            else Apply();
        }

        private static bool SelectedMatchesCurrentInput(List<string> selected, string currentInput)
        {
            var input = ParseWaybillOrder(currentInput).Select(GetBaseWaybillStatic).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selectedSet = (selected ?? new List<string>()).Select(GetBaseWaybillStatic).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return input.Count > 0 && selectedSet.SetEquals(input);
        }

        private static string GetBaseWaybillStatic(string waybill)
        {
            if (string.IsNullOrWhiteSpace(waybill)) return "";
            string normalized = waybill.Trim().ToUpperInvariant();
            int hyphenIndex = normalized.IndexOf('-');
            return hyphenIndex > 0 ? normalized.Substring(0, hyphenIndex) : normalized;
        }

        private async Task<string> GetPdfUrlViaCSharpAsync(List<string> waybills, int printType, int applyTypeCode)
        {
            try
            {
                await RefreshAuthTokenAsync();
                if (!JmsAuthStateService.HasToken)
                {
                    ShowPrintMessage("Không tìm thấy Token xác thực.", true);
                    return string.Empty;
                }

                var payload = new Dictionary<string, object> { { "waybillIds", waybills }, { "applyTypeCode", applyTypeCode }, { "printType", printType }, { "pringType", printType }, { "countryId", "1" } };
                string jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
                string apiUrl = AppConfig.Current.BuildJmsApiUrl("operatingplatform/rebackTransferExpress/printWaybill");

                using var timeoutCts = new CancellationTokenSource(PrintPdfUrlTimeout);
                using (var response = await JmsApiClient.PostJsonAsync(apiUrl, jsonPayload, routeName: "trackingExpress", ct: timeoutCts.Token))
                {
                    if (response == null) return null;
                    string rawJson = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                    AppLogger.Info($"[PrintPerf] phase=GetPdfUrlResponse http={(int)response.StatusCode} bodyLength={rawJson?.Length ?? 0}");
                    if (!response.IsSuccessStatusCode) return null;
                    using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(rawJson))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("code", out System.Text.Json.JsonElement codeElement))
                        {
                            string codeVal = codeElement.ToString();
                            if (codeVal == "200" || codeVal == "0" || codeVal == "1")
                            {
                                if (root.TryGetProperty("data", out System.Text.Json.JsonElement data))
                                {
                                    if (data.ValueKind == System.Text.Json.JsonValueKind.String)
                                    {
                                        string url = data.GetString();
                                        if (!string.IsNullOrEmpty(url) && url.StartsWith("http")) return url;
                                    }
                                    else if (data.ValueKind == System.Text.Json.JsonValueKind.Array && data.GetArrayLength() > 0)
                                    {
                                        string url = data[0].GetString();
                                        if (!string.IsNullOrEmpty(url) && url.StartsWith("http")) return url;
                                    }
                                }
                                ShowPrintMessage("JMS trả về thành công nhưng không có link PDF.", true);
                            }
                            else
                            {
                                string msg = root.TryGetProperty("msg", out System.Text.Json.JsonElement msgElement) ? msgElement.GetString() : "Lỗi từ máy chủ JMS";
                                ShowPrintMessage(msg, true);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                AppLogger.Warning($"[PrintPerf] phase=GetPdfUrl timeoutMs={(int)PrintPdfUrlTimeout.TotalMilliseconds}");
                ShowPrintMessage("JMS trả link PDF quá chậm. Vui lòng thử lại.", true);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Lỗi mạng GetPdfUrl", ex);
            }
            return string.Empty;
        }

        private async Task<string> DownloadPdfWithRetryAsync(string pdfUrl, int keepPdfs, string waybillTag = "")
        {
            if (string.IsNullOrWhiteSpace(pdfUrl)) return string.Empty;
            try
            {
                string printFolder = Path.Combine(AppPaths.DownloadsDir, "Vận đơn đã in");
                if (!Directory.Exists(printFolder)) Directory.CreateDirectory(printFolder);
                using var timeoutCts = new CancellationTokenSource(PrintPdfDownloadTimeout);
                string fileName = string.IsNullOrEmpty(waybillTag)
                    ? $"AutoJMS_{DateTime.Now:yyyyMMdd_HHmmssfff}.pdf"
                    : $"{waybillTag.Replace("/", "_")}-{DateTime.Now:yyyyMMdd_HHmmssfff}.pdf";
                string path = Path.Combine(printFolder, fileName);
                var result = await DownloadPdfFromUrlWithRetriesAsync(pdfUrl.Trim(), path, waybillTag, timeoutCts.Token);
                if (!result.Success || !File.Exists(path))
                    return string.Empty;

                AppLogger.Info(
                    $"[PrintPerf] phase=DownloadPdfReady waybill={waybillTag} bytes={result.Bytes} " +
                    $"attempt={result.Winner} headerMs={result.HeaderMs} bodyMs={result.BodyMs} totalMs={result.TotalMs}");
                var files = new DirectoryInfo(printFolder).GetFiles("*.pdf").OrderByDescending(f => f.CreationTime).ToList();
                for (int j = keepPdfs; j < files.Count; j++)
                {
                    try { files[j].Delete(); } catch { }
                }
                return path;
            }
            catch (OperationCanceledException)
            {
                AppLogger.Warning($"[PrintPerf] phase=DownloadPdf timeoutMs={(int)PrintPdfDownloadTimeout.TotalMilliseconds} waybill={waybillTag}");
                ShowPrintMessage("PDF từ JMS chưa sẵn sàng. Bấm in lại để tải lại đúng link PDF vừa tạo.", true, 6000);
                return string.Empty;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[PrintPerf] phase=DownloadPdf failed waybill={waybillTag} error={ex.Message}");
                ShowPrintMessage("Không thể tải PDF.", true);
                return string.Empty;
            }
        }

        private static async Task<PrintPdfDownloadResult> DownloadPdfFromUrlWithRetriesAsync(
            string pdfUrl,
            string finalPath,
            string waybillTag,
            CancellationToken token)
        {
            string tempPath = finalPath + ".tmp";
            PrintPdfDownloadResult lastResult = new(false, tempPath, "none", 0, 0, 0, 0, "not-started");

            for (int attempt = 1; attempt <= PrintPdfDownloadMaxAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();
                TryDeleteFile(tempPath);

                string attemptName = $"attempt{attempt}";
                TimeSpan attemptTimeout = attempt == 1 ? PrintPdfFirstAttemptTimeout : PrintPdfRetryAttemptTimeout;
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                attemptCts.CancelAfter(attemptTimeout);

                try
                {
                    lastResult = await DownloadPdfOnceAsync(pdfUrl, tempPath, waybillTag, attemptName, attemptCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    long timeoutMs = (long)attemptTimeout.TotalMilliseconds;
                    lastResult = new PrintPdfDownloadResult(false, tempPath, attemptName, 0, timeoutMs, 0, timeoutMs, "attempt-timeout");
                    AppLogger.Warning($"[PrintPerf] phase=DownloadPdfAttemptTimeout waybill={waybillTag} attempt={attemptName} timeoutMs={(int)attemptTimeout.TotalMilliseconds}");
                }

                bool hasPdfFile = lastResult.Success && IsLikelyPdfFile(tempPath);
                AppLogger.Info(
                    $"[PrintPerf] phase=DownloadPdfAttempt waybill={waybillTag} attempt={attemptName} " +
                    $"success={hasPdfFile} bytes={lastResult.Bytes} http={lastResult.StatusCode} " +
                    $"contentType={lastResult.ContentType} headerMs={lastResult.HeaderMs} bodyMs={lastResult.BodyMs} " +
                    $"totalMs={lastResult.TotalMs} error={lastResult.Error}");

                if (hasPdfFile)
                {
                    TryDeleteFile(finalPath);
                    File.Move(tempPath, finalPath, overwrite: true);
                    return lastResult with { TempPath = finalPath };
                }

                TryDeleteFile(tempPath);
                if (attempt >= PrintPdfDownloadMaxAttempts)
                    break;

                var delay = ComputePrintPdfRetryDelay(attempt);
                AppLogger.Warning(
                    $"[PrintPerf] phase=DownloadPdfRetryWait waybill={waybillTag} " +
                    $"attempt={attemptName} delayMs={(int)delay.TotalMilliseconds}");
                await Task.Delay(delay, token).ConfigureAwait(false);
            }

            TryDeleteFile(finalPath);
            return lastResult;
        }

        private static async Task<PrintPdfDownloadResult> DownloadPdfOnceAsync(
            string pdfUrl,
            string tempPath,
            string waybillTag,
            string attemptName,
            CancellationToken token)
        {
            var totalWatch = Stopwatch.StartNew();
            var headerWatch = Stopwatch.StartNew();
            try
            {
                using var response = await PrintPdfHttpClient.GetAsync(
                    pdfUrl,
                    System.Net.Http.HttpCompletionOption.ResponseHeadersRead,
                    token).ConfigureAwait(false);
                headerWatch.Stop();

                int statusCode = (int)response.StatusCode;
                string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (!response.IsSuccessStatusCode)
                {
                    totalWatch.Stop();
                    TryDeleteFile(tempPath);
                    return new PrintPdfDownloadResult(
                        false,
                        tempPath,
                        attemptName,
                        0,
                        headerWatch.ElapsedMilliseconds,
                        0,
                        totalWatch.ElapsedMilliseconds,
                        $"HTTP {statusCode}",
                        statusCode,
                        contentType);
                }

                await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
                var bodyWatch = Stopwatch.StartNew();
                await source.CopyToAsync(file, 64 * 1024, token).ConfigureAwait(false);
                await file.FlushAsync(token).ConfigureAwait(false);
                bodyWatch.Stop();
                totalWatch.Stop();

                long bytes = 0;
                try { bytes = new FileInfo(tempPath).Length; } catch { }
                return new PrintPdfDownloadResult(
                    bytes > 0,
                    tempPath,
                    attemptName,
                    bytes,
                    headerWatch.ElapsedMilliseconds,
                    bodyWatch.ElapsedMilliseconds,
                    totalWatch.ElapsedMilliseconds,
                    bytes > 0 ? "" : "empty-body",
                    statusCode,
                    contentType);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                TryDeleteFile(tempPath);
                throw;
            }
            catch (Exception ex)
            {
                totalWatch.Stop();
                TryDeleteFile(tempPath);
                return new PrintPdfDownloadResult(false, tempPath, attemptName, 0, headerWatch.ElapsedMilliseconds, 0, totalWatch.ElapsedMilliseconds, ex.Message);
            }
        }

        private static TimeSpan ComputePrintPdfRetryDelay(int completedAttempt)
        {
            int delayMs = Math.Min(5000, 500 + (completedAttempt * 750));
            return TimeSpan.FromMilliseconds(delayMs);
        }

        private static bool IsLikelyPdfFile(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return false;

                var info = new FileInfo(path);
                if (info.Length < 64)
                    return false;

                Span<byte> header = stackalloc byte[5];
                using var stream = File.OpenRead(path);
                int read = stream.Read(header);
                return read >= 4
                    && header[0] == (byte)'%'
                    && header[1] == (byte)'P'
                    && header[2] == (byte)'D'
                    && header[3] == (byte)'F';
            }
            catch
            {
                return false;
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        private sealed record PrintPdfDownloadResult(
            bool Success,
            string TempPath,
            string Winner,
            long Bytes,
            long HeaderMs,
            long BodyMs,
            long TotalMs,
            string Error,
            int StatusCode = 0,
            string ContentType = "");

        private void SavePrintSuccessLog(
            List<string> waybills,
            IReadOnlyList<PrintStatusSnapshot> beforeStatus,
            IReadOnlyList<PrintStatusSnapshot> afterStatus,
            int keepLogsDays)
        {
            try
            {
                if (waybills == null || waybills.Count == 0)
                    return;

                string logFolder = Path.Combine(AppPaths.DownloadsDir, "Vận đơn đã in", "Logs");
                if (!Directory.Exists(logFolder)) Directory.CreateDirectory(logFolder);
                string logFile = Path.Combine(logFolder, $"Log_{DateTime.Now:yyyyMMdd}.txt");
                string auditFile = Path.Combine(logFolder, $"PrintAudit_{DateTime.Now:yyyyMMdd}.tsv");
                bool writeAuditHeader = !File.Exists(auditFile);
                var logBuilder = new StringBuilder();
                var auditBuilder = new StringBuilder();

                if (writeAuditHeader)
                {
                    auditBuilder.AppendLine("printedAt\tprintedOrderCount\twaybillNo\tprintCount\tapplyStaffName\tmiddleCode\tmaDoan2\tmatchedScanNetworkCode\treasonCode\taction");
                }

                int printedOrderCount = waybills.Count;
                foreach (var waybill in waybills)
                {
                    var afterInfo = FindStatusSnapshot(afterStatus, waybill);
                    var beforeInfo = FindStatusSnapshot(beforeStatus, waybill);
                    int printCount = ResolveSuccessPrintCount(afterInfo, beforeInfo);
                    string applyStaffName = ResolveSuccessApplyStaff(afterInfo, beforeInfo);
                    string printedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    var safety = _printService?.GetLastAllowedPrintSafetyResult(waybill);

                    string line = BuildPrintSuccessLogLine(
                        waybillNo: waybill,
                        printedOrderCount: printedOrderCount,
                        printCount: printCount,
                        applyStaffName: applyStaffName);

                    logBuilder.AppendLine(line);
                    auditBuilder.AppendLine(string.Join("\t", new[]
                    {
                        printedAt,
                        printedOrderCount.ToString(),
                        waybill,
                        printCount.ToString(),
                        applyStaffName,
                        safety?.MiddleCode ?? "",
                        safety?.MaDoan2 ?? "",
                        safety?.MatchedScanNetworkCode ?? "",
                        safety?.ReasonCode ?? "",
                        "PRINT_SUCCESS"
                    }));
                }

                File.AppendAllText(logFile, logBuilder.ToString());
                File.AppendAllText(auditFile, auditBuilder.ToString());

                DateTime cutoff = DateTime.Now.Date.AddDays(-keepLogsDays);
                var oldLogs = new DirectoryInfo(logFolder)
                    .GetFiles()
                    .Where(f => (f.Name.StartsWith("Log_", StringComparison.OrdinalIgnoreCase)
                                 || f.Name.StartsWith("PrintAudit_", StringComparison.OrdinalIgnoreCase))
                                && f.CreationTime.Date < cutoff)
                    .ToList();
                foreach (var f in oldLogs)
                {
                    try { f.Delete(); } catch { }
                }
            }
            catch { }
        }

        private static List<string> ParseWaybillOrder(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new List<string>();
            return input.Split(new[] { '\r', '\n', ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 5).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void TryReadPrintConfig(out int keepPdfs, out int keepLogsDays)
        {
            keepPdfs = 500;
            keepLogsDays = 3;
            try
            {
                // Try from RuntimeConfigService if available
                if (Program.RuntimeConfig?.Current?.Print != null)
                {
                    var printCfg = Program.RuntimeConfig.Current.Print;
                    if (printCfg.KeepRecentPdfCount > 0) keepPdfs = printCfg.KeepRecentPdfCount;
                    if (printCfg.PrintLogRetentionDays > 0) keepLogsDays = printCfg.PrintLogRetentionDays;
                    return;
                }

                // Fallback: read from AutoJMS.json (legacy)
                string jsonPath = AppPaths.AutoJmsJson;
                if (!File.Exists(jsonPath)) return;
                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("KeepRecentPdfCount", out var pPdf) && pPdf.TryGetInt32(out int vPdf) && vPdf > 0)
                {
                    keepPdfs = vPdf;
                }
                if (root.TryGetProperty("PrintLogRetentionDays", out var pLog) && pLog.TryGetInt32(out int vLog) && vLog > 0)
                {
                    keepLogsDays = vLog;
                }
            }
            catch { }
        }

        private void SetPrintButtonState(bool enabled)
        {
            if (tabPrint_btnPrint.InvokeRequired)
            {
                tabPrint_btnPrint.Invoke(new Action(() => SetPrintButtonState(enabled)));
                return;
            }
            tabPrint_btnPrint.Enabled = enabled;
            tabPrint_btnPrint.Text = enabled ? "IN" : "Đang in...";
        }

        private void tabPrint_btnSelectAll_CheckedChanged(object sender, EventArgs e)
        {
            if (_printService != null)
            {
                _printService.SelectAll(tabPrint_btnSelectAll.Checked);
            }
        }

        private void ShowPrintMessage(string message, bool isError = false, int timeout = 2000)
        {
            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke((MethodInvoker)(() => ShowPrintMessage(message, isError, timeout)));
                return;
            }

            if (tabPrint_messLable == null || tabPrint_messLable.IsDisposed) return;
            if (hideTimer != null)
            {
                hideTimer.Stop();
                hideTimer.Dispose();
                hideTimer = null;
            }
            tabPrint_messLable.Text = message;
            bool isDark = UI.AppTheme.CurrentTheme == UI.ThemeMode.Dark;
            tabPrint_messLable.ForeColor = isError ? (isDark ? Color.FromArgb(252, 115, 115) : Color.Red) : (isDark ? Color.White : Color.Black);
            if (timeout <= 0)
                return;

            hideTimer = new System.Windows.Forms.Timer();
            hideTimer.Interval = timeout;
            hideTimer.Tick += (sender, e) =>
            {
                if (tabPrint_messLable != null && !tabPrint_messLable.IsDisposed) tabPrint_messLable.Text = "";
                hideTimer.Stop();
                hideTimer.Dispose();
                hideTimer = null;
            };
            hideTimer.Start();
        }
    }
}

