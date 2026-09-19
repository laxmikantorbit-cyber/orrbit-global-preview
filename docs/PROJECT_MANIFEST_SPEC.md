# Project Manifest Specification

Each managed project has a versioned manifest.

Required concepts:
- id, name, type and lifecycle status
- source repository and default branch
- environments: development, staging, production
- frontend provider/config
- backend services
- database provider/type
- storage provider
- auth configuration
- payment configuration
- domains/DNS ownership
- health endpoints/checks
- project business rules
- design-system references
- deployment policy
- budget policy

Provider-specific fields live under adapter configuration and must not leak into core domain logic.