# Thiết kế Backend AutoJMS DataHub (REST + SignalR + PostgreSQL trên VPS)

> **Trạng thái:** tài liệu *as-built* — mô tả backend **đã hiện diện trong repo** tại
> `src/AutoJMS.DataHub.Api` và `backend/datahub`, không phải đề xuất tương lai.
>
> **Tài liệu nền:** [docs/superpowers/specs/2026-08-20-datahub-vps-baseline-design.md](../superpowers/specs/2026-08-20-datahub-vps-baseline-design.md)
> (thiết kế nền đã được chủ sở hữu phê duyệt — chứa lý do lựa chọn).
> Tài liệu này **không lặp lại lý do**, chỉ chốt lại hợp đồng thực tế đang chạy trong code.
>
> **Hợp đồng máy đọc:** [backend/datahub/openapi/datahub-v1.yaml](../../backend/datahub/openapi/datahub-v1.yaml)
> — khi tài liệu này và file OpenAPI lệch nhau, **OpenAPI + code là chuẩn**.
>
> **Triển khai VPS:** [backend/datahub/deploy/VPS_DEPLOY_GUIDE.vi.md](../../backend/datahub/deploy/VPS_DEPLOY_GUIDE.vi.md)

---

## 1. Mục tiêu và phạm vi

### 1.1 Backend này giải quyết gì

| Nhu cầu | Cách giải quyết |
|---|---|
| Nhiều máy trạm cùng đọc dữ liệu JMS của một site mà không ghi đè lẫn nhau | Ingest một-giao-dịch + fingerprint sự kiện + idempotency key |
| Chỉ một máy được phép fetch hàng loạt tại một thời điểm | Leader lease có fencing (`leader_term` đơn điệu tăng) |
| Dashboard nhiều máy phải thấy dữ liệu gần thực thời | Con trỏ thay đổi theo site (`change_seq`) + SignalR "doorbell" |
| Máy trạm không được giữ credential PostgreSQL | Chỉ có REST/SignalR; DB nằm trong network Docker `internal` |
| Staging và production không được lẫn dữ liệu | Hai deployment độc lập, khoá ký khác nhau, `DATAHUB_CHANNEL` khác nhau |

### 1.2 Ngoài phạm vi (phase 1)

- Không có bảng điều khiển web quản trị.
- Không có multi-node API (mọi tính đúng đắn dựa trên giao dịch PostgreSQL, không dựa trên tiến trình đơn lẻ — nhưng chưa kiểm chứng scale-out).
- Không có replica đọc, không có sharding.
- SignalR **không** truyền dữ liệu nghiệp vụ (xem §9).

### 1.3 Hai mặt phẳng thẩm quyền — không được trộn

```
                MẶT PHẲNG GIẤY PHÉP / CẬP NHẬT          MẶT PHẲNG DỮ LIỆU (tài liệu này)
                ─────────────────────────────           ────────────────────────────────
                Render license server                    DataHub VPS
                Firebase (license/session/tier)          AutoJMS.DataHub.Api + PostgreSQL
                GitHub Releases (Velopack)               SignalR /hubs/site

                cấp: license JWT, tier, module policy     cấp: device token, dữ liệu waybill
                          │                                        ▲
                          └──── license assertion đã ký ───────────┘
                                (channel + site_codes + seats)
```

DataHub **không** đọc Firebase và **không** biết tier. Nó chỉ tin một *license assertion đã ký*
mang theo `Channel`, `SiteCodes`, `Seats`, `TokenVersion`, `ExpiresAt`. Xem §4.1.
Mô tả mặt phẳng giấy phép: [backend-architecture.md](./backend-architecture.md).

---

## 2. Kiến trúc runtime

