# oRRbit Repair Desktop License Client Kit

This sample is the copy/paste integration layer for the Repair Windows software.
It calls the BusinessOS desktop license API and stores a short offline lease locally.

## Current staging API

```text
https://businessos-commerce-api-live.onrender.com
```

## API endpoints used

```text
POST /api/desktop/licenses/{subscriptionId}/activate
POST /api/desktop/licenses/{subscriptionId}/validate
```

## Required values during staging

Do not hardcode real secrets inside the desktop EXE.
For staging smoke testing, set these environment variables or store them in an encrypted local config:

```text
ORRBIT_LICENSE_API=https://businessos-commerce-api-live.onrender.com
ORRBIT_SUBSCRIPTION_ID=<subscription-id-from-buy-now-flow>
ORRBIT_LICENSE_TOKEN=<staging-token-from-secure-config>
```

## WinForms startup wiring

Call this before opening the main dashboard form:

```csharp
var settings = DesktopLicenseSettings.FromEnvironment();
var client = new OrrbitLicenseClient(settings);

if (!await client.EnsureLicenseOrGraceAsync())
{
    MessageBox.Show("License expired or this device is not activated.");
    Application.Exit();
    return;
}

Application.Run(new frmDashboard());
```

## First-time activation button

```csharp
var result = await client.ActivateAsync(Environment.MachineName, Application.ProductVersion);
if (result.Allowed)
{
    MessageBox.Show("Software activated successfully.");
}
else
{
    MessageBox.Show(result.Reason);
}
```

## Result handling rules

```text
Allowed=true, Reason=license_valid       -> Open software
Allowed=true, Reason=device_activated    -> Save lease and open software
Allowed=false, Reason=device_not_activated -> Show activation screen
HTTP 400 No desktop device entitlement   -> Show device limit/reset message
HTTP 401                                 -> Secure API token/config issue
```

## Offline behavior

After a successful online activation or validation, the API returns a signed lease.
This sample caches it under:

```text
%LOCALAPPDATA%\oRRbit\AI_REPAIR\License\lease.json
```

When internet/API is unavailable, `EnsureLicenseOrGraceAsync()` allows the app only if the cached lease is still valid.
Current lease window: 7 days or subscription expiry, whichever is earlier.
