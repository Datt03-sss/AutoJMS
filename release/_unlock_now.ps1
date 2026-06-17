# One-shot unlock: run on the user's machine to free locks and remove the
# previously-failed publish folder so the next build-release.ps1 starts clean.
$ErrorActionPreference = 'SilentlyContinue'

Write-Host '[unlock] Stopping processes that commonly lock publish files...' -ForegroundColor Cyan
foreach ($n in @('AutoJMS','Update','MSBuild','VBCSCompiler')) {
    $procs = Get-Process -Name $n -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host ("  Killing {0} x{1}" -f $n, $procs.Count) -ForegroundColor Yellow
        $procs | Stop-Process -Force
    }
}

Write-Host '[unlock] Shutting down dotnet build server...' -ForegroundColor Cyan
& dotnet build-server shutdown 2>&1 | Out-Null

$root = Split-Path -Parent $PSScriptRoot
$targets = @(
    (Join-Path $root 'artifacts\publish\win-x64'),
    (Join-Path $root 'src\AutoJMS\bin\Release\net8.0-windows\win-x64\publish'),
    (Join-Path $root 'src\AutoJMS\obj\Release\net8.0-windows\win-x64')
)

foreach ($t in $targets) {
    if (-not (Test-Path $t)) { continue }
    Write-Host ("[unlock] Cleaning {0}" -f $t) -ForegroundColor Cyan

    # Strip ReadOnly recursively
    Get-ChildItem -LiteralPath $t -Recurse -Force -ErrorAction SilentlyContinue |
        ForEach-Object { try { $_.Attributes = 'Normal' } catch {} }

    $ok = $false
    for ($i = 1; $i -le 5; $i++) {
        try {
            Remove-Item -LiteralPath $t -Recurse -Force -ErrorAction Stop
            $ok = $true
            break
        } catch {
            Write-Host ("  retry {0}: {1}" -f $i, $_.Exception.Message) -ForegroundColor Yellow
            Start-Sleep -Milliseconds (500 * $i)
        }
    }

    if (-not $ok) {
        $stash = "$t.locked.$(Get-Date -Format 'yyyyMMddHHmmss')"
        try {
            Rename-Item -LiteralPath $t -NewName (Split-Path $stash -Leaf) -Force -ErrorAction Stop
            Write-Host ("  Renamed locked dir to: {0}" -f $stash) -ForegroundColor Yellow
        } catch {
            Write-Host ("  FAILED to delete or rename {0}" -f $t) -ForegroundColor Red
            Write-Host ("    {0}" -f $_.Exception.Message) -ForegroundColor Red
        }
    } else {
        Write-Host '  OK' -ForegroundColor Green
    }
}

Write-Host '[unlock] Done. You can now re-run release\build-release.ps1' -ForegroundColor Green