```
┌── Máy trạm Windows ───────────────┐
│  AutoJMS.exe (WebView2 + WinForms)│
│  DataHubClient / DataHubSyncService│
│  Windows Service (tuỳ chọn)       │
│                                    │
│  Bearer <device token>             │
└───────────────┬───────────────────┘
                │ HTTPS 443 (REST) + WSS 443 (/hubs/site)
                ▼
┌── VPS (một host, Docker Compose) ─────────────────────────────────────┐
│                                                                       │
│  ┌─────────────────────┐   network: edge                              │
│  │ caddy:2.10-alpine   │  ports 80/443 ← service DUY NHẤT publish port│
│  │ TLS tự động (ACME)  │  admin off                                   │
│  │ /hubs/site: no-buffer│                                             │
│  └──────────┬──────────┘                                              │
│             │ http://api:8080                                         │
│  ┌──────────▼──────────────────────┐  network: edge + data            │
│  │ AutoJMS.DataHub.Api (net10.0)   │  expose 8080 (không publish)     │
│  │ Kestrel, body ≤ 1 MiB           │  mem 768m / cpus 1.5             │
│  │ RetentionHostedService (nền)    │  healthcheck /health/ready       │
│  └──────────┬──────────────────────┘                                  │
│             │ Host=postgres;Port=5432                                 │
│  ┌──────────▼──────────┐   network: data (internal: true)             │
│  │ postgres:16-alpine  │  KHÔNG publish 5432 ra host                  │
│  │ volume postgres_data│  mem 2g / cpus 1.5 / shm 256m                │
│  └─────────────────────┘                                              │
└───────────────────────────────────────────────────────────────────────┘
```

**Bất biến về topology** (được `backend/datahub/tests/deployment-static-smoke.ps1` kiểm tra tự động):

1. `postgres` không có khối `ports:` — không thể truy cập DB từ ngoài host.
2. Chỉ `caddy` publish `80:80` và `443:443`.
3. Network `data` là `internal: true`.
4. `caddy` chỉ start khi `api` **healthy**; `api` chỉ start khi `postgres` **healthy**.
5. Compose trên VPS **không** có `build:` — chỉ tiêu thụ image đã build sẵn, pin theo `@sha256`.
6. Mọi container có log rotation (`max-size` + `max-file`).

File: [docker-compose.yml](../../backend/datahub/docker-compose.yml),
[Caddyfile](../../backend/datahub/Caddyfile), [Dockerfile](../../backend/datahub/Dockerfile).

---

## 3. Pipeline HTTP — thứ tự middleware là một hợp đồng

Từ [Program.cs](../../src/AutoJMS.DataHub.Api/Program.cs):

```
UseExceptionHandler        → mọi exception chưa bắt ⇒ 503 SERVICE_UNAVAILABLE (không rò stack trace)
UseForwardedHeaders       → XForwardedFor|XForwardedProto, ForwardLimit = 1
                             KnownIPNetworks/KnownProxies đã Clear() vì Caddy là hop duy nhất
IngressRateLimitMiddleware → giới hạn theo IP (600/phút) TRƯỚC khi validate token
DeviceAuthenticationMiddleware → parse + verify device token (thuần CPU, KHÔNG chạm DB)
UseRateLimiter            → policy "device" 240/phút/thiết bị, "enrollment" 10/phút/IP
DeviceStatusMiddleware    → cập nhật last_seen (chạm DB) — SAU khi đã qua rate limit
```

Thứ tự này là chủ ý và được assert trong `deployment-static-smoke.ps1`:

- Rate limit theo IP **trước** khi verify token ⇒ credential rác không đốt CPU/DB.
- Verify token **không** chạm PostgreSQL ⇒ token giả không tạo tải DB.
- Ghi `last_seen` **sau** limiter theo thiết bị ⇒ một thiết bị lỗi không spam DB.

Cấu hình khác:

- `MaxRequestBodySize = 1 MiB` (Kestrel) — vượt ⇒ `413`.
- `JsonUnmappedMemberHandling.Disallow` — field lạ ngoài schema OpenAPI ⇒ `400`.
- `AddSignalR(EnableDetailedErrors = false)`.
- Hub gắn policy rate limit thiết bị: `MapHub<SiteHub>("/hubs/site").RequireRateLimiting("device")`.

| Tầng | Hạn mức | Phân vùng | Header trả về |
|---|---|---|---|
| Ingress | 600 / phút | IP client (sau forwarded headers) | `Retry-After: 60` |
| Thiết bị | 240 / phút | `device:{deviceId}` (fallback `ip:`) | `Retry-After: 60` |
| Enrollment | 10 / phút | `enroll:{ip}` | `Retry-After: 60` |

Vượt hạn mức ⇒ `429` với body `{ code: "RATE_LIMITED", ... }`.

---

## 4. Định danh và xác thực thiết bị

### 4.1 Chuỗi tin cậy

