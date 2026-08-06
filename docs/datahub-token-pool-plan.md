# AutoJMS DataHub — Kế hoạch Token Pool (danh sách phiên hợp lệ)

> Tinh chỉnh của token relay → **token pool**; token-binding spike ở `datahub-p0-contract.md` **P0-E /
> G1**. Phạm vi: **kế hoạch, không code.**
>
> **Bối cảnh:** tài khoản JMS hoạt động theo phiên; mỗi máy giữ một phiên WebView2 hợp lệ trong
> `AppData\BrowserData\EBWebView`. Cần ghi lại thông tin request hợp lệ từ các phiên này thành **một
> danh sách (pool)**, chọn một token hợp lệ để fetch, failover khi hỏng; hết token → **PAUSE fetch
> (service Windows vẫn chạy, chờ relay)** — KHÔNG terminate (xem `datahub-worker-lifecycle.md`).

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

**Governor bắt buộc sao chép ở Worker:** cap **12 request JMS in-flight app-wide**
(`MaxConcurrentJmsRequests`) để tránh JMS khoá IP. Worker phải giữ đúng trần này (cộng mọi scope).

⇒ **Kết luận:** fetch **tách headless được hoàn toàn**. "Session-bound" chỉ nằm ở chỗ WebView2
*sinh ra* token và tuổi thọ token gắn với phiên; còn *dùng* token là thuần header HTTP, không cần
browser. Câu hỏi "chạy máy khác được không" **thu về đúng một ẩn số phía server**: JMS có ràng token
với IP nguồn không — **không có gì trong cách dựng request buộc token vào máy/phiên cả.**

---

## 2. Nguyên tắc: pool nhiều token — dùng MỘT tại một thời điểm

- Gom mọi authToken hợp lệ từ các máy thành **pool ứng viên**.
- **Chỉ một token "active"** cho **bulk-fetch** tại một thời điểm (kỷ luật single-session **cho bulk**).
  ⚠️ **Contributor lẻ là NGOẠI LỆ bounded** (owner chốt): desktop có thể tự gọi JMS 1 đơn bằng token
  WebView cục bộ song song với bulk — chấp nhận rủi ro, giới hạn bằng permit (xem P0-D).
- Các token còn lại ở trạng thái **standby/failover**.
- Token active hỏng → chọn token local kế; không còn → **PAUSE** (service Windows vẫn chạy, chờ relay).

Ánh xạ 3 trường hợp:
| TH | Pool | Hành vi |
|---|---|---|
| **1 — 1 máy** | 1 token | Dùng token đó; nếu hỏng → chờ máy đó refresh (WebView2 còn phiên). |
| **2 — 2+ máy** | 2+ token | Chọn **1** token hợp lệ (theo §6) làm active; còn lại standby; xoay khi active hỏng. |
| **3 — token đều invalid** (probe fail, KHÔNG phải "hết hạn" đọc được) | 0 hợp lệ | **PAUSE fetch** (service chạy, `NO_VALID_TOKEN`); giữ snapshot; chờ relay. |

---

## 2b. Token lifecycle & minting (GIẢ ĐỊNH — cần spike P0-E xác nhận)

