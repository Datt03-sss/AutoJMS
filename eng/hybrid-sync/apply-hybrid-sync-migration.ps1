# Apply hybrid-sync Supabase migrations (docs/hybrid-supabase-sync-plan.md)
# Run: powershell -ExecutionPolicy Bypass -File .\eng\apply-hybrid-sync-migration.ps1
$ErrorActionPreference = "Stop"
$projectRef = "bnsnnrlwfzxemmizknwy"
$backendDir = Join-Path $PSScriptRoot "..\backend"

Write-Host "=== 1/4 Kiem tra Supabase CLI ===" -ForegroundColor Cyan
if (-not (Get-Command supabase -ErrorAction SilentlyContinue)) {
    Write-Host "Chua co CLI - dang cai qua winget..."
    winget install --id Supabase.CLI --accept-source-agreements --accept-package-agreements
    # Refresh PATH for current session
    $env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                [Environment]::GetEnvironmentVariable("Path", "User")
    if (-not (Get-Command supabase -ErrorAction SilentlyContinue)) {
        throw "Cai xong nhung PATH chua nhan. Mo PowerShell moi va chay lai script."
    }
}
supabase --version

Write-Host "=== 2/4 Dang nhap (mo trinh duyet neu chua login) ===" -ForegroundColor Cyan
$loggedIn = $false
try { supabase projects list *> $null; $loggedIn = ($LASTEXITCODE -eq 0) } catch {}
if (-not $loggedIn) { supabase login }

Write-Host "=== 3/4 Link project $projectRef ===" -ForegroundColor Cyan
Push-Location $backendDir
try {
    supabase link --project-ref $projectRef
    if ($LASTEXITCODE -ne 0) { throw "Link that bai (kiem tra mat khau DB: Dashboard > Settings > Database)." }

    Write-Host "=== 4/4 Apply migrations (db push) ===" -ForegroundColor Cyan
    supabase db push --include-all
    if ($LASTEXITCODE -ne 0) { throw "db push that bai - xem log tren." }

    Write-Host ""
    Write-Host "=== VERIFY ===" -ForegroundColor Cyan
    # Cac bang/RPC cua hybrid sync phai ton tai sau khi apply
    supabase migration list
    Write-Host ""
    Write-Host "XONG. Kiem tra nhanh tren Dashboard > Table Editor:" -ForegroundColor Green
    Write-Host "  - Bang moi: order_notes, order_checks, dispatch_tasks"
    Write-Host "  - Bang waybills co them cot site_code"
    Write-Host "  - Event store: waybill_events (migration 202607110002)"
}
finally {
    Pop-Location
}
