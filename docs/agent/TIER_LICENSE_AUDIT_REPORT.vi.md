# Báo cáo đối soát: phân quyền Tier, License Key Schema v2, và ranh giới Client ↔ Server

> Phạm vi: `src/AutoJMS/tier-definitions.json`, `TierRuntimePolicy.cs`, `TabManager.cs`,
> `backend/firebase/config-key.json`, `backend/render-license-server/server.js`,
> `license-expiry.js`, `LicenseApiService.cs`, `Program.cs`, `Main.cs`.
> Ngày rà soát: 2026-08-26. Mã nguồn đối chiếu: `c502add` + các sửa đổi chưa commit của phiên này.

---

## 0. Cảnh báo phải đọc trước mọi kết luận: repo ≠ production

Mọi câu trong báo cáo này đều ghi rõ nó nói về **repo** hay về **production**, vì hai thứ đó
**không phải một**.

Render đang phục vụ repo `Datt03-sss/AutoJMS-API` (HEAD `c6f05433`, `server.js` ≈ 895 dòng),
**không phải** `backend/render-license-server/` trong monorepo này (hiện 1 531 dòng). Nghĩa là
mọi thứ báo cáo này mô tả về `server.js` là **đúng với repo, chưa chắc đúng với máy chủ khách
hàng đang gọi**.

Đây là rủi ro nền, không phải một mục rủi ro ngang hàng với các mục khác: nó làm sai lệch mọi
đánh giá khác trong tài liệu. Cho tới khi Owner xác nhận Render trỏ vào đâu, mọi kết luận
"đã vá" chỉ có giá trị với repo.

**Chi tiết lệch — xem §3.2.**

---

## 1. R1 — Đối soát hiện trạng cấu hình Tier

### 1.1 Ba nguồn cấu hình và thứ tự thẩm quyền

| # | Nguồn | Vị trí | Ghi được bởi | Vai trò |
|---|---|---|---|---|
| 1 | **License tier** | Firebase RTDB `Licenses/{key}.tier` | chỉ Owner | **Thẩm quyền tối cao.** Không gì nâng được nó. |
| 2 | **TierDefinitions** | `src/AutoJMS/tier-definitions.json` | *người dùng cuối* (nằm trong InstallDir) | Hiển thị + fallback offline. Chỉ thu hẹp. |
| 3 | **RuntimePolicy** | VPS DataHub `runtime-policy.json` | Owner (qua DataHub) | Kill-switch vận hành. Chỉ thu hẹp. |

Điểm cần nhớ: **nguồn #2 người dùng ghi được**. `TierDefinitions.LoadFromFile()`
(`TierDefinitions.cs:87-100`) đọc thẳng file trong thư mục cài đặt. Chính vì thế nguyên tắc
"chỉ thu hẹp, không nâng quyền" không phải lựa chọn thiết kế cho đẹp — nó là ranh giới bảo mật
duy nhất ngăn khách hàng tự sửa JSON để lên ULTRA.

Ghi chú vận hành: có **hai bản** `tier-definitions.json` trong repo —
`src/AutoJMS/tier-definitions.json` và `backend/datahub/seeds/manifest/tier-definitions.json`.
Bản trong `src/AutoJMS/` **luôn thắng lúc chạy**, vì `Main.cs:150-152` gọi `Resolve` mà không
truyền definitions vào. Bản seed chỉ có tác dụng nếu có caller truyền nó vào tường minh —
hiện không có. Sửa bản seed mà không sửa bản client là **không có tác dụng gì**.

### 1.2 Ma trận quyền năng BASE vs ULTRA

| Năng lực | BASE | ULTRA | Nơi thi hành (đã kiểm chứng) |
|---|:---:|:---:|---|
| Tab **HOME** | ✅ | ✅ | `TabManager.IsTabAllowed` → `_tierConfig.Tabs` |
| Tab **DKCH** | ✅ | ✅ | `TabManager.IsTabAllowed` → `_tierConfig.Tabs` |
| Tab **TRACKING** | ✅ | ✅ | `TabManager.cs:108-109` — kill-switch `tabs.tracking` |
| Tab **PRINT** | ✅ | ✅ | `TabManager.cs:110-111` — kill-switch `tabs.print` |
| Tab **ABOUT** | ✅ | ✅ | luôn là tab cuối (Tab Boundary Rule) |
| **FullStack Operation** (form) | ❌ | ✅ | `TierRuntimePolicy.cs:98-99` + `forms.fullStackOperation` |
| **Background auto-sync** | ❌ | ✅ | `Main.cs:767` `_tierPolicy.EnableBackgroundAutoSync` |
| **Inventory sync** | ❌ | ✅ | `fullStack.inventorySync` |
| **Database tracking** | ❌ | ✅ | `fullStack.databaseTracking` |
| **DataHub Cloud Sync** | ❌ | ✅ | phụ thuộc FullStack + assertion từ license server |
| **Google Sheets** | ✅ | ✅ | **không gate theo tier** — xem ghi chú dưới |
| **Auto/Silent update** | ✅ | ✅ | `modulePolicy`, độc lập tier |