```
License assertion đã ký            Enrollment                    Mọi request sau đó
(từ mặt phẳng giấy phép)     POST /api/v1/devices/enroll         Bearer <device token>
  Channel                     ── xác minh chữ ký assertion
  SiteCodes[]                  ── SiteCode phải nằm trong scope
  Seats                        ── đếm seat dưới row lock của site
  TokenVersion                 ── tạo device row + credential_hash
  ExpiresAt                    ── phát device token
  DataHubUrl (tuỳ chọn)
```

Assertion được gửi **ở chính header `Authorization: Bearer <assertion>`** — đây là operation duy
nhất mà `Authorization` mang assertion thay vì device token. `/api/v1/devices/enroll` là endpoint
**anonymous duy nhất** ngoài `/health/*`.

Sau khi enroll xong, assertion **không** được dùng cho bất kỳ request nào khác; mọi request tiếp
theo dùng device token dẫn xuất. `siteCode` trong body chỉ là *bộ chọn site*, không phải quyền:
nó phải nằm trong `site_codes` của assertion đã ký. Enrollment **không bao giờ tạo site** —
site phải được provision trước (§5).

### 4.2 Định dạng device token

Token HMAC tự định nghĩa (không phải JWT), do
[HmacDeviceTokenService](../../src/AutoJMS.DataHub.Api/Auth/HmacDeviceTokenService.cs) phát và xác minh:

```
v1.<base64url(payload JSON)>.<base64url(HMACSHA256(key, "v1." + payloadPart))>
```

Claims trong payload: `DeviceId`, `SiteId`, `Channel`, `Role`, `TokenVersion`,
`ExpiresAt`, `Issuer`, `Audience`.

Ràng buộc:

- Khoá ký (`DATAHUB_DEVICE_TOKEN_SIGNING_KEY`) **≥ 32 byte**; thiếu ⇒ service không ready.
- So sánh chữ ký bằng `CryptographicOperations.FixedTimeEquals`.
- Kiểm `Issuer`, `Audience`, `ExpiresAt`.
- Mã lỗi nội bộ: `TOKEN_UNAVAILABLE`, `TOKEN_MALFORMED`, `TOKEN_INVALID`, `TOKEN_EXPIRED`
  — tất cả trả ra ngoài dưới dạng `401 UNAUTHORIZED` (không tiết lộ lý do chi tiết).
- Bản ghi thiết bị lưu `credential_hash = HMACSHA256(DATAHUB_ENROLLMENT_PEPPER, token)` (hex),
  **không** lưu token thô. Pepper cũng phải ≥ 32 ký tự.

### 4.3 Cách gửi token

| Kênh | Cách gửi |
|---|---|
| REST | `Authorization: Bearer <token>` |
| SignalR | `Authorization: Bearer <token>`, hoặc `?access_token=` **chỉ** trên đường dẫn `/hubs/site` |

Query `access_token` được chấp nhận vì WebSocket trong browser/một số client không đặt được
header — [DeviceAuthenticationMiddleware](../../src/AutoJMS.DataHub.Api/Auth/DeviceAuthenticationMiddleware.cs)
chỉ cho phép nó trên `/hubs/site` và **không bao giờ log giá trị này**.

### 4.4 Phân quyền tenant

[TenantAuthorizationEvaluator](../../src/AutoJMS.DataHub.Api/Auth/AuthContracts.cs) kiểm theo thứ tự:

1. `token.Channel != DATAHUB_CHANNEL` ⇒ `403 CHANNEL_MISMATCH`
   (token staging không dùng được trên production và ngược lại).
2. `token.SiteId != {siteId}` trong route ⇒ `403 SITE_NOT_LICENSED`.

Mọi endpoint dữ liệu đều nằm dưới `/api/v1/sites/{siteId}/...`, nên `siteId` luôn được đối chiếu
với token. Ingest còn **ghi đè** `SiteId` của từng item bằng `siteId` trong route — client không thể
chèn dữ liệu cho site khác dù cố tình.

### 4.5 Validator license assertion — production đang fail-closed

[IdentityServiceCollectionExtensions](../../src/AutoJMS.DataHub.Api/Auth/IdentityServiceCollectionExtensions.cs):

- Issuer test HMAC chỉ được đăng ký khi `StagingTestIssuerPolicy.IsEnabled(env, flag)` **và**
  `DATAHUB_CHANNEL == "staging"`.
- Ngoài trường hợp đó, DI đăng ký `UnavailableLicenseAssertionValidator` ⇒
  `/api/v1/devices/enroll` trả `503`.

