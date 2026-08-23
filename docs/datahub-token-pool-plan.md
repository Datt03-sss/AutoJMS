# AutoJMS DataHub — Kế hoạch Token Pool (danh sách phiên hợp lệ)

Cập nhật 2026-08-23. Phạm vi: **kế hoạch, không code.**

> **Bối cảnh:** tài khoản JMS hoạt động theo phiên; mỗi máy giữ một phiên WebView2 hợp lệ trong
> `AppData\BrowserData\EBWebView`. Nhiều máy ULTRA cùng bưu cục ⇒ nhiều token; cần chọn một token để
> fetch nền, failover khi hỏng, và không để hai máy fetch song song.
>
> ⚠️ **Trạng thái: CHƯA TRIỂN KHAI.** Backend hiện tại (`AutoJMS.DataHub.Api`) **không có bảng token
> nào**. Bản v2 của tài liệu này mô tả 5 bảng registry, private RPC, Edge boundary, DB role
> `datahub_worker`/`datahub_edge`, Named Pipe và một Worker Windows Service — **không cái nào tồn tại**,
> và kiến trúc đã chốt không đi theo hướng đó. §1–§2b dưới đây là dữ kiện đọc từ code, vẫn đúng. §3 trở
> đi đã được viết lại theo những gì thật sự đang chạy (§3) và hình dạng đúng nếu sau này cần làm (§4).
>
> Hợp đồng as-built: [`architecture/datahub-backend-design.vi.md`](./architecture/datahub-backend-design.vi.md).

---

## 1. Phát hiện từ mã nguồn (định hình phạm vi "thông tin request hợp lệ")

`JmsApiClient.SendOnceAsync` dựng request hợp lệ bằng:
- Header **`authToken`** (32-hex) ← **bí mật duy nhất theo phiên**.
- Header tĩnh: `lang=VN`, `langType=VN`, `routeName`, `routerNameList` (tuỳ chọn), `timezone=GMT+0700`.
- `User-Agent` (hằng `DefaultUserAgent`).
- URL từ `AppConfig.BuildJmsApiUrl`.
- **Không đính cookie.**

⇒ "Thông tin request hợp lệ" cần ghi lại **về cơ bản chỉ là `authToken`** cho mỗi phiên. Cookie/UA/
header khác là **hằng ở mức code**, chỉ capture bổ sung **nếu** spike token-binding (P0-E / G1) cho thấy
một endpoint nào đó cần. Vậy "danh sách" = **pool authToken + metadata hiệu lực**, không phải sao
chép cả profile trình duyệt.

> Anti-pattern cần tránh: **không** copy cả thư mục `EBWebView` giữa các máy (nặng, dễ hỏng, có thể
> ràng buộc thiết bị). Chỉ trích xuất token (và tối thiểu context nếu spike đòi).

### 1b. Hợp đồng request hợp lệ — kiểm chứng từ code (`JmsApiClient.SendOnceAsync`)

Một request JMS hợp lệ được định nghĩa **đầy đủ** bởi (tất cả đều tái tạo được headless):

**HTTP:** `POST`, `Content-Type: application/json`, body JSON.
**Headers (đúng thứ tự code đang gửi):**
| Header | Giá trị | Ghi chú |
|---|---|---|
| `authToken` | `<32-hex>` | **Bí mật DUY NHẤT theo phiên** |
| `lang` / `langType` | `VN` / `VN` | hằng |
| `routeName` | theo endpoint (bên dưới) | hằng |
| `routerNameList` | breadcrumb %-encoded (chỉ inventory) | hằng |
| `timezone` | `GMT+0700` | hằng |
| `Accept` | `application/json, text/plain, */*` | hằng |
| `Origin` | `https://jms.jtexpress.vn` | hằng |
| `Referer` | `https://jms.jtexpress.vn/` | hằng |
| `User-Agent` | chuỗi Chrome 120 (hằng trong code) | hằng |

**Base + endpoint (từ `AppConfig`):** API gateway `https://jmsgw.jtexpress.vn`.
| Chức năng | Path | routeName | routerNameList |
|---|---|---|---|
| Tracking (hành trình) | `operatingplatform/podTracking/inner/query/keywordList` | `trackingExpress` | — |
| Order detail | `operatingplatform/order/getOrderDetail` | `trackingExpress` | — |
| Inventory (tồn) | `businessindicator/bigdataReport/detail/take_ret_mon_detail_doris2` | `DetentionMonitoringDB` | `%E7%BB%8F…DB` |

