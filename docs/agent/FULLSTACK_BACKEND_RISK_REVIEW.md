# Rà soát rủi ro: FullStackOperation + DataHub backend

> **KHÔNG COMMIT FILE NÀY LÊN REPO CÔNG KHAI KHI CHƯA CÓ Ý KIẾN CHỦ SỞ HỮU.**
> Tài liệu liệt kê đường dẫn chính xác của các lỗ hổng. Repo
> `Datt03-sss/AutoJMS` là public. Toàn bộ mục P0 đã được vá tại commit
> `27468d2` (xem mục G), nên rào cản còn lại chỉ là các mục chưa vá ở P1/P2 —
> quyết định publish vẫn thuộc chủ sở hữu.

- **Đối tượng**: `FullStackOperation` (cửa sổ ULTRA độc lập, biến `_fullStackForm`, mở bằng lệnh `DASH`) + `AutoJMS.DataHub.Api` + `backend/render-license-server`.
- **Mã nguồn tại thời điểm rà soát**: commit `8092871`, nhánh `main`.
- **Phương pháp**: đối chiếu từng cáo buộc trong 3 báo cáo audit bên thứ ba với mã nguồn thực tế. Mỗi phát hiện dưới đây đều có `file:line` làm bằng chứng. Cáo buộc không kiểm chứng được, hoặc đã lỗi thời, bị đưa vào mục D thay vì lặp lại.
- **Mục A–F là báo cáo + kế hoạch, viết khi chưa sửa mã.** Trạng thái thi hành kế hoạch nằm ở **mục G** (cập nhật 2026-08-23, commit `27468d2`); khi hai phần nói khác nhau thì mục G là hiện trạng.

---

## A. Tóm tắt điều hành

Ba nhóm rủi ro độc lập, mức độ khác nhau:

1. **Bề mặt WebView2 của FullStack mở rộng hơn mọi WebView khác trong ứng dụng.** Không kiểm tra nguồn message, mapping `Allow`, không có CSP, và — điều không audit nào phát hiện — `support.js` là một *dev-time component runtime* có `new Function` và một loader Babel từ CDN, chạy trong đúng ngữ cảnh sở hữu bridge đặc quyền.

2. **Tích hợp DataHub hiện đang *bất động*, và hợp đồng client/server đã vỡ ở 4 điểm.** Bất động vì license server không bao giờ phát `deviceToken`. Điều này *che* các lỗi hợp đồng — chúng sẽ phát nổ đúng lúc ai đó cấp token. Tác hại hôm nay: UI nói "đã bật cloud sync" trong khi không có gì được đồng bộ.

3. **Cổng license ULTRA đã bị comment-out ở cả hai đường vào.** Máy BASE mở được form ULTRA, và form còn được tạo trước ở `OnShown`.

Mức độ ưu tiên: **P0-1 (cổng license)** là mất doanh thu ngay lập tức; **P0-4 (dev runtime)** là mục mà không audit nào thấy và là vector thực tế nhất; **P0-5/P0-6** là bom hẹn giờ, không phải cháy nhà.

---

## B. Bảng tổng hợp

| # | Phát hiện | Mức | Vị trí | Trạng thái |
|---|---|---|---|---|
| P0-1 | Cổng tier ULTRA bị comment-out ở 2 đường vào | P0 | `Main.cs:1614-1647` | ✅ xác nhận |
| P0-2 | Hồ sơ WebView2 nằm trong `current\` (Velopack xoá mỗi update) | P0 | `FullStackOperation.Dashboard.cs:404-410` | ✅ xác nhận |
| P0-3 | Bridge postMessage không kiểm tra `e.Source`/origin, 19 hành động | P0 | `FullStackOperation.Dashboard.cs:437-583` | ✅ xác nhận |
| P0-4 | `support.js` là dev runtime: `new Function` ×2 + Babel từ unpkg.com, không CSP | P0 | `Web/support.js:686,1026,989,154,1266` | ✅ **mới phát hiện** |
| P0-5 | Hợp đồng DataHub vỡ 4 điểm (leaderTerm, body, endpoint, siteId) | P0 (tiềm ẩn) | `DataHubClient.cs` | ✅ xác nhận |
| P0-6 | Lease fail-**open**: lỗi ⇒ `return true` | P0 (tiềm ẩn) | `DataHubSyncService.cs:179,192` | ✅ xác nhận |
| P1-1 | License server không phát `deviceToken` ⇒ tích hợp bất động | P1 | `server.js:594-598` | ✅ xác nhận |
| P1-2 | `DeviceIdentity.Role` không được authorization đọc | P1 | `AuthContracts.cs:16-21,86-101` | ✅ xác nhận |
| P1-3 | `X-Forwarded-For` được tin tuyệt đối ⇒ vượt rate-limit per-IP | P1 | `DataHub.Api/Program.cs:44-52` | ✅ xác nhận |
| P1-4 | `Idempotency-Key` sinh mới mỗi lần retry | P1 | `DataHubClient.cs` | ✅ xác nhận |
| P1-5 | `MachineId` đổi mỗi tiến trình | P1 | `DataHubClient.cs` | ✅ xác nhận |
| P1-6 | `_started=true` trước init; `Dispose` sync-over-async; `ct` bị bỏ | P1 | `DataHubSyncService.cs:98,160,215-219` | ✅ xác nhận |
| P1-7 | Realtime là stub trả rỗng (không có doorbell/delta) | P1 | `DataHubClient.cs` | ✅ xác nhận |
| P2-1 | Lease TTL client 1800s vs server 120s | P2 | `DataHubSyncService.cs:43` | ✅ xác nhận |
| P2-2 | SQLite local không mã hoá | P2 | `FullStack/LocalDb/*` | ✅ xác nhận |
| P2-3 | 10 × `async void`; `StopAsync` fire-and-forget khi đóng form | P2 | `FullStackOperation.cs:268` | ✅ xác nhận |
| P2-4 | Zalo WebView: UA giả mạo Chrome, auto-reminder 5 phút | P2 | `FullStackOperation.cs:1197-1206` | ✅ xác nhận |
| P2-5 | `/health/ready` công khai trả `channel` | P2 | `DataHub.Api/Program.cs:133` | ✅ xác nhận |
| P2-6 | Enroll production fail-closed 503 (đúng thiết kế, nhưng là blocker) | P2 | `IdentityServiceCollectionExtensions.cs:14-26` | ✅ xác nhận |
| P2-7 | `index.html.20260624.bak` được ship kèm bản build | P2 | `Web/index.html.20260624.bak` | ✅ xác nhận |
| — | `service_account.json` bị lộ | — | — | ❌ **bác bỏ** |
| — | AuthToken JMS bị log nguyên văn | — | — | ❌ **bác bỏ** |
| — | Token lưu plaintext trong `AutoJMS.json` | — | — | ❌ **bác bỏ** |
| — | "Cần tắt host objects" | — | — | ❌ **không tồn tại** |
| — | DOM-XSS qua `dangerouslySetInnerHTML` | — | — | ❌ **bác bỏ** (chỉ SVG tĩnh) |

---

## C. Chi tiết các phát hiện đã xác nhận

### P0-1 — Cổng license ULTRA bị vô hiệu hoá ở cả hai đường vào

`src/AutoJMS/Forms/Main.cs:1614-1647`. Khối kiểm tra tier bị comment-out trong **cả** `PreCreateFullStackForm()` **và** `ShowFullStackForm()`:

```csharp
// Bypassed tier check temporarily for owner to test from tabHome
/*
if (_tierPolicy == null || !_tierPolicy.EnableFullStackOperation)
{
    AppLogger.Info($"FullStackOperation disabled for {_tierPolicy?.Tier ?? "BASE"} — not pre-created.");
    return;
}
*/
```

Hệ quả: máy BASE gõ `DASH` là vào được toàn bộ tính năng ULTRA; form còn được **tạo trước** ở `OnShown` nên tốn RAM + khởi tạo WebView2 trên mọi máy. Dấu vết của phiên test còn lại: `_fullStackForm.BackColor = Color.LightBlue` (`Main.cs:1664`).

> `Main.cs` nằm trong **Protected Files** — cần chủ sở hữu yêu cầu rõ ràng cho đúng việc này trước khi sửa.

### P0-2 — Hồ sơ WebView2 của FullStack nằm trong thư mục Velopack xoá

`FullStackOperation.Dashboard.cs:404-410`:

```csharp
var userDataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppData", "BrowserData");
```

`AppPaths.cs:32-49` nói ngược lại bằng chính lời của codebase:

```csharp
/// Why AppData lives next to (not inside) `current\`:
///   Velopack DELETES `current\` on every update.
public static string BrowserDataDir => Path.Combine(UserDataDir, "BrowserData");
```

`AppDomain.CurrentDomain.BaseDirectory` **chính là** `current\`. `Main.cs:231` và `UI/WebViewHost.cs:14` đều dùng `AppPaths.BrowserDataDir` đúng chuẩn — FullStack là ngoại lệ duy nhất. Hệ quả: mỗi lần cập nhật, cookie/session/localStorage của dashboard bị xoá sạch.

Cùng chỗ đó, mapping dùng `CoreWebView2HostResourceAccessKind.Allow`. Giá trị đúng là `Deny`: `Deny` chỉ chặn truy cập **cross-origin** vào mapping; tài liệu tại `https://autojms.local` vẫn tải được subresource của chính nó.

