# DataHub — Phân tích 3 phương án triển khai (dựa trên request profile của TrackingService)

> **Mục đích:** chọn nơi đặt DataHub (VPS / máy client / VPS-tự-fetch) **dựa trên số liệu đo thật** từ
> đường fetch hiện có, không dựa cảm tính. Kết luận ở §6.

---

## 1. Request profile ĐO TỪ CODE (dữ kiện, không phỏng đoán)

| # | Đặc tính | Giá trị | Nguồn |
|---|---|---|---|
| 1 | Endpoint tracking (bulk) | `POST operatingplatform/podTracking/inner/query/keywordList`, payload `{keywordList[], trackingTypeEnum:"WAYBILL", countryId:"1"}` | `WaybillTrackingService.cs:458`, `FullStackTrackingEnrichmentService.cs:213` |
| 2 | **Batch size** | **40 waybill / 1 request** (`BatchSize = 40`) | `WaybillTrackingService.cs:39`, `FullStackTrackingEnrichmentService.cs:16` |
| 3 | Parallelism của caller | **8** (`MaxConcurrency` / `MaxDegreeOfParallelism`) | `WaybillTrackingService.cs:40`, `FullStackTrackingEnrichmentService.cs:19` |
| 4 | **Governor toàn app** | **12 in-flight** cho MỌI JMS POST (`MaxConcurrentJmsRequests`) — chặn khoá IP | `JmsApiClient.cs:35` |
| 5 | Endpoint detail | `POST operatingplatform/order/getOrderDetail` — **1 request / 1 waybill** (đắt) | `WaybillTrackingService.cs:485`, `FullStackTrackingEnrichmentService.cs:263` |
| 6 | **Detail KHÔNG fetch nền nữa** | Background sync **chỉ Stage 1 (tracking)**; detail **fetch on-demand khi user double-click**, rồi persist; merge **giữ cache** | `FullStackTrackingEnrichmentService.cs:75-82`, `FullStackOperation.WaybillDetail.cs`, `FullStackWaybillRepository.cs:576,596` |
| 7 | Nhịp theo từng đơn | `tracking_interval_mins` **default 30 phút** + `next_track_at` (scheduler theo hàng, không quét toàn bảng liên tục) | `FullStackTrackingEnrichmentService.cs:318,371`, `FullStackWaybillRepository.cs:725` |
| 8 | Auth | `authToken` + header hằng; **401 → refresh token 1 lần + retry** (không có refresh endpoint thật) | `JmsApiClient.cs:55-58`, `WaybillTrackingService.cs:882` |
| 9 | Trạng thái cuối cần loại | `快件签收`→**"Ký nhận CPN"**, `退件签收`→**"Ký nhận chuyển hoàn"**; code nhiều nơi đã loại khỏi theo dõi | `WaybillTrackingService.cs:934-935`, `FullStackOperation.cs:724,828,885` |
| 10 | Timeout | 60s / request | `JmsApiClient.cs:49` |

### Load math (suy ra từ #2, #3, #7)

`requests_per_cycle = ceil(N / 40)`, chu kỳ 30 phút:

| N (đơn tồn) | Request / chu kỳ 30' | Trung bình req/phút | So với governor 12 concurrent |
|---:|---:|---:|---|
| 500 | 13 | 0,4 | không đáng kể |
| 2.000 | 50 | 1,7 | không đáng kể |
| 5.000 | 125 | 4,2 | thoải mái |
| 10.000 | 250 | 8,3 | vẫn dưới trần |

**Backfill `getOrderDetail` 1 lần/ngày** (nếu muốn đầy cột detail): N request, ở concurrency ≤12, ~0,3s/req
⇒ N=2.000 ≈ **50 giây**; N=10.000 ≈ **4 phút**. → **rẻ**, hoàn toàn đặt được vào ca thấp điểm.

---

## 2. Năm hệ quả kiến trúc rút ra từ số liệu

