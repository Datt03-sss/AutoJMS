# Broadcast Update Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sửa 7 lỗi/rủi ro của tính năng "Lệnh cập nhật đồng loạt & App Version" đã ship ở commit `403b999`, để một lệnh phát từ Dashboard thực sự tới được máy trạm, đúng kênh, đúng phiên bản, không làm mất dữ liệu người dùng đang làm và không bão ghi Firebase.

**Architecture:** Ba tầng độc lập, sửa theo đúng ranh giới cũ. **Backend** (`admin-routes.js`, `server.js`) chuẩn hoá chuỗi phiên bản ngay tại biên (git tag → SemVer, client `appVersion` → sanitize) và biến `Licenses/{key}.appVersion` thành giá trị *đơn điệu tăng* kèm sàn tần suất ghi. **Dashboard** (`app.js`) giữ danh sách release trong `state` để suy ra kênh từ phiên bản, và bọc hai thao tác toàn hệ thống bằng `confirmAction` sẵn có. **Client** (`UpdateChannelDialog.cs`, `VelopackUpdateService.cs`, `LicenseApiService.cs`, `Main.cs`) bỏ popup trùng bằng hai cờ tuỳ chọn, chuyển câu hỏi khởi động lại lên *trước* bước tắt dịch vụ, và thay lần gọi một-phát-duy-nhất ở `OnShown` bằng một static event để lệnh tới muộn (khởi động lúc mất mạng) vẫn được hỏi.

**Tech Stack:** Node 20 + Express + Firebase Admin RTDB (`backend/render-license-server`), `node --test` (test runner thuần, không framework), vanilla ES5-safe JS + CSP `script-src 'self'` (dashboard), .NET 8 WinForms + SunnyUI + Velopack 1.2.0 (`src/AutoJMS`), xUnit 2.7.0 (`tests/AutoJMS.Tests`).

## Global Constraints

- **Single-Writer Lock**: acquire `.agent-lock.md` (`Current Writer: Claude Code`, `Mode: WRITE_ACTIVE`) trước khi sửa file đầu tiên, release sau khi push. Task 0 và Task 9 lo việc này.
- **Protected Files — phạm vi được Owner duyệt cho task này chỉ gồm**: `src/AutoJMS/Forms/Main.cs`, `src/AutoJMS/Updates/VelopackUpdateService.cs`, `src/AutoJMS/Licensing/LicenseApiService.cs`. Không chạm bất kỳ Protected File nào khác (`Program.cs`, `TierRuntimePolicy.cs`, `JmsAuthTokenService.cs`, `release/build-release.ps1`, `installer/inno/AutoJMS.iss`, DataHub production config, DB migrations).
- **Không sửa `*.Designer.cs`** — kể cả `Main.Designer.cs`. Mọi UI mới phải dùng control/thuộc tính đã có.
- **Tab Boundary Rule**: không đổi layout hay logic của bất kỳ tab nào (`HOME`, `DKCH`, `TRACKING`, `PRINT`, `ABOUT`); `ABOUT` vẫn là tab cuối.
- **Minimal Edit Rule**: chỉ thay đúng phần cần thay, giữ nguyên style/tên biến/format quanh đó. Không refactor kèm.
- **Secret Policy**: không log token/key đầy đủ — mọi log có license key phải qua `maskLicenseKey` (backend) hoặc dạng `first4...last4`.
- **`<Nullable>disable</Nullable>` toàn project** (`src/AutoJMS/AutoJMS.csproj`). Chỉ `VelopackUpdateService.cs` có `#nullable enable` ở dòng 1. **Không viết `?` trên reference type trong `LicenseApiService.cs` hay `Main.cs`** — sẽ ra warning CS8632 và build hiện đang 0 warning.
- **Commit message**: ASCII-only (tiếng Việt không dấu), kết thúc bằng dòng `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`. Viết message ra file tạm rồi `git commit -F` (Git Bash heredoc ăn backslash).
- **Không `git add .`** — luôn `git add` từng path cụ thể. Không force push, không rewrite history.
- **Không push nếu build fail.**
- Máy Owner 8 GB RAM: chạy `dotnet build-server shutdown` trước `verify.ps1`, nếu 3 gate FAIL cùng lúc thì nghi hết RAM chứ đừng sửa code.

---

## File Structure

| File | Trách nhiệm trong plan này | Loại |
|---|---|---|
| `.agent-lock.md` | Single-writer lock | Modify (Task 0, Task 9) |
| `backend/render-license-server/admin-routes.js` | `versionFromTag` cắt `-Release`; `POST /broadcast-update` tự sửa kênh khi version là beta | Modify (Task 1) |
| `backend/render-license-server/server.js` | `parseSemverParts` / `isNewerOrEqualVersion`; sàn `LICENSE_ACTIVITY_MIN_WRITE_GAP_MS`; sanitize `appVersion` khi ghi session | Modify (Task 2) |
| `backend/render-license-server/test/helpers/harness.js` | Khai báo env mới để mọi boot test khởi đầu sạch | Modify (Task 2) |
| `backend/render-license-server/test/broadcast-update.test.js` | Test hồi quy cho cả 3 hạng mục backend | Modify (Task 1, 2) |
| `backend/render-license-server/dashboard/app.js` | `state.broadcastReleases`; đồng bộ version→channel; 2 `confirmAction`; sửa tooltip cột Phiên bản | Modify (Task 3) |
| `src/AutoJMS/Forms/UpdateChannelDialog.cs` | Cắt hậu tố `-Release` trong `TryParseComparableVersion` | Modify (Task 4) |
| `tests/AutoJMS.Tests/AppVersionTests.cs` | `[Theory]` cho `IsUpgrade` với tag có `-Release` | Modify (Task 4) |
| `src/AutoJMS/Updates/VelopackUpdateService.cs` | 2 cờ `suppressPrompt` / `promptBeforeRestart`; hỏi khởi động lại TRƯỚC khi tắt dịch vụ | Modify (Task 5) |
| `src/AutoJMS/Forms/Main.cs` | Truyền 2 cờ + tiến trình trên thanh tiêu đề; đăng ký/huỷ static event | Modify (Task 6, 7) |
| `src/AutoJMS/Licensing/LicenseApiService.cs` | `BroadcastUpdateReceived` static event | Modify (Task 7) |

Không tạo file mới. Mọi thay đổi là sửa tại chỗ trong file đã tồn tại.

---

## Deviations from the spec — ĐỌC TRƯỚC KHI CODE

Bảy chỗ plan này làm khác bản spec. Mỗi chỗ đều vì code thật khác điều spec giả định.

1. **`directive.Active == true` không tồn tại.** `BroadcastUpdateDirective` (`LicenseApiService.cs:35-48`) chỉ có `Version`, `Channel`, `Message`, `UpdatedAt`. Server chỉ gửi khối `broadcastUpdate` khi lệnh đang bật, nên điều kiện đúng là `directive != null`. → Task 7.
2. **Bỏ dấu `?` trong khai báo event.** Spec viết `public static event Action<BroadcastUpdateDirective>? BroadcastUpdateReceived;`. File này không `#nullable enable` → CS8632. Viết không `?`. → Task 7.
3. **Vị trí cắt `-Release` ở client phải SAU bước cắt `+build`.** Spec chèn ngay dòng 397, nhưng `1.26.12-Release+abc123` khi đó không `EndsWith("-Release")` nên vẫn lỗi. Chèn sau 3 dòng xử lý `+`. → Task 4.
4. **Câu hỏi "khởi động lại?" phải nằm TRƯỚC `_prepareForUpdate`.** Spec ghi "sau dòng 284", nhưng `_prepareForUpdate` (dòng 274-282) chính là `Main.PrepareForUpdateAsync`: nó cancel `_appCts`, tắt auto-sync timer, tắt nhắc Zalo, đóng `FullStackOperation`, nhả DataHub inventory lease, dispose 3 WebView2 rồi chờ 800 ms. Hỏi "để sau?" ở đó là trả lại người dùng một cái app đã bị rút ruột. → Task 5.
5. **Nhánh "Để sau" KHÔNG gọi `WaitExitThenApplyUpdates` và không hứa tự áp dụng.** `grep UpdatePendingRestart` trên `src/AutoJMS/**/*.cs` không ra kết quả — không có code khởi động nào áp dụng gói đã stage; và `WaitExitThenApplyUpdates` chỉ chờ tối đa **60 giây** rồi bỏ. Nên câu "sẽ tự động áp dụng trong lần mở app kế tiếp" là lời hứa không ai giữ. Thay bằng: giữ app sống nguyên vẹn, nói thật rằng gói đã tải xong và chỉ cho đường áp dụng thủ công (tab GIỚI THIỆU → Kiểm tra cập nhật, không phải tải lại vì Velopack cache gói). Muốn tự áp dụng khi mở app thì phải sửa `Program.cs` — Protected File, **không** nằm trong phạm vi Owner duyệt cho task này → báo Owner ở Final Report. → Task 5.
6. **Sàn 30 giây cần một env knob và làm một test hiện có phải sửa.** Hard-code `30_000` sẽ làm test `broadcast-update.test.js:234` ("a version change is written immediately, whatever the interval says", seed `lastActiveAt: Date.now()`) đổi màu. Thêm `LICENSE_ACTIVITY_MIN_WRITE_GAP_MS` (mặc định 30 000, cho phép 0), khai báo trong harness, và set `0` ở boot của `test.describe("verify-license and heartbeat")` — test đó vốn nói về `LICENSE_ACTIVITY_WRITE_INTERVAL_MS`, giữ đúng phát biểu của nó. Sàn 30 s được kiểm bằng test riêng. → Task 2.
7. **Quy tắc đơn điệu đổi *ý nghĩa* của cột Phiên bản.** Sau Task 2, `Licenses/{key}.appVersion` là "phiên bản CAO NHẤT từng thấy trên license này", không còn là "phiên bản máy báo về gần nhất". Sự thật từng-máy nằm ở `sessions/{sessionId}`. Tooltip cột phải nói đúng điều đó, nếu không Owner sẽ đọc sai. → Task 3.

