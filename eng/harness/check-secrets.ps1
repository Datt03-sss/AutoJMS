<#
.SYNOPSIS
    Secret scan harness for AutoJMS.
.DESCRIPTION
    Scans git-tracked, staged, and untracked-but-not-ignored files for common secret
    patterns and for the infrastructure denylist.
    Verifies .gitignore has required entries.
    Files that git actually ignores are out of scope on purpose: `git add .` cannot
    stage them, so they are not one command away from a PUBLIC repository. Untracked
    files that are NOT ignored are exactly that, which is why part 5 exists.
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
Write-Host '[1/5] Checking .gitignore...' -ForegroundColor Yellow

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
Write-Host '[2/5] Scanning tracked files for secrets...' -ForegroundColor Yellow

# Get list of tracked files.
#
# -c core.quotepath=false is not cosmetic. With git's default (quotepath=true) a tracked path
# containing non-ASCII characters is printed wrapped in double quotes with octal escapes —
# "docs/layout/tabThoiHieu/B\341\272\243ng th\341\273\235i hi\341\273\207u N-1.svg". A double
# quote is an illegal character in a Windows path, so [System.IO.Path]::GetExtension() throws
# and $ErrorActionPreference = 'Stop' kills the entire scan in part 2 — parts 3 and 4 never run.
# This is invisible on a machine whose git config already sets core.quotepath=false (as the
# Owner's does); on a CI runner it is the default, and it aborted this script there.
#
# [Console]::OutputEncoding: Windows PowerShell decodes a child process's stdout using the
# console code page, which turns those UTF-8 names into mojibake. The path then fails Test-Path
# and the file is skipped in silence — a hole in the gate rather than a crash.
$previousOutputEncoding = $null
try {
    Push-Location $Root
    try {
        $previousOutputEncoding = [Console]::OutputEncoding
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    } catch {
        # Some hosts refuse to change it; the quotepath fix below still applies.
    }
    $trackedFiles = & git -c core.quotepath=false ls-files 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host '  WARNING: git ls-files failed. Falling back to file system scan.' -ForegroundColor Yellow
        $trackedFiles = Get-ChildItem -Path $Root -Recurse -File |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.vs|\.git|node_modules)[\\/]' } |
            ForEach-Object { $_.FullName.Substring($Root.Length + 1).Replace('\', '/') }
    }
} finally {
    if ($previousOutputEncoding) {
        try { [Console]::OutputEncoding = $previousOutputEncoding } catch { }
    }
    Pop-Location
}

# Belt and braces: whatever the git config on this machine, never hand an unusable string to the
# path APIs below. Warn instead of dying, so one odd filename cannot switch the gate off.
$invalidPathChars = [System.IO.Path]::GetInvalidPathChars()
$trackedFiles = @($trackedFiles | Where-Object {
    $candidate = "$_"
    if (-not $candidate.Trim()) { return $false }
    if ($candidate.IndexOfAny($invalidPathChars) -ge 0) {
        Write-Host "  WARNING: skipping unscannable path from git ls-files: $candidate" -ForegroundColor Yellow
        return $false
    }
    return $true
})

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
    # Wildcard on purpose: CI injects the denylist as an environment variable and must never
    # write it to disk, but a variant filename (forbidden-values.ci.txt) is an easy accident.
    '.*forbidden-values.*\.txt$'
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

