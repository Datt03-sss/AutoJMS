# AutoJMS DataHub — Kế hoạch triển khai tổng

> **🟢 QUYẾT ĐỊNH KIẾN TRÚC v4 (owner chốt — THAY TOÀN BỘ SUPABASE DATAHUB):** FullStackForm dùng
> **PostgreSQL shared cluster + ASP.NET Core DataHub Gateway (host SignalR)** — **KHÔNG Supabase DataHub,
> KHÔNG Redis, KHÔNG ClickHouse.** Giữ nguyên: **Firebase license**, **Windows Service**, **SQLite local**
> (cache/outbox/cursor/offline), **backup**. *(Supabase của module/update CHUNG của AutoJMS là hệ KHÁC,
> giữ nguyên — không đụng.)*
>
> **Bảng thay thế (Supabase DataHub → stack mới):**
> | Supabase (bỏ) | Thay bằng |
> |---|---|
> | Postgres project-per-site | **PostgreSQL shared cluster** (mọi bảng/index có `site_code`, shard sau) |
> | PostgREST / RPC | **ASP.NET Core API + Npgsql/Dapper + PostgreSQL functions** |
> | Realtime Postgres Changes | **`LISTEN/NOTIFY` + SignalR** (group `site:{site_code}`) |
> | Edge Functions | **DataHub Gateway endpoints** |
> | Supabase DataHub JWT | **JWT do license server/Firebase cấp** (Gateway verify) |
> | RLS claims (JWT) | **PostgreSQL RLS + `SET LOCAL app.site_code` (Gateway đặt) + `FORCE ROW LEVEL SECURITY`** |
> | `pg_cron` | **`.NET BackgroundService`** / scheduler OS |
> | Management API | **Migration runner / provisioning theo cluster** |
>
> **📌 MÔ TẢ KIẾN TRÚC (câu chốt):** **SignalR là kênh BÁO thay đổi (doorbell), KHÔNG phải nguồn phát lại
> bền vững** (không gửi cả row qua SignalR, không dùng làm durable queue). Windows Service **luôn tồn tại**;
> token **relay cục bộ qua Named Pipe**, **chỉ Service lưu DPAPI**. **Worker + contributor gửi observation
> tới Gateway → PostgreSQL ghi `event+projection+receipt+datahub_changes` trong MỘT transaction → cùng txn
> `pg_notify(site_code, high_watermark)` (chỉ phát sau commit)** → Gateway đẩy SignalR tới group site →
> FullStackForm coi SignalR là doorbell rồi **HTTPS delta-pull theo `(change_seq, stable_id)`** cập nhật
> SQLite/UI. **Desktop KHÔNG BAO GIỜ nhận PostgreSQL credential.** Token JMS **không expiry/refresh** — chỉ biết qua probe.

> **✅ Hợp đồng CARRY-OVER (portable, giữ nguyên qua switch):** unified **`datahub_changes`** feed, **event
> immutable + receipt** (`request_event_id→canonical_event_id`), **cursor `(change_seq, stable_id)` +
> pull-side UPSERT**, **canonical writer một-transaction**, **token model local DPAPI/Named Pipe + leader/
> fence + site attestation**, **G0 inventory integrity**, entitlement per-license + dead-man. Chỉ lớp
> **coupled** đổi (Realtime→SignalR, Edge→Gateway, PostgREST direct-SELECT→**Gateway delta-pull API**,
> project-per-site→**shared cluster + RLS context**, JWKS/jose→license-JWT verify tại Gateway,
> pg_cron→BackgroundService, Management API→migration runner).

> **Trạng thái: Draft v4 (PostgreSQL+Gateway/SignalR) — BLOCKED BY P0 CLOSURE.** Kiến trúc **đã duyệt
> (9/10)**; **mở spike/P0**, **CHƯA production**. **Không mở P1/P2 tới khi P0 đóng theo stack mới.**
> Chi tiết: `datahub-p0-contract.md` (contract), `datahub-token-pool-plan.md`, `datahub-worker-lifecycle.md`,
> `event-sourcing-lite.md`.

---

## 0. Đã triển khai một phần — CHƯA đạt hợp đồng mới (không phải "done")

- **Hybrid local-first + Supabase (`202607110001`)**: SQLite read-store; delta-pull + realtime
  doorbell; outbox; per-site `site_code`; lease RPC. ⚠️ **RLS CHƯA cách ly thật**: policy cho JWT
  không có `site_code` đọc toàn bộ và cấp RPC `SECURITY DEFINER` cho `anon`
  (`202607110001_hybrid_sync.sql:128–129`) ⇒ client có anon key **bypass được**. Đây là **defect
  bảo mật phải sửa ở P0-A**, không để tới Phase 4.
