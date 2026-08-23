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
| P0-1 | ✅ đã vá | `IsFullStackOperationAllowed()`; Release cưỡng chế tier, Debug giữ đường thoát bằng `#if DEBUG` thay vì comment-out. |
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
