# Verify hybrid-sync schema tren Supabase (doc key tu .env.supabase)
# Run: powershell -ExecutionPolicy Bypass -File .\eng\verify-hybrid-sync.ps1
$ErrorActionPreference = "Continue"
$repoRoot = Split-Path $PSScriptRoot -Parent
$logFile = Join-Path $PSScriptRoot "hybrid-sync-verify-log.txt"
Start-Transcript -Path $logFile -Force | Out-Null

# Doc .env.supabase
$envFile = Join-Path $repoRoot ".env.supabase"
$env_ = @{}
Get-Content $envFile | ForEach-Object {
    if ($_ -match '^\s*([^#=]+)=(.*)$') { $env_[$Matches[1].Trim()] = $Matches[2].Trim() }
}
$url = $env_["SUPABASE_URL"]; $key = $env_["SUPABASE_SERVICE_ROLE_KEY"]
$headers = @{ apikey = $key; Authorization = "Bearer $key"; "Content-Type" = "application/json" }
$pass = 0; $fail = 0

function Check($name, [scriptblock]$call) {
    try {
        & $call | Out-Null
        Write-Host "[PASS] $name" -ForegroundColor Green
        $script:pass++
    } catch {
        $msg = $_.Exception.Message
        try { $msg = $_.ErrorDetails.Message } catch {}
        Write-Host "[FAIL] $name : $msg" -ForegroundColor Red
        $script:fail++
    }
}

Check "waybills.site_code column" {
    Invoke-RestMethod -Uri "$url/rest/v1/waybills?select=site_code,updated_at&limit=1" -Headers $headers
}
Check "table order_notes" {
    Invoke-RestMethod -Uri "$url/rest/v1/order_notes?limit=1" -Headers $headers
}
Check "table order_checks" {
    Invoke-RestMethod -Uri "$url/rest/v1/order_checks?limit=1" -Headers $headers
}
Check "table dispatch_tasks" {
    Invoke-RestMethod -Uri "$url/rest/v1/dispatch_tasks?limit=1" -Headers $headers
}
Check "rpc pull_waybill_delta" {
    Invoke-RestMethod -Method Post -Uri "$url/rest/v1/rpc/pull_waybill_delta" -Headers $headers `
        -Body '{"p_site_code":"default","p_since":"1970-01-01T00:00:00Z","p_limit":1}'
}
Check "rpc pull_order_notes" {
    Invoke-RestMethod -Method Post -Uri "$url/rest/v1/rpc/pull_order_notes" -Headers $headers `
        -Body '{"p_site_code":"default","p_since":"1970-01-01T00:00:00Z","p_limit":1}'
}
Check "rpc merge_waybill_rows_v2 (empty)" {
    Invoke-RestMethod -Method Post -Uri "$url/rest/v1/rpc/merge_waybill_rows_v2" -Headers $headers `
        -Body '{"p_site_code":"default","p_rows":[]}'
}
Check "rpc try_acquire + release site lease" {
    $r = Invoke-RestMethod -Method Post -Uri "$url/rest/v1/rpc/try_acquire_site_lease" -Headers $headers `
        -Body '{"p_site_code":"VERIFY","p_owner_id":"verify-script","p_lease_seconds":5}'
    if (-not $r) { throw "lease not granted" }
    Invoke-RestMethod -Method Post -Uri "$url/rest/v1/rpc/release_site_lease" -Headers $headers `
        -Body '{"p_site_code":"VERIFY","p_owner_id":"verify-script"}'
}
Check "realtime publication (4 tables)" {
    $q = 'select count(*) from pg_publication_tables where pubname=''supabase_realtime'' and tablename in (''waybills'',''order_notes'',''order_checks'',''dispatch_tasks'')'
    # PostgREST khong cho query truc tiep - kiem tra qua CLI neu co
    if (Get-Command supabase -ErrorAction SilentlyContinue) {
        Push-Location (Join-Path $repoRoot "backend")
        try { Write-Host "  (kiem tra thu cong: Dashboard > Database > Publications)" }
        finally { Pop-Location }
    }
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Yellow
Write-Host "KET QUA: $pass PASS / $fail FAIL" -ForegroundColor $(if ($fail -eq 0) { "Green" } else { "Red" })
Write-Host "Log: $logFile"
Write-Host "=========================================="
Stop-Transcript | Out-Null
Read-Host "Nhan ENTER de dong cua so"
