# Đề xuất schema v2 cho `config-key` và `tier-definitions`

**Trạng thái: ĐỀ XUẤT — chưa sửa file cấu hình production nào.**
Chủ sở hữu yêu cầu "gợi ý viết lại config … chuyên nghiệp hơn", nên tài liệu này
trình bày thiết kế để chốt trước; không có file nào trong
`backend/firebase/` hay `src/AutoJMS/tier-definitions.json` bị đổi ở commit này.

- Ngày: 2026-08-24
- Rủi ro nền: xem [FULLSTACK_BACKEND_RISK_REVIEW.md](./FULLSTACK_BACKEND_RISK_REVIEW.md) mục **K**
- Ràng buộc: **phải khớp backend hiện tại** (`backend/render-license-server/server.js`,
  `src/AutoJMS.DataHub.Api`, `VpsRuntimePolicyService`, `TierConfig`)

---

## 0. Năm nguyên tắc thiết kế

1. **Cộng thêm, không đổi tên.** Mọi field v1 giữ nguyên tên và ý nghĩa, nên
   server hiện tại chạy được với bản ghi v2 mà không cần deploy trước. Deploy
   server chỉ cần cho các tính năng *mới* (hết hạn, allowlist tier).
2. **Field nào server đọc thì để phẳng.** `server.js` đọc `data.<field>` trực
   tiếp; gom vào object lồng là buộc phải sửa server *và* migrate mọi key đang
   chạy cùng lúc. Chỉ những field **server không đọc** (metadata khách hàng, vết
   audit) mới được để lồng.
3. **Thời gian là ISO-8601 UTC.** `"2027-05-26T00:00:00Z"` — so sánh được, sắp
   xếp được theo thứ tự chữ, không mơ hồ múi giờ. Định dạng
   `"26-05-2026 01:22"` hiện tại không có cái nào trong ba tính chất đó.
4. **Thẩm quyền tier vẫn nằm ở license.** Không có gì trong hai schema này được
   phép *nâng* quyền. `tier-definitions.json` và `runtime-policy.json` chỉ
   **thu hẹp** — đúng bất biến đã thi hành ở `TierRuntimePolicy.Resolve`.
5. **Fail-closed khi thiếu, tương thích ngược khi vắng.** Thiếu `siteCode` =
   không enroll được (đã đúng). Vắng `expiresAt` = key cũ, không hết hạn (để
   không khoá oan khách đang dùng).

---

## PHẦN 1 — `config-key` v2 (bản ghi `Licenses/{licenseKey}` trên Firebase)

### 1.1 Backend hiện tại đã đọc nhiều field hơn template ghi

Đây là phát hiện đáng giá nhất của phần này: **server đã hỗ trợ một schema giàu
hơn hẳn cái template đang mô tả.** Sáu field dưới đây `server.js` đọc rồi, nhưng
`backend/firebase/config-key.json` không có:

| Field server đã đọc | Ở đâu | Hệ quả của việc template thiếu nó |
|---|---|---|
| `siteCodes` (array) | `server.js:163-173` | Không có → fallback về `middleCode` → **K.5**, nhiều license dùng chung tenant `"0000"` |
| `siteCode` (string) | `server.js:163-173` | như trên |
| `siteId` | `server.js:703` | Client cũ đọc field này |
| `seats` | `server.js:196` (chặn 1..500) | Không có → mặc định `DATAHUB_ASSERTION.DEFAULT_SEATS`, không ai biết key được mấy máy |
| `tokenVersion` | `server.js:197` (chặn 1..1e6) | Không có → luôn là 1, mất đường vô hiệu hoá device token hàng loạt |
| `updateChannel` | `server.js:698` | Không có → luôn là `CONFIG.DEFAULT_CHANNEL`, không đặt được kênh beta cho từng khách |

Và một field **server tự ghi vào** mà template không khai: `activatedAt`
(`server.js:598`, đang là epoch ms).

Nên phần lớn công việc của v2 không phải là "thêm tính năng", mà là **viết ra
đúng những gì backend đã làm được**, cộng thêm `expiresAt` là thứ thật sự còn
thiếu.

### 1.2 Schema v2