---

### Task 0: Acquire single-writer lock

**Files:**
- Modify: `.agent-lock.md`

**Interfaces:**
- Consumes: —
- Produces: quyền ghi cho mọi task sau.

- [ ] **Step 1: Đồng bộ cây làm việc**

```bash
git switch main && git pull --ff-only origin main && git status --short
```

Expected: `Already up to date.` hoặc fast-forward; `git status --short` chỉ còn ` M .agent-lock.md` (Antigravity đã reset lock nhưng chưa commit).

- [ ] **Step 2: Đọc lock hiện tại**

```bash
sed -n '1,30p' .agent-lock.md
```

Expected: `Current Writer: None`, `Mode: READ_ONLY`, `Scope: None`. Nếu thấy writer khác đang `WRITE_ACTIVE` → **DỪNG, hỏi Owner**, không được ghi đè.

- [ ] **Step 3: Ghi lock cho Claude Code**

Đổi 3 trường trong khối trạng thái (giữ nguyên format hiện có của file, chỉ thay giá trị):

- `Current Writer: None` → `Current Writer: Claude Code`
- `Mode: READ_ONLY` → `Mode: WRITE_ACTIVE`
- `Scope: None` → `Scope: fix(update) broadcast hardening - admin-routes.js, server.js, dashboard/app.js, UpdateChannelDialog.cs, VelopackUpdateService.cs, LicenseApiService.cs, Main.cs, tests`

Nếu file có trường thời gian (`Acquired At` / `Updated At`), điền giờ VN bằng:

```bash
date -u -d '+7 hours' '+%Y-%m-%d %H:%M (UTC+7)'
```

(Git Bash trên máy này không có `/usr/share/zoneinfo`, nên `TZ=Asia/Ho_Chi_Minh date` im lặng trả về GMT.)

- [ ] **Step 4: Commit lock**

```bash
git add .agent-lock.md
git commit -m "chore(lock): claude code acquire write lock cho broadcast update hardening" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

Expected: 1 file changed. **Không push** — lock commit đi cùng lần push ở Task 8.

---

### Task 1: Git tag → SemVer, và tự sửa kênh khi phiên bản là beta

**Files:**
- Modify: `backend/render-license-server/admin-routes.js:1456-1465` (`versionFromTag`), `:1670-1673` (kiểm tra kênh trong `POST /broadcast-update`)
- Test: `backend/render-license-server/test/broadcast-update.test.js`

**Interfaces:**
- Consumes: `KNOWN_CHANNELS` (Set `["stable","beta"]`, dòng 119), `BROADCAST_VERSION_PATTERN` (dòng 180), `fail(res, status, code, message)`, `logEvent(level, event, fields)`.
- Produces: `versionFromTag(tag)` trả SemVer sạch (`1.26.12`, `1.26.12-beta.1`); `POST /api/admin/broadcast-update` ghi `channel: "beta"` khi `version` chứa `-beta`.

- [ ] **Step 1: Viết test thất bại**

Thêm vào cuối `test.describe("admin broadcast routes")` trong `backend/render-license-server/test/broadcast-update.test.js` (khối kết ở khoảng dòng 473 — chèn *trước* dấu `});` đóng describe):

```js
    test("a -beta version forces the beta channel even when stable was asked for", async () => {
        const harness = await startServer({ env: { ADMIN_SECRET_TOKEN: ADMIN_TOKEN } });
        try {
            harness.db.reset({});
            const res = await harness.post("/api/admin/broadcast-update", {
                body: { active: true, version: "1.26.12-beta.1", channel: "stable" },
                ...withAuth()
            });

            assert.equal(res.status, 200);
            // A beta build exists only on the beta feed. Honouring "stable" here
            // would send every station to a feed with no such release, and Velopack
            // would answer "you are up to date" — a broadcast that looks delivered
            // and changes nothing.
            assert.equal(storedDirective(harness).channel, "beta");
        } finally { await harness.close(); }
    });

    test("a plain version keeps the channel the owner chose", async () => {
        const harness = await startServer({ env: { ADMIN_SECRET_TOKEN: ADMIN_TOKEN } });
        try {
            harness.db.reset({});
            const res = await harness.post("/api/admin/broadcast-update", {
                body: { active: true, version: "1.26.12", channel: "stable" },
                ...withAuth()
            });

            assert.equal(res.status, 200);
            assert.equal(storedDirective(harness).channel, "stable");
        } finally { await harness.close(); }
    });
```

Và thêm vào cuối khối release-list (`test.describe` dùng `stubServer()`, kết ở khoảng dòng 586 — chèn trước `});` đóng describe):

```js
    test("a release tag keeps its -Release suffix out of the version", async () => {
        const harness = await stubServer([
            { tag_name: "v1.26.12-Release", prerelease: false, published_at: "2026-09-01T00:00:00Z" },
            { tag_name: "v1.26.13-beta.1-Release", prerelease: true, published_at: "2026-09-02T00:00:00Z" }
        ]);
        try {
            const res = await harness.get("/api/admin/releases", withAuth());
            assert.equal(res.status, 200);

            const versions = res.body.releases.map(r => r.version);
            // AutoJMS tags every build `-Release`. Left on, the client's semver
            // comparison reads it as a prerelease label and refuses the upgrade.
            assert.ok(versions.includes("1.26.12"), `stable version not normalised: ${versions.join(", ")}`);
            assert.ok(versions.includes("1.26.13-beta.1"), `beta version not normalised: ${versions.join(", ")}`);
        } finally { await harness.close(); }
    });
```

> Trước khi chèn, đọc `sed -n '475,600p' backend/render-license-server/test/broadcast-update.test.js` để khớp đúng tên helper `stubServer` và chữ ký nó nhận (mảng release thô của GitHub). Nếu chữ ký khác, sửa **lời gọi trong test**, không sửa helper.

- [ ] **Step 2: Chạy test để chắc nó fail**

```bash
cd backend/render-license-server && node --test test/broadcast-update.test.js
```

Expected: FAIL — `channel: "beta"` nhận được `"stable"`, và `versions` chứa `1.26.12-Release` thay vì `1.26.12`.

- [ ] **Step 3: Sửa `versionFromTag`**

Thay nguyên khối (doc comment + hàm, dòng 1456-1465):

```js
/**
 * The plain SemVer inside a git tag.
 *
 * AutoJMS tags every build `-Release` (`v1.26.12-Release`,
 * `v1.26.13-beta.1-Release`) — a house convention, not a SemVer prerelease
 * label. Handing it to the client untouched makes `1.26.12-Release` sort BELOW
 * `1.26.12`, so a station reads the newest build as a downgrade and refuses it;
 * and `beta.1-Release` / `beta.2-Release` both parse their build number as 0,
 * so no beta ever supersedes another. Strip it here, at the one place tags
 * become versions.
 */
function versionFromTag(tag) {
    return String(tag || "")
        .trim()
        .replace(/^[vV]/, "")
        .replace(/-[Rr]elease$/i, "");
}
```

> Doc comment hiện tại nói tag có dạng `v1.26.12`; đọc `sed -n '1450,1470p'` rồi thay đúng số dòng của comment cũ.

- [ ] **Step 4: Tự sửa kênh trong `POST /broadcast-update`**

Trong `POST /broadcast-update`, thay khối lấy + kiểm tra kênh (dòng 1670-1673):

```js
        const channel = String(req.body?.channel || "").trim().toLowerCase();
        if (!KNOWN_CHANNELS.has(channel)) {
```

thành:

```js
        const requestedChannel = String(req.body?.channel || "").trim().toLowerCase();
        if (!KNOWN_CHANNELS.has(requestedChannel)) {
```

(giữ nguyên thân `fail(...)` và dấu `}` đóng bên dưới), rồi chèn ngay sau dấu `}` đó:

```js

        // A beta build exists only on the beta feed. A broadcast naming one while
        // saying "stable" sends every station to a feed that has no such release:
        // Velopack answers "you are up to date", so the broadcast looks delivered
        // and changes nothing. Correct it here rather than trusting the form.
        const channel = version.toLowerCase().includes("-beta") ? "beta" : requestedChannel;
        if (channel !== requestedChannel) {
            logEvent("info", "admin.broadcast_channel_corrected", { version, requestedChannel, channel });
        }