- **Event-sourcing-lite (`202607110002`, cờ `EventPipelineEnabled` mặc định TẮT)**: `fs_events` +
  event store `waybill_events`. ⚠️ **Fingerprint CHƯA dedupe đúng** (`eventTime` luôn vào hash, còn
  `OrderDetailObserved`/inventory/workflow dùng `DateTime.UtcNow`) ⇒ mỗi lần fetch tạo fingerprint
  mới; **cursor event** `MAX(remote_seq)` kẹt khi `INSERT OR IGNORE` bỏ qua dòng trùng. Sửa ở P0-B/C.
- `append_waybill_events` cấp cho `anon` (`202607110002_event_store.sql:130`) ⇒ client giả được cả
  `InventoryLeft`/event. Sửa ở P0-A.

⇒ Nền này **giữ làm điểm xuất phát nhưng phải qua P0 hardening** trước khi bật dữ liệu thật. Emitters
đang tắt nên chưa có tác động sản xuất.

---

## 1. Mục tiêu

1. DB tách riêng có cơ chế **tự fetch** JMS liên tục.
2. **PostgreSQL shared cluster** cách ly theo `site_code` bằng **RLS** (KHÔNG còn project-per-site; shard sau nếu cần).
3. AutoJMS **chỉ đọc** qua **Gateway delta-pull + SignalR doorbell** (KHÔNG nhận PG credential).
4. Client vừa **consumer** vừa **contributor** (tự gọi JMS khi thao tác, đẩy lên qua Gateway nếu mới hơn).

---

## 2. Quyết định đã chốt (nội dung cần duyệt)