```jsonc
{
  // ---------- định danh & phiên bản schema ----------
  "schemaVersion": 2,

  // ---------- vòng đời ----------
  "status": "active",                      // active | suspended | revoked   (server: chỉ "active" đi qua)
  "createdAt": "2026-05-26T01:22:00Z",     // do người cấp key ghi
  "activatedAt": null,                     // server ghi lần bind HWID đầu tiên
  "expiresAt": "2027-05-26T00:00:00Z",     // MỚI — vắng = không hết hạn (key cũ)
  "graceDays": 7,                          // MỚI — số ngày còn cho chạy sau expiresAt, kèm cảnh báo
  "offlineGraceHours": 72,                 // MỚI — trần thời gian chạy khi mất mạng

  // ---------- tier ----------
  "tier": "ULTRA",                         // BASE | ULTRA  (chỉ hai giá trị này, xem S2)

  // ---------- ràng buộc máy & chỗ ngồi ----------
  "hwid": "",                              // "" = chưa bind; server tự bind lần đầu
  "hwidBoundAt": null,                     // MỚI — server ghi cùng lúc với hwid
  "hwidResetCount": 0,                     // MỚI — đếm số lần người cấp key reset máy cho khách
  "seats": 1,                              // 1..500
  "tokenVersion": 1,                       // tăng lên để vô hiệu hoá mọi device token cũ của key

  // ---------- tenant DataHub ----------
  "siteCode": "HN01",                      // BẮT BUỘC từ v2 — không được để trống, xem K.5
  "siteCodes": ["HN01"],                   // nhiều site cho khách nhiều chi nhánh
  "siteId": "",                            // GUID site, để trống nếu chưa enroll lần nào
  "middleCode": "HN01",                    // KHÔNG còn để "0000"

  // ---------- tích hợp ----------
  "dataSpreadsheetId": "1AbC...",          // sheet dữ liệu của riêng khách này
  "updateChannel": "stable",               // stable | beta
  "minAppVersion": "1.0.0",                // MỚI — chặn build cũ hơn (cần S3 + C1)
  "skipHashCheck": false,                  // v1 mặc định true; v2 mặc định false, xem 1.5

  "modulePolicy": {
    "autoUpdate": false,
    "silentUpdate": true,
    "applyOnNextStartup": true
  },

  // ---------- server KHÔNG đọc: metadata cho người quản trị ----------
  "meta": {
    "customerName": "Cửa hàng ABC",
    "contact": "0900000000",
    "orderId": "",
    "issuedBy": "owner",
    "notes": ""
  },

  // ---------- server ghi, chỉ để tra cứu ----------
  "audit": {
    "lastVerifyAt": null,                  // ISO-8601
    "lastVerifyIp": "",
    "lastAppVersion": "",                  // cần C-app gửi appVersion, xem K.10.1
    "verifyCount": 0
  }
}
```

### 1.3 Từ điển field

| Field | Loại | Bắt buộc | Ai ghi | Ai đọc |
|---|---|---|---|---|
| `schemaVersion` | int | v2 | người cấp key | chưa ai — dùng để migrate về sau |
| `status` | enum | ✅ | người cấp key | `server.js:538`, `:758`, `:982` |
| `createdAt` | ISO | ✅ | người cấp key | chưa ai — hồ sơ |
| `activatedAt` | ISO | — | **server** | chưa ai — hồ sơ |
| `expiresAt` | ISO | nên có | người cấp key | **S1 (mới)** |
| `graceDays` | int | — | người cấp key | **S1 (mới)** |
| `offlineGraceHours` | int | — | người cấp key | **S5 (mới)** |
| `tier` | enum | ✅ | người cấp key | `server.js:545` → toàn bộ phân quyền client |
| `hwid` | string | ✅ (rỗng được) | server (bind) | `server.js:584-603` |
| `seats` | int | nên có | người cấp key | `server.js:196` |
| `tokenVersion` | int | nên có | người cấp key | `server.js:197` → DataHub |
| `siteCode` | string | ✅ v2 | người cấp key | `server.js:163-173` |
| `siteCodes` | array | — | người cấp key | `server.js:163-173` |
| `middleCode` | string | ✅ | người cấp key | `server.js:547`, fallback site |
| `dataSpreadsheetId` | string | nên có | người cấp key | `server.js:697` → client |
| `updateChannel` | enum | — | người cấp key | `server.js:698` |
| `minAppVersion` | semver | — | người cấp key | **S3 (mới)** |
| `skipHashCheck` | bool | ✅ | người cấp key | `server.js:546,557-582` |
| `modulePolicy` | object | ✅ | người cấp key | `server.js:549` (vắng = **bật hết**, K.10.4) |
| `meta.*` | object | — | người cấp key | không ai |
| `audit.*` | object | — | server | không ai |