1. **Tải fetch RẤT NHỎ.** Bottleneck không phải throughput JMS ⇒ **"ai fetch" là quyết định an toàn/vận
   hành, KHÔNG phải quyết định hiệu năng.** Cả 3 phương án đều "đủ nhanh".
2. **Lý do cần leader/lease không phải vì tải, mà vì trùng lặp.** M máy ULTRA fetch độc lập ⇒ M× request
   trùng + M× nguy cơ khoá IP + M× ghi xung đột. Một leader là đủ (và cần).
3. **Mô hình "client là contributor" ĐÃ tồn tại trong code**: detail chỉ được lấy khi user mở đơn rồi
   ghi lại DB (#6). Vậy PA nào cũng phải hỗ trợ *hai* nguồn ghi (leader nền + client theo tương tác) —
   đúng hợp đồng "hai producer, một canonical writer" đã chốt.
4. **`getOrderDetail` là dữ liệu tĩnh, đắt theo đơn** ⇒ đúng như nhận định: **1 lần/đơn** là đủ (on-demand
   + backfill đêm), **không** đưa vào chu kỳ 30 phút.
5. **AuthToken là ràng buộc cứng duy nhất:** chỉ WinForms+WebView2 sinh được; không có refresh endpoint
   (#8). Mọi phương án phải có **≥1 UI mở**; khác biệt chỉ là **token đi tới đâu**.

---

## 3. Phương án 1 — VPS hub (Postgres+Gateway) + Windows Service fetch ở client

**Logic:** tách **stateful** (DB, cố định ở VPS) khỏi **stateless** (fetch, float theo lease giữa các máy
ULTRA). Token **không rời máy**: UI → Named Pipe → Service cùng máy → gọi JMS bằng IP bưu cục → đẩy kết
quả lên Gateway (HTTPS).

**Phương pháp triển khai**
- **Fetch leader/lease** như đã chốt: lease theo site + `leader_fencing_token`; máy tắt/hết token ⇒
  bounded-flush → `clear_active_and_release_leader` (atomic) → máy khác đoạt. Không cần thay đổi gì.
- **Realtime:** canonical writer (PG function) ghi `event+projection+receipt+datahub_changes` +
  `pg_notify(site_code, high_watermark)` **trong một transaction** → Gateway `LISTEN` → **SignalR** group
  `site:{code}` → client delta-pull `(change_seq, stable_id)` → SQLite. Độ trễ thực tế **<500ms** (xem §5).
- **"Mỗi bưu cục như một folder riêng"** = chọn 1 trong 2, **không** cần nhiều DB instance:
  - **row-level `site_code` + RLS** (đã chốt v4) — đơn giản nhất, migration một lần cho mọi site;
  - **schema-per-tenant** — isolation mạnh hơn, `pg_dump` từng bưu cục dễ, nhưng migration phải chạy vòng
    qua từng schema (drift risk). *Khuyến nghị: row-level trước, schema-per-tenant chỉ khi có yêu cầu
    pháp lý/isolation cụ thể.*
- Thêm bưu cục = **1 hàng `site` + license `dataHub.enabled`** (không dựng hạ tầng mới).

**Ưu**
- Token **không lên cloud** (giữ nguyên nguyên tắc bảo mật đã ký) và JMS vẫn thấy **IP bưu cục**.
- Dữ liệu tồn tại độc lập với máy nhân viên: **tắt hết máy vẫn còn dữ liệu**; backup/PITR một chỗ.
- Failover fetch là **stateless** ⇒ đơn giản, không có split-brain.
- Khớp 100% hợp đồng P0 hiện tại (không phải thiết kế lại).

**Nhược / rủi ro**
- Cần **VPS** (chi phí + phụ thuộc đường Internet ra ngoài).
- Mất Internet ⇒ mất realtime; **giảm nhẹ:** SQLite mirror + outbox đã có (UI vẫn xem/ghi được, đồng bộ
  lại sau) — nhưng các máy **không thấy nhau** trong lúc mất mạng.
- VPS đơn node = **SPOF**; cần backup + (tuỳ) standby.

---

## 4. Phương án 2 — DataHub (Postgres+SignalR) đặt trên máy client

**⚠️ Lỗi thiết kế cần chỉ rõ trước:** phương án như mô tả **trộn hai vai khác bản chất**:
- **fetch leader** = *stateless* → float giữa máy rất dễ;
- **DB host** = *stateful* → **float rất khó**.

Nếu "mỗi máy đều có folder DataHub" **và** hub di chuyển theo leader thì khi máy leader tắt:
- Dữ liệu **rebuild được từ JMS** (tracking/inventory/detail) → OK, refetch là xong;
- Nhưng **workflow do người nhập (notes / checks / dispatch_tasks) KHÔNG rebuild được** → máy mới **thiếu**
  dữ liệu đó ⇒ **mất dữ liệu nghiệp vụ** (hoặc phải multi-master replication ⇒ split-brain, quá phức tạp).

⇒ PA2 chỉ an toàn ở **biến thể (a)**:

**PA2(a) — hub LAN cố định (khuyến nghị nếu bỏ VPS):** chọn **một máy trong bưu cục làm hub** (Postgres +
Gateway/SignalR chạy ở đó, tốt nhất là máy văn phòng luôn bật/UPS). Các máy khác **chỉ là client**.
**Fetch leader vẫn float** giữa các máy ULTRA và tất cả đẩy về hub LAN.
- Triển khai: **giống hệt PA1**, chỉ khác `gatewayUrl` trỏ IP LAN thay vì domain VPS ⇒ **cùng một mã nguồn,
  hai chế độ triển khai** (xem §6 Hybrid).
- Ưu: **không tốn VPS**; latency LAN ~1ms; dữ liệu không ra ngoài.
- Nhược/rủi ro: máy hub phải **luôn bật**; **không ai backup** (phải tự cấu hình job + đem file ra ngoài);
  Postgres trên máy nhân viên (firewall/antivirus/Windows Update/người dùng tự tắt/format); mất máy hub =
  **mất toàn bộ workflow data của bưu cục**; nâng cấp schema phải làm tại từng bưu cục.

**PA2(b) — hub float theo leader:** **KHÔNG khuyến nghị** (mất workflow data như trên) trừ khi chấp nhận
"workflow data là ephemeral" — trái với việc `order_notes/checks/tasks` là dữ liệu người dùng.

**Về badge "Leader"** (title app / nút Đồng bộ): hợp lý và nên làm — nhưng phải nói rõ badge chỉ ám chỉ
**fetch leader**. Ở PA2 nếu trộn vai, người dùng sẽ hiểu sai là "máy tôi đang giữ database" và tắt máy vô tư.

---

## 5. Phương án 3 — VPS hub TỰ fetch JMS, client chỉ viewer + workflow writer

**Logic:** bỏ Windows Service fetch ở client; client chỉ đẩy **token** lên hub; hub gọi JMS.

**Ưu**
- Client **mỏng nhất**: không cài Service, không Named Pipe fetch-pipeline, không Postgres ở client.
- Rate-limit/điều tiết tập trung, dễ quan sát.
- "Máy khác gánh cấp token" thay cho "máy khác gánh fetch" — logic leader **đơn giản hơn** (chỉ chọn
  nguồn token, không cần fence request đang bay).

**Nhược / rủi ro (nặng)**
1. 🔴 **Token JMS phải lên cloud** → phá nguyên tắc "token không rời máy" đã ký; hub giữ token của **nhiều
   bưu cục** ⇒ mục tiêu tấn công giá trị cao (một lần xâm nhập = mất token toàn hệ).
2. 🔴 **JMS sẽ thấy request đến từ IP VPS**, không phải IP bưu cục. Code hiện tại cố tình mô phỏng browser
   (UA/Origin/Referer) + đi từ IP bưu cục; đổi sang IP datacenter là **rủi ro bị coi bất thường/khoá** —
   **phải spike xác nhận trước khi cam kết** (đây là rủi ro nghiệp vụ, không chỉ kỹ thuật).
3. Vẫn cần **≥1 UI mở** để có token ⇒ **không** giảm phụ thuộc UI như kỳ vọng.
4. Detail on-demand (#6) hiện do client gọi — chuyển lên hub thì mỗi lần user mở đơn phải **round-trip qua
   VPS**, chậm hơn gọi trực tiếp từ LAN.

---

## 6. So sánh & khuyến nghị

| Tiêu chí | PA1 (VPS + service client fetch) | PA2(a) (hub LAN cố định) | PA3 (VPS tự fetch) |
|---|---|---|---|
| Token rời máy? | **Không** ✅ | **Không** ✅ | **Có** 🔴 |
| JMS thấy IP | bưu cục ✅ | bưu cục ✅ | VPS 🔴 (cần spike) |
| Dữ liệu sống khi tắt hết máy | ✅ | ❌ (hub tắt là mất) | ✅ |
| Backup/PITR | dễ (1 chỗ) ✅ | thủ công ⚠️ | dễ ✅ |
| Chi phí hạ tầng | VPS ⚠️ | **0** ✅ | VPS ⚠️ |
| Latency | ~100–500ms ✅ | ~1–50ms ✅ | ~100–500ms ✅ |
| Phức tạp client | Service + pipe ⚠️ | Service + pipe + Postgres ở 1 máy ⚠️ | **mỏng nhất** ✅ |
| Khớp hợp đồng P0 đã ký | **100%** ✅ | 100% (chỉ đổi URL) ✅ | phải sửa security posture 🔴 |
| Blast radius xấu nhất | mất VPS (có backup) | **mất workflow data 1 bưu cục** 🔴 | **mất token nhiều bưu cục** 🔴 |

### Khuyến nghị

1. **Chọn PA1 làm mặc định** — cân bằng nhất, và **không phải thiết kế lại** (đúng v4 đã chốt).
2. **Không chọn PA2(b)** (hub float theo leader) — mất dữ liệu workflow.
3. **HYBRID (điểm mạnh nên khai thác):** vì Gateway là **ASP.NET Core** nên **PA1 và PA2(a) là CÙNG một mã
   nguồn, khác *deployment mode*** — chỉ khác `gatewayUrl` + nơi cài Postgres:
   - `DeploymentMode = Cloud` → Gateway+PG trên VPS (mặc định, nhiều bưu cục, RLS `site_code`).
   - `DeploymentMode = OnPrem` → Gateway+PG trên 1 máy LAN của bưu cục (khách không muốn VPS).
   ⇒ **Không phải chọn cứng ngay bây giờ.** Xây theo PA1; bưu cục nào từ chối VPS thì bật OnPrem.
4. **PA3 chỉ mở khi** owner ký chấp nhận (i) token lên cloud và (ii) spike chứng minh JMS không phạt IP
   datacenter. Nếu vẫn muốn client mỏng, có thể giữ PA1 nhưng **đóng gói Service vào installer** để người
   dùng không phải làm gì.

---

## 7. Chi tiết triển khai áp dụng cho cả PA1/PA2(a)

### 7.1 LISTEN/NOTIFY — 3 cạm bẫy phải xử lý
- ⚠️ **Không dùng transaction-mode pooler cho listener.** PgBouncer ở transaction mode **làm mất
  LISTEN/NOTIFY**. Gateway phải giữ **một connection dedicated (session mode)** để `LISTEN`, tách khỏi
  pool dùng cho query/write.
- ⚠️ **Payload ≤ ~8000 bytes** ⇒ chỉ gửi **`high_watermark`** (đúng như đã chốt: không gửi row).
- ⚠️ **Notification mất khi Gateway restart** ⇒ bắt buộc **catch-up delta-pull khi (re)connect** +
  **safety pull 30–60s** (đã có trong hợp đồng).

### 7.2 Ngân sách độ trễ (mục tiêu owner: <2s)
| Chặng | Thời gian |
|---|---|
| Writer commit + `pg_notify` | ~1–10ms |
| Gateway `LISTEN` nhận | ~1–5ms |
| SignalR push tới client | LAN ~1–5ms / VPS(VN) ~10–40ms |
| Client debounce (gộp doorbell) | 200–500ms *(cấu hình)* |
| Delta-pull + apply SQLite | ~20–100ms |
| **Tổng** | **~250–650ms** ⇒ đạt <2s với biên rộng |

### 7.3 Ca đêm 00:00–05:00 (maintenance) — đúng ý owner, nhưng 2 điều kiện
- Purge đơn đã **"Ký nhận CPN" / "Ký nhận chuyển hoàn"** (trạng thái cuối, #9) để DB không phình.
- 🔴 **KHÔNG hard-delete:** phải ghi **tombstone vào `datahub_changes`** (`operation='delete'`) trong cùng
  transaction — nếu xoá thẳng, **client offline sẽ giữ đơn đó vĩnh viễn**. Retention tombstone **≥ cửa sổ
  offline dài nhất** (đề xuất 30–90 ngày).
- **Archive thay vì xoá hẳn:** chuyển sang bảng lịch sử (hoặc export) để còn đối chiếu/thống kê; chỉ xoá
  khỏi bảng "đang theo dõi".
- Chạy bằng **`.NET BackgroundService`** trong Gateway (thay `pg_cron`).
- Ca đêm cũng là chỗ đặt **backfill `getOrderDetail`** (§1: 50s–4 phút) và **full-scan đối chiếu** cho G0
  (2 scan cùng full-set hash mới cho phép mark-left).

### 7.4 Token / lease — giữ đúng hợp đồng đã ký
- "WebView2 luôn mở ⇒ token luôn hợp lệ" là **giả định**, không phải bảo đảm: token vẫn có thể bị vô hiệu
  (logout, đăng nhập cùng account ở máy khác, đổi ca). ⇒ giữ **probe 4 kết quả**
  (`Valid/AuthRejected/Transient/SiteMismatch`) + **two-strike**; **lỗi mạng ≠ token invalid**.
- **`SiteMismatch` đặc biệt quan trọng ở mô hình shared/nhiều bưu cục:** token hợp lệ của **bưu cục khác**
  vẫn trả `code=1` ⇒ phải đối chiếu `principal`/`actionSiteCode` với site của license mới cho `Valid`.
- Thứ tự khi hết token: **ngừng JMS → bounded flush dưới fence còn hiệu lực → clear+release leader
  (atomic) → không flush bằng fence cũ**.
- **Badge "Leader"**: nên hiển thị (title `AutoJMS - … Realtime-Leader` hoặc badge ở nút Đồng bộ) + tooltip
  ghi rõ *"máy này đang chạy fetch nền"*; **không** ngụ ý máy này giữ database.

### 7.5 Hai điều kiện độc lập (đang sai trong code — phải sửa)
- **License DataHub hợp lệ ⇒ được ĐỌC** (Gateway/SignalR/delta-pull) — *không* phụ thuộc JMS token.
- **JMS token hợp lệ ⇒ mới được FETCH** (Worker) / contribute.
- Hiện `FullStackOperation.cs` (~122) gộp hai điều kiện (DataHub chỉ khởi động khi có JMS token) và
  desktop vẫn giành lease + bulk-fetch (~488) ⇒ phải tách và chuyển lease hoàn toàn sang Service.

---

## 8. Việc phải làm tiếp (không đổi so với P0 hiện tại)

1. **G0** hoàn tất (full-set hash 2-scan + failure-injection test) — *độc lập phương án*.
2. **Spike G1a**: token lấy ở UI process có dùng được từ **Windows Service (tài khoản khác)** cùng máy —
   **bắt buộc cho PA1/PA2**; nếu FAIL → fetch phải chạy trong process UI (đổi mô hình).
3. **Spike SignalR + LISTEN/NOTIFY**: reconnect/JWT refresh/catch-up + dedicated listener connection.
4. *(Chỉ nếu cân nhắc PA3)* **Spike IP**: JMS có phạt request từ IP datacenter không.
5. Chốt `DeploymentMode` (Cloud/OnPrem) làm **cấu hình**, không phải hai nhánh mã.
