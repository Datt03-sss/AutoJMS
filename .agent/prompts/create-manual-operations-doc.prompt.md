# Create Manual Operations Doc Prompt

Bạn là senior build/release engineer + technical writer cho project AutoJMS.

Nhiệm vụ: tạo hoặc cập nhật tài liệu manual cho các thao tác nằm ngoài local production code để developer dùng trong workflow vibe coding. Chỉ tạo/cập nhật markdown, không sửa production code, không sửa `.cs`, không sửa `.csproj`, không sửa script build/release/installer nếu chưa được yêu cầu, không thêm secret.

File cần tạo/cập nhật:

```txt
docs/manual/MANUAL_OPERATIONS.md
.agent/prompts/create-manual-operations-doc.prompt.md
```

Nếu chưa có folder thì tạo:

```txt
docs/manual/
```

Context cần phản ánh:

- AutoJMS là app desktop .NET 8 WinForms logistics automation.
- Inno Setup dùng cho cài lần đầu, reinstall, uninstall, chọn đường dẫn và cài runtime thiếu.
- Velopack dùng cho update trong app.
- GitHub Releases chứa file Velopack lớn vì Supabase free plan không upload được file lớn.
- Supabase Storage chỉ chứa manifest/config/hash/selector-update nhỏ.
- Firebase Realtime Database chứa license key, tier, hwid, middleCode, skipHashCheck/modulePolicy.
- Render `server.js` dùng verify license và heartbeat.
- .NET Reactor protect `AutoJMS.dll` trước khi pack release và lấy hash.
- Major update chỉ chạy khi user bấm tab About → Check Update.
- Selector/runtime config nhỏ có thể auto update qua Supabase selector-update-manifest.
- Không mở GitHub page khi update.
- Không upload `.nupkg` lên Supabase.

`docs/manual/MANUAL_OPERATIONS.md` phải viết bằng tiếng Việt, thực dụng, dạng checklist/manual, gồm đủ 20 section:

1. Mục đích tài liệu.
2. Sơ đồ trách nhiệm hệ thống: Local project, Inno Setup, Velopack, GitHub Releases, Supabase Storage, Firebase, Render server, User machine.
3. Cấu trúc folder local cần biết.
4. Cấu trúc cài đặt trên máy user `C:\AutoJMS`.
5. Quy tắc version: Velopack SemVer hợp lệ, `displayVersion` có thể 4 số, stable/beta, version mới phải lớn hơn.
6. Build release update bằng Velopack: `cd Release`, `build-release.bat`, publish, Reactor, hash, `vpk pack`, GitHub upload, Supabase manifest upload.
7. File Velopack upload lên GitHub Release: `RELEASES`, `.nupkg`, Setup.exe; repo `Datt03-sss/AutoJMS-Update`; tag `v{VelopackVersion}-Release`; beta mark prerelease.
8. File manifest upload lên Supabase: `version-latest.json`, `hash-manifest.json`, `app-manifest.json`, `selector-update-manifest.json`, `tier-definitions.sec`, `selector.config.sec`, `runtime-config.sec`, `runtime-config.sig`, `public-config.json`; không upload binary/secret.
9. Nội dung mẫu `version-latest.json`.
10. Nội dung mẫu `hash-manifest.json`; hash sau .NET Reactor.
11. Build setup cài lần đầu bằng Inno Setup: build release trước, build installer sau, output `installer/inno/installer-output/AutoJMS-win-Setup.exe`.
12. Firebase manual operations: đổi tier, reset hwid, disable key, dev `skipHashCheck`, không chứa update URL.
13. Render server manual operations: `server.js`, `/api/verify-license`, `/api/heartbeat`, health endpoint, ENV JWT/hash/Supabase. Nếu code hiện tại dùng tên khác, ghi `NEED VERIFY`.
14. GitHub Release manual operations bằng GitHub CLI.
15. Supabase manual operations qua Dashboard/CLI.
16. Checklist release stable.
17. Checklist beta.
18. Rollback manual.
19. Bảng `KHÔNG ĐƯỢC`.
20. Troubleshooting: Supabase >50MB, SemVer leading zero, Velopack parse/version lỗi, GitHub CLI auth, release exists, app không thấy update, hash mismatch, user nhập key lại, setup repair fail vì app/WebView2 còn chạy, thiếu WebView2 Runtime, thiếu .NET 8 Desktop Runtime.

Yêu cầu chất lượng:

- Dựa trên file/code thật trong repo.
- Ghi rõ file nào upload lên đâu.
- Ghi rõ stable/beta khác nhau thế nào.
- Ghi rõ release update khác setup installer thế nào.
- Ghi rõ Supabase vs GitHub vs Firebase vs Render.
- Không đưa secret thật vào tài liệu.
- Nếu không chắc file/path/ENV/route nào, ghi `NEED VERIFY`.

Khi hoàn tất, trả summary:

```txt
DONE:
- Created docs/manual/MANUAL_OPERATIONS.md
- Created .agent/prompts/create-manual-operations-doc.prompt.md

No production code modified.
No build/release script modified.
No secret added.
```


