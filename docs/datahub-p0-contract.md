# AutoJMS DataHub — P0: Contract & Threat Model (P0-A → P0-E)

> **🟢 BACKEND v4 (owner chốt): PostgreSQL shared cluster + ASP.NET Core Gateway/SignalR — KHÔNG Supabase
> DataHub.** Ánh xạ khi đọc hợp đồng dưới đây:
> - **RLS/JWT:** JWT do license server cấp; **Gateway verify** rồi **`SET LOCAL app.site_code`** trong
>   transaction + **`FORCE ROW LEVEL SECURITY`**. (Bỏ Supabase JWKS/`getClaims()`/`jose`, bỏ Data API GRANT.)
> - **Đường ĐỌC desktop = Gateway HTTPS delta-pull API** (`(change_seq, stable_id)`), **KHÔNG direct
>   SELECT, desktop KHÔNG có PG credential**. (Chỗ nào viết "direct keyset SELECT dưới RLS" ⇒ đọc là
>   "Gateway delta-pull dưới RLS do Gateway đặt".)
> - **Realtime doorbell = `LISTEN/NOTIFY` + SignalR** (Gateway phát group `site:{code}` chỉ high-watermark).
>   `pg_notify(site_code, high_watermark)` gọi **trong cùng transaction canonical writer, chỉ phát sau commit**.
> - **Writer = Gateway gọi PG function** ghi `event+projection+receipt+datahub_changes+pg_notify` 1 transaction.
> - **Edge Function → Gateway endpoint**; **worker_gateway → Gateway**; **pg_cron → `.NET BackgroundService`**;
>   **Management API/project-per-site → migration runner trên 1 cluster (site = 1 hàng dữ liệu)**.
> - **PORTABLE giữ nguyên:** `datahub_changes`, event immutable+receipt, cursor+UPSERT, token local/Named
>   Pipe, site attestation, G0, entitlement+dead-man. Chỉ thuật ngữ hạ tầng đổi theo ánh xạ trên.

> **Cổng bắt buộc trước P1/P2.** Đồng hành: `datahub-master-plan.md` (Draft v4),
> `datahub-token-pool-plan.md`, `datahub-worker-lifecycle.md`.
>
> P0 sinh ra từ review phát hiện lỗi hợp đồng: RLS/anon chưa cách ly, fingerprint không dedupe, cursor
> bỏ sót/lặp, per-scope lease không giữ single-session, token schema tự mâu thuẫn, projection chưa có
> chủ, identity đặt quá muộn.

---

## 0. P0 chia 2 giai đoạn (giải mâu thuẫn "P0 chỉ đụng tài liệu" vs "Exit cần migration/code")

- **P0-Design (chỉ tài liệu):** ký duyệt hợp đồng A–E (schema, RLS policy, identity, fingerprint,
  cursor, leader/token, ACK). Không code.
- **P0.5 Security/Identity (ĐƯỢC đụng migration + code ngay trong P0):** viết & apply **migration
  forward-only hardening** (RLS revoke PUBLIC, Data API GRANT, private schema + DB role least-privilege),
  **identity adapter** (JWT + SetAuth ở `SupabaseDbService`), **chuyển desktop read → direct SELECT**,
  dựng token pool 5 bảng metadata + leader/CAS, và **chạy G2–G3** (G1 là spike TRƯỚC P0.5; G4–G7 là Integration Gate sau P1–P4). → các thay đổi code/migration **được phép trong P0** (không đẩy sang P2/P5).

**Thứ tự phase/gate (G1 spike TRƯỚC schema — vì branch A/B quyết schema token):**
```
P0-Design (ký hợp đồng A–E)
   → G1 SPIKE token-binding → CHỌN BRANCH (quyết schema token/proxy)
   → P0.5 Security/Identity (migration hardening + identity + desktop read→SELECT + token pool theo branch)
        └─ GATE G2–G3 đạt  ← cổng MỞ P1
   → P1–P4 → Integration Gate G4–G7 → P4.5 Provisioning tooling → Operations Gate G8–G13 → P5
```
- **G1 chạy TRƯỚC P0.5** (không đóng schema token khi chưa biết branch A/B). **Decision tree 3 nhánh:**
  - **G1a PASS + G1b PASS** (cùng máy khác-process **và** máy khác cùng LAN) → **Worker LAN** (nhánh A đầy đủ).
  - **Chỉ G1a PASS** (chỉ cùng máy) → **Worker-host trên ĐÚNG máy** giữ phiên (không chạy máy khác).
  - **G1a FAIL** → **desktop fetch-proxy** (nhánh B, cần proxy subsystem) **hoặc No-Go**.
- **G0** = code-fix inventory integrity (trước P1). **G2–G3** = cổng ra P0.5 (mở P1). **G4–G7** Integration Gate sau P1–P4; **G8–G13** Operations Gate trước P5.

