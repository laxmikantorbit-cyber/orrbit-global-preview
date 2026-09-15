param(
    [string]$ApiBaseUrl = "https://businessos-commerce-api-live.onrender.com",
    [Parameter(Mandatory = $true)]
    [string]$BearerToken
)

$ErrorActionPreference = "Stop"
$base = $ApiBaseUrl.TrimEnd('/')
$headers = @{
    Authorization = "Bearer $BearerToken"
    "Content-Type" = "application/json"
}

function Assert-StatusCode {
    param($Response, [int]$Expected, [string]$Step)
    if ([int]$Response.StatusCode -ne $Expected) {
        throw "$Step failed. Expected HTTP $Expected, got $([int]$Response.StatusCode). Body: $($Response.Content)"
    }
}

Write-Host "BusinessOS free-staging smoke started for $base"
$health = Invoke-WebRequest -Uri "$base/health" -UseBasicParsing
Assert-StatusCode $health 200 "Health"
$healthJson = $health.Content | ConvertFrom-Json
if ($healthJson.status -ne "ok") { throw "Health returned unexpected status: $($health.Content)" }
Write-Host "PASS health"
