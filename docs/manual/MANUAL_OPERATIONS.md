# AutoJMS Manual Operations

Tài liệu vận hành thủ công cho build, release, upload, license, deploy server và rollback AutoJMS.

## 1. Mục đích tài liệu

Tài liệu này dành cho các thao tác nằm ngoài local production code của AutoJMS.

- Dùng trong workflow vibe coding để không quên bước build, upload, release, setup, Supabase, GitHub, Firebase, Render và rollback.
- Ghi rõ file nào được tạo ra, file nào upload lên đâu, lúc nào dùng setup và lúc nào dùng update trong app.
- Không thay thế README kỹ thuật, tài liệu architecture hoặc audit codebase.
- Nếu một path/biến ENV/route chưa khớp hoàn toàn với code hiện tại, tài liệu ghi `NEED VERIFY`.

## 2. Sơ đồ trách nhiệm hệ thống

| Thành phần       | Vai trò                  | Có upload file lớn không  | Ghi chú                     |
| ---------------- | ------------------------ | ------------------------- | --------------------------- |
| Local project    | build/test/dev           | không                     | nơi chạy script             |
| Inno Setup       | cài lần đầu              | tạo setup cuối            | AutoJMS-win-Setup.exe       |
| Velopack         | update trong app         | tạo RELEASES/.nupkg/Setup | binary update               |
| GitHub Releases  | lưu Velopack binary lớn  | có                        | RELEASES, .nupkg, Setup.exe |
| Supabase Storage | manifest/config nhỏ      | không                     | không upload .nupkg         |
| Firebase         | license key/tier/hwid    | không                     | không chứa update URL       |
| Render server    | verify-license/heartbeat | không                     | server.js deploy            |
| User machine     | chạy app                 | không                     | C:\AutoJMS                  |

## 3. Cấu trúc folder local cần biết

```txt
AutoJMS/
├── installer/inno/
│   ├── AutoJMS.iss
│   ├── build-installer.ps1
│   ├── build-installer.bat
│   ├── redist/
│   │   ├── windowsdesktop-runtime-8.0-win-x64.exe
│   │   ├── MicrosoftEdgeWebView2RuntimeInstallerX64.exe
│   │   └── vc_redist.x64.exe
│   └── installer-output/
│       └── AutoJMS-win-Setup.exe
│
├── release/
│   ├── build-release.ps1
│   ├── build-release.bat
│   ├── upload-only.bat
│   └── output/
│       ├── stable/
│       └── beta/
│
├── backend/
├── infra/
├── docs/
├── .agent/
└── src/AutoJMS/AutoJMS.csproj
```

| Folder/file | Dùng để làm gì |
| ----------- | --------------- |
| `installer/inno/` | Build bộ cài lần đầu/reinstall/repair bằng Inno Setup. |
| `installer/inno/redist/` | Chứa runtime prerequisites: .NET 8 Desktop Runtime, WebView2 Evergreen x64, VC++ Redistributable. |
| `installer/inno/installer-output/` | Output setup cuối cho user mới hoặc reinstall. |
| `release/` | Build publish, protect bằng .NET Reactor, hash, đóng gói Velopack, upload GitHub/Supabase. |
| `release/output/stable/` | Output release stable. Trong repo hiện có `AutoJMS-stable-Setup.exe`, `RELEASES-stable`, `assets.stable.json`, `releases.stable.json`. |
| `release/output/beta/` | Output release beta. Nếu chưa có file thì `NEED VERIFY` sau lần build beta đầu tiên. |
| `backend/render-license-server/` | Source server license/heartbeat dùng deploy lên Render. |
| `infra/supabase/` | Cấu trúc mẫu bucket Supabase Storage. |
| `docs/` | Tài liệu architecture, release, troubleshooting, manual. |
| `.agent/` | Agent context, rules, prompts, skills, workflows, checklists. |
| `src/AutoJMS/AutoJMS.csproj` | Project .NET 8 WinForms chính. Không sửa khi chỉ làm manual operations. |

## 4. Cấu trúc cài đặt trên máy user

