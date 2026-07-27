# Operations Runbook — Users Service

**Service:** `users-service`
**Domain:** User Lifecycle Management
**Owner:** Platform Engineering Team
**Lifecycle:** Production
**SLA Target:** 99.95% availability
**On-Call:** PagerDuty — `#platform-eng` escalation policy (15 min response)

---

## Table of Contents

1. [Purpose and Scope](#1-purpose-and-scope)
2. [Routine Maintenance Tasks](#2-routine-maintenance-tasks)
3. [Entra ID Sync Monitoring](#3-entra-id-sync-monitoring)
4. [Soft-Delete Purging](#4-soft-delete-purging)
5. [Capacity Planning](#5-capacity-planning)
6. [Performance Tuning](#6-performance-tuning)
7. [Backup Verification](#7-backup-verification)
8. [Health Check Monitoring](#8-health-check-monitoring)
9. [Runbook Automation](#9-runbook-automation)
10. [Escalation and Support](#10-escalation-and-support)

---

## 1. Purpose and Scope

This runbook documents the recurring operational procedures for the Users Service. It is intended for Platform Engineering team members, SREs, and on-call engineers who manage the service in production.

All procedures assume authenticated access to the following consoles:

| Console | URL |
|---|---|
| Azure Portal | `https://portal.azure.com/` — subscription `Platform-Prod` |
| Azure DevOps | `https://dev.azure.com/platform/` — project `platform` |
| Grafana | `https://grafana.internal/d/users/users-service` |
| Kibana | `https://kibana.internal/s/platform` |
| Backstage | `https://backstage.internal/platform/component/users-service` |

**Prerequisites for all procedures:**

- Azure CLI (`az`) logged into the `Platform-Prod` subscription
- `kubectl` configured with the production AKS cluster context (`aks-platform-prod`)
- Access to Azure Key Vault `kv-platform-users-prod`
- Membership in the `platform-engineering` Azure AD group
- `psql` client (PostgreSQL 16 compatible) installed on the jumpbox
- `jq` for JSON parsing

---

## 2. Routine Maintenance Tasks

Routine maintenance follows a tiered cadence: daily checks performed by the on-call engineer, weekly reviews by the service team, and monthly deeper audits.

### 2.1 Daily Tasks (On-Call)

| Time | Task | Tool | Duration |
|---|---|---|---|
| 09:00 | Review dashboards for anomalies | Grafana | 10 min |
| 09:15 | Check PagerDuty for overnight alerts | PagerDuty | 5 min |
| 09:30 | Verify all pods are `Running` and healthy | `kubectl` | 5 min |
| 09:45 | Confirm readiness probe passes on all endpoints | Grafana / cURL | 5 min |
| 10:00 | Check Entra ID sync health | Grafana | 5 min |

**Daily dashboard review checklist:**

1. Open the Users Service dashboard in Grafana (`https://grafana.internal/d/users/users-service`).
2. Verify the following metrics are within baseline:

   | Metric | Alert Threshold | Notes |
   |---|---|---|
   | Request rate (p50/p95/p99) | p95 > 800ms | Higher than auth due to DB queries |
   | Error rate (4xx and 5xx) | > 1% of total requests | 4xx from auth failures are expected |
   | PostgreSQL connection count | > 80% of `max_connections` | Currently 50 per pool |
   | Graph API call latency | p99 > 2s | Throttling may need investigation |
   | Event bus backlog | > 1,000 unconsumed messages | Indicates consumer lag |
   | Soft-delete purge job errors | Any failure in last 24h | Critical for PII compliance |

3. Check the `users-service` logs in Kibana for structured error patterns (`"@level": "Error"` or `"@level": "Fatal"`).
4. Verify the nightly Entra ID sync completed successfully (see Section 3).

**Kubernetes pod verification:**

```bash
# Switch to the production AKS cluster
kubectl config use-context aks-platform-prod

# Check pod status across all availability zones
kubectl get pods -n idp-system -l app=users-service -o wide

# Expected output: 9 pods (3 per zone × 3 zones), all status "Running"

# Inspect any CrashLoopBackOff or Pending pods
kubectl describe pod -n idp-system -l app=users-service | grep -A 5 "Status:"

# Quick health check across all pods
kubectl get endpoints -n idp-system users-service
```

### 2.2 Weekly Tasks (Service Team)

| Task | Frequency | Owner |
|---|---|---|
| Review event consumer lag and dead-letter queue | Weekly (Mon) | Platform engineer |
| Analyze slow-query log from PostgreSQL | Weekly (Tue) | Backend engineer |
| Verify Graph API throttling and quota consumption | Weekly (Wed) | Platform engineer |
| Review soft-delete purge job logs and metrics | Weekly (Thu) | Backend engineer |
| Review dependency vulnerability scan results | Weekly (Fri) | Team rotation |

**Event consumer lag review:**

```bash
# Check Service Bus subscription metrics via Azure CLI
az servicebus topic subscription show \
  --resource-group platform-prod-rg \
  --namespace-name sb-platform-prod \
  --topic-name auth-events \
  --subscription-name users-service \
  --query "{activeMessageCount:countDetails.activeMessageCount, deadLetterCount:countDetails.deadLetteredMessageCount, scheduledCount:countDetails.scheduledMessageCount}"

# Expected: activeMessageCount < 100 during business hours, approaching 0 during low-traffic periods

# Inspect dead-lettered messages (if any)
az servicebus topic subscription show \
  --resource-group platform-prod-rg \
  --namespace-name sb-platform-prod \
  --topic-name auth-events \
  --subscription-name users-service \
  --query "deadLetteringOnMessageExpiration"

# If dead-letter count exceeds 50, investigate:
#   - Message deserialization errors (check Kibana for EventProcessor errors)
#   - Poison messages (check message body and dead-letter reason)
#   - Processing timeouts (message lock duration default: 30s)
```

**PostgreSQL slow query analysis:**

```sql
-- Log into the production PostgreSQL server (credentials from Key Vault)
-- PGPASSWORD retrieved via: az keyvault secret show ...

SELECT
  queryid,
  calls,
  total_exec_time / 1000 AS total_seconds,
  mean_exec_time / 1000 AS mean_ms,
  rows,
  shared_blks_hit,
  shared_blks_read,
  query
FROM pg_stat_statements
ORDER BY total_exec_time DESC
LIMIT 20;
```

Frequent offenders to watch for:

- Queries missing the `tenant_id` filter (should never happen — RLS enforces this, but a full scan wastes I/O).
- Queries against `users` table without index on `(tenant_id, deleted_at)` — the soft-delete filter must use this composite index.
- Queries on `user_sessions` that do not filter by `tenant_id` and `user_id`.

### 2.3 Monthly Tasks

| Task | Expected Duration |
|---|---|
| Capacity review and scaling plan adjustment | 45 min |
| Disaster recovery drill — failover to North Europe | 60 min |
| TLS certificate expiry audit (all layers) | 20 min |
| Entra ID sync account token rotation verification | 30 min |
| Dependency patch cycle (minor updates) | 120 min |
| Soft-delete purge runbook exercise in staging | 30 min |

**Monthly capacity checklist:**

```bash
# 1. Review HPA metrics over the trailing 30 days
kubectl get hpa users-service -n idp-system -o yaml

# 2. Check cluster autoscaler events
kubectl get events -n kube-system --field-selector reason=TriggeredAutoscaler \
  --sort-by=.lastTimestamp

# 3. Review PostgreSQL storage growth
az postgres flexible-server show \
  --name pg-users-prod \
  --resource-group platform-prod-rg \
  --query "{storageUsed:storage.storageUsedGB, storageLimit:storage.storageSizeGB, backupRetention:backup.backupRetentionDays}"
```

### 2.4 Quarterly Tasks

| Task | Expected Duration |
|---|---|
| Full disaster recovery exercise | 4 hours |
| Performance benchmark regression test | 2 hours |
| Access review — Key Vault, PostgreSQL, Kubernetes RBAC | 1 hour |
| Entra ID sync end-to-end validation | 1 hour |
| Architecture review meeting | 1 hour |
| PostgreSQL connection string rotation | 30 min |

---

## 3. Entra ID Sync Monitoring

### 3.1 Overview

The Users Service enriches user profiles from **Microsoft Entra ID (Azure AD)** via the Microsoft Graph API. The sync runs on a configurable schedule (default: nightly at 02:00 UTC, cron `0 2 * * *`) and is controlled by the `GraphApiSync.Enabled` feature flag.

**Sync flow:**

```
1. Sync trigger fires (timer or manual)
2. Fetch all active users from PostgreSQL
3. For each user, call Microsoft Graph API (GET /users/{id})
4. Update profile fields: display_name, department, job_title, mobile_phone
5. Log discrepancies (users in DB but not in Entra ID, and vice versa)
6. Emit users.sync.completed event with summary
```

**Important:** Entra ID is the authoritative source for corporate identity. The Users Service never pushes profile data upstream — it only reads.

### 3.2 Sync Health Dashboard

The Grafana dashboard `Users / Entra ID Sync` tracks the following panels:

| Panel | Metric | Warning | Critical |
|---|---|---|---|
| **Sync Success Rate** | `graph_api_sync_success_total / graph_api_sync_attempts_total` | < 99% | < 95% |
| **Sync Duration** | `graph_api_sync_duration_seconds` | > 10 min | > 20 min |
| **Update Count** | `graph_api_users_updated_total` over last run | 0 (no changes) | N/A (zero is valid at night) |
| **Error Breakdown** | `graph_api_errors_total` by `error_code` label | > 5 errors | > 20 errors |
| **Throttle Status** | `graph_api_throttled_requests_total` | > 0 | > 10 in 5 min |
| **Entra ID Coverage** | `(graph_api_users_found / expected_users_total) * 100` | < 95% | < 90% |

**Dashboard URL:** `https://grafana.internal/d/users/entra-id-sync`

### 3.3 Daily Sync Verification

```bash
# Step 1: Check the last sync timestamp and status via logs
kubectl logs -n idp-system -l app=users-service --tail=500 --since=24h \
  | grep -E "SyncCompleted|SyncFailed|GraphApiSync" \
  | tail -20

# Expected output includes a log line like:
# {"@level":"Information","message":"Entra ID sync completed","sync_duration_seconds":342,"users_updated":15,"users_skipped":2841,"errors":0,"@timestamp":"..."}

# Step 2: Verify the sync published its completion event
kubectl logs -n idp-system -l app=users-service --tail=200 \
  | grep "users.sync.completed" \
  | tail -5

# Step 3: Check the metric directly (if Prometheus port-forward is available)
curl -s http://localhost:7201/metrics | grep graph_api_sync_
```

### 3.4 Investigating Sync Failures

**Common failure modes:**

| Symptom | Likely Cause | Remediation |
|---|---|---|
| `429 Too Many Requests` | Graph API throttling | Check `graph_api_throttled_requests_total`. Sync backs off exponentially (Polly retry policy: 3 retries, 30s base delay). If throttling persists, request a quota increase via Azure support ticket. |
| `401 Unauthorized` | Managed Identity or service principal expired | Verify the pod's managed identity: `az identity show --name users-service-identity --resource-group platform-prod-rg`. Check role assignment on Microsoft Graph. |
| `404 Not Found` | User deleted from Entra ID but still in PostgreSQL | The sync logs this as a discrepancy. Review the discrepancy report (see Section 3.5) and decide whether to soft-delete the local record. |
| Timeout > 30s | Network latency or Graph API degradation | Check `graph_api_sync_duration_seconds`. Consider reducing batch size or increasing the `GraphApi__SyncTimeoutSeconds` config (default: 120). |

**Manual sync trigger:**

```bash
# Trigger the sync endpoint (internal, not exposed via API Gateway)
# Requires kubectl port-forward or direct pod access
kubectl exec -n idp-system deploy/users-service -- \
  curl -s -X POST http://localhost:7201/api/internal/sync-entra-id \
    -H "X-Internal-Key: $(cat /etc/secrets/internal-api-key)"

# Monitor the sync in real-time
kubectl logs -n idp-system -l app=users-service --tail=100 -f \
  | grep -E "Sync|GraphApi|entra"
```

### 3.5 Discrepancy Report

The sync generates a discrepancy report stored in a dedicated PostgreSQL table:

```sql
-- Query the latest sync discrepancy report
SELECT
  sync_run_id,
  sync_timestamp,
  db_only_count,       -- Users in PostgreSQL but not in Entra ID
  entra_only_count,    -- Users in Entra ID but not in PostgreSQL
  field_mismatch_count -- Users where fields differ
FROM sync_discrepancy_reports
ORDER BY sync_timestamp DESC
LIMIT 5;

-- View detailed field mismatches
SELECT
  u.id,
  u.email,
  d.field_name,
  d.db_value,
  d.entra_value
FROM sync_field_mismatches d
JOIN users u ON u.id = d.user_id
WHERE d.sync_run_id = '<latest-run-id>'
ORDER BY u.email;
```

**Action on discrepancies:**

- **Users in DB not in Entra ID:** These are likely non-employee service accounts or platform-internal users. Flag them with a `source = 'platform'` tag. If they represent former employees, initiate the offboarding workflow.
- **Users in Entra ID not in DB:** These may be new employees synced from the HR system. Evaluate whether they need a user profile in the platform. If so, create the profile.
- **Field mismatches:** The sync updates the DB fields automatically. Review the mismatch volume; a high count may indicate a bulk update in the HR system or a mapping error.

### 3.6 Sync Performance Tuning

```yaml
# Configuration for Graph API sync (appsettings.Production.json)
GraphApi:
  Sync:
    Enabled: true
    Schedule: "0 2 * * *"        # Nightly at 2 AM UTC
    TimeoutSeconds: 120
    BatchSize: 50                 # Max users per batch request
    Concurrency: 4                # Parallel Graph API calls
    RetryCount: 3                 # Exponential backoff (Polly)
    RetryBaseDelaySeconds: 30
    FieldMappings:
      display_name: "displayName"
      department: "department"
      job_title: "jobTitle"
      mobile_phone: "mobilePhone"
```

**Tuning guidelines:**

| Issue | Adjustment |
|---|---|
| Sync duration exceeds 20 min for 50k users | Increase `Concurrency` to 8 (monitor throttling) |
| Graph API 429 errors | Decrease `Concurrency` to 2 and increase `RetryBaseDelaySeconds` to 60 |
| Sync never finishes before next scheduled start | Ensure `TimeoutSeconds` > expected duration; consider running twice daily instead |
| Too many unnecessary updates (0 field changes) | The sync skips updates when all fields match — check `graph_api_users_updated_total` metric. If 0 consistently, the sync is healthy. |

---

## 4. Soft-Delete Purging

### 4.1 Overview

The Users Service implements a **soft-delete** pattern: when a user is deleted, their record is marked with `deleted_at = NOW()` and `is_active = false`. The data is retained for a configurable period (`SoftDeleteRetentionDays`, default: 30 days) before being permanently purged.

This design ensures:

- Referential integrity is preserved (FKEYs referencing `users.id` remain valid).
- An undelete window is available for accidental deletions.
- A scheduled purge job handles permanent removal and PII anonymization.

**Data lifecycle:**

```
User created ──► Soft-deleted ──► Retention window (30 days) ──► Purged
                     │                       │
                     │ Can be restored        │ Permanent removal
                     │ (undelete)             │ + audit trail update
                     ▼                       ▼
              deleted_at = NOW()        Record deleted from DB
              is_active = false         PII anonymized in audit logs
              All queries exclude       (email → hash, display_name → "Deleted User")
              by default filter
```

### 4.2 Purge Job Configuration

```yaml
# appsettings.Production.json
Users:
  SoftDeleteRetentionDays: 30       # Environment-specific (dev: 7, qa: 14, staging: 30, prod: 30)
PurgeJob:
  Schedule: "0 3 * * *"            # Daily at 3 AM UTC
  BatchSize: 500                   # Users purged per batch
  BatchDelayMs: 100                # Pause between batches to reduce DB load
  TimeoutMinutes: 30               # Maximum job runtime
  DryRunEnabled: true              # Safety flag — see Section 4.4
  AuditRetentionDays: 90           # How long to keep purged-user audit records
```

### 4.3 Monitoring Purge Job Health

**Key metrics (Grafana panel: `Users / Purge Job`):**

| Metric | Description | Warning | Critical |
|---|---|---|---|
| `purge_job_success` | 1 if last run succeeded, 0 if failed | 0 (failure) | — |
| `purge_job_duration_seconds` | Time to complete | > 10 min | > 20 min |
| `purge_job_users_purged_total` | Users removed in last run | — | — |
| `purge_job_errors_total` | Errors encountered | > 0 | > 5 |
| `purge_job_dry_run` | 1 if dry-run mode is on, 0 if live | — | — |

**Verify the purge job ran successfully:**

```bash
# Check the most recent purge job log
kubectl logs -n idp-system -l app=users-service --tail=500 --since=36h \
  | grep "PurgeJob" \
  | tail -20

# Expected output includes:
# {"@level":"Information","message":"Purge job completed","users_purged":42,"batches":3,"errors":0,"duration_seconds":14.2,"dryRun":false,"@timestamp":"..."}
# OR (if dry-run mode):
# {"@level":"Information","message":"Purge job completed (DRY RUN)","candidates":42,"dryRun":true,"duration_seconds":12.1}
```

**Direct database query:**

```sql
-- Check how many users are pending purge
SELECT COUNT(*) AS pending_purge_count
FROM users
WHERE deleted_at IS NOT NULL
  AND deleted_at < NOW() - INTERVAL '30 days';

-- Check purge job history
SELECT
  run_timestamp,
  users_purged,
  errors,
  duration_seconds,
  dry_run
FROM purge_job_runs
ORDER BY run_timestamp DESC
LIMIT 10;
```

### 4.4 Dry-Run Mode and Canary Deployment

The purge job runs in **dry-run mode** by default (`DryRunEnabled: true`). In dry-run mode, the job identifies candidates for purging but does not delete any records. To enable live purging, the operator must:

1. Verify the candidates are correct by reviewing the dry-run log.
2. Set `PurgeJob__DryRunEnabled` to `false` via a Kubernetes ConfigMap or environment variable.
3. Monitor the first live run closely.
4. Re-enable dry-run mode after confirmation.

```bash
# Step 1: Check dry-run candidates
kubectl logs -n idp-system -l app=users-service --tail=200 --since=36h \
  | grep "PurgeJob" | grep "dryRun" | grep "candidates"

# Step 2: Review sample candidates via database
SELECT id, email, deleted_at
FROM users
WHERE deleted_at IS NOT NULL
  AND deleted_at < NOW() - INTERVAL '30 days'
LIMIT 10;

# Step 3: Disable dry-run (temporary — will be reverted after next restart)
kubectl set env deployment users-service -n idp-system \
  PurgeJob__DryRunEnabled=false

# Step 4: Verify the live run via logs (next scheduled run, or trigger manually)
# Step 5: Re-enable dry-run
kubectl set env deployment users-service -n idp-system \
  PurgeJob__DryRunEnabled=true
```

**Manual trigger (for testing):**

```bash
# Trigger purge job via internal endpoint
kubectl exec -n idp-system deploy/users-service -- \
  curl -s -X POST http://localhost:7201/api/internal/purge-users \
    -H "X-Internal-Key: $(cat /etc/secrets/internal-api-key)"

# For dry-run:
kubectl exec -n idp-system deploy/users-service -- \
  curl -s -X POST "http://localhost:7201/api/internal/purge-users?dryRun=true" \
    -H "X-Internal-Key: $(cat /etc/secrets/internal-api-key)"
```

### 4.5 PII Anonymization

When a user is purged, the following transformations occur:

| Field | Before Purge | After Purge |
|---|---|---|
| `email` | `john.doe@company.com` | `sha256(user_id + salt)@purged.internal` |
| `display_name` | `John Doe` | `Deleted User` |
| `username` | `jdoe` | `deleted-{user_id_prefix}` |
| `mobile_phone` | `+1 555-0123` | `NULL` |
| `external_ids` (Entra ID OID) | `aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee` | `NULL` |
| Audit log user references | `user_id` (UUID) | `user_id` preserved (key needed to link events) |

**Verification of anonymization:**

```sql
-- After a purge run, confirm PII is removed
SELECT email, display_name, mobile_phone, external_ids
FROM audit_users_purged
WHERE purge_run_id = '<latest-run-id>'
LIMIT 5;
-- email should contain '@purged.internal'
-- display_name should be 'Deleted User'
-- mobile_phone should be NULL
```

### 4.6 Restoring a Soft-Deleted User (Undelete)

If a user was accidentally deleted and the retention window has not expired, an admin can restore them:

```bash
# REST API operation (requires admin role)
curl -X POST https://api.internal.platform/api/users/{user-id}/restore \
  -H "Authorization: Bearer $JWT" \
  -H "Content-Type: application/json"
```

The restore operation:

1. Sets `deleted_at = NULL` and `is_active = true`.
2. Publishes a `users.restored` event.
3. Logs the restore action in the audit trail.
4. Does NOT re-enrich from Entra ID (manual sync required).

**After the retention window expires, restoration is impossible.** The data has been permanently purged and PII anonymized.

---

## 5. Capacity Planning

### 5.1 Current Capacity Baseline

| Resource | Current Allocation | Peak Utilization | Headroom |
|---|---|---|---|
| **AKS Node Pool** | 9 nodes (Standard_D4s_v5) | 60% CPU / 50% memory | 40-50% |
| **Users Service Pods** | 9 (3 per AZ × 3 zones) | 35% CPU / 45% memory | 55-65% |
| **PostgreSQL (Primary)** | 4 vCores, 16 GB RAM, 256 GB storage | 25% CPU / 40% connections / 60 GB used | 60-75% |
| **PostgreSQL (Read Replica — NE)** | 2 vCores, 8 GB RAM | 10% CPU / 15% connections | 85%+ |
| **Service Bus (auth-events topic)** | Premium 1 MU | 150 msg/s peak | 65% |
| **Service Bus (users-events topic)** | Premium 1 MU | 30 msg/s peak | 85% |
| **Graph API Requests** | 500,000 / 30-day rolling | 120,000 used (~24%) | 76% |

### 5.2 Scaling Triggers and Actions

| Metric | Threshold | Action | Lead Time |
|---|---|---|---|
| Pod CPU > 70% for 5 min | HPA triggers scale-up (max 6 per AZ) | Automatic | 2 min |
| Pod request latency p95 > 800ms | HPA triggers scale-up | Automatic | 2 min |
| Memory > 80% for 5 min | HPA triggers scale-up | Automatic | 2 min |
| AKS node CPU > 75% | Cluster Autoscaler adds node (max 10 per AZ) | Automatic | 5 min |
| PostgreSQL connections > 80% | Increase `max_connections` and monitor vCores | Manual (planned) | 30 min |
| PostgreSQL storage > 75% | Request storage increase; plan index maintenance | Manual | 4 hours (Azure ticket for storage increase) |
| Graph API quota > 80% | Request quota increase via Azure support | Manual (ticket) | 2-3 days |

### 5.3 Scaling Procedures

**Horizontal Pod Autoscaler (HPA):**

```bash
# View current HPA configuration
kubectl get hpa users-service -n idp-system -o yaml

# Expected configuration:
#   minReplicas: 3
#   maxReplicas: 6
#   metrics:
#     - type: Resource
#       resource:
#         name: cpu
#         target:
#           type: Utilization
#           averageUtilization: 70
#     - type: Resource
#       resource:
#         name: memory
#         target:
#           type: Utilization
#           averageUtilization: 80
```

**Manual scale-up (preemptive for planned traffic increases, e.g., onboarding event):**

```bash
# Increase minimum replicas ahead of a load event
kubectl scale deployment users-service -n idp-system --replicas=5

# After the event, revert
kubectl scale deployment users-service -n idp-system --replicas=3
```

**PostgreSQL vertical scaling:**

```bash
# Step 1: Check current tier and storage
az postgres flexible-server show \
  --name pg-users-prod \
  --resource-group platform-prod-rg \
  --query "{sku:sku.name, storage:storage.storageSizeGB, storageUsed:storage.storageUsedGB}"

# Step 2: Scale compute (brief failover — plan during maintenance window)
az postgres flexible-server update \
  --name pg-users-prod \
  --resource-group platform-prod-rg \
  --sku-name Standard_D4ds_v5

# Step 3: Scale storage (no downtime, but irreversible)
az postgres flexible-server update \
  --name pg-users-prod \
  --resource-group platform-prod-rg \
  --storage-size 512

# Step 4: Update server parameters
az postgres flexible-server parameter set \
  --name max_connections \
  --value 200 \
  --server-name pg-users-prod \
  --resource-group platform-prod-rg
```

### 5.4 Capacity Review Cadence

| Review Type | Frequency | Participants | Deliverable |
|---|---|---|---|
| Dashboard review | Daily | On-call | Metric trend check (Grafana snapshot) |
| Trend analysis | Weekly | Platform engineer | 7-day resource usage chart |
| Capacity planning | Monthly | SRE + Platform team | Scaling recommendations |
| Budget forecasting | Quarterly | Platform team + FinOps | Cost projection and optimization |

### 5.5 Autoscaling Test Procedure

Execute this quarterly to validate that HPA and Cluster Autoscaler respond correctly:

```bash
# Step 1: Deploy a load-test job in the staging environment
kubectl apply -f k8s/staging/load-test/users-service-loadtest.yaml

# Step 2: Monitor pod scaling
watch -n 10 'kubectl get pods -n idp-system -l app=users-service'

# Step 3: Verify HPA metrics
kubectl get hpa users-service -n idp-system -w

# Step 4: Verify Cluster Autoscaler adds nodes
kubectl get nodes -w

# Step 5: After test completes, confirm scale-down back to minimum
kubectl get pods -n idp-system -l app=users-service

# Step 6: Remove the load test
kubectl delete -f k8s/staging/load-test/users-service-loadtest.yaml
```

---

## 6. Performance Tuning

### 6.1 Key Performance Baselines

| Metric | Target | Warning | Critical | Measurement Source |
|---|---|---|---|---|
| P95 GET /api/users/{id} latency | < 200 ms | 400 ms | 800 ms | Grafana (http_request_duration_seconds) |
| P95 POST /api/users latency | < 300 ms | 500 ms | 1,000 ms | Grafana (http_request_duration_seconds) |
| P95 list users (paginated) | < 500 ms | 800 ms | 1,500 ms | Grafana (http_request_duration_seconds) |
| JWT validation latency | < 5 ms | 10 ms | 50 ms | Grafana (jwt_validation_duration_seconds) |
| PostgreSQL query time (write) | < 30 ms | 60 ms | 150 ms | `pg_stat_statements` |
| PostgreSQL query time (read) | < 10 ms | 25 ms | 75 ms | `pg_stat_statements` |
| Graph API call latency | < 500 ms | 1,000 ms | 2,000 ms | Grafana (graph_api_duration_seconds) |
| Service Bus message processing | < 100 ms | 250 ms | 500 ms | Grafana (event_processing_duration_seconds) |
| P95 purge job batch | < 5 s | 15 s | 30 s | Grafana (purge_job_duration_seconds) |

### 6.2 PostgreSQL Tuning

**Current configuration:**

```ini
# Applied via Azure Flexible Server parameter group "users-service-prod"

max_connections = 150                    # 50 per pod × 3 pods
shared_buffers = '4GB'                   # 25% of 16 GB RAM
effective_cache_size = '12GB'            # 75% of 16 GB RAM
work_mem = '8MB'                         # Reduced from default (simple lookups, not OLAP)
maintenance_work_mem = '1GB'
random_page_cost = 1.1                   # Azure Premium SSD
effective_io_concurrency = 200
wal_buffers = '32MB'

# Users Service specific
jit = on                                 # Beneficial for complex reporting queries
enable_nestloop = on                     # Acceptable for typical user lookups
parallel_query_workers = 2               # Limit to avoid I/O contention on the primary
```

**Critical indexes to verify:**

```sql
-- Verify that the essential indexes exist and are being used
SELECT
  schemaname,
  tablename,
  indexname,
  idx_scan,
  idx_tup_read,
  idx_tup_fetch
FROM pg_stat_user_indexes
WHERE tablename IN ('users', 'user_sessions', 'audit_log')
ORDER BY idx_scan ASC;

-- Expected indexes on `users`:
--   ix_users_tenant_id_deleted_at (composite, partial: WHERE deleted_at IS NULL)
--   ix_users_email (unique)
--   ix_users_tenant_id_username (unique composite)
--   ix_users_deleted_at (for purge job queries)
```

**Missing index detection (run weekly):**

```sql
-- Tables with high sequential scans = potential missing index
SELECT
  relname,
  seq_scan,
  seq_tup_read,
  idx_scan,
  CASE WHEN seq_scan > 0
    THEN ROUND(seq_tup_read::numeric / NULLIF(seq_scan, 0), 0)
    ELSE 0
  END AS avg_tuples_per_seq
FROM pg_stat_user_tables
WHERE seq_scan > 100                      <!-- Ignore tables with very few scans -->
  AND seq_tup_read > 10000                <!-- Many rows read per scan -->
ORDER BY avg_tuples_per_seq DESC
LIMIT 10;
```

**Table maintenance:**

```sql
-- Check table bloat (run monthly)
SELECT
  schemaname,
  tablename,
  n_live_tup,
  n_dead_tup,
  ROUND(n_dead_tup::numeric / NULLIF(n_live_tup, 0) * 100, 1) AS dead_pct,
  last_autovacuum,
  last_autoanalyze
FROM pg_stat_user_tables
ORDER BY dead_pct DESC
LIMIT 10;

-- If any table has > 20% dead tuples and no recent autovacuum:
-- Manually vacuum the table
VACUUM (VERBOSE, ANALYZE) users;
```

### 6.3 Connection Pooling

The service uses Npgsql connection pooling with the following configuration:

```ini
# Connection string parameters
Host=pg-users-prod.postgres.database.azure.com;Database=usersdb;
Maximum Pool Size=50;                    # Per pod (3 pods × 50 = 150 max)
Connection Idle Lifetime=300;            # 5 min idle before pool eviction
Connection Pruning Interval=60;          # Check every 60s for idle connections
Multiplexing=false;                      # Disabled — read/write mix reduces multiplexing benefit
```

**Pool monitoring:**

```bash
# Grafana metric: npgsql_connection_pool_total_connection_count
# Target: active connections < 75% of pool size (37 of 50)

# Direct PostgreSQL check:
SELECT COUNT(*) AS active_connections
FROM pg_stat_activity
WHERE state = 'active'
  AND datname = 'usersdb';

SELECT COUNT(*) AS idle_connections
FROM pg_stat_activity
WHERE state = 'idle'
  AND datname = 'usersdb';
```

### 6.4 Event Processing Tuning

The event consumer processes auth events (login, logout, token revoked) from Azure Service Bus:

```yaml
# appsettings.Production.json
ServiceBus:
  EventProcessor:
    MaxConcurrentCalls: 10            # Max messages processed simultaneously per pod
    PrefetchCount: 20                 # Messages pre-fetched for performance
    AutoComplete: true                # Auto-complete on successful processing
    MaxAutoLockRenewalDuration: "00:05:00"  # 5 min lock renewal
    RetryCount: 3                     # On transient failure
    DeadLetterOnError: true           # Poison messages go to DLQ
```

**Performance tuning guidelines:**

| Symptom | Likely Cause | Action |
|---|---|---|
| High message backlog with low CPU | `MaxConcurrentCalls` too low | Increase to 20-30; monitor DB connection pool |
| High DB connection count with message backlog | DB queries are slow (query tuning needed) | Check `pg_stat_statements` for slow event processing queries |
| Messages being dead-lettered | Deserialization failure or processing error | Inspect DLQ, check Kibana logs for `EventProcessor` errors |
| Processing latency > 500ms | Service Bus throttling at 1 MU | Check namespace metrics; consider scaling to 2 MUs |

### 6.5 JIT Compilation and Startup Warmup

The service supports a startup warmup endpoint to reduce cold-start latency after deployment:

```bash
# Trigger warmup (called by the startup probe)
# Internal-only endpoint, not exposed via API Gateway
curl -X POST http://localhost:7201/api/internal/warmup \
  -H "X-Internal-Key: ..."

# Expected effect: first-request latency drops from ~800ms to <100ms
```

### 6.6 Query Optimization Patterns

**List users query (the most common read path):**

```sql
-- The service generates a query equivalent to:
SELECT id, tenant_id, email, display_name, roles, created_at, updated_at
FROM users
WHERE tenant_id = @tenantId
  AND deleted_at IS NULL
ORDER BY created_at DESC
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

-- This should use the composite index ix_users_tenant_id_deleted_at
-- covering (tenant_id, deleted_at DESC, created_at DESC) INCLUDE (email, display_name, roles)
```

**Purge candidates query:**

```sql
SELECT id, email, display_name
FROM users
WHERE deleted_at IS NOT NULL
  AND deleted_at < @cutoffDate
ORDER BY deleted_at ASC
LIMIT @batchSize;

-- Uses ix_users_deleted_at (partial index on deleted_at IS NOT NULL)
```

---

## 7. Backup Verification

### 7.1 PostgreSQL Backups

**Configuration:**

| Attribute | Value |
|---|---|
| **Backup type** | Azure-managed, geo-redundant |
| **Retention** | 35 days point-in-time recovery (PITR) |
| **Backup window** | 01:30 - 03:30 UTC |
| **Geo-redundancy** | Enabled (backups replicated to North Europe paired region) |

**Daily verification procedure:**

```bash
# Step 1: List recent backups
az postgres flexible-server backup list \
  --name pg-users-prod \
  --resource-group platform-prod-rg \
  --query "[].{name:name, created:createdTime, size:backupSize}" \
  --output table

# Expected: at least one completed backup within the last 24 hours

# Step 2: Verify the earliest point-in-time restore date
az postgres flexible-server show \
  --name pg-users-prod \
  --resource-group platform-prod-rg \
  --query "backup.earliestRestoreDate"

# Step 3: Validate backup integrity by checking the latest backup size
# A backup size suddenly dropping to near-zero indicates a problem
```

**Quarterly restore drill:**

```bash
# Step 1: Set the restore point (24 hours ago for a recent snapshot)
RESTORE_TIME=$(date -u -d "24 hours ago" +"%Y-%m-%dT%H:%M:%SZ")
RESTORE_NAME="pg-users-prod-restore-$(date +%Y%m%d)"

# Step 2: Restore to a temporary instance (takes 10-20 minutes)
az postgres flexible-server restore \
  --name "$RESTORE_NAME" \
  --resource-group platform-prod-rg \
  --source-server pg-users-prod \
  --restore-time "$RESTORE_TIME" \
  --zone 1

# Step 3: Verify data integrity
PGPASSWORD=$(az keyvault secret show --vault-name kv-platform-users-prod \
  --name restore-test-password --query value -o tsv)

psql "host=$RESTORE_NAME.postgres.database.azure.com \
  port=5432 dbname=usersdb user=restoretest password=$PGPASSWORD sslmode=require" \
  -c "SELECT 'users_count' AS metric, COUNT(*) AS value FROM users UNION ALL
      SELECT 'active_users', COUNT(*) FROM users WHERE deleted_at IS NULL UNION ALL
      SELECT 'deleted_users', COUNT(*) FROM users WHERE deleted_at IS NOT NULL UNION ALL
      SELECT 'user_sessions', COUNT(*) FROM user_sessions;"

# Step 4: Spot-check recent records
psql ... -c "SELECT id, email, created_at FROM users ORDER BY created_at DESC LIMIT 5;"

# Step 5: Verify RLS policies are intact
psql ... -c "
  SELECT schemaname, tablename, policyname, permissive, roles, cmd
  FROM pg_policies
  WHERE tablename = 'users';
"

# Step 6: Tear down the test instance
az postgres flexible-server delete \
  --name "$RESTORE_NAME" \
  --resource-group platform-prod-rg \
  --yes --no-wait
```

**Restore drill success criteria:**

- All row counts match the source database at the restore point.
- RLS policies are present and match the expected configuration.
- No corruption errors during `SELECT` queries.
- The restore completed within the expected time window.

### 7.2 Application State Backups

The Users Service is largely **stateless**, but the following stateful data requires backup consideration:

| Stateful Component | Backup Method | Verification | Frequency |
|---|---|---|---|
| PostgreSQL (primary) | Azure PITR (35-day retention) | Daily backup list / Quarterly restore drill | See 7.1 |
| Service Bus subscriptions | No backup needed — events are transient | N/A | N/A |
| JWKS cache | Regenerated from Auth Service on restart | N/A | N/A |
| Configuration (Key Vault) | Azure Key Vault geo-replication | Check replication status monthly | Monthly |

### 7.3 Key Vault Backup

Key Vault secrets and certificates are backed up via Azure platform replication:

```bash
# Verify geo-replication status
az keyvault show \
  --name kv-platform-users-prod \
  --query "properties.enableSoftDelete"

# Expected: true (soft-delete enabled — 90-day recovery window)

# Backup a specific secret (for compliance archive)
az keyvault secret backup \
  --vault-name kv-platform-users-prod \
  --name users-db-connection-string \
  --file /tmp/backup-users-db-connection.secret

# Verify the backup file
ls -la /tmp/backup-users-db-connection.secret
file /tmp/backup-users-db-connection.secret
# Expected: non-empty file, identifiable as Azure Key Vault backup format
```

### 7.4 Disaster Recovery Backup Procedure

In the event of a total regional failure (West Europe unavailable):

```bash
# Step 1: Restore PostgreSQL from geo-redundant backups to North Europe
az postgres flexible-server geo-restore \
  --name pg-users-prod-dr \
  --resource-group platform-prod-rg \
  --source-server pg-users-prod \
  --location northeurope

# Step 2: Validate the restored database
# (Run same integrity checks as the quarterly restore drill in Section 7.1)

# Step 3: Point the North Europe read replica to the new primary
# (See deployment runbook for DNS and connection string updates)

# Step 4: Verify service functionality
curl -f -s -o /dev/null -w "%{http_code}" \
  https://users.internal.platform/api/health/ready

# Step 5: Run synthetic user operations
# Create, read, update, delete — full lifecycle test
```

---

## 8. Health Check Monitoring

### 8.1 Probe Architecture

```
                                ┌──────────────────────────┐
                                │  Azure Traffic Manager    │
                                │  (30s interval)          │
                                └──────┬───────────────────┘
                                       │ GET /api/health/ready
                                       ▼
┌──────────────┐           ┌──────────────────────┐
│ kubelet      │◄─────────►│ Users Service Pod    │
│ liveness     │  GET /api │                      │
│ (15s period) │  /health/ │  ┌────────────────┐  │
│              │  live     │  │ Readiness      │  │
│              │           │  │ Probe          │  │
│ kubelet      │  GET /api │  │  - PostgreSQL  │  │
│ readiness    │  /health/ │  │  - Auth Service│  │
│ (5s period)  │  ready    │  │  - Service Bus │  │
│              │           │  └────────────────┘  │
│ kubelet      │           │                      │
│ startup      │           └──────────────────────┘
│ (initial 60s)│
└──────────────┘
```

### 8.2 Health Check Endpoints

**Liveness (`GET /api/health/live`):**

```json
{
  "status": "Healthy",
  "checks": {
    "process": {
      "status": "Healthy",
      "latency_ms": 0.1
    }
  }
}
```

No dependency checks — returns `200` as long as the process is running.

**Readiness (`GET /api/health/ready`):**

```json
{
  "status": "Healthy",
  "checks": {
    "postgres": {
      "status": "Healthy",
      "latency_ms": 2.3
    },
    "auth_service": {
      "status": "Healthy",
      "latency_ms": 4.1
    },
    "service_bus": {
      "status": "Healthy",
      "latency_ms": 12.5
    }
  }
}
```

Returns `503` if any dependency is unhealthy. Deprecated endpoints are not included in readiness checks.

**Readiness thresholds:**

| Dependency | Timeout | Failure Count | Impact |
|---|---|---|---|
| PostgreSQL | 3s | 3 consecutive | Pod is NOT ready — no traffic |
| Auth Service (gRPC) | 2s | 3 consecutive | Pod is NOT ready — JWT validation degraded |
| Service Bus | 5s | 3 consecutive | Pod is NOT ready — event publishing degraded |
| Graph API | 5s | 3 consecutive | Pod is ready but sync is degraded (not a hard dependency) |

### 8.3 Probe Configuration

```yaml
# Deployment template — current production settings
readinessProbe:
  httpGet:
    path: /api/health/ready
    port: 7201
  initialDelaySeconds: 10
  periodSeconds: 5
  timeoutSeconds: 3
  successThreshold: 1
  failureThreshold: 3                     # 15s (3 × 5s) before removal from service

livenessProbe:
  httpGet:
    path: /api/health/live
    port: 7201
  initialDelaySeconds: 30
  periodSeconds: 15
  timeoutSeconds: 5
  successThreshold: 1
  failureThreshold: 3                     # 45s without liveness = container restart

startupProbe:
  httpGet:
    path: /api/health/ready
    port: 7201
  initialDelaySeconds: 5
  periodSeconds: 10
  failureThreshold: 6                     # 60s max startup time
```

### 8.4 Prometheus Alert Rules

```yaml
# prometheus/rules/users-service-alerts.yaml
groups:
  - name: users-service
    rules:
      - alert: UsersServiceDown
        expr: up{job="users-service"} == 0
        for: 1m
        labels:
          severity: critical
          team: platform-engineering
        annotations:
          summary: "Users service is down"
          description: "{{ $labels.instance }} has been unreachable for >1 minute."

      - alert: UsersServiceHighErrorRate
        expr: |
          rate(http_requests_total{job="users-service", status=~"5.."}[5m])
          /
          rate(http_requests_total{job="users-service"}[5m])
          > 0.01
        for: 3m
        labels:
          severity: critical
        annotations:
          summary: "Users service error rate exceeds 1%"
          description: "Error rate is {{ $value | humanizePercentage }} over 5 minutes."

      - alert: UsersServiceHighLatency
        expr: |
          histogram_quantile(0.95,
            rate(http_request_duration_seconds_bucket{job="users-service"}[5m])
          ) > 0.8
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Users service p95 latency exceeds 800ms"

      - alert: UsersServiceAuthDown
        expr: users_auth_service_up{job="users-service"} == 0
        for: 1m
        labels:
          severity: critical
        annotations:
          summary: "Auth Service is unreachable from Users Service"

      - alert: UsersServicePostgresDown
        expr: pg_up{job="users-service"} == 0
        for: 1m
        labels:
          severity: critical
        annotations:
          summary: "PostgreSQL is unreachable from Users Service"

      - alert: UsersServiceSyncFailed
        expr: graph_api_sync_success_total{job="users-service"} == 0
        for: 24h
        labels:
          severity: warning
        annotations:
          summary: "Entra ID sync has not succeeded in 24 hours"

      - alert: UsersServicePurgeJobFailed
        expr: purge_job_success{job="users-service"} == 0
        for: 24h
        labels:
          severity: warning
        annotations:
          summary: "Soft-delete purge job failed in the last 24 hours"

      - alert: UsersServiceHighConnectionCount
        expr: |
          pg_stat_database_numbackends{job="users-service", datname="usersdb"}
          > 120
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "PostgreSQL connections exceed 80% of max (150)"

      - alert: UsersServiceEventConsumerBacklog
        expr: |
          azure_servicebus_subscription_active_messages
          > 1000
        for: 10m
        labels:
          severity: warning
        annotations:
          summary: "Event consumer backlog exceeds 1,000 messages"

      - alert: UsersServiceGraphApiThrottling
        expr: |
          rate(graph_api_throttled_requests_total{job="users-service"}[5m])
          > 0.1
        for: 10m
        labels:
          severity: warning
        annotations:
          summary: "Graph API throttling detected"
          description: "Throttled requests at {{ $value | humanizeRate }} per second."
```

### 8.5 Synthetic Monitoring

Synthetic transactions run every 5 minutes from two external locations to validate end-to-end functionality:

```bash
# Synthetic health check — simulates user lifecycle operations
# Executed via Azure Monitor Availability Tests

# Step 1: Liveness check
curl -f -s -o /dev/null -w "%{http_code}" \
  https://users.internal.platform/api/health/live
# Expected: 200

# Step 2: Readiness check
curl -f -s -o /dev/null -w "%{http_code}" \
  https://users.internal.platform/api/health/ready
# Expected: 200

# Step 3: List users (paginated, tenant-scoped, requires valid JWT)
HEALTH_TOKEN=$(curl -s -X POST https://auth.internal.platform/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"healthcheck","password":"..."}' | jq -r '.access_token')

curl -s -H "Authorization: Bearer $HEALTH_TOKEN" \
  "https://users.internal.platform/api/users?pageSize=5" | \
  jq -e '.data | length > 0' > /dev/null

# Step 4: Get a specific user by ID (from the list response)
USER_ID=$(curl -s -H "Authorization: Bearer $HEALTH_TOKEN" \
  "https://users.internal.platform/api/users?pageSize=1" | \
  jq -r '.data[0].id')

curl -s -H "Authorization: Bearer $HEALTH_TOKEN" \
  "https://users.internal.platform/api/users/$USER_ID" | \
  jq -e '.id == "'$USER_ID'"' > /dev/null
```

### 8.6 Degraded Mode Behavior

When a dependency is unhealthy, the service enters degraded mode:

| Dependency Unhealthy | Service Behavior | Ready Probe |
|---|---|---|
| **PostgreSQL** | All user CRUD operations fail with 503. JWKS validation cache may still serve requests for 5 min (Auth Service call not required for read-only JWT validation). | Unhealthy |
| **Auth Service** | JWKS cache serves token validation for up to 5 min. After cache expires, all authenticated requests fail with 503. The `/api/health/live` endpoint and unauthenticated endpoints still work. | Unhealthy (after cache expires) |
| **Service Bus** | Event publishing is queued in-process (bounded buffer: 5,000 events). If buffer fills, oldest events are dropped. Event consumption pauses (auth events ignored). | Unhealthy if buffer > 80% |
| **Graph API** | Entra ID sync fails; existing profiles continue to serve stale data. No impact on CRUD operations. | Healthy (soft dependency — sync-only impact) |

---

## 9. Runbook Automation

The following procedures are candidates for automation via Azure Automation Runbooks or Azure DevOps Pipelines:

| Procedure | Current State | Automation Target | Priority |
|---|---|---|---|
| Entra ID sync health check | Manual (daily) | Grafana alert + scheduled report | High |
| Soft-delete purge job monitoring | Manual (daily) | Alert-based notification | High |
| PostgreSQL backup restore drill | Manual (quarterly) | Azure DevOps pipeline | High |
| Capacity report generation | Manual (monthly) | Scheduled Grafana report | Medium |
| Dependency vulnerability scan | Automated (weekly) | Already automated | Complete |
| PostgreSQL index health check | Manual (weekly) | Scheduled SQL script + report | Medium |
| Quota usage report (Graph API) | Manual (monthly) | Azure Automation runbook | Low |

---

## 10. Escalation and Support

### 10.1 On-Call Rotation

| Role | Contact | Response Time |
|---|---|---|
| Primary on-call (SRE) | PagerDuty `platform-primary` | 15 min |
| Secondary on-call (Platform) | PagerDuty `platform-secondary` | 30 min |
| Engineering manager | Slack `@platform-eng-manager` | 1 hour |
| InfoSec | Slack `#infosec` | Varies by severity |

### 10.2 Severity Definitions

| Severity | Definition | Response | Escalate After |
|---|---|---|---|
| **SEV1** | Service unavailable or unable to read/write user profiles | 15 min | 30 min |
| **SEV2** | Degraded performance, elevated errors, or partial feature outage (e.g., sync failing, purge stalled) | 30 min | 2 hours |
| **SEV3** | Non-critical issue, cosmetic, or single-tenant problem | Next business day | 1 week |
| **SEV4** | Minor bug, documentation improvement | Next sprint | N/A |

### 10.3 Handoff Checklist

When handing off to the next on-call engineer:

- [ ] Current incident status (if any) reviewed and documented
- [ ] Dashboard snapshots captured for any ongoing anomalies
- [ ] Daily checklist from Section 2.1 completed
- [ ] Entra ID sync status verified as healthy
- [ ] Soft-delete purge job status confirmed
- [ ] PagerDuty rotation acknowledged and forwarded
- [ ] Any scheduled maintenance windows communicated

### 10.4 Related Documents

| Document | Location |
|---|---|
| Incident Response Runbook | `docs/runbooks/incident-response.md` |
| Deployment Runbook | `docs/runbooks/deployment.md` |
| Rollback Runbook | `docs/runbooks/rollback.md` |
| Restart Service | `docs/runbooks/restart-service.md` |
| Security Architecture | `docs/architecture/security.md` |
| Deployment View | `docs/architecture/deployment-view.md` |
| Variables & Configuration | `docs/api/variables.md` |
| Events Reference | `docs/api/events.md` |
| Monitoring Configuration | `docs/decisions/monitoring.md` |
| Observability Decisions | `docs/decisions/observability.md` |

---

*Maintained by the Platform Engineering Team. Last updated: 2026-07-26.*
*For questions or corrections, open an issue or contact `#platform-eng` on Slack.*
