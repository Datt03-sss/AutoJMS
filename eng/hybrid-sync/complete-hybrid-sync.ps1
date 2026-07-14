# Hoan tat trien khai hybrid-supabase-sync:
#   1. Push commit len origin/main
#   2. Apply Supabase migrations (CLI: install -> login -> link -> db push)
# Run: powershell -ExecutionPolicy Bypass -File .\eng\complete-hybrid-sync.ps1
# Toan bo output duoc ghi vao eng\hybrid-sync-log.txt (Claude doc truc tiep file nay).

$ErrorActionPreference = "Continue"
$repoRoot = Split-Path $PSScriptRoot -Parent
$logFile = Join-Path $PSScriptRoot "hybrid-sync-log.txt"
$projectRef = "bnsnnrlwfzxemmizknwy"
$backendDir = Join-Path $repoRoot "backend"

Start-Transcript -Path $logFile -Force | Out-Null
$failed = @()

function Step($name, [scriptblock]$action) {
    Write-Host ""
    Write-Host "=== $name ===" -ForegroundColor Cyan
    try {
        & $action
        if ($LASTEXITCODE -ne $null -and $LASTEXITCODE -ne 0) {
            throw "Exit code $LASTEXITCODE"
        }
        Write-Host "[OK] $name" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "[LOI] $name : $($_.Exception.Message)" -ForegroundColor Red
        $script:failed += $name
        return $false
    }
}

Step "1/5 Push origin/main" {
    Set-Location $repoRoot
    git log --oneline -1
    git push origin main
} | Out-Null

$cliOk = Step "2/5 Supabase CLI" {
    if (-not (Get-Command supabase -ErrorAction SilentlyContinue)) {
        Write-Host "Chua co CLI - cai qua winget..."
        winget install --id Supabase.CLI --accept-source-agreements --accept-package-agreements
        $env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                    [Environment]::GetEnvironmentVariable("Path", "User")
    }
    if (-not (Get-Command supabase -ErrorAction SilentlyContinue)) {
        throw "CLI cai xong nhung PATH chua nhan - mo PowerShell MOI va chay lai script."
    }
    supabase --version
}

if ($cliOk) {
    Step "3/5 Login Supabase" {
        supabase projects list *> $null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Mo trinh duyet de dang nhap..."
            supabase login
        } else {
            Write-Host "Da dang nhap san."
            $global:LASTEXITCODE = 0
        }
    } | Out-Null

    Step "4/5 Link project $projectRef" {
        Set-Location $backendDir
        supabase link --project-ref $projectRef
    } | Out-Null

    Step "5/5 Apply migrations (db push)" {
        Set-Location $backendDir
        supabase db push --include-all
        supabase migration list
    } | Out-Null
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Yellow
if ($failed.Count -eq 0) {
    Write-Host "HOAN TAT - tat ca buoc OK." -ForegroundColor Green
} else {
    Write-Host ("CO LOI o buoc: " + ($failed -join ", ")) -ForegroundColor Red
    Write-Host "Log day du: $logFile" -ForegroundColor Yellow
    Write-Host "Bao Claude: 'doc log' de Claude tu doc file va xu ly tiep." -ForegroundColor Yellow
}
Write-Host "=========================================="
Stop-Transcript | Out-Null
Read-Host "Nhan ENTER de dong cua so"