**Google Sheets không bị gate theo tier.** `RuntimePolicyApplier.ApplyGoogleSheets` không hề
đọc tier; cả hai file seed đều đặt `enabled: true`. Đây đúng với quyết định đã chốt của Owner
("Không, BASE cũng được dùng"), nên đây là **hành vi mong muốn**, không phải lỗ hổng — ghi ở
đây để không ai "sửa" nhầm về sau.

**Cả 5 tab đều mở cho cả hai tier.** Khác biệt BASE/ULTRA nằm hoàn toàn ở FullStack Operation
và các tiến trình nền, không nằm ở tab. `tier-definitions.json` liệt kê đúng 5 tab cho cả hai.

### 1.3 Kiểm chứng bất biến "chỉ thu hẹp, không nâng quyền"

**Kết luận: bất biến được thi hành thật, ở mức mã nguồn, không phải chỉ ở tài liệu.**

`TierRuntimePolicy.Resolve(RuntimePolicyDocument, string licenseTier)` —
`TierRuntimePolicy.cs:147-198`:

- Dòng 150: `var entitlement = Resolve(licenseTier);` — quyền gốc tính **từ license tier**, không
  phải từ document.
- Dòng 157-170: mỗi cờ được `AND` giữa entitlement và document. Document đặt `true` cho một cờ
  mà entitlement là `false` → kết quả vẫn `false`.
- Dòng 178: đối tượng kết quả được dựng bằng `entitlement.Tier`. **Document không có đường nào
  đặt được tier.**

Hai chốt chặn phụ, cùng chiều:

- `TierRuntimePolicy.cs:98-99`:
  `isUltra = (normalized == "ULTRA") || (normalized != "BASE" && hasFullStack)`.
  Nhánh leo thang qua `hasFullStack` **loại trừ BASE tường minh**. Một license BASE có
  `forms: [FULLSTACK_OPERATION]` trong `tier-definitions.json` do người dùng tự thêm vẫn **không**
  lên được ULTRA.
- `VpsRuntimePolicyService.TryParsePolicy` (dòng 98-114) từ chối document có `tier` khác tier
  đang yêu cầu; `LoadCachedPolicy` (dòng 140-145) bỏ cache của tier khác. Policy của ULTRA
  không thể bị áp cho phiên BASE.

### 1.4 Phát hiện quan trọng nhất của đợt rà soát: nghịch lý cấu hình DataHub

**Cấu hình DataHub đúng rồi gặp lỗi mạng thoáng qua thì TỆ HƠN là không cấu hình DataHub.**

Hai đường đi, cùng một license ULTRA:

| Tình huống | Đường mã | Kết quả cho ULTRA |
|---|---|---|
| DataHub **không** cấu hình | `Program.cs:350` gate false → `RuntimePolicy` là `null` → `Main.cs:152` `Resolve(CurrentTier)` | **Giữ đủ FullStack** ✅ |
| DataHub **có** cấu hình, fetch policy lỗi | `VpsRuntimePolicyService.cs:53` `SafeDefault("BASE", …)` → `Main.cs:151` `Resolve(doc, "ULTRA")` | **Mất FullStack, background sync, inventory sync, database tracking** ❌ *âm thầm* |

Nguyên nhân gồm **hai** chỗ, sửa một chỗ không đủ:

1. `VpsRuntimePolicyService.cs:53` hard-code `"BASE"` thay vì dùng `normalizedTier` đang có
   sẵn trong hàm.
2. `RuntimePolicyDocument.SafeDefault` (`RuntimePolicyDocument.cs:73-101`) **dù** có tôn trọng
   tier ở thuộc tính `Tier` (dòng 75-78), vẫn đặt **vô điều kiện**
   `forms.fullStackOperation = false`, `fullStack.backgroundSync = false`,
   `FullStack.Enabled = false`, `FullStack.Launch = "DISABLED"` (dòng 88-98).

Vì thế chỉ sửa dòng 53 sẽ **không** khắc phục được: `SafeDefault("ULTRA")` vẫn trả về một
document đã tắt sạch FullStack, và phép `AND` ở `TierRuntimePolicy.cs:157-170` sẽ tắt nốt.

Hệ quả vận hành: khách ULTRA mất tính năng đã trả tiền vì một lần fetch hỏng, **không có thông
báo nào**, và cách "khắc phục" hiệu quả nhất tại hiện trường lại là *gỡ cấu hình DataHub đi*.

> **Đề xuất vá — chưa thực hiện, xem §5.** Đây là nới một cổng tier trong đường safe-default,
> tức là quyết định bảo mật, nên thuộc thẩm quyền Owner.

### 1.5 Cờ policy chết (viết ra nhưng không ai đọc)

