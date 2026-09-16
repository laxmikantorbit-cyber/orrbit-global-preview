# Desktop License Integration - AI Repair

This document describes the customer-safe desktop licensing flow for oRRbit AI Repair.

## Final customer flow

```text
Website Buy Now
-> Payment captured
-> Subscription and license created
-> Activation code generated
-> Customer enters activation code in Repair desktop software
-> Desktop sends activation code + machine fingerprint
-> API binds device and returns signed offline lease
-> Desktop stores lease locally and starts software
```

The desktop EXE must not contain a master/admin bearer token.

## Public desktop endpoints

```text
POST /api/desktop/licenses/activate
POST /api/desktop/licenses/validate
```

These endpoints accept an activation code and do not require a bearer token. The activation code resolves the tenant/subscription internally.

## Protected internal/admin endpoints

```text
POST /api/desktop/licenses/{subscriptionId}/activation-code
POST /api/desktop/licenses/{subscriptionId}/activate
POST /api/desktop/licenses/{subscriptionId}/validate
```

## Activate request

```json
{
  "activationCode": "ORR-XXXX-XXXX-XXXX-XXXX",
  "deviceFingerprint": "machine-fingerprint",
  "deviceName": "Owner PC",
  "appVersion": "3.1.108.62",
  "currentLease": null
}
```

Expected result:

```text
Allowed=true
Reason=device_activated
Status=Active
Lease=<signed lease payload>
PublicKeyBase64=<license public key>
```

## Validate request

```json
{
  "activationCode": "ORR-XXXX-XXXX-XXXX-XXXX",
  "deviceFingerprint": "machine-fingerprint",
  "deviceName": null,
  "appVersion": null,
  "currentLease": { }
}
```

## Repair desktop startup logic

1. Load cached offline lease from `C:\Users\Dell\AppData\Local\\oRRbit\\AI_REPAIR\\License\\lease.json`.
2. Try online validation using activation code and current machine fingerprint.
3. If validation succeeds, overwrite cache with new signed lease.
4. If API/internet fails, allow only when cached lease is still valid.
5. If license is expired, device is not activated, or device limit is full, show activation/support screen.

## Sample client

Ready-to-copy C# sample:

```text
samples/repair-desktop-license-client/
```

Environment variables for smoke test:

```text
ORRBIT_LICENSE_API=https://businessos-commerce-api-live.onrender.com
ORRBIT_ACTIVATION_CODE=ORR-XXXX-XXXX-XXXX-XXXX
```

## Production DB tables

```text
commerce_license_activation_codes
commerce_desktop_device_activations
```
