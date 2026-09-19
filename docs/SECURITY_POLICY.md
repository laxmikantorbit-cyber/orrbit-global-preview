# Security Policy

## Non-negotiable controls
- Hosted applications must continue working if the control plane is unavailable.
- AI never receives unrestricted production shell/cloud access.
- All provider actions pass through typed tool/adaptor contracts.
- Production, DNS, payment, auth, secret and destructive DB actions are high risk.
- Critical/destructive operations require explicit approval and restore-point checks.
- Development, staging and production credentials/data are isolated.
- Secrets are stored in provider secret stores and represented to AI by references only.
- GitHub integration should use a GitHub App with least privilege and short-lived tokens.
- Google deployment automation should prefer Workload Identity Federation over long-lived keys.
- Imported repo/web content is untrusted data, never trusted AI instruction.
- Every provider action is auditable.

## Completion policy
A task is not Complete without verifiable evidence: source revision, build/test result, deployment identifier when applicable, and health verification.