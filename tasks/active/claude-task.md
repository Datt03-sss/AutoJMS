# Claude Code Task — Default Printer Page Size Set/Unset

## Approved by owner
Yes

## Repo
https://github.com/Datt03-sss/AutoJMS

## Branch
main

## Task summary
Update AutoJMS print page size behavior so set/unset page size applies to the current Windows default printer used by AutoJMS print jobs.

## Requirements
* Set page size applies to the current default printer.
* Unset page size removes AutoJMS override and returns to default printer default page settings.
* Do not hardcode printer name.
* Do not change Windows global printer preferences.
* Do not affect unrelated print business logic.
* Validate missing default printer and unsupported page size.
* Build must pass before commit/push.

## Scope allowed
- `src/AutoJMS/Forms/Main.cs`

## Scope forbidden
- License/Auth/Hash check
- Firebase session
- Supabase production config
- Velopack/update/release
- Database schema
- JMS API
- Tracking parser
- WebView automation
- service_account/key/token/secret
- version/release config
- unrelated UI theme files
- Windows global printer preferences (Win32 DEVMODE)

## Build commands
```powershell
dotnet restore .\AutoJMS.slnx
dotnet build .\AutoJMS.slnx -c Release
```

## Required checks before commit
```powershell
git diff --name-only
git diff --stat
powershell -ExecutionPolicy Bypass -File .\eng\agents\check-scope.ps1
```

## Commit command after build and scope check pass
```powershell
git add .
git commit -m "fix(print): apply page size settings to default printer"
git push origin main
git log --oneline -1
git status
```

## Final report required
* Summary
* Files changed
* Default printer behavior
* Set page size behavior
* Unset page size behavior
* Error handling
* Build result
* Scope check result
* Commit hash
* Pushed to GitHub
* Owner manual test checklist
* Risks

Owner manual test checklist:
* Kiểm tra Windows đang có default printer.
* Set page size trong AutoJMS.
* In thử bằng default printer.
* Đổi default printer trong Windows, mở lại/in lại để kiểm tra app lấy default printer mới.
* Unset page size trong AutoJMS.
* In lại và xác nhận dùng page size mặc định của default printer.
* Test khi printer không hỗ trợ page size đã chọn.
* Test khi không có default printer nếu có thể.
* Đảm bảo Print flow cũ không bị hỏng.
* Đảm bảo các tab khác không bị ảnh hưởng.