| Cờ / thành phần | Nơi ghi | Có ai đọc không |
|---|---|---|
| `tabs.home`, `tabs.dkch`, `tabs.about` | `SafeDefault` + seed | **Không.** `IsTabAllowed` chỉ kill-switch TRACKING/PRINT (`TabManager.cs:108-111`); tab khác trả `true` sau khi đã qua `_tierConfig.Tabs`. |
| `modulePolicy.applyOnNextStartup` | cả hai seed + `config-key.json` | **Không.** `Program.cs:366-367` chỉ đọc `AutoUpdate` và `SilentUpdate`. |
| `VerifyResult.ApplyOnNextStartup` | `LicenseApiService.cs:35`, gán ở dòng 342 | **Không.** Không call-site nào đọc. Đây là đường chết **thứ hai**, độc lập với dòng trên. |
| `VpsManifestService.FetchTierDefinitionsAsync()` | `VpsManifestService.cs` | **Không.** `grep` toàn `src/` chỉ khớp binary. Tier definitions luôn nạp cục bộ qua `TierDefinitions.LoadFromFile()`. |
| `smallUpdateManifest` (khoá manifest) | `server.js` | **Không** — và path trùng hệt `selectorUpdateManifest`. `DataHubManifestUrls` (`VpsConfig.cs:7-44`) không khai báo thuộc tính này, `System.Text.Json` bỏ qua khoá lạ trong im lặng. **Đã xoá** (§4). |

Từ vựng policy **thực sự được tiêu thụ** chỉ gồm 11 khoá:
`forms.fullStackOperation`, `fullStack.backgroundSync`, `fullStack.inventorySync`,
`fullStack.databaseTracking`, `tabs.tracking`, `tabs.print` (trong `TierRuntimePolicy.Resolve`);
`googleSheets.enabled`, `googleSheets.provider`, `print.defaultAutoPrint`,
`print.enablePrinterPreflight`, `debugCapture.enabled` (trong `RuntimePolicyApplier`).

Tắt bất kỳ cờ nào ngoài 11 khoá trên là **không có tác dụng** — nguy hiểm ở chỗ nó *trông như*
đã tắt.

---

## 2. R2 — Đối soát License Key Schema v2

### 2.1 Từ điển trường `Licenses/{licenseKey}` (schema v2)

Đối chiếu với `backend/firebase/config-key.json`. Cột "Server đọc?" nói về repo.

| Trường | Ai ghi | Ai đọc | Mặc định khi thiếu | Hậu quả nếu thiếu / sai |
|---|---|---|---|---|
| `schemaVersion` | Owner | **không ai** | — | Không ảnh hưởng gì. Thuần tài liệu. |
| `status` | Owner | `evaluateLicenseRecord`, route assertion | `"unknown"` | Khác `"active"` → chặn ở cả 4 route. Thiếu → **khoá máy**. |
| `tier` | Owner | verify-license, heartbeat | — | Ngoài `{BASE, ULTRA}` → `LICENSE_TIER_INVALID` (`server.js:329,338`). Thiếu → không đăng nhập được. |
| `createdAt` | Owner | **không ai** | — | Chỉ để người đọc console hiểu. `computeExpiry` **không** được gọi từ đâu (xem §2.3). |
| `expiresAt` | Owner (**gõ tay**) | `evaluateLicense` | `null` | `null` ⇒ **vĩnh viễn**. Gõ sai định dạng ⇒ *cũng* vĩnh viễn — xem §2.2. |
| `activatedAt` | **Server ghi** khi bind HWID lần đầu (`server.js:850`) | không ai | — | Không ảnh hưởng. Chỉ là dấu vết kiểm toán. |
| `graceDays` | Owner | `evaluateLicense` (thi hành thật) | `7` | `null`/`""`/rác → fallback `7`, **không** thành `0` (`license-expiry.js:201-208`). |
| `offlineGraceHours` | Owner | verify-license response | `72` | Trước phiên này **bị bỏ qua hoàn toàn** — đã vá (§4). Không thi hành phía server, chỉ chuyển tiếp. |
| `middleCode` | Owner | verify-license, assertion | `""` | **Chính là Site Code của DataHub.** Để `"0000"` ⇒ trước đây trộn tenant; nay bị chặn (§4). |
| `siteCodes` | Owner | `resolveLicenseSiteCodes` | suy ra từ `siteCode`/`siteId`/`middleCode` | Rỗng sau khi lọc ⇒ **không cấp assertion** (503), nhưng vẫn đăng nhập được. |
| `siteId` | Owner / DataHub | response `datahub.siteId` | `middleCode` | Client cũ dùng; bị GUID từ enrollment thay thế. |
| `seats` | Owner | payload assertion + response | `3`, kẹp `[1, 500]` | **Không thi hành ở license server.** Không đếm phiên ở đâu cả. Thi hành hoàn toàn phụ thuộc VPS DataHub. |
| `hwid` | **Server ghi** lần đầu | khoá HWID | `""` | Khác máy ⇒ `HWID_MISMATCH`. Rỗng ⇒ máy đầu tiên chiếm. |
| `skipHashCheck` | Owner | gate kiểm hash EXE | `false` | `true` ⇒ bỏ kiểm hash cho key đó. |
| `tokenVersion` | Owner | **chỉ** payload assertion (`server.js:202`) | `1`, kẹp `[1, 1e6]` | Không thi hành ở license server. Là chốt thu hồi *phía VPS*. |
| `dataSpreadsheetId` | Owner | `cfg.dataSpreadsheetId` | `""` | Rỗng ⇒ client không có sheet đích. |
| `updateChannel` | Owner | `cfg.updateChannel` | `CONFIG.DEFAULT_CHANNEL` | Sai kênh ⇒ nhận nhầm nhánh cập nhật. |
| `modulePolicy.autoUpdate` | Owner | `Program.cs:366` | **`true` khi thiếu cả khối** | Template v2 ghi `false`, fallback của server ghi `true` — **mâu thuẫn**, xem §5. |
| `modulePolicy.silentUpdate` | Owner | `Program.cs:367` | `true` | — |
| `modulePolicy.applyOnNextStartup` | Owner | **không ai** | `true` | Cờ chết. |
| `meta.*` | Owner | **không ai** | — | Thuần hành chính (tên khách, đơn hàng, ghi chú). |