**Hệ quả:** production **chưa** enroll được thiết bị cho tới khi có adapter xác minh assertion
bất đối xứng (JWS/JWKS). Đây là lỗ hổng chức năng đã biết, có chủ ý — xem §13.

---

## 5. Mô hình dữ liệu (phase 1)

DDL: [001_core.sql](../../backend/datahub/migrations/001_core.sql) → `005_change_retention_floor.sql`.

| Bảng | Vai trò | Khoá / bất biến chính |
|---|---|---|
| `schema_migrations` | sổ ghi migration đã chạy | `version` unique |
| `sites` | tenant | `site_code` unique, `seats` |
| `devices` | thiết bị đã enroll | `credential_hash`, `token_version`, `last_seen_at` |
| `site_fetch_leases` | leader lease | `leader_term` bigint đơn điệu tăng, `lease_expires_at` |
| `site_change_counters` | con trỏ đổi theo site | `change_seq`, `pruned_through_seq` (`0 ≤ pruned ≤ change_seq`) |
| `waybill_scan_events` | quan sát thô (append-only) | unique `(site_id, event_fingerprint)` |
| `waybill_projections` | trạng thái hiện tại của waybill | 3 slot độc lập (§7.4) |
| `jms_event_policies` | map mã JMS → loại sự kiện | seed ở `002_seed_policies.sql` |
| `dashboard_changes` | log delta cho client | PK `(site_id, change_seq)` |
| `idempotency_records` | chống ghi trùng | PK `(site_id, key)`, có `body_hash`, `expires_at` |
| `retention_policies` | đồng hồ xoá dữ liệu (là *data*, không phải code) | unique riêng cho hàng global (`site_id IS NULL`) và hàng theo site |
| `audit_logs` | dấu vết thao tác | `at`, `site_id` |

Helper `create_datahub_site(p_site_id uuid, p_site_code text)` seed **nguyên tử** cả ba thứ:
hàng `sites`, hàng `site_fetch_leases`, hàng `site_change_counters`. Không bao giờ tạo site bằng
`INSERT` tay — thiếu counter thì mọi ingest của site đó sẽ lỗi.

---

## 6. Hợp đồng thời gian

Chỉ **một** mốc thời gian nghiệp vụ: `scanTime`.

| Đầu vào | Cách hiểu |
|---|---|
| `yyyy-MM-dd HH:mm:ss` (naive, từ JMS) | múi giờ `Asia/Ho_Chi_Minh` |
| ISO có `Z` hoặc có offset rõ ràng | dùng đúng offset đó |
| Không parse được | **từ chối request** — tuyệt đối không thay bằng thời điểm hiện tại |

`uploadTime` chỉ được lưu bên trong object `payload` thô. Nó **không** tham gia sắp xếp,
fingerprint, reduce projection, hay retention. Lý do: JMS có thể trả `uploadTime` khác nhau cho
cùng một sự kiện, làm fingerprint mất tính ổn định.

---

## 7. Ghi dữ liệu: lease → ingest → change

### 7.1 Leader lease (fencing)

[LeaseRepository](../../src/AutoJMS.DataHub.Api/Infrastructure/LeaseRepository.cs):

| Tham số | Giá trị |
|---|---|
| Thời hạn lease | 120 giây |
| Chu kỳ renew khuyến nghị | 30 giây |
| `leader_term` | bigint, **chỉ tăng** |

| Thao tác | Hiệu ứng |
|---|---|
| `acquire` | nếu lease trống/hết hạn: `leader_term += 1`, gán owner; nếu đang giữ bởi thiết bị khác: `409 LEASE_HELD` |
| `renew` | gia hạn `lease_expires_at`, **không** tăng term; term sai ⇒ `409 LEADER_FENCED` |
| `release` | `leader_term += 1`, owner = NULL, `lease_expires_at = -infinity` |

`release` tăng term để một leader "zombie" (bị treo GC/mạng) không thể quay lại ghi dữ liệu bằng
term cũ. `-infinity` của Npgsql được map về `null` ở biên HTTP.

### 7.2 Hai đường ingest

| Endpoint | Fencing | Dùng khi |
|---|---|---|
| `POST /api/v1/sites/{siteId}/jms/ingest` | **Bắt buộc** header `X-Leader-Term`; thiếu/sai ⇒ `409 LEADER_FENCED` | fetch hàng loạt bởi leader |
| `POST /api/v1/sites/{siteId}/jms/observations` | Không fence | thao tác tương tác của người dùng (scan lẻ, tra cứu) |

