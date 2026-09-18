# Hợp Nhất Phân Giải Mã Bưu Cục (MiddleCode) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Gom toàn bộ đường phân giải mã bưu cục về một nguồn duy nhất `SiteContextProvider.Get()`, xoá sạch hardcode `214A02` khỏi đường chạy thật, và khi chưa có mã thì bỏ gọi API + hỏi người dùng một lần rồi lưu vào config.

**Architecture:** Thêm hai thành viên `static` vào `SiteContextProvider` (`Get()` có RAM cache, `Require(Form?)` mở dialog khi rỗng). Năm resolver rải rác giữ nguyên tên/chữ ký nhưng thân hàm rút còn một dòng uỷ quyền cho `Get()` — 22 call site hiện có không phải đổi. Fallback `"214A02"` bị gỡ khỏi ba sync service; chỗ nào trước đây dựa vào fallback thì nay thoát sớm.

**Tech Stack:** .NET 8 (`net8.0-windows`), WinForms, SunnyUI 3.9.6 (`Sunny.UI.UIInputDialog`), WebView2, xunit 2.7.0 + Moq (`tests/AutoJMS.Tests`).

## Global Constraints

- **Minimal Edit Rule** — chỉ sửa đúng phần cần thiết, giữ nguyên coding style, tên biến, format hiện có. Không refactor rộng.
- **Không file Protected nào được đụng** trong plan này. Cụ thể KHÔNG sửa: `src/AutoJMS/Program.cs`, `src/AutoJMS/Forms/Main.cs`, `src/AutoJMS/Forms/Main.Designer.cs`, `src/AutoJMS/Licensing/TierRuntimePolicy.cs`, `src/AutoJMS/Licensing/LicenseApiService.cs`, `src/AutoJMS/Licensing/JmsAuthTokenService.cs`, `src/AutoJMS/Updates/VelopackUpdateService.cs`, `release/build-release.ps1`, `installer/inno/AutoJMS.iss`. Nếu một task buộc phải chạm vào các file này → **DỪNG và hỏi Owner**.
- **Tab Boundary Rule** — thay đổi không được rò sang tab khác; `ABOUT` luôn là tab cuối.
- **Workspace Lock** — `.agent-lock.md` phải ghi `Current Writer: Claude Code (site-code-resolution)` / `Mode: WRITE` / `Scope: <danh sách file>` trước edit đầu tiên, trả về `None` / `READ_ONLY` / `None` sau khi push.
- **Never push if build fails.** `dotnet build -c Release` phải `0 Error(s)` và `eng\harness\verify.ps1` phải PASS cả 5 gate trước khi `git push origin main`.
- **Không `git add .`** — liệt kê đường dẫn cụ thể ở mọi lệnh `git add`.
- **Không xoá file.** Task 5 xoá một *hàm chết bên trong* `index.html`, không xoá file nào.
- **Secret Policy** — không commit `.env`, key, `*.pfx`, `*.pem`; token trong log mask dạng `first4...last4`.
- **Mã bưu cục hợp lệ được chuẩn hoá bằng `NormalizeCode`**: `(value ?? "").Trim().ToUpperInvariant()`. Giá trị `""` và `"0000"` đều tính là **chưa cấu hình**.
- Commit message kết thúc bằng dòng: `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`

---

## Đính chính so với bản spec đã duyệt

Bốn chi tiết trong spec không khớp code thật; plan này dùng bản đã kiểm chứng:

1. **`UIInputDialog.InputStringDialog` không tồn tại.** Chữ ký thật trong SunnyUI 3.9.6 (đã reflect từ assembly) là:
   `bool ShowInputStringDialog(Form owner, ref string value, bool checkEmpty, string desc, bool showMask, int maxLength)`.
2. **`Require` nhận `Form?` chứ không phải `IWin32Window?`.** Overload trên cần `Form`, và `InvokeRequired` cần `Control`. `FullStackOperation : UIForm` (kế thừa `Form`) nên `Require(this)` hợp lệ.
3. **Không xoá 5 resolver.** `DataHubSyncService.ResolveSiteCode()` có 15 call site trong chính file đó, `DataHubClient.ResolveSiteCode()` có 7. Giữ nguyên hàm, rút thân còn một dòng gọi `Get()` — cùng kết quả, diff nhỏ hơn 22 chỗ sửa.
4. **Filter `"0000"` phải được bảo toàn.** Hiện chỉ `DataHubSyncService.ResolveSiteCode()` coi `"0000"` là chưa cấu hình. Chuyển luật đó vào `Get()` để áp dụng ở mọi nơi.
5. **`FullStackInventorySyncService` không cần thêm guard.** Guard `missing or 0000` ở dòng 52-56 đã log **và** `return new FullStackSyncResult { ... }` đầy đủ từ trước. Nó chỉ chưa bao giờ chạy được vì fallback `"214A02"` khiến giá trị không bao giờ rỗng — gỡ fallback là nó tự sống lại. Task 3 chỉ sửa thân resolver ở file này.

Và một bổ sung: bảng 9 điểm trong spec bỏ sót `ThoiHieuKpiModels.cs:9,33` + `ThoiHieuKpiImageExporter.cs:31`. Đây là hardcode thật, rò `214A02` vào **ảnh KPI xuất ra** cho trạm khác — trái với nguyên tắc cốt lõi của chính spec. Đưa vào **Task 7**, Owner có thể bỏ task đó nếu muốn giữ đúng phạm vi bảng.

