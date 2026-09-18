param(
    [Parameter(Mandatory=$true)] [string] $InstallerPath,
    [Parameter(Mandatory=$true)] [string] $DownloadUrl,
    [Parameter(Mandatory=$true)] [string] $Version,
    [Parameter(Mandatory=$true)] [string] $BearerToken,
    [string] $ApiBase = "https://businessos-commerce-api.onrender.com",
    [string] $ProductCode = "AI_REPAIR",
    [ValidateSet("Stable", "Beta", "Internal")] [string] $Channel = "Stable",
    [ValidateSet("Windows")] [string] $Platform = "Windows",
    [ValidateSet("x64", "x86", "arm64")] [string] $Architecture = "x64",
    [string] $ReleaseNotes = "",
    [switch] $DryRun
)

$ErrorActionPreference = "Stop"

if (!(Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    throw "Installer file was not found: $InstallerPath"
}

$file = Get-Item -LiteralPath $InstallerPath
$allowedExtensions = @(".exe", ".msi", ".msix", ".zip")
if ($allowedExtensions -notcontains $file.Extension.ToLowerInvariant()) {
    throw "Installer extension must be one of: $($allowedExtensions -join ', ')"
}

if ($file.Name -match '[\\/<>:"|?*]') {
    throw "Installer file name contains unsafe characters."
}

if ($file.Name.Length -gt 160) {
    throw "Installer file name must be 160 characters or fewer."
}

if ($file.Name -match '(?i)(source|src|dev-ready|wpf-ready|recovered|backup)') {
    throw "Installer file name looks like a source/developer/backup package, not a customer installer."
}

if ($file.Extension.ToLowerInvariant() -eq '.zip') {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($file.FullName)
    try {
        $sourceEntries = $zip.Entries | Where-Object {
            $_.FullName -match '(?i)(\.sln$|\.csproj$|\.vbproj$|\.fsproj$|\.cs$|\.xaml$|\.resx$|\.designer\.cs$)'
        } | Select-Object -First 10
        if ($sourceEntries) {
            $examples = ($sourceEntries | ForEach-Object { $_.FullName }) -join ', '
            throw "ZIP contains source/project files and cannot be published: $examples"
        }
    }
    finally { $zip.Dispose() }
}

if ($DownloadUrl -notmatch '^https://') {
    throw "DownloadUrl must be an absolute HTTPS URL."
}

if ($ProductCode -notmatch '^[A-Z0-9_-]{1,64}$') {
    throw "ProductCode must match ^[A-Z0-9_-]{1,64}$"
}
if ($Version -notmatch '^[A-Za-z0-9][A-Za-z0-9.+-]*$' -or $Version -notmatch '[0-9]' -or $Version.Length -gt 40) {
    throw "Version must be safe, contain at least one digit, and be 40 characters or fewer."
}

if ($ReleaseNotes.Length -gt 4000) {
    throw "ReleaseNotes must be 4000 characters or fewer."
}

$hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$body = [ordered]@{
    productCode = $ProductCode.ToUpperInvariant()
    version = $Version.Trim()
    channel = $Channel
    platform = $Platform
    architecture = $Architecture
    fileName = $file.Name
    downloadUrl = $DownloadUrl.Trim()
    sha256 = $hash
    sizeBytes = $file.Length
    releaseNotes = if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) { $null } else { $ReleaseNotes.Trim() }
    publishedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
}

$json = $body | ConvertTo-Json -Depth 8
Write-Host "Verified installer artifact"
Write-Host "File: $($file.FullName)"
Write-Host "Size: $($file.Length) bytes"
Write-Host "SHA-256: $hash"
Write-Host "Download URL: $DownloadUrl"

if ($DryRun) {
    Write-Host "DryRun enabled; release metadata was not published."
    Write-Output $json
    exit 0
}
$headers = @{
    Authorization = "Bearer $BearerToken"
    "Content-Type" = "application/json"
}

$uri = "$($ApiBase.TrimEnd('/'))/api/software/admin/releases"
$response = Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -Body $json -TimeoutSec 120
Write-Host "Release published successfully."
$response | ConvertTo-Json -Depth 8
