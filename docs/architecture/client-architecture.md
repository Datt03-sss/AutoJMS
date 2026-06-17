# Client Architecture

## Entry Point Flow

```
Program.Main()
    │
    ├─► VelopackApp.Build().Run()
    │
    ├─► Compute HWID (SMBIOS UUID + disk serial + MachineGuid)
    │
    ├─► AppConfig.LoadBootstrap()
    │
    ├─► Read/verify license key (offline-first)
    │       │
    │       ├─► Online: LicenseApiService.VerifyLicenseSecureAsync()
    │       └─► Offline: Use cached license
    │
    ├─► InitializeServicesFromLicense()
    │       │
    │       ├─► SupabaseManifestService
    │       ├─► RuntimeConfigService
    │       ├─► IntegrityService
    │       ├─► MajorUpdateService
    │       └─► SmallUpdateService
    │
    ├─► Start background services
    │       │
    │       ├─► uiControlService (network monitor)
    │       ├─► HeartbeatSupervisor (license heartbeat)
    │       ├─► ModuleSystem initialization
    │       ├─► HashVerifier (async integrity check)
    │       └─► SmallUpdateService (async selector update)
    │
    └─► Application.Run(new Main(sessionTier))
```

## MainForm Initialization

```
Main..ctor(tier)
    │
    ├─► TierRuntimePolicy.Resolve(tier)
    │
    ├─► InitializeComponent() [Designer]
    │
    ├─► TabManager.RegisterTabs() [HOME, DKCH, TRACKING, PRINT, ABOUT]
    │
    ├─► TabManager.ApplyTier(tier)
    │
    ├─► WebView2 CreationProperties (shared BrowserData)
    │
    └─► AutoSyncTimer setup [ULTRA only]
            │
            └─► if (_tierPolicy.EnableBackgroundAutoSync)
                    _autoSyncTimer.Start()
```

## OnLoad Flow

```
Main.OnLoad()
    │
    ├─► InitNetworkUI()
    │
    ├─► Version display
    │
    ├─► Ensure WebView2 instances
    │       │
    │       ├─► tabHome_webView.EnsureCoreWebView2Async()
    │       ├─► tabDKCH_webView.EnsureCoreWebView2Async()
    │       └─► tabPrint_printPreview.EnsureCoreWebView2Async()
    │
    ├─► Navigate to JMS URLs
    │
    ├─► Services initialization
    │       │
    │       ├─► WaybillTrackingService
    │       ├─► PrintService
    │       └─► DkchManager (start daemon)
    │
    ├─► Auth token validation
    │
    └─► Startup sync [ULTRA only]
            │
            └─► if (_tierPolicy.EnableStartupInventorySync)
                    RunStartupSyncAsync()
```

## Form Lifecycle

### BASE Tier

```
Main.Shown
    └─► No FullStackOperation created

Main closing
    ├─► Stop DkchManager
    ├─► Release Supabase lease
    └─► Dispose resources
```

### ULTRA Tier

```
Main.Shown
    └─► PreCreateFullStackForm()
            └─► _fullStackForm = new FullStackOperation()

Main closing
    ├─► Stop all timers
    ├─► Close FullStackOperation
    ├─► Release Supabase lease
    └─► Dispose resources
```

## Service Dependencies

```
Main.cs
    │
    ├──► JmsAuthTokenService (static)
    │       │
    │       └──► WebViewTokenReader = GetTokenFromJmsWebViewAsync
    │
    ├──► JmsAuthStateService (static)
    │       │
    │       └──► WebViewTokenRefresher = GetTokenFromAnyWebViewAsync
    │
    ├──► WaybillTrackingService
    │       └──► Uses JmsAuthTokenService
    │
    ├──► PrintService
    │       └──► Uses JmsAuthTokenService
    │
    ├──► DkchManager
    │       └──► Uses WebViewAutomation
    │
    └──► ZaloChatService (FullStackOperation)
```

## Path Architecture

```
C:\AutoJMS\                    ← InstallRoot
├── current\                    ← AppContext.BaseDirectory
│   └── [App binaries]
├── packages\                  ← Velopack cache
├── AppData\                  ← UserDataDir
│   ├── AutoJMS.json          ← Settings
│   ├── secure\
│   │   ├── AutoJMS.secure  ← Encrypted config
│   │   └── license.dat      ← Encrypted license
│   ├── logs\
│   │   └── debug.log       ← Application logs
│   ├── cache\
│   ├── BrowserData\          ← WebView2 shared data
│   ├── Downloads\
│   │   └── Vận đơn đã in\  ← Printed PDFs
│   └── ZaloProfile\
└── AutoJMS.exe               ← Velopack stub
```

## Key Classes

| Class | File | Responsibility |
|-------|------|----------------|
| Program | Program.cs | Entry point, initialization |
| Main | Main.cs | Main form, tab management |
| FullStackOperation | FullStackOperation.cs | ULTRA-only form |
| TierRuntimePolicy | TierRuntimePolicy.cs | Tier enforcement |
| JmsAuthTokenService | JmsAuthTokenService.cs | Token orchestration |
| JmsAuthStateService | JmsAuthStateService.cs | Token state |
| JmsApiClient | JmsApiClient.cs | JMS API HTTP client |
| InventorySyncService | InventorySyncService.cs | Inventory fetch |
| SupabaseDbService | SupabaseDbService.cs | Waybill database |
| VelopackUpdateService | VelopackUpdateService.cs | In-app updates |
