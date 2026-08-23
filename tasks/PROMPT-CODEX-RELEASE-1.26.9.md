# Prompt cho Codex — Build & publish release đầy đủ (stable + beta), overwrite 1.26.9

> Copy toàn bộ phần dưới dấu `---` và dán cho Codex.

---

Bạn đang làm việc trên repo AutoJMS tại `D:\v1.2605.2(new-test)` (branch `main`, remote
`https://github.com/Datt03-sss/AutoJMS`). Đọc `AGENTS.md` trước khi bắt đầu và tuân thủ nó, **trừ**
các điểm được Owner cho phép tường minh ngay dưới đây.

## 0. Uỷ quyền tường minh từ Owner (bắt buộc đọc)

`AGENTS.md` có 3 điều cấm liên quan trực tiếp tới task này. Owner **cho phép tường minh** cho đúng
task này, không mở rộng sang việc khác:

1. *"No release: Do not build, sign, or upload a production release artifact."*
   → **Được phép.** Task này chính là build + upload production release cho cả 2 channel.
2. *"No Velopack production changes: Do not touch `VelopackUpdateService.cs` hoặc
   `release/build-release.ps1`"* và danh sách **Protected Files**.
   → Hai file này **đã được Owner sửa sẵn** trong working tree. **KHÔNG revert, KHÔNG refactor,
   KHÔNG "sửa lại cho đẹp".** Chỉ đọc để hiểu, rồi build.
3. *"Bump version number: ❌ unless owner requests."*
   → **KHÔNG bump.** Owner yêu cầu **dùng lại đúng version 1.26.9** và ghi đè release cũ.

## 1. Bối cảnh: vì sao phải publish lại 1.26.9

Velopack 1.2.0 client đọc release feed từ GitHub Release asset tên **đúng** là
`releases.{channel}.json` (`Velopack.CoreUtil.GetVeloReleaseIndexName` →
`GithubSource` → `GitBase.GetReleaseFeed`). File text `RELEASES` là index legacy Squirrel,
Velopack 1.x **không đọc**.

`release/build-release.ps1` trước đây chỉ upload `RELEASES` + `.nupkg` + `Setup.exe`, bỏ sót
`releases.{channel}.json`. Hệ quả: feed rỗng → `CheckForUpdatesAsync()` trả `null` → app báo
*"Bạn đang dùng phiên bản mới nhất."* dù đã publish bản mới. **Cả stable và beta đều bị.**

Hai release đã publish đang thiếu index (đã kiểm tra bằng `gh release view`):

| Tag | Asset hiện có | Thiếu |
|---|---|---|
| `v1.26.9-Release` | `AutoJMS-1.26.9-full.nupkg`, `AutoJMS-Installer-1.26.9.exe`, `AutoJMS-win-Setup.exe`, `RELEASES` | `releases.stable.json` |
| `v1.26.9-beta.1-Release` | `AutoJMS-1.26.9-beta.1-full.nupkg`, `AutoJMS-Installer-1.26.9-beta.1.exe`, `AutoJMS-win-Setup.exe`, `RELEASES` | `releases.beta.json` |

Script đã được sửa. Task của bạn là **build lại và publish đè lên đúng 2 tag đó**, để asset khớp
với code hiện tại trong tree.

> **Owner đã biết và chấp nhận giới hạn của việc ghi đè:** máy nào đã cài sẵn `1.26.9` /
> `1.26.9-beta.1` bằng Inno installer sẽ **không** nhận được bản mới qua auto-update, vì Velopack
> so version theo SemVer và `1.26.9` không lớn hơn `1.26.9` (log sẽ ghi
> `NO_UPDATE_BECAUSE_LATEST_VERSION_NOT_GREATER`). Những máy đó phải cài lại bằng installer.
> Máy đang ở `1.26.7` hoặc thấp hơn thì update bình thường. **Đây là quyết định của Owner, không
> phải lỗi — đừng tự ý bump version để "sửa".**

## 2. Thay đổi đã có sẵn trong working tree — không được revert

```
M docs/manual/MANUAL_OPERATIONS.md          <- docs: RELEASES không phải Velopack index
M docs/manual/QUICK_RELEASE_CHECKLIST.md    <- docs: checklist asset
M release/build-release.ps1                 <- FIX CHÍNH
M src/AutoJMS/Forms/UpdateChannelDialog.cs  <- so sánh version
M src/AutoJMS/Updates/VelopackUpdateService.cs <- chẩn đoán + so sánh version
?? release/repair-release-index.ps1         <- script vá release đã publish (KHÔNG cần chạy)
```