### 1.4 Vì sao giữ phẳng thay vì nhóm lồng

Cấu trúc "chuyên nghiệp" theo bản năng sẽ là
`{identity:{...}, lifecycle:{...}, datahub:{...}}`. Đề xuất **không** làm vậy,
vì `server.js` đọc `data.status`, `data.tier`, `data.siteCodes`, `data.seats`…
phẳng ở 8 chỗ khác nhau. Gom lồng nghĩa là:

- phải sửa server **và** migrate 100% key đang chạy trong cùng một lần deploy;
- trong khoảng giữa, key chưa migrate sẽ bị đọc ra `undefined` → `status` khác
  `"active"` → **khách bị chặn**.

Đổi lấy một cái đẹp hơn về hình thức mà mua thêm rủi ro khoá khách là không
đáng. Giải pháp giữ được cả hai: field server đọc thì phẳng, field chỉ người
đọc thì lồng (`meta`, `audit`). Ranh giới đó cũng chính là tài liệu: nhìn vào là
biết field nào có tác dụng thật.

### 1.5 Quy tắc trạng thái hiệu lực

```
effectiveStatus(license, now):
    status != "active"                      -> status              (403, không mở)
    expiresAt vắng                          -> "active"            (key v1, không hết hạn)
    now <= expiresAt                        -> "active"
    now <= expiresAt + graceDays            -> "grace"             (vẫn chạy, client cảnh báo)
    ngược lại                               -> "expired"           (403 LICENSE_EXPIRED)
```

Hai công tắc, hai mục đích, không chồng nhau: `status` là **tay** (ngừng ngay,
ví dụ phát hiện chia sẻ key), `expiresAt` là **tự động** (theo kỳ thanh toán).

`revoked` khác `suspended` ở ý định: `suspended` là mở lại được, `revoked` là
vĩnh viễn — server xử lý y như nhau (đều khác `"active"`), khác nhau ở hồ sơ.
Vì server chỉ nhận `"active"`, thêm hai giá trị này **không cần deploy**.

Về `skipHashCheck`: v1 mặc định `true`, tức bỏ kiểm hash EXE. Đề xuất v2 để
`false`. Lưu ý là đổi mặc định này **hôm nay chưa có tác dụng gì**: khi
`VALID_EXE_HASHES` rỗng thì server bỏ kiểm bất kể `skipHashCheck`
(`server.js:557-582`, chính là mục J-3). Nên trình tự đúng là: điền hash khi
phát hành trước, rồi mới lật mặc định.

---

## PHẦN 2 — `tier-definitions` v2

### 2.1 Quyết định kiến trúc: năng lực tier đặt ở kênh nào

Hiện có **hai** kênh cấu hình tier, và chỉ một trong hai là kênh thật:

| | `configs/runtime-policy.{tier}.json` | `tier-definitions.json` |
|---|---|---|
| Nguồn | DataHub (server) | file trong thư mục cài đặt |
| Ai ghi được | chủ sở hữu | **người dùng** (`AppPaths.InstallDir`) |
| Có kiểm tier không | **Có** — từ chối policy của tier khác | Không |
| Có cache không | Có, cache kèm tier | Không |
| Ai đọc | `VpsRuntimePolicyService` → `TierRuntimePolicy` | `TabManager`, `HasForm` |
| Bản server-side | — | **không ai đọc** (K.8) |

Kết luận: **năng lực tier phải nằm ở `runtime-policy.{tier}.json`.** Đó là kênh
đã tier-aware, đã cache có kiểm tier, đã được giao nhau (intersect) nên chỉ thu
hẹp được. `tier-definitions.json` bị **giáng xuống** làm hai việc, không hơn:

1. **fallback offline** khi chưa lấy được policy từ server;
2. **danh mục hiển thị** (tên tier, mô tả) cho màn hình About/giấy phép.

