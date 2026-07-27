# Sources of Truth

## Canonical Sources

The following are considered **authoritative sources of truth** for information about the Users Service. When conflicts arise between sources, higher-precedence sources take priority.

## Precedence Hierarchy

| Priority | Source | Scope | Authority |
|---|---|---|---|
| **1 (Highest)** | `openapi.yaml` | API contract | **Normative** — the API IS what the spec says |
| **2** | `catalog-info.yaml` | Entity metadata | **Normative** — ownership, dependencies, lifecycle, system/domain membership |
| **3** | `docs/architecture/*.md` | System design | **Authoritative** — written by architects, reviewed by team |
| **4** | `docs/adr/*.md` | Decisions | **Authoritative** — records of accepted architectural decisions (e.g., PostgreSQL over MongoDB, dual JWT validation) |
| **5** | `docs/api/*.md` | API documentation | **Informative** — derived from OpenAPI, adds narrative |
| **6** | `docs/runbooks/*.md` | Operations | **Authoritative** — written by SRE, validated in production |
| **7** | `docs/decisions/*.md` | Standards | **Normative** — these ARE the rules |
| **8** | `docs/onboarding/*.md` | Guides | **Informative** — helpful but may lag behind code |
| **9** | `README.md` | Overview | **Informative** — entry-level summary |
| **10 (Lowest)** | Source code (`src/`) | Implementation | **Informative** — the code does what the code does, but the spec is the contract |

## Conflict Resolution

When two sources disagree:

1. The **higher-precedence** source is considered correct
2. File an issue to reconcile the discrepancy
3. Label it `documentation-gap` or `spec-implementation-gap`

## External Sources of Truth

| Source | Scope | Relationship |
|---|---|---|
| **Azure AD / Entra ID** | Corporate identity | Master for employee existence, department, job title, manager hierarchy. The Users Service enriches profiles from this source nightly |
| **Authentication Service** | JWT issuance | Master for access tokens, refresh tokens, and session state. Users Service validates JWTs against this service |
| **Azure Key Vault** | Secrets and keys | Master for PostgreSQL connection strings, Service Bus connection strings, and gRPC client certificates |
| **PostgreSQL (Users DB)** | User profiles | Runtime source of truth for user profile data, role assignments, and audit logs. Backed up nightly, point-in-time restore enabled |
| **Backstage Catalog** | Consolidated entity registry | Aggregate view — fed by individual `catalog-info.yaml` files |
| **Microsoft Graph API** | Entra ID enrichment | Source of truth for corporate directory attributes (department, office location, manager, profile photo) |

## Specific Precedence Notes for Users Service

| Scenario | Precedence Rule |
|---|---|
| RBAC role definitions | `docs/architecture/security.md` takes precedence over `docs/api/users-api.md` |
| Endpoint contract | `openapi.yaml` takes precedence over `docs/api/users-api.md` |
| Dependency declaration | `catalog-info.yaml` `spec.dependsOn` takes precedence over architecture markdown |
| Event schema | `docs/api/events.md` takes precedence (single source of truth for event payloads) |
| Data retention policy | `docs/architecture/security.md` (PII Handling section) is the canonical source |
| Technology versions | `docs/architecture/technology-stack.md` takes precedence over `README.md` |

## Related Documents

- [Document Priority](document-priority.md)
- [Indexing Strategy](indexing-strategy.md)