---

## File Structure

| File | Trách nhiệm sau thay đổi |
|---|---|
| `src/AutoJMS/Services/SiteContextProvider.cs` | **Nguồn sự thật duy nhất.** Thêm `Get()`, `Require(Form?)`, `InvalidateCache()`. |
| `tests/AutoJMS.Tests/SiteContextProviderGetTests.cs` | *(tạo mới)* Unit test cho `Get()` — cache, chuẩn hoá, luật `"0000"`. |
| `src/AutoJMS/FullStack/Services/DataHubSyncService.cs` | `ResolveSiteCode()` rút còn một dòng uỷ quyền. |
| `src/AutoJMS/Data/DataHubClient.cs` | `ResolveSiteCode()` rút còn một dòng uỷ quyền. |
| `src/AutoJMS/Services/InventorySyncService.cs` | Bỏ fallback `214A02`; rỗng → thoát sớm. |
| `src/AutoJMS/Services/StockCheckSyncService.cs` | Bỏ fallback `214A02`; rỗng → thoát sớm. |
| `src/AutoJMS/FullStack/Services/FullStackInventorySyncService.cs` | Bỏ fallback `214A02`; guard "missing or 0000" sẵn có (đang là code chết) tự sống lại. |
| `src/AutoJMS/Forms/FullStackOperation.ArrivalMonitor.cs` | Bỏ hằng `ArrivalStationCode`; dùng `Require(this)`; rỗng → bỏ gọi API. |
| `src/AutoJMS/Forms/FullStackOperation.Dashboard.cs` | `siteId` lấy từ `Get()` (hiển thị, không mở dialog). |
| `src/AutoJMS/Forms/FullStackOperation.cs` | Banner Thời Hiệu + fallback `quetMa` dùng `Get()`. |
| `src/AutoJMS/Web/index.html` | Bind `{{ siteId }}`; xoá hardcode trong state/fallback; xoá hàm chết `genOrders`. |
| `src/AutoJMS/FullStack/UI/ThoiHieu/ThoiHieuKpiModels.cs` | Default `SiteCode` thành `""`. |
| `src/AutoJMS/FullStack/UI/ThoiHieu/ThoiHieuKpiImageExporter.cs` | Fallback tên file dùng `Get()`. |

---

### Task 0: Lấy workspace lock

**Files:**
- Modify: `.agent-lock.md:2-4`

- [ ] **Step 1: Kiểm tra lock đang trống**

```bash
git switch main && git pull --ff-only origin main && git status --short && cat .agent-lock.md
```

Expected: working tree clean; `Current Writer: None`. Nếu thấy một định danh phiên khác → repo đang bị khoá, **DỪNG và chờ**, không ghi đè.

- [ ] **Step 2: Ghi lock**

Sửa `.agent-lock.md` dòng 2-4 từ:

```markdown
Current Writer: None
Mode: READ_ONLY
Scope: None
```

thành:

```markdown
Current Writer: Claude Code (site-code-resolution)
Mode: WRITE
Scope: SiteContextProvider, DataHubSyncService, DataHubClient, InventorySyncService, StockCheckSyncService, FullStackInventorySyncService, FullStackOperation.{ArrivalMonitor,Dashboard,cs}, Web/index.html, ThoiHieu KPI
```

- [ ] **Step 3: Commit lock**

```bash
git add .agent-lock.md && git commit -m "chore(lock): giu single-writer lock cho task site-code-resolution

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 1: `SiteContextProvider.Get()` + `InvalidateCache()`

Lõi logic thuần — làm TDD theo `.agent/rules` (WinForms/WebView2 ở các task sau thì không).

**Files:**
- Create: `tests/AutoJMS.Tests/SiteContextProviderGetTests.cs`
- Modify: `src/AutoJMS/Services/SiteContextProvider.cs:56-60` (thêm field), `:89-100` (thêm `InvalidateCache()` vào `ApplyLicenseMiddleCode`)

**Interfaces:**
- Consumes: `AppConfig.Current.ActionSiteCode` (string), `SettingsManager.Load().MiddleCode` (string), `SiteContextProvider.NormalizeCode(string?)` (private static, đã có tại dòng 177).
- Produces:
  - `public static string SiteContextProvider.Get()` → mã đã chuẩn hoá, hoặc `""` nếu chưa cấu hình. Không chạm đĩa sau lần gọi đầu.
  - `public static void SiteContextProvider.InvalidateCache()` → xoá RAM cache.

- [ ] **Step 1: Viết test thất bại**

Tạo `tests/AutoJMS.Tests/SiteContextProviderGetTests.cs`:

```csharp
using AutoJMS.Config;
using AutoJMS.Services;
using Xunit;

namespace AutoJMS.Tests;

// Các test này chạm state static toàn cục (AppConfig.Current). xunit chạy tuần tự
// trong cùng một class nên không cần khoá thêm, nhưng phải trả state về cũ.
public class SiteContextProviderGetTests
{
    private static string WithRuntimeCode(string code)
    {
        AppConfig.Current.ActionSiteCode = code;
        SiteContextProvider.InvalidateCache();
        return SiteContextProvider.Get();
    }