Cả hai đi qua **cùng một** pipeline `IngestPipeline → IngestRepository.IngestAsync` với
`requireFence` khác nhau. Không có nhánh logic ghi riêng ⇒ không thể lệch hành vi.

### 7.3 Giao dịch ingest (một transaction duy nhất)

[IngestRepository](../../src/AutoJMS.DataHub.Api/Infrastructure/IngestRepository.cs), tối đa
**200 item**/request:

```
BEGIN
 1. Kiểm fence (nếu requireFence):
      lease_expires_at > clock_timestamp() AND leader_term = @term
      ── dùng clock_timestamp(), KHÔNG dùng now(): now() là thời điểm bắt đầu
         transaction, một transaction dài có thể vượt qua fence đã hết hạn.
 2. Reserve idempotency:
      INSERT ... ON CONFLICT DO NOTHING RETURNING 1
      ── không reserve được ⇒ so body_hash:
           khác  ⇒ 409 IDEMPOTENCY_KEY_REUSED
           giống ⇒ trả lại kết quả cũ (replayed), hoặc 409 IDEMPOTENCY_IN_PROGRESS
 3. SELECT ... FROM site_change_counters WHERE site_id = @s FOR UPDATE
 4. Với từng item:
      INSERT INTO waybill_scan_events
        ON CONFLICT (site_id, event_fingerprint) DO NOTHING RETURNING id
      ── không RETURNING ⇒ trùng, bỏ qua
      Reduce projection (§7.4) → UPSERT waybill_projections
      Nếu projection đổi ⇒ change_seq += 1, INSERT dashboard_changes
 5. Kiểm fence LẦN NỮA với FOR UPDATE (nếu requireFence)
 6. Ghi kết quả vào idempotency_records
COMMIT
 7. Publish doorbell (ngoài transaction, best-effort)
```

Thứ tự **fence trước idempotency** là chủ ý: một leader đã bị fence không được phép "chiếm"
idempotency key. Kiểm fence lần hai (bước 5) đóng cửa sổ giữa lúc kiểm lần đầu và lúc commit.

Response: `IngestResponse(siteId, accepted, duplicates, changed, replayed, firstSeq, lastSeq)`.

### 7.4 Fingerprint và projection

- **Fingerprint v1** (`EventFingerprintV1.Compute`) băm các trường định danh sự kiện, **loại trừ**
  `uploadTime`. Khoá thắng khi so sánh: `(event_occurred_at, event_fingerprint)`.
- **Ba slot projection độc lập**, mỗi slot có winner riêng:

| Slot | Nhận sự kiện loại | Ý nghĩa |
|---|---|---|
| `state_*` | `state_transition` | trạng thái vận đơn |
| `last_activity_*` | **mọi** loại | hoạt động gần nhất |
| `inventory_*` | `inventory` | tồn kho |

Mã JMS không có trong `jms_event_policies` mặc định là `activity` — dữ liệu lạ vẫn được lưu và
vẫn cập nhật `last_activity_*`, chỉ không làm nhiễu `state_*`/`inventory_*`.

### 7.5 Idempotency

- Header `Idempotency-Key` **bắt buộc** cho cả hai endpoint ingest, dài 8–128 ký tự.
- Khoá được so kèm SHA-256 của body ⇒ dùng lại key với body khác là lỗi, không phải "ghi đè".
- Mẫu *reserve-then-fill*: reserve trước khi làm việc, ghi kết quả sau khi thành công. Nếu tiến
  trình chết giữa đường, retry gặp `IDEMPOTENCY_IN_PROGRESS` cho tới khi bản ghi hết hạn — không
  bao giờ ghi đôi.

---

## 8. Đọc dữ liệu: snapshot + delta

[ChangeRepository](../../src/AutoJMS.DataHub.Api/Infrastructure/ChangeRepository.cs),
[SyncEndpoints](../../src/AutoJMS.DataHub.Api/Endpoints/SyncEndpoints.cs):

| Endpoint | Trả về |
|---|---|
| `GET /api/v1/sites/{siteId}/projections/snapshot` | toàn bộ projection + **một** `snapshotSeq` |
| `GET /api/v1/sites/{siteId}/changes?after=&limit=` | `ChangePage(siteId, after, items, hasMore, next)` |

Chi tiết:

- Cả hai chạy ở isolation **REPEATABLE READ** với `SET LOCAL statement_timeout = '30s'`
  ⇒ snapshot và `snapshotSeq` luôn nhất quán với nhau.
- `limit` mặc định 500, clamp về `[1, 500]`. Query lấy `limit + 1` hàng để suy ra `hasMore`
  mà không cần `COUNT(*)`.
- **`409 RESYNC_REQUIRED`** khi con trỏ không còn phục vụ được:
  `after < pruned_through_seq || after > change_seq`
  ([ChangeCursorWindow](../../src/AutoJMS.DataHub.Api/Domain/ChangeCursorWindow.cs)).

Vòng đời client đúng:

```
snapshot (lấy snapshotSeq)
   └─► changes?after=snapshotSeq   ◄── lặp lại mỗi lần nhận doorbell
          ├─ 200 + hasMore=true ⇒ gọi lại ngay với next
          └─ 409 RESYNC_REQUIRED ⇒ quay về snapshot
```

`pruned_through_seq` tồn tại vì `MIN(change_seq)` không phân biệt được "lịch sử nguyên vẹn bắt đầu
từ 1" với "lịch sử đã bị retention cắt". Không có cột này thì client có thể *âm thầm* bỏ sót delta.

---

## 9. SignalR: doorbell, không phải kênh dữ liệu

[SiteHub](../../src/AutoJMS.DataHub.Api/Hubs/SiteHub.cs),
[SignalRDoorbellPublisher](../../src/AutoJMS.DataHub.Api/Services/SignalRDoorbellPublisher.cs):

| Hạng mục | Giá trị |
|---|---|
| Đường dẫn hub | `/hubs/site` |
| Xác thực | device token; **không có identity ⇒ abort connection** |
| Group | `site:{siteId:D}` — client chỉ nhận tín hiệu của đúng site mình |
| Tên method phía client | `change` |
| Payload | `ChangeDoorbell` (site + con trỏ), **không** chứa dữ liệu waybill |

Doorbell được publish **sau khi commit**, trong `try/catch`; thất bại chỉ ghi log
([IngestEndpoints](../../src/AutoJMS.DataHub.Api/Endpoints/IngestEndpoints.cs)).

Đây là điểm thiết kế quan trọng: **mất doorbell không mất dữ liệu.** Client vẫn lấy được delta
bằng `GET /changes?after=`. Vì vậy SignalR là *tối ưu độ trễ*, không phải thành phần bắt buộc để
đúng. Client nên poll dự phòng theo chu kỳ chậm.

Caddy proxy `/hubs/site` với `flush_interval -1` (tắt buffering) và `read_timeout`/`write_timeout`
= 1 giờ để WebSocket dài không bị cắt.

---

## 10. Bề mặt API phase 1

| Method | Đường dẫn | Auth | Ghi chú |
|---|---|---|---|
| `GET` | `/health/live` | anonymous | chỉ báo tiến trình còn sống |
| `GET` | `/health/ready` | anonymous | check `runtime-configuration` + `postgres`; trả cả `channel` |
| `POST` | `/api/v1/devices/enroll` | `Authorization: Bearer <assertion>` | 10/phút/IP; `503` nếu validator không khả dụng |
| `POST` | `/api/v1/sites/{siteId}/lease/acquire` | device | `409 LEASE_HELD` |
| `POST` | `/api/v1/sites/{siteId}/lease/renew` | device | body `{ leaderTerm }`; `409 LEADER_FENCED` |
| `POST` | `/api/v1/sites/{siteId}/lease/release` | device | tăng term |
| `POST` | `/api/v1/sites/{siteId}/jms/ingest` | device | cần `Idempotency-Key` + `X-Leader-Term` |
| `POST` | `/api/v1/sites/{siteId}/jms/observations` | device | cần `Idempotency-Key`, không fence |
| `GET` | `/api/v1/sites/{siteId}/changes` | device | `after`, `limit` |
| `GET` | `/api/v1/sites/{siteId}/projections/snapshot` | device | `snapshotSeq` |
| `—` | `/hubs/site` (+ `/negotiate`) | device | SignalR |

### Mã lỗi