### P0-3 — Bridge postMessage không xác thực nguồn

`FullStackOperation.Dashboard.cs:437-583`, `OnWebViewMessageReceived`: không kiểm tra `e.Source`, không allowlist origin, không allowlist action. 19 hành động đặc quyền được dispatch: `READY, SYNC, EXPORT, CHANGE_SOURCE, CHANGE_SEARCH, CHANGE_TIME_INTERVAL, CHANGE_STATUS_SELECT, FETCH_JOURNEY, SELECT_WAYBILL, FETCH_RECEIVER_NETWORK, FETCH_NETWORK_INFO, FETCH_NETWORK_SEARCH, SUBMIT_ISSUE, TOGGLE_STAR, SEARCH, CHANGE_DATE_RANGE, REGISTER_ISSUE, SAVE_NOTE, REFRESH_JOURNEY`.

Không có `NavigationStarting` handler ⇒ không có gì chặn WebView điều hướng khỏi `autojms.local`. DevTools và context menu không bị tắt.

### P0-4 — `support.js` là runtime dev, chạy trong bản production (phát hiện mới)

Không audit nào nêu mục này. `src/AutoJMS/Web/support.js` là một *Design Component runtime* đang **hoạt động**, không phải thư viện chết — `index.html:11` mở bằng `<x-dc>` và `index.html:591` chứa `<script type="text/x-dc">`.

| Hành vi | Vị trí |
|---|---|
| `new Function` biên dịch logic UI từ text | `support.js:686-693` (`evalDcLogic`) |
| `new Function` thực thi module JS đã fetch | `support.js:1026` |
| Nạp Babel từ CDN ngoài | `support.js:989` — `https://unpkg.com/@babel/standalone@7.26.4/babel.min.js` |
| Re-fetch `location.href` khi load rồi recompile template | `support.js:154-158` |
| Component sibling: fetch `./<Name>.dc.html` rồi `new Function` phần `<script>` | `support.js:1266` (`COMPONENT_DIR = "."`), `1286-1334` |

Hệ quả cộng dồn với P0-2 và P0-3:

