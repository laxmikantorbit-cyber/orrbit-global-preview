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

$ready = Invoke-WebRequest -Uri "$base/health/ready" -UseBasicParsing
Assert-StatusCode $ready 200 "Readiness"
$readyJson = $ready.Content | ConvertFrom-Json
if ($readyJson.ready -ne $true) { throw "Readiness is not true: $($ready.Content)" }
if ($readyJson.deploymentMode -ne "FreeTesting") { throw "Unexpected deployment mode: $($ready.Content)" }
if ($readyJson.storageMode -ne "InMemory") { throw "Unexpected storage mode: $($ready.Content)" }
Write-Host "PASS readiness"
$paymentId = "stg_pay_" + [Guid]::NewGuid().ToString("N")
$activationPayload = @{
    organisationId = "11111111-1111-1111-1111-111111111111"
    productCode = "AI_REPAIR"
    planId = $null
    planVersionId = $null
    planVersionNumber = 1
    amount = 29999
    currencyCode = "INR"
    termMonths = 12
    desktopDeviceLimit = 1
    locationLimit = 1
    webAdminSeats = 10
    fieldStaffSeats = 10
    multiLocationCloud = $true
    paymentId = $paymentId
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
} | ConvertTo-Json
$activation = Invoke-WebRequest `
    -Uri "$base/api/commerce/activations/initial" `
    -Method POST `
    -Headers $headers `
    -Body $activationPayload `
    -UseBasicParsing
Assert-StatusCode $activation 200 "Initial activation"
$activationJson = $activation.Content | ConvertFrom-Json
if (-not $activationJson.subscriptionId) { throw "Activation did not return subscriptionId: $($activation.Content)" }
if ($activationJson.productCode -ne "AI_REPAIR") { throw "Unexpected product code: $($activation.Content)" }
Write-Host "PASS initial activation"

$subscriptionId = $activationJson.subscriptionId
$subscription = Invoke-WebRequest `
    -Uri "$base/api/commerce/subscriptions/$subscriptionId" `
    -Headers $headers `
    -UseBasicParsing
Assert-StatusCode $subscription 200 "Subscription lookup"
Write-Host "PASS subscription lookup"
$admin = Invoke-WebRequest `
    -Uri "$base/api/commerce/admin/status" `
    -Headers $headers `
    -UseBasicParsing
Assert-StatusCode $admin 200 "Admin status"
$adminJson = $admin.Content | ConvertFrom-Json
if ($adminJson.counts.activeSubscriptions -lt 1) {
    throw "Admin status does not show an active subscription after smoke activation: $($admin.Content)"
}
Write-Host "PASS admin status"

Write-Host "BusinessOS free-staging smoke completed successfully."
Write-Host "SubscriptionId: $subscriptionId"