Nội dung fix trong `build-release.ps1` (hàm `Rename-VelopackAssets`):

- tìm `releases.{channel}.json`, đưa vào danh sách asset upload;
- rewrite field `FileName` bên trong index cho khớp tên `.nupkg` sau khi rename
  (`AutoJMS-1.26.9-beta.1-beta-full.nupkg` → `AutoJMS-1.26.9-beta.1-full.nupkg`), nếu không app
  sẽ thấy update rồi **404 khi tải**;
- thêm `Assert-VelopackReleaseIndex` — **fail build** nếu index không khớp tên/version asset.

**Nếu `Assert-VelopackReleaseIndex` throw: đó là guard đang làm việc. Dừng lại, báo Owner, tuyệt
đối không bypass, không comment out, không `-Force`.**

## 3. HAI file dirty KHÔNG phải của task này

```
M src/AutoJMS/Automation/Tab2Config.cs
M src/AutoJMS/Forms/FullStackOperation.Dashboard.cs
```

Đây là việc đang làm dở của agent khác. **Không sửa, không revert, không commit chúng.**
**Tuyệt đối không `git add .`** — chỉ `git add` đúng từng file bạn được phép commit.
(Điểm này ghi đè mục "Commit & Push" trong `AGENTS.md` — mục đó viết `git add .` là sai với
tree đang dirty như hiện tại.)

## 4. Pre-flight

```powershell
cd D:\v1.2605.2(new-test)

# 4.1 Lock (AGENTS.md single-writer)
type .agent-lock.md
# Nếu Current Writer khác None và khác bạn -> DỪNG, báo Owner.
# Nếu None -> set Current Writer: Codex / Mode: WRITE_ACTIVE /
#   Scope: release/build-release.ps1 (read-only), release build+publish 1.26.9

# 4.2 KHÔNG pull, KHÔNG stash, KHÔNG reset — tree đang có thay đổi cần build
git status
git log --oneline -3

# 4.3 Toolchain — thiếu bất kỳ cái nào thì DỪNG và báo, đừng tự cài
dotnet --version
vpk --version
gh --version ; gh auth status
docker compose version
Test-Path "D:\Cshap\.NET Reactor\dotNET_Reactor.Console.exe"
Test-Path ".\installer\inno\build-installer.ps1"

# 4.4 Secret cho DataHub manifest upload
#   Cần DATAHUB_ADMIN_TOKEN (env), cấp riêng theo môi trường VPS.
#   KHÔNG in giá trị token ra log. KHÔNG commit file .env.
```

### 4.5 CỔNG CHẶN: `VALID_EXE_HASHES` trên license server — kiểm tra TRƯỚC KHI publish

Đây là rủi ro lớn nhất của lần release này, lớn hơn cả lỗi update. Đọc kỹ.

Client gửi `exeHash = SHA256(AutoJMS.dll)` khi validate license
(`src/AutoJMS/Licensing/LicenseApiService.cs:100` và `:349`, giá trị từ
`Program.ExecutableHash` ← `HashVerifier.ComputeDllHash()`).

Phía server, `backend/render-license-server/server.js:486-510`:

```js
if (!skipHashCheck) {
    const validHashesStr = process.env.VALID_EXE_HASHES || "";
    if (validHashesStr.trim() !== "") {
        // ... nếu localHash không nằm trong danh sách:
        return res.status(403).json({ success:false, error:"HASH_INVALID", ... });
    }
}
```

Nghĩa là: **nếu env `VALID_EXE_HASHES` trên Render đang được set (không rỗng), và không chứa hash
`AutoJMS.dll` của bản build mới, thì bản vừa publish sẽ KHÔNG activate được license — user nhận
HTTP 403 `HASH_INVALID`.** Code thay đổi + .NET Reactor ⇒ hash mới chắc chắn khác hash cũ.

**Trước khi chạy bất kỳ lệnh `-Upload` nào, phải xác định trạng thái env này.** Bạn không có quyền
đọc env production — hãy hỏi Owner một câu duy nhất:

> "Env `VALID_EXE_HASHES` trên Render license server hiện đang rỗng hay đang set danh sách hash?"