- Không có CSP nào trong `index.html`, và đường dẫn nạp script từ unpkg.com tồn tại trong mã ⇒ **không đúng khi nói "không có vector script từ xa"**. Hiện tại nhánh Babel chưa bị kích hoạt (`index.html` không dùng `x-import` kiểu jsx), nhưng nó nằm cách một component mới đúng một dòng.
- Bất kỳ file `*.dc.html` nào bị đặt vào `current\Web\` sẽ được fetch và **thực thi** — trong ngữ cảnh sở hữu bridge đặc quyền ở P0-3.
- Ứng dụng production không nên thực thi logic UI qua `new Function` trên text đọc lại từ đĩa lúc chạy.

`index.html.20260624.bak` (63 KB) cũng được ship — một bản dashboard cũ nằm ngay trong thư mục mà runtime này fetch tương đối.

### P0-5 — Hợp đồng client/server DataHub vỡ ở 4 điểm

Tất cả trong `src/AutoJMS/Data/DataHubClient.cs`:

1. **`leaderTerm` bị vứt bỏ.** `SendLeaseAsync` chỉ `return response.IsSuccessStatusCode;` — không đọc body. Server `LeaseRepository.cs` trả về term mới khi acquire; client không giữ ⇒ không bao giờ gửi được `X-Leader-Term`. Đây là **gốc rễ** của toàn bộ chuỗi lỗi fencing, chứ không phải hệ quả.
2. **Body sai ⇒ renew/release luôn 400.** Client gửi `{ leaseSeconds }`. Server `LeaseEndpoints.cs` khai báo `record LeaseTermRequest(long LeaderTerm)`, và `DataHub.Api/Program.cs:23` bật `JsonUnmappedMemberHandling.Disallow` ⇒ trường lạ là 400 cứng. `AcquireAsync` **không** có tham số body nên acquire vẫn qua — che mất lỗi cho tới lần renew đầu tiên.
3. **Sai endpoint.** Client luôn POST `/jms/observations`; endpoint có fence là `/jms/ingest` (`IngestEndpoints.cs:16-21`, `53-60` ⇒ 409 `LEADER_FENCED` nếu thiếu header). `observations` cố tình *không* fence, dành cho ghi tương tác.
4. **`siteId` không giải được.** `TryGetSiteId` fallback `Guid.TryParse(siteCode, ...)` với `siteCode = MiddleCode` (ví dụ `214A02`) — không bao giờ parse thành Guid.

Ghi chú: `IngestBigDataWaybillsAsync` trả 0 là **cố ý**, có comment giải thích (bulk phải đến từ Windows Service với `scanTime` gốc). Đừng "sửa" mục này.

### P0-6 — Lease fail-open

`FullStack/Services/DataHubSyncService.cs:179` và `:192`:

```csharp
if (!IsEnabled) return true; // cloud off => behave like before (always pull JMS)
...
catch (Exception ex)
{
    AppLogger.Warning("[HybridSync] lease acquire failed, fallback to JMS pull: " + ex.Message);
    _hasLease = false;
    return true;
}
```

Một sự cố mạng ⇒ **mọi** máy trong site đồng thời tin mình là leader và cùng pull JMS. Đúng ngược với mục đích của lease.

### P1 — Danh sách rút gọn

- **P1-1** `backend/render-license-server/server.js:594-598` trả `datahub: { apiBaseUrl, siteId, manifests }` — **không có `deviceToken`**. `LicenseApiService.cs:242-243` đọc `datahub.deviceToken` (luôn null) ⇒ `Program.cs:351` `DataHubClient.Configure(url, null, siteId)` ⇒ `IsEnabled` false. Toàn bộ tích hợp chỉ sống nếu operator tự set biến môi trường `AUTOJMS_DATAHUB_DEVICE_TOKEN`.
- **P1-2** `AuthContracts.cs:16-21` có `DeviceIdentity.Role`; `TenantAuthorizationEvaluator.Evaluate` (`:86-101`) chỉ xét Channel + SiteId ⇒ mọi device enroll xong có full read/write toàn site.
- **P1-3** `DataHub.Api/Program.cs:44-52`: `KnownIPNetworks.Clear(); KnownProxies.Clear();` ⇒ tin `X-Forwarded-For` của mọi client ⇒ rate-limit 600/phút/IP (`IngressRateLimitMiddleware.cs:11`) bị vượt bằng cách đổi header.
- **P1-4** `Idempotency-Key = Guid.NewGuid()` sinh mới mỗi lần gửi ⇒ retry tạo observation trùng.
- **P1-5** `MachineId = MachineName + "_" + Guid.NewGuid()` ⇒ mỗi lần khởi động là một device mới với server.
- **P1-6** `DataHubSyncService.cs:98` set `_started = true` **trước** khi init xong; `:160` `Dispose` dùng `.GetAwaiter().GetResult()` (nguy cơ deadlock trên UI thread); `:215-219` truyền `CancellationToken.None` ⇒ hủy không có tác dụng.
- **P1-7** `PullWaybillDeltaAsync` và `SubscribeSiteChangesAsync` trả rỗng/false ⇒ chưa có SignalR doorbell, chưa có `/changes?after=`, chưa có `RESYNC_REQUIRED`. Kiến trúc mô tả trong `backend/datahub/README.md` chưa được nối phía client.

### P2 — Danh sách rút gọn

`DataHubSyncService.cs:43` `DefaultLeaseSeconds = 1800` vs server cố định 120 (`LeaseRepository.cs:14,30`) · SQLite `journey_history.db`/`details.db` không mã hoá (vị trí thì đúng: `AppPaths.UserDataDir/FullStack/`) · 10 × `async void` (7 trong `FullStackOperation.cs`, 3 trong `WaybillWorkspace.cs`) và `FullStackOperation.cs:268` `_ = DataHubSyncService.Instance.StopAsync();` fire-and-forget · Zalo WebView (`:1197-1206`) giả UA Chrome + auto-reminder 5 phút · `/health/ready` công khai trả `channel` · enroll production fail-closed 503 (đúng thiết kế, chờ RS256/JWKS) · `index.html.20260624.bak` ship kèm.

Điểm cần ghi nhận là làm **đúng**: `FormClosing` (`FullStackOperation.cs:250-283`) cẩn thận hơn các audit khẳng định — set `_isClosing`, stop+dispose 4 timer, `CancelCurrentJourneyLoad()`, `_cts.Cancel()`, unsubscribe event, `StopAutoReminder()`.

---

## D. Các cáo buộc bị bác bỏ

Bốn mục dưới đây do audit bên thứ ba nêu và **không đúng với mã hiện tại**. Hành động theo chúng sẽ tốn công vô ích, hoặc tệ hơn: viết lại đoạn code đã được vá.

| Cáo buộc | Thực tế |
|---|---|
| `service_account.json` bị lộ | `git ls-files` sạch với `service_account`, `.pfx`, `.pem`, `.env`; không có trên đĩa; đã ignore ở `.gitignore:33` và `:87`. |
| AuthToken JMS bị log nguyên văn | Đã mask. `Main.cs:1774`: `authToken={TokenRedactor.MaskToken(token)}`. 49 điểm gọi mask. Regex tìm token nội suy trực tiếp trong `AppLogger.*` trả về **rỗng**. |
| Token lưu plaintext trong `AutoJMS.json` | Đã vá, `SettingsManager.cs:214-218`, kèm comment `// Do NOT re-add: Set(root, "lastAuthToken", ...)` và migration một chiều xoá giá trị cũ. |
| "Cần tắt host objects trong WebView2" | Không có `AddHostObjectToScript` **ở đâu** trong codebase. Bridge thuần postMessage. Không có gì để tắt. |
| DOM-XSS qua `dangerouslySetInnerHTML` | Bác bỏ. `index.html:1354` `{__html: x[2]}` với `x[2]` là hằng `I.box`/`I.alert`… — chuỗi SVG hard-code tại `index.html:1331-1345`. `index.html:1517` `{__html: m.icon}` với `m.icon` lấy từ mảng menu literal. Dữ liệu động (`label`, `total`) đi qua React children ⇒ tự escape. Ba `.innerHTML` trong `support.js` là đọc (`:32`, `:431`) hoặc compile template nội bộ (`:421`). **Nhưng** xem P0-4: chỗ đáng lo trong `support.js` là `new Function`, không phải `innerHTML`. |

Hiệu chỉnh mô hình tấn công: `src/AutoJMS/Web/` ship cục bộ (`index.html`, `react*.min.js`, `support.js`), URL ngoài duy nhất trong `index.html` là `https://pro-jmsvn-file.jtexpress.vn/`. Vector thực tế **không** phải XSS từ dữ liệu JMS, mà là **giả mạo file cục bộ trong `current\Web\`** — mà P0-4 biến từ "cần ghi được file EXE" thành "chỉ cần ghi được một file `.dc.html`".

---

## E. Kế hoạch triển khai

Sáu giai đoạn. Giai đoạn 1 và 2 độc lập nhau, chạy song song được. Không giai đoạn nào bắt đầu trước khi chủ sở hữu chốt phạm vi.

### Giai đoạn 0 — Quyết định của chủ sở hữu (chặn phần còn lại)

| Việc | Cần gì |
|---|---|
| P0-1 bật lại cổng tier | `Main.cs` là **Protected File** — cần yêu cầu rõ ràng cho đúng việc này. Kèm quyết định: có giữ đường tắt test cho máy owner không (đề xuất: cờ debug-only, không phải comment-out). |
| P1-1 phát `deviceToken` | Sửa `server.js` **và** `LicenseApiService.cs` (Protected File). Cần chốt: license server tự đúc device token, hay chỉ phát license assertion để desktop tự enroll với DataHub? Đề xuất: **assertion + enroll**, vì đó là thiết kế mà `IdentityServiceCollectionExtensions.cs` đã dựng sẵn. |
| Có publish tài liệu này lên repo public không | Repo là public. |

### Giai đoạn 1 — Đóng băng bề mặt WebView2 (không cần Protected File)

Toàn bộ trong `FullStackOperation.Dashboard.cs` + `src/AutoJMS/Web/`.

1. `userDataFolder` → `AppPaths.BrowserDataDir`. Kèm migration một lần: nếu thư mục cũ trong `current\AppData\BrowserData` tồn tại thì copy sang rồi bỏ qua (không xoá).
2. Mapping `Allow` → `Deny`.
3. Thêm `NavigationStarting`: hủy mọi điều hướng có host ≠ `autojms.local`.
4. `OnWebViewMessageReceived`: kiểm tra `e.Source` bắt đầu bằng `https://autojms.local/`, và allowlist đúng 19 action đã liệt kê; action lạ ⇒ log + bỏ qua.
5. Tắt DevTools + context menu ở bản Release (`AreDevToolsEnabled = false`, `AreDefaultContextMenusEnabled = false`).
6. Thêm CSP `<meta>` vào `index.html`: `default-src 'self'; script-src 'self' 'unsafe-inline'; connect-src 'self'` — `'unsafe-inline'` là bắt buộc cho tới khi mục 7 xong.
7. **Gỡ nhánh dev khỏi `support.js`**: bỏ `ensureBabel`/`BABEL_URL`, bỏ re-fetch `location.href`, bỏ fetch component sibling. Nếu `<x-dc>` là cách dashboard được build thì tiền biên dịch thành JS tĩnh lúc build thay vì `new Function` lúc chạy. Đây là mục nặng nhất của giai đoạn này và cũng là mục có giá trị nhất.
8. Xoá `index.html.20260624.bak` khỏi output build (giữ trong git nếu muốn, nhưng đừng ship).