```txt
C:\AutoJMS/
├── AutoJMS.exe
├── Update.exe
├── current/
├── packages/
└── AppData/
    ├── AutoJMS.json
    ├── logs/
    ├── secure/
    ├── cache/
    ├── modules/
    ├── Downloads/
    └── BrowserData/
```

Quy tắc vận hành:

- Không copy file thủ công vào `C:\AutoJMS\current`.
- `current` do Velopack quản lý.
- User data nằm trong `C:\AutoJMS\AppData`.
- Update không được xóa `AppData`.
- Nếu reinstall, cần bảo toàn hoặc backup `AppData` nếu user yêu cầu.
- Không dùng folder install của user làm nơi test ghi đè binary thủ công.

## 5. Quy tắc version

AutoJMS có 3 giá trị version khác nhau:

| Field | Vai trò | Ví dụ stable | Ví dụ beta |
| ----- | ------- | ------------ | ---------- |
| `VelopackVersion` | Version kỹ thuật cho Velopack update, bắt buộc SemVer | `1.26.6` | `1.26.6-beta.1` |
| `DisplayVersion` | Version hiển thị cho user | `1.26.6` | `1.26.6 beta 1` |
| `InternalBuild` | Version 4 số cho assembly/file diagnostics | `1.26.6.0` | `1.26.6.1` |

Quy tắc bắt buộc:

- Không dùng 4 segment cho `VelopackVersion`, ví dụ `1.26.6.1`.
- Không convert `1.26.6.1` thành `1.26.7`.
- Không dùng leading zero trong SemVer, ví dụ `1.26.05`.
- Stable: `VelopackVersion` và `DisplayVersion` phải cùng line dễ hiểu, ví dụ `1.26.6`.
- Beta: `VelopackVersion` phải có suffix `-beta.n`, `DisplayVersion` phải nói rõ beta của nhánh nào, ví dụ `1.26.6 beta 1`.
- Version mới phải lớn hơn version cũ trong cùng channel, trừ downgrade có xác nhận khi user đổi channel.

Stable:

```txt
VelopackVersion = 1.26.6
DisplayVersion  = 1.26.6
InternalBuild   = 1.26.6.0
```

Beta:

```txt
VelopackVersion = 1.26.6-beta.1
DisplayVersion  = 1.26.6 beta 1
InternalBuild   = 1.26.6.1
```

Beta sequence:

```txt
1.26.6-beta.1
1.26.6-beta.2
1.26.6-beta.3
1.26.6
```

Sau khi stable `1.26.6` live, beta kế tiếp là `1.26.7-beta.1`.

## 6. Build release update bằng Velopack

Workflow:

1. Sửa code.
2. Test local.
3. Chạy:

```bat
cd Release
build-release.bat
```

4. Nhập version.
5. Chọn channel:
   - stable
   - beta
6. Chọn build/upload theo menu script.
7. Script phải thực hiện:
   - `dotnet publish`
   - .NET Reactor protect `AutoJMS.dll`
   - hash `AutoJMS.dll`
   - `vpk pack`
   - tạo `RELEASES`, `.nupkg`, `Setup.exe`
   - upload file lớn lên GitHub Release
   - generate `version-latest.json`
   - generate `hash-manifest.json`
   - upload manifest nhỏ lên Supabase

Ghi chú hiện trạng:

- `release/build-release.ps1` có logic `vpk pack`, GitHub CLI, Supabase upload qua `SUPABASE_SERVICE_ROLE_KEY` hoặc Supabase CLI.
- File setup output hiện thấy trong repo có dạng `AutoJMS-stable-Setup.exe`; tên `AutoJMS-win-Setup.exe` là tên setup chuẩn cần verify theo từng pipeline.

## 7. File Velopack upload lên GitHub Release

| File                                                    | Upload lên đâu       | Bắt buộc | Ghi chú                         |
| ------------------------------------------------------- | -------------------- | -------- | ------------------------------- |
| `RELEASES`                                              | GitHub Release asset | có       | Velopack feed metadata          |
| `AutoJMS-x.x.x-full.nupkg`                              | GitHub Release asset | có       | file lớn, không upload Supabase |
| `AutoJMS-win-Setup.exe` hoặc `AutoJMS-stable-Setup.exe` | GitHub Release asset | có       | Velopack setup/update asset     |

