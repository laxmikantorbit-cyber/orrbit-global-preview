# Verified Windows Installer Publishing

This flow publishes a real Windows installer to the BusinessOS customer portal only after the artifact has a verified checksum and a controlled HTTPS download URL.

## Required inputs

- Real installer file: `.exe`, `.msi`, `.msix`, or `.zip`
- Final HTTPS download URL where customers will download the same file
- Commerce Admin bearer token
- Product code, version, channel, platform, architecture

Do not publish source ZIPs, temporary builds, unsigned experiments, or files from an untrusted location.

## Calculate and publish from PowerShell

```powershell
.\scripts\publish-verified-software-release.ps1 `
  -InstallerPath "C:\Builds\oRRbit-AI-Repair-1.0.0.exe" `
  -DownloadUrl "https://downloads.orrbit.in/ai-repair/oRRbit-AI-Repair-1.0.0.exe" `
  -Version "1.0.0" `
  -BearerToken "<COMMERCE_ADMIN_TOKEN>" `
  -Channel Stable `
  -Platform Windows `
  -Architecture x64 `
  -ReleaseNotes "Initial verified installer release"
```
## Dry run

Use `-DryRun` before publishing. It prints the exact release JSON but does not call the API.

```powershell
.\scripts\publish-verified-software-release.ps1 `
  -InstallerPath "C:\Builds\oRRbit-AI-Repair-1.0.0.exe" `
  -DownloadUrl "https://downloads.orrbit.in/ai-repair/oRRbit-AI-Repair-1.0.0.exe" `
  -Version "1.0.0" `
  -BearerToken "dry-run-token" `
  -DryRun
```

## Customer visibility rule

The portal shows a download button only when all conditions are true:

1. Subscription belongs to the authenticated tenant.
2. Subscription status is Active or Grace.
3. Plan includes desktop entitlement.
4. Active release exists for product/channel/platform/architecture.
5. Release metadata passes API and database validation.

Until these are true, customers see a safe unavailable message and no raw download URL.