**Tiêu chí hoàn thành**: dashboard mở bình thường sau `dotnet build -c Release`; session Zalo còn sau một lần update Velopack giả lập; DevTools không mở được; không còn `new Function` trong `Web/`; không còn request ra unpkg.com trong log network.

### Giai đoạn 2 — Sửa hợp đồng DataHub

Toàn bộ trong `DataHubClient.cs` + `DataHubSyncService.cs` (không phải Protected File).

1. `SendLeaseAsync` đọc `leaderTerm` từ response và lưu vào state; renew/release gửi `{ leaderTerm }` đúng schema.
2. Bulk đi `/jms/ingest` kèm header `X-Leader-Term`; giữ `/jms/observations` cho ghi tương tác.
3. `DefaultLeaseSeconds` bỏ hẳn — TTL do server quyết (120s), client chỉ cần chu kỳ renew 30s.
4. `Idempotency-Key` = hash ổn định của nội dung batch, không phải Guid mới.
5. `MachineId` bền: lưu Guid vào `AppPaths.UserDataDir` lần đầu rồi tái dùng.
6. `TryGetSiteId`: bỏ fallback parse `MiddleCode`; nếu chưa có siteId thật thì trả false + log rõ ràng thay vì gửi request chắc chắn 400.
7. Lease **fail-closed**: `catch` ⇒ `return false`. Riêng nhánh `!IsEnabled ⇒ return true` giữ nguyên (cloud tắt thì hành vi cũ là đúng).
8. `_started = true` chuyển xuống sau init; `RunCycleSafeAsync` truyền `ct` thật; `Dispose` không sync-over-async.
9. Trong khi `IsEnabled == false`, UI **không được** hiển thị trạng thái ngụ ý cloud sync đang chạy.

**Tiêu chí hoàn thành**: một máy acquire được lease và renew liên tục >5 phút không 400; máy thứ hai bị từ chối; bulk không header bị 409 `LEADER_FENCED`; ngắt mạng ⇒ máy mất lease *ngừng* pull.

### Giai đoạn 3 — Nối license → enroll (sau khi Giai đoạn 0 chốt)

License server phát license assertion (RS256); desktop enroll với DataHub để lấy device token; thay `UnavailableLicenseAssertionValidator` bằng validator RS256/JWKS thật. Đây là điều kiện để production readiness chuyển từ đỏ sang xanh — `backend/datahub/README.md:32-35` đã ghi rõ đây là chốt cố ý.

### Giai đoạn 4 — Realtime

Hiện thực `SubscribeSiteChangesAsync` (SignalR doorbell trên `/hubs/site`) + `PullWaybillDeltaAsync` (`/changes?after=`) + xử lý `RESYNC_REQUIRED` bằng snapshot một transaction. Chỉ làm sau Giai đoạn 2, vì nó dựa trên cùng state lease/term.

### Giai đoạn 5 — Cứng hoá backend

`ForwardedHeaders`: khai báo đúng subnet Docker của Caddy thay vì `Clear()` · authorization đọc `DeviceIdentity.Role` · fail2ban/rate-limit ở tầng Caddy · cứng hoá VPS (ufw thu hẹp từ `1000:2000/tcp`, `3389`, `3306`, `8080`; gỡ xrdp; `PermitRootLogin prohibit-password`) · bản sao thứ hai của `.env.staging` lưu ngoài VPS · dọn site/device rác từ các lần smoke test.

### Giai đoạn 6 — Hoàn thiện

SQLCipher hoặc DPAPI cho 2 file SQLite · chuyển `async void` sang `async Task` + handler an toàn · unit test cho `DataHubClient` state machine (đúng phạm vi TDD: logic thuần) · cập nhật `docs/agent/CODEBASE_MAP.md` và `backend/datahub/README.md` cho khớp thực tế sau Giai đoạn 2.

---

## F. Rủi ro của chính kế hoạch này

- **Giai đoạn 1 mục 7** có thể phá layout dashboard nếu `<x-dc>` đang được dùng làm cơ chế build thật sự. Cần khảo sát trước khi cam kết: dashboard hiện được sinh ra như thế nào, và ai đang sửa nó.
- **Giai đoạn 2** thay đổi hành vi mạng nhưng **không thể kiểm thử end-to-end** cho tới khi có device token (Giai đoạn 0/3). Rủi ro: sửa xong mà vẫn không biết đúng. Giảm thiểu: dùng staging assertion issuer trên VPS để cấp token thử.
- **Fail-closed ở P0-6** sẽ làm dữ liệu *ngừng* cập nhật trong sự cố mạng, thay vì mọi máy cùng pull. Đó là hành vi đúng, nhưng là một thay đổi vận hành cần thông báo trước.
- Bật lại cổng tier sẽ khiến các máy đang dùng FullStack nhờ lỗ hổng **mất tính năng ngay**. Cần biết trước có bao nhiêu máy như vậy.

---

## G. Trạng thái thi hành (2026-08-23, commit `27468d2`)

Kiểm chứng: `dotnet build -c Release` 0 warning/0 error · `dotnet test` 136 + 60 pass · `eng/harness/verify.ps1` ALL GATES PASSED.

