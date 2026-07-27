# Roadmap — Users Service

- **Status:** Approved
- **Owner:** Platform Engineering Team
- **Last Updated:** 2026-07-20

## Overview

This document defines the planned feature roadmap for the Users Service through Q1 2027. Features are organized by quarter with milestones, success criteria, and dependencies.

---

## Q3 2026 (July — September)

### 1. Microsoft Graph API Profile Enrichment

**Description:** Integrate with Microsoft Graph API to enrich user profiles with data from Entra ID, including profile photo, department, manager, job title, and office location. Enrichment runs asynchronously on user creation and on a scheduled refresh cycle.

**Milestones:**

| Milestone | Target Date | Deliverable |
|---|---|---|
| Design review | 2026-07-25 | Integration architecture, permission model |
| Graph API client | 2026-08-10 | Authenticated HTTP client, token management |
| Profile enrichment on user creation | 2026-08-30 | Async enrichment pipeline triggered by user.created event |
| Scheduled enrichment refresh | 2026-09-15 | Daily background job for stale profile refresh |
| Cache layer | 2026-09-25 | Redis caching of Graph API responses (1-hour TTL) |
| GA release | 2026-09-30 | Profile enrichment enabled for all users |

**Success Criteria:**
- Profile enrichment completes for 95% of users within 5 minutes of creation.
- Graph API rate limits respected (max 10,000 requests per 10 minutes per tenant).
- Cache hit ratio > 80% for frequently accessed profiles.
- Graceful degradation: users created when Graph API is unavailable fall back to local data only.

### 2. Enhanced RBAC Model

**Description:** Extend the role-based access control model from the current three roles (Admin, Operator, User) to support custom roles with granular permissions. Introduce permission sets that can be composed into roles.

**Milestones:**

| Milestone | Target Date | Deliverable |
|---|---|---|
| RBAC schema design | 2026-08-01 | Permission model, role hierarchy |
| Custom role CRUD API | 2026-08-20 | `POST/GET/PUT/DELETE /api/v2/roles` |
| Permission assignment | 2026-09-05 | Assign permissions to roles, validate at middleware |
| Role assignment to users | 2026-09-20 | `POST /api/v2/users/{id}/roles` |
| GA release | 2026-09-30 | Enhanced RBAC with custom roles |

### 3. Cursor Pagination for List Endpoints

**Description:** Replace offset-based pagination with cursor-based pagination for all list endpoints. Performance improvement for large datasets and consistent pagination across the platform.

**Milestones:**

| Milestone | Target Date | Deliverable |
|---|---|---|
| Cursor pagination implementation | 2026-08-10 | Base64URL-encoded cursor, keyset pagination |
| Migration of existing endpoints | 2026-08-25 | All list endpoints use cursor pagination |
| Backward-compatible v1 deprecation | 2026-09-01 | Offset pagination deprecated with Sunset header |

---

## Q4 2026 (October — December)

### 1. SCIM 2.0 Provisioning

**Description:** Implement SCIM 2.0 server endpoints for automated user provisioning from identity providers (Entra ID, Okta). See future-integrations.md for details.

### 2. Bulk User Operations

**Description:** Support bulk create, update, and delete operations for users (up to 1000 users per request). Includes CSV/JSON input, validation summary, error reporting, and idempotent processing.

**Milestones:**

| Milestone | Target Date | Deliverable |
|---|---|---|
| Design review | 2026-10-01 | Bulk operation schema, error model |
| Bulk create | 2026-10-20 | `POST /api/v2/users/bulk` |
| Bulk update | 2026-11-05 | `PATCH /api/v2/users/bulk` |
| Bulk soft-delete | 2026-11-15 | `POST /api/v2/users/bulk/delete` |
| Result reporting | 2026-12-01 | Per-item success/error reporting |
| GA release | 2026-12-15 | Bulk operations available |

### 3. Audit Trail for User Mutations

**Description:** Implement comprehensive audit logging for all user mutations, capturing before/after state, actor identity, timestamp, and IP address. Audit logs are immutable and stored separately from operational logs.

---

## Q1 2027 (January — March)

### 1. Advanced Tenant Management

**Description:** Self-service tenant creation, tenant configuration management, tenant-level feature flags, and tenant usage reporting.

**Milestones:**

| Milestone | Target Date | Deliverable |
|---|---|---|
| Tenant creation API | 2027-01-15 | `POST /api/v2/tenants` |
| Tenant configuration | 2027-02-01 | Feature flags, settings per tenant |
| Tenant usage reporting | 2027-02-15 | Usage metrics per tenant |
| GA release | 2027-03-01 | Self-service tenant management |

### 2. User Data Export (GDPR Portability)

**Description:** Implement GDPR Article 20 data portability: export all user data in machine-readable JSON format, including profile, roles, activity history.

### 3. Hard-Delete Purge Job

**Description:** Background job that permanently deletes users that have been soft-deleted for more than 90 days (compliance requirement). Includes configurable retention period, pre-deletion notification, and audit logging.

---

## Future Considerations

- **User Groups:** Group management with nested group support.
- **User Delegation:** Temporary access delegation between users.
- **Self-Service Profile Management:** Allow users to update own profile fields.
- **Approval Workflows:** Multi-step approval for user role changes.
- **Entra ID Group Sync:** Automatic user group synchronization from Entra ID.
