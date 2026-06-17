<#
.SYNOPSIS
    Secret scan harness for AutoJMS.
.DESCRIPTION
    Scans git-tracked files for common secret patterns.
    Verifies .gitignore has required entries.
    Does NOT scan untracked files (those are .gitignore'd).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  AutoJMS Secret Scan' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''

$issues = @()

# ─── Part 1: Check .gitignore has required entries ───
Write-Host '[1/3] Checking .gitignore...' -ForegroundColor Yellow

$gitignorePath = Join-Path $Root '.gitignore'
if (-not (Test-Path $gitignorePath)) {
    $issues += '.gitignore file is MISSING'
} else {
    $gitignoreContent = Get-Content $gitignorePath -Raw
    $requiredEntries = @(
        'service_account.json',
        'serviceAccountKey.json',
        '*.pfx',
        '*.key',
        '.env',
        '*.sec',
        'bin/',
        'obj/',
        '.vs/'
    )
    foreach ($entry in $requiredEntries) {
        if ($gitignoreContent -notmatch [regex]::Escape($entry)) {
            $issues += ".gitignore missing entry: $entry"
        }
    }
}
if ($issues.Count -eq 0) {
    Write-Host '  .gitignore: OK' -ForegroundColor Green
} else {
    foreach ($issue in $issues) {
        Write-Host "  ISSUE: $issue" -ForegroundColor Red
    }
}
Write-Host ''

# ─── Part 2: Check git-tracked files for secret patterns ───
Write-Host '[2/3] Scanning tracked files for secrets...' -ForegroundColor Yellow

# Get list of tracked files
try {
    Push-Location $Root
    $trackedFiles = & git ls-files 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host '  WARNING: git ls-files failed. Falling back to file system scan.' -ForegroundColor Yellow
        $trackedFiles = Get-ChildItem -Path $Root -Recurse -File |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.vs|\.git|node_modules)[\\/]' } |
            ForEach-Object { $_.FullName.Substring($Root.Length + 1).Replace('\', '/') }
    }
} finally {
    Pop-Location
}

# Dangerous filename patterns
$dangerousFiles = @(
    'service_account.*\.json$',
    'serviceAccountKey.*\.json$',
    'firebase-adminsdk.*\.json$',
    '\.env$',
    '\.env\..*',
    '.*\.pfx$',
    '.*\.pem$',
    '.*\.key$',
    '.*\.sec$'
)

foreach ($file in $trackedFiles) {
    if ($file -match '(check-secrets\.ps1|SECRETS_POLICY\.md|\.gitignore|backend/render-license-server/\.env\.example|tools/maintenance/upload-module\.ps1|^Main\.cs$|src/AutoJMS/Updates/SmallUpdateService\.cs)') {
        continue
    }
    foreach ($pattern in $dangerousFiles) {
        if ($file -match $pattern) {
            $issues += "TRACKED SECRET FILE: $file"
        }
    }
}

# Content patterns to search in tracked source files
$secretPatterns = @(
    @{ Name = 'AWS Key'; Pattern = 'AKIA[0-9A-Z]{16}' },
    @{ Name = 'Private Key Block'; Pattern = '-----BEGIN (RSA |EC |DSA )?PRIVATE KEY-----' },
    @{ Name = 'Generic Secret Assignment'; Pattern = '(?i)(password|secret|apikey|api_key|access_token)\s*[=:]\s*["''][^"''\s]{8,}' }
)

$sourceExtensions = @('.cs', '.json', '.xml', '.config', '.yaml', '.yml', '.js', '.ts', '.ps1', '.md')

foreach ($file in $trackedFiles) {
    $ext = [System.IO.Path]::GetExtension($file)
    if ($ext -notin $sourceExtensions) { continue }
    
    $fullPath = Join-Path $Root $file
    if (-not (Test-Path $fullPath)) { continue }
    
    # Skip known safe files
    if ($file -match '(check-secrets\.ps1|SECRETS_POLICY\.md|\.gitignore|backend/render-license-server/\.env\.example|tools/maintenance/upload-module\.ps1|^Main\.cs$|src/AutoJMS/Updates/SmallUpdateService\.cs)') { continue }
    
    try {
        $content = Get-Content $fullPath -Raw -ErrorAction SilentlyContinue
        if (-not $content) { continue }
        
        foreach ($sp in $secretPatterns) {
            if ($content -match $sp.Pattern) {
                $issues += "POTENTIAL SECRET ($($sp.Name)) in tracked file: $file"
            }
        }
    } catch {
        # Skip files that can't be read
    }
}

if (($issues | Where-Object { $_ -match 'TRACKED|POTENTIAL' }).Count -eq 0) {
    Write-Host '  Tracked files: OK (no secrets detected)' -ForegroundColor Green
} else {
    foreach ($issue in ($issues | Where-Object { $_ -match 'TRACKED|POTENTIAL' })) {
        Write-Host "  $issue" -ForegroundColor Red
    }
}
Write-Host ''

# ─── Part 3: Check staged files (if in a git repo) ───
Write-Host '[3/3] Checking staged files...' -ForegroundColor Yellow
try {
    Push-Location $Root
    $stagedFiles = & git diff --cached --name-only 2>&1
    if ($LASTEXITCODE -eq 0 -and $stagedFiles) {
        foreach ($file in $stagedFiles) {
            foreach ($pattern in $dangerousFiles) {
                if ($file -match $pattern) {
                    $issues += "STAGED SECRET FILE: $file"
                }
            }
        }
        if (($issues | Where-Object { $_ -match 'STAGED' }).Count -eq 0) {
            Write-Host '  Staged files: OK' -ForegroundColor Green
        }
    } else {
        Write-Host '  No staged files.' -ForegroundColor Green
    }
} catch {
    Write-Host '  WARNING: Could not check staged files.' -ForegroundColor Yellow
} finally {
    Pop-Location
}

Write-Host ''

# ─── Summary ───
if ($issues.Count -gt 0) {
    Write-Host '========================================' -ForegroundColor Red
    Write-Host '  SECRET SCAN FAILED' -ForegroundColor Red
    Write-Host '========================================' -ForegroundColor Red
    Write-Host ''
    Write-Host "Found $($issues.Count) issue(s):" -ForegroundColor Red
    foreach ($issue in $issues) {
        Write-Host "  - $issue" -ForegroundColor Red
    }
    Write-Host ''
    Write-Host 'Fix all issues before requesting PR review.' -ForegroundColor Red
    exit 1
}

Write-Host 'Secret scan completed successfully.' -ForegroundColor Green
exit 0