- **Rỗng / không set** → server bỏ qua hash check. Publish bình thường, không cần làm gì thêm.
- **Đang set** → **DỪNG, không publish.** Báo Owner rằng cần thêm hash mới vào env trước, và cung
  cấp hash ngay khi build xong (xem dưới). Có thể chạy build **không** `-Upload` để lấy hash trước.

Sau **mỗi** lần build, lấy hash `AutoJMS.dll` mới (đã tính sau Reactor) và **in ra rõ ràng** —
đây là số Owner cần dán vào `VALID_EXE_HASHES`:

```powershell
# hash do chính script ghi ra, đã là hash sau Reactor
Get-Content .\release\output\beta\hash-manifest.json   -Raw
Get-Content .\release\output\stable\hash-manifest.json -Raw

# đối chiếu trực tiếp với DLL đã publish
(Get-FileHash .\artifacts\publish\win-x64\AutoJMS.dll -Algorithm SHA256).Hash.ToLower()
```

Ghi cả 2 hash (stable + beta) vào báo cáo cuối, mục **Risks**, dạng copy-paste được.

Lưu ý phụ (không phải blocker): `hash-manifest.json` trên DataHub VPS sẽ bị ghi hash mới cho key
`1.26.9` / `1.26.9.0`. Máy nào đang chạy bản 1.26.9 **cũ** sẽ log
`INTEGRITY FAILURE: local AutoJMS.dll hash does not match manifest`. Kiểm tra
`src/AutoJMS/Program.cs:310-324` cho thấy check này **non-blocking, chỉ ghi log**, không chặn app,
không đòi nhập lại license. Không cần xử lý, nhưng nêu trong mục Risks.

## 5. Build compile trước — không được publish nếu fail

```powershell
dotnet restore .\AutoJMS.slnx
dotnet build .\AutoJMS.slnx -c Release
```

Bắt buộc **0 error**. Nếu fail: **DỪNG**, không chạy `build-release.ps1`, dán nguyên văn lỗi
compile vào báo cáo. Không tự ý sửa 5 file ở mục 2 để cho qua build — báo Owner.

Nếu có `.\eng\harness\verify.ps1` thì chạy tiếp:

```powershell
powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
```

## 6. Release — BETA TRƯỚC, STABLE SAU

**Thứ tự này bắt buộc**, vì `update.xml`, `version-latest.json` và `hash-manifest.json` đều được
script **merge** với bản đang publish (`Read-ExistingUpdateXml` / `-ExistingManifest`). Chạy beta
trước rồi stable sau thì lần chạy cuối cùng đọc được channel beta vừa publish, và manifest cuối
cùng do stable ghi — đúng thứ tự ưu tiên.

### 6.0 Soạn release notes từ git log

Bạn tự soạn notes, **không** dùng notes cũ trong `update.xml`. Lấy nguyên liệu:

```powershell
git log --oneline v1.26.8-Release..HEAD 2>$null
# nếu tag local không có:
git log --oneline -40
git diff --stat HEAD
```

Yêu cầu notes:

- **Tiếng Việt**, 3–6 bullet, mỗi bullet là một thay đổi người dùng cảm nhận được.
- Bullet đầu tiên **phải** là fix auto-update, vì đó là lý do publish lại:
  *"Sửa lỗi không nhận được bản cập nhật — GitHub Release trước đây thiếu file index
  `releases.{channel}.json` mà Velopack cần đọc."*
- Có bullet cho fix so sánh version trong dialog "Chọn kênh cập nhật".
- Gộp các commit lặt vặt (refactor, format, typo) thành một bullet chung hoặc bỏ. Không liệt kê
  commit hash. Không đưa tên file code vào notes.
- Notes beta thêm dòng cuối: *"Bản beta, có thể chưa ổn định."*

Ghi ra file rồi truyền bằng `-ReleaseNotesFile` — đừng nhồi tiếng Việt có dấu vào `-ReleaseNotes`
trên command line, PowerShell rất dễ làm hỏng encoding.