Nó tuyệt đối không được là nơi *duy nhất* quyết định bật một thứ gì — vì nó ghi
được bởi người dùng, đúng như điểm cắt thứ 5 đã chứng minh.

### 2.2 `configs/runtime-policy.{tier}.json` v2 — bản chuẩn

Một file cho mỗi tier, publish lên DataHub. `VpsRuntimePolicyService` đã thử
đường này **đầu tiên**, nên không cần sửa client để dùng.

```jsonc
{
  "schemaVersion": 2,
  "tier": "ULTRA",                         // BẮT BUỘC — thiếu là mất kiểm tier (xem RuntimePolicyDocument.Tier)
  "revision": 7,                           // MỚI — tăng đơn điệu, để log/nhận biết cache cũ
  "updatedAt": "2026-08-24T00:00:00Z",

  "features": {
    "tabs.home": true,
    "tabs.dkch": true,
    "tabs.tracking": true,
    "tabs.print": true,
    "tabs.about": true,

    "forms.fullStackOperation": true,
    "fullStack.backgroundSync": true,

    "googleSheets.enabled": true,
    "googleSheets.provider": "TokenBroker",

    "print.defaultAutoPrint": true,
    "print.enablePrinterPreflight": true,

    "debugCapture.enabled": false
  },

  "fullStack":    { "enabled": true, "launch": "AFTER_MAINFORM_SHOWN", "backgroundSync": true, "localDbEnabled": true },
  "googleSheets": { "enabled": true, "provider": "TokenBroker", "tokenRefreshSkewMinutes": 5 },
  "print":        { "defaultAutoPrint": true, "enablePrinterPreflight": true, "maxReprintCount": 3 },
  "modulePolicy": { "autoUpdate": false, "silentUpdate": true, "applyOnNextStartup": true },
  "debugCapture": { "enabled": false, "slowApiThresholdMs": 3000 }
}
```

Hai điểm cần nhớ khi soạn file này:

- **`tier` bắt buộc.** `RuntimePolicyDocument.Tier` mặc định là rỗng, và rỗng có
  nghĩa "policy dùng chung mọi tier" — nên nếu quên khai `tier`, file ULTRA sẽ
  được nhận cho cả BASE. Cơ chế đã đúng, chỗ dễ sai là người soạn file.
- **Thiếu một feature key = không thu hẹp**, không phải "cấm". Muốn tắt thì phải
  ghi `false` tường minh.

### 2.3 `tier-definitions.json` v2 — fallback + danh mục

```jsonc
{
  "schemaVersion": 2,
  "updatedAt": "2026-08-24T00:00:00Z",
  "notice": "Chỉ dùng để HIỂN THỊ và làm fallback offline. File này nằm trong thư mục cài đặt nên người dùng ghi được: mọi giá trị ở đây chỉ có thể THU HẸP quyền, không bao giờ mở thêm. Thẩm quyền tier nằm ở license.",

  "tiers": {
    "BASE": {
      "displayName": "AutoJMS BASE",
      "description": "Nhập đơn, tracking và in thủ công.",
      "tabs": ["HOME", "DKCH", "TRACKING", "PRINT", "ABOUT"],
      "forms": []
    },

    "ULTRA": {
      "inherits": "BASE",
      "displayName": "AutoJMS ULTRA",
      "description": "Toàn bộ BASE, cộng FullStack Operation và đồng bộ nền.",
      "tabs": ["HOME", "DKCH", "TRACKING", "PRINT", "ABOUT"],
      "forms": [
        {
          "name": "FULLSTACK_OPERATION",
          "type": "VISIBLE_FORM",
          "launch": "AFTER_MAINFORM_SHOWN",
          "fetchApiAfterAuthToken": true
        }
      ]
    }
  }
}
```

Bỏ khỏi file so với v1:

| Bỏ | Vì sao |
|---|---|
| `backgroundJobs` | Không có property tương ứng trên `TierConfig`, bị `MergeWithParent` bỏ im lặng (I.2) |
| `modules` | `TierConfig.Modules` parse ra nhưng không ai đọc (J.4) |
| `backgroundForms` | Là property tính toán `[JsonIgnore]`, không phải field của file |

