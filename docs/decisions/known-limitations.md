# Known Limitations — Users Service

- **Status:** Approved
- **Owner:** Platform Engineering Team
- **Last Updated:** 2026-07-20

## Overview

This document catalogs the current known limitations of the Users Service. Each entry includes impact, reason, and planned resolution path.

---

## L01: No Self-Service Profile Management

| Attribute | Value |
|---|---|
| **Limitation ID** | U-L01 |
| **Impact** | Users cannot update their own profile fields (name, email, phone). All profile changes must be performed by an Administrator or Operator. |
| **Reason** | Self-service profile management requires a user-facing UI and careful validation rules (e.g., email change requires verification). |
| **Workaround** | Users can request profile changes via support ticket; Operators execute the change. |
| **Resolution Path** | Planned for Q1 2027. |

---

## L02: No Bulk Operations

| Attribute | Value |
|---|---|
| **Limitation ID** | U-L02 |
| **Impact** | Administrators must create, update, or delete users one at a time. Bulk operations for large migrations (1000+ users) require custom scripts or direct database access. |
| **Reason** | Bulk operations require careful design for idempotency, error reporting, and transaction boundaries. |
| **Workaround** | Script via API calls (limited by rate limiting). Direct database migration scripts for one-time operations. |
| **Resolution Path** | Planned for Q4 2026. See roadmap.md. |

---

## L03: No SCIM Provisioning

| Attribute | Value |
|---|---|
| **Limitation ID** | U-L03 |
| **Impact** | No automated user provisioning from identity providers (Entra ID, Okta). Users must be created manually or via API. Deprovisioning requires manual action. |
| **Reason** | SCIM 2.0 implementation deferred to Q4 2026. |
| **Workaround** | Users created via API. Entra ID users are created on first login via auth event consumption. |
| **Resolution Path** | SCIM 2.0 planned for Q4 2026. See future-integrations.md. |

---

## L04: No Group Management

| Attribute | Value |
|---|---|
| **Limitation ID** | U-L04 |
| **Impact** | No support for user groups. Role-based authorization is per-user only. |
| **Reason** | Group management deferred to future release. |
| **Workaround** | Assign roles directly to users. For large teams, use infrastructure-as-code (Terraform) for role assignments. |
| **Resolution Path** | Groups planned for Q1 2027. |

---

## L05: No Advanced Search / Full-Text Search

| Attribute | Value |
|---|---|
| **Limitation ID** | U-L05 |
| **Impact** | User search is limited to exact-match or prefix-match on email and name fields. No full-text search across profile fields. |
| **Reason** | Full-text search requires PostgreSQL tsvector indexes or dedicated search infrastructure (Elasticsearch). |
| **Workaround** | Use exact-match queries with cursor pagination. |
| **Resolution Path** | Planned for Q1 2027. |

---

## L06: No Approval Workflows for Role Changes

| Attribute | Value |
|---|---|
| **Limitation ID** | U-L06 |
| **Impact** | Role changes take effect immediately. There is no approval workflow for sensitive role escalations (e.g., User to Admin). |
| **Reason** | Approval workflow infrastructure (notification, state machine, escalation) not yet implemented. |
| **Workaround** | Operationally enforced: Operators should coordinate role changes via Slack or support tickets. Audit trail provides retrospective oversight. |
| **Resolution Path** | Planned for 2027. |

---

## L07: No Hard-Delete for Users (Soft-Delete Only)

| Attribute | Value |
|---|---|
| **Limitation ID** | U-L07 |
| **Impact** | Users are soft-deleted (marked as deleted, data retained). There is no API to permanently delete user data. GDPR right to erasure requires a manual database operation. |
| **Reason** | Hard-delete requires careful cascading deletion of related data and compliance verification. |
| **Workaround** | Manual database deletion by DBA with logged request. Soft-delete users are automatically purged after 90 days (see purge job in roadmap.md). |
| **Resolution Path** | Hard-delete API planned for Q1 2027. |

---

## L08: No User Data Export (GDPR Portability)

| Attribute | Value |
|---|---|
| **Limitation ID** | U-L08 |
| **Impact** | Users cannot export their personal data in a machine-readable format (GDPR Article 20). Requests must be fulfilled manually. |
| **Reason** | Data export endpoint requires careful design for scope (which data is included), format (JSON schema), and delivery mechanism. |
| **Workaround** | Manual database export by DBA with legal approval. |
| **Resolution Path** | Planned for Q1 2027. See roadmap.md. |

---

## L09: No Entra ID Group Sync

| Attribute | Value |
|---|---|
| **Limitation ID** | U-L09 |
| **Impact** | User group membership in Entra ID is not synchronized to the Users Service. RBAC roles must be assigned independently. |
| **Reason** | Group sync requires a scheduled background job with delta tracking and conflict resolution. |
| **Workaround** | Assign roles to users individually via API. |
| **Resolution Path** | Planned for 2027. |

---

## L10: No Audit Log Viewer

| Attribute | Value |
|---|---|
| **Limitation ID** | U-L10 |
| **Impact** | Audit logs are written to storage but there is no built-in viewer or search interface for audit data. Investigating user change history requires querying the audit store directly. |
| **Reason** | Audit log viewer deferred to future release. |
| **Workaround** | Query audit data via Azure Monitor or directly from the audit storage. |
| **Resolution Path** | Planned for 2027. |

---

## L11: No Webhook Support for User Events

| Attribute | Value |
|---|---|
| **Limitation ID** | U-L11 |
| **Impact** | External systems cannot receive real-time user event notifications via webhooks. They must poll the API or integrate with Azure Service Bus directly. |
| **Reason** | Webhook delivery infrastructure (registration, retry, signing, deduplication) not implemented. |
| **Workaround** | Consume user events from Azure Service Bus topic `user-events` directly. |
| **Resolution Path** | Planned for 2027. |

---

## Unknown Limitation Summary

| ID | Limitation | Impact | Resolution |
|---|---|---|---|
| U-L01 | No self-service profile | Medium | Q1 2027 |
| U-L02 | No bulk operations | Medium | Q4 2026 |
| U-L03 | No SCIM provisioning | High | Q4 2026 |
| U-L04 | No group management | Medium | Q1 2027 |
| U-L05 | No full-text search | Low | Q1 2027 |
| U-L06 | No approval workflows | Medium | 2027 |
| U-L07 | No hard-delete API | Medium | Q1 2027 |
| U-L08 | No data export | Medium | Q1 2027 |
| U-L09 | No Entra ID group sync | Medium | 2027 |
| U-L10 | No audit log viewer | Low | 2027 |
| U-L11 | No webhook support | Medium | 2027 |