**Bằng chứng quyết định — auth KHÔNG dính cookie/thiết bị ở tầng HTTP:** `JmsApiClient` dùng một
`HttpClient` tĩnh với `HttpClientHandler` **chỉ bật AutomaticDecompression** — **không** seed
CookieContainer từ `EBWebView`, không client cert, không fingerprint. Vậy mà desktop vẫn fetch được.
⇒ **Cookie/profile trình duyệt KHÔNG phải là chứng thực; chỉ header `authToken` là.** Mọi process có
`authToken` hợp lệ + bộ header hằng trên tạo ra **request giống hệt, hợp lệ**.

**Governor:** cap **12 request JMS in-flight app-wide** (`MaxConcurrentJmsRequests`) để tránh JMS khoá
IP. Bất cứ thứ gì gọi JMS đều phải nằm dưới trần này, cộng mọi scope.

⇒ **Kết luận:** fetch **tách headless được**. "Session-bound" chỉ nằm ở chỗ WebView2 *sinh ra* token và
tuổi thọ token gắn với phiên; còn *dùng* token là thuần header HTTP, không cần browser. Thực tế hiện nay
vẫn để process UI gọi JMS (xem §3) — không phải vì bắt buộc, mà vì như thế token không cần rời process.

---

## 2. Nguyên tắc: pool nhiều token — dùng MỘT tại một thời điểm

- Gom mọi authToken hợp lệ từ các máy thành **pool ứng viên**.
- **Chỉ một token "active"** cho **bulk-fetch** tại một thời điểm (kỷ luật single-session **cho bulk**).
  ⚠️ **Contributor lẻ là NGOẠI LỆ bounded** (owner chốt): desktop có thể tự gọi JMS 1 đơn bằng token
  WebView cục bộ song song với bulk — chấp nhận rủi ro, giới hạn bằng permit (xem P0-D).
- Các token còn lại ở trạng thái **standby/failover**.
- Token đang dùng hỏng → **tạm dừng fetch nền**, giữ snapshot đã có, chờ token mới (user đăng nhập lại).
  Không tắt gì cả, và không xoá dữ liệu.

Ánh xạ 3 trường hợp (áp cho mô hình lease ở §3: "chọn token" trên thực tế là "máy nào giữ lease"):
| TH | Pool | Hành vi |
|---|---|---|
| **1 — 1 máy** | 1 token | Máy đó giữ lease; token hỏng → chờ chính nó refresh (WebView2 còn phiên). |
| **2 — 2+ máy** | 2+ token | Đúng một máy giữ lease tại một thời điểm; máy đó fetch bằng token của chính nó. Nhả/hết lease → máy khác đoạt. |
| **3 — token đều invalid** (probe fail, KHÔNG phải "hết hạn" đọc được) | 0 hợp lệ | Không máy nào fetch được; UI vẫn đọc dữ liệu cũ qua `/projections/snapshot`. |

---

## 2b. Token lifecycle & minting (GIẢ ĐỊNH — cần spike P0-E xác nhận)

