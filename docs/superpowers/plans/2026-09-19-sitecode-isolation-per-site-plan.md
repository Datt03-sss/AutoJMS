# Cô lập dữ liệu giữa các Middle Code (Site Code) trên cùng máy

**Nguồn:** Antigravity Prompt Proposal, Owner chuyển tiếp 2026-09-19.
**Base commit:** `dfbdff3`

## Mục tiêu

Khi đổi license có `middleCode` khác trên cùng một máy (ví dụ `214A02` → `214A03`), toàn bộ
dữ liệu cục bộ (SQLite DB), sync cache, alias mã và tên bưu cục phải được cô lập theo trạm.
Không được trộn vận đơn hoặc giữ alias của trạm cũ.

## Global Constraints

- **Minimal Edit Rule.** Chỉ sửa đúng chỗ cần; giữ nguyên style, tên biến, format hiện có.
- **`src/AutoJMS/Forms/Main.cs` là Protected File.** Owner đã cho phép rõ ràng cho *task này*
  (2026-09-19: "Cho phép claude sửa các file được bảo vệ, chỉ cần xử lý được vấn đề").
  Chỉ được sửa đúng các dòng đường dẫn sync cache — không đụng bất kỳ phần nào khác.
- **Tab Boundary Rule.** Thay đổi không được rò sang tab khác; `ABOUT` luôn là tab cuối.
- Namespace phẳng `namespace AutoJMS` cho code ngoài `FullStack` — `using AutoJMS.Services;`
  KHÔNG compile (CS0246). Code trong `FullStack/LocalDb` dùng `namespace AutoJMS.FullStack.LocalDb`.
- Build phải `0 Warning(s), 0 Error(s)` ở cấu hình Release.
- **Test không được làm hỏng cấu hình thật của máy dev.** Xem Task 4.
- Không `git add .`; không xoá file; không bump version; không push khi build fail.

## Ngữ cảnh đã kiểm chứng (base `dfbdff3`)

- `SiteContextProvider.ApplyLicenseMiddleCode` (dòng ~228) hiện `Append(normalized)` vào
  `MiddleCodeAliases` → tích lũy mã của trạm cũ.
- `SiteNameAliases` (mới thêm ở `dfbdff3`) chứa tên trạm đã học; hiện không được reset.
- Ba factory đều trỏ cứng `Path.Combine(AppPaths.UserDataDir, "FullStack", "<file>.db")`.
  `FullStackDbConnectionFactory` và `JourneyHistoryDbConnectionFactory` cùng trỏ
  `journey_history.db`; `WaybillJourneyDetailsDbConnectionFactory` trỏ `details.db`.
- `FullStackDbConnectionFactory.OpenAsync` **đã có sẵn** `Directory.CreateDirectory(...)`.
  Hai factory kia cần kiểm tra tương tự.
- Khoá SQLCipher là **một khoá chung** (`AppData\secure\fullstack-db.key`), không gắn theo
  đường dẫn DB, và `LocalDbEncryption.PreparedPaths` là HashSet theo path → tách thư mục
  theo trạm không ảnh hưởng lớp mã hoá.
- Mọi consumer đọc `DatabasePath` qua property (`FullStackDashboardService.DatabasePath`,
  `WaybillJourneyDetailsRepository.DatabasePath`, 3 initializer, `FullStackJourneyService:368`)
  → chuyển sang property động là tương thích, không ai cache vào field.
- `Program.cs:347` gọi `ApplyLicenseMiddleCode` lúc khởi động, trước khi `FullStackOperation`
  tồn tại → `Get()` đã có giá trị trước lần mở DB đầu tiên.
- `Main.cs:82-84` khai báo `_syncCacheFilePath`; đọc tại dòng 924, 925, 949, 961.

---

## Task 1: Reset alias khi đổi trạm

**File:** `src/AutoJMS/Services/SiteContextProvider.cs`

Sửa `ApplyLicenseMiddleCode(string? middleCode)`:

- Đọc `oldCode` từ `NormalizeCode(settings.MiddleCode)` **trước khi** gán giá trị mới.
- `bool isStationChange = !string.Equals(oldCode, normalized, StringComparison.OrdinalIgnoreCase);`
- Nếu `isStationChange`:
  - `settings.MiddleCodeAliases = normalized.Length == 0 ? new List<string>() : new List<string> { normalized };`
  - `settings.SiteNameAliases = new List<string>();`
  - Log: `AppLogger.Info($"[SiteContext] doi tram '{oldCode}' -> '{normalized}', reset MiddleCodeAliases va SiteNameAliases");`
- Nếu KHÔNG đổi trạm (re-apply cùng mã): giữ nguyên hành vi hiện tại — `MiddleCodeAliases`
  vẫn là `DistinctCodes(settings.MiddleCodeAliases.Append(normalized))`, `SiteNameAliases`
  giữ nguyên (tên đã học phải sống sót qua mỗi lần khởi động).
- Giữ nguyên thứ tự hiện có: `SettingsManager.Save(settings);` rồi `InvalidateCache();`.
  **Không** đảo thứ tự này.

`InvalidateCache()` đã reset đủ `_cachedMiddleCode`, `_homeCodes`, `_siteNames`,
`_promptedThisSession` — chỉ xác nhận, không sửa.

---

## Task 2: Phân vùng database theo site

**Files:** `src/AutoJMS/FullStack/LocalDb/FullStackLocalDbPaths.cs` (mới) và ba factory.