**Cảnh báo code kèm theo:** thêm `displayName`/`description` đòi sửa
`TierDefinitions.MergeWithParent` (`TierDefinitions.cs:51-60`) — nó dựng
`TierConfig` mới chỉ với `Inherits/Tabs/Forms/Modules`, nên ULTRA (có
`inherits: "BASE"`) sẽ **mất** `displayName` sau khi merge. Không sửa thì hai
field mới im lặng biến mất đúng ở tier cần chúng nhất.

### 2.4 Ma trận BASE vs ULTRA theo từ vựng chuẩn

| Feature key | BASE | ULTRA | Ghi chú |
|---|:---:|:---:|---|
| `tabs.home` | ✅ | ✅ | |
| `tabs.dkch` | ✅ | ✅ | |
| `tabs.tracking` | ✅ | ✅ | Thi hành thật từ commit `e33a029` |
| `tabs.print` | ✅ | ✅ | như trên |
| `tabs.about` | ✅ | ✅ | Luôn là tab cuối |
| `forms.fullStackOperation` | ❌ | ✅ | Ranh giới tier chính |
| `fullStack.backgroundSync` | ❌ | ✅ | Bất biến: BASE **không bao giờ** sync nền |
| `fullStack.localDbEnabled` | ❌ | ✅ | |
| `googleSheets.enabled` | ✅ | ✅ | Chủ sở hữu đã chốt ở vòng 2: BASE cũng được dùng |
| `print.defaultAutoPrint` | ✅ | ✅ | |
| `debugCapture.enabled` | ❌ | ❌ | Chỉ bật khi cần chẩn đoán |

Hôm nay BASE và ULTRA có cùng 5 tab, nên `tier-definitions.json` v2 giữ đúng như
vậy. Chỗ khác nhau duy nhất là `forms` và nhóm `fullStack.*` — và đó là chỗ nên
để nó khác nhau: tab là bề mặt, năng lực là thứ bán.

---

## PHẦN 3 — Thay đổi cần ở backend

| # | Nơi | Thay đổi | Có breaking không |
|---|---|---|---|
| S1 | `server.js:538` | Sau kiểm `status`, tính `effectiveStatus` (1.5). `expired` → 403 `LICENSE_EXPIRED` kèm `expiresAt`. `grace` → cho qua, gắn cờ vào response | Không — key vắng `expiresAt` chạy như cũ |
| S2 | `server.js:294` | `normalizeTier` allowlist `{BASE, ULTRA}`; giá trị lạ → log cảnh báo + lỗi rõ ràng, **không** âm thầm hạ về BASE | Không, nếu Firebase chỉ có hai tier. Vá K.7 |
| S3 | `server.js:680-711` | Thêm `license.expiresAt`, `license.effectiveStatus`, `license.graceUntil`, `license.seats`, `cfg.minAppVersion` | Không — thêm field |
| S4 | `server.js:825` | Heartbeat **đọc lại `Licenses/{key}`**, so `tier`/`status`/`expiresAt` với token; đổi tier → outcome mới, hết hạn hoặc `status` khác `active` → `ServerKill`. Mẫu đã có ở `server.js:931-1030` | Thêm 1 lần đọc RTDB / 2 phút / máy. Vá K.2 |
| S5 | mới | Cấp "offline grant" RS256 trong response verify: claims `{key, hwid, tier, exp}` với `exp = min(expiresAt, now + offlineGraceHours)`. Client đã có public key (`LicenseApiService.cs:80`) | Không — thêm field. Vá K.3 |
| S6 | `server.js:598` | `activatedAt` ghi ISO thay vì epoch ms; đọc chấp nhận cả number cũ | Không |
| S7 | `server.js:1034` | `/api/logout`: thêm `limiter` + xác thực token qua helper có sẵn `server.js:411` | Client phải gửi `Authorization` khi logout. Vá K.4 |
| S8 | `server.js:732` | Không trả token SA nữa; proxy theo `dataSpreadsheetId` của chính license | **Có** — đổi cách client đọc Sheets. Vá K.6, nên làm riêng |

S2 và S7 là hai mục **rẻ, độc lập, không breaking** — làm được ngay nếu chủ sở
hữu cho deploy Render.

## PHẦN 4 — Thay đổi cần ở client

