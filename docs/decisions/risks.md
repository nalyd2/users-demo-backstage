# Risks — Users Service

- **Status:** Approved
- **Owner:** Platform Engineering Team
- **Last Updated:** 2026-07-20

## Overview

This document catalogs identified risks for the Users Service. Each risk includes severity, likelihood, impact, and planned mitigations. Risks are reviewed quarterly.

---

## Risk Register

### U-R01: Auth Service Unavailable (JWT Validation Fails)

| Attribute | Value |
|---|---|
| **Risk ID** | U-R01 |
| **Category** | External Dependency / Availability |
| **Severity** | Critical |
| **Likelihood** | Possible |
| **Impact** | Critical — Users Service cannot validate incoming JWT tokens because the JWKS endpoint is unreachable. Cached JWKS keys provide a 5-minute grace window. After cache expiry, all API requests fail authentication and are rejected. All user management operations are blocked. |
| **Detection** | JWKS fetch failure alert. Error rate spike on all endpoints. PagerDuty alert on AuthServiceDown cascades to Users Service monitoring. |
| **Mitigation** | 1. JWKS document cached in memory with 5-minute TTL. Token validation continues during cache lifetime. 2. Retry with exponential backoff on JWKS fetch failure. 3. Users Service health endpoint (`/health`) reflects Auth Service dependency status. 4. Multi-region deployment ensures JWKS fetch can fall back to secondary region endpoint. |
| **Contingency** | 1. Immediately alert on-call Auth Service team. 2. If outage exceeds 5 minutes, all API calls to Users Service will fail. 3. Consider extending cache TTL if Auth Service RTO exceeds 5 minutes (requires engineering lead approval). 4. Post-incident: verify data consistency and replay missed auth events. |
| **Recovery Time Objective** | 5 minutes (cached JWKS grace window) |

---

### U-R02: PostgreSQL Database Failure

| Attribute | Value |
|---|---|
| **Risk ID** | U-R02 |
| **Category** | Infrastructure / Dependency |
| **Severity** | High |
| **Likelihood** | Unlikely |
| **Impact** | Major — all user operations fail (create, read, update, delete, list). Event consumption fails (no persistence for incoming events). Existing user JWTs cannot be verified (requires DB for role validation). |
| **Mitigation** | 1. Zone-redundant HA with automatic failover (< 60 seconds). 2. Connection pooling with retry logic. 3. 35-day point-in-time restore. 4. Read replica in secondary region. |
| **Contingency** | Initiate zone-redundant failover. If primary region fails, geo-failover to read replica. |

---

### U-R03: Tenant Isolation Violation

| Attribute | Value |
|---|---|
| **Risk ID** | U-R03 |
| **Category** | Security / Data Leakage |
| **Severity** | Critical |
| **Likelihood** | Rare |
| **Impact** | Critical — tenant A users can access tenant B's data. PII exposure, regulatory non-compliance (GDPR), reputational damage. |
| **Detection** | 1. SQL query logging for anomalous cross-tenant access patterns. 2. Regular penetration testing for tenant isolation. 3. Automated integration tests verify tenant isolation for every endpoint. |
| **Mitigation** | 1. Tenant isolation enforced in the repository layer: every query includes `WHERE tenant_id = @tenantId`. 2. Tenant ID extracted from JWT claims (not from request body) to prevent tampering. 3. Integration tests for every endpoint validate cross-tenant access is blocked. 4. Database RLS (Row-Level Security) as defense-in-depth. 5. All user identifiers include tenant_id prefix in audit logs. |

---

### U-R04: Microsoft Graph API Throttling or Outage

| Attribute | Value |
|---|---|
| **Risk ID** | U-R04 |
| **Category** | External Dependency |
| **Severity** | Medium |
| **Likelihood** | Possible |
| **Impact** | Moderate — profile enrichment is delayed or skipped. User profiles are served with locally-stored data (may be stale). |
| **Mitigation** | 1. Graph API responses cached for 1 hour. 2. Retry with exponential backoff on throttling (Retry-After header). 3. Profile enrichment is async and non-blocking; user CRUD operations are not affected. 4. Circuit breaker pattern prevents cascading failures. |

---

### U-R05: Event Processing Backlog