**Bắt buộc ghi UTF-8 CÓ BOM.** Lý do: `build-release.ps1:1037` đọc file bằng
`Get-Content -Path $ReleaseNotesFile -Raw` **không truyền `-Encoding`**. Trên PowerShell 5.1,
`Get-Content` mặc định dùng ANSI code page, nên file UTF-8 **không BOM** sẽ bị đọc sai và notes ra
mojibake (`Sá»­a lá»—i...`). Có BOM thì PS 5.1 tự nhận đúng, và PS 7 cũng đọc đúng. Dùng .NET API để
chắc chắn đúng trên cả hai:

```powershell
New-Item -ItemType Directory -Force -Path .\release\notes | Out-Null
$utf8Bom = New-Object System.Text.UTF8Encoding($true)   # $true = emit BOM

$betaNotes = @"
<notes beta bạn soạn ở đây>
"@
$stableNotes = @"
<notes stable bạn soạn ở đây>
"@

[System.IO.File]::WriteAllText((Join-Path $PWD "release\notes\1.26.9-beta.1.md"), $betaNotes, $utf8Bom)
[System.IO.File]::WriteAllText((Join-Path $PWD "release\notes\1.26.9.md"),        $stableNotes, $utf8Bom)
```

Sau khi tạo, **đọc lại và in ra để mắt thường xác nhận dấu tiếng Việt còn nguyên**:

```powershell
Get-Content .\release\notes\1.26.9-beta.1.md -Raw -Encoding UTF8
Get-Content .\release\notes\1.26.9.md        -Raw -Encoding UTF8
```

Nếu thấy ký tự lạ ⇒ **DỪNG**, sửa encoding, đừng publish. Không commit thư mục `release/notes/`.

### 6.1 Beta

```powershell
.\release\build-release.ps1 -Version "1.26.9-beta.1" -Channel beta -Upload `
  -ReleaseNotesFile ".\release\notes\1.26.9-beta.1.md"
```

### 6.2 Stable

```powershell
.\release\build-release.ps1 -Version "1.26.9" -Channel stable -Upload `
  -ReleaseNotesFile ".\release\notes\1.26.9.md"
```

### Quy tắc tham số — đọc kỹ, dễ sai

- **KHÔNG dùng `-SkipPublish`** cho lần chạy thứ hai. Cả 2 channel dùng chung
  `artifacts\publish\win-x64`, và script bake `-p:InformationalVersion=$DisplayVersion` vào
  assembly ở bước publish. Bỏ qua publish ở lần 2 ⇒ binary mang version của channel trước ⇒
  `AppVersion.Current` sai ⇒ client so sánh version sai.
- **KHÔNG dùng `-SkipReactor`.** Hash trong `hash-manifest.json` phải là hash của `AutoJMS.dll`
  **sau** .NET Reactor. Bỏ Reactor ⇒ hash lệch ⇒ user bị đòi nhập lại license sau update.
- **KHÔNG truyền `-DisplayVersion` dạng 4 số** (`1.26.9.1`). Để script dùng default
  (`1.26.9` cho stable, `1.26.9 beta 1` cho beta). Lý do: `AppVersion.Current` trả thẳng
  `InformationalVersion`, và một chuỗi 4 số là **nhập nhằng** — không phân biệt được
  "revision 1" với "beta 1". Giữ nhãn `beta` trong DisplayVersion thì dialog so sánh đúng.
- **KHÔNG đổi `-Version`.** Đúng `1.26.9` và `1.26.9-beta.1`. Tag `v{Version}-Release` đã tồn tại;
  `Publish-GitHubRelease` tự phát hiện và dùng `gh release upload --clobber` + `gh release edit`
  để set lại cờ prerelease. Đây là hành vi mong muốn.
  - Nếu cần chạy thử trước khi publish: bỏ `-Upload` (build local, không upload), hoặc thêm
  `-SkipDataHubManifestUpload` nếu DataHub chưa sẵn sàng — nhưng **phải báo Owner** là manifest
  chưa lên.

## 7. Verify sau publish — đây là phần quan trọng nhất

Không được kết luận "xong" nếu chưa chạy hết mục này và dán output vào báo cáo.

```powershell
# 7.1 Asset trên GitHub Release — BẮT BUỘC có releases.{channel}.json
gh release view v1.26.9-Release          --repo Datt03-sss/AutoJMS-Update --json assets,isPrerelease `
  | ConvertFrom-Json | % { $_.isPrerelease; $_.assets.name }
gh release view v1.26.9-beta.1-Release   --repo Datt03-sss/AutoJMS-Update --json assets,isPrerelease `
  | ConvertFrom-Json | % { $_.isPrerelease; $_.assets.name }
```