```

Không đổi gì ở `const directive = { active: true, version, channel, message, updatedAt: now };` — nó đã dùng đúng biến `channel`.

> Thứ tự quan trọng: khối này phải nằm **sau** `const version = ...` (dòng 1660) và sau kiểm tra `BROADCAST_VERSION_PATTERN`, để không đọc một `version` chưa validate.

- [ ] **Step 5: Chạy test để chắc nó pass**

```bash
cd backend/render-license-server && node --test test/broadcast-update.test.js
```

Expected: `pass 32 | fail 0` (29 test cũ + 3 mới).

- [ ] **Step 6: Commit**

```bash
git add backend/render-license-server/admin-routes.js backend/render-license-server/test/broadcast-update.test.js
git commit -m "fix(admin): strip -Release suffix tu git tag va ep kenh beta theo version" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Chống bão ghi Firebase & làm sạch phiên bản khi ghi session

**Files:**
- Modify: `backend/render-license-server/server.js:521-524` (chèn helper sau `sanitizeAppVersion`), `:535` (chèn knob sau `LICENSE_ACTIVITY_WRITE_INTERVAL_MS`), `:554-584` (`recordLicenseActivity`), `:1377` (ghi session)
- Modify: `backend/render-license-server/test/helpers/harness.js:202-206` (danh sách env)
- Test: `backend/render-license-server/test/broadcast-update.test.js`

**Interfaces:**
- Consumes: `sanitizeAppVersion(v)` → `""` khi không đọc được; `numericEnv(raw, fallback, min)`; `sessionTimestamp(v)`; `maskLicenseKey(key)`; `withTimeout(promise, ms, label)`.
- Produces: `parseSemverParts(v)` → `{ parts: number[], pre: string }`; `isNewerOrEqualVersion(incoming, stored)` → `boolean`; env `LICENSE_ACTIVITY_MIN_WRITE_GAP_MS` (mặc định `30_000`, min `0`).

- [ ] **Step 1: Khai báo env mới trong harness**

Trong `backend/render-license-server/test/helpers/harness.js`, ngay dưới dòng `LICENSE_ACTIVITY_WRITE_INTERVAL_MS: undefined,` (khoảng dòng 203) thêm:

```js
        LICENSE_ACTIVITY_MIN_WRITE_GAP_MS: undefined,
```

> Danh sách này liệt kê **mọi** env `server.js` đọc, đặt về `undefined` ở từng lần boot. Thiếu một dòng là một biến rò rỉ giữa các boot trong cùng file test.

- [ ] **Step 2: Viết test thất bại**

Trong `test.describe("verify-license and heartbeat")` (mở ở dòng 51), thêm `LICENSE_ACTIVITY_MIN_WRITE_GAP_MS: 0` vào `env` của helper boot ở đầu describe — tìm `{ BROADCAST_UPDATE_CACHE_MS: 0, LICENSE_ACTIVITY_WRITE_INTERVAL_MS: 0 }` và đổi thành:

```js
            env: {
                BROADCAST_UPDATE_CACHE_MS: 0,
                LICENSE_ACTIVITY_WRITE_INTERVAL_MS: 0,
                LICENSE_ACTIVITY_MIN_WRITE_GAP_MS: 0
            }
```

Cũng thêm `LICENSE_ACTIVITY_MIN_WRITE_GAP_MS: 0` vào `env` của test ở **dòng 234** ("a version change is written immediately, whatever the interval says") — test đó nói về khoảng 10 phút, không phải về sàn 30 giây:

```js
            env: {
                BROADCAST_UPDATE_CACHE_MS: 0,
                LICENSE_ACTIVITY_WRITE_INTERVAL_MS: 600_000,
                LICENSE_ACTIVITY_MIN_WRITE_GAP_MS: 0
            }
```

Rồi thêm 3 test mới, chèn ngay sau test ở dòng 234 (cùng cấp `test.describe`):

```js
    test("an older station does not drag the licence version backwards", async () => {
        const harness = await startServer({
            env: { BROADCAST_UPDATE_CACHE_MS: 0, LICENSE_ACTIVITY_WRITE_INTERVAL_MS: 0, LICENSE_ACTIVITY_MIN_WRITE_GAP_MS: 0 }
        });
        try {
            harness.db.reset(seedWithActiveSession({ appVersion: "1.26.12" }));
            await harness.post("/api/heartbeat", {
                body: { appVersion: "1.26.11" },
                token: harness.signToken()
            });

            // One licence, several stations. The licence-level column means
            // "highest build seen here"; the older machine's own build stays on
            // its session row.
            assert.equal(harness.db.read(`Licenses/${FIXTURE.licenseKey}`).appVersion, "1.26.12");
        } finally { await harness.close(); }
    });

    test("a newer station still moves the licence version forward", async () => {
        const harness = await startServer({
            env: { BROADCAST_UPDATE_CACHE_MS: 0, LICENSE_ACTIVITY_WRITE_INTERVAL_MS: 0, LICENSE_ACTIVITY_MIN_WRITE_GAP_MS: 0 }
        });
        try {
            harness.db.reset(seedWithActiveSession({ appVersion: "1.26.12-beta.1" }));
            await harness.post("/api/heartbeat", {
                body: { appVersion: "1.26.12" },
                token: harness.signToken()
            });

            // A release supersedes its own prerelease.
            assert.equal(harness.db.read(`Licenses/${FIXTURE.licenseKey}`).appVersion, "1.26.12");
        } finally { await harness.close(); }
    });

    test("two writes cannot land inside the minimum gap", async () => {
        const harness = await startServer({
            env: {
                BROADCAST_UPDATE_CACHE_MS: 0,
                LICENSE_ACTIVITY_WRITE_INTERVAL_MS: 0,
                LICENSE_ACTIVITY_MIN_WRITE_GAP_MS: 30_000
            }
        });
        try {
            harness.db.reset(seedWithActiveSession({ appVersion: "1.26.11", lastActiveAt: Date.now() }));
            await harness.post("/api/heartbeat", {
                body: { appVersion: "1.26.12" },
                token: harness.signToken()
            });

            // The floor holds even for a version change: a station that just
            // updated re-activates (force: true) on restart and is written then.
            assert.equal(harness.db.read(`Licenses/${FIXTURE.licenseKey}`).appVersion, "1.26.11");
        } finally { await harness.close(); }
    });
```

Và một test cho session sanitize — chèn cùng chỗ:

```js
    test("an unreadable version never reaches the session row", async () => {
        const harness = await startServer({
            env: { BROADCAST_UPDATE_CACHE_MS: 0, LICENSE_ACTIVITY_WRITE_INTERVAL_MS: 0, LICENSE_ACTIVITY_MIN_WRITE_GAP_MS: 0 }
        });
        try {
            harness.db.reset(activeLicenseSeed());
            const res = await harness.post("/api/verify-license", {
                body: {
                    licenseKey: FIXTURE.licenseKey,
                    hwid: FIXTURE.hwid,
                    appVersion: "1.26.12<script>alert(1)</script>"
                }
            });

            assert.equal(res.status, 200);
            const sessions = Object.values(harness.db.read("sessions") || {});
            assert.equal(sessions.length, 1);
            assert.equal(sessions[0].appVersion, "");
        } finally { await harness.close(); }
    });
```

> `activeLicenseSeed()` là **placeholder cần thay**: dùng đúng helper seed mà các test `verify-license` khác trong file này đang dùng (đọc `sed -n '51,120p' backend/render-license-server/test/broadcast-update.test.js` — describe này có helper `seed`/`verify` cục bộ; dùng chúng và bỏ `activeLicenseSeed()`). Payload `verify-license` cũng phải khớp đúng những field helper `verify` đã gửi.

- [ ] **Step 3: Chạy test để chắc nó fail**

```bash
cd backend/render-license-server && node --test test/broadcast-update.test.js
```

Expected: FAIL 4 test mới — licence version bị tụt về `1.26.11`; sàn 30 s chưa có nên `1.26.12` được ghi; session nhận `1.26.12<script>alert(1)</script>`.

- [ ] **Step 4: Thêm 2 helper so sánh phiên bản**

Chèn ngay **sau** `sanitizeAppVersion` (kết ở dòng 524) trong `backend/render-license-server/server.js`:

```js

/**
 * A version reduced to the numbers a comparison can use.
 *
 * Deliberately not a SemVer library: only two shapes reach this — what
 * AppVersion.Current reports (`1.26.12`, `1.26.12-beta.1`, or the 4-part
 * assembly fallback) and a git tag that arrived with `-Release` still attached.
 * Anything unreadable degrades to zeros instead of throwing.
 */
function parseSemverParts(v) {
    const clean = String(v || "").trim().replace(/^[vV]/, "").replace(/-[Rr]elease$/i, "");
    const [main, pre] = clean.split("-");
    const parts = String(main || "").split(".").map(n => parseInt(n, 10) || 0);
    while (parts.length < 3) parts.push(0);
    return { parts, pre: pre || "" };
}

/**
 * Whether `incoming` may replace `stored` on the LICENCE record.
 *
 * One licence can hold several stations. When they run different builds, every
 * heartbeat saw `version !== storedVersion` and wrote — two stations a minute
 * apart meant a write per beat, forever, and the dashboard column flickered
 * between the two. Keeping the licence-level version monotonic ends that: the
 * older station simply has nothing to add. Its own build is still recorded on
 * its session row, which is where per-machine truth belongs.
 */
function isNewerOrEqualVersion(incoming, stored) {
    if (!stored) return true;
    if (!incoming) return false;

    const a = parseSemverParts(incoming);
    const b = parseSemverParts(stored);

    for (let i = 0; i < Math.max(a.parts.length, b.parts.length); i++) {
        const diff = (a.parts[i] || 0) - (b.parts[i] || 0);
        if (diff > 0) return true;
        if (diff < 0) return false;
    }

    if (!a.pre && b.pre) return true;
    if (a.pre && !b.pre) return false;
    return a.pre >= b.pre;
}
```

- [ ] **Step 5: Thêm knob sàn tần suất**

Chèn ngay **sau** `LICENSE_ACTIVITY_WRITE_INTERVAL_MS` (dòng 535):

```js

/**
 * The floor under any licence-activity write, version change included.
 *
 * The ten-minute interval above only rations writes that carry nothing new. It
 * does not stop the case that genuinely thrashes: two stations on one licence
 * whose reported versions each look "newer or equal" to the other — versions
 * differing only past what parseSemverParts reads — alternating every beat.
 * Thirty seconds costs no real telemetry, because a station that has just
 * updated re-activates through verify-license (force: true) on restart and is
 * written immediately anyway.
 */
const LICENSE_ACTIVITY_MIN_WRITE_GAP_MS = numericEnv(
    process.env.LICENSE_ACTIVITY_MIN_WRITE_GAP_MS,
    30_000,
    0
);
```

- [ ] **Step 6: Sửa điều kiện ghi trong `recordLicenseActivity`**

Thay phần đầu thân hàm (từ `const version = sanitizeAppVersion(appVersion);` đến `if (version !== "") patch.appVersion = version;`) bằng:

```js
    const version = sanitizeAppVersion(appVersion);
    const now = Date.now();

    const storedVersion = sanitizeAppVersion(record?.appVersion);
    const lastActiveAt = sessionTimestamp(record?.lastActiveAt) ?? 0;
    const sinceLastWrite = now - lastActiveAt;

    // Only a version that does not move the stored one backwards may be written.
    const versionWritable = version !== "" && isNewerOrEqualVersion(version, storedVersion);
    const versionChanged = versionWritable && version !== storedVersion;

    if (!force) {
        if (sinceLastWrite < LICENSE_ACTIVITY_MIN_WRITE_GAP_MS) return;
        if (!versionChanged && sinceLastWrite < LICENSE_ACTIVITY_WRITE_INTERVAL_MS) return;
    }

    const patch = { lastActiveAt: now };
    if (versionWritable) patch.appVersion = version;
```

Phần `try { await withTimeout(...) } catch { logEvent("warn", "license.activity_write_failed", { license: maskLicenseKey(licenseKey), message: e.message }); }` giữ nguyên không đổi — log đã che key đúng chuẩn.

- [ ] **Step 7: Sanitize phiên bản khi ghi session**

Tại `backend/render-license-server/server.js:1377`, thay:

```js
            appVersion: appVersion || "",
```

bằng:

```js
            appVersion: sanitizeAppVersion(appVersion),
```

- [ ] **Step 8: Chạy toàn bộ test backend**

```bash
cd backend/render-license-server && npm test
```

Expected: `fail 0`, tổng số test = 237 (trước task này) + số test mới của Task 1 và Task 2. Nếu có test cũ đỏ, **đọc lại nó trước khi sửa** — nó có thể đang phát biểu đúng một hành vi plan này vô tình đổi.

- [ ] **Step 9: Commit**

```bash
git add backend/render-license-server/server.js backend/render-license-server/test/helpers/harness.js backend/render-license-server/test/broadcast-update.test.js
git commit -m "fix(license): giu appVersion don dieu tang va dat san tan suat ghi firebase" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Dashboard — đồng bộ kênh theo phiên bản, xác nhận trước khi phát lệnh

**Files:**
- Modify: `backend/render-license-server/dashboard/app.js` — `state` (dòng 72-84), `loadBroadcastReleases` (1533-1556), `submitBroadcast` (1634-1654), wiring (1677-1684), `versionCell`

**Interfaces:**
- Consumes: `confirmAction({ title, message, okText, cancelText, danger })` → `Promise<boolean>` (dòng 873; trả `false` ngay nếu modal confirm đang mở); `sendBroadcast(payload, submitLabel)` → `Promise`; `dom.broadcastVersion` / `dom.broadcastChannel` / `dom.broadcastDisable`; `el(tag, props)`.
- Produces: `state.broadcastReleases` → `Array<{ version, channel, publishedAt? }>`; `syncBroadcastChannel()`.

- [ ] **Step 1: Thêm `broadcastReleases` vào `state`**

Trong `backend/render-license-server/dashboard/app.js`, chèn trước dòng `// Edit-modal state.` (dòng 74):

```js

        // Broadcast-modal state. The release list is kept, not just rendered into
        // the datalist, because picking a version has to be able to look its
        // channel up — a `<datalist>` option carries no data back to us.
        broadcastReleases: [],
```

- [ ] **Step 2: Lưu danh sách release**

Trong `loadBroadcastReleases`, ngay sau dòng `const releases = Array.isArray(result?.releases) ? result.releases : [];` (1539) thêm:

```js
            state.broadcastReleases = releases;
```

Và trong nhánh `catch` (1553-1555), thêm ngay trước dòng `dom.broadcastReleaseHint.textContent = ...`:

```js
            state.broadcastReleases = [];
```

> Xoá danh sách khi tải lỗi để listener không suy kênh theo dữ liệu của lần mở modal trước.

- [ ] **Step 3: Thêm hàm đồng bộ kênh**

Chèn ngay **sau** `loadBroadcastReleases` (sau dấu `}` ở dòng 1556):

```js

    /**
     * Keeps the channel select honest about the version typed beside it.
     *
     * A beta build exists only on the beta feed. Leaving the select on "stable"
     * after picking `1.26.12-beta.1` sends every station to a feed with no such
     * release, and each one reports back "you are already up to date" — the
     * broadcast looks delivered and does nothing. The server corrects this too;
     * doing it here means the owner SEES the channel they are about to send.
     */
    function syncBroadcastChannel() {
        const version = dom.broadcastVersion.value.trim();
        if (!version) return;

        const matched = state.broadcastReleases.find(release => release.version === version);
        if (matched && (matched.channel === "beta" || matched.channel === "stable")) {
            dom.broadcastChannel.value = matched.channel;
            return;
        }

        // Hand-typed version: the `-beta` label is all we have to go on.
        dom.broadcastChannel.value = version.toLowerCase().includes("-beta") ? "beta" : "stable";
    }
```

- [ ] **Step 4: Nối listener**

Trong `init()`, chèn ngay sau dòng `dom.broadcastForm.addEventListener("submit", submitBroadcast);` (1680):

```js
        // Both events: `change` fires when a datalist option is picked, `input`
        // when the version is typed by hand.
        dom.broadcastVersion.addEventListener("input", syncBroadcastChannel);
        dom.broadcastVersion.addEventListener("change", syncBroadcastChannel);
```

- [ ] **Step 5: Xác nhận trước khi BẬT lệnh**

Thay `submitBroadcast` (1634-1654) bằng:

```js
    async function submitBroadcast(event) {
        event.preventDefault();

        const version = dom.broadcastVersion.value.trim();
        if (!version) {
            dom.broadcastError.textContent = "Nhập phiên bản muốn đẩy, ví dụ 1.26.12.";
            dom.broadcastError.hidden = false;
            dom.broadcastVersion.focus();
            return;
        }

        const channel = dom.broadcastChannel.value;
        const ok = await confirmAction({
            title: "Bật lệnh cập nhật đồng loạt",
            message: `Phát lệnh yêu cầu TẤT CẢ máy trạm cập nhật lên ${version} (kênh ${channel})?`,
            okText: "Bật cập nhật",
            danger: false
        });
        if (!ok) return;

        return sendBroadcast(
            {
                active: true,
                version,
                channel,
                message: dom.broadcastMessage.value
            },
            "Đang bật…"
        );
    }
```