GitHub repo:

```txt
Datt03-sss/AutoJMS-Update
```

Tag format:

```txt
v{VelopackVersion}-Release
```

Ví dụ:

```txt
v1.26.6-Release
v1.26.6-beta.1-Release
```

Beta release phải mark prerelease.

## 8. File manifest upload lên Supabase

| File                            | Upload lên Supabase path                                 | Khi nào upload                    |
| ------------------------------- | -------------------------------------------------------- | --------------------------------- |
| `version-latest.json`           | `autojms-modules/manifest/version-latest.json`           | mỗi lần release                   |
| `hash-manifest.json`            | `autojms-modules/manifest/hash-manifest.json`            | mỗi lần hash AutoJMS.dll đổi      |
| `app-manifest.json`             | `autojms-modules/manifest/app-manifest.json`             | khi đổi schema/top-level manifest |
| `selector-update-manifest.json` | `autojms-modules/manifest/selector-update-manifest.json` | khi đổi selector/runtime config   |
| `tier-definitions.sec`          | `autojms-modules/manifest/tier-definitions.sec`          | khi đổi tier/tabs/forms           |
| `selector.config.sec`           | `autojms-modules/selector-updates/selector.config.sec`   | khi đổi selector                  |
| `runtime-config.sec`            | `autojms-modules/selector-updates/runtime-config.sec`    | khi đổi runtime config            |
| `runtime-config.sig`            | `autojms-modules/selector-updates/runtime-config.sig`    | khi ký lại config                 |
| `public-config.json`            | `autojms-modules/configs/public-config.json`             | khi đổi public config             |

Nhấn mạnh:

- Không upload `.nupkg` lên Supabase.
- Không upload Setup.exe lớn lên Supabase.
- Không upload secret lên Supabase public bucket.
- Production sensitive config dùng `.sec`, dev có thể có `.json`.
- Hiện trạng repo có `infra/supabase/autojms-modules/manifest/tier-definitions.json` và `selector-updates/runtime-config.json`; nếu production chuyển sang `.sec`, cần verify client/server đọc đúng path trước khi release.

## 9. Nội dung mẫu version-latest.json

```json
{
  "schemaVersion": 1,
  "updatedAt": "2026-06-02T00:00:00+07:00",
  "channels": {
    "stable": {
      "version": "1.26.6",
      "displayVersion": "1.26.6",
      "internalBuild": "1.26.6.0",
      "velopackChannel": "stable",
      "provider": "github",
      "githubRepo": "Datt03-sss/AutoJMS-Update",
      "githubRepoUrl": "https://github.com/Datt03-sss/AutoJMS-Update",
      "tag": "v1.26.6-Release",
      "prerelease": false,
      "manualOnly": true,
      "mandatory": false,
      "releaseNotes": "Stable release."
    },
    "beta": {
      "version": "1.26.6-beta.1",
      "displayVersion": "1.26.6 beta 1",
      "internalBuild": "1.26.6.1",
      "velopackChannel": "beta",
      "provider": "github",
      "githubRepo": "Datt03-sss/AutoJMS-Update",
      "githubRepoUrl": "https://github.com/Datt03-sss/AutoJMS-Update",
      "tag": "v1.26.6-beta.1-Release",
      "prerelease": true,
      "manualOnly": true,
      "mandatory": false,
      "releaseNotes": "Beta test release."
    }
  }
}
```

## 10. Nội dung mẫu hash-manifest.json

```json
{
  "schemaVersion": 1,
  "updatedAt": "2026-06-02T00:00:00+07:00",
  "versions": {
    "1.26.6": {
      "displayVersion": "1.26.6",
      "files": {
        "AutoJMS.dll": "SHA256_AFTER_DOTNET_REACTOR"
      }
    }
  }
}
```

Quy tắc hash:

- Hash phải lấy sau khi .NET Reactor protect.
- Hash dùng để xác thực app hợp lệ sau update.
- Không bắt nhập lại key nếu hash mới trùng trusted hash manifest.
- Nếu hash mismatch, không disable license vội; kiểm tra manifest trước.

## 11. Build setup cài lần đầu bằng Inno Setup

Workflow:

1. Build release trước:

```bat
cd Release
build-release.bat
```

2. Build installer sau:

```bat
cd Installer
build-installer.bat
```

3. Output:

```txt
installer/inno/installer-output/AutoJMS-win-Setup.exe
```

4. File này dùng cho:
   - user chưa cài app
   - reinstall
   - repair
   - cài runtime thiếu

5. Inno Setup phải đóng gói:
   - app setup
   - .NET 8 Desktop Runtime
   - WebView2 Evergreen Runtime x64
   - VC++ Redistributable nếu cần

6. Không dùng Inno Setup cho update thường ngày. Update thường ngày dùng tab About.

Ghi chú hiện trạng:

- `installer/inno/build-installer.ps1` tìm Velopack `*Setup.exe` trong release output, rồi tạo setup cuối.
- Output `installer/inno/installer-output/AutoJMS-win-Setup.exe` hiện có trong repo.

## 12. Firebase manual operations

Firebase chỉ chứa license/session/auth/tier data. Không dùng Firebase làm nơi chứa update URL hoặc binary.

Mẫu key:

```json
{
  "createdAt": "26-05-2026 01:22",
  "status": "active",
  "tier": "ULTRA",
  "hwid": "",
  "middleCode": "214A02",
  "skipHashCheck": true,
  "modulePolicy": {
    "autoUpdate": false,
    "silentUpdate": false,
    "applyOnNextStartup": true
  }
}
```

Thao tác thủ công:

- Đổi gói user: sửa `tier`.
- Reset máy: xóa `hwid`.
- Khóa key: `status = disabled`.
- Dev key có thể `skipHashCheck = true`.
- Không để update URL trong Firebase license.
- Không config tabs/forms riêng từng key nếu tier-definitions đã quản lý.
- BASE chỉ có HOME, DKCH, TRACKING, PRINT, ABOUT.
- ULTRA thêm FullStackOperationForm và background sync theo tier policy.

## 13. Render server manual operations

Render server chạy:

```txt
backend/render-license-server/server.js
```

API chính:

- `/api/verify-license`
- `/api/heartbeat`
- `/api/health`

`NEED VERIFY`: code hiện tại trong `server.js` có route health dạng `/health`; nếu muốn dùng `/api/health` thì cần đồng bộ server/client/deploy.

ENV cần có:

| ENV | Mục đích | Ghi chú |
| --- | -------- | ------- |
| `JWT_PRIVATE_KEY` | Ký license JWT RS256 | Không commit secret. |
| `JWT_PUBLIC_KEY` | Client/server verify JWT | Không log raw key. |
| `VALID_EXE_HASHES` | Danh sách hash hợp lệ nếu server còn kiểm tra hash | Có thể bỏ qua cho dev key `skipHashCheck = true`. |
| `SUPABASE_PUBLIC_BASE_URL` | Base URL public nếu server trả manifest URL | `NEED VERIFY`: code hiện tại đọc `SUPABASE_BASE_URL`. |
| `SUPABASE_BASE_URL` | Base URL Supabase manifest theo code hiện tại | Đang dùng trong `server.js`. |

Khi sửa `server.js`:

1. Commit/push hoặc deploy lên Render.
2. Kiểm tra health endpoint.
3. Test `/api/verify-license`.
4. Test `/api/heartbeat`.
5. Kiểm tra client parse đúng `tier`, `modulePolicy`, `supabase.baseUrl`, `supabase.manifests`.
6. Không deploy `serviceAccountKey.json` vào repo public; cấu hình secret qua môi trường/deploy secret phù hợp.

## 14. GitHub Release manual operations

Login GitHub CLI:

```powershell
gh auth login
```

Tạo stable release:

