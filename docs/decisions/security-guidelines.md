# Security Guidelines — Users Service

- **Status:** Approved
- **Owner:** Platform Engineering Team / Security Champion
- **Last Updated:** 2026-07-20

## Scope

This document defines the security standards for the Users Service, which manages user profiles, tenant data, and RBAC roles. As a Tier 1 service containing Personally Identifiable Information (PII), it has distinct security requirements beyond the Auth Service.

## OWASP Top 10 Mitigations

### A01: Broken Access Control

- All endpoints require a valid JWT from the Auth Service, validated via JWKS signature verification.
- RBAC (Admin, Operator, User roles) enforced at middleware level for every endpoint.
- Tenant isolation: every query includes `tenant_id = {user_tenant}` enforced in the repository layer.
- Soft-delete prevents unauthorized permanent data destruction; only Admin role can hard-delete.
- Cursor pagination prevents resource enumeration via sequential IDs (UUIDv4 used for all resource identifiers).

### A02: Cryptographic Failures

- All PII fields (email, phone, address) encrypted at rest using AES-256-GCM in the database.
- Sensitive fields logged only as hashes (never plaintext).
- All data in transit uses TLS 1.3.
- Passwords are never stored in the Users Service; authentication is entirely delegated to the Auth Service.

### A03: Injection

- All SQL queries use Entity Framework Core with parameterized queries.
- Graph API queries use the Microsoft Graph SDK with parameterized request builders.
- Input validation on all DTOs using FluentValidation.

### A04: Insecure Design

- New features require security review with focus on tenant isolation and PII handling.
- Rate limiting at API gateway for user enumeration prevention.
- Bulk operations limited to 1000 records per request with configurable throttle.

### A05: Security Misconfiguration

- All configuration from Azure App Configuration with Key Vault references.
- Container images scanned (Mend) before deployment.
- HTTP security headers set on all responses.
- Graph API permissions follow least-privilege: only `User.Read.All` and `User.ReadWrite.All` as needed.

### A06: Vulnerable and Outdated Components

- Mend scanning on every PR; CVSS 7.0+ blocks merge.
- NuGet packages pinned with lock file validation.
- Weekly base image rebuild with latest OS patches.
- SBOM generated for every release.

### A07: Identification and Authentication Failures

- Authentication fully delegated to Auth Service via JWT validation.
- Session information received via auth events (login/logout).
- No local authentication implemented; no credential storage.

### A08: Software and Data Integrity Failures

- All CI/CD artifacts signed and verified.
- Published user events include schema version and correlation ID.
- Consumed auth events validated for schema compliance before processing.

### A09: Security Logging and Monitoring

- All user mutations logged with before/after state for audit.
- Access to PII fields logged separately in the audit trail.
- Event processing failures logged with full context for replay.
- Logs retained for minimum 1 year for compliance.

### A10: Server-Side Request Forgery

- All outbound HTTP (Graph API) restricted to `graph.microsoft.com` and `login.microsoftonline.com`.
- HTTP clients use restricted redirect policies.
- All outbound requests include timeout and cancellation token.

## Secret Management

- No secrets in code; all secrets in Azure Key Vault accessed via Managed Identity.
- Per-environment Key Vault instances.
- Graph API client secret stored in Key Vault with auto-rotation.
- Database connection strings stored in Key Vault; never in configuration files.

## PII Data Classification

| Data Field | Classification | Encryption | Logging |
|---|---|---|---|
| Email | PII | AES-256-GCM at rest | Hashed only |
| First Name | PII | AES-256-GCM at rest | Never logged |
| Last Name | PII | AES-256-GCM at rest | Never logged |
| Phone Number | PII | AES-256-GCM at rest | Never logged |
| Address | PII | AES-256-GCM at rest | Never logged |
| User ID (UUID) | Internal | None | Full value |
| Tenant ID (UUID) | Internal | None | Full value |
| Roles | Internal | None | Full value |

## Dependency Scanning (Mend)

- All PRs scanned; CVSS 7.0+ blocks merge.
- Daily full scan against all dependencies.
- CVSS 9.0+ alerts trigger immediate notification.
- Remediation SLA: CVSS 9.0+ within 24 hours, 7.0-8.9 within 7 days, 4.0-6.9 within 30 days.
- Graph API SDK dependencies monitored for breaking changes.

## SAST (SonarQube)

- Quality Gate on every PR: coverage >= 80%, no critical/blocker issues.
- Security hotspots reviewed by security champion every sprint.
- Custom rules: no hardcoded credentials, no PII in log messages, tenant filter validation on queries.

## Threat Modeling

- **Cadence:** Quarterly full service review; per-feature for new capabilities.
- **Focus areas:** Tenant isolation bypass, PII data leakage, event processing integrity, Graph API token misuse.
- **Tool:** OWASP Threat Dragon.
- **Output:** Threat model document stored in `docs/security/threat-models/`.

## Penetration Testing

- **Frequency:** Annual full-scope pen test by external third party.
- **Scope:** All user management endpoints, tenant isolation, RBAC enforcement, PII data handling.
- **Remediation:** Critical findings within 48 hours, High within 14 days, Medium within 60 days.

## Compliance

- The Users Service must comply with SOC 2 Type II, ISO 27001, GDPR, and CCPA.
- Data retention enforced: users are soft-deleted (retained for 90 days) then purged.
- Right to erasure (GDPR Article 17) supported via hard-delete API for Admin role.
- Data portability (GDPR Article 20) supported via user data export endpoint.