> ⚠️ Đây là **giả định vận hành, chưa phải quyết định cuối** (thống nhất với master #9 / P0-E). Phải
> spike xác nhận hard-expire lúc giao ngày + hành vi logout trước khi cam kết.

**Giả định token JMS:** một `authToken` giữ hiệu lực **cả ngày (00:00–23:59)**, chỉ mất khi **logout**.
Mỗi ngày nhân viên **tự đăng nhập khi bắt đầu làm việc** nếu token cũ đã hết → thao tác đăng nhập
hằng ngày này **chính là** cơ chế cấp token mới cho pool.

**Hệ quả kiến trúc:**
- **Không cần tiến trình nuôi token riêng.** Việc login buổi sáng (nhân viên vẫn phải làm để làm việc)
  tự sinh token tươi cho máy đó.
- **Fetch nền chạy trong giờ làm, trong process UI của máy đang giữ lease.** Đóng hết AutoJMS ⇒ không ai
  fetch; dữ liệu vẫn nằm trên VPS và đọc lại được ngay khi mở app.
- **Ranh giới ngày / qua đêm:** nếu token hết hiệu lực lúc giao ngày và không ai đăng nhập ban đêm →
  fetch **tạm dừng qua đêm** (degraded), tự chạy lại khi có người đăng nhập sáng hôm sau. Chấp nhận
  được vì nghiệp vụ diễn ra ban ngày. Timer hiện tại vốn chỉ chạy 8h–23h30.
- **Nếu sau này cần fetch 24/7 xuyên nửa đêm:** khi đó mới cần một máy giữ phiên/đăng nhập lại lúc
  giao ngày — **không thuộc phạm vi hiện tại**.

> Còn phải xác nhận bằng quan sát vận hành: hành vi hết hạn chính xác lúc giao ngày và khi logout. Độ dài
> sống (theo ngày) coi như đã biết từ vận hành thực tế.

**⚠️ HARD-FACT token JMS (đã kiểm code — quyết định state machine):**
- **KHÔNG có trường expiry, KHÔNG có RefreshToken, KHÔNG có refresh endpoint đã xác minh.** `authToken` là
  chuỗi 32-hex đọc từ WebView2; `JmsAuthTokenService.ForceRefreshFromWebViewAsync()` **chỉ đọc lại**
  authToken từ WebView2 — **không** gọi API refresh; nếu vẫn lỗi → **yêu cầu user đăng nhập lại**.
- ⇒ **CẤM dùng khái niệm "token còn hạn/hết hạn" như một thuộc tính đọc được.** Không có cách biết token
  hợp lệ **ngoài việc probe** (gọi thử JMS).
- **State machine thuần probe-driven:** token mới = **`unknown`** → **probe** → `valid`/`invalid`;
  **lỗi mạng (429/5xx/timeout) KHÔNG phải invalid** (không cộng strike); **auth-fail** đi **two-strike
  `valid→suspect→invalid`**; hết token → tạm dừng fetch nền, app vẫn chạy (xem §4.1).
- "Hết hạn qua đêm" ở trên là **giả định vận hành** (mất token khi logout/giao ngày), **không** phải trường
  expiry — biểu hiện của nó vẫn là **auth-fail khi probe**, xử lý y hệt `invalid`.

---

## 3. Hiện trạng đang chạy — chống trùng bằng LEASE, không bằng token registry

Kiến trúc đã dựng giải bài toán "nhiều máy" ở một tầng khác: thay vì gom token vào cloud rồi chọn token
active, nó **chọn máy active**. Máy nào giữ lease thì máy đó fetch bằng token WebView2 của **chính nó**.

| Vấn đề | Cách bản v2 định giải | Cách đang chạy |
|---|---|---|
| Hai máy fetch trùng | active token pointer + fence trong DB | lease theo site: `POST /api/v1/sites/{siteId}/lease/{acquire,renew,release}`, bảng `site_fetch_leases`, TTL **120 giây** do server đặt |
| Token đi tới đâu | relay qua Named Pipe cho Worker cùng máy | **không đi đâu cả** — token ở trong phiên WebView2 của process UI, chính process đó gọi JMS |
| Lưu token | DPAPI `tokens.dat` của Service | không lưu ra đĩa: `SettingsManager` **đã ngừng** ghi `lastAuthToken` vào `AutoJMS.json` (plaintext), key cũ bị strip ở lần save kế |
| Ghi lệch nhiệm kỳ | `leader_fencing_token` + RPC CAS | `DataHubClient.CurrentLeaderTerm` / `HasSiteLease` phía client; server đối chiếu lease khi ghi |
| Ghi trùng dữ liệu | receipt/ACK per-event | `UNIQUE (site_id, event_fingerprint)` + header `Idempotency-Key` |

Hệ quả: **pool token không tồn tại như một khái niệm phía server.** Một site có N máy ULTRA thì có N
token độc lập, và tại một thời điểm đúng một máy giữ lease nên đúng một token đang gọi JMS nền. Kỷ luật
single-session đạt được **không cần** biết token của máy khác.

Contributor lẻ (user mở một đơn, client gọi `getOrderDetail` rồi `POST /jms/observations`) vẫn là ngoại
lệ bounded như §2: dùng token WebView cục bộ, không qua lease.

**Còn thiếu so với ý định ban đầu:**

- Không có bảng `devices`-level liveness cho token: nếu máy giữ lease mất token, nó phải tự nhả lease.
  Trường hợp máy treo thì phải chờ lease hết 120 giây.
- Không có site attestation (§4 dưới đây): token hợp lệ của **bưu cục khác** vẫn trả `code=1`, và hiện
  không có gì đối chiếu `principal`/`actionSiteCode` với site của license trước khi ingest.
- Không có site-wide rate limit chung cho bulk + probe + contributor.

## 4. Nếu sau này cần làm thật — hình dạng đúng theo kiến trúc API

Ba phần dưới đây là những phần của bản v2 **vẫn còn giá trị**, viết lại cho đúng backend hiện tại. Tất cả
đều là **endpoint + bảng**, không phải RPC, không phải DB role cho máy trạm.

### 4.1 Probe 4 kết quả + site attestation (nên làm — đây là lỗ bảo mật thật)

Vì `authToken` **không có trường expiry, không có refresh endpoint** (`JmsAuthTokenService.ForceRefreshFromWebViewAsync`
chỉ đọc lại token từ WebView2), không có cách biết token còn dùng được ngoài **probe**. Phân loại bắt buộc
bốn kết quả:

| Kết quả | Điều kiện | Xử lý |
|---|---|---|
| `Valid` | `2xx` + `code=1` **và** `principal`/`actionSiteCode` khớp site của license | dùng được |
| `AuthRejected` | 401, hoặc `2xx` với body báo hết phiên | two-strike → coi như mất token |
| `Transient` | 429 / 5xx / timeout | **không** tính strike, backoff |
| `SiteMismatch` | auth OK nhưng site không khớp | **không** dùng, cảnh báo — đây là ca bơm dữ liệu sai site |

Two-strike + single-flight: với governor 12 request in-flight, nhiều lỗi ập tới cùng lúc; chỉ **một** lỗi
auth đầu tiên chuyển sang `suspect` (CAS phía client), ngừng phát request mới với token đó, rồi **một**
probe xác nhận mới kết luận. Lỗi mạng không bao giờ là lỗi token.

`SiteMismatch` là phần đáng làm trước: server nên từ chối ingest khi payload không khớp site của device
token — kiểm ở endpoint, một chỗ, thay vì tin client.

### 4.2 Nếu cần theo dõi trạng thái token nhiều máy

Thì đó là **hai endpoint + một bảng**, không phải 5 bảng và không phải RPC:

- `POST /api/v1/sites/{siteId}/token-state` — máy báo `{ tokenFingerprint, state, observedPrincipalFp }`.
  `siteId` lấy từ device token; `deviceId` cũng vậy. Client không khai site.
- `GET /api/v1/sites/{siteId}/token-state` — trả trạng thái tổng hợp cho banner UI.
- Bảng `device_token_states` (một migration `006_*.sql`): `site_id`, `device_id`, `token_fingerprint`,
  `state`, `observed_principal_fp`, `updated_at`, PK `(site_id, device_id)`.

**Không lưu token, không lưu ciphertext** — chỉ `SHA256(token)` để định danh, log `first4…last4`. Lộ
database vẫn không lộ token. Đây là điểm duy nhất của bản v2 phải giữ nguyên tinh thần.

### 4.3 Rate limit cấp site

Một bucket duy nhất đếm chung **bulk + probe + contributor**, đặt ở API (không ở client, vì client không
thấy nhau), **fail-closed**: API lỗi ⇒ không cấp phép ⇒ không gọi JMS. Trần phải tôn trọng governor 12
in-flight ở §1b.

## 5. Việc cần chốt

- [ ] **`SiteMismatch` guard ở endpoint ingest** (§4.1) — ưu tiên cao nhất trong danh sách này, vì nó là
      đường bơm dữ liệu sai site.
- [ ] Máy giữ lease mất token ⇒ nhả lease ngay thay vì chờ TTL 120 giây.
- [ ] Two-strike phía client (hiện `JmsApiClient` retry 401 một lần rồi báo hết hạn — chưa phân biệt
      `Transient` với `AuthRejected` một cách hệ thống).
- [ ] Có cần `device_token_states` (§4.2) hay banner UI cục bộ là đủ.
- [ ] Rate limit cấp site (§4.3) — chỉ cần khi số máy ULTRA mỗi bưu cục tăng.
- [ ] Fetch xuyên nửa đêm: hiện chấp nhận degraded (§2b). Nếu cần 24/7 mới phải nuôi một phiên đăng nhập
      lại lúc giao ngày.
