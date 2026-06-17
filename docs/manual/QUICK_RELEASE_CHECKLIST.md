# AutoJMS Quick Release Checklist

Bản ngắn dùng mỗi lần build/release. Nếu có lỗi không rõ nguyên nhân, đọc bản đầy đủ: `docs/manual/MANUAL_OPERATIONS.md`.

## 1. Chọn loại release

| Loại | Khi dùng | Version | GitHub Release |
| ---- | -------- | ------- | -------------- |
| Stable | Phát hành cho user chính | `1.26.6` | Không mark prerelease |
| Beta | Test nội bộ/beta client | `1.26.6-beta.1` | Mark prerelease |
| Setup/Inno | User cài lần đầu, reinstall, repair, thiếu runtime | Dùng sau khi build release | Output installer riêng |

Version rule:

- Stable: `VelopackVersion=1.26.6`, `DisplayVersion=1.26.6`, `InternalBuild=1.26.6.0`
- Beta: `VelopackVersion=1.26.6-beta.1`, `DisplayVersion=1.26.6 beta 1`, `InternalBuild=1.26.6.1`
- VelopackVersion sai: `1.26.05`, `1.26.6.1`, hoặc `1.26.7` nhưng DisplayVersion lại là `1.26.6.1`
- Không convert `1.26.6.1` thành `1.26.7`; 4 số chỉ dùng cho `InternalBuild`.
- Version mới phải lớn hơn version đang phát hành.

## 2. Checklist trước khi build

```txt
[ ] Code đã test local
[ ] Không sửa nhầm production config/secret
[ ] Không log full token production
[ ] BASE không có background inventory/database sync
[ ] ULTRA FullStackOperation vẫn là form riêng, không phải tab
[ ] Version đã bump đúng SemVer, không dùng 4 segment làm VelopackVersion
[ ] GitHub CLI đã login: gh auth status
[ ] Có quyền repo: Datt03-sss/AutoJMS-Update
[ ] Supabase upload sẵn sàng: SUPABASE_SERVICE_ROLE_KEY hoặc Supabase CLI
```

## 3. Build release bằng Velopack

Chạy:

```bat
cd Release
build-release.bat
```

Trong script:

```txt
[ ] Nhập version đúng
[ ] Chọn channel stable hoặc beta
[ ] Chọn build/upload theo nhu cầu
[ ] dotnet publish thành công
[ ] .NET Reactor protect AutoJMS.dll thành công
[ ] Hash AutoJMS.dll lấy sau Reactor
[ ] vpk pack thành công
[ ] Tạo được RELEASES/.nupkg/Setup.exe
```

Output thường nằm trong:

```txt
release/output/stable/
release/output/beta/
```

## 4. GitHub Release phải có

Repo:

```txt
Datt03-sss/AutoJMS-Update
```

Tag:

```txt
v{VelopackVersion}-Release
```

Checklist asset:

```txt
[ ] RELEASES hoặc RELEASES-{channel}
[ ] AutoJMS-x.x.x-full.nupkg
[ ] AutoJMS-win-Setup.exe hoặc AutoJMS-stable-Setup.exe
[ ] Beta release đã mark prerelease
[ ] Stable release không mark prerelease
```

Nếu thao tác thủ công bằng GitHub CLI:

```powershell
gh auth login
gh release create v1.26.6-Release --repo Datt03-sss/AutoJMS-Update --title "AutoJMS 1.26.6 Stable" --notes "Stable release"
gh release upload v1.26.6-Release RELEASES AutoJMS-1.26.6-stable-full.nupkg AutoJMS-win-Setup.exe --repo Datt03-sss/AutoJMS-Update --clobber
```

Beta:

```powershell
gh release create v1.26.6-beta.1-Release --repo Datt03-sss/AutoJMS-Update --title "AutoJMS 1.26.6 Beta 1" --notes "Beta test release" --prerelease
```

## 5. Supabase manifest phải có

Bucket/path:

```txt
autojms-modules/manifest/version-latest.json
autojms-modules/manifest/hash-manifest.json
autojms-modules/manifest/app-manifest.json
autojms-modules/manifest/selector-update-manifest.json
autojms-modules/manifest/tier-definitions.sec
autojms-modules/selector-updates/selector.config.sec
autojms-modules/selector-updates/runtime-config.sec
autojms-modules/selector-updates/runtime-config.sig
autojms-modules/configs/public-config.json
```