**Ba trường đáng chú ý nhất**: `seats` và `tokenVersion` **không được thi hành ở license
server** — chúng chỉ được đóng gói vào assertion. Nếu VPS DataHub không kiểm, chúng vô nghĩa.
`offlineGraceHours` cũng chỉ được chuyển tiếp, client tự quyết định chạy offline bao lâu.

### 2.2 Thuật toán hạn "mốc ngày 16"

`license-expiry.js` — hằng số: `TZ_OFFSET_MINUTES = 420` (+07:00 cố định, không DST),
`BILLING_ANCHOR_DAY = 16`, `MIN_TERM_DAYS = 30`, `DEFAULT_GRACE_DAYS = 7`,
`DEFAULT_OFFLINE_GRACE_HOURS = 72`.

`computeExpiry(startAt)`:

1. `floor` = nửa đêm VN của ngày bắt đầu **+ 30 ngày** (`MIN_TERM_DAYS`).
2. `candidate` = nửa đêm VN ngày 16 của tháng chứa `floor`.
3. Nếu `candidate < floor` → tiến một tháng (`month + 1`; `Date.UTC` tự xử lý tràn năm và
   tháng 2 ngắn).

Việc **làm tròn xuống theo ngày** ở bước 1 chính là thứ chặn "key mở ngày 17 được hai kỳ" —
đã có test phủ (`license-expiry.test.js:65-70`), cùng test tràn năm và tháng 2.

**Đánh giá: thuật toán đúng và được test kỹ.** Vấn đề không nằm ở thuật toán.

### 2.3 Vấn đề thật của R2: `computeExpiry` không ai gọi

`grep` toàn repo: `computeExpiry` chỉ xuất hiện trong `license-expiry.js` (định nghĩa + export)
và trong `license-expiry.test.js`. **Không một dòng production nào gọi nó**, và
`backend/firebase/` không có script phát hành key nào (chỉ có template + mô tả schema).

Nghĩa là hôm nay `expiresAt` **được gõ tay vào Firebase console**. Kết hợp với hành vi ở §2.4,
đây là rủi ro sống chứ không phải lý thuyết.

### 2.4 `expiresAt` không đọc được ⇒ license vĩnh viễn

`license-expiry.js:188-199`: `parseInstant` trả `null` ⇒ trả về `effectiveStatus: "active"`,
`allowed: true`, không hạn.

Điều này **đúng** cho bản ghi v1 chưa backfill, và **sai** cho một lỗi gõ — nhưng hai trường
hợp đó **không phân biệt được** ở phía dưới.

Đã kiểm chứng bằng `parseInstant` thật:

| Giá trị gõ vào | Kết quả |
|---|---|
| `"2026-10-16T00:00:00+07:00"` | ✅ đúng hạn |
| `"16-10-2026"` (legacy DD-MM-YYYY) | ✅ đúng hạn |
| `"2026/10/16"` | ⚠️ **parse được** — thành 16/10 |
| `"16/10/2026"` (kiểu VN, ngày trước) | ❌ **null ⇒ vĩnh viễn** |
| `"16.10.2026"` | ❌ **null ⇒ vĩnh viễn** |

Cách gõ tự nhiên nhất của người Việt — ngày trước, gạch chéo — rơi đúng vào ô "vĩnh viễn".
Đã bổ sung cảnh báo `[LICENSE_EXPIRES_AT_UNPARSEABLE]` (§4) để tìm ra được key gõ sai; **không**
đổi hành vi, vì đổi sẽ làm hết hạn toàn bộ đội máy v1 đang chạy.

### 2.5 Ân hạn

| Cơ chế | Thi hành ở đâu | Trạng thái |
|---|---|---|
| `graceDays` (7) | **License server**, cả 4 route qua `evaluateLicenseRecord` | ✅ thi hành thật |
| `offlineGraceHours` (72) | **Không ở server** — chỉ trả về cho client | ⚠️ chỉ tư vấn |