| # | Nơi | Thay đổi | File bảo vệ? |
|---|---|---|---|
| C1 | `LicenseApiService.cs:22-60` | `VerifyResult` += `ExpiresAt`, `GraceUntil`, `EffectiveStatus`, `OfflineGrant`, `MinAppVersion` | ✅ cần chủ sở hữu cho phép |
| C2 | `LicenseApiService.cs:19,62-74` | `HeartbeatOutcome` += `TierChanged`; `HeartbeatResult` += `Tier`, `ExpiresAt`; `HeartbeatSupervisor` xử lý ca mới | ✅ |
| C3 | `TierRuntimePolicy` | Đường re-resolve khi heartbeat báo tier đổi, **chỉ chấp nhận thu hẹp**: tier mới thấp hơn thì áp ngay; cao hơn thì đòi khởi động lại (nâng quyền lúc đang chạy là điểm cắt mới) | ✅ |
| C4 | `Program.cs:139,152,195-205` | Nhánh offline: xác thực `OfflineGrant` (chữ ký + `exp` + `hwid`) → cấp đúng tier khi offline; hết `exp` → không authorize | ✅ |
| C5 | `TierDefinitions.cs:51-60` | `MergeWithParent` merge thêm `DisplayName`/`Description`; bỏ `Modules`/`BackgroundForms` | Không |
| C6 | `LicenseApiService.cs` | Gửi `appVersion` trong body verify (K.10.1) | ✅ |
| C7 | `VpsManifestService.cs:70` | Hoặc xoá `FetchTierDefinitionsAsync`, hoặc đấu dây nó **kèm kiểm tier** như `VpsRuntimePolicyService` | Không |

C1–C4 và C6 nằm trong Protected Files, nên cần chủ sở hữu cho phép theo từng
task, đúng như đã làm ở vòng 2.

## PHẦN 5 — Lộ trình migrate

| Pha | Việc | Rủi ro | Cần deploy |
|---|---|---|---|
| **0** | Chốt schema này. Viết lại `backend/firebase/config-key.json` thành template v2 + cập nhật `license-key-schema.txt` (đang mô tả sai: nói `configs/tier-definitions.json` trong khi server publish `manifest/tier-definitions.json`, và mô tả `backgroundForms` không còn khớp `TierConfig`) | Không | Không |
| **1** | Backfill `siteCode` cho **mọi key đang chạy** để dứt điểm K.5. Bỏ `middleCode: "0000"` | Sai site = re-tenant dữ liệu khách. Phải có danh sách site DataHub thật trong tay | Không |
| **2** | S2 + S7 (allowlist tier, khoá `/api/logout`) | Thấp | Render |
| **3** | S1 + S3 ở **chế độ chỉ log**: tính hết hạn, ghi log, **chưa chặn**. Chạy 1-2 tuần để thấy có key nào bị tính sai không | Rất thấp | Render |
| **4** | Bật chặn hết hạn. Thêm `expiresAt` vào key mới; key cũ để vắng = vĩnh viễn cho tới khi chủ sở hữu điền | Sai múi giờ là khoá khách thật — nên pha 3 phải chạy trước | Render |
| **5** | S4 + S5 + C1..C4: thu hồi gần-thực-thời qua heartbeat và tier offline có chữ ký | Cao nhất — chạm 4 file bảo vệ. Làm sau cùng, một task riêng | Render + client |
| **6** | S8 (proxy Google Sheets) và C7 (dọn kênh tier-definitions) | Độc lập với các pha trên | Render |

Thứ tự này chọn theo nguyên tắc: **vá lỗ hổng dữ liệu trước (pha 1), rồi vá lỗ
hổng rẻ (pha 2), rồi mới đến tính năng có thể khoá oan khách (pha 3-4).**

## PHẦN 6 — Hai bản ghi mẫu

BASE, một site, hết hạn theo năm:

```json
{
  "schemaVersion": 2,
  "status": "active",
  "createdAt": "2026-08-24T09:00:00Z",
  "activatedAt": null,
  "expiresAt": "2027-08-24T00:00:00Z",
  "graceDays": 7,
  "offlineGraceHours": 72,
  "tier": "BASE",
  "hwid": "",
  "seats": 1,
  "tokenVersion": 1,
  "siteCode": "HN01",
  "siteCodes": ["HN01"],
  "middleCode": "HN01",
  "dataSpreadsheetId": "",
  "updateChannel": "stable",
  "skipHashCheck": false,
  "modulePolicy": { "autoUpdate": false, "silentUpdate": true, "applyOnNextStartup": true },
  "meta": { "customerName": "", "contact": "", "orderId": "", "issuedBy": "owner", "notes": "" }
}
```