| # | Quyết định | Chốt |
|---|---|---|
| 1 | Nơi gọi JMS | **.NET Worker (Windows Service) trong LAN bưu cục** |
| 2 | Cách ly DB | **PostgreSQL SHARED cluster** — cách ly theo `site_code` bằng **RLS + `FORCE ROW LEVEL SECURITY`**; **mọi bảng/index có `site_code`**; shard sau nếu cần. (Bỏ project-per-site Supabase → hết per-project provisioning/economics/PITR.) |
| 3 | Điều phối cloud | **ASP.NET Core DataHub Gateway** (xác thực JWT, đặt RLS context, gọi PG functions, host SignalR) + **`.NET BackgroundService`** cho maintenance/health/stale (thay `pg_cron`). Gateway **không** đặt lịch fetch (Worker sở hữu lịch) |
| 4 | Mô hình gọi | **PULL**: Worker tự pull `datahub_control` (qua Gateway); **Gateway/PG không gọi ngược vào LAN** |
| 5 | Lịch fetch | **Worker sở hữu** HOT/WARM/COLD + adaptive (nguồn quyết định duy nhất); control-plane chỉ cấp policy |
| 6 | Code chung | Tách **`AutoJMS.Fetch.Core`** (class library, không dính WinForms) |
| 7 | Chống double-fetch | **Site-level `fetch_leader` lease** là gốc; scope lease (`site_code+fetch_scope`) là con của cùng leader; fencing token. Mất leader → **drain/huỷ toàn bộ request JMS đang bay** trước khi leader/token mới hoạt động (P0-D) |
| 8 | Token — dùng | **Token pool**, một **active pointer/site** gắn `selection_epoch`; chỉ **fetch_leader** được đặt active + gọi JMS (không phải per-scope) |
| 9 | Token — vòng đời | ⚠️ **JMS KHÔNG cấp expiry/RefreshToken** (đã kiểm code: `ForceRefreshFromWebViewAsync` chỉ đọc lại authToken). *Giả định vận hành*: dài hạn theo ngày, mất khi logout → pool-only, qua đêm tạm dừng. Hợp lệ **chỉ biết qua probe** (không có trường "còn hạn"). **Cần spike P0-E xác nhận** (chưa phải quyết định cuối) |
| 10 | Token — bảo mật | **Token LOCAL**: mã hoá **DPAPI** trong `tokens.dat` trên máy sở hữu; AutoJMS→Worker cùng máy qua **Named Pipe**; **PostgreSQL/cloud KHÔNG giữ ciphertext JMS** (chỉ `token_fp`/binding). Bỏ mã-hoá-cho-Worker-khác/cloud relay |
| 11 | Request hợp lệ | **Đã kiểm chứng code**: chỉ `authToken` + header hằng, KHÔNG cookie/thiết bị. `cap 12` = **concurrency của một process**, KHÔNG phải rate-limit toàn site → **cần thêm site-wide rate limit** (P0-E) |
| 12 | Mô hình thời gian | 4 mốc: ưu tiên `source_event_at` → `received_at` → `seq`; không tin đồng hồ client |
| 13 | Contributor | Qua **Gateway endpoint** (thay Edge): rate-limit, `site_code` từ JWT đã verify (Gateway đặt `SET LOCAL app.site_code`), whitelist event type, **fingerprint tính lại phía server**, newest-wins có tie-break |
| 14 | Control plane + identity | **Entitlement DataHub ở FIREBASE per-license**, cổng `DataHubAllowed = active AND **tier==ULTRA** AND dataHub.enabled AND normalize(middleCode)==normalize(siteCode)` (kiểm server-side). Chỉ **ULTRA** nhận `dataHub` block (BASE/ULTRA-chưa-gán = fail-closed). Render → phát **DataHub JWT (license server/Firebase)** (RS256, `site_code`, scopes, `entitlement_version`, TTL ngắn). **Firebase config ULTRA chỉ gồm** `enabled, gatewayUrl, clusterId, siteCode, scopes` — **KHÔNG** DB key/secret/projectUrl. **Gateway** verify JWT + đặt `SET LOCAL app.site_code` cho RLS. **P0-A** |
| 15 | Projection owner | **CHỐT: một canonical writer = Gateway gọi PG function ghi `event+projection+receipt+datahub_changes` + `pg_notify` trong CÙNG transaction**. Cả Worker lẫn contributor đi qua Gateway → cùng writer. KHÔNG merge row + append event ở 2 thao tác rời (P0-B) |
| 16 | Fallback | **KHÔNG có "desktop app bulk-fetch".** Fallback = máy khác chạy **Worker-host** (identity/key riêng) giành `fetch_leader`; `FetchMode = Worker/Off`. Contributor lẻ (token WebView cục bộ) là ngoại lệ riêng, không bulk |
| 17 | Worker lifecycle | **Windows Service LUÔN chạy** (Automatic Delayed Start + auto-restart); AutoJMS **không sở hữu** vòng đời (relay token qua Named Pipe, đóng app không stop service); **persistent token store** (DPAPI, `%ProgramData%`) sống qua đóng app/reboot; **hết token = PAUSE fetch** (service chạy) chứ KHÔNG terminate; revoke/disable/mất-lease = DRAIN-STOP. Worker có **`WorkerAccessToken`** riêng. Chi tiết `datahub-worker-lifecycle.md` |
| 18 | DB credential | **KHÔNG phát PostgreSQL credential cho bất kỳ máy nào** (Worker lẫn desktop). Worker → **ASP.NET Core Gateway** (HTTPS + `WorkerAccessToken`); **chỉ Gateway giữ PG connection** (pooling, `SET LOCAL app.site_code` trong txn). JMS request vẫn từ IP bưu cục. |
| 19 | **Backend = PostgreSQL + Gateway (Supabase DataHub ĐÃ BỎ)** | Concrete backend: **PostgreSQL shared cluster + ASP.NET Core Gateway/SignalR**. Seam **`IDataHubClient`** (HTTP delta/write + SignalR subscription) ở desktop; Worker gọi Gateway HTTPS. **KHÔNG** dựng framework đa-provider tổng quát (chỉ interface + 1 adapter hiện tại — theo khuyến nghị review). |

---

## 2b. Backend portability — PORTABLE vs (đã) SUPABASE-COUPLED (Quyết định #19)

> ✅ **Switch ĐÃ thực hiện:** cột "SUPABASE-COUPLED" bên dưới **đã được thay** bằng PostgreSQL + Gateway/
> SignalR (xem banner v4 + bảng mapping đầu tài liệu). Mục này giữ để thấy **ranh giới**: phần PORTABLE
> carry-over nguyên vẹn; phần coupled đổi adapter. **KHÔNG** dựng đa-provider tổng quát — chỉ interface + adapter Postgres.

> **Mục tiêu owner:** đổi dịch vụ lưu trữ DB **nhanh**, không viết lại lõi. Cách làm: mọi truy cập backend
> đi qua **interface provider**; Supabase là **một implementation**. Đổi backend = viết adapter mới + đổi config.

**PORTABLE (giữ nguyên khi đổi backend — là hợp đồng lõi):**
- Mô hình dữ liệu: `waybills` + field-group (P0-B), event log **immutable** + `canonical_event_id`/receipt + unified `datahub_changes` feed,
  snapshot store `(leader_term, snapshot_seq)`, workflow tables.
- **Cursor `(change_seq, stable_id)`** + pull-side **UPSERT** (P0-C) — thuần SQL/logic, không phụ thuộc vendor.
- Event-sourcing-lite, fingerprint/dedup, projection-owner một-writer.
- Worker fetch (`AutoJMS.Fetch.Core`), token LOCAL/Named Pipe, leader/fence, outbox.
- Entitlement model (license/project/session + dead-man `valid_until`) — logic, không phụ thuộc RLS cụ thể.

