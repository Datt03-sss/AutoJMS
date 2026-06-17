# Build Stabilization Plan

This plan is for build failures, especially missing root `modules/*.json` content files.

## Current Recorded State

- Historical issue: `src/AutoJMS/AutoJMS.csproj` referenced root `modules/*.json` files that were missing.
- Current recorded fix: each root `modules/*.json` `Content Include` has `Condition="Exists('...')"`.
- Latest recorded `dotnet build src/AutoJMS/AutoJMS.csproj -c Debug` succeeded with warnings.

If build fails again, follow this plan.

## Steps

1. Kiểm tra `src/AutoJMS/AutoJMS.csproj`.
2. Tìm `Content Include` liên quan `modules/*.json`.
3. Nếu file không bắt buộc cho build, thêm:

```xml
Condition="Exists('modules\file.json')"
```

4. Nếu runtime cần file, tạo default JSON tối thiểu hợp lệ trong `modules/`.
5. Không xóa `ModuleSystem`.
6. Không sửa runtime logic.
7. Sau khi fix, chạy:

```bash
dotnet restore
dotnet build src/AutoJMS/AutoJMS.csproj -c Debug
```

## Acceptance Criteria

- Build Debug pass.
- Không thêm secret.
- Không sửa logic app.
- Không sửa installer/release script.
- Kết quả được ghi vào `docs/troubleshooting/build-errors.md`.

## Known Warnings To Track

- `PdfiumViewer 2.13.0` compatibility warning under `net8.0-windows`.
- Obsolete `GoogleCredential.FromJson` / `FromStream`.
- Nullable warnings while nullable is disabled.
- Unawaited async calls around Google Sheet upload flow.


