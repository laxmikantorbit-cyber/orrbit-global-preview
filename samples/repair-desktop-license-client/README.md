# oRRbit Repair Desktop License Client Kit

This sample is the copy/paste integration layer for the Repair Windows software.
It uses a customer activation code, binds the current machine fingerprint, and stores a short offline lease locally.

## Current staging API

```text
https://businessos-commerce-api-live.onrender.com
```

## Customer-safe API endpoints used

```text
POST /api/desktop/licenses/activate
POST /api/desktop/licenses/validate
```

No master bearer token or admin token is required inside the desktop EXE.

## Required values during staging

```text
ORRBIT_LICENSE_API=https://businessos-commerce-api-live.onrender.com
ORRBIT_ACTIVATION_CODE=<code-shown-after-buy-now>
```

Activation code format:

```text
ORR-XXXX-XXXX-XXXX-XXXX
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
Allowed=true, Reason=license_valid         -> Open software
Allowed=true, Reason=device_activated      -> Save lease and open software
Allowed=false, Reason=device_not_activated -> Show activation screen
HTTP 400 No desktop device entitlement     -> Show device limit/reset message
HTTP 404 Activation code not found         -> Ask customer to re-check activation code
```

## Offline behavior

After a successful online activation or validation, the API returns a signed lease.
This sample caches it under:

```text
C:\Users\Dell\AppData\Local\oRRbit\AI_REPAIR\License\lease.json
```

When internet/API is unavailable, EnsureLicenseOrGraceAsync() allows the app only if the cached lease is still valid.
Current lease window: 7 days or subscription expiry, whichever is earlier.

## Smoke test

```powershell
$env:ORRBIT_ACTIVATION_CODE="ORR-XXXX-XXXX-XXXX-XXXX"
dotnet run --project .\Orrbit.RepairDesktopLicenseClient -- --activate
dotnet run --project .\Orrbit.RepairDesktopLicenseClient
```