**SUPABASE-COUPLED (thay khi đổi backend — cô lập sau interface):**
- **RLS/policy** (Postgres thuần cũng có; backend khác có thể phải thay bằng tầng authorization ứng dụng).
- **Realtime** (doorbell) → interface `IDataHubRealtime` (có thể thay bằng LISTEN/NOTIFY, SignalR, polling).
- **Edge Functions** (write gateway/contributor) → interface `IDataHubWriteGateway` (có thể thay bằng
  API service/RPC gateway tự host).
- **PostgREST/`supabase-csharp`** (đọc trực tiếp) → interface `IDataHubStore` (có thể thay bằng Npgsql/REST khác).
- **pg_cron** (maintenance) → scheduler bất kỳ (Quartz trong Worker, cron hệ điều hành…).
- **GoTrue/JWKS/JWT mint** → interface `IDataHubIdentity`.
- **Provisioning (Management API)** → adapter riêng mỗi vendor.

**Ghi chú thực thi:** seam đặt ở P0/P1 (interface hoá cùng lúc tách `Fetch.Core`); Supabase adapter là mặc
định. **KHÔNG** để RLS/Edge/Realtime rò rỉ ra lõi. Chi phí đổi vendor = số adapter ở nhóm COUPLED, không đụng PORTABLE.

## 3. Kiến trúc cuối (tóm tắt)

```
JMS API ──(authToken + header hằng, IP bưu cục)── AutoJMS.DataHub.Worker (Windows Service, LAN)
   ▲ token (Named Pipe, LOCAL DPAPI)                 │  dùng AutoJMS.Fetch.Core · fetch_leader (fence) · governor 12 + rate site
   │                                                 │  tiered HOT/WARM/COLD · retry/backoff/CB · fail-closed
Desktop (login) ──Named Pipe (token LOCAL)──► Worker cùng máy   │
   │                                                 │ HTTPS (WorkerAccessToken)
   │ SignalR (doorbell) + HTTPS delta-pull           ▼
   └──────────────►  ASP.NET Core DataHub GATEWAY  (verify JWT · SET LOCAL app.site_code · host SignalR)
        ▲ đóng góp lẻ (Gateway: client_contribute)          │  canonical writer (1 transaction)
        │                                                   ▼
   SQLite local mirror                          PostgreSQL SHARED cluster (RLS + FORCE RLS, mọi bảng có site_code)
   (cache/outbox/cursor/offline)                  waybills · waybill_events(immutable) · receipt · datahub_changes
                                                   token BINDING metadata · leases · datahub_control/health
                                                        │ pg_notify(site_code, high_watermark) SAU commit
                                                        └────► Gateway LISTEN/NOTIFY → SignalR group site:{code}

Control plane (license server + Firebase): middle_code → gatewayUrl, clusterId, siteCode, scopes, public_key, versions.
```

---

## 4. Lộ trình thực thi

### P0 — Contract & Threat model + Spike (CỔNG bắt buộc, không code fetcher diện rộng)

Chia 5 gói phải đóng **theo thứ tự** trước khi mở P1/P2 (chi tiết: `datahub-p0-contract.md`):

- **P0-A — Identity & security boundary:** license server phát JWT/identity per-project (claim
  **top-level `site_code`**, khớp `jwt_site_code()`; TTL, revoke, key rotation); **siết RLS** + **enforce
  `entitlement_version/scopes/session` trong Supabase** (mirror **per-license** `license_entitlement` +
  `project_entitlement` + `revoked_sessions`; KHÔNG per-site `site_entitlement`);
  `REVOKE EXECUTE` **các RPC privileged/mutating** (KHÔNG revoke helper RLS — allowlist
  `jwt_site_code`/`jwt_entitlement_ok`/`jwt_has_scope` cho `authenticated`); phân định **private RPC**
  (schema `private`, DB role `datahub_worker`/`datahub_edge`, KHÔNG `service_role` API key) vs **Edge
  boundary**. → *Cổng bảo mật, xong đầu tiên.*
- **P0-B — Event schema v2 + fingerprint theo event-type + projection owner:** fingerprint riêng cho
  tracking/inventory/workflow; **`OrderDetail` = SNAPSHOT store (không fingerprint transition)**; **tính
  lại phía server**; **một writer duy nhất** cho `waybills` (append+project cùng RPC/transaction).
