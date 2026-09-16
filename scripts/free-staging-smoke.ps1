param(
    [string]$ApiBaseUrl = "https://businessos-commerce-api-live.onrender.com",
    [Parameter(Mandatory = $true)]
    [string]$BearerToken,
    [ValidateSet("InMemory", "Postgres")]
    [string]$ExpectedStorageMode = "InMemory"
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
if ($readyJson.storageMode -ne $ExpectedStorageMode) { throw "Unexpected storage mode. Expected $ExpectedStorageMode. Body: $($ready.Content)" }
if ($readyJson.paymentsMode -ne "RazorpayTestPending") { throw "Unexpected payments mode: $($ready.Content)" }
Write-Host "PASS readiness"

$checkoutPayload = @{
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
} | ConvertTo-Json
$checkout = Invoke-WebRequest `
    -Uri "$base/api/commerce/checkout/initial" `
    -Method POST `
    -Headers $headers `
    -Body $checkoutPayload `
    -UseBasicParsing
Assert-StatusCode $checkout 200 "Initial checkout"
$checkoutJson = $checkout.Content | ConvertFrom-Json
if (-not $checkoutJson.razorpayOrderId.StartsWith("order_free_test_")) {
    throw "Unexpected simulated order id: $($checkout.Content)"
}
if ($checkoutJson.razorpayKeyId -ne "rzp_test_free_testing") {
    throw "Unexpected test key id: $($checkout.Content)"
}
Write-Host "PASS initial checkout"

$capturePayload = @{
    paymentId = "pay_free_smoke_" + [Guid]::NewGuid().ToString("N")
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
} | ConvertTo-Json
$capture = Invoke-WebRequest `
    -Uri "$base/api/testing/payments/razorpay/orders/$($checkoutJson.razorpayOrderId)/capture" `
    -Method POST `
    -Headers $headers `
    -Body $capturePayload `
    -UseBasicParsing
Assert-StatusCode $capture 200 "Simulated payment capture"
$captureJson = $capture.Content | ConvertFrom-Json
if ($captureJson.paymentOutcome -ne "provider_payment_captured") {
    throw "Unexpected payment outcome: $($capture.Content)"
}
if ($captureJson.activationOutcome -ne "activated") {
    throw "Unexpected activation outcome: $($capture.Content)"
}
if (-not $captureJson.initialActivation.subscriptionId) {
    throw "Capture did not return activated subscription: $($capture.Content)"
}
Write-Host "PASS simulated payment capture"

$subscriptionId = $captureJson.initialActivation.subscriptionId
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
    throw "Admin status does not show an active subscription after capture: $($admin.Content)"
}
Write-Host "PASS admin status"

Write-Host "BusinessOS free-staging smoke completed successfully."
Write-Host "SubscriptionId: $subscriptionId"