`graceDays` sau khi hết hạn: `effectiveStatus = "grace"`, `allowed = true`. Sau `graceUntil`:
`"expired"`, `allowed = false` → chặn verify-license, heartbeat, google-sheets/grant, và
datahub-assertion (`server.js:1387-1404`). Đường sau cùng quan trọng nhất: không có nó, một
license hết hạn vẫn tự gia hạn quyền ghi vào toàn bộ data plane, mỗi lần một assertion.

---

## 3. R3 — Ranh giới phân quyền & đồng bộ Client ↔ Server

### 3.1 Các điểm kiểm soát

| Kiểm soát | License Server (repo) | Desktop Client | Nhận xét |
|---|---|---|---|
| Tier hợp lệ | ✅ `KNOWN_TIERS` (`server.js:329`) | ✅ `TierRuntimePolicy` | Tier lạ bị chặn từ server. Tốt. |
| Hạn dùng + ân hạn | ✅ 4/4 route | ⚠️ **không cảnh báo trước hạn** | Rủi ro **C1** — client im lặng tới lúc bị chặn. |
| Khoá HWID | ✅ | — | Tốt. |
| Kiểm hash EXE | ⚠️ **bỏ qua nếu env rỗng** (`server.js:808`) | — | Rủi ro **J-3/H-1**. |
| Site code duy nhất | ⚠️ `REQUIRE_UNIQUE_SITE_CODE` mặc định **false** (`server.js:71-72`) | — | Chỉ cảnh báo, không chặn đăng nhập. |
| Placeholder vào assertion | ✅ **đã vá phiên này** (§4) | — | Trước đây trộn tenant. |
| `seats` | ❌ không đếm phiên | ❌ | Phụ thuộc hoàn toàn VPS DataHub. |
| `tokenVersion` | ❌ chỉ đóng gói | ❌ | Như trên. |
| Chống replay token | ⚠️ `jtiCache` **in-memory** (`server.js:290`) | — | Restart ⇒ replay được token trong 60 phút trước đó. **Bắt buộc chạy 1 instance.** |
| Rate limit | ⚠️ in-memory | — | Cùng lý do: 1 instance. |
| Đổi tier realtime | ❌ heartbeat lấy tier từ `decoded.tier` (JWT cũ) | ❌ `_tierPolicy` chỉ tính 1 lần trong constructor `Main` | **Quyết định đã chốt của Owner**: đổi tier cần khởi động lại app. |

### 3.2 Danh sách lệch: repo ↔ production Render

Production (`AutoJMS-API` @ `c6f05433`) **thiếu** so với repo:

| # | Thiếu ở production | Hệ quả |
|---|---|---|
| 1 | `issueDataHubAssertion`, `resolveLicenseSiteCodes` | Không cấp được assertion |
| 2 | Khối `datahub` trong response, `DATAHUB_MANIFESTS` | `Program.cs:350` gate false ⇒ **toàn bộ dịch vụ DataHub là `null`** |
| 3 | `POST /api/datahub/license-assertion` | Không có route |
| 4 | `seats`, `tokenVersion` | Không có trong response |
| 5 | Gate vòng đời ở heartbeat và ở google-sheets/grant | License hết hạn vẫn heartbeat và vẫn lấy được token Sheets |
| 6 | `/api/logout` có xác thực | — |
| 7 | `datahubAssertionLimiter`, `healthLimiter` | Thiếu rate limit |
| 8 | **Còn Supabase** (`supabase.anonKey` phát cho mọi client) | Repo **không còn một dòng Supabase nào** trong `*.js` (chỉ còn dấu vết trong `docs/`). Khoá anon đã lộ ⇒ **cần thu hồi** (L-3). |

Hệ quả tổng hợp cho production hôm nay: `DataHubClient`, `DataHubManifest`, `RuntimeConfig`,
`RuntimePolicy`, `Integrity`, `MajorUpdateServiceInstance`, `SmallUpdate` **đều `null`**.
ULTRA vẫn còn FullStack **duy nhất nhờ** nhánh fallback `Main.cs:152` — tức là đúng cái nghịch
lý ở §1.4 đang che giấu việc DataHub chưa hoạt động ở production.

### 3.3 Rủi ro còn tồn tại (chưa vá)

| Mã | Rủi ro | Vị trí | Vì sao chưa vá |
|---|---|---|---|
| **L-1** | Không rõ Render trỏ repo nào | hạ tầng | **Chờ Owner.** Chặn mọi kết luận khác. |
| **C1** | Client không cảnh báo trước hạn | `LicenseApiService.cs` | **Protected File** — cần Owner cho phép. |
| **H-2** | Cache policy dùng chung một path cho mọi tier | `VpsRuntimePolicyService` | Đã có chốt chặn đọc (dòng 140-145), nhưng vẫn nên tách path. |
| — | Heartbeat 5xx bị phân loại `Fatal` | `LicenseApiService.cs:659` | **Protected File.** Không nhất quán với verify (coi 5xx là `Transient`). 5 lần Fatal liên tiếp chỉ ghi log, app **không** đóng. |
| — | `_tierPolicy` không bao giờ tính lại | `Main.cs:150` | **Protected File** + đúng quyết định "đổi tier cần restart". |
| — | Khởi động offline âm thầm tụt BASE | `Program.cs:139` | **Protected File.** |
| **J-3/H-1** | `VALID_EXE_HASHES` rỗng ⇒ tắt kiểm hash | `server.js:808` | Quyết định vận hành, cần Owner. |
| — | `middleCode: "0000"` trong dữ liệu thật | Firebase | Cần **danh sách site code thật** để backfill. |
| **L-3** | Khoá anon Supabase đã lộ | production | Cần thu hồi. |