    [Fact]
    public void Get_NormalizesRuntimeCode()
    {
        Assert.Equal("214A99", WithRuntimeCode("  214a99 "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0000")]
    public void Get_TreatsUnsetSentinelsAsEmpty(string raw)
    {
        // "0000" là sentinel "chưa cấu hình" mà DataHubSyncService vẫn luôn lọc.
        // Luật đó giờ nằm ở Get() nên áp dụng cho mọi caller.
        AppConfig.Current.ActionSiteCode = raw;
        SiteContextProvider.InvalidateCache();
        Assert.NotEqual("0000", SiteContextProvider.Get());
    }

    [Fact]
    public void Get_CachesUntilInvalidated()
    {
        Assert.Equal("214A77", WithRuntimeCode("214A77"));

        AppConfig.Current.ActionSiteCode = "214A88";
        Assert.Equal("214A77", SiteContextProvider.Get());   // vẫn là giá trị cache

        SiteContextProvider.InvalidateCache();
        Assert.Equal("214A88", SiteContextProvider.Get());
    }
}
```

**Không** viết test cho `ApplyLicenseMiddleCode` ở đây: hàm đó gọi `SettingsManager.Save()`, tức là **ghi đè `AutoJMS.json` thật trên máy chạy test**. Nhánh đó thuộc Owner Manual Test Checklist (Ca 3), không phải unit test.

- [ ] **Step 2: Chạy test để xác nhận FAIL**

```bash
dotnet test tests/AutoJMS.Tests/AutoJMS.Tests.csproj --filter SiteContextProviderGetTests
```

Expected: FAIL khi biên dịch — `'SiteContextProvider' does not contain a definition for 'Get'` và `'InvalidateCache'`.

- [ ] **Step 3: Thêm cache field**

Trong `src/AutoJMS/Services/SiteContextProvider.cs`, ngay dưới dòng 58 `private static readonly object Sync = new();`, thêm:

```csharp
    // Get() là đường nóng: Dashboard, sync service và ArrivalMonitor gọi nó mỗi nhịp
    // refresh. Getter `Current` bên dưới đọc (và có khi ghi) AutoJMS.json ở MỖI lần
    // truy cập — không được để đường nóng đi qua đó.
    private static string? _cachedMiddleCode;
    private static bool _promptedThisSession;
```

- [ ] **Step 4: Thêm `Get()` và `InvalidateCache()`**

Trong cùng file, chèn ngay TRƯỚC `public static void ApplyLicenseMiddleCode(...)` (dòng 89):

```csharp
    /// <summary>
    /// Mã bưu cục (middleCode) đã chuẩn hoá, hoặc "" nếu chưa cấu hình.
    /// Nguồn sự thật duy nhất cho toàn bộ app. Chỉ chạm đĩa ở lần gọi đầu tiên.
    /// </summary>
    public static string Get()
    {
        lock (Sync)
        {
            if (_cachedMiddleCode != null) return _cachedMiddleCode;

            string runtime = NormalizeCode(AppConfig.Current.ActionSiteCode);
            if (runtime.Length == 0 || runtime == "0000")
            {
                // AutoJMS.json giữ lại middleCode của lần verify online gần nhất,
                // nên lần chạy offline sau vẫn ra đúng mã.
                runtime = NormalizeCode(SettingsManager.Load().MiddleCode);
            }

            _cachedMiddleCode = runtime == "0000" ? "" : runtime;
            return _cachedMiddleCode;
        }
    }

    public static void InvalidateCache()
    {
        lock (Sync) { _cachedMiddleCode = null; }
    }
```

- [ ] **Step 5: Làm mới cache khi license áp mã mới**

Trong `ApplyLicenseMiddleCode`, ngay sau dòng `AppConfig.SaveCurrent();`, thêm:

```csharp
        InvalidateCache();
```

- [ ] **Step 6: Chạy test để xác nhận PASS**

```bash
dotnet test tests/AutoJMS.Tests/AutoJMS.Tests.csproj --filter SiteContextProviderGetTests
```

Expected: `Passed! - Failed: 0, Passed: 5` (1 `[Fact]` + 3 ca `[Theory]` + 1 `[Fact]`)

- [ ] **Step 7: Commit**

```bash
git add src/AutoJMS/Services/SiteContextProvider.cs tests/AutoJMS.Tests/SiteContextProviderGetTests.cs
git commit -m "feat(sitecode): them SiteContextProvider.Get() co cache lam nguon su that duy nhat

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: `SiteContextProvider.Require(Form?)` + dialog nhập mã

**Files:**
- Modify: `src/AutoJMS/Services/SiteContextProvider.cs` (thêm `Require`, thêm `using System.Windows.Forms;`)

**Interfaces:**
- Consumes: `SiteContextProvider.Get()`, `SiteContextProvider.ApplyLicenseMiddleCode(string?)` (Task 1).
- Produces: `public static string SiteContextProvider.Require(Form? owner)` → mã đã chuẩn hoá; nếu chưa có thì mở dialog **một lần mỗi phiên**, lưu rồi trả về; user huỷ hoặc không có `owner` → `""`.

Không viết unit test cho task này: `Require` mở modal WinForms, thuộc diện Owner Manual Test Checklist theo `CLAUDE.md`. Phần logic thuần (`Get`) đã có test ở Task 1.

- [ ] **Step 1: Thêm `using`**

Trong `src/AutoJMS/Services/SiteContextProvider.cs`, sau dòng 7 (`using System.Threading.Tasks;`), thêm:

```csharp
using System.Windows.Forms;
```

- [ ] **Step 2: Thêm `Require`**

Chèn ngay sau `InvalidateCache()` (đã thêm ở Task 1):

```csharp
    /// <summary>
    /// Như <see cref="Get"/> nhưng khi chưa cấu hình thì hỏi người dùng rồi lưu lại.
    /// Chỉ hỏi MỘT lần mỗi phiên — các vòng lặp refresh nền không được spam dialog.
    /// Trả về "" nếu user huỷ hoặc không có form chủ để làm owner cho modal.
    /// </summary>
    public static string Require(Form? owner)
    {
        string current = Get();
        if (current.Length > 0) return current;

        // Không có owner nghĩa là đang ở thread nền không có UI — mở modal ở đó
        // sẽ dựng message loop lạc chỗ. Thà bỏ API còn hơn.
        if (owner == null || owner.IsDisposed) return "";

        lock (Sync)
        {
            if (_promptedThisSession) return "";
            _promptedThisSession = true;
        }

        string entered = "";
        bool ok = false;
        void Prompt() => ok = Sunny.UI.UIInputDialog.ShowInputStringDialog(
            owner, ref entered,
            checkEmpty: true,
            desc: "Chưa xác định được mã bưu cục từ license. Vui lòng nhập mã bưu cục (Middle Code):",
            showMask: true,
            maxLength: 16);

        if (owner.InvokeRequired) owner.Invoke((Action)Prompt);
        else Prompt();

        if (!ok) return "";

        string normalized = NormalizeCode(entered);
        if (normalized.Length == 0 || normalized == "0000") return "";

        // Ghi vào CẢ AppConfig lẫn AutoJMS.json để lần chạy sau (kể cả offline) có sẵn.
        ApplyLicenseMiddleCode(normalized);
        AppLogger.Info($"[SiteContext] middleCode nhap tay source=dialog value={normalized}");
        return normalized;
    }
```

- [ ] **Step 3: Build để xác nhận chữ ký SunnyUI khớp**

```bash
dotnet build src/AutoJMS/AutoJMS.csproj -c Release
```

Expected: `Build succeeded. 0 Error(s)`.

Nếu lỗi `CS1501 No overload for 'ShowInputStringDialog'`: bỏ các named argument và truyền theo đúng thứ tự vị trí `(owner, ref entered, true, "…", true, 16)`.

- [ ] **Step 4: Commit**

```bash
git add src/AutoJMS/Services/SiteContextProvider.cs
git commit -m "feat(sitecode): them Require() hoi ma buu cuc mot lan moi phien va luu vao config

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Trỏ 5 resolver về `Get()`, bỏ fallback `214A02`

Đây là task gỡ nguồn sai. Sau task này không resolver nào còn tự bịa ra mã trạm.

**Files:**
- Modify: `src/AutoJMS/FullStack/Services/DataHubSyncService.cs:88-93`
- Modify: `src/AutoJMS/Data/DataHubClient.cs:1277`
- Modify: `src/AutoJMS/Services/InventorySyncService.cs:27-32` và `:162`
- Modify: `src/AutoJMS/Services/StockCheckSyncService.cs:37-42` và `:58`
- Modify: `src/AutoJMS/FullStack/Services/FullStackInventorySyncService.cs:558-563`

**Interfaces:**
- Consumes: `SiteContextProvider.Get()` (Task 1).
- Produces: không API mới. `DataHubSyncService.ResolveSiteCode()`, `DataHubClient.ResolveSiteCode()`, `InventorySyncService.GetActionSiteCode()`, `StockCheckSyncService.GetNetworkCode()`, `FullStackInventorySyncService.GetActionSiteCode()` giữ nguyên tên và chữ ký `static string ()` — 22 call site hiện có không đổi.

- [ ] **Step 1: `DataHubSyncService.ResolveSiteCode()`**

Thay nguyên thân hàm tại dòng 88 từ:

```csharp
        public static string ResolveSiteCode()
        {
            var site = (AppConfig.Current.ActionSiteCode ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(site) || site == "0000") return "";
            return site;
        }
```

thành:

```csharp
        public static string ResolveSiteCode() => SiteContextProvider.Get();
```

Thêm `using AutoJMS.Services;` vào đầu file nếu chưa có.

- [ ] **Step 2: `DataHubClient.ResolveSiteCode()`**

Thay dòng 1277 từ:

```csharp
    private static string ResolveSiteCode() => new SiteContextProvider().Current?.MiddleCode ?? string.Empty;
```

thành:

```csharp
    // Get() thay cho `.Current` vì `Current` đọc (và có khi ghi) AutoJMS.json mỗi lần
    // truy cập, còn hàm này bị gọi trên mọi đường lease/observation.
    private static string ResolveSiteCode() => SiteContextProvider.Get();
```

- [ ] **Step 3: `InventorySyncService`**

Thay dòng 27-32 từ:

```csharp
        private static string GetActionSiteCode()
        {
            if (!string.IsNullOrWhiteSpace(AppConfig.Current.ActionSiteCode))
                return AppConfig.Current.ActionSiteCode.Trim();
            return "214A02";
        }
```

thành:

```csharp
        private static string GetActionSiteCode() => SiteContextProvider.Get();
```

Hàm này có hai call site (dòng 78 trong `RunInventorySyncAsync`, dòng 162 trong `FetchAllInventoryWaybillsWithRetryAsync`). Chỉ cần **một** guard: `FetchAllInventoryWaybillsWithRetryAsync` là nơi thực sự bắn request JMS và cả hai đường đều đi qua nó. Tại dòng 162, thay:

```csharp
            string actionSiteCode = GetActionSiteCode();
```

bằng:

```csharp
            string actionSiteCode = GetActionSiteCode();
            if (actionSiteCode.Length == 0)
            {
                // Trước đây rơi về "214A02" và lẳng lặng kéo tồn kho của trạm khác.
                AppLogger.Warning("[InventorySync] bo qua: chua cau hinh ma buu cuc.");
                return new List<string>();
            }
```

- [ ] **Step 4: `StockCheckSyncService`**

Thay dòng 37-42 từ:

```csharp
        private static string GetNetworkCode()
        {
            if (!string.IsNullOrWhiteSpace(AppConfig.Current.ActionSiteCode))
                return AppConfig.Current.ActionSiteCode.Trim();
            return "214A02";
        }
```

thành:

```csharp
        private static string GetNetworkCode() => SiteContextProvider.Get();
```

Rồi tại dòng 58, thay:

```csharp
            string networkCode = GetNetworkCode();
```

thành:

```csharp
            string networkCode = GetNetworkCode();
            if (networkCode.Length == 0)
            {
                AppLogger.Warning("[StockCheckSync] bo qua: chua cau hinh ma buu cuc.");
                return new List<string>();
            }
```

Hàm bao quanh là `FetchStockCheckWaybillsAsync` trả `Task<List<string>>`, và đã có sẵn một guard cùng kiểu ở dòng 49-53 (`No authToken; skip.`) — guard mới bám đúng khuôn đó.

- [ ] **Step 5: `FullStackInventorySyncService`**

Thay dòng 558-563 từ:

```csharp
        private static string GetActionSiteCode()
        {
            if (!string.IsNullOrWhiteSpace(AppConfig.Current.ActionSiteCode))
                return AppConfig.Current.ActionSiteCode.Trim();
            return "214A02";
        }
```

thành:

```csharp
        private static string GetActionSiteCode() => SiteContextProvider.Get();
```

**Chỉ sửa thân resolver, không đụng gì thêm trong file này.** `SyncInventoryAsync` đã có sẵn guard đầy đủ ở dòng 52-56 — log `FULLSTACK_SYNC_PARAM_ERROR` rồi `return new FullStackSyncResult { ... }`. Guard đó lâu nay là **code chết** vì fallback `"214A02"` khiến `actionSiteCode` không bao giờ rỗng; gỡ fallback xong nó tự sống lại đúng như tác giả đã viết. Không thêm `return` nào nữa.

- [ ] **Step 6: Xác nhận không còn fallback nào sót**

```bash
git grep -n "214A02" -- "src/AutoJMS/Services" "src/AutoJMS/FullStack/Services" "src/AutoJMS/Data"
```

Expected: không có kết quả.

- [ ] **Step 7: Build + test**

```bash
dotnet build .\AutoJMS.slnx -c Release && dotnet test tests/AutoJMS.Tests/AutoJMS.Tests.csproj
```

Expected: `Build succeeded. 0 Error(s)`, toàn bộ test PASS.

- [ ] **Step 8: Commit**

```bash
git add src/AutoJMS/FullStack/Services/DataHubSyncService.cs src/AutoJMS/Data/DataHubClient.cs src/AutoJMS/Services/InventorySyncService.cs src/AutoJMS/Services/StockCheckSyncService.cs src/AutoJMS/FullStack/Services/FullStackInventorySyncService.cs
git commit -m "fix(sitecode): 5 resolver uy quyen cho Get(), bo fallback 214A02 trong sync service

Chua cau hinh ma buu cuc thi bo sync thay vi keo nham du lieu tram khac.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: ArrivalMonitor — bỏ API khi chưa có mã

**Files:**
- Modify: `src/AutoJMS/Forms/FullStackOperation.ArrivalMonitor.cs:20` (xoá hằng), `:83` (dùng tham số)

**Interfaces:**
- Consumes: `SiteContextProvider.Require(Form?)` (Task 2).
- Produces: không API mới. `FetchArrivalBucketAsync` nhận thêm tham số `string stationCode` ở vị trí đầu sau `token`.

Đây là nơi duy nhất gọi `Require` — `FullStackOperation : UIForm` nên `this` là `Form` hợp lệ, và `RefreshArrivalMonitorAsync` resume trên UI thread nhờ `ConfigureAwait(true)`.

- [ ] **Step 1: Xoá hằng hardcode**

Xoá dòng 20:

```csharp
        private const string ArrivalStationCode = "214A02";
```

- [ ] **Step 2: Phân giải mã một lần trong `RefreshArrivalMonitorAsync`**

Trong `RefreshArrivalMonitorAsync`, chèn ngay **sau** guard authToken đang có ở dòng 36-41 (khối `if (string.IsNullOrWhiteSpace(token)) { ... return; }`) và **trước** dòng 43 `var today = DateTime.Now.Date;`:

```csharp
                // Đặt trong try, sau guard token, để bám đúng khuôn "thiếu điều kiện thì skip"
                // đã có sẵn ở trên và được catch của hàm bảo vệ.
                string stationCode = SiteContextProvider.Require(this);
                if (stationCode.Length == 0)
                {
                    // Bỏ API thay vì hỏi JMS bằng mã của trạm khác.
                    AppLogger.Warning("[ArrivalMonitor] bo qua: chua cau hinh ma buu cuc.");
                    return;
                }
```

`RefreshArrivalMonitorAsync` trả `Task` nên `return;` trơn là đúng. `ConfigureAwait(true)` ở dòng 36 đảm bảo đoạn này chạy trên UI thread, nên modal của `Require` mở hợp lệ.

- [ ] **Step 3: Truyền mã xuống `FetchArrivalBucketAsync`**

Đổi chữ ký tại dòng 73-75 từ:

```csharp
        private async Task<(List<object> List, int Total)> FetchArrivalBucketAsync(
            string token, int dateType, string jumpType, string startTime, string endTime,
            int size, string subLabel, string subKey, CancellationToken ct)
```

thành:

```csharp
        private async Task<(List<object> List, int Total)> FetchArrivalBucketAsync(
            string token, string stationCode, int dateType, string jumpType, string startTime, string endTime,
            int size, string subLabel, string subKey, CancellationToken ct)
```

Và trong body request tại dòng 83, thay:

```csharp
                    arrivalSationCode = ArrivalStationCode,
```

thành:

```csharp
                    arrivalSationCode = stationCode,
```

- [ ] **Step 4: Cập nhật 2 call site của `FetchArrivalBucketAsync`**

Có đúng hai call site — dòng 49 (`var arrived = await FetchArrivalBucketAsync(`) và dòng 53 (`var notScanned = await FetchArrivalBucketAsync(`). Ở cả hai, chèn `stationCode` làm đối số **thứ hai**, ngay sau `token`. Xác nhận lại:

```bash
git grep -n "FetchArrivalBucketAsync" -- src/AutoJMS/Forms/FullStackOperation.ArrivalMonitor.cs
```

Expected: 3 kết quả — 2 call site đã có `stationCode` và 1 dòng khai báo.

- [ ] **Step 5: Build**

```bash
dotnet build .\AutoJMS.slnx -c Release
```

Expected: `Build succeeded. 0 Error(s)`. Nếu `CS1501`, còn call site chưa cập nhật ở Step 4.

- [ ] **Step 6: Commit**

```bash
git add src/AutoJMS/Forms/FullStackOperation.ArrivalMonitor.cs
git commit -m "fix(arrival): lay ma buu cuc tu SiteContextProvider, bo API khi chua cau hinh

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Dashboard C# + template `index.html`

Gộp một task vì `siteId` bên C# và binding `{{ siteId }}` bên template là hai nửa của cùng một đường dữ liệu — review tách ra sẽ không kết luận được.

**Files:**
- Modify: `src/AutoJMS/Forms/FullStackOperation.Dashboard.cs:1086`
- Modify: `src/AutoJMS/Web/index.html:48, 653, 658, 871, 1865`, xoá `:1391-1429`

**Interfaces:**
- Consumes: `SiteContextProvider.Get()` (Task 1). Message `UPDATE_DATA` đã mang sẵn `siteId` và template đã map nó vào state tại `index.html:679` — chỉ dòng 48 là chưa bind.
- Produces: không API mới.

**Ràng buộc template** *(xem memory `dashboard-page-css-and-csp-constraints`)*: **không** sửa `support.js`, **không** thêm `<script>` inline vào `index.html` — CSP của trang là `script-src 'self' 'unsafe-eval'`, không có `'unsafe-inline'`.

- [ ] **Step 1: C# — thôi hardcode `siteId`**

Trong `src/AutoJMS/Forms/FullStackOperation.Dashboard.cs`, thay dòng 1086:

```csharp
                string siteId = "214A02";
```

bằng:

```csharp
                // Get() chứ không Require(): đây là đường hiển thị chạy mỗi nhịp refresh,
                // không phải chỗ để bật modal. Rỗng thì UI hiện "---".
                string siteId = SiteContextProvider.Get();
```

- [ ] **Step 2: Template — bind ô "Bưu cục"**

Trong `src/AutoJMS/Web/index.html`, thay dòng 48:

```html
        214A02
```

bằng:

```html
        {{ siteId }}
```

- [ ] **Step 3: Template — dọn state khởi tạo**

Dòng 653, thay `siteId: '214A02',` bằng:

```js
    siteId: '',
```

Dòng 658, thay `selectedSource: '214A02',` bằng:

```js
    selectedSource: 'LOCAL',
```

(`'LOCAL'` là giá trị mà dòng 685 và 1868 vốn đã dùng làm mặc định — dòng 658 là chỗ duy nhất lệch.)

- [ ] **Step 4: Template — fallback hiển thị và fallback ghi**

Dòng 1865, thay `siteId: this.state.siteId || '214A02',` bằng:

```js
      siteId: this.state.siteId || '---',
```

Dòng 871, thay `return this.state.receiverNetCode || this.state.siteId || '214A02';` bằng:

```js
    // Đây là đường GHI: giá trị này đi vào "Bưu cục tiếp nhận" của issue-report gửi lên
    // JMS. Không có mã thật thì trả rỗng — tuyệt đối không bịa mã trạm.
    return this.state.receiverNetCode || this.state.siteId || '';
```

- [ ] **Step 5: Xoá hàm chết `genOrders`**

Xác nhận không còn tham chiếu:

```bash
git grep -c "genOrders" -- src/AutoJMS/Web/index.html
```

Expected: `1` (chỉ dòng khai báo).

Xoá dòng 1391 đến 1429 — từ `  genOrders(seed, count, mainKey, opts) {` đến dấu `  }` đóng hàm cùng dòng trống ngay sau, tức là toàn bộ khối nằm giữa `  }` kết thúc hàm trước nó và `  buildSummary(wb, status) {`.

- [ ] **Step 6: Xác nhận template sạch**

```bash
git grep -c "214A02" -- src/AutoJMS/Web/index.html
```

Expected: không có kết quả (exit code 1).

- [ ] **Step 7: Build**

```bash
dotnet build .\AutoJMS.slnx -c Release
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 8: Commit**

```bash
git add src/AutoJMS/Forms/FullStackOperation.Dashboard.cs src/AutoJMS/Web/index.html
git commit -m "fix(tabDash): bind o Buu cuc theo ma that, xoa hardcode 214A02 khoi template

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: Banner Thời Hiệu + fallback `quetMa`

**Files:**
- Modify: `src/AutoJMS/Forms/FullStackOperation.cs:3384, 4028`

**Interfaces:**
- Consumes: `SiteContextProvider.Get()` (Task 1).
- Produces: không API mới.

- [ ] **Step 1: Banner động**

Thay dòng 3384:

```csharp
            banner.Text = "BẢNG KÝ NHẬN THỜI HIỆU THEO MỐC THỜI GIAN 214A02";
```

bằng:

```csharp
            string bannerSite = SiteContextProvider.Get();
            banner.Text = bannerSite.Length == 0
                ? "BẢNG KÝ NHẬN THỜI HIỆU THEO MỐC THỜI GIAN"
                : $"BẢNG KÝ NHẬN THỜI HIỆU THEO MỐC THỜI GIAN {bannerSite}";
```

- [ ] **Step 2: Fallback `quetMa`**

Thay dòng 4028:

```csharp
                    string quetMa = list.FirstOrDefault(w => !string.IsNullOrEmpty(w.BuuCucThaoTac))?.BuuCucThaoTac ?? "214A02";
```

bằng:

```csharp
                    string quetMa = list.FirstOrDefault(w => !string.IsNullOrEmpty(w.BuuCucThaoTac))?.BuuCucThaoTac
                                    ?? SiteContextProvider.Get();
```

- [ ] **Step 3: Build**

```bash
dotnet build .\AutoJMS.slnx -c Release
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/AutoJMS/Forms/FullStackOperation.cs
git commit -m "fix(thoihieu): banner va fallback quetMa lay ma buu cuc tu SiteContextProvider

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: ThoiHieu KPI — mã bưu cục trong ảnh xuất ra

> **Bổ sung ngoài bảng 9 điểm của spec.** Hai default `= "214A02"` này chảy thẳng vào **ảnh KPI người dùng xuất ra** và vào **tên file**, nên trạm khác sẽ xuất ra ảnh mang mã của trạm 214A02. Nếu Owner muốn giữ đúng phạm vi bảng đã duyệt thì bỏ qua task này — các task khác không phụ thuộc vào nó.

**Files:**
- Modify: `src/AutoJMS/FullStack/UI/ThoiHieu/ThoiHieuKpiModels.cs:9, 33`
- Modify: `src/AutoJMS/FullStack/UI/ThoiHieu/ThoiHieuKpiImageExporter.cs:31`

**Interfaces:**
- Consumes: `SiteContextProvider.Get()` (Task 1).
- Produces: không API mới. Default của `SiteCode` đổi từ `"214A02"` thành `""`; caller nào chưa gán sẽ nhận giá trị từ exporter.

- [ ] **Step 1: Default rỗng**

Trong `ThoiHieuKpiModels.cs`, tại cả dòng 9 và dòng 33, thay:

```csharp
        public string SiteCode { get; set; } = "214A02";
```

bằng:

```csharp
        public string SiteCode { get; set; } = "";
```

- [ ] **Step 2: Exporter lấy mã thật**

Trong `ThoiHieuKpiImageExporter.cs`, thay dòng 31:

```csharp
                string siteCode = SanitizeFilePart(string.IsNullOrWhiteSpace(data.SiteCode) ? "214A02" : data.SiteCode);
```

bằng:

```csharp
                string resolvedSite = string.IsNullOrWhiteSpace(data.SiteCode)
                    ? SiteContextProvider.Get()
                    : data.SiteCode;
                // Đây là một phần TÊN FILE nên không được rỗng.
                string siteCode = SanitizeFilePart(resolvedSite.Length == 0 ? "KHONG-RO" : resolvedSite);
```

Thêm `using AutoJMS.Services;` vào đầu file nếu chưa có.

- [ ] **Step 3: Xác nhận chỉ còn sample data**

```bash
git grep -n "214A02" -- src/AutoJMS
```

Expected: **chỉ** hai dòng `ThoiHieuKpiSampleData.cs:73` và `:92` (dữ liệu demo, giữ nguyên có chủ ý).

- [ ] **Step 4: Build**

```bash
dotnet build .\AutoJMS.slnx -c Release
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add src/AutoJMS/FullStack/UI/ThoiHieu/ThoiHieuKpiModels.cs src/AutoJMS/FullStack/UI/ThoiHieu/ThoiHieuKpiImageExporter.cs
git commit -m "fix(thoihieu-kpi): ma buu cuc trong anh xuat ra lay theo cau hinh thay vi 214A02

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: Verify toàn bộ, nhả lock, push

**Files:**
- Modify: `.agent-lock.md:2-4`

- [ ] **Step 1: Restore + build Release**

```bash
dotnet restore .\AutoJMS.slnx && dotnet build .\AutoJMS.slnx -c Release
```

Expected: `Build succeeded. 0 Warning(s), 0 Error(s)`.

Nếu Tests/NodeTests/Secrets cùng FAIL một lượt: máy 8 GB hết RAM chứ không phải lỗi code — chạy `dotnet build-server shutdown` rồi build lại, **đừng sửa code**.

- [ ] **Step 2: Harness 5 gate**

```bash
powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
```

Expected: cả 5 gate PASS.

Lưu ý: harness **không dựng form thật** nên không gate nào chạm tới `Require()`, dialog, hay WebView2. Smoke test của Owner ở Step 5 là lớp kiểm tra duy nhất cho phần đó.

- [ ] **Step 3: Quét lại hardcode lần cuối**

```bash
git grep -n "214A02" -- src/ | grep -v SampleData
```

Expected: không có kết quả.

- [ ] **Step 4: Nhả lock và push**

Trả `.agent-lock.md` dòng 2-4 về:

```markdown
Current Writer: None
Mode: READ_ONLY
Scope: None
```

```bash
git add .agent-lock.md
git commit -m "chore(lock): nha single-writer lock sau khi xong site-code-resolution

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
git push origin main && git log --oneline -1 && git status
```

- [ ] **Step 5: Owner Manual Test Checklist**

WinForms Designer code và WebView2 automation không có test tự động theo `CLAUDE.md` — Owner xác nhận bằng tay:

- **Ca 1 — đã có license/config:** mở Vận hành → Dashboard. Ô **Bưu cục** trên Filter Bar hiện đúng mã trạm của tài khoản, không phải `214A02`. Bấm **Làm mới** và **Đồng bộ**, mã vẫn đúng.
- **Ca 2 — offline:** ngắt mạng, mở lại app. Mã bưu cục vẫn hiện đúng (đọc từ `AutoJMS.json`).
- **Ca 3 — chưa cấu hình:** xoá `MiddleCode` trong `AutoJMS.json` và `actionSiteCode` trong config runtime, mở Dashboard. Dialog SunnyUI hỏi mã hiện **đúng một lần**; nhập mã → mở lại `AutoJMS.json` thấy mã mới đã được ghi; đóng/mở lại app không hỏi nữa.
- **Ca 4 — huỷ dialog:** ở tình huống Ca 3 bấm Cancel. App không crash; panel Hàng đến trống và log có `[ArrivalMonitor] bo qua`; dialog không bật lại trong cùng phiên.
- **Ca 5 — Thời Hiệu:** mở bảng Thời Hiệu, banner hiện đúng mã trạm; xuất ảnh KPI, tên file và nội dung mang đúng mã (nếu làm Task 7).
- **Ca 6 — tab khác:** HOME, DKCH, TRACKING, PRINT mở bình thường, `ABOUT` vẫn là tab cuối.

---

## Rủi ro đã biết

1. **`ShowInputStringDialog` là extension method của SunnyUI** — chữ ký đã reflect từ chính `SunnyUI.dll` 3.9.6 trong package cache, nhưng named argument có thể không khớp nếu tên tham số khác. Task 2 Step 3 có sẵn đường lùi sang positional argument.
2. **`Require` chỉ được gọi từ ArrivalMonitor.** Nếu sau này có thread nền gọi nó với `owner == null`, nó trả `""` im lặng — đúng thiết kế, nhưng người dùng sẽ không thấy dialog. Đó là lý do ArrivalMonitor truyền `this`.
3. **Gỡ fallback `214A02` là thay đổi hành vi thật.** Máy nào đang chạy với `ActionSiteCode` rỗng sẽ chuyển từ "lẳng lặng kéo dữ liệu của 214A02" sang "không sync và có log cảnh báo". Đó là mục tiêu, nhưng nếu có trạm đang vô tình sống nhờ fallback thì họ sẽ thấy dữ liệu biến mất cho tới khi nhập mã.
4. **`DkchJourneyAnalyzer.cs:423` vẫn còn `netCode == "214A02"`** kèm `"Kim Tân"` và `"(LCI)"` — đây là nhận diện trạm nhà bằng alias, nằm trên đường `PrintSafetyGuard`. Cố ý để lại, tách task riêng.
5. **`SiteContextProvider.Current` (getter cũ) vẫn đọc/ghi `AutoJMS.json` mỗi lần truy cập.** Plan này chỉ đưa đường nóng ra khỏi nó, không sửa nó — các caller còn lại của `Current` giữ nguyên hành vi.