> `event.preventDefault()` vẫn là câu đầu tiên và chạy đồng bộ, nên việc hàm thành `async` không làm form submit thật.

- [ ] **Step 6: Xác nhận trước khi TẮT lệnh**

Thay dòng 1684 (`dom.broadcastDisable.addEventListener("click", () => sendBroadcast({ active: false }, "Đang tắt…"));`) bằng:

```js
        dom.broadcastDisable.addEventListener("click", async () => {
            const ok = await confirmAction({
                title: "Tắt lệnh cập nhật đồng loạt",
                message: "Tắt lệnh cập nhật đồng loạt? Máy trạm sẽ không còn nhận thông báo này.",
                okText: "Tắt cập nhật",
                danger: true
            });
            if (!ok) return;
            return sendBroadcast({ active: false }, "Đang tắt…");
        });
```

Giữ nguyên 3 dòng comment ngay trên nó (1681-1683).

- [ ] **Step 7: Sửa tooltip cột Phiên bản cho khớp ý nghĩa mới**

Sau Task 2, giá trị trên `Licenses/{key}` là phiên bản cao nhất từng thấy trên license đó, không phải phiên bản máy báo về gần nhất. Trong `versionCell(license)`, thay 3 dòng `title:`:

```js
            title: seen
                ? `Cao nhat da thay tren license nay. Bao ve lan cuoi: ${seen[3]}/${seen[2]}/${seen[1]} ${seen[4]}:${seen[5]}`
                : "Phien ban cao nhat cac may tram tren license nay tung bao ve"
```

bằng bản có dấu (file này dùng tiếng Việt có dấu, UTF-8):

```js
            title: seen
                ? `Phiên bản cao nhất đã thấy trên license này. Báo về lần cuối: ${seen[3]}/${seen[2]}/${seen[1]} ${seen[4]}:${seen[5]}`
                : "Phiên bản cao nhất mà các máy trạm trên license này từng báo về"
```

> Chỉ thay đúng 3 dòng `title:`; `class`, `text` và nhánh `badge--version-unknown` giữ nguyên.

- [ ] **Step 8: Kiểm tra cú pháp và tham chiếu DOM**

Browser pane không chạy được dashboard local (`file://` bị đổi thành `data:` nên `styles.css`/`app.js` không bao giờ load) — kiểm bằng script:

```bash
cd backend/render-license-server/dashboard && node --check app.js && node -e "const fs=require('fs');const js=fs.readFileSync('app.js','utf8');const html=fs.readFileSync('index.html','utf8');const ids=[...js.matchAll(/\\\$\\(\"([^\"]+)\"\\)/g)].map(m=>m[1]);const missing=ids.filter(id=>!html.includes('id=\"'+id+'\"'));console.log(missing.length?'MISSING IDS: '+missing.join(', '):'ALL DOM IDS OK ('+ids.length+')');process.exit(missing.length?1:0)"
```

Expected: `ALL DOM IDS OK (…)`, không lỗi cú pháp.

- [ ] **Step 9: Commit**

```bash
git add backend/render-license-server/dashboard/app.js
git commit -m "fix(dashboard): dong bo kenh theo version va xac nhan truoc khi phat lenh dong loat" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: Client — cắt hậu tố `-Release` khi so sánh phiên bản

**Files:**
- Modify: `src/AutoJMS/Forms/UpdateChannelDialog.cs:392-400` (`TryParseComparableVersion`)
- Test: `tests/AutoJMS.Tests/AppVersionTests.cs`

**Interfaces:**
- Consumes: `internal static bool UpdateChannelDialog.IsUpgrade(string currentVersion, string targetVersion)` (dòng 374-377), `IsDowngrade` (361-364) — cả hai đã lộ ra test qua `InternalsVisibleTo Include="AutoJMS.Tests"` (`AutoJMS.csproj:67`).
- Produces: `IsUpgrade`/`IsDowngrade` đọc đúng tag `-Release` trong mọi trường hợp.

- [ ] **Step 1: Viết test thất bại**

Thêm vào cuối class trong `tests/AutoJMS.Tests/AppVersionTests.cs` (giữ style `[Theory]`/`[InlineData]` đã có ở file này):

```csharp
        // AutoJMS tags every build `-Release`. Read as a SemVer prerelease label
        // it sorts BELOW the plain version, so the newest build looked like a
        // downgrade; and `beta.1-Release` / `beta.2-Release` both parsed their
        // build number as 0, so no beta ever superseded another.
        [Theory]
        [InlineData("1.26.11", "1.26.12-Release", true)]
        [InlineData("1.26.12", "1.26.12-Release", false)]
        [InlineData("1.26.12-Release", "1.26.12", false)]
        [InlineData("1.26.12-beta.1-Release", "1.26.12-beta.2-Release", true)]
        [InlineData("1.26.12-beta.2-Release", "1.26.12-beta.1-Release", false)]
        [InlineData("1.26.12-beta.1", "1.26.12-Release", true)]
        [InlineData("1.26.11", "v1.26.12-Release+abc1234", true)]
        public void IsUpgrade_HandlesReleaseSuffixedTags(string current, string target, bool expected)
        {
            Assert.Equal(expected, UpdateChannelDialog.IsUpgrade(current, target));
        }

        [Theory]
        [InlineData("1.26.12", "1.26.11-Release", true)]
        [InlineData("1.26.12", "1.26.12-Release", false)]
        public void IsDowngrade_HandlesReleaseSuffixedTags(string current, string target, bool expected)
        {
            Assert.Equal(expected, UpdateChannelDialog.IsDowngrade(current, target));
        }
```

> Đọc `tests/AutoJMS.Tests/AppVersionTests.cs` trước để lấy đúng namespace/tên class và thứ tự tham số của `IsUpgrade(currentVersion, targetVersion)`. Nếu file chưa `using AutoJMS.Forms;` thì thêm.

- [ ] **Step 2: Chạy test để chắc nó fail**

```bash
dotnet test .\tests\AutoJMS.Tests\AutoJMS.Tests.csproj -c Release --filter "FullyQualifiedName~ReleaseSuffixedTags"
```

Expected: FAIL — `IsUpgrade("1.26.11", "1.26.12-Release")` trả `false` (bị coi là prerelease thấp hơn), và cặp `beta.1` vs `beta.2` trả `false` vì cả hai parse ra build 0.

- [ ] **Step 3: Cắt hậu tố `-Release`**

Trong `src/AutoJMS/Forms/UpdateChannelDialog.cs`, thay:

```csharp
            var clean = value.Trim().TrimStart('v', 'V');
            var plus = clean.IndexOf('+');
            if (plus >= 0) clean = clean.Substring(0, plus);