| Attribute | Value |
|---|---|
| **Risk ID** | U-R05 |
| **Category** | Processing / Latency |
| **Severity** | Medium |
| **Likelihood** | Possible |
| **Impact** | Moderate — auth events (login/logout) are not processed in real-time. User session state becomes stale. Login events may be processed after token expiry, causing inconsistent state. |
| **Detection** | 1. `users_auth_events_lag_seconds` metric alerts when lag exceeds threshold. 2. DLQ depth monitoring. |
| **Mitigation** | 1. Event consumers run as independent background services with configurable parallelism. 2. Each event consumer has its own processing pipeline (no cross-event blocking). 3. Events are idempotent: processing the same event twice is safe. 4. Autoscaling for event consumers based on queue depth. |

---

### U-R06: Data Loss from Soft-Delete Purge Job

| Attribute | Value |
|---|---|
| **Risk ID** | U-R06 |
| **Category** | Data Integrity |
| **Severity** | High |
| **Likelihood** | Unlikely |
| **Impact** | Major — incorrectly configured purge job permanently deletes user data that should have been retained. Regulatory non-compliance if retention requirements are violated. |
| **Detection** | 1. Purge job counts logged before and after execution. 2. Anomalous purge volume triggers manual review. 3. Audit log of all purged records. |
| **Mitigation** | 1. Dry-run mode: preview records to be purged before actual deletion. 2. Configurable retention period (default: 90 days). 3. Maximum batch size per execution to limit blast radius. 4. Purge job requires manual confirmation in production. 5. Database backup exists before purge execution. |

---

### U-R07: Event Schema Incompatibility

| Attribute | Value |
|---|---|
| **Risk ID** | U-R07 |
| **Category** | Integration |
| **Severity** | Medium |
| **Likelihood** | Unlikely |
| **Impact** | Major — if Auth Service publishes auth events with a new schema that Users Service cannot parse, all event processing fails. User session state becomes permanently stale until the issue is resolved. |
| **Detection** | 1. Event deserialization failure rate monitored. 2. Error rate spike in event consumer. |
| **Mitigation** | 1. Events include schema version number. 2. Schema version negotiation: consumers declare supported versions, producers use the highest mutually supported version. 3. Backward-compatible schema evolution: new fields are optional, never removed. 4. Integration tests between Auth Service and Users Service event schemas run in CI. |

---

### U-R08: RBAC Permission Escalation

| Attribute | Value |
|---|---|
| **Risk ID** | U-R08 |
| **Category** | Security |
| **Severity** | High |
| **Likelihood** | Rare |
| **Impact** | Major — user with Operator role escalates to Admin role and gains unauthorized access to tenant configuration or user data. |
| **Detection** | 1. Role change events logged and monitored. 2. Anomaly detection on role assignments. 3. Audit trail requires justification for role changes to Admin. |
| **Mitigation** | 1. Role changes require multi-step approval (Admin approves Operator role changes, separate Admin approves Admin role changes). 2. Role assignment is logged with actor identity and timestamp. 3. Principle of least privilege: default role is User, escalation requires explicit approval. 4. Automated tests verify role hierarchy is enforced. |

---

### U-R09: Team Bus Factor

| Attribute | Value |
|---|---|
| **Risk ID** | U-R09 |
| **Category** | Organization |
| **Severity** | Medium |
| **Likelihood** | Unlikely |
| **Impact** | Major — loss of key team members familiar with tenant isolation, event processing, and Graph API integration. |
| **Mitigation** | 1. Infrastructure as code (Terraform, Helm). 2. Comprehensive runbooks for incident response and operations. 3. Code review ensures multiple team members understand each component. 4. Cross-training sessions every sprint. 5. On-call rotation for operational experience. |

---

## Risk Summary

| ID | Description | Severity | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| U-R01 | Auth Service unavailable | Critical | Possible | Critical | Cached JWKS (5-min grace) |
| U-R02 | PostgreSQL failure | High | Unlikely | Major | Zone-redundant HA, read replica |
| U-R03 | Tenant isolation violation | Critical | Rare | Critical | Repository-layer enforcement, RLS |
| U-R04 | Graph API throttling | Medium | Possible | Moderate | Caching, circuit breaker |
| U-R05 | Event processing backlog | Medium | Possible | Moderate | Autoscaling, idempotent events |
| U-R06 | Purge job data loss | High | Unlikely | Major | Dry-run, manual confirmation |
| U-R07 | Event schema incompatibility | Medium | Unlikely | Major | Schema versioning |
| U-R08 | RBAC permission escalation | High | Rare | Major | Multi-step approval, audit |
| U-R09 | Team bus factor | Medium | Unlikely | Major | Documentation, cross-training |