Mỗi release cần kiểm tra:

```txt
[ ] version-latest.json trỏ đúng channel/version/tag
[ ] hash-manifest.json có hash AutoJMS.dll sau Reactor
[ ] Stable: prerelease = false
[ ] Beta: prerelease = true
[ ] manualOnly = true cho major update
[ ] Không upload .nupkg lên Supabase
[ ] Không upload Setup.exe lớn lên Supabase
[ ] Không upload secret lên Supabase public bucket
```

Ghi chú: repo hiện có một số file dev dạng `.json`; production sensitive config ưu tiên `.sec`. Nếu path `.sec` chưa được client/server đọc, đánh dấu `NEED VERIFY` trước release.

## 6. Smoke test sau release

```txt
[ ] Mở app đang cài thật hoặc máy test
[ ] Đăng nhập license OK
[ ] BASE chỉ có HOME, DKCH, TRACKING, PRINT, ABOUT
[ ] BASE không chạy background inventory/database sync
[ ] ULTRA mở được FullStackOperationForm
[ ] About → Check Update thấy version mới
[ ] Update không mở GitHub page
[ ] App tải/update qua Velopack OK
[ ] Restart app OK
[ ] Không mất C:\AutoJMS\AppData
[ ] Không bị yêu cầu nhập key lại nếu license/hash hợp lệ
```

## 7. Build setup Inno nếu cần

Chỉ làm bước này khi cần file cài lần đầu/reinstall/repair/runtime prerequisites.

```bat
cd Release
build-release.bat

cd Installer
build-installer.bat
```

Output:

```txt
installer/inno/installer-output/AutoJMS-win-Setup.exe
```

Checklist:

```txt
[ ] Setup include app setup
[ ] Setup include .NET 8 Desktop Runtime
[ ] Setup include WebView2 Evergreen Runtime x64
[ ] Setup include VC++ Redistributable nếu cần
[ ] Không dùng Inno Setup cho update thường ngày
```

## 8. Rollback nhanh

Nếu release lỗi:

```txt
[ ] Đổi version-latest.json về version stable cũ
[ ] Upload lại Supabase manifest
[ ] Nếu GitHub release lỗi, unpublish/xóa release lỗi hoặc restore asset đúng
[ ] Nếu hash sai, lấy hash AutoJMS.dll đúng sau Reactor và upload hash-manifest.json lại
[ ] Nếu Render server lỗi, rollback deploy trên Render
[ ] Nếu Firebase key sai, sửa tier/hwid/status/skipHashCheck về đúng
[ ] Test lại About → Check Update
```

## 9. Không được làm

```txt
KHÔNG ĐƯỢC:
- Không upload .nupkg lên Supabase.
- Không mở GitHub page trong app update.
- Không copy file thủ công vào C:\AutoJMS\current.
- Không xóa C:\AutoJMS\AppData khi update.
- Không dùng version 1.26.05 hoặc 1.26.6.1 làm VelopackVersion.
- Không để secret/serviceAccountKey.json trong repo.
- Không log full token production.
- Không sửa Firebase từng key để bật/tắt tab nếu tier-definitions đã quản lý.
```

## 10. Lỗi nhanh cần nhớ

| Triệu chứng | Cách xử lý nhanh |
| ----------- | ---------------- |
| Supabase upload fail `>50MB` | Đưa `.nupkg`/Setup lên GitHub Release, không đưa lên Supabase. |
| Velopack báo SemVer invalid | Đổi `1.26.05` thành `1.26.5`; beta dùng `1.26.6-beta.1`; không dùng 4 segment làm VelopackVersion. |
| Release already exists | Nếu chưa public rộng, upload lại asset `--clobber`; nếu đã public, bump version. |
| App không thấy update | Kiểm tra `version-latest.json`, GitHub tag, channel, version lớn hơn, asset `RELEASES`. |
| Hash mismatch | Lấy hash sau Reactor, cập nhật `hash-manifest.json`, kiểm tra `VALID_EXE_HASHES` nếu server còn dùng. |
| User nhập key lại | Kiểm tra `C:\AutoJMS\AppData`, hash manifest, secure store, reinstall có xóa data không. |
| Setup repair fail | Đóng app/WebView2/Update.exe rồi chạy lại setup. |
| Thiếu WebView2/.NET Runtime | Chạy Inno Setup hoặc cài runtime trong `installer/inno/redist/`. |