```powershell
gh release create v1.26.6-Release --repo Datt03-sss/AutoJMS-Update --title "AutoJMS 1.26.6 Stable" --notes "Stable release"
gh release upload v1.26.6-Release RELEASES AutoJMS-1.26.6-stable-full.nupkg AutoJMS-win-Setup.exe --repo Datt03-sss/AutoJMS-Update --clobber
```

Tạo beta release:

```powershell
gh release create v1.26.6-beta.1-Release --repo Datt03-sss/AutoJMS-Update --title "AutoJMS 1.26.6 Beta 1" --notes "Beta test release" --prerelease
```

Quy tắc:

- Stable release không mark prerelease.
- Beta release phải mark prerelease.
- Nếu release đã tồn tại, dùng `gh release upload ... --clobber` cho asset hoặc bump version nếu release đã public cho user.
- Không mở GitHub page trong app update; app phải tải qua Velopack feed.

## 15. Supabase manual operations

Upload qua Dashboard:

```txt
Storage
→ bucket autojms-modules
→ manifest/
→ upload version-latest.json, hash-manifest.json
```

Upload qua CLI nếu đã login/link project:

```powershell
supabase storage cp .\version-latest.json ss:///autojms-modules/manifest/version-latest.json --linked --experimental
supabase storage cp .\hash-manifest.json ss:///autojms-modules/manifest/hash-manifest.json --linked --experimental
```

Hoặc upload qua REST nếu có service role key trong môi trường:

```powershell
$env:SUPABASE_SERVICE_ROLE_KEY = "<set outside repo>"
```

Nhấn mạnh:

- Supabase free plan không upload file Velopack lớn.
- Supabase chỉ là control plane: manifest, config, hash, selector-update.
- GitHub Release là binary hosting.
- Không đưa `.nupkg`, `Setup.exe`, private key, service account, token vào Supabase public bucket.

## 16. Checklist release stable

```txt
[ ] Code đã test local
[ ] Version đã tăng
[ ] Build Debug không lỗi nếu có thể
[ ] Build Release thành công
[ ] .NET Reactor protect thành công
[ ] Hash AutoJMS.dll đã ghi vào hash-manifest.json
[ ] vpk pack thành công
[ ] GitHub Release có RELEASES
[ ] GitHub Release có .nupkg
[ ] GitHub Release có Setup.exe
[ ] Supabase version-latest.json đã cập nhật
[ ] Supabase hash-manifest.json đã cập nhật
[ ] Client About → Check Update test OK
[ ] App restart OK
[ ] Không mất C:\AutoJMS\AppData
```

## 17. Checklist beta

```txt
[ ] Channel beta
[ ] Version dạng x.y.z-beta.n
[ ] GitHub release mark prerelease
[ ] version-latest.json beta.prerelease = true
[ ] Stable không nhận beta
[ ] Beta client nhận beta
```

## 18. Rollback manual

Nếu release lỗi nhưng user chưa update nhiều:

- Update `version-latest.json` về version cũ.
- Upload lại Supabase manifest.
- Test About → Check Update trên client.

Nếu GitHub release lỗi:

- Xóa hoặc unpublish release lỗi.
- Restore release tag cũ nếu cần.
- Nếu asset upload sai nhưng tag đúng, upload lại asset bằng `--clobber` chỉ khi chưa ảnh hưởng user.

Nếu hash manifest sai:

- Lấy lại hash `AutoJMS.dll` đúng sau .NET Reactor.
- Update `hash-manifest.json`.
- Upload lại Supabase.
- Test verify license/update flow.

Nếu `server.js` lỗi:

- Rollback Render deploy.
- Kiểm tra health endpoint.
- Test `/api/verify-license` và `/api/heartbeat`.

Nếu Firebase key sai:

- Sửa `tier`, `hwid`, `status`, `skipHashCheck` về trạng thái đúng.
- Không đổi update URL trong Firebase vì update không thuộc Firebase.

## 19. Bảng không được làm