| # | Trạng thái | Ghi chú |
|---|---|---|
| P0-1 | ✅ đã vá (siết thêm 2026-08-24) | `IsFullStackOperationAllowed()`. Vòng 1 (`27468d2`): Release cưỡng chế tier, Debug giữ đường thoát `#if DEBUG`. Vòng 2 (xem **mục H**): bỏ hẳn `#if DEBUG` — Debug enforce y hệt Release — và vá thêm 3 điểm cắt khác vì chỉ chặn ở `Main` là chưa đủ. |
| P0-2 | ✅ đã vá | Dùng `AppPaths.BrowserDataDir`. **Lệch kế hoạch có chủ ý**: không copy-migrate từ `current\AppData\BrowserData` — trên bản đóng gói thư mục đó bị xoá mỗi update, và Main đã mở profile chung nên copy vào profile đang sống có thể làm hỏng nó. |
| P0-3 | ✅ đã vá | Kiểm tra `e.Source` + allowlist 19 action + `NavigationStarting` chặn ra ngoài `autojms.local` + `AreDevToolsEnabled=false` ở Release. Mapping `Allow` → `Deny`. |
| P0-4 | ✅ đã vá (còn 1 mục treo) | Bỏ loader `x-import`, bỏ Babel/unpkg.com, bỏ re-fetch `location.href`, không publish cầu nối editor lên `window`. **Còn `'unsafe-eval'` trong CSP** vì dc-runtime biên dịch khối `data-dc-script` nội tuyến của chính trang bằng `new Function`; chỉ bỏ được khi dashboard có bước build (mục 7 của Giai đoạn 1 làm 90%, phần còn lại là bước build). |
| P0-5 | ✅ đã vá | `leaderTerm` từ response, `/jms/ingest` + `X-Leader-Term`, `Idempotency-Key` dẫn xuất từ nội dung, `MachineId` bền, `TryGetSiteId` bỏ fallback `MiddleCode`. |
| P0-6 | ✅ đã vá (điều chỉnh so với kế hoạch) | Thay bool bằng 3 trạng thái `Granted/Held/Unreachable`. `Held` ⇒ fail-closed đúng như kế hoạch. `Unreachable` ⇒ vẫn pull local nhưng `_hasLease=false`: không push có fencing, không hiện badge "máy chủ lease". Kế hoạch ghi "catch ⇒ return false"; như vậy sẽ làm mất cả pull local khi chỉ mất mạng, nên phân tách 3 trạng thái. |
| P1-1 | ❌ chưa | Cần `LicenseApiService.cs` (**Protected File**) + `server.js` đúc assertion. Chờ quyết định Giai đoạn 0. |
| P1-2 | ❌ chưa | `DeviceIdentity.Role` vẫn chưa được authorization đọc. Đã thêm allowlist role ở `/enroll` (defense-in-depth), nhưng đó là việc khác. |
| P1-3 | ✅ đã vá | `DATAHUB_TRUSTED_PROXY_NETWORKS` (mặc định loopback + RFC 1918); override không parse được thì degrade về mặc định, không bao giờ "tin tất cả". |
| P1-4 | ✅ đã vá | |
| P1-5 | ✅ đã vá | |
| P1-6 | ✅ đã vá | Thêm `_lifetimeCts`, `_started` set sau init, `Dispose` chờ tối đa 3s. |
| P1-7 | ⛔ chặn | Cần `Microsoft.AspNetCore.SignalR.Client` trong app desktop self-contained ⇒ đổi payload bản phát hành ⇒ **cần chủ sở hữu chấp thuận**. Polling hiện tại vẫn hoạt động. |
| P2-1 | ✅ đã vá | Bỏ `DefaultLeaseSeconds`; TTL do server quyết. |
| P2-2 | ⛔ chặn | SQLCipher/DPAPI cần đổi dependency + migrate dữ liệu ⇒ cần chấp thuận. |
| P2-3 | ⚠️ vá một phần | Đã bịt 3 đường crash thật (FormClosing, `RunSyncAsync` finally chạm control sau khi đóng, 2 handler WaybillWorkspace). Các `async void` còn lại giữ nguyên theo Minimal Edit Rule vì chưa chứng minh được đường crash. |
| P2-4 | ❌ chưa | UA giả mạo + auto-reminder 5 phút của WebView Zalo là quyết định sản phẩm, không sửa mà không có yêu cầu. |
| P2-5 | ✅ đã vá | `/health/ready` không còn trả `channel`. |
| P2-6 | ✅ mở đường (phía server) | `RsaLicenseAssertionValidator` xác thực `v1rs256` bằng public key RSA ≥2048 bit, bật bằng `DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY(_PATH)`; không có key thì vẫn fail-closed; cấu hình nhầm PRIVATE key cũng bị từ chối. Claim check gom vào `LicenseAssertionClaims` dùng chung với HMAC để hai validator không lệch nhau. Còn thiếu phía client (P1-1). |
| P2-7 | ✅ đã vá | `csproj` không copy `Web\**\*.bak/.orig/.tmp/~` vào output (kiểm chứng: output không còn file `.bak`). File `.bak` vẫn nằm trên đĩa nhưng không được git theo dõi và không ship. |

Giai đoạn 5 phần **hạ tầng** — cập nhật 2026-08-23:

| Việc | Trạng thái |
|---|---|
| Thu hẹp ufw (`3389`, `3306/tcp`, `8080`, `53`, `1000:2000/tcp`) | ✅ **đã làm** — chỉ còn `22/tcp`, `80/tcp`, `443/tcp` (cả v4 + v6); default `deny incoming` / `allow outgoing` / `deny routed`; đã ghi vào `/etc/ufw/user{,6}.rules` nên bền qua reboot. Chạy với lưới an toàn `systemd-run --on-active=15min` hoàn tác tự động (đã huỷ sau khi xác minh SSH). Bản sao rules cũ: `/root/ufw-backup-pre-narrow/`. |
| Gỡ xrdp | ✅ **đã làm** — purge `xrdp`, `xorgxrdp`, `pipewire-module-xrdp`, `libpipewire-0.3-modules-xrdp`; unit/`/etc/xrdp`/user `xrdp` đều sạch; không còn gì listen trên 3389. |
| `PermitRootLogin prohibit-password` | ❌ chưa — chưa yêu cầu. |
| fail2ban / rate-limit ở Caddy | ❌ chưa. |
| Sao lưu `.env.staging` ngoài VPS | ❌ chưa. |
| Dọn site/device rác từ smoke test | ❌ chưa. |

> ⚠️ `DOCKER-USER` đang rỗng ⇒ cổng nào Docker publish ra host sẽ **bỏ qua** rule UFW. Hiện chỉ Caddy publish `80`/`443` (đúng ý muốn); `8080` của API và `5432` của Postgres chỉ tồn tại trong network compose, không bind ra host. Nếu sau này publish thêm cổng, phải chặn ở `DOCKER-USER`, không phải `ufw allow/deny`.

Giai đoạn 6 phần tài liệu (`docs/agent/CODEBASE_MAP.md`, `backend/datahub/README.md`) chưa cập nhật theo hành vi mới của Giai đoạn 2.

---

## H. Vòng 2 — Chuỗi leo quyền BASE → ULTRA (2026-08-24)

Sau khi P0-1 đã vá, **license BASE vẫn mở được FullStack** mà không cần sửa gì ở
Firebase. Nguyên nhân: `TierRuntimePolicy` suy ra tier **từ nội dung runtime policy**
thay vì từ license. Chuỗi hoàn chỉnh, 4 điểm cắt độc lập, cả 4 đều hở:

```
license BASE
  → VpsRuntimePolicyService.FetchPolicyAsync("BASE")
      ├─ fetch được file dùng chung / file khai tier=ULTRA   ← điểm cắt 3
      └─ hoặc fetch fail → đọc cache của phiên ULTRA cũ      ← điểm cắt 3
  → Program.cs: sessionTier = RuntimePolicy?.Tier            ← điểm cắt 2
  → new Main("ULTRA") → TierRuntimePolicy.Resolve(policy)
  → effectiveTier = "ULTRA", fullStack = true                ← điểm cắt 1
  → IsFullStackOperationAllowed() (#if DEBUG return true)    ← điểm cắt 4
  → FullStackOperation mở, không tự kiểm tra gì
```

### Nguyên tắc đã chốt với chủ sở hữu

> **License tier là thẩm quyền bất biến. Runtime policy chỉ được THU HẸP quyền
> (`true → false`), không bao giờ được nâng quyền (`false → true`). Policy/cache
> của tier khác thì BỊ TỪ CHỐI, rơi về `SafeDefault(BASE)`.**

Policy vẫn là kill switch dùng được (tắt tính năng của ULTRA từ xa), nhưng không còn
là công cụ phát license.

### Đã vá