**Một cách "hạ tier tức thì" nghe hợp lý nhưng KHÔNG hoạt động** — ghi lại để không ai mất công
thử: sửa trực tiếp `sessions/{sid}/tier` trong Firebase **không có tác dụng**.
`server.js:1277` là `const tier = decoded.tier || sessionData.tier || "BASE"` — `decoded.tier`
lấy từ JWT và **luôn có** với mọi token do `signAccessToken` phát, nên `sessionData.tier` chỉ là
đường lui cho token không mang claim tier. Muốn hạ tier ngay lập tức thì phải **thu hồi session**
(để client buộc đăng nhập lại), không phải sửa tier của session.

---

## 4. Thay đổi đã thực hiện trong phiên này

Tất cả nằm trong `backend/render-license-server/` (**không** phải Protected File), đã kiểm
chứng từng dòng trước khi sửa, và có test phủ.

| # | Thay đổi | Vấn đề nó khắc phục |
|---|---|---|
| A | `resolveLicenseSiteCodes` **loại bỏ** placeholder | `"0000"` là chuỗi truthy nên lọt qua `filter(Boolean)`; **mọi license còn placeholder được cấp assertion cho CÙNG một danh sách SiteCodes** ⇒ các khách hàng đó vào chung một tenant DataHub, đọc và ghi đè dữ liệu của nhau. |
| B | Cảnh báo `[LICENSE_EXPIRES_AT_UNPARSEABLE]` | `expiresAt` gõ sai ⇒ license vĩnh viễn, không phân biệt được với bản ghi v1 (§2.4). |
| C | `resolveOfflineGraceHours(data)` — đọc giá trị **theo từng key** | Schema v2 công bố `offlineGraceHours` theo key nhưng server hard-code giá trị env. Có chống bẫy `Number(null) === 0`. |
| D | Sửa comment `CONFIG` cho đúng sự thật | Comment cũ mô tả sai mức độ thi hành. |
| E | Cảnh báo `[LICENSE_MODULE_POLICY_MISSING]` | Ghi ra được danh sách bản ghi cần backfill — **không** đổi giá trị mặc định (§5). |
| F | **Xoá khoá manifest chết `smallUpdateManifest`** | Trùng path hệt `selectorUpdateManifest` và không client nào đọc (§1.5). Xoá thay vì gán path mới: bịa ra một URL không ai fetch là loại config chết tốn kém hơn. |
| G | **Thêm handler `SIGTERM`** cho graceful shutdown | Render gửi SIGTERM mỗi lần deploy/scale; Node thoát ngay, cắt đứt request đang bay. Các request này **không read-only** — verify-license ghi `hwid`, `activatedAt` và ghi session. Client bị cắt giữa chừng sẽ retry vào một session ghi dở. Chỉ đăng ký trong nhánh `require.main === module` để harness test không thừa hưởng handler cấp process. |

**Phạm vi của bản vá A rất quan trọng**: nó chặn **credential của data plane**, **không** chặn
đăng nhập. Một máy có `middleCode: "0000"` **vẫn đăng nhập được và vẫn làm việc cục bộ bình
thường**, chỉ là chưa được cấp assertion cho tới khi có site code thật. Như vậy nghiêm ngặt hơn
`REQUIRE_UNIQUE_SITE_CODE` (thứ gate đăng nhập) một cách có chủ đích.

**Test**: thêm `test/license-record-fields.test.js` (8 test). Đặt thành **file riêng** vì
`node --test` chạy mỗi *file* trong một process riêng ⇒ có ngân sách rate-limiter riêng;
`verify-license-guards.test.js` đã dùng gần hết hạn mức 20 req/phút do các vòng lặp bên trong.
Thêm một assert vào `verify-license.test.js`: **mọi path manifest phải phân biệt** — hai khoá
trùng path luôn là copy-paste, không bao giờ là thiết kế, và client bỏ khoá lạ trong im lặng
nên lỗi này vô hình từ cả hai đầu.

Kết quả: **123/123 test pass** (`node --test test/*.test.js`); `dotnet build -c Release`
0 warning / 0 error; `eng/harness/verify.ps1` **ALL GATES PASSED**.

---

## 5. Thay đổi **cố ý không** thực hiện — chờ quyết định Owner

Hai mục dưới đây đều có bản vá sẵn sàng; tôi không tự áp vì cả hai là quyết định của Owner,
không phải dọn dẹp mã.

### 5.1 Lật mặc định `modulePolicy.autoUpdate`

