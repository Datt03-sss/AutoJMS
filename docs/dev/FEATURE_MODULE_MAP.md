# Feature Module Map

| Feature | Tier | UI | Main files | Background job | Notes |
| ------- | ---- | -- | ---------- | -------------- | ----- |
| HOME | BASE/ULTRA | Main tab | `Main.cs`, `Main.Designer.cs`, `WebViewHost.cs` | No | JMS WebView2 browser and token capture surface. |
| DKCH | BASE/ULTRA | Main tab | `Main.cs`, `WebViewAutomation.cs` | No | Manual/user-triggered automation must remain available on BASE. |
| TRACKING manual | BASE/ULTRA | Main tab | `Main.cs`, `WaybillTrackingService.cs`, `JmsApiClient.cs` | No | BASE manual feature allowed. |
| PRINT | BASE/ULTRA | Main tab | `Main.cs`, `PrintService.cs`, `IPrintService.cs` | No | BASE manual feature allowed. |
| ABOUT update | BASE/ULTRA | Main tab | `Main.cs`, `VelopackUpdateService.cs`, `MajorUpdateService.cs` | No | Major update manual via About tab. |
| FullStackOperation | ULTRA | Standalone form | `FullStackOperation.cs`, `Main.cs` | Yes | Not a tab; gated by `TierRuntimePolicy`. |
| Inventory sync | ULTRA | Background/service | `InventorySyncService.cs`, `Main.cs`, `SupabaseDbService.cs` | Yes | Must not run on BASE. |
| Database tracking | ULTRA | Background/service | `DatabaseTracking.cs`, `SupabaseDbService.cs`, `Main.cs` | Yes | Must not run on BASE. |
| Zalo/SLA dashboard | ULTRA | FullStackOperation | `FullStackOperation.cs`, `ZaloChatService.cs` | Yes | Requires lifecycle cancellation/guarding. |
| Selector/runtime small update | BASE/ULTRA | Startup/background config | `SmallUpdateService.cs`, `RuntimeConfigService.cs`, `SupabaseManifestService.cs` | Controlled small update | Supabase control-plane files only. |
| Major update | BASE/ULTRA | ABOUT tab | `VelopackUpdateService.cs`, `MajorUpdateService.cs`, `SupabaseManifestService.cs` | No | GitHub Releases binary, Supabase manifest. |

Rules:

- BASE manual features allowed.
- ULTRA background/realtime features only.
- Do not start feature work until build state is stable.

