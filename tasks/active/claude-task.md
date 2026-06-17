# Claude Code Task — AutoJMS

## Approved by owner
Yes

## Repo
https://github.com/Datt03-sss/AutoJMS

## Branch
main

## Task summary
Loại bỏ các problems/warnings về format whitespace trong solution bằng lệnh dotnet format.

## Scope allowed
- src/AutoJMS/Forms/FullStackOperation.Dashboard.cs
- src/AutoJMS/Forms/Main.cs
- src/AutoJMS/Models/WaybillModels.cs
- src/AutoJMS/Program.cs
- src/AutoJMS/Services/InventorySyncService.cs
- src/AutoJMS/UI/AppPalette.cs
- src/AutoJMS/UI/AppTheme.cs

## Scope forbidden
- License/auth/hash-check
- Firebase session
- Supabase production config
- Velopack release
- Database schema
- JMS API logic
- Print business logic nếu không liên quan
- Tracking parser nếu không liên quan
- service_account/key/token/secret

## Requirements
1. Chạy lệnh `dotnet format` để tự động sửa các lỗi whitespace formatting trong các file được báo cáo.
2. Đảm bảo không thay đổi bất kỳ logic code hay cấu trúc nào ngoài định dạng khoảng trắng.
3. Chạy build ở cả cấu hình Debug và Release để xác nhận build thành công không lỗi.
4. Chạy verification harness `powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1` để kiểm tra.
5. Commit thay đổi với commit message rõ ràng và push lên origin main.

## Build commands
```powershell
dotnet restore .\AutoJMS.slnx
dotnet build .\AutoJMS.slnx -c Release
```