```

bằng:

```csharp
            var clean = value.Trim().TrimStart('v', 'V');
            var plus = clean.IndexOf('+');
            if (plus >= 0) clean = clean.Substring(0, plus);

            // AutoJMS tags every build `-Release`; it is a house convention, not a
            // SemVer prerelease label. Left on, it sorts the newest build below the
            // plain version, and it swallows a beta's build number
            // (`beta.1-Release` parses as beta build 0, same as `beta.2-Release`).
            // After the `+build` strip, so `1.26.12-Release+abc1234` is caught too.
            if (clean.EndsWith("-Release", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(0, clean.Length - "-Release".Length);
```

- [ ] **Step 4: Chạy test để chắc nó pass**

```bash
dotnet test .\tests\AutoJMS.Tests\AutoJMS.Tests.csproj -c Release --filter "FullyQualifiedName~ReleaseSuffixedTags"
```

Expected: `Passed! - Failed: 0`, 9 test.

- [ ] **Step 5: Commit**

```bash
git add src/AutoJMS/Forms/UpdateChannelDialog.cs tests/AutoJMS.Tests/AppVersionTests.cs
git commit -m "fix(update): bo hau to -Release khi so sanh semver o client" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Client — bỏ popup trùng, hỏi khởi động lại trước khi tắt dịch vụ

**Files:**
- Modify: `src/AutoJMS/Updates/VelopackUpdateService.cs:177` (chữ ký), `:242-252` (popup xác nhận), `:273-293` (chuẩn bị + áp dụng)

**Interfaces:**
- Consumes: `_prepareForUpdate` (`Func<CancellationToken, Task>`, là `Main.PrepareForUpdateAsync`), `_channel` (string), `ShowInfo(string)`, `ShowError(string)`, `AppLogger.Info/Error`, `mgr.ApplyUpdatesAndRestart(updateInfo)`.
- Produces: `public async Task CheckAndUpdateAsync(IProgress<int>? downloadProgress = null, CancellationToken ct = default, bool suppressPrompt = false, bool promptBeforeRestart = false)` — hai cờ mới, **mặc định `false`** nên mọi call site cũ (tab GIỚI THIỆU) giữ nguyên hành vi.

- [ ] **Step 1: Mở rộng chữ ký**

Thay dòng 177:

```csharp
        public async Task CheckAndUpdateAsync(IProgress<int>? downloadProgress = null, CancellationToken ct = default)
```

bằng:

```csharp
        /// <param name="suppressPrompt">
        /// Người gọi đã xin phép người dùng rồi (lệnh cập nhật đồng loạt từ Dashboard).
        /// Hỏi lại ở đây là hộp thoại thứ hai y hệt hộp thoại vừa bấm Yes.
        /// </param>
        /// <param name="promptBeforeRestart">
        /// Hỏi trước khi khởi động lại. Mặc định false để luồng thủ công ở tab
        /// GIỚI THIỆU giữ nguyên hành vi cũ.
        /// </param>
        public async Task CheckAndUpdateAsync(
            IProgress<int>? downloadProgress = null,
            CancellationToken ct = default,
            bool suppressPrompt = false,
            bool promptBeforeRestart = false)
```

> Giữ nguyên doc comment `<summary>` đang có phía trên (nếu có) và chèn 2 khối `<param>` này bên dưới nó.

- [ ] **Step 2: Bỏ popup trùng**

Thay khối 242-252:

```csharp
            var confirm = MessageBox.Show(
                $"Có bản cập nhật mới: v{newVersion}\n\nBạn có muốn cập nhật ngay không?",
                "AutoJMS Update",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                AppLogger.Info($"VelopackUpdateService: user declined update v{newVersion} on channel={_channel}");
                return;
            }
```

bằng:

```csharp
            if (!suppressPrompt)
            {
                var confirm = MessageBox.Show(
                    $"Có bản cập nhật mới: v{newVersion}\n\nBạn có muốn cập nhật ngay không?",
                    "AutoJMS Update",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (confirm != DialogResult.Yes)
                {
                    AppLogger.Info($"VelopackUpdateService: user declined update v{newVersion} on channel={_channel}");
                    return;
                }
            }
            else
            {
                AppLogger.Info($"VelopackUpdateService: prompt suppressed, caller already has consent for v{newVersion} on channel={_channel}");
            }
```

- [ ] **Step 3: Hỏi khởi động lại TRƯỚC khi tắt dịch vụ**

Thay toàn bộ khối 273-293 (comment `// Stop running services...`, try `_prepareForUpdate`, và try `ApplyUpdatesAndRestart`):

```csharp
            // Câu hỏi này phải đứng TRƯỚC _prepareForUpdate. _prepareForUpdate là
            // Main.PrepareForUpdateAsync: nó cancel _appCts, tắt auto-sync timer,
            // tắt nhắc Zalo, đóng FullStackOperation, nhả DataHub inventory lease và
            // dispose các WebView2. Hỏi "để sau?" sau đó là trả lại một cái app đã
            // bị rút ruột.
            if (promptBeforeRestart)
            {
                var restartNow = MessageBox.Show(
                    $"Đã tải xong bản cập nhật v{newVersion}.\n\nKhởi động lại ngay để hoàn tất?",
                    "Hoàn tất tải cập nhật",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (restartNow != DialogResult.Yes)
                {
                    // Không gọi _prepareForUpdate: app phải còn nguyên để làm việc tiếp.
                    // Cũng không gọi WaitExitThenApplyUpdates — updater chỉ chờ tối đa
                    // 60 giây rồi bỏ, và không có code khởi động nào áp dụng gói đã
                    // stage, nên hứa "tự áp dụng lần mở kế tiếp" là hứa suông.
                    AppLogger.Info($"VelopackUpdateService: v{newVersion} downloaded; user chose to restart later.");
                    ShowInfo(
                        $"Đã tải xong bản cập nhật v{newVersion}, gói đã nằm sẵn trên máy.\n\n" +
                        $"Khi nào tiện, vào tab GIỚI THIỆU → Kiểm tra cập nhật (kênh {_channel}) rồi chọn Có để hoàn tất. Lần đó không phải tải lại.");
                    return;
                }
            }

            // Stop running services before the process is replaced/restarted.
            try
            {
                if (_prepareForUpdate != null)
                    await _prepareForUpdate(ct).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"VelopackUpdateService: prepare-for-update reported: {ex.Message}");
            }

            try
            {
                AppLogger.Info($"VelopackUpdateService: applying update v{newVersion} and restarting.");
                mgr.ApplyUpdatesAndRestart(updateInfo);
            }
            catch (Exception ex)
            {
                AppLogger.Error("VelopackUpdateService: apply & restart failed", ex);
                ShowError($"Áp dụng bản cập nhật thất bại.\n\n{ex.Message}");
            }
```

- [ ] **Step 4: Build**

```bash
dotnet build .\AutoJMS.slnx -c Release
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. **Bất kỳ warning nào cũng là hồi quy** — build hiện tại 0 warning. CS8632 nghĩa là đã viết `?` ở file không bật nullable.

- [ ] **Step 5: Commit**

```bash
git add src/AutoJMS/Updates/VelopackUpdateService.cs
git commit -m "fix(update): them co suppressPrompt va promptBeforeRestart, hoi khoi dong lai truoc khi tat dich vu" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: Client — truyền 2 cờ và hiện tiến trình tải

**Files:**
- Modify: `src/AutoJMS/Forms/Main.cs:1463` (câu hỏi), `:1479-1500` (gọi service)

**Interfaces:**
- Consumes: `CheckAndUpdateAsync(IProgress<int>?, CancellationToken, bool suppressPrompt, bool promptBeforeRestart)` từ Task 5; `_appCts`; `this.Text`.
- Produces: —

- [ ] **Step 1: Sửa câu hỏi cho khớp hành vi mới**

Sau Task 5, app **không** còn tự khởi động lại — nó hỏi. Thay dòng 1463:

```csharp
                text.Append("Cập nhật ngay bây giờ? Ứng dụng sẽ tự khởi động lại sau khi tải xong.");
```

bằng:

```csharp
                text.Append("Cập nhật ngay bây giờ? Tải xong sẽ hỏi lại trước khi khởi động lại.");
```

- [ ] **Step 2: Thêm tiến trình trên thanh tiêu đề và truyền 2 cờ**

Thay khối 1479-1500 (từ `var updateSvc = new VelopackUpdateService(` đến hết `catch (Exception ex)`):

```csharp
                // Tiến trình tải hiện trên thanh tiêu đề: người dùng đang ở tab nào
                // cũng thấy, và không phải thêm control mới (Main.Designer.cs là
                // Protected File). Designer đặt Text = "AutoJMS" và Main.cs chưa bao
                // giờ gán lại, nên đây là bề mặt trống duy nhất luôn nhìn thấy được.
                string originalTitle = this.Text;
                var progress = new Progress<int>(percent =>
                {
                    if (this.IsDisposed) return;
                    this.Text = percent >= 100
                        ? "AutoJMS — Đang áp dụng cập nhật…"
                        : $"AutoJMS — Đang tải cập nhật {percent}%";
                });

                var updateSvc = new VelopackUpdateService(
                    channel,
                    PrepareForUpdateAsync,
                    (installedVersion, targetVersion, selectedChannel) =>
                    {
                        // Lệnh đồng loạt chỉ đẩy tới, không bao giờ kéo lùi phiên bản.
                        AppLogger.Warning(
                            $"[BroadcastUpdate] từ chối hạ cấp: installed={installedVersion ?? "UNKNOWN"}, " +
                            $"target={targetVersion ?? "UNKNOWN"}, channel={selectedChannel}");
                        return false;
                    });

                try
                {
                    // suppressPrompt: người dùng vừa bấm Yes ở hộp thoại trên, hỏi lại
                    // là hộp thoại thứ hai y hệt. promptBeforeRestart: không tắt app
                    // đột ngột giữa lúc đang nhập liệu.
                    await updateSvc.CheckAndUpdateAsync(
                        progress,
                        _appCts.Token,
                        suppressPrompt: true,
                        promptBeforeRestart: true);
                }
                finally
                {
                    if (!this.IsDisposed) this.Text = originalTitle;
                }
            }
            catch (OperationCanceledException)
            {
                // App đang đóng — không có gì để báo.
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[BroadcastUpdate] thất bại: {ex.Message}");
            }
```

> `Progress<int>` được tạo trên UI thread (hàm này chạy qua `BeginInvoke`), nên callback tự marshal về UI thread — không cần `Invoke` thủ công.

- [ ] **Step 3: Build**

```bash
dotnet build .\AutoJMS.slnx -c Release
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/AutoJMS/Forms/Main.cs
git commit -m "fix(main): truyen co prompt cho broadcast update va hien tien trinh tai tren title bar" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: Client — nhận lệnh cập nhật tới muộn (offline startup gap)

**Files:**
- Modify: `src/AutoJMS/Licensing/LicenseApiService.cs` — khai báo event (cạnh `CurrentBroadcastUpdate`, dòng 170), `RememberBroadcastUpdate` (623-666)
- Modify: `src/AutoJMS/Forms/Main.cs:186-187` (đăng ký), `:1027-1029` (huỷ đăng ký), `:1422` (doc comment), thêm handler cạnh `CheckAndPromptBroadcastUpdateAsync`

**Interfaces:**
- Consumes: `LicenseApiService.CurrentBroadcastUpdate` (`BroadcastUpdateDirective`), `BroadcastUpdateDirective` (chỉ có `Version`, `Channel`, `Message`, `UpdatedAt` — **không có** `Active`), `CheckAndPromptBroadcastUpdateAsync()`, `Main_FormClosing` (dòng 1013).
- Produces: `public static event Action<BroadcastUpdateDirective> LicenseApiService.BroadcastUpdateReceived;` (**không có** `?` — file này không bật nullable); `private void Main.OnBroadcastUpdateReceived(BroadcastUpdateDirective directive)`.

- [ ] **Step 1: Khai báo static event**

Trong `src/AutoJMS/Licensing/LicenseApiService.cs`, chèn ngay **trên** khai báo `CurrentBroadcastUpdate` (dòng 170):

```csharp
        /// <summary>
        /// Bắn khi verify-license hoặc heartbeat mang về một lệnh cập nhật đồng loạt.
        /// </summary>
        /// <remarks>
        /// Cần thiết vì một máy khởi động lúc mất mạng sẽ thấy directive đầu tiên là
        /// null; nếu chỉ đọc CurrentBroadcastUpdate một lần lúc mở form thì lệnh về
        /// sau (qua heartbeat) không bao giờ tới được người dùng.
        /// Handler chạy trên thread của heartbeat — người nghe phải tự marshal về UI.
        /// </remarks>
        public static event Action<BroadcastUpdateDirective> BroadcastUpdateReceived;
```

- [ ] **Step 2: Bắn event khi parse được directive**

Ở cuối `RememberBroadcastUpdate(JsonElement root)`, thay:

```csharp
            CurrentBroadcastUpdate = directive;
            return directive;
```

bằng:

```csharp
            CurrentBroadcastUpdate = directive;

            // Server chỉ gửi khối broadcastUpdate khi lệnh đang bật, nên directive
            // khác null đã là "đang bật". Nuốt lỗi của người nghe: một handler hỏng
            // không được phép làm chết heartbeat.
            if (directive != null)
            {
                try
                {
                    BroadcastUpdateReceived?.Invoke(directive);
                }
                catch { }
            }

            return directive;
```

> Nếu hàm có nhiều điểm `return` khác gán `CurrentBroadcastUpdate`, chỉ sửa điểm gán directive hợp lệ này. Nhánh gán `null` (lệnh đã tắt) **không** bắn event.

- [ ] **Step 3: Đăng ký trong constructor**

Trong `src/AutoJMS/Forms/Main.cs`, thay:

```csharp
            _tabManager.ApplyTier(CurrentTier);
            _userSettings = new UserSettingsService();
```

bằng:

```csharp
            _tabManager.ApplyTier(CurrentTier);

            // Heartbeat nền có thể mang lệnh cập nhật về MUỘN hơn lúc mở app (khởi
            // động lúc mất mạng thì directive đầu tiên là null). Đăng ký ở constructor
            // để đúng một lần cho mỗi form; huỷ trong Main_FormClosing.
            LicenseApiService.BroadcastUpdateReceived += OnBroadcastUpdateReceived;

            _userSettings = new UserSettingsService();
```

- [ ] **Step 4: Thêm handler**

Chèn ngay **trên** `private async Task CheckAndPromptBroadcastUpdateAsync()` (trên khối doc comment ở dòng ~1410):

```csharp
        /// <summary>
        /// Lệnh cập nhật đồng loạt vừa về từ verify-license hoặc heartbeat.
        /// </summary>
        /// <remarks>
        /// Chạy trên thread của heartbeat, nên phải marshal về UI thread trước khi
        /// hiện MessageBox. Cờ _broadcastUpdatePromptedThisSession bên trong
        /// CheckAndPromptBroadcastUpdateAsync lo việc không hỏi hai lần.
        /// </remarks>
        private void OnBroadcastUpdateReceived(BroadcastUpdateDirective directive)
        {
            if (directive == null) return;
            if (this.IsDisposed || !this.IsHandleCreated) return;

            try
            {
                this.BeginInvoke(new Action(() => _ = CheckAndPromptBroadcastUpdateAsync()));
            }
            catch (ObjectDisposedException)
            {
                // Form đóng ngay giữa lúc heartbeat về — không có gì để làm.
            }
        }
```

> Nếu `Main.cs` chưa có `using AutoJMS.Licensing;` thì thêm; kiểm bằng `grep -n "using AutoJMS.Licensing" src/AutoJMS/Forms/Main.cs`.

- [ ] **Step 5: Huỷ đăng ký khi đóng form**

Trong `Main_FormClosing`, thay:

```csharp
            _isExiting = true;

            this.Hide();
```

bằng:

```csharp
            _isExiting = true;

            // Event tĩnh giữ tham chiếu tới form: không huỷ là leak, và heartbeat
            // vẫn gọi vào một form đã dispose. Phải đặt SAU _isExiting = true, vì
            // người dùng có thể đã bấm Huỷ ở hộp thoại thoát phía trên.
            LicenseApiService.BroadcastUpdateReceived -= OnBroadcastUpdateReceived;

            this.Hide();
```

- [ ] **Step 6: Sửa doc comment đã cũ**

Dòng 1422 hiện nói lệnh bật giữa ca chỉ nhận được ở lần mở app kế tiếp — sau task này thì không còn đúng. Thay:

```csharp
        /// Chỉ hỏi một lần mỗi phiên — lệnh bật giữa ca sẽ được nhận ở lần mở app kế tiếp.
```

bằng:

```csharp
        /// Chỉ hỏi một lần mỗi phiên. Lệnh bật giữa ca vẫn tới được, qua
        /// LicenseApiService.BroadcastUpdateReceived.
```

- [ ] **Step 7: Build**

```bash
dotnet build .\AutoJMS.slnx -c Release
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. Nếu ra CS8632 → đã viết `?` trong file không bật nullable, xoá đi.

- [ ] **Step 8: Commit**

```bash
git add src/AutoJMS/Licensing/LicenseApiService.cs src/AutoJMS/Forms/Main.cs
git commit -m "fix(update): nhan lenh cap nhat dong loat toi muon qua static event" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: Verify toàn bộ và push

**Files:**
- Không sửa file nào.

**Interfaces:**
- Consumes: mọi thay đổi của Task 1-7.
- Produces: 1 lần push lên `origin/main`.

- [ ] **Step 1: Restore + build Release**

```bash
dotnet restore .\AutoJMS.slnx && dotnet build .\AutoJMS.slnx -c Release
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Test backend**

```bash
cd backend/render-license-server && npm test
```

Expected: `fail 0`.

- [ ] **Step 3: Test .NET**

```bash
dotnet test .\tests\AutoJMS.Tests\AutoJMS.Tests.csproj -c Release
```

Expected: `Failed: 0`.

- [ ] **Step 4: Harness đầy đủ**

```bash
dotnet build-server shutdown && powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
```

Expected: `Build PASS | Tests PASS | NodeTests PASS | Secrets PASS | Structure PASS` và `OVERALL: ALL GATES PASSED`.

> Máy này 8 GB RAM. Nếu Tests/NodeTests/Secrets FAIL **cùng lúc** thì là hết bộ nhớ, không phải lỗi code: chạy lại `dotnet build-server shutdown` rồi `verify.ps1`, đừng sửa code theo triệu chứng đó.

- [ ] **Step 5: Squash-free review diff**

```bash
git status --short && git log --oneline main@{u}..HEAD
```

Expected: working tree sạch; danh sách commit gồm `chore(lock)` + các commit của Task 1-7.

- [ ] **Step 6: Push**

```bash
git push origin main && git log --oneline -1
```

Expected: push thành công, không force.

> Nếu `origin/main` đã tiến lên trong lúc làm (agent khác push), `git pull --rebase origin main` rồi build lại **trước** khi push. Không force push.

---

### Task 9: Nhả lock và viết Final Report

**Files:**
- Modify: `.agent-lock.md`

**Interfaces:**
- Consumes: kết quả Task 8.
- Produces: lock về `None` / `READ_ONLY`; Final Report 10 mục cho Owner.

- [ ] **Step 1: Nhả lock**

Đưa `.agent-lock.md` về `Current Writer: None`, `Mode: READ_ONLY`, `Scope: None` (đúng giá trị đã đọc ở Task 0 Step 2).

- [ ] **Step 2: Commit và push lock**

```bash
git add .agent-lock.md
git commit -m "chore(lock): claude code release write lock" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
git push origin main
```

- [ ] **Step 3: Xuất Final Report 10 mục**

Theo đúng format bắt buộc trong `CLAUDE.md`: **1. Summary — 2. Files Changed — 3. Build/Verify Result — 4. Commit Message — 5. Commit Hash — 6. Pushed To — 7. Behavior Changed — 8. Behavior Intentionally Unchanged — 9. Owner Manual Test Checklist — 10. Risks.**

**Mục 9 — Owner Manual Test Checklist phải gồm:**

1. Dashboard → 📢 → chọn một bản beta từ danh sách xổ xuống → ô **Kênh** tự nhảy sang `beta`.
2. Dashboard → 📢 → gõ tay `1.26.13-beta.2` → Kênh tự sang `beta`; gõ `1.26.13` → về `stable`.
3. Dashboard → 📢 → Bật → xuất hiện modal xác nhận có nêu đúng phiên bản + kênh; bấm Huỷ thì **không** có request nào đi.
4. Dashboard → 📢 → Tắt → modal xác nhận màu nguy hiểm; Huỷ thì lệnh vẫn đang bật.
5. Máy trạm đang ở bản cũ: nhận lệnh → **chỉ một** hộp thoại hỏi cập nhật (không còn hộp thứ hai).
6. Bấm Có → thanh tiêu đề chạy `AutoJMS — Đang tải cập nhật xx%`, kiểm được từ mọi tab.
7. Tải xong → hỏi "Khởi động lại ngay?" → chọn **Không** → app còn dùng được bình thường (DKCH nhập được, TRACKING tra được, PRINT in được), tiêu đề trở lại `AutoJMS`.
8. Lặp lại kịch bản 5-7 nhưng chọn **Có** → app khởi động lại và lên bản mới.
9. Rút mạng → mở app → cắm mạng lại → chờ một nhịp heartbeat → hộp thoại cập nhật xuất hiện (đây là lỗi #5 đã sửa).
10. Từ chối một lần rồi chờ nhịp heartbeat kế tiếp → **không** bị hỏi lại trong cùng phiên.
11. Một license 2 máy chạy 2 bản khác nhau → cột **Phiên bản** trên Dashboard đứng yên ở bản cao hơn, không nhảy qua lại.
12. Tab GIỚI THIỆU → Kiểm tra cập nhật (luồng thủ công) → vẫn hỏi xác nhận như trước và vẫn tự khởi động lại — **không** được đổi.
13. Thứ tự tab không đổi, `ABOUT` vẫn cuối cùng.

**Mục 10 — Risks phải nêu:**

- **Chưa có auto-apply khi mở app.** Chọn "Để sau" thì gói đã tải nằm chờ, nhưng không có code khởi động nào áp dụng nó; người dùng phải vào tab GIỚI THIỆU. Làm tự động cần sửa `Program.cs` (`UpdateManager.UpdatePendingRestart`) — Protected File, **chưa** được duyệt trong task này. **Xin Owner quyết.**
- **Ý nghĩa cột Phiên bản đã đổi**: giờ là "bản cao nhất từng thấy trên license", per-máy nằm ở `sessions/{sessionId}`. Tooltip đã nói rõ, nhưng Owner cần biết.
- **Sàn 30 giây** làm telemetry `lastActiveAt` thô hơn một chút cho license nhiều máy. Hạ được bằng env `LICENSE_ACTIVITY_MIN_WRITE_GAP_MS` mà không cần deploy code.
- **Prompt có thể chen ngang giữa ca.** Đây đúng là điều lỗi #5 yêu cầu, nhưng hệ quả là hộp thoại modal có thể hiện lúc người dùng đang nhập liệu. Chỉ hỏi một lần mỗi phiên nên không quấy rầy lặp lại.
- **`server.js:1060` và `:1237` vẫn log `appVersion` thô** (`appVersion || null`, không qua `sanitizeAppVersion`) — cùng gốc với lỗi #6 nhưng chỉ là log, nên cố ý **để ngoài** diff này để không mở rộng phạm vi. Đề xuất làm ở task sau.
- **`.agent/rules/04-update-release-rules.md:203`** vẫn ghi "No automatic updates - user must click About tab", và `manualOnly: true` ở dòng 175/188 — đã cũ từ commit `403b999`. Cần Owner duyệt mới sửa tài liệu rule.

---

## Self-Review

**1. Spec coverage**

| Yêu cầu trong spec | Task |
|---|---|
| §1.1 `versionFromTag` cắt `v` + `-Release` | Task 1 Step 3 |
| §1.2 `POST /broadcast-update` ép `channel = "beta"` khi version có `-beta` | Task 1 Step 4 |
| §2 Vị trí 1 — `server.js:1377` dùng `sanitizeAppVersion` | Task 2 Step 7 |
| §2 Vị trí 2 — `parseSemverParts` + `isNewerOrEqualVersion` | Task 2 Step 4 |
| §2 quy tắc 1 — sàn 30 giây | Task 2 Step 5 + 6 (qua env `LICENSE_ACTIVITY_MIN_WRITE_GAP_MS`) |
| §2 quy tắc 2 — không cho tụt phiên bản trên License gốc | Task 2 Step 6 |
| §2 quy tắc 3 — giữ nguyên khoảng 10 phút | Task 2 Step 6 (`LICENSE_ACTIVITY_WRITE_INTERVAL_MS` còn nguyên) |
| §3.1 `state.broadcastReleases` + listener đồng bộ kênh | Task 3 Step 1-4 |
| §3.2 hai `confirmAction` | Task 3 Step 5-6 |
| §4 File 1 — cắt `-Release` trong `TryParseComparableVersion` | Task 4 Step 3 |
| §4 File 2 — 2 cờ + hỏi trước khi khởi động lại | Task 5 Step 1-3 |
| §4 File 3 — `Main.cs` truyền 2 cờ | Task 6 Step 2 |
| §5 File 1 — static event + `Invoke` trong `RememberBroadcastUpdate` | Task 7 Step 1-2 |
| §5 File 2 — đăng ký, handler UI-thread, huỷ đăng ký | Task 7 Step 3-5 |
| §6.1 test `versionFromTag` cắt `-Release` (stable + beta) | Task 1 Step 1 (test thứ 3) |
| §6.2 test máy cũ không làm tụt `appVersion` | Task 2 Step 2 (test thứ 1) |
| §6.3 test version `-beta` ép channel `beta` | Task 1 Step 1 (test thứ 1) |
| Single-writer lock | Task 0, Task 9 |
| `maskLicenseKey`, không log key đầy đủ | Global Constraints; Task 2 Step 6 giữ nguyên log đã che |
| Protected Files trong phạm vi | Global Constraints; File Structure |
| `dotnet restore` / `build -c Release` / harness / commit + push | Task 8 |

Không còn mục nào của spec không có task. Hai chỗ spec **không** yêu cầu mà plan thêm vào, đều là hệ quả trực tiếp: sửa câu hỏi ở `Main.cs:1463` (Task 6 Step 1) và tooltip cột Phiên bản (Task 3 Step 7) — cả hai sẽ thành *sai sự thật* nếu để nguyên.

**2. Placeholder scan**

Không có "TBD", "implement later", "add appropriate error handling", hay "similar to Task N". Mọi step sửa code đều mang code đầy đủ.

Hai chỗ **cố ý** yêu cầu executor đọc file trước khi chèn, và đã ghi rõ lệnh đọc + việc cần khớp:
- Task 1 Step 1: chữ ký `stubServer()` trong khối release-list.
- Task 2 Step 2: `activeLicenseSeed()` là tên tạm — phải thay bằng helper `seed`/`verify` cục bộ của `test.describe("verify-license and heartbeat")`.

Lý do không viết cứng: hai helper này là nội bộ file test và spec không mô tả; đoán sai chữ ký sẽ tạo ra code không chạy — tệ hơn là một chỉ dẫn rõ ràng.

**3. Type consistency**

- `versionFromTag(tag) → string`: định nghĩa Task 1, dùng bởi `toReleaseEntry` (không đổi).
- `parseSemverParts(v) → { parts: number[], pre: string }` và `isNewerOrEqualVersion(incoming, stored) → boolean`: định nghĩa Task 2 Step 4, dùng ở Step 6. Tên khớp spec.
- `LICENSE_ACTIVITY_MIN_WRITE_GAP_MS`: cùng một tên ở harness (Task 2 Step 1), env test (Step 2), khai báo (Step 5), sử dụng (Step 6), Risks (Task 9).
- `syncBroadcastChannel()` và `state.broadcastReleases`: định nghĩa Task 3 Step 1/3, dùng Step 2/4.
- `CheckAndUpdateAsync(IProgress<int>?, CancellationToken, bool suppressPrompt, bool promptBeforeRestart)`: khai báo Task 5 Step 1, gọi với named argument ở Task 6 Step 2 — đúng thứ tự, đúng tên.
- `BroadcastUpdateReceived` là `Action<BroadcastUpdateDirective>` (không `?`): khai báo Task 7 Step 1, `Invoke` Step 2, `+=` Step 3, handler `OnBroadcastUpdateReceived(BroadcastUpdateDirective)` Step 4, `-=` Step 5 — cùng một tên và một chữ ký ở cả 5 chỗ.
- `directive != null` được dùng thay `directive.Active` ở mọi chỗ (Task 7 Step 2 và Step 4).
