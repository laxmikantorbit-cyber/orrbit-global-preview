# Desktop License Integration

This document describes the free-staging desktop license API flow for oRRbit AI Repair.

## Scope

The desktop app should use these APIs after a website purchase has produced a subscription id.

- Bind a subscription/license to a desktop device fingerprint.
- Receive a signed offline lease for short offline use.
- Validate an already activated device.
- Block unactivated devices.
- Enforce purchased desktop device limit.

## Staging API base

```text
https://businessos-commerce-api-live.onrender.com
```

## Authentication

Desktop activation and validation are protected endpoints.
The desktop app must send the tenant bearer token through a secure backend/channel in production.
Do not hardcode production secrets inside the desktop installer.
## Activate device

```http
POST /api/desktop/licenses/{subscriptionId}/activate
Authorization: Bearer <tenant-token>
Content-Type: application/json
```

```json
{
  "deviceFingerprint": "DESKTOP-FOFADB8-BOARD-001",
  "deviceName": "Owner PC",
  "appVersion": "3.1.108.62"
}
```

Expected success returns `allowed=true`, `reason=device_activated`, a signed `lease`, and `publicKeyBase64`.

## Validate device

```http
POST /api/desktop/licenses/{subscriptionId}/validate
Authorization: Bearer <tenant-token>
Content-Type: application/json
```
```json
{
  "deviceFingerprint": "DESKTOP-FOFADB8-BOARD-001",
  "offlineLease": {
    "algorithm": "ECDSA-P256-SHA256",
    "payloadBase64": "...",
    "signatureBase64": "..."
  }
}
```

Expected success returns `allowed=true`, `reason=license_valid`, and a refreshed lease.
If the device was never activated, it returns `allowed=false` with `reason=device_not_activated`.

## Response fields

- `subscriptionId`, `licenseId`, `productCode`
- `deviceFingerprint`, `deviceName`
- `status`, `renewalStatus`, `allowed`, `reason`
- `activeDesktopDevices`, `desktopDeviceLimit`
- `startsOn`, `validUntil`, `leaseValidUntil`
- `lease`, `publicKeyBase64`, `entitlements`
## Offline behavior

The API returns a signed lease that can be verified locally by the desktop software using `publicKeyBase64`.
The current lease window is up to 7 days, capped by the subscription expiry date.

Recommended desktop behavior:

1. On app start, validate online when internet is available.
2. Store the latest signed lease locally in encrypted app storage.
3. If internet is unavailable, verify the lease signature and device fingerprint offline.
4. If the lease is expired, show renewal/support message and block protected usage.
5. After payment renewal, validate online again to refresh the lease.

## Free staging note

The public website demo purchase endpoint is staging-only and non-production.
Production checkout must use real Razorpay payment confirmation and persistent DB.