| # | Điểm cắt | File | Thay đổi |
|---|---|---|---|
| 1 | Policy suy ra tier | `Licensing/TierRuntimePolicy.cs` | `Resolve(RuntimePolicyDocument, string licenseTier)` tính `entitlement = Resolve(licenseTier)` rồi **AND từng cờ** với entitlement; `Tier` lấy từ license, bỏ hẳn biến `effectiveTier`. Đổi tên tham số `fallbackTier` → `licenseTier` cho đúng nghĩa: nó là thẩm quyền, không phải giá trị dự phòng. |
| 2 | `sessionTier` bị policy ghi đè | `Program.cs:174, 259` | Bỏ 2 dòng `sessionTier = RuntimePolicy?.Tier ?? sessionTier;`. `sessionTier` giữ tier của license từ đầu đến `new Main(...)`. |
| 3 | Cache/document của tier khác | `Policies/VpsRuntimePolicyService.cs` | `LoadCachedPolicy` **trả `null`** khi cache lệch tier (trước đó chỉ log warning rồi dùng tiếp "một cách bảo thủ"). `TryParsePolicy` **trả `null`** khi document tự khai tier khác tier đang yêu cầu. |
| 3b | Không phân biệt "khuyết tier" với "tier=BASE" | `Policies/RuntimePolicyDocument.cs` | `Tier` mặc định `""` thay vì `"BASE"`. Rỗng = file dùng chung (được đóng dấu tier đang yêu cầu); `"BASE"` = file chỉ dành cho BASE (bị từ chối khi máy là ULTRA). Nếu giữ mặc định `"BASE"` thì mọi file dùng chung sẽ bị từ chối oan trên máy ULTRA. |
| 4 | Đường thoát Debug | `Forms/Main.cs:1619` | Bỏ hẳn `#if DEBUG return true;`. **Debug enforce y hệt Release.** Muốn test ULTRA thì dùng license ULTRA. |
| 5 | `FullStackOperation` không tự gác | `Forms/FullStackOperation.cs:111, 181` | Thêm `TierRuntimePolicy.Current` (mặc định fail-closed BASE) và 2 gate: constructor `throw UnauthorizedAccessException` (cả `PreCreateFullStackForm` và `ShowFullStackForm` đều bắt exception nên fail-closed an toàn), `StartRealtimeRuntimeAsync()` `return` sớm. Thành 3 lớp: gate UI + gate đối tượng + gate service. |

Kiểm chứng: `dotnet build -c Release` 0 warning/0 error · `dotnet test` 150 + 60 pass
(thêm 12 test mới trong `tests/AutoJMS.Tests/TierEntitlementTests.cs`) ·
`eng/harness/verify.ps1` ALL GATES PASSED.

### Thay đổi hành vi cần biết

Mặc định của cờ khuyết **đảo từ `false` sang `true`**, vì sau khi AND với entitlement
thì "cờ khuyết" phải nghĩa là *không có hạn chế*, không phải *bị cấm*. Hệ quả: máy
**ULTRA** tải được policy nhưng policy không khai cờ nào thì nay **giữ** FullStack
(trước đây bị tắt oan). Máy **BASE** thì mọi tổ hợp đều tắt.

Máy ULTRA mất mạng và **không có cache** vẫn rơi về `SafeDefault`, trong đó
`forms.fullStackOperation = false` tường minh ⇒ FullStack tắt. Đây là hành vi cũ,
không đổi, và là fail-closed có ý.

### Phát hiện thêm, chưa vá

| # | Vấn đề | Ghi chú |
|---|---|---|
| H-1 | `VALID_EXE_HASHES` rỗng ⇒ **tắt kiểm tra hash EXE cho mọi máy** | `backend/render-license-server/server.js:557-559`. Rộng hơn cờ `skipHashCheck` (dòng 547) vì không cần client gửi gì cả. Cần quyết định của chủ sở hữu: bắt buộc có hash, hay chấp nhận tắt. |
| H-2 | Đường fetch không theo tier | `VpsRuntimePolicyService.cs:68-69` vẫn thử `configs/runtime-policy.json` và `manifest/feature-policy.json`. Sau vòng 2 thì vô hại (file dùng chung được đóng dấu tier đang yêu cầu, và policy không nâng được quyền), nhưng file cache vẫn dùng **một đường dẫn duy nhất** cho mọi tier — đổi license là mất cache. Cache theo tier là việc dọn dẹp, không phải lỗ hổng. |

### Cáo buộc bị bác bỏ ở vòng 2

- **"BASE không được dùng Google Sheets"** — chủ sở hữu xác nhận **BASE được dùng**.
  `/api/google-sheets/grant` chỉ cần license active, không cần ULTRA. Không sửa.
- **"authToken 32-hex bị log nguyên vẹn"** — đã lỗi thời; code mask qua
  `TokenRedactor.MaskToken`/`LogToken`, log chỉ in `valid=32hex`.
- **"device token của DataHub nằm trong source"** — đã lỗi thời; token đến từ response
  của license server (`DataHubClient.Configure`), có fallback env
  `AUTOJMS_DATAHUB_DEVICE_TOKEN`. Không hardcode.

---

## I. Vòng 3 — "Báo cáo rủi ro FullStackForm + Backend" (2026-08-24)

Mục tiêu do chủ sở hữu nêu: **phân biệt rõ tier `BASE` và `ULTRA`**. Vòng này rà từng
cáo buộc của báo cáo về đúng source trên `main`, vá phần thực sự còn hở, và ghi lại
phần đã đúng từ trước để lần sau không rà lại.

### I.1 Điểm cắt thứ 5 — `tier-definitions.json` nâng được BASE (đã vá)

Vòng 2 đóng 4 điểm cắt trên đường *policy → tier*. Còn một đường thứ 5, độc lập, nằm
ở chính hàm sinh entitlement:

```
TierRuntimePolicy.Resolve("BASE")
  → TierDefinitions.LoadFromFile()               // AppPaths.InstallDir\tier-definitions.json
  → HasForm("BASE", "FULLSTACK_OPERATION")
  → isUltra = hasFullStack || normalized == "ULTRA"    // ❌ BASE nâng được
  → policy Tier="ULTRA", cả 6 cờ = true
```

`AppPaths.InstallDir` là `AppContext.BaseDirectory`, tức `{InstallRoot}\current\` — thư
mục người dùng tự chọn khi cài, nên **ghi được**. `tier-definitions.json` ship vào đó
(`AutoJMS.csproj:57`, `PreserveNewest`). Khách license BASE chỉ cần thêm
`FULLSTACK_OPERATION` vào tier `BASE` trong file đã cài là có policy ULTRA đầy đủ,
hoàn toàn offline. Bản vá vòng 2 **không** che được đường này, vì
`Resolve(policy, "BASE")` lấy entitlement từ đúng lời gọi trên.

Đã vá tại `src/AutoJMS/Licensing/TierRuntimePolicy.cs:90-99`:

```csharp
bool isUltra = normalized == "ULTRA"
               || (normalized != "BASE" && hasFullStack);