| HTTP | `code` | Nguyên nhân |
|---|---|---|
| 400 | `BAD_REQUEST` | body sai schema (kể cả field lạ), `scanTime` không parse được, thiếu `Idempotency-Key` |
| 401 | `UNAUTHORIZED` | token thiếu / sai / hết hạn |
| 403 | `CHANNEL_MISMATCH` | token của channel khác |
| 403 | `FORBIDDEN` | role không đủ |
| 403 | `SITE_NOT_LICENSED` | `siteId` không thuộc token hoặc ngoài scope assertion |
| 404 | `NOT_FOUND` | site chưa được provision |
| 409 | `LEASE_HELD` | thiết bị khác đang giữ lease |
| 409 | `LEADER_FENCED` | term sai hoặc lease đã hết hạn |
| 409 | `IDEMPOTENCY_KEY_REUSED` | cùng key, khác body |
| 409 | `IDEMPOTENCY_IN_PROGRESS` | request trùng đang chạy |
| 409 | `RESYNC_REQUIRED` | con trỏ ngoài cửa sổ khả dụng |
| 409 | — | enrollment: `SEAT_LIMIT_REACHED` hoặc `DEVICE_CONFLICT` |
| 413 | — | body > 1 MiB |
| 422 | `VALIDATION_FAILED` | body đúng schema nhưng vi phạm ràng buộc nghiệp vụ |
| 429 | `RATE_LIMITED` | vượt hạn mức; kèm `Retry-After` |
| 503 | `SERVICE_UNAVAILABLE` | DB không sẵn sàng, cấu hình thiếu, hoặc exception chưa bắt |

Mọi lỗi đi qua `ApiProblemWriter` ⇒ hình dạng JSON thống nhất, không rò stack trace.

---

## 11. Retention và phục hồi

[RetentionRepository](../../src/AutoJMS.DataHub.Api/Infrastructure/RetentionRepository.cs) +
[RetentionHostedService](../../src/AutoJMS.DataHub.Api/Services/RetentionHostedService.cs):

- Chính sách là **data** (`retention_policies`), nhưng **tên bảng/cột không bao giờ được nội suy
  từ dữ liệu** — chỉ 4 "đồng hồ" nằm trong allow-list code.
- Mặc định seed (`003_seed_retention.sql`): `waybill_scan_events` 60 ngày,
  `dashboard_changes` 14 ngày, `audit_logs` 90 ngày. Hàng theo site ghi đè hàng global.
- Mỗi loại chạy **transaction ngắn riêng**. Gộp chung sẽ tạo thứ tự lock nghịch với ingest
  (ingest lock: idempotency → counter → event) và gây deadlock.
- Dùng `pg_try_advisory_xact_lock('autojms.datahub.retention')` ⇒ không hai pass chồng nhau.
- Xoá `dashboard_changes` **chỉ theo prefix toàn-cũ**, rồi đẩy `pruned_through_seq` lên.
  Nếu có hàng mới với `change_seq` thấp hơn, toàn bộ hàng sau nó được giữ lại. Nhờ vậy client
  hoặc phục hồi đầy đủ, hoặc nhận `RESYNC_REQUIRED` — **không có trạng thái mất delta âm thầm**.
- Chu kỳ: `DATAHUB_RETENTION_INTERVAL_SECONDS` (mặc định 900). Lỗi chỉ log `Warning`, không
  bao giờ hạ API.

**Phục hồi:** `pg_dump --format=custom` theo lịch; restore bằng
`restore-postgres.ps1` với `--single-transaction --exit-on-error`, và bắt buộc `-AllowExistingData`
mới được `--clean --if-exists`. Mục tiêu "< 30 phút" là **mục tiêu diễn tập**, không phải SLA.

---

## 12. Cấu hình runtime

[DataHubRuntimeOptions](../../src/AutoJMS.DataHub.Api/Configuration/DataHubRuntimeOptions.cs).
Giá trị ngoài biên bị clamp; thiếu biến bắt buộc ⇒ `/health/ready` báo unhealthy.