Mâu thuẫn: template v2 (`config-key.json`) ghi `autoUpdate: false`, nhưng fallback của server
khi thiếu cả khối `modulePolicy` lại là `autoUpdate: true`. Một bản ghi thiếu `modulePolicy`
sẽ **tự cập nhật**, đúng ngược ý người viết template.

**Vì sao không tự lật**: lật xuống `false` sẽ **đóng băng cập nhật trên mọi bản ghi v1 đang ở
hiện trường**. Đó là quyết định chính sách phát hành. Đã thêm cảnh báo (§4-E) để liệt kê được
các bản ghi cần backfill trước khi lật.

### 5.2 Sửa nghịch lý safe-default của ULTRA (§1.4)

Cần **hai** sửa đổi, đủ cả hai mới có tác dụng:

1. `VpsRuntimePolicyService.cs:53` — dùng `normalizedTier` thay cho hằng `"BASE"`.
2. `RuntimePolicyDocument.SafeDefault` (dòng 88-98) — các cờ FullStack phải theo `tier`, không
   đặt `false` vô điều kiện.

**Vì sao không tự sửa**: đây là **nới một cổng tier trong đường safe-default**. Nới cổng tier là
việc nhạy cảm bảo mật, và các phiên trước đã chốt rằng quyết định về cổng tier thuộc Owner.
Nếu Owner đồng ý, cần kèm bộ test cho đúng ma trận: safe-default của BASE **vẫn phải** tắt
FullStack.

---

## 5bis. Tối ưu backend đã khảo sát nhưng **chưa** áp — kèm lý do

Đây là các cơ hội tối ưu đã xác minh, **không** áp trong phiên này vì mỗi mục đều vượt ra ngoài
"sửa lỗi tối thiểu" theo một cách cụ thể. Xếp theo giá trị giảm dần.

### 5bis.1 N+1 query trong `IngestRepository` — **giá trị lớn nhất**

`src/AutoJMS.DataHub.Api/Infrastructure/IngestRepository.cs`, vòng `foreach (var input in
request.Items)` (dòng 148-190). Với batch tối đa ~200 item:

- Dòng 163 `InsertEventAsync` — **N** round-trip. Gộp được bằng `NpgsqlBatch` với
  `ON CONFLICT DO NOTHING RETURNING id`.
- Dòng 171-173 `ReadProjectionAsync` — tối đa **N** round-trip (đã có cache `seenWaybills` nên
  thực tế là số waybill phân biệt). Gộp được bằng một `SELECT … WHERE waybill_no = ANY(@list)`.

**Vì sao chưa áp**: `ReadProjectionAsync` hiện chỉ chạy cho waybill có event **được nhận**
(không trùng). Prefetch trước vòng lặp sẽ đọc — và nếu query dùng `FOR UPDATE`, sẽ **khoá** —
cả các waybill mà cuối cùng toàn event trùng, tức **mở rộng tập khoá của transaction**. Đó là
thay đổi ngữ nghĩa đồng thời (concurrency), không phải thay đổi hiệu năng thuần tuý, và cần bộ
test đồng thời riêng. Đáng làm, nhưng đáng làm **thành một task riêng có test**, không kèm vào
đợt vá license này.

### 5bis.2 Nén response cho DataHub API — **có đánh đổi bảo mật, cần Owner quyết**

Đề xuất `AddResponseCompression(EnableForHttps = true)`. Lợi ích thật: `SnapshotResponse` trả
tới 10 000 `WaybillProjection` JSON lặp khoá, gzip đạt ~80-90%.

**Vì sao chưa áp**: ASP.NET Core để `EnableForHttps = false` theo mặc định là **có chủ đích** —
đó là biện pháp phòng BREACH/CRIME. Bật nó lên là *chọn* chấp nhận rủi ro đó. Ở API này ít nhất
một response body có mang **device token** (đường enroll), tức có bí mật trong thân phản hồi.
Vậy nên đây là quyết định bảo mật, không phải tối ưu miễn phí. Nếu Owner muốn, cách an toàn là
bật nén **có chọn lọc** cho các endpoint snapshot/changes và loại trừ đường enroll.

### 5bis.3 `Promise.all` cho hai lần đọc Firebase tuần tự

Ở heartbeat và ở `/api/datahub/license-assertion`, hai lần đọc (session rồi license) chạy tuần
tự ⇒ ~2× RTT. Chạy song song giảm còn ~1× RTT — đáng kể vì heartbeat là đường nóng nhất
(mỗi client, mỗi phút).

**Vì sao chưa áp**: khi session không hợp lệ, phiên bản song song **vẫn tốn** một lần đọc
license thừa. Token giả không tới được đây (`jwt.verify` chặn trước), nên chỉ ảnh hưởng trường
hợp token hợp lệ + session đã thu hồi — hữu hạn, nhưng vẫn là tăng chi phí đọc Firebase ở nhánh
lỗi để đổi lấy độ trễ ở nhánh thành công. Đánh đổi này nên do Owner cân, vì Firebase tính tiền
theo lượt đọc.

