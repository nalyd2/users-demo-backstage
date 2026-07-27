# Glossary — Users Service

- **Status:** Approved
- **Owner:** Platform Engineering Team
- **Last Updated:** 2026-07-20

## Overview

This glossary defines terminology used throughout the Users Service documentation, codebase, and operational processes. Terms reflect the user management domain, multi-tenancy, RBAC, and event-driven architecture.

---

### Actor

The identity (user or service principal) that performed an operation. Every mutation in the Users Service is recorded with an actor identity extracted from the JWT `sub` claim. The actor is distinct from the target user (the subject of the operation). Audit logs always capture both actor and target.

### Auth Events

Events published by the Auth Service to Azure Service Bus topic `auth-events`. The Users Service consumes `user.login`, `user.logout`, and `token.revoked` events to update user session state and trigger lifecycle actions. Auth events carry a trace context for end-to-end distributed tracing across service boundaries.

### Cursor Pagination

A pagination technique that uses an opaque cursor (a base64-encoded pointer to a specific record) rather than page numbers or offsets. The Users Service uses cursor-based pagination for all list endpoints. Cursor pagination offers consistent results even when data changes between pages, avoids the performance cost of offset-based pagination for large datasets, and prevents resource enumeration attacks.

```json
{
  "data": [...],
  "pagination": {
    "next_cursor": "eyJpZCI6ICIxMjM0In0=",
    "prev_cursor": null,
    "has_more": true
  }
}
```

### Entra ID Profile Enrichment

The asynchronous process of enriching user profiles with data from Microsoft Graph API. When a user is created, the Users Service attempts to fetch additional profile attributes (department, job title, manager, office location, profile photo) from Entra ID using the Microsoft Graph API. Enrichment is non-blocking: user creation succeeds even if the Graph API is unavailable. Enriched data is cached and refreshed on a configurable schedule.

### Hard-Delete

Permanent removal of a user record from the database. The Users Service does not expose a public hard-delete API (see known-limitations.md). Hard-delete is performed by a scheduled purge job that permanently removes users that have been soft-deleted for more than 90 days (the retention period is configurable). Hard-delete is irreversible and is logged as a critical audit event.

### Idempotent Event Processing

The property that processing the same event multiple times produces the same result as processing it once. The Users Service implements idempotent event processing by tracking processed event IDs in a deduplication store (Redis). If the same auth event is delivered twice (Azure Service Bus at-least-once delivery), the second attempt is silently ignored. This ensures consistency even during consumer restarts or network disruptions.

### JWT Validation (Users Service Context)

The process of verifying that an incoming JWT was issued by the Auth Service and has not been tampered with. The Users Service validates JWTs by: (1) fetching the Auth Service's public keys from the JWKS endpoint, (2) verifying the JWT signature using the key identified by the `kid` header, (3) checking the JWT expiry (`exp`), (4) validating the `aud` claim matches the Users Service audience, and (5) verifying the JWT has not been revoked (via the token blacklist). JWKS keys are cached for 5 minutes to minimize dependency on the Auth Service.

### Multi-Tenancy

An architecture where a single Users Service instance serves multiple tenants (organizations or customers) with strict data isolation. Each user record includes a `tenant_id` field that identifies the owning tenant. All queries in the repository layer include a `WHERE tenant_id = @tenantId` filter to prevent cross-tenant data access. The tenant ID is extracted from the JWT claims (not from request parameters) to prevent tenant spoofing.

### RBAC (Role-Based Access Control)

The authorization model used by the Users Service. Three built-in roles define the permission hierarchy:

| Role | Permissions |
|---|---|
| **Admin** | Full access: create/read/update/delete users, manage roles, manage tenant configuration, view audit logs |
| **Operator** | Operational access: create/read/update users (cannot delete, cannot manage roles or tenant config) |
| **User** | Self-service access: read own profile, update own profile (future), view own roles |

Roles are enforced at the middleware level via the `[Authorize(Roles = "...")]` attribute and validated against the JWT `roles` claim. Custom roles with granular permission sets are planned for Q3 2026.

### Role Hierarchy

The relationship between RBAC roles where a higher-privilege role inherits the permissions of lower-privilege roles. In the Users Service: Admin inherits Operator permissions, Operator inherits User permissions. Role hierarchy is enforced in the authorization middleware and is not user-configurable in the current implementation.

### Session State

The record of whether a user is currently "active" (has an active login session) or "inactive" (logged out or session expired). Session state is derived from auth events (login sets state to active, logout sets state to inactive) and stored in the Users Service database. Session state is used for authorization decisions (e.g., deny access to inactive users) and for reporting (e.g., active user counts per tenant).

### Soft-Delete

A deletion pattern where records are marked as deleted (a `deleted_at` timestamp is set) rather than physically removed from the database. Soft-deleted users are excluded from all query results by default (queries include `WHERE deleted_at IS NULL`). Soft-delete allows data recovery within the retention window (90 days) and provides an audit trail of deletion events. A scheduled purge job permanently removes soft-deleted users after the retention period expires.

### Tenant Isolation

The enforcement mechanism that prevents users in one tenant from accessing data belonging to another tenant. The Users Service implements tenant isolation at multiple layers:

1. **Authentication layer:** Tenant ID is embedded in the JWT by the Auth Service and cannot be modified by the client.
2. **Repository layer:** Every database query includes a `tenant_id` filter parameter.
3. **Database layer (defense-in-depth):** PostgreSQL Row-Level Security (RLS) policies enforce tenant isolation at the database level, providing protection even if the application layer is bypassed.
4. **Testing:** Automated integration tests verify that cross-tenant access always returns 403 Forbidden.

### Tenant

An isolated organizational unit within the Users Service. Each tenant has its own users, roles, configuration settings, and feature flags. Tenants are identified by a UUID `tenant_id` that scopes all operations. Tenants are created via the tenant management API (see roadmap.md).

### User Events

Events published by the Users Service to Azure Service Bus topic `user-events`. These events notify downstream services about user lifecycle changes: `user.created`, `user.updated`, `user.deleted`, `user.restored`. Each event includes the user ID, actor ID, timestamp, correlation ID, and a payload of changed fields. Consumers include the Audit Service (for audit logging), Notification Service (for email/Slack notifications), and Auth Service (for token invalidation on user deletion).

### User Profile

The collection of user attributes stored and managed by the Users Service. The user profile includes core identity fields (user ID, email, first name, last name), organizational fields (tenant ID, roles, department, job title, manager), and system fields (status, created_at, updated_at, deleted_at, last_login_at).

### UUIDv4

Universally Unique Identifier version 4 — a 128-bit identifier generated using random numbers. The Users Service uses UUIDv4 for all resource identifiers (user IDs, tenant IDs, role IDs). UUIDv4 identifiers are non-sequential and cannot be enumerated, providing protection against resource guessing attacks.

### Vertical Scaling

Scaling by increasing the resources (CPU, memory) of existing instances rather than adding more instances. The Users Service uses horizontal scaling (adding replicas), but vertical scaling is available as an operational contingency for unexpected load spikes until horizontal scaling catches up.