Kỳ vọng:

| Tag | Phải có | isPrerelease |
|---|---|---|
| `v1.26.9-Release` | `releases.stable.json`, `RELEASES`, `AutoJMS-1.26.9-full.nupkg`, `AutoJMS-win-Setup.exe`, `AutoJMS-Installer-1.26.9.exe` | `false` |
| `v1.26.9-beta.1-Release` | `releases.beta.json`, `RELEASES`, `AutoJMS-1.26.9-beta.1-full.nupkg`, `AutoJMS-win-Setup.exe`, `AutoJMS-Installer-1.26.9-beta.1.exe` | `true` |

```powershell
# 7.2 FileName trong index phải TRÙNG tên asset đã upload, và SHA1/Size trùng file local
Get-Content .\release\output\stable\releases.stable.json -Raw
Get-Content .\release\output\beta\releases.beta.json -Raw
(Get-FileHash .\release\output\stable\AutoJMS-1.26.9-full.nupkg        -Algorithm SHA1).Hash
(Get-FileHash .\release\output\beta\AutoJMS-1.26.9-beta.1-full.nupkg   -Algorithm SHA1).Hash
```

`FileName` **không được** còn chứa `-stable-full` / `-beta-full`. Nếu còn ⇒ update sẽ 404 khi tải
⇒ DỪNG và báo.

```powershell
# 7.3 Tải index từ GitHub về (đúng thứ client sẽ đọc) và so với bản local
gh release download v1.26.9-Release        --repo Datt03-sss/AutoJMS-Update -p releases.stable.json -D .\_verify\stable --clobber
gh release download v1.26.9-beta.1-Release --repo Datt03-sss/AutoJMS-Update -p releases.beta.json   -D .\_verify\beta   --clobber
Get-Content .\_verify\stable\releases.stable.json -Raw
Get-Content .\_verify\beta\releases.beta.json -Raw
# Xoá thư mục _verify sau khi xem, đừng commit nó
```

```powershell
# 7.4 update.xml trên raw phải có ĐỦ CẢ 2 channel, đúng version, đúng tag
Invoke-RestMethod "https://raw.githubusercontent.com/Datt03-sss/AutoJMS-Update/main/update.xml"
```

Kỳ vọng: `stable` → `velopackVersion 1.26.9`, `releaseTag v1.26.9-Release`, `prerelease="false"`;
`beta` → `velopackVersion 1.26.9-beta.1`, `releaseTag v1.26.9-beta.1-Release`, `prerelease="true"`.
**Nếu mất một channel ⇒ merge lỗi ⇒ DỪNG và báo, đừng publish tiếp.**

Kiểm tra thêm phần `<releaseNotes>` của **cả 2 channel**: phải là notes tiếng Việt bạn vừa soạn,
dấu còn nguyên, **không** phải `AutoJMS stable release.` / `Bản thử nghiệm nội bộ.` (đó là default
khi notes rỗng) và không phải mojibake. Đối chiếu với notes trên GitHub Release:

```powershell
gh release view v1.26.9-Release        --repo Datt03-sss/AutoJMS-Update --json body | ConvertFrom-Json | % body
gh release view v1.26.9-beta.1-Release --repo Datt03-sss/AutoJMS-Update --json body | ConvertFrom-Json | % body
```

```powershell
# 7.5 DataHub manifest
Invoke-RestMethod "https://datahub.example.com/manifest/version-latest.json"
Invoke-RestMethod "https://datahub.example.com/manifest/hash-manifest.json"
```

`hash-manifest.json` phải có entry cho **cả** `1.26.9` và `1.26.9-beta.1` (script merge theo key
`VelopackVersion` / `DisplayVersion` / `AssemblyFileVersion`; stable → `1.26.9.0`,
beta → `1.26.9.1`, nên không đè nhau).

### 7.6 Smoke test bằng client thật — Owner làm, bạn hướng dẫn

Cài `AutoJMS-Installer-1.26.9.exe` (hoặc dùng máy đang chạy 1.26.7), rồi:

1. Tab ABOUT → Check Update → dialog "Chọn kênh cập nhật" phải hiện **cả 2 nút bật**.
2. Bấm **Cập nhật Beta** → phải hiện "Có bản cập nhật mới: v1.26.9-beta.1", **không** phải
   "Bạn đang dùng phiên bản mới nhất."