- **P0-C — Cursor contract:** event cursor lưu độc lập trong `fs_sync_state` sau khi xử lý xong trang;
  row cursor keyset theo **`change_seq` server** (không client `updated_at`); writer serialize/site (gap
  từ rollback OK, không chờ contiguous); kéo tới trang ngắn hơn limit; **test pagination**.
- **P0-D — Site leader + token registry + selection epoch:** `fetch_leader` lease gốc; **5 bảng**
  token (§`datahub-token-pool-plan.md`); drain khi mất leader.
- **P0-E — Spike & vận hành:** spike token IP-binding; **site-wide rate limit** (khác cap 12 process);
  provisioning qua **Supabase Management API** (+ **Data API GRANT tường minh** — breaking change
  2026) + key rotation; **Windows Service install/upgrade/rollback + ACL private key**.

**Thứ tự phase/gate (sửa vòng lặp — G4–G7 cần Worker/Edge ở P1–P4 nên KHÔNG đòi trước P1):**
```
P0.0 (RLS hardening + G0) → ký A–C + framework D/E → G1a SPIKE → chốt D/E theo nhánh → P0.5 Security/Identity
   → [G2–G3] → P1–P4 → [Integration Gate G4–G7] → P4.5 Provisioning tooling → [Operations Gate G8–G13] → P5
```
- **P0-Design** = ký duyệt hợp đồng A–E (chỉ tài liệu).
- **P0.5 Security/Identity** = **ĐƯỢC đụng migration + code**: migration forward-only hardening (RLS
  revoke PUBLIC + **FORCE ROW LEVEL SECURITY** + `SET LOCAL app.site_code` context + PG functions),
  identity adapter (**Gateway verify JWT + đặt RLS context**), desktop read → **Gateway delta-pull API**
  (không direct SELECT, không PG cred), token pool + leader/CAS. **G1a spike TRƯỚC; Cổng G2–G3 đạt → MỞ P1.**
- **P1–P4** implementation (Fetch.Core, Worker, **Gateway**, desktop) → **Integration Gate G4–G7** →
  **P4.5 Provisioning tooling** → **Operations Gate G8–G13** → **P5 rollout**. *(KHÔNG nhảy thẳng P5;
  provisioner xây ở P4.5, không ở P0/P5.)*