1. Tạo `FullStackLocalDbPaths` trong `namespace AutoJMS.FullStack.LocalDb`:

```csharp
public static class FullStackLocalDbPaths
{
    public static string GetDatabasePath(string dbFileName)
    {
        string siteCode = SiteContextProvider.Get();
        string folder = string.IsNullOrWhiteSpace(siteCode) ? "default" : SanitizeFolderName(siteCode);
        return Path.Combine(AppPaths.UserDataDir, "FullStack", folder, dbFileName);
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "default" : clean;
    }
}
```

2. `FullStackDbConnectionFactory.cs`: đổi `public string DatabasePath { get; }` + gán trong
   constructor thành `public string DatabasePath => FullStackLocalDbPaths.GetDatabasePath("journey_history.db");`
   và xoá constructor rỗng nếu nó chỉ còn để gán path.
3. `JourneyHistoryDbConnectionFactory.cs`: `=> FullStackLocalDbPaths.GetDatabasePath("journey_history.db");`
4. `WaybillJourneyDetailsDbConnectionFactory.cs`: `=> FullStackLocalDbPaths.GetDatabasePath("details.db");`
5. Mỗi `OpenAsync` phải đảm bảo có `Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);`
   trước khi mở kết nối — thư mục trạm phải tự tạo. `FullStackDbConnectionFactory` đã có;
   kiểm tra và bổ sung cho hai factory còn lại nếu thiếu.

Không sửa schema, không thêm cột `site_code`, không viết migration dữ liệu cũ.

---

## Task 3: Sync cache theo site

**File:** `src/AutoJMS/Forms/Main.cs` — **Protected File, Owner đã cho phép cho task này.**
Chỉ sửa đúng phần đường dẫn sync cache, tuyệt đối không đụng phần khác của file.

Thay field `_syncCacheFilePath` (dòng ~82-84) bằng property động:

```csharp
private string SyncCacheFilePath
{
    get
    {
        string site = SiteContextProvider.Get();
        string suffix = string.IsNullOrWhiteSpace(site) ? "default" : site;
        return Path.Combine(AppPaths.CacheDir, $"sync-cache-{DataHubClient.MachineId}-{suffix}.json");
    }
}
```

Cập nhật 4 chỗ đọc field cũ sang property mới: dòng ~924, ~925, ~949, ~961.

---

## Task 4: Unit tests

**File:** `tests/AutoJMS.Tests/SiteIsolationTests.cs` (mới)

Class phải gắn `[Collection("SiteContext")]` — collection này đã được định nghĩa trong
`tests/AutoJMS.Tests/HomeStationDetectionTests.cs` với `DisableParallelization = true`,
vì các test này đụng static state của `SiteContextProvider`.

**BẮT BUỘC — bảo vệ cấu hình thật của máy dev.** `ApplyLicenseMiddleCode` ghi thẳng xuống
`AutoJMS.json` và `AppConfig` trên đĩa. Constructor phải chụp lại và `Dispose()` phải khôi
phục đầy đủ: `AppConfig.Current.ActionSiteCode` (kèm `AppConfig.SaveCurrent()`), và
`settings.MiddleCode`, `settings.MiddleCodeAliases`, `settings.SiteNameAliases`
(kèm `SettingsManager.Save`). Kết thúc bằng `SiteContextProvider.InvalidateCache()`.
Test nào làm mất mã bưu cục thật của Owner là test hỏng.

1. `StationChange_ResetsAliasesAndNames`
   - Đặt trạm `"214A02"` qua `ApplyLicenseMiddleCode("214A02")`.
   - Học tên: `Assert.True(SiteContextProvider.IsHomeStation("214A02", "Bưu cục Kim Tân"));`
   - Đổi trạm: `ApplyLicenseMiddleCode("214A03")`.
   - Assert: `SettingsManager.Load().MiddleCode == "214A03"`; `MiddleCodeAliases` chỉ chứa
     `"214A03"` và KHÔNG chứa `"214A02"`; `SiteNameAliases` rỗng;
     `IsHomeStation("214A02", "Bưu cục Kim Tân")` là `false`;
     `IsHomeStation("214A03", "")` là `true`.

2. `ReApplySameCode_KeepsLearnedNames`
   - Đặt `"214A02"`, học tên `"Bưu cục Kim Tân"`.
   - Gọi lại `ApplyLicenseMiddleCode("214A02")` (mô phỏng khởi động lại app cùng license).
   - Assert `SiteNameAliases` VẪN chứa `"Bưu cục Kim Tân"` — tên học được không được mất
     mỗi lần mở app.

3. `DatabasePath_PartitionedBySiteCode`
   - Với `"214A02"` → `new FullStackDbConnectionFactory().DatabasePath` chứa thư mục `"214A02"`.
   - Với `"214A03"` → chứa `"214A03"`.
   - Hai đường dẫn phải khác nhau.
   - Với site rỗng → chứa `"default"`.

---

## Task 5: Build, verify, release lock, push

1. `dotnet build .\AutoJMS.slnx -c Release` → 0 Warning, 0 Error.
2. `dotnet test .\AutoJMS.slnx -c Release --no-build` → toàn bộ pass.
3. `powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1` → 5 gate PASS.
4. Trả `.agent-lock.md` về `None` / `READ_ONLY` / `None`.
5. Commit + push `origin/main`.
6. Final Report 10 mục.
