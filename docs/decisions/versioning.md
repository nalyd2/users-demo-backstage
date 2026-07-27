# Versioning Strategy — Users Service

- **Status:** Approved
- **Owner:** Platform Engineering Team
- **Last Updated:** 2026-07-20

## Overview

This document defines the versioning strategy for all artifacts produced by the Users Service. It follows Semantic Versioning 2.0.0 with adaptations for the users domain, including API versioning for user management endpoints.

## MAJOR.MINOR.PATCH Rules

### PATCH — Incremented When

- Backward-compatible bug fixes in user CRUD operations, tenant management, or event processing.
- Security patches for data access or Graph API integration.
- Dependency updates (NuGet packages, base container images).
- Observability improvements (logging, metrics, tracing).

### MINOR — Incremented When

- New user management endpoints (backward-compatible).
- New event types published or consumed.
- Additional profile fields in user responses.
- New RBAC roles or permission scopes.
- Deprecation warnings for existing features.
- Configuration options with safe defaults (disabled by default).

### MAJOR — Incremented When

- Breaking changes to user schema or response format.
- Removal of deprecated endpoints or fields.
- Changes to soft-delete semantics.
- Breaking changes to published event schemas.
- Database migrations that are not backward-compatible.
- Dropping support for a previously supported API version.

### Pre-release Labels

| Label | Use |
|---|---|
| `-alpha.N` | Internal development, API unstable |
| `-beta.N` | Feature-complete for specific feature, only bug fixes before GA |
| `-rc.N` | Release candidate for QA validation |

## API Versioning

The Users Service API uses URL path versioning:

```
https://users.example.com/api/v1/users
https://users.example.com/api/v2/users
```

### Rules

- Version prefix applies to entire API surface (`/api/v1/`, `/api/v2/`).
- Support at most two MAJOR versions simultaneously.
- Previous version receives security patches for minimum 6 months after deprecation.
- Internal endpoints (health, metrics, probes) are not versioned.

### Version Lifecycle

| Phase | Behavior |
|---|---|
| **Active** | Full support, bug fixes, security patches |
| **Deprecated** | Still served with `Sunset` header, consumers encouraged to migrate |
| **Sunset** | Returns `410 Gone`, migration guide remains available |

## Changelog

Every release MUST include an entry in `CHANGELOG.md` following Keep a Changelog format:

```markdown
## [v2.3.0] - 2026-06-15

### Added
- Microsoft Graph profile enrichment endpoint: `GET /api/v2/users/{id}/graph-profile`. (#142)
- Event processing lag metric for auth event consumers. (#155)

### Changed
- Upgrade from .NET 9 to .NET 10. (#168)

### Deprecated
- `GET /api/v1/users` (non-paginated). Use `GET /api/v2/users` with cursor pagination. (#150)
  Support will be removed in v3.0.0.

### Fixed
- Soft-delete filter missing from tenant-scoped user queries. (#89)
```

## Deprecation Policy

- Features deprecated for at least one full MAJOR version cycle before removal.
- Deprecated features return `Sunset` and `Deprecation` headers.
- Deprecation announced in changelog and API reference documentation.
- Security vulnerabilities may be removed without standard deprecation period.

## Container Image Tagging

- `v<MAJOR>.<MINOR>.<PATCH>` — Immutable release tag.
- `v<MAJOR>.<MINOR>` — Mutable, updated with each patch.
- `v<MAJOR>` — Mutable, updated with latest in that major series.
- `latest` — Mutable, always latest stable release.
- `sha-<commit-sha>` — Immutable per-commit tag.