**Tiêu chí "P0 Done" (thứ tự CHUẨN HÓA — High#9):**
- [ ] **P0.0:** emergency RLS hardening (revoke anon/PUBLIC, fixed search_path) + **G0 inventory integrity + test PASS**.
- [ ] **Ký A–C + FRAMEWORK D/E** (chưa chốt chi tiết phụ thuộc nhánh).
- [ ] **G1a spike** (cùng máy, khác process/tài khoản) → chọn branch A/B.
- [ ] **CHỐT D/E theo nhánh** (schema token theo branch).
- [ ] **P0.5:** migration hardening + identity adapter + desktop read-conversion + token pool apply xong.
- [ ] **Gate G2–G3 đạt** (RLS+GRANT+entitlement cách ly · cursor at-least-once) → MỞ P1.
- [ ] *(G4–G7 = Integration Gate sau P1–P4; **G8–G13 = Operations Gate trước P5** — KHÔNG thuộc Exit P0.)*

---

## P0-A — Identity, RLS, private RPC, Edge boundary (cổng bảo mật, làm ĐẦU TIÊN)

**Vấn đề đang có:** migration cho JWT không `site_code` đọc toàn bộ (`using jwt_site_code() is null
or …`) và cấp execute các RPC `SECURITY DEFINER` cho `anon` (`append_waybill_events`,
`merge_*`, lease). ⇒ client có anon key **bypass Edge**, chèn `InventoryLeft`/event giả.
⚠️ **[CRITICAL] Hardening này ĐỘC LẬP với spike G1** — KHÔNG chờ token spike. **KHÔNG đưa dữ liệu thật
lên project trước khi apply migration hardening** (hiện `202607110001`/`202607110002` vẫn để anon
đọc/ghi).

**Phải chốt:**

- **Identity per-project (chốt một claim duy nhất):** `jwt_site_code()` đang đọc **claim top-level
  `site_code`** (`202607150002`). ⇒ License server phát JWT mang `site_code` **top-level** (khớp hàm
  hiện tại), **ký bằng imported asymmetric key RS256 + JWKS** (xem "Hợp đồng JWT" bên dưới — KHÔNG dùng
  HS256 project secret). *Nếu* chuyển sang `app_metadata.site_code` thì phải có **migration forward-only**
  đổi `jwt_site_code()` — **không sửa hàm đã apply tại chỗ**. Chốt: giữ **top-level `site_code`**. Kèm
  TTL, **revoke**,
  **key rotation** (JWT secret/JWKS). Desktop KHÔNG dùng anon key để ghi; KHÔNG nhận service-role key.

- **RLS thật:** bỏ nhánh `jwt_site_code() is null`; `select` chỉ khi `site_code = jwt_site_code()`
  (và `jwt_site_code() is not null`).

- **[Critical] Enforce entitlement NGAY TRONG SUPABASE — mirror PER-LICENSE (không per-site):**
  vì desktop **đọc + Realtime trực tiếp**, denylist Firebase KHÔNG chặn token cũ. ⚠️ **Nhiều license
  ULTRA cùng một site** ⇒ mirror theo `site_code` PK sẽ **chặn nhầm** license khác khi thu hồi một
  license. Tách 2 bảng (⚠️ có `version` cho CAS **và** `valid_until` cho dead-man — hai cột KHÁC vai trò):
  ```
  license_entitlement(site_code, subject PK, enabled bool, min_version int, scopes text[],
      version int,            -- CAS nội dung (chống ghi lùi) — do Firebase cấp
      valid_until timestamptz,-- DEAD-MAN: mirror gia hạn MỖI vòng (liveness), KHÔNG qua CAS version
      updated_at)
      -- subject = ĐỊNH DANH BẤT BIẾN của license (KHÔNG phải license key thô), khớp claim `sub`
  project_entitlement(site_code PK, enabled bool, version int,
      valid_until timestamptz,-- DEAD-MAN (liveness) — gia hạn mỗi vòng
      updated_at)
      -- cấp/vô hiệu toàn site
  revoked_sessions(subject, session_id, revoked_at, primary key(subject, session_id))
  ```
  Helper (allowlist `authenticated`, dùng trong **mọi** RLS policy):
  ```
  jwt_entitlement_ok() =
      project_entitlement[jwt.site_code].enabled
      AND now() < project_entitlement[jwt.site_code].valid_until   -- DEAD-MAN (fail-closed)
      AND EXISTS license_entitlement le WHERE le.subject = jwt.sub AND le.enabled
          AND le.site_code = jwt.site_code           -- ⚠️ [High#8] RÀNG BUỘC license↔site (chống license site A mint JWT site B)
          AND jwt.entitlement_version >= le.min_version
          AND now() < le.valid_until                                  -- DEAD-MAN (fail-closed)
      AND (jwt.sub, jwt.session_id) NOT IN revoked_sessions
  jwt_has_scope(s) = s = ANY(jwt.scopes)
  ```
  ⚠️ **Hardening SECURITY DEFINER (High#8):** mọi helper/RPC `SECURITY DEFINER` phải **`SET search_path =
  pg_catalog, public` cố định** (chống search_path injection); **REVOKE EXECUTE khỏi PUBLIC/anon**; cấp
  EXECUTE đúng role; bảng entitlement có **index** `(subject)`, `(site_code)` cho helper chạy nhanh dưới RLS.
  🔒 **DEAD-MAN switch:** mỗi bản ghi entitlement có **`valid_until`**; mirror **gia hạn `valid_until =
  now() + TTL`** (TTL ≫ chu kỳ reconcile, vd 5 phút) mỗi vòng. Nếu mirror **ngừng chạy/kẹt** → `valid_until`
  hết hạn → `jwt_entitlement_ok()` **tự động fail-closed** (chặn đọc) mà không cần ai can thiệp. Chống
  kịch bản "mirror chết ⇒ entitlement đóng băng ⇒ license đã thu hồi vẫn đọc được". Alert khi mirror lag
  > ngưỡng **trước** khi TTL hết (cảnh báo sớm, không đợi tới lúc fail-closed).
  RLS: `USING (site_code=jwt_site_code() AND jwt_entitlement_ok() AND jwt_has_scope('read'))` (Realtime
  cũng chịu). **Revoke gần-tức-thì per-license:** hạ 1 license → set `license_entitlement[sub].enabled=false`
  (KHÔNG ảnh hưởng license khác cùng site); toàn site → `project_entitlement.enabled=false`.
- **Mirror job Firebase → Supabase (chốt cụ thể, không chỉ "≤ N giây"):**
  - **Owner**: chạy trên **license server (Render)** — nơi đã có Firebase session + Supabase service creds.
  - **Credential (least-privilege — KHÔNG dùng `datahub_worker`):** role **RIÊNG `datahub_entitlement_sync`**
    chỉ được `INSERT/UPDATE` đúng 3 bảng entitlement (`license_entitlement`, `project_entitlement`,
    `revoked_sessions`) + `SELECT` để reconcile; **KHÔNG** đụng `waybills`/events/token/lease. (`datahub_worker`
    dành cho `worker_gateway`; `datahub_edge` cho Edge — tách bạch, không dùng chéo.) Creds không lộ ra client.
  - **Version CAS (chỉ cho NỘI DUNG)**: cột nội dung (`enabled`, `min_version`, `scopes`) chỉ ghi khi
    `incoming.version > stored.version` (idempotent, chống ghi lùi khi event Firebase đến lệch thứ tự).
  - ⚠️ **`valid_until` renew ĐỘC LẬP version-CAS (sửa xung đột dead-man):** mỗi vòng reconcile mirror
    **luôn** `UPDATE valid_until = now() + TTL` cho **mọi** bản ghi còn hiệu lực — **KHÔNG** gác sau điều
    kiện `version >`. Nếu gác chung, Firebase không đổi version ⇒ mirror khỏe vẫn không gia hạn được ⇒
    desktop/Worker bị chặn sai sau TTL. Vậy: **liveness (`valid_until`) = ghi mỗi vòng; nội dung = CAS
    theo version.** (Tách thành 2 câu lệnh trong cùng transaction reconcile.)
  - **Trigger + reconcile**: realtime trên `Licenses/*`/`DataHubProjects/*` **đẩy ngay** + **reconcile
    định kỳ** (vd mỗi 60s) quét chênh lệch.
  - **SLA revoke cụ thể**: p95 ≤ **10s** từ đổi Firebase → RLS chặn; alert nếu mirror lag > ngưỡng.

- **Thu hồi EXECUTE đúng cách (KHÔNG chỉ anon/authenticated):** Postgres mặc định cấp `EXECUTE` cho
  **`PUBLIC`**, nên `anon`/`authenticated` vẫn thừa hưởng. Migration hardening phải:
  1. `REVOKE EXECUTE ON FUNCTION <rpc> FROM PUBLIC, anon, authenticated;` — **chỉ cho RPC
     privileged/mutating** (append/merge/lease/active-token/pull_*).
  2. ⚠️ **ALLOWLIST helper RLS cho `authenticated`:** `jwt_site_code()`, **`jwt_entitlement_ok()`**,
     **`jwt_has_scope()`** (và mọi hàm mà **RLS policy gọi**) **PHẢI giữ EXECUTE cho `authenticated`** —
     nếu revoke, RLS sẽ **hỏng**. Tách rõ 2 nhóm: **helper RLS (allowlist)** vs **RPC đặc quyền (revoke)**.
  3. `ALTER DEFAULT PRIVILEGES ... REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;` (chặn hàm tương lai; helper
     RLS grant lại tường minh).
  4. `GRANT EXECUTE` lại **từng RPC đặc quyền** cho đúng DB role (`datahub_worker`/`datahub_edge`),
     **KHÔNG** cấp cho `PUBLIC`/`anon`/`authenticated`.
  4. Ưu tiên đặt RPC đặc quyền trong **schema không expose** (vd `private`, không nằm trong
     `search_path` của PostgREST) thay vì `public`.
  - **Phạm vi = MỌI RPC cũ + mới**, không chỉ `append_waybill_events`. Danh sách hiện có (kiểm từ
    migrations): `append_waybill_events, merge_waybill_rows_v2, merge_order_checks, merge_dispatch_tasks,
    push_order_notes, pull_* (waybill/events/notes/checks/tasks), try_acquire_site_lease,
    refresh_site_lease, release_site_lease, try_acquire_inventory_lease, refresh/release/complete_inventory_lease,
    upsert_new_waybills, merge_waybill_tracking_rows, ingest_bigdata_waybills, ingest_stockcheck_waybills,
    merge_bigdata_detail, finalize_waybills, mark_left_inventory, mark_waybill_handled,
    reconcile_inventory_sources, run_retention_cleanup, purge_signed_receipts, …` → migration phải
    **liệt kê đầy đủ** (query `pg_proc` để không sót).

- **Đường ghi duy nhất cho desktop = Edge** (thống nhất với P0-D): desktop **chỉ gọi Edge Functions**
  cho cả `client_contribute` **và** relay/heartbeat token. Edge validate JWT (`site_code`, quota,
  whitelist) rồi gọi **private RPC bằng DB role `datahub_edge`** (transaction pooler). Desktop **không** gọi RPC ghi trực tiếp,
  **không** đọc registry.

- **Đường ĐỌC duy nhất cho desktop = Gateway HTTPS delta-pull API** (CHỐT — v4; **KHÔNG** direct SELECT,
  desktop **KHÔNG** có PG credential): desktop gọi `GET /delta?since=(change_seq,stable_id)`; **Gateway**
  verify JWT → `SET LOCAL app.site_code` → keyset SELECT `datahub_changes` dưới **RLS + FORCE RLS** → trả
  JSON. Phân trang keyset `(change_seq, stable_id)` (KHÔNG `updated_at/seq`). ⚠️ `PullEvents` hiện gọi
  `pull_events_delta` (`SupabaseDbService.Hybrid.cs`) → **thay bằng `IDataHubClient.DeltaPull` (HTTP)**.
  Mọi ghi (append/lease/writer) là **PG function private** chỉ Gateway gọi (Gateway giữ PG connection).

- **Private RPC gọi thế nào (đúng tầng credential — DB role KHÔNG rơi xuống máy bưu cục):** RPC đặc
  quyền đặt schema `private` **không expose** PostgREST; gọi qua **kết nối Postgres trực tiếp** bằng
  DB role least-privilege (user/password/pooler), **KHÔNG `service_role` API key**. ⚠️ **Mỗi máy ULTRA
  đều chạy Worker → KHÔNG phát password `datahub_worker` cho từng máy.** Tách tầng:
  - **`worker_gateway`** (dịch vụ server-side) **giữ DB role `datahub_worker`** (session pooler/direct);
    Worker LAN gọi gateway bằng **`WorkerAccessToken`** (scope=worker) → gateway verify entitlement rồi
    gọi private RPC (leader/active-token/append+project/lease). Worker máy bưu cục **chỉ** giữ
    `WorkerAccessToken` (DPAPI), **không** có DB credential.
  - **`datahub_edge`** — Edge Function: **transaction pooler**; `EXECUTE` đúng RPC contributor/relay.
  - `service_role` API key là khoá REST PostgREST, **không** dùng gọi private RPC. (REST chỉ phục vụ
    direct SELECT của desktop trên `public` có RLS.) Ref: Supabase connection guidance.
  - Vòng đời Worker (service luôn chạy, hết token = pause) & token store: xem `datahub-worker-lifecycle.md`.

- **Hợp đồng JWT desktop — CHỐT MỘT phương án (không để hai):**
  - **Giá trị cụ thể (chốt):** `iss = "https://<license-server>/datahub"`, **`aud = "authenticated"`**
    (Supabase yêu cầu audience token authenticated là `authenticated`) **+ claim riêng
    `app_aud = "autojms-datahub"`** (verify bổ sung ở Edge/Worker; không đặt `aud` custom vì Data API
    có thể từ chối), `sub = <UUID bất biến lưu ở Firebase>` (khớp `license_entitlement.subject`),
    `role="authenticated"`, `exp` = **TTL 15 phút**, + `site_code`, `scopes[]`, `session_id`,
    `entitlement_version`, `project_ref`. Truyền qua **`accessToken`** (`SetAuth`). *(Xác nhận `aud`
    qua spike Data API + Realtime nếu cần.)*
  - **Ký = imported asymmetric `RS256`, KEY PER-PROJECT** (mỗi project 1 signing key + JWKS riêng → token
    site A **không** verify được ở project B; cách ly mạnh hơn shared key). Rotate theo **`kid`**, overlap.
  - **Ma trận scope theo endpoint/RPC:**
    | scope | cho phép |
    |---|---|
    | `read` | direct SELECT `waybills`/events/workflow (RLS) **VÀ Realtime** (Realtime cưỡi RLS SELECT) |
    | `relay` | Edge `relay_binding`/`heartbeat` (token đi Named Pipe local, KHÔNG qua Edge) |
    | `contribute` | Edge `client_contribute` + `acquire_contributor_permit` |
    ⚠️ **`realtime` KHÔNG enforce độc lập được** (RLS chỉ kiểm quyền SELECT) → **`read` bao gồm Realtime**;
    bỏ scope `realtime` riêng (hoặc nếu muốn tách phải thiết kế policy/role riêng — hiện gộp vào `read`).
    `relay`/`contribute` enforce ở Edge. Thiếu scope → RLS/Edge từ chối.
  - **Edge/Worker phải kiểm entitlement MIRROR** (`jwt_entitlement_ok()` / `license_entitlement` +
    `project_entitlement`), **không chỉ** verify chữ ký + claim tồn tại (chữ ký hợp lệ nhưng license đã
    hạ vẫn phải bị chặn).
  - **Endpoint phát token = service/endpoint riêng non-protected** (KHÔNG sửa `Licensing/*` protected);
    `SupabaseDbService` gọi endpoint này lấy JWT. Private signing key giữ ở **license-server/KMS**, không
    rời server.
  - **Rotation:** hai `kid` active chồng lấn (overlap window) khi xoay; JWKS công bố cả hai tới khi token
    `kid` cũ hết hạn.
  - **Revoke — CHỐT (không "tùy chọn"):** JWT Data API chỉ hết theo `exp` ⇒ **TTL ngắn + BẮT BUỘC kiểm
    `entitlement_version`/denylist** để revoke trước hạn (không để mỗi TTL).
  - **Edge verify JWT tự-mint bằng thư viện JWT (`jose`) + project JWKS** — ⚠️ `getClaims()` **có thể
    thất bại** với JWT tự mint bằng imported key; và legacy `verify_jwt` từ chối RS256/ES256. ⇒ **tắt
    `verify_jwt` mặc định** trên Edge function này, tự verify bằng `jose`: kiểm
    **`alg`(RS256) / `kid` / `iss` / `aud` / `exp` / `role` / `site_code` / `scopes` / `entitlement_version`**
    theo JWKS công bố. Ref: Supabase custom/third-party JWT guidance.
  - **Refresh + Realtime reconnect:** **Supabase DataHub JWT** (có TTL — KHÁC token JMS vốn không expiry)
    gần hết hạn → refresh qua endpoint → `SetAuth` lại + resubscribe + **delta catch-up** lại.

- **Root-of-trust issuer — entitlement ở FIREBASE per-license (CHỐT, không suy từ `tier-definitions`):**
  ```
  Licenses/{licenseKey}: { status, tier, middleCode, dataHub{ enabled, projectId, scopes[read,relay,contribute], entitlementVersion } }  -- ⚠️ BỎ 'realtime' (gộp vào 'read' — RLS không enforce realtime độc lập)
  DataHubProjects/{projectId}: { enabled, siteCode, projectRef, projectUrl, publishableKey, configVersion }
  DataHubWorkers/{workerId}: {           -- ⚠️ WORKER AUTHORITY REGISTRY (trước đây thiếu)
      licenseSubject, siteCode, projectId,
      devicePublicKey,                    -- public key PoP (private key CHỈ ở máy Worker)
      workerEnabled bool,                 -- bật/tắt Worker (fail-closed khi false)
      credentialVersion int,              -- tăng khi rotate → gateway từ chối version cũ
      enrolledAt,
      revoked { at, reason } null         -- revoke record (thu hồi tức thì qua gateway denylist)
  }
  ```
  **Gateway verify Worker:** `WorkerAccessToken` (PoP ký bằng device private key) hợp lệ **chỉ khi**
  `workerEnabled==true` AND `credentialVersion` khớp AND `revoked==null` AND `aud==projectId` của site đó.
  **Cổng hiệu lực (server-side, đọc license record — KHÔNG tin client):**
  ```
  DataHubAllowed =
      Licenses/{key}.status == active
      AND Licenses/{key}.tier == ULTRA
      AND Licenses/{key}.dataHub.enabled == true
      AND DataHubProjects/{dataHub.projectId}.enabled == true          -- BẮT BUỘC kiểm cả project
      AND normalize(Licenses/{key}.middleCode) == normalize(DataHubProjects/{projectId}.siteCode)
  ```
  - Khoá liên kết nhất quán = **`dataHub.projectId`** (bỏ `siteRef` mơ hồ); site được resolve từ
    `DataHubProjects/{projectId}.siteCode`.
  - `normalize()` = **UPPER + trim** (chuẩn hoá `middleCode ↔ siteCode` chính xác).
  - **`tier==ULTRA` là điều kiện CẦN** (chỉ ULTRA có `FullStackForm` → mới cần Supabase DataHub). BASE
    **không** nhận project config/JWT.
  - Kiểm `tier` **server-side từ license record** (không phải `tier-definitions.json` client — cái đó
    chỉ điều khiển UI/runtime, không phải authority bảo mật).

  **Hành vi theo tier:**
  | Trường hợp | Hành vi |
  |---|---|
  | **BASE** | Không nhận DataHub project/JWT; không Realtime/relay/contributor/Worker |
  | **ULTRA chưa gán project** | **Fail-closed**; không khởi động FullStack/DataHub |
  | **ULTRA + dataHub.enabled** | Nhận đúng project của site + JWT ngắn hạn |
  | **ULTRA hạ xuống BASE** | Ngừng refresh DataHub JWT; token cũ hết theo TTL (+entitlement_version) |

  **Response license server (tách `dataHub` riêng, CHỈ trả cho ULTRA):**
  ```json
  { "tier": "ULTRA",
    "dataHub": { "enabled": true, "siteCode": "214A02", "projectUrl": "...",
                 "publishableKey": "...", "accessToken": "...", "expiresAt": "...",
                 "scopes": ["read","relay","contribute"] } }   // BỎ 'realtime' — gộp vào 'read'
  ```
  **Luồng phát token (server-side, KHÔNG tin client):** (1) Render verify license-JWT + Firebase session;
  (2) đọc lại `/Licenses/{key}` — **không tin** `tier`/`middleCode`/`projectId` client gửi; (3) kiểm
  **`DataHubAllowed`** (status=active **AND tier==ULTRA** AND dataHub.enabled AND `DataHubProjects[projectId].enabled` AND normalize(middleCode)==normalize(siteCode));
  (4) resolve `DataHubProjects/{projectId}`, đối chiếu **`middleCode ↔ siteCode`**; (5) phát **Supabase
  DataHub JWT riêng** (TTL ngắn, `role=authenticated`, `site_code`, `scopes`, `session_id`,
  `entitlement_version`); (6) trả **`dataHub{ projectUrl, publishableKey, accessToken, expiresAt,
  scopes }`** — Supabase enforce tiếp bằng GRANT/RLS. **BASE / ULTRA-chưa-gán → KHÔNG trả `dataHub`
  (fail-closed).**
  - **License JWT ≠ Supabase JWT** (khác audience/signing key/claims) — không dùng thẳng.
  - ⚠️ **Phân biệt rõ 2 hệ Supabase trong toàn bộ tài liệu:**
    - **Supabase module/update CHUNG** (`server.js` global: `SUPABASE_PROJECT_URL/anonKey/manifests`) —
      dùng cho auto-update/module, **giữ nguyên**, dùng chung mọi tier.
    - **Supabase DataHub PER-SITE** (`dataHub` block) — DB dữ liệu đơn của bưu cục, **chỉ ULTRA**, per
      license/site. Hai hệ **không** thay thế nhau.
  - **TUYỆT ĐỐI không trả/không lưu ở client:** DB-role password, service-role/secret key, JWT signing
    private key, Worker private key.
  - `dataHub.enabled=false`/revoke/đổi project → server **ngừng phát/refresh** DataHub JWT; token cũ
    hết theo TTL; thu hồi tức thì cần **denylist/`entitlement_version`**.
  - `tier-definitions.json` từ đây **chỉ điều khiển UI/runtime**, KHÔNG phải authority bảo mật DataHub.

  ⚠️ `SupabaseDbService` hiện **chỉ nhận anon key** → P0.5 thêm adapter gọi endpoint issuer. **Owner
  RATIFY** phương án. Ref: Supabase JWT/Signing Keys.
  - Identity issuance đặt ở **P0.5** (không phải P5); truyền JWT qua **`accessToken`**; kèm **test
    refresh/revoke/Realtime reconnect**. Ref: Supabase JWT Fields/JWT guidance.

- **Forward-only:** **không sửa migration đã apply**; mọi hardening là **migration mới** (owner duyệt).

**Exit A:** ma trận quyền + policy RLS + luồng identity (một claim, một đường ghi) ký duyệt; migration
hardening forward-only (revoke PUBLIC + phủ đủ RPC) viết xong; **kế hoạch ngừng/migrate legacy plaintext
token trong `AutoJMS.json` (`SettingsManager.cs`) ký** (ngừng ghi + migrate + xoá giá trị cũ — xem
`datahub-worker-lifecycle.md` §3).
🔴 **[High#8] CODE ITEMS bắt buộc đóng trong P0.0/P0.5 (chưa hiện thực hóa):**
- **Migration còn `anon` bypass** → hardening forward-only: `REVOKE` anon, RLS phủ đủ, helper `SECURITY
  DEFINER` fixed `search_path`.
- **Client đang dùng anon key + fallback URL Supabase HARD-CODE** (`SupabaseDbService.cs:18`) → thay bằng
  DataHub JWT per-site + cấu hình project từ license response (**KHÔNG hard-code**), fail-closed khi thiếu project.
- **`license_entitlement.site_code = jwt.site_code`** enforce trong helper (đã thêm ở trên) + index.

### Ma trận quyền (least privilege)
| Chủ thể | Đọc | Ghi | Cấm |
|---|---|---|---|
| **Worker** (DB role `datahub_worker`) | project của site | private RPC ghi `waybills`/`waybill_events` (kèm fencing), health, active-token | project site khác; `service_role` API key |
| **Desktop** (JWT site_code) | `waybills`, events, workflow, `datahub_control` | relay token **qua Named Pipe (LOCAL, cùng máy)** + `session`/`heartbeat`/`client_contribute` **qua Edge** | service-role key; ghi thẳng bảng/RPC ghi; gửi token lên cloud; **publish `jms_token_binding`** (chỉ Worker/gateway) |
| **Edge Function** (DB role `datahub_edge`, transaction pooler) | theo nhu cầu validate | append contributor event (fingerprint tính lại) | gọi JMS; giữ token JMS |
| **anon** | — (không) | — | mọi thứ |

---

## P0-B — Event schema v2 + fingerprint theo event-type + projection owner

**⚠️ Bẫy A→B→A (phải xử lý):** với trạng thái **có thể lặp lại**, hash chỉ theo nội dung sẽ khiến
lần xuất hiện thứ hai của giá trị cũ bị `unique(fingerprint)` loại → projection **kẹt** ở giá trị
giữa. Ví dụ OrderDetail đổi A→B→A: bản A thứ hai bị bỏ, projection mắc ở B. ⇒ fingerprint của
observation trạng thái **phải kèm một trục đơn điệu** (epoch/revision) để phân biệt lần quan sát.

**CHỐT: tách 2 loại dữ liệu (giải A→B→A dứt điểm):**

1. **Transition event (append-only, dedupe bằng fingerprint):** những gì có **mốc thời gian nghiệp vụ
   đơn điệu** từ nguồn.
   - **Tracking:** `waybill | type | source_event_at (JMS) | canonical(payload)`. JMS event_time là
     trục đơn điệu → A→B→A an toàn (mỗi transition có event_time khác nhau).
   - **Inventory membership:** `waybill | type | run_id | membership` (leader quyết định).
   - **Workflow:** `waybill | type | client_action_id ổn định`.

2. **Snapshot mutable (KHÔNG phải event append-only) — vd `OrderDetail`:** lưu như **projection
   field-set** trong **bảng snapshot riêng** (`waybill_detail_snapshot`), không đưa vào log
   dedupe-bằng-nội-dung.
   - **Chống ghi lùi bằng revision/CAS — quy tắc chính xác:**
     - `incoming_revision > stored_revision` → **apply**.
     - `incoming_revision = stored_revision` → **no-op nếu `canonical(payload)` giống**; **khác nội
       dung ⇒ `conflict`/quarantine** (không ghi đè theo arrival order — đây là lỗ hổng của `>=`).
     - `incoming_revision < stored_revision` → **stale** (bỏ).
     `revision` = mốc đơn điệu từ **nguồn** (updateTime/version JMS). ⚠️ Upsert "last-arrival" thuần
     **không an toàn** (A,B, retry A đến muộn đè B).
   - ⚠️ **`WaybillModels` hiện KHÔNG có `updateTime`/`version`** → **spike riêng (P0-E/G)** xác minh JMS
     có trường revision.
   - **CHỐT nhánh không-revision (nếu spike fail):** snapshot detail **single-writer = chỉ leader Worker
     ghi** (contributor KHÔNG ghi detail). ⚠️ Single-writer **vẫn chưa đủ** — một Worker có thể gặp
     `A applied → B applied → retry A đến muộn`. Bổ sung:
     - **`snapshot_observation_id` UNIQUE** (idempotent, retry cùng observation = no-op), **và**
     - **per-waybill FIFO sequence `(leader_term, snapshot_seq)`**: `snapshot_seq` **cấp + LƯU BỀN VỮNG
       TRƯỚC lúc dispatch** (persist, không phải lúc apply, để retry sau crash vẫn đúng seq); **retry
       giữ NGUYÊN `snapshot_observation_id` + `snapshot_seq`**
       (không sinh seq mới) → retry cũ có seq nhỏ hơn watermark → **stale, bỏ**; **hoặc** ép
       **one-in-flight mỗi waybill**. **Watermark detail = `(leader_term, snapshot_seq)`.**
     Giữ `waybill_detail_snapshot_history`. **Multi-writer arrival-order bị cấm** (đó là ghi lùi thật).
     KHÔNG mô tả nhánh này là "arrival-order" hay "upsert last-write" nữa — nó là **FIFO theo seq**.
   - **Snapshot ACK riêng + DISPOSITION đầy đủ (đồng bộ với ACK event):**
     - `applied` (CAS thắng, đã ghi) → **xoá outbox**.
     - `noop` (revision/seq ≤ hiện tại, không đổi) → **xoá outbox** (idempotent).
     - `stale` (seq nhỏ hơn watermark — retry cũ tới muộn) → **xoá outbox** (không retry, đã bị vượt).
     - `conflict` (đụng revision song song) → **reconcile**: refetch revision hiện tại + so, **KHÔNG
       dead-letter**, KHÔNG re-stamp seq cũ (provenance bất biến).
     - `rejected` → phân loại như event: **retryable** (5xx/lock) giữ+backoff; **terminal** (payload sai/
       entitlement/scope) → **dead-letter** + alert (không retry vô hạn).
   - **Writer RPC nguyên tử:** CAS snapshot + ghi `waybill_detail_snapshot_history` + patch field-group
     tương ứng trong `waybills` + trả ACK — **trong một transaction** (không tách rời).

`canonical(payload)` = chuẩn hoá thứ tự khoá + trim + rule rõ; đổi rule ⇒ tăng `fingerprint_version`.
Loại `observed_at thô / source_client / event_id` khỏi hash transition.

**Envelope v2 (4 mốc thời gian + version + authority):** `event_id, canonical_event_id(server),
site_code(JWT), waybill_no, event_type(whitelist), fingerprint, fingerprint_version, payload(canonical),
source, source_client, **authority_class{worker|contributor}(server gán)**, source_event_at, observed_at,
received_at(server), seq(server), change_seq(server), schema_version`. (authority_class **phải nằm trong
envelope** để replay tái lập đúng ưu tiên.)

**Chống contributor đầu độc watermark (CHỐT):** projection **KHÔNG tin thẳng** `source_event_at`/
`revision` do desktop gửi (timestamp tương lai hoặc revision khổng lồ có thể **khoá** dữ liệu Worker):
- **Clamp**: từ chối/kẹp `source_event_at` **tương lai quá `now + skew`** (Edge kiểm khi nhận).
- **Source authority theo event-type**: chỉ **leader/Worker** được đặt trạng thái quyền lực cao
  (membership, inventory, detail revision); contributor chỉ đóng góp **`TrackingObserved` mức advisory**.
- **Detail revision từ contributor = advisory**, KHÔNG nâng `last_detail_revision` cho tới khi **Worker
  xác nhận** (không để contributor đẩy revision vượt Worker).
- **Authority_class + 2 watermark (contributor VẪN đạt mục tiêu "đẩy dữ liệu mới hơn"):** mỗi event mang
  `authority_class` **do server gán** (`worker`=authoritative / `contributor`=advisory). Mỗi nhóm giữ
  `last_*_worker` và `last_*_advisory`.
  - **Contributor ĐƯỢC advance projection** khi observation **mới hơn** giá trị hiện tại (đúng mục tiêu:
    máy mở đơn thấy trạng thái mới → đẩy lên để máy khác thấy) — nâng `last_*_advisory`.
  - Nhưng **Worker authoritative luôn thắng khi xung đột**: lần fetch Worker kế tiếp **ghi đè** giá trị
    advisory (kể cả khi timestamp advisory "mới hơn") — Worker là nguồn sự thật. Clamp future timestamp
    vẫn áp dụng để chống đầu độc.
  - **Cột authoritative trong `waybills` CHỈ do Worker ghi** (không merge advisory vào đó — bỏ luật
    "max(worker,advisory)" gây mâu thuẫn). Advisory lưu **ở cột/bảng riêng** (`*_advisory` + cờ
    `has_pending_advisory`) → UI hiển thị "đang chờ Worker xác nhận"; Worker fetch sau **ghi đè cột
    authoritative** và xoá cờ advisory. ⇒ contributor có tác dụng tức thì (qua chỉ báo advisory), Worker
    vẫn là nguồn sự thật duy nhất của cột chính.

**Projection owner — CHỐT (a): append event + update projection trong CÙNG RPC/transaction.**
RPC đặc quyền này là **writer duy nhất** của `waybills`; **cả Worker lẫn Edge-contributor gọi chung**
nó (nguyên tử, idempotent). Loại (b) projector-process riêng (thêm một moving part; không chọn).
KHÔNG để Worker vừa `merge_waybill_rows_v2` vừa append event ở 2 thao tác rời — **gỡ đường này**.

**"HAI PRODUCER — MỘT CANONICAL WRITER" (chốt, KHÔNG cho client/Worker UPSERT thẳng projection):**
```
Worker      → worker_gateway  → private atomic writer RPC ┐
Client(UI)  → Edge client_contribute → cùng writer RPC    ┘→ (append event + project + ACK) 1 transaction
Realtime doorbell → delta pull (cursor) → SQLite/UI (chỉ ĐỌC, KHÔNG ghi ngược projection)
```
- **CẤM** Worker **và** client UPSERT trực tiếp bảng projection — **mọi** ghi qua **một** writer RPC.
- **Phân quyền dữ liệu (authority):**
  - **Worker authoritative:** inventory, tracking chính thức, order detail (snapshot), scheduling, derived.
  - **Client advisory:** observation khi user thao tác (ghi cột `*_advisory`, Worker fetch sau ghi đè).
  - **Workflow = client-owned qua Edge:** `order_notes` **append-only**; `order_checks`/`dispatch_tasks`
    **mutable → CAS bằng `expected_revision`/`change_seq`** (client gửi revision mình thấy; writer chỉ
    ghi nếu khớp, lệch → trả `conflict` để client refetch) — **KHÔNG last-write-wins âm thầm**.
  - **Writer phía server tự gán** `site_code` (từ JWT), `authority_class`, `fingerprint`, `received_at`
    (`clock_timestamp()` sau site lock), `change_seq` — **không tin client khai**.
- ⚠️ **ECHO SUPPRESSION (chống vòng lặp):** dữ liệu **vừa apply từ remote delta-pull KHÔNG được sinh
  outbox mới** — nếu không, pull → apply → outbox → push → doorbell → pull … lặp vô hạn. Cờ rõ nguồn
  "remote-applied" khi ghi SQLite; chỉ **thao tác cục bộ của user/Worker** mới enqueue outbox.

**Ordering theo NHÓM FIELD (một order-tuple chung KHÔNG đủ):** tracking / inventory / workflow /
detail cập nhật các nhóm cột khác nhau, nhịp khác nhau. Field-mapping phải định nghĩa cho **từng nhóm**:
- **owner** (event-type/nguồn nào được ghi nhóm cột đó),
- **partial update** (chỉ đụng cột thuộc nhóm — snapshot detail **không** ghi đè cột tracking và ngược lại),
- **`last_applied` watermark RIÊNG mỗi nhóm**, dùng **khoá có thứ tự**: `last_tracking_event_at`
  (source_event_at), `last_detail_revision`, **`last_inventory (leader_term, run_seq)`** (KHÔNG dùng
  run_id trần vì không bảo đảm thứ tự giữa các nhiệm kỳ leader), `last_workflow_seq`.
⇒ event workflow mới **không** chặn/không lùi tracking; snapshot detail **không** đè field ngoài phạm vi.

**Field mapping — ARTIFACT bắt buộc ký (ĐẦY ĐỦ — đối chiếu `202606110001_autojms_bootstrap.sql` +
`202607110001_hybrid_sync.sql`; owner suy TỪ CODE, không phỏng đoán):**

⚠️ `waybills` có **hai lớp cột**: cột gốc tên **tiếng Việt** (bootstrap) + cột **dashboard tiếng Anh**
(hybrid_sync). Dashboard mirror từ nguồn VN — liệt kê cả hai.

| Nhóm | Cột (ĐỦ) | Nguồn/owner (theo code) | Watermark | Cập nhật |
|---|---|---|---|---|
| **Tenancy** | `site_code` | Server (từ JWT) | — | set khi insert |
| **Tracking** | `trang_thai_hien_tai↔current_status, thao_tac_cuoi↔last_action, thoi_gian_thao_tac↔last_action_time, buu_cuc_thao_tac↔last_site_name, nguoi_thao_tac↔employee_name, last_site_code, employee_code, nhan_vien_nhan_hang, thoi_gian_yeu_cau_phat_lai, nhan_vien_kien_van_de, nguyen_nhan_kien_van_de, dau_chuyen_hoan` | Worker `TrackingObserved` (**authoritative**) / contributor (**advisory** → `*_advisory`). ⚠️ **Các field này do `FullStackTrackingEnrichmentService.ApplyTrackingData` sinh từ tracking — KHÔNG phải Detail** | `last_tracking_event_at`=`source_event_at` | partial |
| **Inventory** | `is_in_current_inventory, left_inventory_at, first_seen_at, last_seen_at` | Worker **leader** (`InventorySeen/Left`) | `(leader_term, run_seq)` | partial |
| **Detail (snapshot)** | `receiver_name, receiver_phone_masked, dia_chi_nhan_hang, phuong, noi_dung_hang_hoa, cod_thuc_te, pttt, dia_chi_lay_hang, thoi_gian_nhan_hang, ten_nguoi_gui, trong_luong, ma_doan_full, ma_doan_1, ma_doan_2, ma_doan_3, reback_status, in_hoan_scan_time, print_count` | **snapshot store** (`waybill_detail_snapshot`) | `(leader_term, snapshot_seq)`/revision | snapshot upsert |
| **Scheduling** | `is_active, tracking_interval_mins, last_tracked_at, next_track_at` | **Worker scheduler** (nhịp fetch nội bộ; KHÔNG contributor/Edge ghi) | Worker-owned | partial |
| **Derived** (server tính) | `current_state, age_hours, days_in_inventory, risk_score, risk_level, risk_reasons, sla_status, sla_deadline` | **Projection RPC** hàm thuần. ⚠️ **`current_state` phụ thuộc CẢ Tracking LẪN Inventory** (`EnrichStateRiskSla(row, isInCurrentInventory)` → `_stateEngine.DeriveState(dto, isInCurrentInventory)`) → **cross-group**, phải recompute khi **một trong hai** đổi | tái tính khi input đổi | recompute |
| **Timestamps** | `created_at, updated_at` | Server | — | set khi ghi |
| **Workflow** (bảng riêng) | `order_checks(is_checked, checked_at, checked_by, note)`, `order_notes(note, created_by)`, `dispatch_tasks(task_type, priority, status, …)` | **Edge contributor** | `last_workflow_seq` | partial |

- ⚠️ **Owner đã sửa theo code (không theo phỏng đoán):** `thoi_gian_thao_tac / buu_cuc_thao_tac /
  nguoi_thao_tac / dau_chuyen_hoan / nhan_vien_nhan_hang` = **Tracking** (enrichment sinh), KHÔNG phải
  Detail như bản trước.
- ⚠️ **`current_state` là cross-group** (Tracking+Inventory) → **KHÔNG** gán watermark một nhóm; recompute
  khi Tracking **hoặc** Inventory advance.
- ⚠️ **`reback_status`/`in_hoan_scan_time`** owner = Detail snapshot (projection copy từ snapshot).
- ⚠️ **Scheduling (`is_active`,…) chỉ Worker ghi** — contributor/Edge KHÔNG đụng (tránh client đổi nhịp fetch).
- ⚠️ **Derived + Timestamps KHÔNG nguồn ngoài ghi trực tiếp** (chỉ projection/server).
- ⚠️ **`print_count` PHẢI tách 2 cột (High#7):** desktop **tăng đếm in cục bộ** — đây KHÔNG phải trường
  Detail do Worker/JMS sở hữu. Chốt: **`jms_print_count`** (Detail snapshot, Worker-owned) **≠**
  **`local_print_count`/workflow counter** (desktop-owned, ghi qua Edge như workflow). Không để desktop
  tăng cột Worker-owned.
- 📋 **Ma trận authority BẮT BUỘC ký (`event_type × producer × field × authority`):** mỗi ô ghi rõ
  producer nào (Worker/contributor/desktop) được ghi field nào với authority gì (authoritative/advisory/
  client-owned) + luật xung đột (CAS `expected_revision` / newest-per-group / append-only). Artifact này
  đóng cùng Exit B.
- `FoldProjectionAsync` phải fold theo đúng bảng này (hiện mới dựng tập nhỏ) trước khi rebuild.

**3 bảng tách bạch theo identity (High#7 — hết đổi vai `event_id`):**
- **`fs_outbox`** khoá **`request_event_id`** (client sinh, ổn định) — hàng chờ đẩy.
- **`waybill_event_receipt`** ánh xạ **`request_event_id → canonical_event_id`** (server chốt lúc ingest).
- **mirror `waybill_events`** khoá **`canonical_event_id`** (immutable). Client fold theo canonical.
⇒ 3 danh tính, 3 vai, KHÔNG dùng lẫn: `request_event_id` (client), `canonical_event_id` (server),
`change_seq` (vị trí feed).

**Ba thứ tự KHÁC NHAU (gỡ mâu thuẫn line "ordering per-field" vs "tuple chung"):**
1. **Cursor/replication** = `(change_seq, stable_id)` (P0-C) — CHỈ để kéo delta không sót/lặp; **không**
   quyết định giá trị field nào thắng.
2. **Projection application** = **watermark RIÊNG mỗi nhóm** (bảng trên) — quyết định event tới có được
   ghi đè nhóm cột đó không. Nhóm khác nhịp khác nhau ⇒ **một tuple chung KHÔNG đủ**: workflow mới không
   chặn/không lùi tracking; snapshot detail không đè cột tracking.
3. **Tie-break TRONG nhóm Tracking (transition)** = `source_event_at → received_at → seq` (dòng "Projection
   order" cuối P0-B) — chỉ áp cho **so hai transition tracking cùng nhóm**, KHÔNG phải luật chung đè
   watermark per-nhóm. (Snapshot dùng `(leader_term, snapshot_seq)`/revision; inventory dùng
   `(leader_term, run_seq)`.)

⚠️ **`received_at` = `clock_timestamp()` LẤY SAU KHI đã cầm site advisory lock** (không phải
`now()`/`transaction_timestamp()` = thời điểm bắt đầu txn) — để `received_at` phản ánh **thứ tự commit
thực** dưới serialize per-site, nhất quán với cách cấp `change_seq`.

**⚠️ [CRITICAL — sửa alias offline] EVENT IMMUTABLE + RECEIPT, BỎ mutate/alias event cũ:**
> Vấn đề bản trước: khi rehash biến root cũ thành alias (`alias_of` set), replication chỉ đọc
> `WHERE alias_of IS NULL` ⇒ **client offline đã lưu root cũ KHÔNG bao giờ biết nó thành alias** (dù bump
> `change_seq`). ⇒ **bỏ hẳn cơ chế mutate/rehash-in-place event cũ.** Event là **BẤT BIẾN** (append-only,
> không bao giờ đổi vai/ghi đè).

```
-- Event log: APPEND-ONLY, IMMUTABLE (không cột alias_of, không mutate)
waybill_events(
  canonical_event_id   uuid primary key,   -- server cấp, ổn định vĩnh viễn
  site_code            text not null,
  canonical_fingerprint text not null,     -- fp canonical (server chốt) — dùng dedup lúc INGEST
  fingerprint_version  int  not null,
  server_seq           bigint not null,    -- identity thứ tự ghi
  change_seq           bigint not null,    -- vị trí trên feed thống nhất (xem datahub_changes)
  authority_class      text not null,      -- worker | contributor (KHÔNG đổi sau khi ghi)
  source_event_at timestamptz, received_at timestamptz, payload jsonb, ...
  ,unique (site_code, canonical_fingerprint)   -- 1 event/фp; dedup ở ingest, KHÔNG tạo alias
)
-- Receipt: ánh xạ request→canonical (dedup KHÔNG cần đổi event cũ)
waybill_event_receipt(
  site_code text, request_event_id uuid,   -- client gửi
  canonical_event_id uuid not null,        -- server map tới (có thể trỏ event đã tồn tại)
  status text,                             -- inserted | duplicate
  primary key (site_code, request_event_id)
)
```
- **Dedup lúc INGEST (không đổi lịch sử):** event trùng `canonical_fingerprint` → server **không** ghi
  dòng mới; **receipt** trả `request_event_id → canonical_event_id` (event ĐÃ tồn tại) + `status=duplicate`.
  Client lưu ánh xạ, fold theo `canonical_event_id`. Root cũ **giữ nguyên vai vĩnh viễn** ⇒ client offline
  không bao giờ lệch.
- **Promotion (contributor→worker) KHÔNG mutate event cũ:** ghi **event authoritative MỚI (immutable)** của
  Worker; projection fold **xếp hạng authority** (worker > contributor) → worker thắng. Cả hai event đều
  lên `datahub_changes` feed (mỗi cái 1 `change_seq`) ⇒ client offline kéo đủ và fold đúng, **không cần**
  đổi vai event cũ.
- **Đổi `fingerprint_version`:** event cũ giữ nguyên `canonical_event_id`; version mới chỉ ảnh hưởng dedup
  các event MỚI. Không backfill/mutate event cũ.
- Local mirror: key `canonical_event_id` (immutable); outbox key `request_event_id` (xem "3 bảng" dưới).

**`authority_class` (server gán, BẤT BIẾN trên mỗi event) + promote KHÔNG mutate:** contributor event
trùng quan sát với Worker → **KHÔNG** đổi `authority_class` event cũ. Worker append **event authoritative
MỚI**; projection fold xếp hạng **worker > contributor** → cột authoritative do worker-event quyết, cột
advisory hiển thị tới khi có worker-event. Cả hai immutable, đều lên feed. Contributor-only giữ advisory.

**Collision khi trùng `canonical_fingerprint`:** server chốt **canonical_event_id = của event `server_seq`
NHỎ NHẤT** (ghi trước thắng); event đến sau **dedup ở ingest** → **receipt** trỏ về canonical đó
(`status=duplicate`), **KHÔNG ghi dòng mới, KHÔNG alias**. Lịch sử không đổi vai.

**Snapshot seq + outbox nguyên tử:** cấp `snapshot_seq` (persist) + ghi outbox trong **cùng một local
transaction** (không để cấp seq nhưng outbox chưa ghi khi crash).

**ACK từng event (CHỐT — sửa mất-event khi dedupe/đổi version):** ⚠️ hiện `append_waybill_events` trả
**số dòng**, còn client (`FullStackCloudSyncService`) đánh dấu **cả batch** đã sync → nếu server
reject/dedupe một item hoặc đổi `fingerprint_version`, client có thể **mất event** hoặc giữ fp cũ.
Hợp đồng đúng:
- RPC trả **kết quả từng event, PHÂN BIỆT request-identity vs canonical-identity:**
  `{ request_event_id (client gửi), canonical_event_id (server), status: inserted|duplicate|rejected,
     server_seq, canonical_fingerprint, fingerprint_version }`. Khi event B **dedupe vào canonical A**
  → trả `request_event_id=B, canonical_event_id=A` (client map B→A, không giữ B như bản riêng).
- **Remote uniqueness** theo **`(site_code, canonical_fingerprint)`** (server quyết canonical); local
  `(site_code, request_event_id)` UNIQUE để ACK idempotent.
- **Đổi `fingerprint_version`:** server chạy **rehash/backfill** (tính canonical_fp mới cho event cũ) và
  trả `canonical_fingerprint` mới; client cập nhật theo, không tạo bản trùng.
- Client **advance outbox theo từng event** theo ACK (xoá khi `inserted|duplicate`). **Phân loại
  `rejected`/`conflict`:**
  - **retryable** (mạng/5xx/lock) → giữ outbox, backoff retry.
  - **terminal** (payload sai, scope thiếu, entitlement fail) → **dead-letter** (không retry vô hạn), alert.
  - **conflict** (fingerprint-version/canonical) → reconcile theo quy tắc dưới, không dead-letter.
- `fingerprint_version` đổi → server trả **canonical fp mới**; client **cập nhật fp theo server**,
  không giữ fp cũ (reconcile).
- Mô hình giao nhận: **at-least-once + trạng thái cuối idempotent** (KHÔNG kỳ vọng "đúng một lần" ở
  transport); dedupe bằng fingerprint/`(site,event_id)` bảo đảm áp nhiều lần cho cùng kết quả.
- **Reconcile khi fingerprint đã tồn tại local:** cập nhật ACK/`server_seq`/canonical fp cho item đó
  **trong cùng transaction** với việc advance outbox (không để "đã có local nhưng chưa ACK" mắc kẹt).
- **Reconcile khác `fingerprint_version` (thuật toán chống collision — local đang `UNIQUE(fingerprint)`
  ở `FullStackMigrations.cs`):**
  1. **Giữ `event_id` server làm khoá canonical** (không phải fingerprint); ACK trả `event_id`+`server_fp`.
  2. Local: **đổi UNIQUE từ `fingerprint` sang `(event_id)`** (migration), thêm cột `fp_version` +
     `canonical_fp`; fingerprint chỉ còn là **chỉ mục dedupe phụ theo version**.
  3. **Receipt (KHÔNG alias):** dedup ở ingest trả `request_event_id → canonical_event_id`; local ánh xạ
     theo receipt, **không** tạo bản ghi mới, **không** đổi vai event cũ (event immutable — xem P0-B).
  4. `fs_outbox.ref_key` = **`request_event_id`** (ổn định); mirror key = `canonical_event_id`.
- Hoặc lựa chọn thay thế: **atomic reject cả batch** + reconcile all-or-nothing.

**Projection order (tie-break TRONG nhóm Tracking):** `source_event_at` → `received_at` → `seq`; không
tin đồng hồ client. Idempotent. **Đây KHÔNG phải luật chung đè watermark per-nhóm** (xem "Ba thứ tự KHÁC
NHAU" ở trên): inventory dùng `(leader_term, run_seq)`, snapshot dùng `(leader_term, snapshot_seq)`/revision.
Membership chỉ do leader (`InventorySeen/Left`); contributor không đảo membership.

**[CRITICAL] Inventory finalize integrity — SỬA CODE TRƯỚC KHI TÁCH `Fetch.Core`:**
⚠️ Hiện `FullStackInventorySyncService` **"log and continue" khi 1 trang lỗi** rồi vẫn trả `Success=true`;
`FullStackWaybillRepository.MarkLeftInventoryAsync` đánh dấu **mọi đơn không thấy = `left`** → **một trang
lỗi có thể mass-mark hàng loạt đơn rời tồn (mất dữ liệu nghiệp vụ)**. Hợp đồng đúng:
- **Chỉ finalize/`InventoryLeft` khi ĐỦ mọi trang** + **khớp `total`/`hash`** đầu-cuối (dùng
  `FetchInventoryHeadAsync`).
- **Thiếu bất kỳ trang / lỗi trang ⇒ run `INCOMPLETE`** → **KHÔNG** chạy `MarkLeftInventory` (giữ nguyên
  membership cũ); chỉ upsert phần thấy được.
- **Empty inventory phải xác nhận HAI LẦN** (2 run liên tiếp cùng rỗng) trước khi mark left hàng loạt.
- ⚠️ **Removal chỉ sau 2 FULL-SCAN ổn định cùng full-set hash (JMS KHÔNG có per-order revision):** vì
  không có revision từng đơn để phân biệt "đơn rời" với "fetch lỗi", chỉ được `MarkLeft` một đơn khi
  **hai lần full-scan COMPLETE liên tiếp** đều **không** thấy đơn đó **và** full-set hash hai lần khớp
  (ổn định). Lưu `last_full_set_hash` + `last_full_scan_complete_at` ở sync_state; incomplete làm **reset**.
- 🔴 **TRẠNG THÁI CODE THẬT (chưa G0 PASS):** working tree đã có `INCOMPLETE` + empty double-confirm +
  page-1-rỗng-mà-total>0 ⇒ INCOMPLETE + reset streak khi incomplete. **CÒN THIẾU:** (i) so **full-set
  hash end-to-end bằng `FetchInventoryHeadAsync`**; (ii) **removal 2-scan-hash** ở trên; (iii)
  **failure-injection tests** (ngắt trang giữa chừng, total>0 nhưng trang rỗng, empty×2, hash lệch).
  ⇒ **G0 vẫn NOT PASS** cho tới khi (i)-(iii) xong + test xanh.
- Đây là **fix code độc lập** (không chờ G1/token), làm **trước P1** để Worker không nhân bản lỗi.

**Bootstrap/backfill + replay test:** rebuild `waybills` từ **`waybill_events` (transition) + snapshot
store (`waybill_detail_snapshot`)** — KHÔNG chỉ từ event log (OrderDetail nằm ở snapshot store, không
trong event log). Replay test chứng minh idempotent + A→B→A (transition kết thúc = A; snapshot theo
revision cao nhất).

**Exit B:** envelope v2 (+`fingerprint_version`) + fingerprint transition-vs-snapshot (đã tách) +
projection owner **(a) đã chốt** + **bảng field-mapping đầy đủ** + **ACK từng event** +
**pull-side UPSERT contract (P0-C) ký** + **G0 inventory-integrity test PASS** + replay/backfill
test ký duyệt.

---

## P0-C — Cursor contract + pagination

**✅ [CHỐT — giải quyết tombstone + alias-offline + cursor đa-stream bằng MỘT feed] `datahub_changes`:**
> Thay vì mỗi bảng một cursor (dễ sót DELETE/alias, khó phối 1 doorbell), dùng **một feed thống nhất**.
```
datahub_changes(
  site_code    text not null,
  change_seq   bigint not null,          -- đơn điệu per-site (cấp dưới per-site advisory lock)
  entity_type  text not null,            -- 'waybill' | 'event' | 'note' | 'check' | 'task'
  stable_id    text not null,            -- khoá ổn định của entity (waybill_no / canonical_event_id / id)
  operation    text not null,            -- 'upsert' | 'delete'   (delete = TOMBSTONE)
  payload      jsonb null,               -- ảnh chụp/patch, hoặc NULL nếu chỉ ref
  ref          text null,                -- (tuỳ chọn) trỏ bảng nguồn để đọc chi tiết
  primary key (site_code, change_seq)
)
create index ix_changes_keyset on datahub_changes(site_code, change_seq, stable_id);
```
- **MỌI thay đổi ghi 1 change record trong CÙNG transaction** với thao tác gốc: waybill upsert, **DELETE
  (tombstone)**, event insert, promotion (event authoritative mới), workflow mutation. Không có đường ghi
  nào "lọt" ngoài feed.
- **Cursor DUY NHẤT = `(change_seq, stable_id)`** trên feed này (thay cho cursor mỗi bảng). Client kéo
  `WHERE (change_seq, stable_id) > (last_cs, last_id) ORDER BY change_seq, stable_id`.
- **Tombstone:** `operation='delete'` là record BẤT BIẾN có `change_seq` ⇒ **client offline chắc chắn
  thấy xoá** (không dựa Realtime DELETE vốn không filter được). Retention tombstone ≥ maxOfflineWindow
  (vd 30–90 ngày, owner chốt); **resurrection** = upsert mới `change_seq` cao hơn.
- **Alias/promotion offline (Critical#1):** vì promotion là **event authoritative mới** có change record
  riêng, client offline kéo được và fold theo authority — **không** phụ thuộc việc đổi vai event cũ.
- **Realtime = CHỈ báo high-watermark** (`max(change_seq)` của site); client nhận doorbell → pull feed từ
  cursor. Payload Realtime bỏ qua.
- **Migration hiện HARD-DELETE** (`is_in_current_inventory`/xoá dòng) ⇒ **phải đổi**: mọi delete ghi
  tombstone vào feed cùng transaction (forward-only migration P0.5).
- *(Fallback nếu không unified: mỗi stream cursor riêng + `is_deleted/deleted_at/change_seq` — kém hơn,
  nhiều doorbell.)*

**Lỗi đang có:** event cursor `MAX(remote_seq)` kẹt khi `INSERT OR IGNORE` bỏ dòng trùng (remote_seq
NULL); row delta `updated_at > cursor + LIMIT` bỏ sót khi nhiều dòng trùng timestamp vượt limit.

**⚠️ Hai bẫy (Critical):**
- **(a) Commit lệch thứ tự:** T1 `seq=1` commit chậm hơn T2 `seq=2` → consumer advance→2 rồi **bỏ sót** T1.
- **(b) "Contiguous seq" KẸT VĨNH VIỄN:** `waybill_events.seq` là **Postgres identity** → rollback/conflict
  vẫn **tiêu thụ sequence** tạo **gap hợp lệ** không bao giờ xuất hiện; nếu chờ "max seq liên tục" sẽ
  kẹt mãi ở gap đó. ⇒ **BỎ yêu cầu contiguous.**

**Hợp đồng đúng (CHỐT: serialize writer + giữ identity + chấp nhận gap + page-max):**
- **Serialize writer theo site** (advisory lock per-site trong writer RPC) → `seq` cấp **trong** txn
  serialize ⇒ **commit order == seq order** (giải bẫy (a)). Với serialize, page-max **an toàn** kể cả
  có gap (gap = seq bị rollback, không bao giờ xuất hiện → cứ bỏ qua, không chờ).
- **`change_seq` là khoá cursor DUY NHẤT cho CẢ event lẫn row** (đơn điệu, cấp **dưới cùng per-site
  lock**). **KHÔNG dùng `updated_at=now()`** (now() = txn-start; commit muộn mang timestamp cũ → sót)
  và **KHÔNG dùng identity `seq` làm cursor** (xem lý do dưới). ⚠️ migration hiện nhận `updated_at` từ
  client → **chuyển sang `change_seq` server** cho mọi stream.
- ⚠️ **[Critical] Promotion/rehash phải BUMP `change_seq`:** khi UPDATE authority_class (promote
  contributor→worker) hoặc rehash `fingerprint_version` trên **event cũ**, **identity `seq` KHÔNG đổi**
  → client offline pull theo `seq` sẽ **mãi bỏ sót promotion**. ⇒ **mọi mutation (insert/promote/rehash)
  bump `change_seq`**; consumer pull `change_seq > cursor` → promotion được tái giao. (Hoặc append một
  bản `AuthorityConfirmed` mới — nhưng bump `change_seq` gọn hơn.) ⚠️ Bump `change_seq` **chỉ có tác dụng
  nếu consumer APPLY bằng UPSERT** theo `canonical_event_id` (xem "Pull-side APPLY = UPSERT" ở P0-C); nếu
  vẫn `INSERT OR IGNORE` thì bản tái giao bị bỏ qua và promotion mất — hai vế phải đi cùng nhau.
- ⚠️ **[CRITICAL] Cursor là TUPLE `(change_seq, stable_id)`, KHÔNG chỉ `change_seq`:** nếu nhiều dòng
  cùng `change_seq` bị cắt giữa hai trang (page boundary), predicate `change_seq > cursor` sẽ **bỏ sót**
  phần còn lại. Đúng: **predicate `(change_seq, stable_id) > (last_change_seq, last_stable_id)`**,
  `ORDER BY change_seq, stable_id`, **checkpoint CẢ HAI**.
- **Event cũng cần `stable_id` riêng** (vd `event_id`/`canonical_event_id`) để tie-break trong cùng
  `change_seq`.
- **`stable_id` từng bảng:** `waybills`=`waybill_no`; `order_notes`/`dispatch_tasks`=`id`;
  **`order_checks`=`waybill_no`** (current-state; history thì bảng riêng). **Composite index
  `(site_code, change_seq, stable_id)`** mỗi bảng/stream + **test cắt trang giữa dòng cùng change_seq**.
- Checkpoint page-max `(change_seq, stable_id)` trong CÙNG transaction cục bộ; duplicate vẫn tính đã xử
  lý; gap (rollback) bỏ qua.
- **Desktop đọc = direct keyset `SELECT` dưới RLS** (P0-A): `WHERE site_code=jwt_site_code() AND
  (change_seq, stable_id) > ($c, $s) ORDER BY change_seq, stable_id LIMIT n`; kéo tới trang ngắn hơn limit.

**⚠️ [CRITICAL] Pull-side APPLY = UPSERT, KHÔNG `INSERT OR IGNORE` (chống mất promotion/rehash):**
Bug hiện tại: `FullStackEventLog` apply delta bằng **`INSERT OR IGNORE`** (khoá `fingerprint`/`event_id`).
Khi server **promote** (`authority_class` đổi) hoặc **rehash** (`fingerprint_version` đổi) một event **đã
tồn tại local**, server **bump `change_seq`** và **tái giao cùng `canonical_event_id`** — nhưng
`INSERT OR IGNORE` **bỏ qua** (đã có) trong khi **cursor vẫn tiến** ⇒ **promotion mất vĩnh viễn**. Hợp
đồng đúng (áp cho **cả event log lẫn 4 row stream**):
- **Khoá UPSERT = `canonical_event_id`** cho event (KHÔNG `fingerprint` — fingerprint đổi khi rehash);
  row stream UPSERT theo **`stable_id`** (`waybills`=`waybill_no`, …).
- **`ON CONFLICT DO UPDATE`** cập nhật **`authority_class`, `canonical_fingerprint`, `fingerprint_version`,
  `change_seq`** (và payload nếu đổi) — **chỉ khi `incoming.change_seq > stored.change_seq`** (monotonic,
  chống ghi lùi khi tái giao trùng).
- **Apply + fold projection + checkpoint cursor `(change_seq, stable_id)` trong CÙNG một local
  transaction** — không advance cursor qua event chưa apply thành công (crash giữa chừng → replay lại,
  idempotent).
- Local đổi UNIQUE từ `fingerprint` sang **`canonical_event_id`** (migration ở P0-B), `fingerprint` chỉ
  còn index dedupe phụ theo `fingerprint_version`.
- **Test:** (a) promote contributor→worker (đổi `authority_class`, bump `change_seq`) → client offline
  pull lại → local **thấy authoritative** (không bị IGNORE); (b) rehash `fingerprint_version` → local cập
  nhật `canonical_fingerprint`, **không** tạo bản trùng; (c) crash sau apply trước checkpoint → replay
  không nhân đôi, không nhảy cursor.

**⚠️ SignalR = DOORBELL (notification), KHÔNG phải nguồn phát lại bền vững — delta-pull vẫn bắt buộc:**
SignalR/WebSocket **không phải durable queue**; reconnect/mất mạng **có thể bỏ lỡ** notification, **không
gửi row qua SignalR** (chỉ high-watermark). **Nguồn phát:** `pg_notify(site_code, high_watermark)` gọi
**trong cùng transaction canonical writer** (PostgreSQL chỉ phát **sau commit**) → Gateway `LISTEN` →
đẩy SignalR group `site:{code}`. **Hợp đồng bắt buộc:**
```
Kết nối SignalR → JOINED group site → DELTA CATCH-UP (HTTPS pull từ cursor) → Live
Doorbell (high_watermark) → COALESCE/DEBOUNCE (cửa sổ ngắn) → 1 lần DELTA-PULL
Reconnect / JWT refresh → join lại group → DELTA CATCH-UP lại (không tin đã liền mạch)
Periodic SAFETY PULL (thưa, vd 30–60s) → bảo hiểm khi doorbell bị mất
```
- **Coalesce/debounce:** nhiều doorbell dồn dập **không** kích N lần pull — gộp cửa sổ ngắn (200–500ms) → pull **một** lần.
- **Không bao giờ** áp trạng thái từ payload SignalR; luôn **pull `(change_seq, stable_id)`** rồi **apply +
  checkpoint trong CÙNG transaction** (P0-C UPSERT).
- **Thứ tự đúng:** Joined **trước**, rồi catch-up — Live trước khi catch-up xong vẫn an toàn (catch-up idempotent).
- ✅ **DELETE/tombstone KHÔNG lo giới hạn Realtime nữa:** delete ghi **tombstone vào `datahub_changes`**
  (operation='delete') → client thấy qua delta-pull (không dựa notification). (Bỏ hẳn ràng buộc
  "Supabase Postgres Changes không filter DELETE".)
- **Một kết nối / DESKTOP:** N desktop = **N SignalR connection** (bình thường). Mỗi desktop giữ **MỘT
  `HubConnection` lâu dài** (join nhiều group site nếu cần) — KHÔNG mở nhiều connection cho mỗi form/bảng.
- ⚠️ **Ghim version `supabase-csharp 0.16.2`** (`AutoJMS.csproj`): nhánh này **cũ**, upstream đã đổi tên ở
  v1 → **KHÔNG nâng cấp lẫn vào P0**. ⚠️ **v4: FullStack HẾT phụ thuộc `supabase-csharp` cho DataHub**
  (dùng **SignalR client `Microsoft.AspNetCore.SignalR.Client` + `HttpClient`** cho delta-pull). Spike
  P0-E: **SignalR reconnect/JWT-refresh/catch-up + LISTEN/NOTIFY độ trễ**. (Package `supabase-csharp`
  chỉ còn cho module/update chung — xem master §4b.)

**Exit C:** **`datahub_changes` feed thống nhất** (mọi upsert/delete/promotion ghi change record cùng txn +
`pg_notify` sau commit; tombstone bất biến; cursor `(change_seq, stable_id)` DUY NHẤT) + checkpoint
page-max/transaction (duplicate vẫn tiến) + **pull-side UPSERT** (apply+fold+checkpoint 1 transaction) +
**SignalR doorbell = join site→catch-up→Live + reconnect→catch-up + periodic safety pull (chỉ high-watermark)**
+ **test pagination + promotion re-apply + tombstone offline + "mất SignalR giữa lúc có thay đổi" 0 sót** +
desktop read = **Gateway delta-pull API** (không direct SELECT, không PG cred).

---

## P0-D — Site leader + token registry + selection epoch + drain

**Lỗi đang có:** per-scope lease cho phép Worker giữ `tracking` còn desktop-fallback giữ `inventory`
→ hai token gọi JMS song song (hai phiên).

**Phải chốt:**
- **`fetch_leader` lease cấp site** là gốc (cấp `leader_fencing_token` + `leader_term`); scope lease
  là con của cùng leader; chỉ một leader.
- **Token registry = 5 bảng METADATA** (`jms_session_sources / jms_token_binding /
  jms_token_candidate_state / jms_active_token / jms_token_validation_events`) — xem
  `datahub-token-pool-plan.md` §3. **Token LOCAL (DPAPI), KHÔNG ciphertext trên cloud**; registry chỉ giữ `token_fp`/binding.
- **Fence bằng `leader_fencing_token`/`leader_term`, KHÔNG bằng owner_id** (owner tái dùng sau
  takeover). Đặt `jms_active_token` + ghi projection/JMS-side qua **RPC CAS nguyên tử** kiểm fence hiện
  tại; `selection_epoch` tăng khi đổi token trong cùng nhiệm kỳ.
- **Drain chịu partition:** worker **fail-closed** khi refresh lease fail; **kiểm lease trước mỗi
  request**; **hard timeout** mỗi request; leader mới chờ `lease_expiry + max_request_timeout +
  clock_skew_margin`. Bảo đảm tuyệt đối ⇒ cân nhắc **single outbound gateway** (DB lease không đủ).
- **Desktop** chỉ relay/heartbeat **qua Edge** (không RPC trực tiếp, không đọc registry, **không tự
  đặt `valid`**); two-strike `suspect→invalid`.

- **CAM KẾT SINGLE-SESSION (owner chốt — ghi rõ đánh đổi):** mô hình là **"MỘT bulk-fetch leader +
  contributor lẻ bounded"**, **THAY THẾ** bất biến "chỉ một authToken hoạt động / 3 token cases" ban
  đầu của dự án. Owner **chấp nhận rõ**:
  - **Có lúc ≥2 token JMS hoạt động đồng thời** (Worker active + contributor WebView).
  - **Permit chỉ COOPERATIVE**: kiểm ở AutoJMS **trước** khi gọi JMS; **JMS không verify permit** ⇒
    **G7 không bảo đảm** trước client lỗi/bị sửa (chỉ giới hạn client trung thực).
  - Muốn **hard-guarantee** sau này (đổi contract): (i) contributor gửi one-waybill cho **Worker** gọi
    bằng active token; hoặc (ii) chỉ cho desktop gọi trực tiếp khi **`token_fp` cục bộ == active token**,
    còn lại chuyển qua Worker. Hiện **giữ mô hình bounded cooperative**.
  - 📝 **RISK ACCEPTANCE (owner ký):** owner chấp nhận chính thức rằng single-session/rate-limit chỉ
    **cooperative** (client lỗi/bị sửa có thể vượt permit; G7 không bảo đảm tuyệt đối). Đây là mục risk
    register, không phải "đã giải quyết".
- **Contributor = ngoại lệ, dùng TOKEN WEBVIEW CỤC BỘ (CHỐT rõ):** thao tác mở-đơn của
  desktop **tự gọi JMS 1 waybill bằng chính authToken trong WebView của nó** (đã có sẵn trong bộ nhớ)
  — **KHÔNG** dùng token active của Worker (token của Worker ở LOCAL máy Worker, desktop không đọc
  registry). Đây là **ngoại lệ tường minh** với quy tắc "chỉ leader gọi JMS": contributor là 1 request
  lẻ, không giữ lease, không bulk. Bound bằng **permit token-bucket site-wide** (xin permit qua Edge
  *trước* khi gọi JMS; hết quota → chặn tại client) và **đếm vào trần request/phút site** (P0-E).
  Permit chỉ giới hạn *tần suất*, không cấp *quyền truy cập token* — token là của WebView cục bộ.

- **`IJmsTokenProvider` phải PIN cả chu kỳ, không chỉ trả token:** trả `(token, token_fp,
  selection_epoch, leader_fencing_token)` và **giữ cố định trong suốt một chu kỳ fetch**; mọi ghi kèm
  bộ pin này. Active token đổi giữa chừng (epoch tăng) ⇒ chu kỳ hiện tại **huỷ an toàn**, không trộn
  hai epoch. (Sửa hợp đồng interface so với bản chỉ `GetTokenAsync()`.)

**Exit D:** leader + 5 bảng metadata + `leader_fencing_token`/`leader_term` + CAS + token LOCAL binding + drain
partition-safe + **contributor permit** + **token provider pin (fp/epoch/fence)** ký duyệt.

---

## P0-E — Spike + rate limit + provisioning + key rotation + Service lifecycle

**Spike (3 câu hỏi):** request hợp lệ = `authToken` + header hằng, không cookie/thiết bị (kiểm chứng
từ `JmsApiClient`, xem token-pool §1b). Cần xác minh:
1. Cùng token gọi từ **process khác/máy khác cùng IP LAN** còn hợp lệ không? (portable vs device-bound)
2. Hard-expire chính xác lúc **giao ngày 00:00 / logout**?
3. **JMS getOrderDetail có trường revision/updateTime/version** cho snapshot CAS không? (`WaybillModels`
   hiện không có) — nếu không → snapshot **single-writer + FIFO `(leader_term, snapshot_seq)`** (P0-B).
4. **Realtime trên `supabase-csharp 0.16.2` (version ĐANG GHIM — KHÔNG nâng cấp trong P0):** xác minh
   `SetAuth(JWT)` + **reconnect + resubscribe + delta catch-up** + RLS lọc đúng site trên đúng version này
   (upstream đã đổi tên ở v1 → hành vi có thể khác tài liệu mới). Một `Supabase.Client`/desktop multiplex
   nhiều channel.
No-Go câu 1 (device-bound) → nhánh B: Worker host trên máy desktop / desktop fetch-proxy.
⚠️ **G1 chạy TRƯỚC khi chốt schema P0.5 token pool** — kết quả branch A/B **quyết định schema** (nhánh B
cần thêm proxy subsystem: device_key_registry/command/replay). Không đóng schema token trước khi có G1.

**Site-wide rate limit:** `cap 12 in-flight` chỉ là **concurrency của một process**, KHÔNG phải giới
hạn tần suất toàn site. Chốt trần **request/phút toàn site** (cộng mọi scope + contributor) từ số liệu
đo thực, đặt HOT/WARM/COLD dưới trần đó.

**Provisioning (Supabase Management API):** script tạo project theo `middle_code` + apply toàn bộ
migration (gồm hardening P0-A) tự động; control plane lưu `project_url/public_key/versions`.
⚠️ **Breaking change Supabase 2026 — Data API GRANT tường minh:** project mới **không** tự expose bảng
`public` ra Data API/PostgREST nữa. **GRANT là tầng riêng, tách khỏi RLS.** Provisioning phải:
- `GRANT SELECT ON <bảng đọc của desktop> TO authenticated;` (để direct SELECT hoạt động),
- **revoke `anon`** khỏi mọi bảng, cấu hình **exposed schema** đúng (chỉ `public` cho REST đọc;
  `private` không expose),
- không dựa vào RLS thay cho GRANT (RLS lọc dòng, GRANT quyết định có truy cập bảng hay không).

**Key rotation & secret:** private key Worker trong **DPAPI(LocalMachine)/Cert Store + ACL**; `key_id`
xoay khoá; service-role key mỗi project không rơi vào desktop; thu hồi khi thay máy Worker.

**Windows Service lifecycle (owner-approved — chi tiết `datahub-worker-lifecycle.md`):** **Service LUÔN
chạy** (`Automatic Delayed Start` + auto-restart); **AutoJMS KHÔNG sở hữu vòng đời** (check/start/
handshake/relay token qua **Named Pipe ACL**; đóng app KHÔNG stop service). **Persistent token store**
`%ProgramData%\AutoJMS\DataHub\tokens.dat` (DPAPI LocalMachine + ACL Service SID/Admins, no plaintext,
no `EBWebView` copy) → sống qua đóng app/reboot, probe lại trước dùng. **`WorkerAccessToken`** riêng
(license session của Worker, không phụ thuộc UI JWT). Kế hoạch cài/nâng cấp (Velopack)/rollback.
**`CanFetch`** = `service_running AND worker_license_ULTRA AND dataHub.workerEnabled AND project_enabled
AND worker_credential_valid AND fetch_leader_lease_valid AND active_JMS_token_valid AND
circuit_breaker_closed`. **Hết token = PAUSE** (service chạy, 0 request, chờ relay); **revoke/disable/
mất lease = DRAIN-STOP** (mất quyền). 8 tiêu chí nghiệm thu ở `datahub-worker-lifecycle.md` §9.

**Exit E (chỉ KÝ contract/runbook — KHÔNG implement):** kết quả spike (Go/No-Go) + trần rate site +
**contract/runbook** provisioning + quy trình key rotation + runbook Service. ⚠️ **Script/tooling
provisioning được XÂY ở P4.5**, không phải P0-E; P0-E chỉ chốt thiết kế + runbook (tránh đòi script
trước P1).

---

## Tiêu chí Go/No-Go **đo được** (3 cổng)

**Cổng 1 — G0–G3 (ra P0.5, MỞ P1):**
| # | Tiêu chí | Ngưỡng đo được |
|---|---|---|
| **G0** | **Inventory finalize integrity (code-fix, ĐỘC LẬP token/G1) — BẮT BUỘC** | **Âm (chống mass-left):** inject 1 trang lỗi/timeout giữa chừng → run = `INCOMPLETE`, **`MarkLeftInventory` KHÔNG chạy**, membership cũ giữ nguyên, chỉ upsert phần thấy được; `total`/`hash` đầu-cuối không khớp → cũng `INCOMPLETE`. **Empty inventory:** chỉ mark-left hàng loạt khi **2 run liên tiếp cùng rỗng** (double-confirm). **Dương:** đủ mọi trang + khớp `total`/`hash` → finalize + `InventoryLeft` đúng các đơn thực sự rời. Test này **PASS trước khi tách `Fetch.Core` (P1)** |
| G1 | Token portable (2 test tách) | **G1a — BẮT BUỘC (quyết mô hình): cùng máy, process/service account KHÁC** (Worker Service ≠ user session của desktop): gọi 3 endpoint = **2xx + `code=1`** (loại yếu tố process/tài khoản). **G1a PASS → nhánh A (Worker Windows Service cùng máy)** — mô hình owner đã chốt (mỗi máy 1 Worker + token riêng). **G1a FAIL → nhánh B (desktop fetch-proxy)**. **G1b — máy KHÁC cùng IP LAN: REFERENCE-ONLY** (không quyết nhánh; chỉ khảo sát khả năng 1 Worker phục vụ nhiều máy trong tương lai — KHÔNG thuộc mô hình hiện tại). |
| G2 | RLS + GRANT + entitlement theo tier | **Âm:** JWT site A chạm site B = 0 dòng/denied; `anon` không execute RPC/không SELECT; **BASE KHÔNG nhận DataHub token**; **ULTRA chưa gán project bị chặn (fail-closed)**; **license không `dataHub.enabled` KHÔNG mint**; **site A KHÔNG lấy project/token site B**; **downgrade ULTRA→BASE → refresh/reconnect thất bại**; **DEAD-MAN: dừng mirror → sau `valid_until` TTL, mọi đọc bị chặn (fail-closed), không đóng băng entitlement cũ**. **Dương:** ULTRA được gán **chỉ đọc đúng site**, Realtime đúng site, refresh OK, **nhiều license cùng bưu cục nhận đúng 1 project**; revoked/expired (qua `entitlement_version`) bị chặn |
| G3 | Cursor at-least-once + idempotent (event **và** 4 row stream, khoá `change_seq`) | **Event:** replay trang + retry giữa trang + **promotion/rehash BUMP `change_seq` → client offline nhận lại** + **"seq thấp commit SAU seq cao"** (serialize/site → 0 sót) + **gap rollback KHÔNG kẹt**. **Row keyset `change_seq`:** >limit cùng `change_seq`, retry giữa trang, concurrent update — 0 sót, không lặp vô hạn |

**Cổng 2 — Integration Gate G4–G7 (SAU P1–P4, trước production):**
| # | Tiêu chí | Ngưỡng đo được |
|---|---|---|
| G4 | Single **bulk-fetch** leader (bounded) + **failover hết-token** | Ép 2 owner tranh leader: chỉ **1** leader bulk-fetch; sau takeover chủ cũ = **0** request **sau grace window** `lease_expiry+max_req_timeout+skew` (bảo đảm *bounded*, không phải tức thì). Muốn bounded=0 tuyệt đối trong cửa sổ → **bắt buộc outbound gateway**. **Failover hết-token:** A là leader, B có token valid → làm A hết mọi candidate → A gọi `clear_active_and_release_leader` (nguyên tử) → **B giành leader và fetch trong ≤ grace window**; A ở `NO_LOCAL_TOKEN` **không** flap-giành-lại; relay token mới cho A → A đủ điều kiện tranh lại. Test biến thể: A hết token nhưng **giữ leader** (mô phỏng lỗi) → phát hiện site kẹt = FAIL. **Bootstrap-probe (chống deadlock):** site chưa có leader, chỉ có token `unknown` mới relay → Worker giành **provisional leader** → probe → `valid` → lên leader thường + fetch; nếu probe `invalid` và hết candidate → nhả, không kẹt. *(Contributor kiểm ở G7.)* |
| G5 | Fingerprint A→B→A + snapshot ordering | Replay A→B→A: transition kết thúc = **A**; snapshot: có revision → revision cao nhất; **không revision → single-writer + FIFO `(leader_term, snapshot_seq)`**; **test delayed-retry + ACK-loss** (retry A đến muộn KHÔNG đè B nếu seq cũ); trùng thật bị dedupe |
| G6 | ACK từng event + snapshot | Batch có duplicate/rejected: outbox xoá `inserted|duplicate`; **`rejected` phân loại**: `retryable`→giữ+backoff, `terminal`→**dead-letter** (không giữ vô hạn), `conflict`→reconcile; snapshot ACK `applied\|noop\|stale\|conflict\|rejected`; 0 mất-event |
| G7 | Rate site (gồm contributor) — **cooperative** | Tổng request/phút mọi scope **+ contributor + probe** ≤ trần đo (1 giờ); permit chặn khi quá quota. ⚠️ **cooperative-only** (JMS không verify permit; hard-cap tuyệt đối cần proxy/gateway — P0-D). Test: client trung thực **không** vượt trần |

**Cổng 3 — Operations Gate G8–G13 (SAU P1–P4, ngay trước P5 diện rộng — KHÔNG thuộc Exit P0):**
> ⚠️ **Không còn là danh sách mô tả** — mỗi gate phải có **ngưỡng số + phép đo + tiêu chí dừng**. Con số
> có dấu **(owner chốt)** là giá trị kinh doanh do owner ký; con số kỹ thuật đo trong canary.
| # | Tiêu chí | Ngưỡng đo được |
|---|---|---|
| G8 | Provisioning N-project (idempotent) | Provisioner tạo **nhiều** project + apply **toàn bộ** migration (gồm Data API GRANT) + đăng ký Firebase → project sẵn sàng, **desktop SELECT được, Realtime đúng site, Edge verify JWKS OK**, anon bị chặn; **rerun idempotent**, **partial recovery** — 0 bước thủ công |
| G9 | Rotate/revoke/recovery **mọi secret** | Xoay lần lượt: **DB-role passwords, Edge secrets, Management API token, JWT signing key (`kid`), WorkerAccessToken credential, token store DPAPI (relay lại)** + revoke license/`entitlement_version`: token/secret cũ **hết tác dụng ≤ TTL** (+ denylist trước exp), thành phần mới hoạt động, **0 downtime đọc**; recovery mất máy Worker có runbook chạy được |
| **G10** | **Economics / TCO (budget ceiling)** | Bảng chi phí thực đo ở **10/50/100 project** (Pro $25 + $10/Micro ⇒ **~$1,015/tháng @100** baseline + storage/egress/Realtime/Edge/thuế). ⚠️ **PITR KHÔNG mặc định** (xem G12): PITR ~**$100/project/tháng (7 ngày) + yêu cầu tối thiểu Small compute** → nếu bật cho cả 100 project thì **riêng PITR ~$10.000/tháng** (gấp ~10× baseline). Chi phí PITR **chỉ cộng cho số project Tier-B** thực sự bật. **Budget ceiling/site = $___/tháng (owner chốt)**; **alert ở 80%**, **stop-provisioning tự động ở 100%**. Phép chiếu tới **500 project**. |
| **G11** | **Observability + SLO** | Dashboard đa project: **freshness lag** (thời điểm JMS→đọc được) **p95 ≤ ___s (owner chốt)**, **uptime fetch ≥ ___%**, mirror-lag, outbox-depth, dead-letter count, budget. **Alert** khi vi phạm SLO; **1 chỉ số "site khỏe/không"** tổng hợp. Không có ⇒ FAIL. |
| **G12** | **Recovery / DR — PHÂN TẦNG (PITR KHÔNG mặc định)** | ⚠️ **Dữ liệu chia 2 loại theo khả năng rebuild:** **(Tier A — rebuild được, mặc định)** `waybills` tracking/inventory/detail = **tái tạo được từ JMS** vì Worker fetch liên tục → **chỉ cần daily backup + off-site export**; recovery = restore daily backup **rồi re-fetch JMS** để đóng khoảng trống (**KHÔNG PITR** → tiết kiệm ~$100/proj/tháng). **(Tier B — KHÔNG rebuild được, ít project)** dữ liệu người nhập: `order_notes`/`order_checks`/`dispatch_tasks` (+ event workflow) → **cần bảo vệ chặt**: PITR **hoặc** backup tần suất cao/off-site riêng. **RPO/RTO (owner chốt):** Tier A `RPO ≤ 1 ngày` (rebuild tới gần-now qua re-fetch), Tier B `RPO ≤ ___ phút`. **Restore drill THẬT** mỗi tier (khôi phục + re-fetch cho A; PITR/backup cho B) **định kỳ**, có biên bản. Chi phí PITR chỉ cộng cho project Tier-B (G10). |
| **G13** | **Canary rollout (soak + stop + rollback)** | Thả **1 site canary trước**, **soak ≥ ___ ngày (owner chốt)** không vượt SLO/budget; **stop criteria** rõ (SLO vi phạm / lỗi dữ liệu / vượt budget) → **dừng mở rộng**; **rollback drill**: gỡ/tạm dừng 1 project mà không ảnh hưởng site khác (blast radius = 1). Chỉ mở đợt kế khi canary PASS. |

> **CHỐT vào P5:** P5 diện rộng chỉ mở khi **G8–G13 đều PASS** (không còn "G8–G9 là đủ"). G10–G13 là điều
> kiện **bắt buộc** cho project-per-site ở quy mô, không phải tuỳ chọn.

**Cổng PROD — Bộ test bắt buộc TRƯỚC production (owner chốt; nhiều mục trùng G3–G7, liệt kê để không sót):**
| # | Kịch bản | PASS khi |
|---|---|---|
| P-1 | **Mất WebSocket giữa lúc có thay đổi** | reconnect → resubscribe → **delta catch-up** phủ mọi thay đổi bị lỡ; 0 sót (doorbell mất không mất dữ liệu) |
| P-2 | **JWT hết hạn / reconnect** | `SetAuth` refresh → resubscribe → catch-up; RLS vẫn đúng site; không kẹt |
| P-3 | **Đóng UI nhưng Service tiếp tục** | AutoJMS đóng → Worker vẫn fetch + flush outbox bằng token đã lưu |
| P-4 | **Hết toàn bộ token** | `NO_VALID_TOKEN`, service chạy, 0 request JMS; relay token mới → `unknown`→probe→`valid`→tự chạy lại |
| P-5 | **Failover nhiều máy ULTRA** | A hết token → `clear_active_and_release_leader` → B tiếp quản ≤ grace; không 2 bulk song song |
| P-6 | **Worker và Client cùng cập nhật một vận đơn** | qua **một canonical writer**; Worker authoritative đè advisory; workflow CAS `expected_revision` (conflict → refetch), không last-write âm thầm |
| P-7 | **Duplicate event** | dedupe theo `canonical_fingerprint`; promotion/rehash bump `change_seq`, pull-side UPSERT không nhân đôi |
| P-8 | **DELETE / tombstone** | Delete ghi **tombstone vào `datahub_changes`** (operation='delete') cùng txn; client offline pull feed thấy xoá (KHÔNG dựa Realtime DELETE); resurrection = upsert change_seq cao hơn; membership chỉ leader |
| P-9 | **Echo suppression** | dữ liệu remote-applied KHÔNG sinh outbox → không lặp pull↔push |

## Kết luận

P0 **không viết fetcher**. ⚠️ **Thứ tự gate CHUẨN HÓA (High#9 — bỏ mâu thuẫn "ký A–E trước G1" trong khi
D/E phụ thuộc G1):**
```
P0.0  emergency RLS hardening (revoke anon/PUBLIC, fixed search_path) + G0 (inventory integrity + test)
  → ký A–C + FRAMEWORK quyết định D/E (chưa chốt chi tiết phụ thuộc nhánh)
  → G1a (cùng máy, khác process/tài khoản)
  → CHỐT D/E theo nhánh (A: Worker-service / B: proxy)
  → P0.5 (apply schema + migration hardening forward-only)
  → G2–G3  → mở P1
```
Xây P1–P4 → **Cổng 2 (G4–G7)** → **P4.5 Provisioning tooling** → **Cổng 3 Operations (G8–G13)** →
production/P5. Thứ tự đóng hợp đồng: **A (bảo mật) → B (dữ liệu) → C (cursor) → D (leader/token) →
E (spike/vận hành)**; nhưng **D/E chỉ CHỐT sau G1a** (trước G1a chỉ ký framework), còn **A–C + G0 đóng
trước** ở P0.0.