```

Chọn cách này thay vì bỏ hẳn `HasForm` vì license server **không có allowlist tier**
(`normalizeTier` ở `server.js:294` chỉ upper-case; chuỗi `ULTRA` không xuất hiện ở đâu
trong server.js) — nên tier mới đặt tên ở Firebase là hợp lệ, và đường mở rộng qua
`tier-definitions.json` phải còn dùng được cho chúng. Người dùng không sửa được chuỗi
tier do server ký, nên loại riêng `BASE` là đủ khoá đường leo quyền.

Thêm 4 test (`TierEntitlementTests` giờ 16 test): BASE với file đã bị sửa vẫn là BASE;
tier `PRO` được cấp form thì lên ULTRA; `PRO` không được cấp thì là BASE; `ULTRA` vẫn
là ULTRA khi không đọc được file.

### I.2 `backgroundJobs` trong `tier-definitions.json` là **config chết**

`TierConfig` (`TierDefinitions.cs:97-123`) chỉ có `Inherits`, `Tabs`, `Forms`,
`Modules` — **không có** `BackgroundJobs`, nên khối `backgroundJobs` trong JSON bị
`JsonSerializer` bỏ im lặng. Công tắc thật của background sync là
`forms[].name == "FULLSTACK_OPERATION"` → `TierRuntimePolicy` → `_tierPolicy.Enable*`.

**Quyết định: giữ nguyên, coi là mô tả, không nối vào code.** Nối `backgroundJobs` vào
`TierConfig` sẽ tạo *nguồn chân lý thứ hai* nằm trong một file người dùng ghi được —
đúng loại lỗ vừa bịt ở I.1 — mà không đổi hành vi nào (giá trị trong file trùng khớp
100% với kết quả suy ra từ `forms[]`: BASE toàn `false`, ULTRA toàn `true`).

### I.3 Phase 4 — heartbeat `kill` có thật sự dừng FullStack + background jobs

Có. `LicenseApiService.cs:645-648` map `action == "kill"` → `HeartbeatOutcome.ServerKill`;
`HeartbeatSupervisor` xử lý ở `:781-785`: cảnh báo → `Task.Delay(3000)` →
`System.Windows.Forms.Application.Exit()` → `return` (thoát hẳn vòng lặp).

Đã kiểm ba chỗ có thể chặn:

- `Main_FormClosing` (`Main.cs:997`) chỉ `e.Cancel = true` khi
  `e.CloseReason == CloseReason.UserClosing`. `Application.Exit()` sinh
  `ApplicationExitCall` ⇒ **hộp thoại xác nhận thoát không chặn được kill**; nhánh
  không-UserClosing chạy thẳng `_isExiting = true; _appCts.Cancel()`.
- `FullStackOperation_FormClosing` (`:273-311`) chạy đủ cleanup (4 timer Stop+Dispose,
  `_cts.Cancel()`, huỷ subscribe SignalR + `StopAsync()`, `StopAutoReminder()`).
- Ba `e.Cancel = true` còn lại không liên quan: 2 chỗ `DataGridView.DataError`
  (`Main.cs:975`, `FullStackOperation.cs:1732`) và 1 chốt chặn điều hướng WebView2
  (`FullStackOperation.Dashboard.cs:470`).

Tiến trình thoát ⇒ mọi background job chết theo. Không cần sửa.

### I.4 Cách ly staging / production

Chốt chặn ở tầng auth **đã đúng**: `StagingTestIssuerPolicy.IsEnabled` yêu cầu **đồng
thời** `DATAHUB_ALLOW_STAGING_TEST_ISSUER=true` **và** `ASPNETCORE_ENVIRONMENT=Staging`
(`src/AutoJMS.DataHub.Api/Configuration/StagingTestIssuerPolicy.cs:5-6`), và compose
mặc định `:-false` / `:-` (`docker-compose.yml:44-45`). Nên dù cờ có lọt vào
production, issuer test vẫn không bật được.

Phần còn hở là **triển khai**, không phải auth: `docker-compose.yml:1` cố định
`name: autojms-datahub` với volume có tên (`postgres_data`, `caddy_data`) và cổng host
`80:80`/`443:443`. Chạy staging và prod cạnh nhau trên **cùng một** host sẽ dùng chung
volume Postgres và tranh cổng. Hiện chỉ có một stack trên một VPS nên không phải lỗ
hổng đang mở — là ràng buộc cần biết trước khi dựng stack thứ hai (phải đổi `-p` và
`DATAHUB_PUBLIC_HOST`).

### I.5 Cáo buộc đã đúng, nhưng code đã đúng từ trước — không sửa

| Cáo buộc | Thực tế |
|---|---|
| Bypass gate tier để mở FullStack cho BASE | Đã đóng ở vòng 2 (4 điểm cắt) + I.1 (điểm thứ 5). `Main.cs` không còn `#if DEBUG`. |
| Có chỗ hardcode `if (CurrentTier == "ULTRA")` | Không có. Mọi cửa vào background đều qua `_tierPolicy`: `Main.cs:243` (`_autoSyncTimer.Start()`), `:707`, `:738` (sync lúc khởi động), `:767` (`HandleAutoSyncTickAsync`). |
| Rò rỉ theo lifecycle / thiếu `_isClosing`, `_uiReady` | Đã có đủ; xem I.3. |
| `license.modulePolicy` vs parse ở root | Server phát **cả hai** (`server.js:686` root, `:693` nested "backward compatibility"); client đọc root. Không lệch. |
| Hình dạng hash-manifest lệch DTO | Tương thích: field lạ (`displayVersion`) bị bỏ qua khi deserialize. |
| Log nguyên token JMS | Đã mask qua `TokenRedactor`. |
| `service_account.json` trong workspace | Không có trên đĩa, không có trong `git ls-files`. |
| `/devices/enroll` 503 vì thiếu validator bất đối xứng | `RsaLicenseAssertionValidator` đã có (P2-6); client nối xong ở `59de236`. |
| Còn lời gọi Supabase/RPC cũ | Đã xoá ở `5483e15`. |
| Còn warning khi build | 0 warning ở cả Release và Debug. |

### I.6 Còn mở sau vòng 3

| # | Vấn đề | Ghi chú |
|---|---|---|
| I-1 | `Main.cs:1672` — `_fullStackForm.BackColor = Color.LightBlue;` | Vết test còn sót, làm cửa sổ FullStack xanh nhạt trong bản production. Chỉ ảnh hưởng thị giác. `Main.cs` là Protected File nên chờ chủ sở hữu yêu cầu. |
| I-2 | DataHub trả 401 thì chỉ log + trả list rỗng | Không có nhánh riêng cho 401: không dừng realtime, không backoff, không báo trạng thái cho người dùng. Token hết hạn giữa phiên sẽ hiện ra như "không có dữ liệu". |
| I-3 | `TabManager.ApplyTier` đặt `TabPage.Visible = false` | Là no-op trong WinForms (muốn ẩn phải `TabPages.Remove`). Hiện vô hại vì BASE và ULTRA có **cùng** danh sách 5 tab; sẽ thành lỗi thật ngày nào hai tier khác tab. **→ Đã vá ở vòng 4, xem J.1.** |
| I-4 | `DataGridView` không dùng `VirtualMode` | Có giới hạn sẵn nên chưa nguy: snapshot clamp 1–5000, changes feed 500 dòng/trang với con trỏ `after` + `HasMore`. |
| I-5 | `_cts` trong `FullStackOperation` chỉ `Cancel()`, không `Dispose()` | Rò rỉ nhỏ, mỗi lần đóng form một `CancellationTokenSource`. |
| I-6 | DataHub chưa hỗ trợ multi-node | Đúng, và là chủ ý ở giai đoạn này (một VPS). Lease fencing + rate-limit hiện dựa trên tiến trình đơn. |

---

## J. Vòng 4 — Phân quyền BASE / ULTRA (2026-08-24)

Vòng này trả lời câu hỏi *"BASE và ULTRA khác nhau ở đâu, và ranh giới đó có
được thi hành thật không?"* — chứ không chỉ *"có leo quyền được không?"*.

### J.1 Cờ `AllowManualTracking` / `AllowManualPrint` được tính ra rồi bỏ đó