| Biến | Bắt buộc | Mặc định | Biên |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | ✅ | — | `Staging` \| `Production` |
| `DATAHUB_CHANNEL` | ✅ | — | `staging` \| `production` |
| `DATAHUB_PUBLIC_HOST` | ✅ (Caddy) | — | hostname DNS |
| `TLS_CONTACT_EMAIL` | ✅ (Caddy) | — | email ACME |
| `DATAHUB_API_IMAGE` | ✅ | — | **phải** `...@sha256:<64 hex>` |
| `POSTGRES_DB` / `POSTGRES_USER` / `POSTGRES_PASSWORD` | ✅ | — | — |
| `DATAHUB_DEVICE_TOKEN_ISSUER` / `_AUDIENCE` | ✅ | — | — |
| `DATAHUB_DEVICE_TOKEN_SIGNING_KEY` | ✅ | — | ≥ 32 byte |
| `DATAHUB_ENROLLMENT_PEPPER` | ✅ | — | ≥ 32 ký tự |
| `DATAHUB_LICENSE_ASSERTION_ISSUER` / `_AUDIENCE` | ✅ | — | — |
| `DATAHUB_LICENSE_ASSERTION_VALIDATION_KEY` | production | trống | trống ⇒ enroll `503` |
| `DATAHUB_STAGING_TEST_SIGNING_KEY` | chỉ staging | trống | ≥ 32 ký tự |
| `DATAHUB_ALLOW_STAGING_TEST_ISSUER` | chỉ staging | `false` | chỉ có tác dụng khi channel = `staging` |
| `DATAHUB_DB_MAX_POOL_SIZE` | ❌ | 20 | 1–100 |
| `DATAHUB_DEVICE_TOKEN_LIFETIME_SECONDS` | ❌ | 86400 | 300–2592000 |
| `DATAHUB_RETENTION_INTERVAL_SECONDS` | ❌ | 900 | 60–86400 |
| `DATAHUB_RETENTION_BATCH_SIZE` | ❌ | 1000 | 100–5000 |

Template: [env.staging.template](../../backend/datahub/env.staging.template),
[env.production.template](../../backend/datahub/env.production.template).
**Staging và production phải sinh secret độc lập** — không copy khoá giữa hai môi trường.

---

## 13. Lỗ hổng đã biết (chưa làm, có chủ ý)

| # | Lỗ hổng | Ảnh hưởng | Ghi chú |
|---|---|---|---|
| 1 | Chưa có adapter xác minh assertion bất đối xứng (JWS/JWKS) | **Production chưa enroll được thiết bị** (`503`) | Fail-closed là đúng; cần làm trước khi go-live production |
| 2 | Không có client SignalR trong `src/AutoJMS` (không có `HubConnection`) | Desktop chưa nhận doorbell ⇒ độ trễ = chu kỳ poll | Không ảnh hưởng tính đúng (§9) |
| 3 | `docs/architecture/backend-architecture.md` mô tả mặt phẳng dữ liệu theo kiến trúc cũ (RPC kiểu Supabase) | Dễ gây hiểu sai | Đã thêm con trỏ sang tài liệu này |
| 4 | `backend/BACKEND_DEPLOY_STATUS.md` lặp `DATAHUB_API_BASE_URL` 3 lần (dòng 75–77) | Nhiễu tài liệu | Sửa nhỏ, ngoài phạm vi lần này |
| 5 | Chưa kiểm chứng API multi-instance | Không scale-out ngang được | Mọi bất biến dựa trên transaction, nhưng chưa test |

---

## 14. Bản đồ kiểm chứng

| Kiểm gì | Bằng gì |
|---|---|
| Bất biến topology + thứ tự middleware + không có secret trong Dockerfile | `backend/datahub/tests/deployment-static-smoke.ps1` |
| Catalog schema sau migration | `backend/datahub/tests/001_core_catalog_assertions.sql`, `001_core_smoke.ps1` |
| Map policy sự kiện JMS | `backend/datahub/tests/002_policy_smoke.ps1` |
| Hành vi retention | `backend/datahub/tests/003_retention_smoke.ps1` |
| Hợp đồng OpenAPI | `backend/datahub/openapi/openapi-lint.ps1` |
| Logic API (unit/integration) | `tests/AutoJMS.DataHub.Api.Tests` |

---

## 15. Đọc tiếp

- Triển khai VPS từng bước: [VPS_DEPLOY_GUIDE.vi.md](../../backend/datahub/deploy/VPS_DEPLOY_GUIDE.vi.md)
- Thiết kế nền (lý do lựa chọn): [2026-08-20-datahub-vps-baseline-design.md](../superpowers/specs/2026-08-20-datahub-vps-baseline-design.md)
- Kế hoạch triển khai: [2026-08-20-datahub-vps-backend-plan.md](../superpowers/plans/2026-08-20-datahub-vps-backend-plan.md)
- Ghi chú vận hành ngắn (EN): [backend/datahub/README.md](../../backend/datahub/README.md)
- Mặt phẳng giấy phép / cập nhật: [backend-architecture.md](./backend-architecture.md)
