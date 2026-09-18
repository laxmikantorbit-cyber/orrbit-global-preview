[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $InstallerPath,
    [Parameter(Mandatory=$true)] [string] $Version,
    [string] $ProductCode = 'AI_REPAIR',
    [ValidateSet('Stable','Beta','Internal')] [string] $Channel = 'Stable',
    [ValidateSet('Windows')] [string] $Platform = 'Windows',
    [ValidateSet('x64','x86','arm64')] [string] $Architecture = 'x64',
    [string] $ArtifactRoot = '.release-artifacts',
    [string] $BaseDownloadUrl,
    [string] $ExpectedSha256,
    [string] $ReleaseNotes,
    [switch] $AllowSourceZip
)

$ErrorActionPreference = 'Stop'
$allowedExtensions = @('.exe','.msi','.msix','.zip')

function Fail([string] $Message) {
    throw "Installer artifact rejected: $Message"
}

$resolved = Resolve-Path -LiteralPath $InstallerPath -ErrorAction Stop
$item = Get-Item -LiteralPath $resolved
if ($item.PSIsContainer) { Fail 'path must be a file' }
$extension = [IO.Path]::GetExtension($item.Name).ToLowerInvariant()
if ($allowedExtensions -notcontains $extension) {
    Fail "extension '$extension' is not allowed. Use .exe, .msi, .msix or .zip."
}

if ($item.Name -match '[<>:"|?*\\/]') { Fail 'filename contains unsafe characters' }
if ($item.Name -match '(?i)(source|src|dev-ready|wpf-ready|recovered|backup)') {
    Fail 'filename looks like a source/developer/backup package, not a customer installer'
}

if ($ProductCode -notmatch '^[A-Z0-9_-]{1,64}$') {
    Fail 'product code must match ^[A-Z0-9_-]{1,64}$'
}
if ($Version -notmatch '^[A-Za-z0-9][A-Za-z0-9.+-]*$' -or $Version.Length -gt 40 -or $Version -notmatch '[0-9]') {
    Fail 'version must be safe, 40 chars or less, and contain a number'
}

if ($extension -eq '.zip' -and -not $AllowSourceZip) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($item.FullName)
    try {
        $sourceEntries = $zip.Entries | Where-Object {
            $_.FullName -match '(?i)(\.sln$|\.csproj$|\.vbproj$|\.fsproj$|\.cs$|\.xaml$|\.resx$|\.designer\.cs$)'
        } | Select-Object -First 10
        if ($sourceEntries) {
            $examples = ($sourceEntries | ForEach-Object { $_.FullName }) -join ', '
            Fail "ZIP contains source/project files: $examples"
        }
    }
    finally { $zip.Dispose() }
}

$hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
if ($ExpectedSha256 -and $hash -ne $ExpectedSha256.ToLowerInvariant()) {
    Fail "SHA-256 mismatch. Expected $ExpectedSha256 but calculated $hash"
}

$targetDirectory = Join-Path $ArtifactRoot (Join-Path $ProductCode (Join-Path $Version "$Channel-$Platform-$Architecture"))
New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
$targetFile = Join-Path $targetDirectory $item.Name
Copy-Item -LiteralPath $item.FullName -Destination $targetFile -Force

$downloadUrl = $null
if ($BaseDownloadUrl) {
    if ($BaseDownloadUrl -notmatch '^https://') { Fail 'BaseDownloadUrl must be HTTPS' }
    $downloadUrl = ($BaseDownloadUrl.TrimEnd('/') + '/' + [Uri]::EscapeDataString($item.Name))
}

$metadata = [ordered]@{
    productCode = $ProductCode
    version = $Version
    channel = $Channel
    platform = $Platform
    architecture = $Architecture
    fileName = $item.Name
    sha256 = $hash
    sizeBytes = $item.Length
    releaseNotes = $(if ($ReleaseNotes) { $ReleaseNotes } else { $null })
    publishedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    localArtifactPath = (Resolve-Path -LiteralPath $targetFile).Path
    downloadUrl = $downloadUrl
}

$metadataPath = Join-Path $targetDirectory 'release-metadata.json'
$metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $metadataPath -Encoding UTF8

Write-Host 'Installer artifact staged successfully'
Write-Host "Artifact: $targetFile"
Write-Host "Metadata: $metadataPath"
Write-Host "SHA-256: $hash"
if ($downloadUrl) { Write-Host "Download URL: $downloadUrl" }
else { Write-Host 'Download URL not set yet. Upload artifact first, then publish with the final HTTPS URL.' }
