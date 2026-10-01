<#
.SYNOPSIS
  Đẩy tab2config.json lên DataHub để mọi máy client (license bật autoUpdate) tự nhận trong vòng 30 phút.

.DESCRIPTION
  Dùng khi JMS đổi nhãn nút/dropdown: sửa src/AutoJMS/modules/tab2config.json (dropdownOptions,
  dropdownValues, saveButtonTitles...) rồi chạy script này. Không cần build/phát hành app.

  Ghi 3 object qua PUT /api/v1/admin/manifests/{path}:
    1. modules/tab2config/<version>/tab2config.json  — đường dẫn theo version để cache 60s của
       DataHub không trả bản cũ cho manifest mới (lệch sha256 -> client từ chối file).
    2. modules/modules.json      — GỘP: chỉ thay mục "tab2config", giữ nguyên các module khác.
    3. manifest/app-manifest.json — chỉ tạo khi chưa có (client cần nó để biết modules.json ở đâu).

  CHỈ Owner chạy (đụng DataHub production). Token admin đọc từ $env:DATAHUB_ADMIN_TOKEN,
  không bao giờ ghi vào file.

.EXAMPLE
  $env:DATAHUB_ADMIN_TOKEN = '<token>'
  .\tools\maintenance\publish-tab2config.ps1 -DryRun
  .\tools\maintenance\publish-tab2config.ps1
#>
param(
    [string]$File = (Join-Path $PSScriptRoot "..\..\src\AutoJMS\modules\tab2config.json"),
    [string]$Base = "https://dev.jmsauto.online",
    [switch]$DryRun
)
$ErrorActionPreference = "Stop"
$Base = $Base.TrimEnd('/')

$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $File))
$null = [System.Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json   # JSON hỏng thì dừng ở đây
$sha = -join ([System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes) | ForEach-Object { $_.ToString("x2") })
$version = (Get-Date).ToUniversalTime().ToString("yyyyMMdd.HHmmss")
$blobPath = "modules/tab2config/$version/tab2config.json"

function Get-Published([string]$path) {
    try { return Invoke-RestMethod -Uri "$Base/$path" -Headers @{ "Cache-Control" = "no-cache" } }
    catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { return $null }
        throw
    }
}

function Publish([string]$path, [byte[]]$body) {
    if ($DryRun) { Write-Host "[DryRun] PUT $path ($($body.Length) bytes)"; return }
    if (-not $env:DATAHUB_ADMIN_TOKEN) { throw "Thiếu `$env:DATAHUB_ADMIN_TOKEN." }
    $null = Invoke-WebRequest -UseBasicParsing -Method Put -Uri "$Base/api/v1/admin/manifests/$path" `
        -Headers @{ Authorization = "Bearer $($env:DATAHUB_ADMIN_TOKEN)" } `
        -ContentType "application/json" -Body $body
    Write-Host "OK  PUT $path"
}

function JsonBytes($obj) { [System.Text.Encoding]::UTF8.GetBytes(($obj | ConvertTo-Json -Depth 10)) }

# 1. Blob trước: manifest chỉ được trỏ vào file đã nằm trên server.
Publish $blobPath $bytes

# 2. modules.json — fetch rồi gộp, PUT là ghi đè toàn bộ object.
$modules = Get-Published "modules/modules.json"
$list = @()
if ($modules -and $modules.modules) { $list = @($modules.modules | Where-Object { $_.name -ne "tab2config" }) }
$list += [pscustomobject]@{ name = "tab2config"; version = $version; file = $blobPath; sha256 = $sha; required = $false; requires = @() }
$manifest = [ordered]@{ manifestVersion = "1.0"; appVersion = $(if ($modules) { $modules.appVersion } else { "1.0.0" }); modules = $list }
Publish "modules/modules.json" (JsonBytes $manifest)

# 3. app-manifest.json — chỉ tạo khi chưa có, không đè cấu hình Owner đã đặt.
$app = Get-Published "manifest/app-manifest.json"
if (-not $app) {
    Publish "manifest/app-manifest.json" (JsonBytes ([ordered]@{
        appVersion = "1.0.0"; manifestVersion = $version; minCoreVersion = "1.0.0"; modulesManifestUrl = "modules/modules.json" }))
} elseif (-not $app.modulesManifestUrl) {
    Write-Warning "manifest/app-manifest.json đã có nhưng thiếu modulesManifestUrl — client sẽ dùng mặc định modules/modules.json."
}

Write-Host ""
Write-Host "tab2config v$version  sha256=$sha"
Write-Host "Client có license autoUpdate=true nhận trong <= 30 phút (hoặc khi mở lại app). Log client: 'Updated tab2config -> v$version'."
