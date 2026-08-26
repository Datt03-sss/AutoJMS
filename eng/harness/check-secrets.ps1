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
Write-Host '[1/4] Checking .gitignore...' -ForegroundColor Yellow

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
Write-Host '[2/4] Scanning tracked files for secrets...' -ForegroundColor Yellow

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
    '.*\.sec$',
    # Infrastructure state and the infra denylist itself. Both are .gitignore'd, so a match
    # here means the ignore rule was bypassed (git add -f, or an entry removed). The full VPS
    # report names the host, its operator account and the path of its secrets file; the
    # denylist is a list of exactly those values. This repo is PUBLIC.
    '.*\.private\.md$',
    '.*forbidden-values\.local\.txt$'
)

foreach ($file in $trackedFiles) {
    if ($file -match '(backend/render-license-server/\.env\.example)') { continue }
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
Write-Host '[3/4] Checking staged files...' -ForegroundColor Yellow
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

# ─── Part 4: Infrastructure identifiers (denylist) ───
#
# Parts 2 and 3 look for credentials. This part looks for the OTHER leak: the VPS public IP,
# its hostname, the operator account, the deploy and secrets paths. None of those match a
# credential pattern, so before this existed a status report naming all of them passed the
# gate with "Tracked files: OK (no secrets detected)" — silently, into a PUBLIC repo.
#
# Why a denylist of literal values and not a regex: a generic IPv4 pattern is unusable here.
# `InternalBuild` versions like 1.26.6.0 are shaped exactly like an address and appear in ~28
# tracked files, and the hardening keywords (NOPASSWD, PermitRootLogin, maxretry) already live
# in bootstrap-vps.sh and the deploy guides, where they are a recipe rather than a disclosure.
# Matching those would fail the gate on day one and teach everyone to ignore it.
#
# The list lives OUTSIDE git on purpose: a committed denylist of infrastructure identifiers
# would publish the very values it exists to keep out. CI has no local file, so it reads the
# same list from an environment variable fed by a repository secret.
Write-Host '[4/4] Checking for infrastructure identifiers...' -ForegroundColor Yellow

$forbiddenListPath = Join-Path $PSScriptRoot 'forbidden-values.local.txt'
$forbiddenRaw = @()
$forbiddenSource = $null
if (Test-Path $forbiddenListPath) {
    $forbiddenRaw = Get-Content $forbiddenListPath -ErrorAction SilentlyContinue
    $forbiddenSource = 'forbidden-values.local.txt'
} elseif ($env:AUTOJMS_FORBIDDEN_VALUES) {
    $forbiddenRaw = $env:AUTOJMS_FORBIDDEN_VALUES -split '[\r\n;]+'
    $forbiddenSource = 'AUTOJMS_FORBIDDEN_VALUES'
}

# Entry format: "<literal value>" or "<literal value> | <label>". The label is what gets
# printed on a hit — the value itself is never echoed, or the failure message would leak it
# into the CI log it was meant to protect.
$forbidden = @()
$entryIndex = 0
foreach ($line in $forbiddenRaw) {
    $trimmed = "$line".Trim()
    if (-not $trimmed -or $trimmed.StartsWith('#')) { continue }
    $entryIndex++
    $parts = $trimmed -split '\|', 2
    $value = $parts[0].Trim()
    if (-not $value) { continue }
    $label = if ($parts.Count -gt 1 -and $parts[1].Trim()) { $parts[1].Trim() } else { "entry #$entryIndex" }
    $forbidden += @{ Value = $value; Label = $label }
}

if ($forbidden.Count -eq 0) {
    # Not a failure: a fresh clone has no list, and failing there would block every
    # contributor. But it must be loud — a gate nobody configured is a gate that is off.
    Write-Host '  NOTICE: no infra denylist configured; this check is INACTIVE.' -ForegroundColor Yellow
    Write-Host "         Create $forbiddenListPath (git-ignored) or set AUTOJMS_FORBIDDEN_VALUES." -ForegroundColor Yellow
} else {
    $binaryExtensions = @(
        '.png', '.jpg', '.jpeg', '.gif', '.ico', '.bmp', '.dll', '.exe', '.pdb', '.zip',
        '.7z', '.gz', '.pdf', '.ttf', '.otf', '.woff', '.woff2', '.snk', '.nupkg', '.wav',
        '.mp3', '.mp4', '.db', '.sqlite', '.bin', '.cur'
    )

    foreach ($file in $trackedFiles) {
        $ext = [System.IO.Path]::GetExtension($file)
        if ($ext -in $binaryExtensions) { continue }

        $fullPath = Join-Path $Root $file
        if (-not (Test-Path $fullPath)) { continue }

        try {
            $content = Get-Content $fullPath -Raw -ErrorAction SilentlyContinue
            if (-not $content) { continue }

            foreach ($entry in $forbidden) {
                if ($content.IndexOf($entry.Value, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) { continue }
                # Report the line so it can be fixed without hunting, but never the value.
                $hit = Select-String -Path $fullPath -Pattern $entry.Value -SimpleMatch -List -ErrorAction SilentlyContinue
                $where = if ($hit) { "${file}:$($hit.LineNumber)" } else { $file }
                $issues += "INFRA LEAK ($($entry.Label)) in tracked file: $where"
            }
        } catch {
            # Skip files that can't be read
        }
    }

    if (($issues | Where-Object { $_ -match 'INFRA LEAK' }).Count -eq 0) {
        Write-Host "  Infra identifiers: OK ($($forbidden.Count) value(s) checked from $forbiddenSource)" -ForegroundColor Green
    } else {
        foreach ($issue in ($issues | Where-Object { $_ -match 'INFRA LEAK' })) {
            Write-Host "  $issue" -ForegroundColor Red
        }
    }
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