```txt
KHÔNG ĐƯỢC:
- Không upload .nupkg lên Supabase.
- Không mở GitHub page trong app update.
- Không copy file thủ công vào C:\AutoJMS\current.
- Không xóa C:\AutoJMS\AppData khi update.
- Không dùng version `1.26.05` hoặc `1.26.6.1` làm VelopackVersion.
- Không phát hành kiểu `VelopackVersion=1.26.7` nhưng `DisplayVersion=1.26.6.1`; beta phải là `1.26.6-beta.1` / `1.26.6 beta 1`.
- Không để secret/serviceAccountKey.json trong repo.
- Không log full token production.
- Không sửa Firebase từng key để bật/tắt tab nếu tier-definitions đã quản lý.
```

## 20. Troubleshooting

| Lỗi | Triệu chứng | Nguyên nhân | Cách xử lý |
| --- | ----------- | ----------- | ---------- |
| Supabase upload fail vì `>50MB` | Upload `.nupkg` hoặc `Setup.exe` lên Supabase lỗi size limit | Supabase Storage free plan không phù hợp cho binary lớn | Upload binary lên GitHub Release; Supabase chỉ giữ manifest/config/hash nhỏ. |
| Velopack SemVer invalid leading zero | `vpk pack` báo version không hợp lệ với `1.26.05` | SemVer không cho leading zero | Dùng `1.26.5` hoặc bump thành `1.26.6`; beta dùng `1.26.6-beta.1`. |
| Velopack unexpected character after patch | `vpk pack` báo lỗi parse version sau patch | Version có 4 segment như `1.26.6.1` hoặc suffix sai SemVer | Đổi VelopackVersion thành `1.26.6` hoặc `1.26.6-beta.1`; dùng `InternalBuild=1.26.6.1` nếu cần 4 số. |
| GitHub CLI chưa login | `gh release create/upload` lỗi auth | Chưa chạy `gh auth login` hoặc token hết hạn | Chạy `gh auth login`, chọn đúng account có quyền repo `Datt03-sss/AutoJMS-Update`. |
| Release already exists | `gh release create` báo tag/release tồn tại | Tag version đã được tạo | Nếu asset sai và chưa public rộng, upload lại bằng `--clobber`; nếu đã public, bump version mới. |
| App không thấy update | About → Check Update báo không có update | `version-latest.json` chưa upload, tag sai, channel sai, version không lớn hơn, hoặc GitHub asset thiếu `RELEASES` | Kiểm tra Supabase manifest, tag `v{VelopackVersion}-Release`, GitHub assets, channel stable/beta và version hiện tại của client. |
| Hash mismatch sau update | App update xong yêu cầu verify lại hoặc báo hash không hợp lệ | Hash lấy trước .NET Reactor, hash-manifest chưa cập nhật hoặc server `VALID_EXE_HASHES` thiếu hash mới | Lấy hash sau Reactor, cập nhật `hash-manifest.json`, cập nhật `VALID_EXE_HASHES` nếu server còn dùng. |
| User bị yêu cầu nhập key lại | User sau update phải nhập lại license | AppData bị xóa, hash mismatch, secure store lỗi hoặc reinstall không giữ data | Kiểm tra `C:\AutoJMS\AppData`, logs, hash manifest; khi reinstall phải backup/giữ AppData. |
| Setup repair fail vì app/WebView2 còn chạy | Inno Setup repair/reinstall lỗi file đang bị khóa | `AutoJMS.exe`, `Update.exe` hoặc WebView2 process còn chạy | Thoát app, kill process liên quan nếu cần, chạy lại setup bằng quyền phù hợp. |
| Thiếu WebView2 Runtime | App mở WebView2 lỗi hoặc browser control không load | Máy user chưa có WebView2 Evergreen Runtime x64 | Chạy Inno Setup có bundled runtime hoặc cài `MicrosoftEdgeWebView2RuntimeInstallerX64.exe`. |
| Thiếu .NET 8 Desktop Runtime | App không chạy, báo thiếu framework `Microsoft.WindowsDesktop.App` | Máy user chưa có .NET 8 Desktop Runtime | Chạy Inno Setup có bundled runtime hoặc cài `windowsdesktop-runtime-8.0-win-x64.exe`. |