**Exit P0 (= Design + P0.5, đạt G0–G3):** A–E ký duyệt + P0.5 apply xong + **G0–G3 đạt** (G0 = code-fix inventory integrity trước P1; G4–G7 = Integration Gate sau P1–P4; G8–G13 = Operations Gate trước P5).
Đụng: tài liệu + **migration forward-only mới** (owner duyệt) + code non-protected (`SupabaseDbService`,
FullStack/*). **Không** đụng file protected (Main.cs/Licensing/Velopack). *(G4–G7 = Integration Gate sau P1–P4.)*

> **P1–P5 chỉ mở sau khi P0 đóng.** **G1a PASS (mặc định kỳ vọng) → nhánh A: Worker Windows Service
> cùng máy.** Chỉ khi **spike G1a FAIL** (token ràng buộc process/tài khoản, service không dùng được token
> của user session) → **nhánh B: desktop fetch-proxy** (KHÔNG có "desktop app tự bulk-fetch" — xem #Fallback).

### P1 — Tách `AutoJMS.Fetch.Core` (chỉ sau P0)
- Rút inventory/tracking/detail + interface hoá (`IJmsHttpClient/IJmsEndpointCatalog/IJmsTokenProvider/
  ISiteContext/IEventSink`). Desktop chuyển sang dùng core, **giữ nguyên hành vi — NGOẠI TRỪ fix
  inventory finalize integrity**.
- ⚠️ **Đổi hành vi BẮT BUỘC (không nằm trong "giữ nguyên hành vi") — 🔴 HIỆN CHƯA THỰC HIỆN:** fix
  mass-left (thiếu trang ⇒ `INCOMPLETE`, KHÔNG `mark_left`; empty double-confirm 2 run) **PHẢI land ở
  P0 code-fix TRƯỚC P1**. **Tình trạng thật:** code hiện **VẪN còn lỗi** — `FullStackInventorySyncService`
  "log and continue" khi 1 trang lỗi rồi trả `Success=true`, `FullStackWaybillRepository.MarkLeftInventoryAsync`
  vẫn mark-left hàng loạt. ⇒ **G0 CHƯA PASS.** "Giữ nguyên hành vi" **KHÔNG** áp cho lỗi mass-left này.
- **Exit:** build Release 0 lỗi; smoke desktop fetch như cũ; **G0 inventory-integrity test PASS**
  (xem Cổng 1) — nếu regress thành công-giả + mark_left thì **FAIL P1**.
- Đụng: project mới `src/AutoJMS.Fetch.Core`; refactor `FullStackInventorySyncService`/
  `FullStackTrackingEnrichmentService` (không protected). **Không** đụng Main.cs/Licensing/Velopack.

### P2a — **ASP.NET Core DataHub Gateway** + Worker credential issuer (TRƯỚC Worker — phá vòng phụ thuộc)
- **Gateway** = 1 service ASP.NET Core (**giữ PG connection duy nhất**, pooling); nhận `WorkerAccessToken`,
  verify entitlement, **`SET LOCAL app.site_code`** trong transaction, gọi PG functions (canonical writer),
  host **SignalR**, `LISTEN` PG notify. Worker/desktop **không** có PG credential.
- **Worker credential issuer** ở license server: enroll/refresh/rotate `WorkerAccessToken` (PoP bằng device
  key) — xem `datahub-worker-lifecycle.md` §6.
- **Exit:** Worker (giả lập) lấy `WorkerAccessToken` → gọi gateway → private RPC OK; revoke → gateway từ
  chối. **Test bảo mật bắt buộc:** (a) **copied token** (bê `WorkerAccessToken` sang máy khác, không có
  device private key) → PoP fail → từ chối; (b) **nonce replay** (phát lại request đã ký) → từ chối theo
  `jti`/nonce; (c) **cross-project audience** (`aud`=project A gọi gateway project B) → từ chối; (d)
  `workerEnabled=false`/`credentialVersion` cũ → fail-closed.

### P2b — `AutoJMS.DataHub.Worker` skeleton (Windows Service)
- .NET Worker + Quartz; dùng Fetch.Core; **gọi Gateway (HTTPS)** bằng `WorkerAccessToken` (KHÔNG giữ PG
  credential); **site-level `fetch_leader` lease** (scope lease là con) +
  **fence bằng `leader_fencing_token`**; dùng token **LOCAL** (`tokens.dat`, DPAPI) do AutoJMS relay qua Named Pipe;
  Gateway ghi PostgreSQL qua **canonical writer duy nhất = event+projection+receipt+datahub_changes+pg_notify
  trong CÙNG transaction** (P0-B); tiered scheduler; governor cap 12 (+ **site-wide rate limit**);
  retry/backoff/timeout/**circuit-breaker + fail-closed khi mất lease**; health heartbeat.
- **Exit:** Worker kéo 1 site → ghi qua writer duy nhất; desktop thấy realtime; đo tải JMS ≤ trần site.
- Đụng: project mới `src/AutoJMS.DataHub.Worker`. ⚠️ **Schema (token pool 5 bảng, `datahub_control`,
  `datahub_health`, `fetch_leader`+scope lease, projection writer RPC, RLS hardening) đã tạo ở P0.5** —
  **P2 KHÔNG tạo lại**, chỉ dùng. Migration mới ở P2 chỉ khi phát sinh cột/RPC thật sự mới.

### P3 — Gateway endpoints + `.NET BackgroundService` + SignalR/LISTEN-NOTIFY
- **Gateway endpoints (thay Edge) = đường ghi/đọc của desktop:** `delta_pull` (keyset `(change_seq,
  stable_id)` từ `datahub_changes`), `heartbeat`, `client_contribute` (verify JWT `site_code`/quota/
  whitelist, **tính lại fingerprint**), `acquire_contributor_permit`. Gateway `SET LOCAL app.site_code`
  rồi gọi PG function; **desktop KHÔNG có PG credential**. `consume_site_budget` = PG function private.
- **SignalR + `LISTEN/NOTIFY`:** Gateway `LISTEN` kênh site; nhận `pg_notify(site, high_watermark)` (sau
  commit) → đẩy SignalR group `site:{code}` (chỉ high-watermark, không gửi row).
- **`.NET BackgroundService` (thay `pg_cron`): chỉ maintenance** — cleanup, stale detection, health
  rollup, tombstone retention. **KHÔNG** đặt lịch fetch (scheduler thuộc Worker).
- **Exit:** contributor/delta đi qua Gateway có kiểm soát; SignalR doorbell + reconnect catch-up; stale/health hiển thị.

### P4 — Desktop: reader (Gateway API) + token relay (Named Pipe) + contributor
- **Đọc = HTTPS delta-pull qua Gateway** (SignalR doorbell → pull `(change_seq, stable_id)`), **KHÔNG**
  direct SELECT, **KHÔNG** nhận PG credential. **Relay token qua Named Pipe cục bộ** cho Worker cùng máy
  (token **KHÔNG** lên cloud). Desktop chỉ ghi **session/heartbeat/contribute qua
  Edge**; **KHÔNG publish `jms_token_binding`** (chỉ **Worker/gateway** publish binding sau khi persist
  token). **không đọc registry, không tự đặt `valid`, KHÔNG tự bulk-fetch, KHÔNG tranh leader**.
  **Contributor** lẻ dùng **token WebView cục bộ** (ngoại lệ bounded) + permit.
- **Fallback KHÔNG thuộc desktop app**: là một máy khác chạy **Worker-host** (identity/key riêng) giành
  leader khi Worker chính chết (drain partition-safe).
- **Exit:** 3 trường hợp token (1 máy / nhiều máy / **hết token hợp lệ** — probe fail, không phải expiry
  đọc được) chạy đúng; contributor bị permit chặn khi quá quota; Worker-host takeover không trùng phiên (drain).

### P4.5 — Provisioning tooling (đơn giản hơn NHIỀU với shared cluster)
- **Migration runner** (thay Management API): apply **toàn bộ migration forward-only** (RLS hardening +
  FORCE RLS + functions) lên **MỘT cluster**; thêm bưu cục = **INSERT một hàng `site` + license
  `dataHub.enabled`** (KHÔNG tạo project/DB mới). Cấp `WorkerAccessToken`, đăng ký site vào Firebase.
- **Operations Gate G8–G13** (đã đơn giản hoá cho shared cluster) chạy trên tooling này.
- **Exit:** migration **idempotent** (rerun an toàn); thêm site = 1 hàng dữ liệu, 0 provisioning hạ tầng.

### P5 — Rollout diện rộng (gates đơn cluster)
- Thêm bưu cục = tạo license `dataHub.enabled` + thêm hàng `site` (không sửa code, không tạo project).
- ✅ **Shared cluster ĐƠN GIẢN HOÁ các gates (so với project-per-site):**
  - **Economics/TCO:** **một cluster** (compute + storage + backup) — **KHÔNG** còn $/project. Chi phí
    tuyến tính theo tải, không theo số site; đặt **budget ceiling/cluster** + cảnh báo. (Bỏ $1,015/100proj.)
  - **Observability:** health/log/lag **theo `site_code`** trên một cluster (một dashboard, filter site).
  - **Recovery/DR:** **RPO/RTO** cho **một cluster** + **restore drill** (PITR/backup cluster). Dữ liệu
    `waybills` rebuild từ JMS ⇒ RPO nới; workflow (notes/checks/tasks) cần backup chặt.
  - **Fleet/drift:** một schema, một version ⇒ **hết drift đa-project**; chỉ quản 1 cluster (+ shard nếu scale).
  - **Canary rollout:** thả theo **nhóm `site_code`** (feature flag/allowlist), tiêu chí dừng/rollback.
- **Exit:** thêm 1 bưu cục = 1 hàng dữ liệu + license; **gates trên PASS** ở canary trước khi mở 100+ site.

---

## 4b. Code changes bắt buộc (switch Supabase DataHub → PostgreSQL + Gateway)

> Owner đã chỉ điểm; đây là danh sách phải sửa (một số đụng file **protected** → cần owner duyệt riêng).
- **Tách DataHub-auth khỏi JMS-auth** — `FullStackOperation.cs` (~122): hiện DataHub **chỉ khởi động khi
  có JMS token**. Phải tách: **License DataHub hợp lệ ⇒ đọc PG/SignalR**; **JMS token hợp lệ ⇒ Worker/
  contributor mới gọi JMS**. (Hai điều kiện độc lập.)
- **Chuyển lease/bulk-fetch HOÀN TOÀN sang Windows Service** — `FullStackOperation.cs` (~488): desktop
  hiện vẫn giành lease + bulk-fetch. Bỏ; fetch leader chỉ ở Worker.
- **Thay `SupabaseDbService` bằng `IDataHubClient`** — `FullStackCloudSyncService.cs` (~66): client mới =
  **HTTP delta/write + SignalR subscription** tới Gateway (không SDK Supabase).
- **`SupabaseDbService.MachineId`** trong event pipeline → **worker/installation identity độc lập**.
- **Firebase config ULTRA** chỉ `enabled, gatewayUrl, clusterId, siteCode, scopes` — **KHÔNG** DB key.
- 🔴 **Tier guard FullStackForm đang bị COMMENT để test** — `Main.cs` (~1502, **PROTECTED**): **blocker
  trước production**, phải bật lại; **Gateway vẫn phải kiểm ULTRA độc lập** (không tin client).
- **`supabase-csharp`** (`AutoJMS.csproj` ~74): FullStack **hết phụ thuộc** sau switch, **nhưng CHƯA xoá
  package** khỏi toàn app (vì `Main.cs` + `DatabaseTracking` còn gọi `SupabaseDbService`) — cần migration
  riêng cho module/update. **SQLite local GIỮ** (cache/outbox/cursor/offline).

## 5. Cổng, ràng buộc & rủi ro

- **Cổng P0 bắt buộc** trước P1/P2. **Mô hình fetch chốt theo spike G1:**
  - **Nhánh A (token khả chuyển — mặc định):** fetch/fallback = **Worker-host** (máy khác chạy Worker
    giành leader). KHÔNG desktop bulk-fetch.
  - **Nhánh B (token device-bound):** execution phải trên máy sở hữu phiên → **desktop fetch-proxy**
    (dùng plaintext token cục bộ, cần **proxy subsystem riêng** — device-key registry, command/replay,
    lifecycle; xem token-pool §3/§9). Chỉ dựng nhánh B **sau khi spike xác nhận device-bound**.
- **File protected (CLAUDE.md)** — không đụng nếu owner không yêu cầu: `Main.cs`, `Licensing/*`,
  `Velopack*`, release scripts, **Supabase production config + schema migrations** (mọi migration mới
  cần owner duyệt — P2/P5).
- **3 rủi ro trọng yếu** (chứng minh ở P0): hiệu lực token cross-machine; quản lý N project; phân phối
  secret (service-role không rơi vào desktop; private key trong DPAPI).
- **Governor 12 in-flight** phải giữ ở Worker để không bị khoá IP.

---

## 6. Blast radius (sẽ tạo/đụng gì)

**Tạo mới:** `src/AutoJMS.Fetch.Core` (lib), `src/AutoJMS.DataHub.Worker` (service), Edge Functions
(`relay_binding`/`heartbeat`/`client_contribute`/control-plane), migration forward-only (token pool **5
bảng**, `fetch_leader`+scope lease RPC với `leader_fencing_token`, projection writer RPC,
`datahub_control`, `datahub_health`, **RLS hardening revoke PUBLIC**), provisioning + control-plane
(license server phát JWT `site_code`), script spike P0.
**Refactor (không protected):** `FullStack*InventorySyncService`, `FullStackTrackingEnrichmentService`
(chuyển sang Fetch.Core), desktop token-relay-qua-Named-Pipe (metadata qua Edge) + FetchMode.
**Giữ nguyên:** toàn bộ UI/dashboard/in ấn; file protected; nền hybrid + event-sourcing đã commit.

---

## 7. Điểm cần owner chốt để ĐÓNG P0 (mở P1/P2)

1. **P0-A** RLS hardening bằng **migration forward-only mới** (KHÔNG sửa migration đã apply): bỏ
   `jwt_site_code() is null`, `REVOKE EXECUTE FROM PUBLIC + anon + authenticated` trên **mọi** RPC,
   grant lại cho DB role `datahub_worker`/`datahub_edge` (KHÔNG `service_role` API key). Duyệt hướng này?
2. **P0-A** Identity — chốt (owner RATIFY/override): **entitlement ở Firebase per-license**
   (`Licenses/{key}/dataHub`, config gồm `gatewayUrl/clusterId/siteCode/scopes`); license server phát
   **DataHub JWT** ký **RS256** (`kid` rotate; **không** HS256), TTL ngắn + **`entitlement_version`/
   denylist** + **enforce entitlement trong PostgreSQL (RLS `jwt_entitlement_ok` + `app.site_code`)** cho
   revoke gần-tức-thì; **Gateway verify JWT** (thư viện JWT chuẩn) rồi `SET LOCAL app.site_code`; endpoint
   issuer **non-protected** (KHÔNG sửa `Licensing/*`). `tier-definitions` chỉ UI. Owner đồng ý?
3. **P0-B** Projection owner **đã chốt (a) cùng-RPC/transaction** — owner xác nhận/override?
4. **P0-D** Chấp nhận **site-leader lease + drain** (mất leader phải huỷ request đang bay) — có thể
   làm tăng độ trễ chuyển quyền?
5. **P0-E** Provisioning: **Migration runner trên 1 cluster** (thay Management API) — thêm site =
   INSERT hàng `site` + license, KHÔNG tạo project/DB. Owner xác nhận?
6. **Contributor & Fallback (đã chốt, cần xác nhận):** contributor dùng **token WebView cục bộ** của
   chính desktop (ngoại lệ single-active); **bỏ "desktop app tự bulk-fetch"**, fallback = máy khác chạy
   **Worker-host** giành leader. Đồng ý?
7. Còn giả định cần spike: token expiry theo ngày, token cross-machine (G1).

> Sau khi 7 điểm trên chốt và P0-A..E đóng, master plan chuyển “Approved — execute P1”.