`TierRuntimePolicy` đọc `tabs.tracking` và `tabs.print` từ runtime policy, ghi
kết quả ra log, và có unit test — nhưng **không một dòng code production nào
đọc hai cờ đó**. Kill switch tab của runtime policy do đó hoàn toàn vô tác
dụng: admin tắt `tabs.print` trên DataHub thì client vẫn hiện tab PRINT.

Cùng chỗ đó là I-3: `TabManager.ApplyTier` ẩn tab bằng `TabPage.Visible = false`,
mà trong WinForms đây là **no-op** — `TabPage` chỉ biến mất khi bị gỡ khỏi
`TabControl.TabPages`. Hai lỗi này cộng lại nghĩa là **phân quyền tab chưa bao
giờ có hiệu lực**, kể cả phần theo `tier-definitions.json`.

Đã vá cùng lúc trong `src/AutoJMS/Forms/TabManager.cs`:

- `RegisterTab` ghi lại **thứ tự thiết kế** của từng tab (`_designOrder`).
- `ApplyTier` gỡ thật bằng `TabPages.Remove`, và chèn lại bằng
  `TabPages.Insert(InsertIndexFor(...))` để tab hiện lại đúng vị trí cũ —
  ABOUT vẫn là tab cuối cùng (Tab Boundary Rule).
- `IsTabAllowed` trả về **giao** của hai nguồn: danh sách `tabs` của tier trong
  `tier-definitions.json` **và** entitlement đang có hiệu lực. Cùng nguyên tắc
  như điểm cắt thứ 5 (I.1): file nằm trong thư mục cài đặt nên ghi được, chỉ
  được phép THU HẸP, không được mở một tab mà entitlement đã tắt.

Năm test mới ở `tests/AutoJMS.Tests/TabEntitlementTests.cs`. Vì
`TierRuntimePolicy.Current` là static toàn tiến trình, cả hai lớp test đụng tới
nó nằm chung collection `TierPolicy` với `DisableParallelization = true`.

**Hành vi hôm nay không đổi**: BASE và ULTRA vẫn dùng chung 5 tab và không có
cờ nào bị tắt, nên không tab nào biến mất. Cái thay đổi là kill switch giờ *hoạt
động* khi được dùng.

### J.2 Gate tầng service cho DataHub sync

`DataHubSyncService.IsEnabled` trước đây chỉ hỏi `CloudSyncEnabled`, site code và
credentials — không hỏi tier. Hôm nay không có lỗ thật vì mọi caller đều nằm
trong `FullStackOperation` (đã bị chặn ở tầng form), nhưng đó là phòng thủ một
lớp. Đã thêm `TierRuntimePolicy.Current.EnableFullStackOperation` vào đầu
`IsEnabled` (log một lần rồi thôi, tránh spam vì `IsEnabled` bị hỏi mỗi nhịp).
`TierRuntimePolicy.Current` mặc định fail-closed ở BASE nên nếu policy chưa
resolve xong thì sync không chạy.

### J.3 Phát hiện chính: phân quyền tier hiện **100 % phía client**

Đây là khoảng trống lớn nhất còn lại, và không vá được ở client.

| Nơi | Có trường tier không? |
|---|---|
| `issueDataHubAssertion` (`backend/render-license-server/server.js:179`) | **Không.** Payload chỉ có `Channel`, `SiteCodes`, `ExpiresAt`, `DataHubUrl`, `Seats`, `TokenVersion`, `Issuer`, `Audience`. |
| `LicenseAssertionPayload` (`src/AutoJMS.DataHub.Api/Auth/LicenseAssertionPayload.cs:10-20`) | **Không.** Cùng 8 trường, không có `Tier`. |
| `server.js` nói chung | Chuỗi `ULTRA` **không xuất hiện ở đâu**; `normalizeTier()` (`:294`) chỉ viết hoa. |
| DataHub device token (HMAC 24 h) | Không mang tier. |

Hệ quả: license BASE vẫn xin được assertion, vẫn `POST /api/v1/devices/enroll`
thành công, và sau đó dùng được **toàn bộ mặt phẳng dữ liệu DataHub** bằng
`curl` hoặc một client đã sửa. Mọi thứ đã vá ở vòng 2–4 chỉ chặn *giao diện*
AutoJMS mở FullStack, không chặn *server* phục vụ một BASE.

Nói cách khác: BASE ≠ ULTRA là **quy ước UI**, chưa phải ranh giới an ninh.

Ba lựa chọn, cần chủ sở hữu quyết vì đều đụng vào production:

1. **Thêm `Tier` vào assertion + DataHub từ chối BASE khi enroll.** Vá triệt để,
   nhưng là breaking change: khách BASE nào đang sync sẽ mất kết nối ngay sau
   khi deploy Render + DataHub.
2. **Thêm `Tier` vào assertion, DataHub chỉ ghi log (chưa chặn).** Additive, không
   gãy ai, và tạo sẵn dữ liệu để bật chặn sau khi biết chắc không khách BASE nào
   đang dùng.
3. **Giữ nguyên**, chấp nhận rằng ranh giới tier chỉ nằm ở client.

Khuyến nghị: (2) trước, (1) sau khi có số liệu.

### J.4 Cấu hình chết trong `tier-definitions.json`

Ngoài `backgroundJobs` đã ghi ở I.2, còn: `TierConfig.Modules`,
`TierConfig.BackgroundForms`, `TierDefinitions.GetForms()` — parse ra nhưng
không nơi nào đọc. Sau vòng 4, hai thứ duy nhất trong file này thực sự có tác
dụng là `HasForm(tier, "FULLSTACK_OPERATION")` và `GetTier().Tabs`. Giữ nguyên
là **có chủ ý**: file này ghi được bởi người dùng, nối thêm cấu hình từ nó vào
đường quyết định là tự tạo thêm điểm cắt.

### J.5 Chính tài liệu này đang nằm trong repo công khai

Đầu file có dòng tự dặn không commit lên repo công khai, nhưng
`Datt03-sss/AutoJMS` là **public** (`gh repo view` → `"visibility":"PUBLIC"`) và
file này đang được git theo dõi trên `main`. Nội dung gồm đường dẫn file, số
dòng và mô tả từng điểm yếu — không phải lỗ hổng tự nó, nhưng là bản đồ tấn
công sẵn cho người khác.

Ba mục H, I, J do Claude Code viết thêm vào file này, tức là **mở rộng** phần
lộ lọt đó. Cần chủ sở hữu quyết: `git rm` khỏi `main` (lịch sử vẫn còn, chỉ
giảm khả năng tìm thấy về sau), chuyển sang nơi riêng tư, hay lược bớt chi tiết
file:line. Dự án cấm rewrite history nên không có phương án xoá sạch dấu vết.

### J.6 Còn mở sau vòng 4

| # | Vấn đề | Ghi chú |
|---|---|---|
| J-1 | Assertion không mang tier (J.3) | Chờ quyết định của chủ sở hữu. Đây là mục quan trọng nhất còn lại. |
| J-2 | Tài liệu này nằm trong repo public (J.5) | Chờ quyết định của chủ sở hữu. |
| J-3 | `VALID_EXE_HASHES` rỗng | Kiểm tra hash EXE có code nhưng danh sách rỗng nên không thi hành. Cần quy trình điền hash khi phát hành. |
| J-4 | `DeviceIdentity.Role` | Chưa đánh giá được: không tìm thấy khai báo `class DeviceIdentity` trong repo. |
| I-1, I-2, I-4, I-5, I-6 | Xem I.6 | Chưa đổi. |