> ⚠️ Đây là **giả định vận hành, chưa phải quyết định cuối** (thống nhất với master #9 / P0-E). Phải
> spike xác nhận hard-expire lúc giao ngày + hành vi logout trước khi cam kết.

**Giả định token JMS:** một `authToken` giữ hiệu lực **cả ngày (00:00–23:59)**, chỉ mất khi **logout**.
Mỗi ngày nhân viên **tự đăng nhập khi bắt đầu làm việc** nếu token cũ đã hết → thao tác đăng nhập
hằng ngày này **chính là** cơ chế cấp token mới cho pool.

**Hệ quả kiến trúc (chốt):**
- **Pool-only, KHÔNG cần TokenAgent riêng.** Việc login buổi sáng (nhân viên vẫn phải làm để làm
  việc) tự bơm token tươi vào pool → không phải nuôi thêm tiến trình browser.
- **Fetch service chạy độc lập suốt ngày làm việc** dù mọi AutoJMS đã đóng — dùng token của ngày.
  Nhiều máy ⇒ pool luôn có ít nhất một token tươi trong giờ làm.
- **Ranh giới ngày / qua đêm:** nếu token hard-expire lúc 00:00 và không ai đăng nhập ban đêm →
  fetch **tạm dừng qua đêm** (degraded), tự chạy lại khi có người đăng nhập sáng hôm sau. Chấp nhận
  được vì nghiệp vụ diễn ra ban ngày.
- **Nếu sau này cần fetch 24/7 xuyên nửa đêm:** khi đó mới cần một máy giữ phiên/đăng nhập lại lúc
  giao ngày (biến thể TokenAgent) — **không thuộc phạm vi hiện tại**.

> Vẫn cần spike P0-E (G1) xác nhận: (a) token dùng được từ process/máy khác **cùng IP LAN**; (b) hành vi
> hết hạn chính xác lúc giao ngày/logout. Độ dài sống (theo ngày) coi như đã biết từ vận hành thực tế.

**⚠️ HARD-FACT token JMS (đã kiểm code — quyết định state machine):**
- **KHÔNG có trường expiry, KHÔNG có RefreshToken, KHÔNG có refresh endpoint đã xác minh.** `authToken` là
  chuỗi 32-hex đọc từ WebView2; `JmsAuthTokenService.ForceRefreshFromWebViewAsync()` **chỉ đọc lại**
  authToken từ WebView2 — **không** gọi API refresh; nếu vẫn lỗi → **yêu cầu user đăng nhập lại**.
- ⇒ **CẤM dùng khái niệm "token còn hạn/hết hạn" như một thuộc tính đọc được.** Không có cách biết token
  hợp lệ **ngoài việc probe** (gọi thử JMS).
- **State machine thuần probe-driven:** token mới relay = **`unknown`** → Worker **probe** → `valid`/`invalid`;
  **lỗi mạng (429/5xx/timeout) KHÔNG phải invalid** (không cộng strike); **auth-fail** đi **two-strike
  `valid→suspect→invalid`**; hết mọi candidate → **`NO_VALID_TOKEN`**, service **vẫn chạy** chờ relay mới.
- "Hết hạn qua đêm" ở trên là **giả định vận hành** (mất token khi logout/giao ngày), **không** phải trường
  expiry — biểu hiện của nó vẫn là **auth-fail khi probe**, xử lý y hệt `invalid`.

---

## 3. Registry schema (per-site project) — **tách 5 bảng** (sửa §3 v1 tự mâu thuẫn)

> ⚠️ Schema cũ để `session_id` PK **lẫn** `unique(site_code, token_fp)`: một token relay từ 2 máy
> làm mất nguồn, token mới ghi đè token cũ cùng profile, và heartbeat reset `status=unknown` gây probe
> liên tục. Tách trách nhiệm thành **5 bảng** (4 chính + 1 audit):

**(1) `jms_session_sources`** — máy/profile & liveness (desktop sở hữu, chỉ ghi qua RPC):
```
session_id text PK        -- machine GUID + webview profile (ổn định)
site_code  text           -- từ JWT, không client tự khai
machine    text           -- che khi log
last_heartbeat_at timestamptz
alive      boolean
```

**(2) `jms_token_binding`** — CHỈ **metadata** (⚠️ KHÔNG ciphertext — token ở local `tokens.dat`):
```
site_code text, worker_id text
token_fp text            -- SHA256(token) định danh, KHÔNG lộ token
license_subject text     -- license đang cấp token này (khớp license_entitlement → revoke chính xác)
session_id text · relay_generation int · last_relayed_at · last_validated_at
primary key (site_code, worker_id)   -- mỗi Worker/máy giữ token của CHÍNH nó (không dùng chéo máy)
```
⚠️ **BỎ** `token_ciphertext`/`recipient_key_id`/`context_ciphertext`/multi-key: token **không lên cloud**.
Supabase chỉ giữ `token_fp` + binding để điều phối leader/failover.

**(3) `jms_token_candidate_state`** — trạng thái xác thực do **Worker sở hữu** (desktop KHÔNG ghi).
**Danh tính = `candidate_id` BẤT BIẾN** (High#5 — hết mơ hồ token_fp):
```
candidate_id uuid primary key        -- BẤT BIẾN: một danh tính cho MỖI lần relay token
site_code text, worker_id text
token_fp text                        -- SHA256(token)
generation int not null              -- ++ mỗi lần relay token mới (kể cả trùng fp) → phân biệt lần relay
license_subject text                 -- license cấp token này
session_id text null                 -- phiên WebView (nhánh device-bound)
principal_fp text null               -- ⚠️ SITE ATTESTATION: fp principal/actionSiteCode QUAN SÁT từ JMS (§6)
state text                 -- unknown | valid | suspect | invalid | site_mismatch
validated_by text, last_validated_at timestamptz, fail_count int
unique (site_code, worker_id, token_fp, generation)   -- N candidate/worker; mỗi lần relay 1 candidate_id
```
> ✅ **Cardinality + crash-consistency:** nhiều candidate/worker (đúng "A thử token khác của A");
> **`candidate_id` bất biến** ⇒ active/binding tham chiếu chính xác dù cùng `token_fp` qua các lần relay.

**(4) `jms_active_token`** — **một active pointer / site** (KHÔNG phải bảng relay metadata — tách bạch
với `jms_token_binding`), fence theo leader, **trỏ candidate BẤT BIẾN**:
```
site_code text PK
candidate_id uuid not null   -- ⚠️ trỏ candidate BẤT BIẾN (High#5) — không chỉ token_fp
generation int not null      -- + generation: chống dùng nhầm token cùng fp KHÁC lần relay
worker_id text · license_subject text · execution_mode text  -- 'worker' | 'proxy'
leader_fencing_token bigint · leader_term bigint · selection_epoch bigint
leader_owner text (chẩn đoán) · selected_at
```
> ⚠️ Đặt active qua **RPC CAS**: kiểm `leader_fencing_token`/`leader_term` khớp leader **và** candidate
> `state=valid`; ghi kèm fence; fence cũ → từ chối. **`jms_token_binding` = relay metadata per-worker**
> (last_relayed/generation); **`jms_active_token` = con trỏ đang dùng** — hai vai KHÁC nhau, không lẫn.
> **Token A chỉ dùng bởi Worker A** (không chéo máy).

**(5) `jms_token_validation_events`** — audit rút gọn (probe/failover), che token.

> **5 bảng metadata; KHÔNG bảng nào chứa ciphertext JMS** (token ở local `tokens.dat`). Nhánh B
> (device-bound) mới cần proxy subsystem riêng — chỉ dựng nếu spike G1a/b = device-bound.

**Luật:**
- **AutoJMS → Worker CÙNG MÁY qua Named Pipe** (không relay token lên cloud). AutoJMS gọi Edge **chỉ** cho
  `session`/`heartbeat`/`client_contribute` — **KHÔNG gửi token, KHÔNG publish `jms_token_binding`**.
  Binding do **Worker/gateway** publish sau khi persist token (xem §4 thứ tự nguyên tử) — hết mâu thuẫn
  "desktop/Edge cập nhật binding".
- **Chỉ Worker** đặt `state`/`jms_active_token`. **Two-strike** single-flight (401→`suspect`, xác nhận→`invalid`).
- Desktop **không đọc registry**. Lộ DB/registry **KHÔNG lộ token JMS**.

---

## 4. Relay token — Named Pipe CỤC BỘ (KHÔNG qua cloud)

- **Token:** `WebViewTokenReader` đọc `authToken` từ WebView2 → AutoJMS gửi cho **Worker CÙNG MÁY** qua
  **Named Pipe** (ACL: server = Service SID; client = local user thường của AutoJMS — xem lifecycle §8).
- ⚠️ **Thứ tự nguyên tử (chống dual-write lệch, KHÔNG thể atomic xuyên local↔cloud → dùng JOURNAL):**
  Vì không có transaction phủ cả file local lẫn Supabase, dùng **local durable journal/outbox** để đạt
  crash-consistency (High#5):
  1. Worker ghi **journal record** `{candidate_id (mới), token_fp, generation, license, session, state=pending_persist}` (durable, local).
  2. **Persist `tokens.dat` (DPAPI)** → cập nhật journal `state=persisted`.
  3. Trả **ACK** cho AutoJMS qua pipe.
  4. **Publish `jms_token_binding` (CAS idempotent theo `candidate_id`)** → journal `state=published`.
  - **Startup reconciliation:** khi Worker khởi động, quét journal: record `persisted` nhưng chưa
    `published` → **publish lại (idempotent)**; record `pending_persist` mà `tokens.dat` không có → bỏ.
  - **Publish idempotent theo `candidate_id`** (bất biến) ⇒ chạy lại nhiều lần cho cùng kết quả.
  **Desktop KHÔNG tự ghi binding** (tránh binding ảo khi token chưa lưu). Binding chỉ tồn tại sau khi
  token đã nằm an toàn ở local (journal `persisted`).
- Không plaintext, không copy `EBWebView`, log `first4…last4`. **KHÔNG gửi token/ciphertext lên cloud.**
- **`state` do Worker** ghi (`jms_token_candidate_state`) sau probe; heartbeat không reset state.
- Desktop **không đọc registry**; nhận `token_state` tổng hợp cho banner qua Edge.

---

## 5. Vòng đời hiệu lực (state — CHỈ 4 giá trị, Worker sở hữu)

```
unknown ──probe OK──► valid ──auth-fail #1──► suspect ──xác nhận #2──► invalid
   ▲                    │                         │
   │                    └──dùng (active)          └──probe OK lại──► valid
desktop relay token mới (qua **Named Pipe local**, KHÔNG qua Edge) → ứng viên mới (unknown) → Worker probe → …
```
- **Chỉ 4 state:** `unknown | valid | suspect | invalid` (bỏ hẳn `expired/rejected` cũ).
- **Ai probe:** **chỉ Worker** (giữ `fetch_leader`) probe **rẻ** một endpoint nhẹ (head-probe
  inventory) trước khi đưa token vào active; không probe ồ ạt (**site-wide rate limit**).
- ⚠️ **[CRITICAL#3] PROBE có 4 KẾT QUẢ + SITE ATTESTATION (chống token bưu cục KHÁC bị nhận `valid`):**
  token hợp lệ của **bưu cục khác** vẫn trả 2xx `code=1` → nếu chỉ xét mã, nó thành `valid` và **bơm dữ
  liệu SAI vào project-per-site**. Bắt buộc phân loại probe:
  - **`Valid`** = `2xx` + `code=1` **VÀ** `principal`/`actionSiteCode` trong response **KHỚP `site_code`
    của license** (đọc từ chính response JMS, **KHÔNG** tin `site_code`/`license_subject` client gửi).
  - **`AuthRejected`** = 401 / expired-body → two-strike `invalid`.
  - **`Transient`** = 429/5xx/timeout → **KHÔNG** strike, retry/backoff (không kết luận invalid).
  - **`SiteMismatch`** = auth OK nhưng principal/site **KHÔNG khớp** license site → **KHÔNG** đưa active,
    đánh dấu riêng (không dùng token này cho site này), alert; không tính là `valid`.
  - Lưu **fingerprint của `principal`/`site`** quan sát được (`jms_token_candidate_state`) để đối chiếu
    lần sau; binding site chỉ dựa trên **attestation từ JMS**, không dựa metadata relay.
- **Two-strike + single-flight (chống 12 request đồng thời tính nhầm 2 strike):** với governor 12
  in-flight, nhiều lỗi có thể ập tới cùng lúc. Quy tắc:
  1. **Chỉ đếm strike khi `JmsResponseClassifier.auth_rejected`** — tức 401 **HOẶC** HTTP 2xx với
     **expired-body** (session hết hạn). **429 / 5xx / timeout KHÔNG phải auth failure** → không thành
     strike (đi vào retry/backoff bình thường).
  2. Auth-fail đầu tiên chuyển `valid→suspect` bằng **CAS/single-flight** (một request thắng; các lỗi
     song song còn lại không cộng strike).
  3. **Ngừng phát request mới** với token đó khi vào `suspect`.
  4. Chỉ **một probe xác nhận** chuyển `suspect→invalid` (probe auth-fail) hoặc `suspect→valid` (OK).
- **Dọn:** pg_cron xoá ứng viên `invalid` cũ quá TTL (maintenance, không quyết định fetch).

---

## 6. Thuật toán chọn token active + xử lý 3 TH

Actor giữ `fetch_leader` chạy thuật toán, **tách 2 nhánh theo loại executor**:

**Token luôn LOCAL của Worker** (trong `tokens.dat`); "selection" = Worker chọn token nào **của chính nó**
để active, KHÔNG giải mã ciphertext cloud.

⚠️ **Eligibility tranh leader (SỬA DEADLOCK PROBE):** probe chỉ chạy khi **đang giữ lease** (chỉ leader
được probe). Nếu cấm **mọi** Worker chưa-có-`valid` tranh leader thì token `unknown` mới relay **không bao
giờ được probe** ⇒ **deadlock bootstrap** (token mới luôn bắt đầu `unknown`). Chốt 3 mức:
- **Có ≥1 candidate `valid`** → tranh **leader THƯỜNG** (được bulk-fetch).
- **Chỉ có candidate `unknown`** (chưa probe) → được tranh **PROVISIONAL leader** *chỉ để probe* (head-probe
  nhẹ, site-wide rate limit; **KHÔNG bulk-fetch** khi còn provisional).
- **ZERO candidate** = trạng thái **`NO_LOCAL_TOKEN`** → **KHÔNG** đủ điều kiện tranh leader (chờ relay).

Thuật toán (khi đang giữ lease — thường hoặc provisional):
1. Liệt kê candidate `valid` của `worker_id` mình. Có → nếu đang provisional thì **promote → leader
   THƯỜNG**, sang (4).
2. Không `valid` nhưng có `unknown` → **probe** (dưới provisional lease) → `valid`→(1); `invalid`→loại,
   xét candidate kế.
3. Hết cả `valid` lẫn `unknown` (Worker thật sự hết token) → **THỨ TỰ (khớp lifecycle §5, High#6):**
   (0) ngừng JMS; **(0.5) BOUNDED final-flush outbox DƯỚI fence đang giữ** (drain có giới hạn) — làm
   **trước** khi nhả leader; rồi → **RPC NGUYÊN TỬ `clear_active_and_release_leader`** (một transaction,
   CAS theo `leader_fencing_token`/`leader_term`): (a) xoá `jms_active_token` + tăng `selection_epoch`;
   (b) **nhả `fetch_leader`** — **KHÔNG** tách 2 bước (chỉ `clear_active_token` mà **giữ leader** → B không
   giành được → cả site kẹt dù B còn token). **Sau release KHÔNG flush bằng fence cũ.** Vào
   **`NO_LOCAL_TOKEN`** + **PAUSE** (service chạy, chờ relay).
   ⚠️ **`NO_LOCAL_TOKEN` (ZERO candidate) KHÔNG đủ điều kiện tranh lại leader** cho tới khi **có candidate
   mới — kể cả `unknown`**; lúc đó lại được tranh **provisional** để probe. (Chặn flap **chỉ** khi ZERO
   candidate, **không** chặn khi đã có `unknown` → bootstrap/relay mới luôn probe được.)
4. Chọn `valid` theo độ tươi → đặt `jms_active_token` qua **RPC CAS** (khớp `leader_fencing_token`/
   `leader_term`; ghi `worker_id`+`token_fp`+`license_subject`; tăng `selection_epoch`).

**Nhánh Proxy (chỉ khi spike = device-bound):** token vẫn local trên máy sở hữu; Worker (leader) ĐIỀU PHỐI,
proxy THỰC THI request và trả kết quả **ĐÃ KÝ** (device credential); `execution_mode='proxy'`. Cần proxy
subsystem riêng (§9).
- ⚠️ **`mark_candidate_state` BẮT BUỘC kiểm `leader_fencing_token`/`leader_term`** — probe/kết quả từ
  nhiệm kỳ cũ (fence thấp hơn) **bị từ chối**, không ghi đè state của nhiệm kỳ mới.
5. Fetch bằng token active cho **mọi scope** (một phiên). **Auth-fail giữa chừng KHÔNG đổi token
   ngay:**
   - Lần 1 (strike 1): đánh `suspect`, **giữ nguyên** token active, **re-probe** ngay bằng head-probe
     nhẹ. Probe OK → về `valid` (lỗi thoáng qua). Probe fail = xác nhận (strike 2) → `invalid`.
   - Chỉ khi `invalid` mới **đổi active** (chọn candidate kế) + tăng `selection_epoch`; batch dở dừng
     an toàn, retry (idempotent nhờ fingerprint).
   → Không có chuyện "đổi token ngay sau 401" (đã đồng bộ với §5 two-strike).

Kỷ luật: một active identity duy nhất **gắn với site-leader** (không per-scope — xem §7).

---

## 7. Single-session enforcement — gắn SITE-LEADER, không per-scope

> ⚠️ Sửa lỗi v1: nếu chỉ gắn active token với **fencing của scope lease**, mỗi scope có fencing riêng
> ⇒ Worker giữ `tracking` còn desktop-fallback giữ `inventory` → **hai token khác nhau gọi JMS song
> song** (hai phiên). Phải có lease gốc cấp site.

- **`fetch_leader` lease cấp site là gốc**; các scope lease (`inventory`, `tracking-hot`…) là **lease
  con của cùng leader**. Chỉ **một** owner giữ `fetch_leader` tại một thời điểm.
- **Ai được tranh leader = Worker có ENTITLEMENT hợp lệ** (không phải "bất kỳ Worker có DB role").
  ⚠️ **Worker KHÔNG có JWT `sub/session_id`** (không phải desktop) → **không** gọi `jwt_entitlement_ok()`
  như desktop. **Contract entitlement riêng cho Worker:**
  - ⚠️ **Binding CHÍNH XÁC (KHÔNG phải "≥1 ULTRA bất kỳ"):** Worker A gắn **đúng License A** đang cấp
    token đang dùng. `acquire_fetch_leader`/`refresh_lease` kiểm bộ **`(worker_id, license_subject,
    machine/device key, site_code, project_ref)`**: `project_entitlement[site].enabled` AND `worker_id`
    đã duyệt AND **`license_entitlement[license_subject_của_token].enabled`** (đúng license đó).
  - ⇒ **Thu hồi License A (dù License B còn ULTRA)** → Worker A **mất binding → không giữ/tranh leader**;
    refresh THẤT BẠI → **DRAIN → dừng trước request kế**; Worker B (License B) tiếp quản bằng token B.
  - ⚠️ **Phân biệt PAUSE vs DRAIN-STOP** (xem `datahub-worker-lifecycle.md`):
    - **Hết token JMS ⇒ PAUSE fetch, service VẪN chạy** (nhả/follower lease, `NO_VALID_TOKEN`, chờ relay).
    - **Mất binding license / project disable / mất lease ⇒ DRAIN → dừng fetch** — service vẫn RUNNING.
  - Worker gọi leader/active-token qua **`worker_gateway`** (bằng `WorkerAccessToken`); gateway giữ DB
    role `datahub_worker` và gọi private RPC — **KHÔNG phát DB password cho từng máy** (§`worker-lifecycle` §8).
  **Desktop app KHÔNG tranh leader**; **"Fallback" = máy khác chạy Worker-host** (cũng phải có entitlement).
  Contributor lẻ dùng token WebView cục bộ — ngoại lệ riêng (P0-D).
- **Chỉ leader** được đặt `jms_active_token` và **bulk-fetch** JMS. `selection_epoch` gắn nhiệm kỳ
  leader. *(Contributor lẻ là ngoại lệ, dùng token WebView cục bộ + permit — không thuộc đường leader.)*
- **Mất leader (hết heartbeat) ⇒ DRAIN**: chủ cũ phải **huỷ/kết thúc mọi request JMS đang bay** trước
  khi leader/token mới hoạt động. Fencing chỉ chặn **stale write** vào DB, **không** chặn chủ cũ tiếp
  tục gọi JMS — nên drain là bắt buộc để giữ single-session.
- **Xử lý network partition (bắt buộc):** chủ cũ có thể **mất Supabase nhưng vẫn gọi được JMS** → không
  ai ra lệnh drain được. Vì vậy:
  1. **Worker fail-closed**: refresh `fetch_leader` lease thất bại (timeout/lỗi) ⇒ **tự dừng gọi JMS
     ngay**, không chờ lệnh.
  2. **Kiểm lease trước mỗi batch/mỗi request** (không chỉ đầu chu kỳ).
  3. **Hard timeout** cho mỗi request JMS (chặn trên).
  4. **Leader mới chờ** `lease_expiry + max_request_timeout + clock_skew_margin` trước khi bắt đầu.
  - ⚠️ Nếu cần bảo đảm **tuyệt đối** single-session, **database lease không đủ** (vẫn có cửa sổ chủ cũ
    gọi JMS) → cần **một outbound gateway duy nhất** (mọi request JMS qua một chốt, chốt đó fence theo
    leader). Ghi nhận là lựa chọn hardening tối đa (P0-D chốt có cần hay không).
- **Backoff ở mức tài khoản** (không mỗi token): JMS lỗi/nghi khoá → giãn toàn bộ fetch site, không
  dồn token khác ngay.
- Đổi token active chỉ khi token hiện tại **thực sự hỏng** (qua two-strike), không xoay vô cớ.

---

## 8. Bảo mật (token LOCAL — không ciphertext cloud)

- **Token mã hoá DPAPI (LocalMachine) trong `tokens.dat`** trên máy sở hữu (xem `datahub-worker-lifecycle.md`
  §3); **KHÔNG** lên Supabase. **BỎ** crypto envelope cloud / `recipient_key_id` / signed worker-key
  manifest (không còn quan hệ mã-hoá-cho-Worker-khác).
- **Registry (metadata) đặt schema `private`**, **REVOKE direct SELECT khỏi TẤT CẢ**; đọc/ghi **chỉ qua
  RPC fenced** (kiểm `fence/leader_term/entitlement`). Lộ DB **không lộ token** (token chỉ ở local).
- **Phân quyền RPC theo role:**
  - **`datahub_worker`** (trong `worker_gateway`): `acquire_fetch_leader`, `set_active_token`,
    `clear_active_token`, **`clear_active_and_release_leader`**, `mark_candidate_state`, append+project, lease.
  - **`datahub_edge`**: `relay_binding` (metadata), `heartbeat`, `client_contribute`,
    `acquire_contributor_permit` (wrapper desktop). **KHÔNG** điều khiển leader.
  - **`consume_site_budget` = private** (Worker/Edge nội bộ); desktop chỉ gọi wrapper permit. Bucket chung
    bulk+probe+contributor, fail-closed.
- Log token `first4…last4`; `token_fp` là hash. Xoay token = relay token mới qua Named Pipe (local).

---

## 9. Phụ thuộc spike token-binding (P0-E / G1) — quyết định branch

Spike **G1a** (dùng token từ **Windows Service khác process nhưng CÙNG máy**) là **gate bắt buộc** (Worker
Service phải dùng được token do AutoJMS relay). **G1b** (cross-machine cùng LAN) chỉ còn **test tham khảo**
(mô hình local mỗi máy 1 token/JMS account nên không cần token chạy máy khác).

**Nhánh A — G1a PASS (token dùng được từ process khác cùng máy) — MẶC ĐỊNH:**
- Worker Service dùng token local (`tokens.dat`) do AutoJMS relay qua Named Pipe. Mỗi máy 1 Worker + token
  riêng; failover = máy khác (Worker-host) giành leader dùng **token của máy đó**. Không cloud multi-key.

**Nhánh B — G1a FAIL (token ràng buộc process/thiết bị) — chỉ khi spike đòi:**
- Fetch phải chạy **trên process/máy sở hữu** → desktop làm **fetch-proxy**. Proxy cần **subsystem riêng**:
  `device_key_registry` + `proxy_command`(nonce/command-id/deadline/payload-hash + replay) + lifecycle;
  lệnh/kết quả **ký device credential** (`worker_id` định danh không đủ), mang `leader_fencing_token`+`selection_epoch`,
  fail-closed khi mất lease. `candidate_state` keyed `(site_code, worker_id, token_fp)`.

→ **Chốt nhánh sau spike G1a.** Schema §3 đủ trường (`worker_id`, `token_fp`, `license_subject`,
`session_id`, `generation`) cho cả hai nhánh, không phải đổi schema. (Token JMS **LOCAL** — KHÔNG có
cột ciphertext nào trên cloud.)

---

## 10. Việc cần chốt (đưa vào P0-D/P0-E)

- [ ] Spike (P0-E): token dùng được cross-machine cùng IP LAN không? cần cookie/UA kèm không? → nhánh A/B.
- [ ] `session_id` định danh (machine GUID + profile) ổn định, không sinh rác.
- [ ] Ngưỡng probe + **site-wide rate limit** (đo cùng P0-E) để không chạm giới hạn JMS.
- [ ] Active token gắn **`leader_fencing_token`/`leader_term`** qua **RPC CAS** (một identity fetch).
- [ ] Chính sách thu hồi ứng viên `invalid` + tần suất heartbeat từ desktop.
- [ ] **Hợp đồng Edge endpoints** (desktop chỉ gọi cái này): `relay_binding`, `heartbeat`,
      `client_contribute`, **`acquire_contributor_permit`** (wrapper). **Private RPC** (DB role
      `datahub_worker`): `acquire_fetch_leader`, `acquire_scope_lease`, `set_active_token(cas theo fence)`,
      **`clear_active_and_release_leader(cas theo fence)`**, `mark_candidate_state`; **`consume_site_budget`
      = private** (Worker/Edge nội bộ, KHÔNG desktop).
- [ ] **Một atomic bucket duy nhất** cho **bulk + probe + contributor** (đếm chung trần site);
      **fail-closed** khi Edge/DB lỗi (không cấp permit ⇒ không gọi JMS).
- [ ] Xoay token = relay token mới qua Named Pipe (local `tokens.dat`); thay máy Worker = enroll WorkerAccessToken mới.
