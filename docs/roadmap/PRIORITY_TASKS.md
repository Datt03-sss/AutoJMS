# AutoJMS Priority Tasks

Backlog ưu tiên cho Phase 1.5 trở đi. Đây là kế hoạch, không phải danh sách thay đổi đã thực hiện.

| Priority | Task | Why | Files likely affected | Risk | Status |
| -------- | ---- | --- | --------------------- | ---- | ------ |
| P0 | Fix build blockers | Build pass là điều kiện trước khi refactor hoặc test sâu | `src/AutoJMS/AutoJMS.csproj`, `docs/troubleshooting/build-errors.md` | Medium | Done / monitor |
| P0 | Normalize module manifest/default files | ModuleSystem cần rõ file nào bắt buộc runtime, file nào optional | `modules/*.json`, `ModuleSystem/*`, docs | Medium | Planned |
| P0 | Fix JMS authToken 401 classifier | Tránh clear token sai và tránh nhầm license JWT với JMS token | `JmsApiClient.cs`, `JmsAuthTokenService.cs`, `JmsResponseClassifier.cs` | High | Planned |
| P0 | Ensure WebView2 token refresh on UI thread | WebView2 access ngoài UI thread gây crash/undefined behavior | `Main.cs`, `JmsAuthTokenService.cs`, `UiThread.cs` | High | Planned |
| P0 | Disable BASE background inventory/database sync | BASE tier phải ổn định, không chạy job ULTRA | `Main.cs`, `TierRuntimePolicy.cs`, `tier-definitions.json` | High | Planned |
| P1 | Stabilize FullStackOperation grid lifecycle | Tránh update grid disposed/null và race khi close form | `FullStackOperation.cs`, `Main.cs` | High | Planned |
| P1 | Stabilize GitHub Release + Supabase manifest update flow | Update phải predictable, không upload binary lớn lên Supabase | `release/*`, `docs/release/*`, `docs/manual/*` | Medium | Planned |
| P1 | Mask token logs before production | Full JMS token trong log là security issue | `Main.cs`, `JmsAuthTokenService.cs`, `AppLogger.cs` | High | Planned |
| P1 | Review secrets and rotate if needed | `service_account.json` tồn tại trong workspace; cần xử lý ngoài repo nếu đã lộ | Firebase/Google console, Render secrets, docs | High | Planned |
| P2 | Add minimal tests for TierRuntimePolicy/JMS token/manifest parsing | Có test trước khi refactor MainForm/release parsing | future test project | Medium | Planned |

Notes:

- Không sửa code từ file này. Dùng `.agent/tasks/*.md` để mở task riêng.
- Nếu task cần sửa `.cs`, `.csproj`, script hoặc `server.js`, phải có yêu cầu riêng và planned files trước khi patch.