### 5bis.4 `_deviceLimiter` trùng lặp trong `IngressRateLimitMiddleware`

Trùng ngưỡng với policy `"device"` đã khai trong `UseRateLimiter`. **Vì sao chưa áp**: bỏ nó tạo
một **phụ thuộc ngầm** — endpoint device thêm về sau mà quên `.RequireRateLimiting("device")`
sẽ mất hẳn giới hạn per-device. Mức lợi (một bucket bộ nhớ) không xứng với loại lỗi im lặng đó.

### 5bis.5 Index cho retention scan — **Protected (migration)**

`ix_audit_logs_at` trên `audit_logs (at)` và `ix_dashboard_changes_site_at` trên
`dashboard_changes (site_id, change_at)`. Query retention hiện không có equality predicate trên
`site_id` nên không dùng được index tổ hợp sẵn có ⇒ full scan mỗi chu kỳ. **Migration schema
nằm trong danh sách Protected** ⇒ cần Owner cho phép.

### 5bis.6 Lỗi log ở nhánh Fatal — **Protected**

`LicenseApiService.cs:796-799` gán `_fatalRetryCount = 0` **trước** dòng log
`"Sẽ thử lại (lần {_fatalRetryCount})"` ⇒ log luôn in "lần 0" thay vì số thật. Không ảnh hưởng
hành vi, nhưng làm sai lệch chẩn đoán đúng lúc cần chẩn đoán nhất. **Protected File.**

---

## 6. Danh mục file liên quan & đánh giá an toàn

| File | Vai trò | Người dùng ghi được? | Đánh giá |
|---|---|---|---|
| `src/AutoJMS/tier-definitions.json` | Hiển thị + fallback offline | ✅ **Có** (InstallDir) | ✅ An toàn — chỉ thu hẹp được (§1.3) |
| `backend/datahub/seeds/manifest/tier-definitions.json` | Seed | ❌ | ⚠️ **Không có tác dụng lúc chạy** — dễ gây hiểu nhầm |
| `src/AutoJMS/Licensing/TierRuntimePolicy.cs` | Thi hành AND | ❌ | ✅ Đúng. **Protected File** |
| `src/AutoJMS/Policies/VpsRuntimePolicyService.cs` | Nạp policy | ❌ | ❌ **Lỗi dòng 53** (§1.4) |
| `src/AutoJMS/Policies/RuntimePolicyDocument.cs` | Safe default | ❌ | ❌ **Lỗi dòng 88-98** (§1.4) |
| `src/AutoJMS/Forms/TabManager.cs` | Gate tab | ❌ | ✅ Đúng; 3 cờ tab là cờ chết (§1.5) |
| `backend/firebase/config-key.json` | Template key | ❌ | ⚠️ **`.gitignore` bỏ qua** (`*-key.json`) — **không bao giờ commit**. Bản hiện tại không chứa bí mật. |
| `backend/render-license-server/server.js` | License server | ❌ | ⚠️ Đã vá 5 điểm (§4); **có thể không phải bản đang chạy** (§0) |
| `backend/render-license-server/license-expiry.js` | Vòng đời | ❌ | ✅ Đúng, test kỹ; **nhưng `computeExpiry` không ai gọi** (§2.3) |
| `src/AutoJMS/Licensing/LicenseApiService.cs` | Client gọi API | ❌ | ⚠️ C1 + heartbeat 5xx. **Protected File** |
| `src/AutoJMS/Program.cs` | Khởi động | ❌ | ⚠️ Gate DataHub dòng 350; offline tụt BASE dòng 139. **Protected File** |
| `src/AutoJMS/Forms/Main.cs` | Áp policy | ❌ | ⚠️ `_tierPolicy` tính 1 lần. **Protected File** |

---

## 7. Việc cần Owner quyết

1. **L-1 — Render đang trỏ repo nào?** Chặn mọi kết luận khác (§0, §3.2).
2. **Danh sách site code thật** để backfill `middleCode` cho các key còn `"0000"`.
3. **Lật `modulePolicy.autoUpdate`?** (§5.1)
4. **Sửa nghịch lý safe-default ULTRA?** (§5.2)
5. **Cho phép sửa `LicenseApiService.cs`** (Protected) để client cảnh báo trước hạn — C1.
6. **`VALID_EXE_HASHES`** — đặt giá trị thật hay chấp nhận tắt kiểm hash? (J-3/H-1)
7. **Thu hồi khoá anon Supabase** đã lộ ở production (L-3).
8. **Gọi `computeExpiry` từ đâu** — cần script phát hành key, hay tiếp tục gõ tay `expiresAt`?
   Nếu gõ tay thì §2.4 là rủi ro thường trực.
9. **N+1 trong `IngestRepository`** — cho phép mở thành task riêng kèm test đồng thời? (§5bis.1)
10. **Nén response DataHub API** — chấp nhận đánh đổi BREACH hay bật có chọn lọc? (§5bis.2)
11. **Hai index retention** — cần cho phép tạo migration (Protected). (§5bis.5)
