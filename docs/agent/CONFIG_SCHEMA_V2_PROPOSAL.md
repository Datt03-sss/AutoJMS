# Schema v2 cho `config-key` và `tier-definitions` — bản đã chốt

**Trạng thái: ĐÃ CHỐT theo quyết định của chủ sở hữu ngày 2026-08-24.**
Vòng trước là đề xuất; vòng này đã có sáu quyết định (PHẦN 0) và một phần đã
được thi hành trong repo — xem cột "Đã làm" ở PHẦN 3, PHẦN 4.

- Ngày: 2026-08-24
- Rủi ro nền: [FULLSTACK_BACKEND_RISK_REVIEW.md](./FULLSTACK_BACKEND_RISK_REVIEW.md) mục **K** và **L**
- Ràng buộc: **phải khớp backend hiện tại** (`backend/render-license-server/server.js`,
  `src/AutoJMS.DataHub.Api`, `VpsRuntimePolicyService`, `TierConfig`)

> ⚠ **Chặn toàn bộ phần server.** Render production đang chạy repo
> [`Datt03-sss/AutoJMS-API`](https://github.com/Datt03-sss/AutoJMS-API), **không**
> phải `backend/render-license-server/` trong repo này (bằng chứng: mục **L**).
> Mọi thay đổi S1–S7 dưới đây đã viết vào bản trong repo này và **chưa có tác
> dụng ở production** cho tới khi hai bản được hợp nhất. Đó là pha 0 mới của lộ
> trình.

---

## PHẦN 0 — Sáu quyết định đã chốt

| # | Câu hỏi vòng trước | Quyết định | Hệ quả trong tài liệu này |
|---|---|---|---|
| 1 | License bán theo kỳ hay vĩnh viễn? | **Theo kỳ 1 tháng, nhưng hết hạn thật vào 00:00 ngày 16 hàng tháng** = 30 ngày cơ bản + số ngày dư tới ngày 16 | Thuật toán ở **§1.6**, code ở `license-expiry.js` |
| 2 | `graceDays` / `offlineGraceHours`? | **7 ngày / 72 giờ** | Mặc định trong `license-expiry.js`, ghi vào template |
| 3 | Hạ/nâng tier gần-thực-thời? | **Không.** Khởi động lại app mới cập nhật tier | **S4, C2, C3 bị huỷ.** Xem §3.1 |
| 4 | Cho deploy Render (allowlist tier + khoá API)? | **Cho** | S2 + S7 đã viết xong, chờ giải quyết pha 0 |
| 5 | Site-code lấy từ đâu? | **`site-code = middleCode` tại license key** | §1.5, `middleCode` phải duy nhất, `"0000"` bị loại |
| 6 | Google Sheets: proxy phía server hay giữ nguyên? | **Giữ nguyên, chưa sửa lúc này** | **S8 bỏ.** K.6 ghi nhận là rủi ro đã biết và chấp nhận |

---

## 1. Năm nguyên tắc thiết kế

1. **Cộng thêm, không đổi tên.** Mọi field v1 giữ nguyên tên và ý nghĩa, nên
   server hiện tại chạy được với bản ghi v2 mà không cần deploy trước. Deploy
   server chỉ cần cho các tính năng *mới* (hết hạn, allowlist tier).
2. **Field nào server đọc thì để phẳng.** `server.js` đọc `data.<field>` trực
   tiếp; gom vào object lồng là buộc phải sửa server *và* migrate mọi key đang
   chạy cùng lúc. Chỉ những field **server không đọc** (metadata khách hàng) mới
   được để lồng — và ranh giới đó chính là tài liệu: nhìn vào là biết field nào
   có tác dụng thật.
3. **Thời gian là ISO-8601 kèm offset `+07:00`.** Vòng trước đề xuất UTC; quyết
   định 1 đã thay đổi lựa chọn đó. Mốc hết hạn là *một thời điểm theo giờ treo
   tường Việt Nam* ("00:00 ngày 16"), nên ghi `"2026-10-16T00:00:00+07:00"` cho
   người đọc thấy đúng cái mốc đã bán. Ghi `"2026-10-15T17:00:00Z"` là cùng một
   thời điểm nhưng không ai soi ra được ngày 16. Cả hai dạng vẫn so sánh và sắp
   xếp được; dạng `"26-05-2026 01:22"` cũ thì không.
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
`backend/firebase/config-key.json` v1 không có:

| Field server đã đọc | Ở đâu | Hệ quả của việc template thiếu nó |
|---|---|---|
| `siteCodes` (array) | `server.js:163-173` | Không có → fallback về `middleCode` → **K.5**, nhiều license dùng chung tenant `"0000"` |
| `siteCode` (string) | `server.js:163-173` | như trên |
| `siteId` | `server.js:703` | Client cũ đọc field này |
| `seats` | `server.js:196` (chặn 1..500) | Không có → mặc định `DATAHUB_ASSERTION.DEFAULT_SEATS`, không ai biết key được mấy máy |
| `tokenVersion` | `server.js:197` (chặn 1..1e6) | Không có → luôn là 1, mất đường vô hiệu hoá device token hàng loạt |
| `updateChannel` | `server.js:698` | Không có → luôn là `CONFIG.DEFAULT_CHANNEL`, không đặt được kênh beta cho từng khách |

Và một field **server tự ghi vào** mà template không khai: `activatedAt`.

Nên phần lớn công việc của v2 không phải là "thêm tính năng", mà là **viết ra
đúng những gì backend đã làm được**, cộng thêm `expiresAt` là thứ thật sự còn
thiếu.

### 1.2 Schema v2 — đã thi hành ở `backend/firebase/config-key.json`

```jsonc
{
  "schemaVersion": 2,

  // ---------- vòng đời ----------
  "status": "active",                          // active | suspended | revoked (server: chỉ "active" đi qua)
  "tier": "ULTRA",                             // BASE | ULTRA — allowlist, giá trị lạ => 403
  "createdAt": "2026-08-24T01:22:00+07:00",    // người cấp key ghi
  "expiresAt": "2026-10-16T00:00:00+07:00",    // §1.6 — vắng = vĩnh viễn (key v1)
  "activatedAt": null,                         // SERVER ghi khi bind HWID lần đầu
  "graceDays": 7,                              // sau expiresAt vẫn chạy + cảnh báo
  "offlineGraceHours": 72,                     // trần thời gian chạy khi mất mạng

  // ---------- tenant DataHub ----------
  "middleCode": "HCM001",                      // CHÍNH LÀ site-code (quyết định 5). Phải duy nhất.
  "siteCodes": ["HCM001"],                     // tuỳ chọn — khách nhiều chi nhánh
  "siteId": "",                                // GUID DataHub trả về sau enroll
  "seats": 3,                                  // 1..500

  // ---------- ràng buộc máy & build ----------
  "hwid": "",                                  // "" = chưa bind; server tự bind lần đầu
  "skipHashCheck": true,                       // xem ghi chú §1.7
  "tokenVersion": 1,                           // tăng để vô hiệu hoá device token cũ

  // ---------- tích hợp ----------
  "dataSpreadsheetId": "",
  "updateChannel": "stable",                   // stable | beta

  "modulePolicy": {
    "autoUpdate": false,
    "silentUpdate": true,
    "applyOnNextStartup": true
  },

  // ---------- server KHÔNG đọc ----------
  "meta": {
    "customerName": "",
    "contact": "",
    "orderId": "",
    "issuedBy": "owner",
    "notes": ""
  }
}
```

Hai field từng có trong bản đề xuất mà **cố ý bỏ khỏi template**:
`minAppVersion` (cần S3 + C1 mà C1 là file bảo vệ, chưa làm — để trong template
sẽ thành field trang trí không có tác dụng) và nhóm `audit.*` (server chưa ghi
gì vào đó).

### 1.3 Từ điển field

| Field | Loại | Bắt buộc | Ai ghi | Ai đọc |
|---|---|---|---|---|
| `schemaVersion` | int | v2 | người cấp key | chưa ai — dùng để migrate về sau |
| `status` | enum | ✅ | người cấp key | `server.js:538`, `:758`, `:982` |
| `tier` | enum | ✅ | người cấp key | allowlist `isKnownTier` → toàn bộ phân quyền client |
| `createdAt` | ISO+07 | ✅ | người cấp key | đầu vào của `computeExpiry` |
| `expiresAt` | ISO+07 | ✅ với key mới | người cấp key | `evaluateLicense` (S1) |
| `activatedAt` | ISO+07 | — | **server** | chưa ai — hồ sơ |
| `graceDays` | int | — | người cấp key | `evaluateLicense` (S1) |
| `offlineGraceHours` | int | — | người cấp key | forward xuống client; **S5 chưa làm** |
| `middleCode` | string | ✅ | người cấp key | `server.js:547` + site-code DataHub |
| `siteCodes` | array | — | người cấp key | `server.js:163-173` |
| `siteId` | string | — | DataHub / người cấp key | `server.js:703` |
| `seats` | int | nên có | người cấp key | `server.js:196` + response |
| `hwid` | string | ✅ (rỗng được) | server (bind) | `server.js:584-603` |
| `skipHashCheck` | bool | ✅ | người cấp key | `server.js:546,557-582` |
| `tokenVersion` | int | nên có | người cấp key | `server.js:197` → DataHub |
| `dataSpreadsheetId` | string | nên có | người cấp key | `server.js:697` → client |
| `updateChannel` | enum | — | người cấp key | `server.js:698` |
| `modulePolicy` | object | ✅ | người cấp key | `server.js:549` (vắng = **bật hết**, K.10.4) |
| `meta.*` | object | — | người cấp key | không ai |

### 1.4 Vì sao giữ phẳng thay vì nhóm lồng

Cấu trúc "chuyên nghiệp" theo bản năng sẽ là
`{identity:{...}, lifecycle:{...}, datahub:{...}}`. Bản chốt **không** làm vậy,
vì `server.js` đọc `data.status`, `data.tier`, `data.siteCodes`, `data.seats`…
phẳng ở 8 chỗ khác nhau. Gom lồng nghĩa là:

- phải sửa server **và** migrate 100% key đang chạy trong cùng một lần deploy;
- trong khoảng giữa, key chưa migrate sẽ bị đọc ra `undefined` → `status` khác
  `"active"` → **khách bị chặn**.

Đổi lấy một cái đẹp hơn về hình thức mà mua thêm rủi ro khoá khách là không
đáng. Chỉ `meta` được lồng, vì server không đọc nó.

### 1.5 Site-code = `middleCode` (quyết định 5)

`middleCode` từ nay **là** site-code của DataHub, không còn là "mã giữa" dùng
tạm. Ba việc kèm theo:

1. **Phải duy nhất theo khách.** `server.js` có `PLACEHOLDER_SITE_CODES` =
   `{"", "0000", "00000", "0", "DEFAULT", "NONE", "TBD"}`. Key trúng danh sách
   này sẽ bị ghi log `LICENSE_SITE_CODE_PLACEHOLDER`.
2. **Thi hành là opt-in.** Bật `REQUIRE_UNIQUE_SITE_CODE=1` thì placeholder →
   403 `LICENSE_SITE_CODE_INVALID`. Mặc định **tắt**, vì hôm nay *mọi* key trong
   fleet đều đang là `"0000"` — bật ngay là khoá sạch khách.
3. **Response trả `license.siteCode`** = `middleCode` đã upper-case, đúng dạng
   `/api/v1/devices/enroll` cần.

Thứ tự đúng: backfill `middleCode` cho mọi key → kiểm log không còn
`LICENSE_SITE_CODE_PLACEHOLDER` → mới bật `REQUIRE_UNIQUE_SITE_CODE=1`.

### 1.6 `expiresAt` — mốc ngày 16 (quyết định 1)

Quy tắc nghiệp vụ: bán theo kỳ 1 tháng, nhưng cả fleet hết hạn cùng một ngày
lịch để đối soát doanh thu được.

```
expiresAt = mốc "ngày 16, 00:00 +07:00" SỚM NHẤT mà >= (nửa đêm ngày tạo + 30 ngày)
```

**Tính theo ngày tròn, không theo giờ tạo key.** Đây là chỗ duy nhất phải chọn
thêm, và nó đáng giá đúng một tháng doanh thu mỗi key: nếu tính theo giờ, key
tạo 2026-08-17 **10:00** thì mốc 30 ngày rơi vào 2026-09-16 **10:00** — đã quá
mốc 00:00 của tháng đó — nên phải nhảy sang **2026-10-16**, tức khách trả một
tháng mà nhận gần hai. Làm tròn về nửa đêm ngày tạo xoá hẳn cái vực đó. Giá phải
trả: key tạo lúc 23:59 mất tối đa một ngày so với "30 × 24 giờ" nguyên nghĩa.

| `createdAt` | `expiresAt` | Số ngày |
|---|---|---:|
| 2026-08-01 | 2026-09-16 | 46 |
| 2026-08-15 | 2026-09-16 | 32 |
| 2026-08-16 00:00 | 2026-09-16 | 31 |
| 2026-08-17 10:00 | 2026-09-16 | **30** ← nhờ làm tròn ngày |
| 2026-08-18 | 2026-10-16 | 59 |
| 2026-08-24 01:22 | 2026-10-16 | 53 |
| 2026-12-20 | 2027-02-16 | 58 |
| 2026-01-31 | 2026-03-16 | 44 |

Kỳ hạn **không bao giờ ngắn hơn 30 ngày** và dài nhất là 59–60 ngày (khi ngày
tạo là 17 hoặc 18). Bán nhiều kỳ thì dùng `computeExpiry(start, { terms: 3 })` —
cộng nguyên tháng theo mốc, vẫn ra một mốc ngày 16 duy nhất.

Cách sinh giá trị khi cấp key:

```bash
cd backend/render-license-server && node -e "console.log(require('./license-expiry').computeExpiry('2026-08-24').expiresAt)"
```

Cài đặt: [`backend/render-license-server/license-expiry.js`](../../backend/render-license-server/license-expiry.js),
17 test ở `test/license-expiry.test.js` (`npm test`). Múi giờ cố định `+07:00` —
Asia/Ho_Chi_Minh không có DST nên không cần thư viện tz.

### 1.7 Quy tắc trạng thái hiệu lực

```
effectiveStatus(license, now):
    status != "active"                      -> status              (401, không mở)
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

Một cái bẫy đã gặp khi cài đặt: `graceDays: null` mà đọc bằng `Number(null)` thì
ra `0`, tức là **xoá sạch cửa sổ gia hạn** thay vì dùng mặc định 7 ngày. Test
`evaluateLicense falls back to the default grace window on a bad graceDays` giữ
chỗ đó.

Về `skipHashCheck`: v1 mặc định `true`, tức bỏ kiểm hash EXE. Template v2 **giữ
`true`**, vì đổi sang `false` hôm nay chưa có tác dụng gì: khi `VALID_EXE_HASHES`
rỗng thì server bỏ kiểm bất kể `skipHashCheck` (`server.js:557-582`, chính là
mục J-3). Trình tự đúng là điền hash khi phát hành trước, rồi mới lật mặc định.

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
  "revision": 7,                           // tăng đơn điệu, để log/nhận biết cache cũ
  "updatedAt": "2026-08-24T00:00:00+07:00",

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

### 2.3 `tier-definitions.json` v2 — đã thi hành

File thật: [`src/AutoJMS/tier-definitions.json`](../../src/AutoJMS/tier-definitions.json).
Có thêm `notice` ngay trong file, để ai mở nó trong thư mục cài đặt cũng đọc
được rằng sửa file này không mở thêm được quyền nào.

```jsonc
{
  "schemaVersion": 2,
  "updatedAt": "2026-08-24T00:00:00+07:00",
  "notice": "Chỉ dùng để HIỂN THỊ và làm fallback offline. …",

  "tiers": {
    "BASE": {
      "displayName": "AutoJMS Base",
      "description": "Vận hành thủ công: đăng ký chuyến hàng, tra cứu vận đơn, in tem. Không có tiến trình nền, không đồng bộ kho.",
      "tabs": ["HOME", "DKCH", "TRACKING", "PRINT", "ABOUT"],
      "forms": []
    },

    "ULTRA": {
      "inherits": "BASE",
      "displayName": "AutoJMS Ultra",
      "description": "Toàn bộ Base, cộng FullStack Operation: đồng bộ kho và theo dõi đơn theo thời gian thực qua DataHub.",
      "tabs": ["HOME", "DKCH", "TRACKING", "PRINT", "ABOUT"],
      "forms": [
        { "name": "FULLSTACK_OPERATION", "type": "VISIBLE_FORM", "launch": "AFTER_MAINFORM_SHOWN", "fetchApiAfterAuthToken": true }
      ]
    }
  }
}
```

Bỏ khỏi file so với v1:

| Bỏ | Vì sao |
|---|---|
| `backgroundJobs` | Không có property tương ứng trên `TierConfig` — `BASE.backgroundJobs.fullStackRealtime = false` **không thi hành gì cả**, chỉ tạo cảm giác an toàn giả |
| `modules` | `TierConfig.Modules` parse ra nhưng không ai đọc (J.4) — giữ property để không phá file cũ, nhưng không viết vào file mới |
| `backgroundForms` | Là property tính toán `[JsonIgnore]`, không phải field của file |

**Đã sửa kèm:** `TierDefinitions.MergeWithParent` dựng `TierConfig` mới chỉ với
`Inherits/Tabs/Forms/Modules`, nên ULTRA (có `inherits: "BASE"`) sẽ **mất**
`displayName`/`description` sau khi merge. Đã thêm hai field vào merge; test
`Tier_ke_thua_giu_duoc_displayName_va_description_cua_chinh_no` giữ chỗ đó.

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

## PHẦN 3 — Thay đổi ở backend

Cột "Đã làm" là trạng thái trong `backend/render-license-server/` của repo này.
**Không** phải trạng thái production — xem cảnh báo đầu tài liệu.

| # | Nơi | Thay đổi | Đã làm | Breaking |
|---|---|---|---|---|
| S1 | `server.js` sau kiểm `status` | `evaluateLicense` (§1.7). `expired` → 403 `LICENSE_EXPIRED` kèm `expiresAt`/`graceUntil`; `grace` → cho qua + log `LICENSE_GRACE` | ✅ | Không — key vắng `expiresAt` chạy như cũ |
| S2 | `normalizeTier` | Allowlist `{BASE, ULTRA}`; giá trị lạ → `console.error` + 403 `LICENSE_TIER_INVALID`, **không** âm thầm hạ về BASE | ✅ | Không, nếu Firebase chỉ có hai tier. Vá K.7 |
| S3 | response verify | `license.effectiveStatus/expiresAt/graceUntil/daysRemaining/graceDays/offlineGraceHours/billingAnchorDay/seats/siteCode` | ✅ (trừ `minAppVersion`) | Không — thêm field |
| S4 | heartbeat | ~~Đọc lại `Licenses/{key}`, đổi tier → outcome mới~~ | ❌ **HUỶ** — quyết định 3 | — |
| S5 | mới | Offline grant RS256 `{key, hwid, tier, exp}`, `exp = min(expiresAt, now + offlineGraceHours)` | ❌ hoãn (cần C4, file bảo vệ) | Không — thêm field. Vá K.3 |
| S6 | bind HWID | `activatedAt` ghi ISO `+07:00` thay vì epoch ms | ✅ | Không — không ai đọc field này (đã grep) |
| S7 | `/api/logout` | Thêm `limiter` + đòi Bearer token và `decoded.sid === sid` | ✅ | Không — **client chưa từng gọi route này** (đã grep toàn bộ `src/`). Vá K.4 |
| S8 | `/api/google-sheets/grant` | ~~Proxy theo `dataSpreadsheetId`~~ | ❌ **BỎ** — quyết định 6 | — |

Kèm theo, bốn biến môi trường mới (đều có mặc định, không bắt buộc khai):
`LICENSE_GRACE_DAYS=7`, `LICENSE_OFFLINE_GRACE_HOURS=72`,
`LICENSE_BILLING_ANCHOR_DAY=16`, `REQUIRE_UNIQUE_SITE_CODE=0`.

### 3.1 Vì sao huỷ S4 quan trọng hơn nó trông

Quyết định 3 ("khởi động lại app mới cập nhật tier") xoá luôn một họ rủi ro,
không chỉ tiết kiệm công:

- **Không có nâng quyền lúc đang chạy.** S4 + C3 phải mở một đường đổi
  `TierRuntimePolicy.Current` khi app đang chạy. Đường đó chính là điểm cắt thứ
  sáu tiềm năng — mọi nơi đã đọc `_tierPolicy` trước đó sẽ không được đánh giá
  lại, mà `Main.cs` thì đọc nó ở rất nhiều chỗ.
- **Đổi lại:** thu hồi chậm hơn. Sửa Firebase xong, máy đang chạy **vẫn giữ tier
  cũ cho tới lần khởi động lại**. Muốn cắt ngay thì đổi `status` khác `"active"`
  — heartbeat đã kiểm session và sẽ `kill` — hoặc tăng `tokenVersion` để chặn
  device token DataHub.
- **K.2 chuyển từ "lỗi" thành "giới hạn đã biết".** Vẫn ghi trong sổ rủi ro, kèm
  đúng câu này.

### 3.2 Phía DataHub (VPS) — trạng thái sau vòng 7

Bảng S1–S8 chỉ nói về license server. Nửa còn lại của backend là
`src/AutoJMS.DataHub.Api/`, và vòng 7 (`FULLSTACK_BACKEND_RISK_REVIEW.md` mục M)
đóng nốt phần cấu hình của nó:

| Hạng mục | Trạng thái | Ghi chú |
|---|---|---|
| Xác minh assertion bất đối xứng | ✅ **code đã đủ** | `RsaLicenseAssertionValidator`: `v1rs256`, RSASSA-PKCS1-v1_5/SHA-256, sàn 2048 bit, **từ chối khoá private**. Tài liệu kiến trúc §13 trước đây nói ngược — đã sửa |
| `DATAHUB_LICENSE_ASSERTION_VALIDATION_KEY` | ❌ **đã xoá** | Biến chết: `ValidateAsync` trả `LICENSE_ASSERTION_UNAVAILABLE` trừ khi staging opt-in **và** `channel == staging`, mà nhánh chọn khoá cũng rẽ dưới đúng điều kiện đó. Tác dụng duy nhất là làm người vận hành tin đã cấu hình xong |
| `DATAHUB_DEVICE_TOKEN_LIFETIME_SECONDS` | ✅ nối vào compose + hai template | Bị kẹp `300..2592000` giây, nên gõ sai không tắt được expiry |
| Publish seed control-plane | ✅ script + tài liệu | `scripts/publish-manifests.{sh,ps1}`, có `--dry-run` |

**Điều quan trọng nhất cho phần chốt này:** rào cuối của enrollment production
**không phải code**, mà là hai biến `DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY` (nửa
public, **không bao giờ** nửa private) và `_ISSUER`/`_AUDIENCE` phải **khớp từng
ký tự** với license server. Lệch một ký tự thì assertion ký đúng vẫn bị từ chối
là `LICENSE_ASSERTION_INVALID`, và triệu chứng ở máy trạm chỉ là "enroll thất
bại".

**Và một cái bẫy đụng trực tiếp vào quyết định về tier:** VPS chưa publish seed
trả 404 cho cả sáu đường policy, mà 404 đó dẫn tới
`RuntimePolicyDocument.SafeDefault("BASE", ...)`. Nên **một VPS mới chạy mọi máy
trạm ULTRA với quyền BASE, im lặng hoàn toàn** — không log lỗi, không có gì fail.
Publish seed là bước bắt buộc của mọi lần triển khai mới, không phải bước tuỳ
chọn. Chi tiết ở `backend/datahub/README.md`.

Khi tự viết `configs/runtime-policy.{tier}.json`, hai điều dễ mất thời gian:

1. **Google Sheets phải dùng khối typed.** `RuntimeGoogleSheetsPolicy.Provider`
   mặc định `"TokenBroker"` (không rỗng) và `RuntimePolicyApplier` chỉ đọc
   `features["googleSheets.provider"]` khi giá trị typed **rỗng**.
2. **Đừng publish `print.*` / `debugCapture.*`** nếu không thật sự muốn ép: khi
   thiếu, chúng theo `AppSettings` của máy trạm; khi có, chúng ghi đè lựa chọn của
   kỹ thuật viên **ở mỗi lần khởi động**.

Ngoài ra `tabs.home`, `tabs.dkch`, `tabs.about` **không có ai đọc** — đặt trong
seed không có tác dụng. Từ vựng thật là 11 khoá, xem mục M.4.1 của sổ rủi ro.

## PHẦN 4 — Thay đổi ở client

| # | Nơi | Thay đổi | Trạng thái | File bảo vệ? |
|---|---|---|---|---|
| C1 | `LicenseApiService.cs:22-60` | `VerifyResult` += `ExpiresAt`, `GraceUntil`, `EffectiveStatus`, `DaysRemaining` + hiển thị cảnh báo khi `grace` | ⏳ chờ chủ sở hữu | ✅ |
| C2 | `LicenseApiService.cs:19,62-74` | ~~`HeartbeatOutcome` += `TierChanged`~~ | ❌ **HUỶ** — quyết định 3 | ✅ |
| C3 | `TierRuntimePolicy` | ~~Re-resolve khi heartbeat báo tier đổi~~ | ❌ **HUỶ** — quyết định 3 | ✅ |
| C4 | `Program.cs:139,152,195-205` | Nhánh offline: xác thực offline grant (chữ ký + `exp` + `hwid`) → cấp đúng tier khi offline | ⏳ chờ S5 | ✅ |
| C5 | `TierDefinitions.cs` | `MergeWithParent` mang thêm `DisplayName`/`Description` | ✅ **xong** | Không |
| C6 | `LicenseApiService.cs` | Gửi `appVersion` trong body verify (K.10.1) | ⏳ chờ chủ sở hữu | ✅ |
| C7 | `VpsManifestService.cs:70` | Hoặc xoá `FetchTierDefinitionsAsync`, hoặc đấu dây nó **kèm kiểm tier** như `VpsRuntimePolicyService` | ⏳ | Không |

C1, C4, C6 nằm trong Protected Files nên cần chủ sở hữu cho phép theo từng task,
đúng như đã làm ở vòng 2. **Hệ quả cần biết:** vì C1 chưa làm, client hôm nay
*không đọc* `license.expiresAt`. Server chặn được key hết hạn (S1) nhưng client
**không cảnh báo trước** khi sắp hết hạn — khách chỉ thấy app không mở được vào
đúng ngày 16.

## PHẦN 5 — Lộ trình

| Pha | Việc | Rủi ro | Cần deploy |
|---|---|---|---|
| **0** | **Hợp nhất hai bản server** (mục L): quyết định Render trỏ vào đâu, rồi đưa `backend/render-license-server/` thành nguồn duy nhất. Trước khi xong pha này, S1–S7 và toàn bộ DataHub **không có tác dụng ở production** | Cao — production đang thiếu enrollment, integrity, update, runtime policy | Render |
| **1** | Backfill `middleCode` (= site-code) cho **mọi key**, bỏ `"0000"`. Chạy với `REQUIRE_UNIQUE_SITE_CODE=0`, đọc log `LICENSE_SITE_CODE_PLACEHOLDER` cho tới khi sạch | Sai site = trộn dữ liệu hai khách. Nguy hiểm nhất trong tài liệu này | Không |
| **2** | S2 + S7 (đã viết xong, chờ pha 0) | Thấp | Render |
| **3** | S1 + S3 ở **chế độ chỉ log**: điền `expiresAt` cho key mới, xem log 1–2 tuần, **chưa** bật chặn | Rất thấp | Render |
| **4** | Bật chặn hết hạn. Key cũ để vắng `expiresAt` = vĩnh viễn cho tới khi chủ sở hữu điền | Sai múi giờ là khoá khách thật — pha 3 phải chạy trước | Render |
| **5** | Bật `REQUIRE_UNIQUE_SITE_CODE=1` sau khi pha 1 sạch log | Trung bình | Render (env) |
| **6** | C1 + C6 (client đọc và cảnh báo hết hạn), rồi S5 + C4 (tier offline có chữ ký) | Chạm file bảo vệ. Task riêng | Render + client |

Thứ tự vẫn theo nguyên tắc cũ — **vá lỗ hổng dữ liệu trước, rồi vá lỗ hổng rẻ,
rồi mới đến tính năng có thể khoá oan khách** — nhưng pha 0 chen lên đầu vì nếu
Render vẫn chạy `AutoJMS-API` thì mọi pha sau chỉ là sửa code không ai chạy.

Phía VPS có lộ trình riêng, **song song** và độc lập với bảng trên, gồm đúng hai
cửa (xem §3.2): (a) đặt `DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY` cùng
`_ISSUER`/`_AUDIENCE` khớp từng ký tự — không có nó thì enroll đóng; (b) publish
seed control-plane — không có nó thì **mọi máy ULTRA chạy như BASE, im lặng**.
Cửa (b) phải xong **trước** khi giao máy ULTRA đầu tiên, chứ không phải sau khi
có ai đó báo "thiếu tính năng".

## PHẦN 6 — Hai bản ghi mẫu

BASE, một site, tạo ngày 2026-08-24:

```json
{
  "schemaVersion": 2,
  "status": "active",
  "tier": "BASE",
  "createdAt": "2026-08-24T09:00:00+07:00",
  "expiresAt": "2026-10-16T00:00:00+07:00",
  "activatedAt": null,
  "graceDays": 7,
  "offlineGraceHours": 72,
  "middleCode": "HN01",
  "siteCodes": ["HN01"],
  "siteId": "",
  "seats": 1,
  "hwid": "",
  "skipHashCheck": true,
  "tokenVersion": 1,
  "dataSpreadsheetId": "",
  "updateChannel": "stable",
  "modulePolicy": { "autoUpdate": false, "silentUpdate": true, "applyOnNextStartup": true },
  "meta": { "customerName": "", "contact": "", "orderId": "", "issuedBy": "owner", "notes": "" }
}
```

ULTRA, hai chi nhánh, ba máy, gia hạn rộng hơn:

```json
{
  "schemaVersion": 2,
  "status": "active",
  "tier": "ULTRA",
  "createdAt": "2026-08-24T09:00:00+07:00",
  "expiresAt": "2026-10-16T00:00:00+07:00",
  "activatedAt": null,
  "graceDays": 14,
  "offlineGraceHours": 168,
  "middleCode": "HCM01",
  "siteCodes": ["HCM01", "HCM02"],
  "siteId": "",
  "seats": 3,
  "hwid": "",
  "skipHashCheck": true,
  "tokenVersion": 1,
  "dataSpreadsheetId": "1AbCdEf_thay_bang_id_that",
  "updateChannel": "stable",
  "modulePolicy": { "autoUpdate": false, "silentUpdate": true, "applyOnNextStartup": true },
  "meta": { "customerName": "", "contact": "", "orderId": "", "issuedBy": "owner", "notes": "" }
}
```

## PHẦN 7 — Những gì cố ý KHÔNG làm

| Không làm | Vì sao |
|---|---|
| Nhóm field vào object lồng | Xem §1.4 — mua rủi ro khoá khách để đổi lấy hình thức |
| Thêm tier thứ ba (PRO/TRIAL) | Chưa có nhu cầu. Bản dùng thử làm bằng `expiresAt` ngắn trên tier BASE, không cần tier mới |
| Đưa danh sách feature vào bản ghi license | Sẽ thành điểm cắt mới: license là nơi khai *tier*, năng lực của tier nằm ở `runtime-policy.{tier}.json` |
| Cho `tier-definitions.json` bật thêm bất cứ thứ gì | File người dùng ghi được. Chỉ thu hẹp |
| Nhét `tier` vào cache offline hiện tại | `BuildCacheSecret` suy ra được từ máy (K.3). Phải là token có chữ ký server |
| Đổi `expiresAt` sang UTC | Mốc bán là giờ treo tường VN; xem nguyên tắc 3 |
| Dùng thư viện timezone | `+07:00` cố định, không DST. Một hằng số rẻ hơn một dependency |
| Bật `REQUIRE_UNIQUE_SITE_CODE` ngay | Mọi key hiện tại đều `"0000"` — bật là khoá sạch fleet |

Rủi ro của chính bản chốt này:

1. **Hết hạn là con dao hai lưỡi.** Điền sai một `expiresAt` là khoá một khách
   đang trả tiền. Pha 3 (chỉ log) là bắt buộc, không phải tuỳ chọn.
2. **Client chưa cảnh báo trước.** Vì C1 chưa làm, ngày hết hạn đến là app im
   lặng không mở. Nên hoặc làm C1 trước pha 4, hoặc chủ sở hữu tự nhắc khách.
3. **`middleCode` đổi vai.** Nó vốn là "mã giữa" trong key; từ nay là danh tính
   tenant. Backfill sai là trộn dữ liệu hai khách.
4. **Offline grant kéo dài** thời gian một cache bị lấy đi còn dùng được. Phải
   buộc vào `hwid` và giữ `exp` ngắn.
5. **Allowlist tier là fail-closed.** Một lỗi chính tả trong Firebase giờ trả 403
   thay vì âm thầm hạ về BASE. Đó là lựa chọn cố ý — lỗi ồn ào sửa trong 5 phút,
   lỗi im lặng sống hàng tháng — nhưng nghĩa là phải nhìn log
   `LICENSE_TIER_INVALID` khi cấp key mới.

## PHẦN 8 — Còn chờ chủ sở hữu

Sáu câu hỏi vòng trước đã chốt hết (PHẦN 0). Còn lại:

| # | Việc | Vì sao cần chủ sở hữu |
|---|---|---|
| 1 | **Render trỏ vào repo nào?** Đưa `backend/render-license-server/` sang `AutoJMS-API`, hay trỏ Render vào thư mục con của monorepo? | Pha 0. Không ai ngoài chủ sở hữu sửa được cấu hình deploy Render |
| 2 | Danh sách site-code thật để backfill `middleCode` | Pha 1. Là dữ liệu, không phải code |
| 3 | Cho làm C1 (`LicenseApiService.cs` — file bảo vệ) để client cảnh báo trước khi hết hạn? | Rủi ro 2 ở trên |
| 4 | J-2: tài liệu rủi ro này nằm trong repo **công khai** | Mục K và L nêu thêm nhiều lỗ hổng chưa vá, và `AutoJMS-API` cũng công khai |
| 5 | J-3: `VALID_EXE_HASHES` đang rỗng → kiểm hash EXE vô hiệu | Chặn việc lật `skipHashCheck` sang `false` |
| 6 | L-2: đặt `DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY` trên VPS và khớp `_ISSUER`/`_AUDIENCE` với license server | §3.2. Code đã đủ; đây là secret production, chỉ chủ sở hữu giữ. Nửa **private** phải ở license server, không ở VPS |
| 7 | L-3: thu hồi Supabase anon key sau khi hợp nhất | Nó đã phát cho mọi client nhiều tháng qua production |