ULTRA, hai chi nhánh, ba máy:

```json
{
  "schemaVersion": 2,
  "status": "active",
  "createdAt": "2026-08-24T09:00:00Z",
  "activatedAt": null,
  "expiresAt": "2027-08-24T00:00:00Z",
  "graceDays": 14,
  "offlineGraceHours": 168,
  "tier": "ULTRA",
  "hwid": "",
  "seats": 3,
  "tokenVersion": 1,
  "siteCode": "HCM01",
  "siteCodes": ["HCM01", "HCM02"],
  "middleCode": "HCM01",
  "dataSpreadsheetId": "1AbCdEf_thay_bang_id_that",
  "updateChannel": "stable",
  "minAppVersion": "1.0.0",
  "skipHashCheck": false,
  "modulePolicy": { "autoUpdate": false, "silentUpdate": true, "applyOnNextStartup": true },
  "meta": { "customerName": "", "contact": "", "orderId": "", "issuedBy": "owner", "notes": "" }
}
```

## PHẦN 7 — Những gì đề xuất cố ý KHÔNG làm

| Không làm | Vì sao |
|---|---|
| Nhóm field vào object lồng | Xem 1.4 — mua rủi ro khoá khách để đổi lấy hình thức |
| Thêm tier thứ ba (PRO/TRIAL) | Chưa có nhu cầu. Bản dùng thử làm bằng `expiresAt` ngắn trên tier BASE, không cần tier mới |
| Đưa danh sách feature vào bản ghi license | Sẽ thành điểm cắt mới: license là nơi khai *tier*, năng lực của tier nằm ở `runtime-policy.{tier}.json` |
| Cho `tier-definitions.json` bật thêm bất cứ thứ gì | File người dùng ghi được. Chỉ thu hẹp |
| Nhét `tier` vào cache offline hiện tại | `BuildCacheSecret` suy ra được từ máy (K.3). Phải là token có chữ ký server |
| Rewrite `license-key-schema.txt` sang JSON Schema chuẩn | Có thể làm sau; giá trị thấp hơn việc sửa chỗ nó đang mô tả sai |

Rủi ro của chính đề xuất này:

1. **Hết hạn là con dao hai lưỡi.** Điền sai một `expiresAt` là khoá một khách
   đang trả tiền. Pha 3 (chỉ log) là bắt buộc, không phải tuỳ chọn.
2. **Heartbeat đọc lại license** tốn thêm ~30 lượt đọc RTDB mỗi máy mỗi giờ.
   Rẻ, nhưng phải nhìn quota Firebase trước khi bật cho toàn bộ khách.
3. **Offline grant kéo dài** thời gian một cache bị lấy đi còn dùng được. Phải
   buộc vào `hwid` và giữ `exp` ngắn.
4. **Backfill `siteCode`** là thao tác dữ liệu, không phải code. Sai là trộn dữ
   liệu hai khách — nguy hiểm hơn mọi mục còn lại trong tài liệu này.

## PHẦN 8 — Chủ sở hữu cần chốt

| # | Câu hỏi | Ảnh hưởng |
|---|---|---|
| 1 | License bán theo kỳ (có `expiresAt`) hay vĩnh viễn? | Quyết định `expiresAt` có bắt buộc trong v2 hay chỉ là tuỳ chọn |
| 2 | `graceDays` và `offlineGraceHours` để bao nhiêu? Đề xuất 7 ngày / 72 giờ | S1, S5 |
| 3 | Có bật hạ tier gần-thực-thời qua heartbeat không? Khách đang chạy sẽ mất tab/form ngay khi chủ sở hữu sửa Firebase | S4, C2, C3 |
| 4 | Cho deploy Render để làm pha 2 (S2 + S7) chưa? Hai mục này rẻ và không breaking | Vá K.4, K.7 |
| 5 | Ai có danh sách site DataHub thật để backfill `siteCode`? | Pha 1, vá K.5 |
| 6 | Google Sheets: đổi sang proxy phía server (S8) hay giữ nguyên và chấp nhận K.6? | Pha 6 |