# Defined here rather than inside part 4, which is where it used to live and where it is still
# used. Part 4 only assigned it on the branch that HAS a denylist, so on a machine with no
# denylist configured it stayed $null — and `-in $null` matches nothing, so part 5 below would
# have read every binary in the tree as text instead of skipping it.
$binaryExtensions = @(
    '.png', '.jpg', '.jpeg', '.gif', '.ico', '.bmp', '.dll', '.exe', '.pdb', '.zip',
    '.7z', '.gz', '.pdf', '.ttf', '.otf', '.woff', '.woff2', '.snk', '.nupkg', '.wav',
    '.mp3', '.mp4', '.db', '.sqlite', '.bin', '.cur'
)

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
Write-Host '[3/5] Checking staged files...' -ForegroundColor Yellow
try {
    Push-Location $Root
    # Same reason as part 2: a staged path with non-ASCII characters would otherwise arrive
    # quoted and octal-escaped, and would not match the $dangerousFiles patterns below.
    $stagedFiles = & git -c core.quotepath=false diff --cached --name-only 2>&1
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
Write-Host '[4/5] Checking for infrastructure identifiers...' -ForegroundColor Yellow

$forbiddenListPath = Join-Path $PSScriptRoot 'forbidden-values.local.txt'
$forbiddenLines = @()
$forbiddenSource = $null
if (Test-Path $forbiddenListPath) {
    $forbiddenLines = Get-Content $forbiddenListPath -ErrorAction SilentlyContinue
    $forbiddenSource = 'forbidden-values.local.txt'
} elseif ($env:AUTOJMS_FORBIDDEN_VALUES) {
    $forbiddenLines = $env:AUTOJMS_FORBIDDEN_VALUES -split '[\r\n]+'
    $forbiddenSource = 'AUTOJMS_FORBIDDEN_VALUES'
}

# Comment stripped FIRST, ';' split second. The other order was a real bug: the env-var branch
# used to split on '[\r\n;]+' before dropping comments, so a comment line containing a semicolon
# turned its own tail into an "entry". The reference file has three such lines, so CI parsed 5
# entries where the local run parsed 2 — one of them 3 characters long, which matched 30 ordinary
# source files and reported them all as infrastructure leaks. Same denylist, two different
# answers, and the CI one was noise. A '#' inside a value is not supported (no IP, hostname or
# path needs one) and ';' remains a separator so the whole list can be pasted on a single line.
$forbiddenRaw = @()
foreach ($line in $forbiddenLines) {
    $text = "$line"
    $commentAt = $text.IndexOf('#')
    if ($commentAt -ge 0) { $text = $text.Substring(0, $commentAt) }
    if (-not $text.Trim()) { continue }
    $forbiddenRaw += ($text -split ';')
}

# Entry format: "<literal value>" or "<literal value> | <label>". The label is what gets
# printed on a hit — the value itself is never echoed, or the failure message would leak it
# into the CI log it was meant to protect.
$forbidden = @()
$entryIndex = 0
# No infrastructure identifier is shorter than this. A shorter entry means the list was
# mis-authored (a truncated paste, a stray separator), and a short fragment matches ordinary
# source code — which is how a 3-character entry once produced 30 "leaks" in one CI run. Fail
# loudly with the length instead of silently drowning the real entries in noise.
$minEntryLength = 6
foreach ($line in $forbiddenRaw) {
    $trimmed = "$line".Trim()
    if (-not $trimmed) { continue }
    $entryIndex++
    $parts = $trimmed -split '\|', 2
    $value = $parts[0].Trim()
    if (-not $value) { continue }
    $label = if ($parts.Count -gt 1 -and $parts[1].Trim()) { $parts[1].Trim() } else { "entry #$entryIndex" }
    if ($value.Length -lt $minEntryLength) {
        $issues += "DENYLIST ENTRY TOO SHORT ($label): $($value.Length) char(s), minimum $minEntryLength. Fix the list in $forbiddenSource; the value is not printed here."
        continue
    }
    $forbidden += @{ Value = $value; Label = $label }
}

if ($forbidden.Count -eq 0) {
    # Not a failure: a fresh clone has no list, and failing there would block every
    # contributor. But it must be loud — a gate nobody configured is a gate that is off.
    Write-Host '  NOTICE: no infra denylist configured; this check is INACTIVE.' -ForegroundColor Yellow
    Write-Host "         Create $forbiddenListPath (git-ignored) or set AUTOJMS_FORBIDDEN_VALUES." -ForegroundColor Yellow
} else {
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

# ─── Part 5: Untracked files that are not ignored ───
#
# Parts 2 and 3 scan TRACKED and STAGED files only. On 2026-08-27
# backend/render-license-server/AutoJMS-API.env sat in this PUBLIC repo holding
# JWT_PRIVATE_KEY and API_SIGNATURE_SECRET — untracked and, at that point, unignored — and
# this gate still printed "Secret scan completed successfully". CI was green too.
#
# Files git actually IGNORES are deliberately out of scope: `git add .` skips them, so they
# are not one command from being published, and scanning them would fail the gate on the
# Owner's own .env files forever.
#
# `git ls-files --others --exclude-standard`, not `git status --porcelain`: status COLLAPSES a
# wholly untracked directory into a single `?? dir/` line, so a directory holding fifty files
# — one of them a private key — arrives as one entry that names no file. ls-files expands it.
# -z because a NUL-separated list is never quoted or octal-escaped whatever core.quotepath is,
# the same failure that killed part 2 on a CI runner.
Write-Host '[5/5] Scanning untracked files...' -ForegroundColor Yellow

# Collected in a local list and appended at the very end. $issues cannot be used directly:
# the OK-message filters in parts 2 and 4 match on 'TRACKED' and 'INFRA LEAK', and both
# appear as substrings of the messages below ("UNTRACKED ..." contains "TRACKED"), which
# would retroactively turn those two green lines red.
$untrackedIssues = @()
$untrackedFiles = @()
$untrackedEnumerated = $false
try {
    Push-Location $Root
    $rawUntracked = & git ls-files --others --exclude-standard -z 2>&1
    if ($LASTEXITCODE -eq 0) {
        $untrackedEnumerated = $true
        # Windows PowerShell hands a NUL-separated stream back as one string; splitting on
        # the NUL recovers the list. The trailing CR/LF trim covers the last element.
        $untrackedFiles = @((($rawUntracked | Out-String) -split [char]0) |
            ForEach-Object { "$_".Trim([char]13, [char]10) } |
            Where-Object { $_.Trim() })
    }
} catch {
    # Handled below by $untrackedEnumerated.
} finally {
    Pop-Location
}

if (-not $untrackedEnumerated) {
    # Not a failure, for the same reason part 2 falls back instead of dying: this script must
    # still run outside a git checkout. But it must never look like a pass, so no OK line is
    # printed on this path.
    Write-Host '  WARNING: git ls-files --others failed; untracked files were NOT scanned.' -ForegroundColor Yellow
} else {
    $maxContentBytes = 2MB
    $skippedLarge = @()
    $invalidPathChars = [System.IO.Path]::GetInvalidPathChars()

    foreach ($file in $untrackedFiles) {
        if ($file.IndexOfAny($invalidPathChars) -ge 0) {
            Write-Host "  WARNING: skipping unscannable untracked path: $file" -ForegroundColor Yellow
            continue
        }

        foreach ($pattern in $dangerousFiles) {
            if ($file -match $pattern) {
                $untrackedIssues += "UNTRACKED SECRET FILE (not ignored, one 'git add .' from being published): $file"
            }
        }

        $fullPath = Join-Path $Root $file
        if (-not (Test-Path -LiteralPath $fullPath)) { continue }
        $ext = [System.IO.Path]::GetExtension($file)
        if ($ext -in $binaryExtensions) { continue }
        $info = Get-Item -LiteralPath $fullPath -ErrorAction SilentlyContinue
        if (-not $info -or $info.Length -eq 0) { continue }
        if ($info.Length -gt $maxContentBytes) { $skippedLarge += $file; continue }

        try {
            $content = Get-Content -LiteralPath $fullPath -Raw -ErrorAction SilentlyContinue
            if (-not $content) { continue }

            # No $sourceExtensions gate here, unlike part 2. For a file nobody has decided to
            # commit yet, a miss is a published secret and a false positive is a local
            # annoyance — and the file that started this was a .env, an extension part 2's
            # allow-list does not contain.
            foreach ($sp in $secretPatterns) {
                if ($content -match $sp.Pattern) {
                    $untrackedIssues += "POTENTIAL SECRET ($($sp.Name)) in untracked file: $file"
                }
            }

            foreach ($entry in $forbidden) {
                if ($content.IndexOf($entry.Value, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) { continue }
                # Line number, never the value — same rule as part 4.
                $hit = Select-String -LiteralPath $fullPath -Pattern $entry.Value -SimpleMatch -List -ErrorAction SilentlyContinue
                $where = if ($hit) { "${file}:$($hit.LineNumber)" } else { $file }
                $untrackedIssues += "INFRA LEAK ($($entry.Label)) in untracked file: $where"
            }
        } catch {
            # Skip files that can't be read
        }
    }

    foreach ($skipped in $skippedLarge) {
        Write-Host "  NOTICE: content not scanned (over $($maxContentBytes / 1MB) MB): $skipped" -ForegroundColor Yellow
    }

    if ($untrackedIssues.Count -eq 0) {
        $denylistNote = if ($forbidden.Count -gt 0) { "$($forbidden.Count) denylist value(s) applied" } else { 'denylist INACTIVE' }
        Write-Host "  Untracked files: OK ($($untrackedFiles.Count) file(s) scanned, $denylistNote)" -ForegroundColor Green
    } else {
        foreach ($issue in $untrackedIssues) {
            Write-Host "  $issue" -ForegroundColor Red
        }
    }
}

$issues += $untrackedIssues

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