3. Tải xong, app restart, ABOUT hiển thị `1.26.9 beta 1`.
4. Lặp lại với **Cập nhật Stable** → `1.26.9`.
5. Nếu vẫn báo "đã mới nhất": mở `C:\AutoJMS\AppData\logs\debug.log`, tìm dòng
   `[Update] no update reason=`.
   - `NO_UPDATE_BECAUSE_VELOPACK_INDEX_MISSING` → index chưa lên GitHub.
   - `NO_UPDATE_BECAUSE_VELOPACK_INDEX_MISSING_ONLY_LEGACY_RELEASES` → chỉ có `RELEASES`, thiếu
     `releases.{channel}.json`.
   - `NO_UPDATE_BECAUSE_LATEST_VERSION_NOT_GREATER` → version không lớn hơn bản đang chạy.
   Dán các dòng `[Update] ...` vào báo cáo.

## 8. Commit & push — chỉ khi mục 5, 6, 7 đều pass

```powershell
git add release/build-release.ps1 release/repair-release-index.ps1 `
        src/AutoJMS/Updates/VelopackUpdateService.cs `
        src/AutoJMS/Forms/UpdateChannelDialog.cs `
        docs/manual/MANUAL_OPERATIONS.md docs/manual/QUICK_RELEASE_CHECKLIST.md
git status          # xác nhận Tab2Config.cs và FullStackOperation.Dashboard.cs KHÔNG được staged
git commit -m "fix(update): upload Velopack releases.{channel}.json + fix version comparers; release 1.26.9 stable/beta"
git push origin main
git log --oneline -1
```

Không commit: `release/output/**`, `release/notes/**`, `artifacts/**`, `_verify/**`, `*.log`, `.env*`.
Kiểm tra `.gitignore` đã chặn chúng chưa; nếu chưa thì **đừng** stage, và báo Owner.

Xong thì reset lock: `Current Writer: None`, `Mode: READ_ONLY`, `Scope: None`.

## 9. Điều kiện DỪNG (không tự quyết, báo Owner)

- **`VALID_EXE_HASHES` trên Render đang được set** và chưa chứa hash `AutoJMS.dll` mới (mục 4.5).
  Đây là điều kiện dừng ưu tiên số 1 — publish trong tình trạng này làm user không activate được.
- `dotnet build` fail.
- `Assert-VelopackReleaseIndex` throw.
- `gh auth status` fail hoặc không có quyền `Datt03-sss/AutoJMS-Update`.
- `update.xml` sau publish mất một channel.
- `releases.{channel}.json` không xuất hiện trên GitHub Release sau khi upload.
- Release notes bị mojibake, hoặc rơi về default `AutoJMS stable release.` /
  `Bản thử nghiệm nội bộ.` (nghĩa là `-ReleaseNotesFile` không được đọc).
- DataHub manifest upload fail (thiếu `DATAHUB_ADMIN_TOKEN`).
- Bất kỳ lúc nào bạn thấy cần sửa 1 trong 5 file ở mục 2 để làm task chạy được.

## 10. Báo cáo cuối — theo đúng "Required Final Report Format" của `AGENTS.md`

10 mục: Summary / Files Changed / Build+Verify Result / Commit Message / Commit Hash / Pushed To /
Behavior Changed / Behavior Intentionally Unchanged / Owner Manual Test Checklist / Risks.

Thêm 3 mục riêng cho release này:

11. **Release Assets** — bảng asset thực tế của cả 2 tag (paste output mục 7.1), nêu rõ
    `releases.{channel}.json` đã có.
12. **Feed Verification** — nội dung `releases.stable.json` / `releases.beta.json` tải từ GitHub,
    kèm SHA1 file `.nupkg` local để đối chiếu, và `update.xml` sau publish.
13. **Rollback Plan** — nếu client update xong bị lỗi: `gh release edit` / re-upload asset của tag
    nào, và cách trả `update.xml` về `1.26.8` (giá trị stable trước đó — xem
    `release/output/beta/update.xml` trong tree, nó còn giữ snapshot stable 1.26.8).
14. **Hash mới cho `VALID_EXE_HASHES`** — 2 dòng SHA256 (stable + beta) dạng copy-paste được, kèm
    trạng thái env mà Owner đã xác nhận ở mục 4.5.
