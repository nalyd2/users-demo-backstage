# Restart Service -- Users Service

**Document Owner:** Platform SRE Team
**Classification:** Internal / Operations
**Primary Audience:** On-Call SRE, Platform Engineering

---

## Table of Contents

1. [Objective](#objective)
2. [When to Use This Runbook](#when-to-use-this-runbook)
3. [Prerequisites](#prerequisites)
4. [Pre-Restart Checklist](#pre-restart-checklist)
5. [Restart Procedure](#restart-procedure)
    - [Rolling Pod Restart (Standard)](#rolling-pod-restart-standard)
    - [Single Pod Restart (Targeted)](#single-pod-restart-targeted)
6. [Graceful Shutdown Details](#graceful-shutdown-details)
7. [Verification During Restart](#verification-during-restart)
8. [Post-Restart Validation](#post-restart-validation)
9. [Rollback: If the Restart Fails](#rollback-if-the-restart-fails)
10. [Post-Incident Actions](#post-incident-actions)

---

## Objective

Safely restart the Users Service with zero or minimal impact to platform users. The procedure ensures in-flight requests complete, queued events are drained, and connectivity to the Auth Service -- the service's critical dependency -- is re-established before the new instance serves traffic.

---

## When to Use This Runbook

| Scenario | Restart Type | Urgency |
|---|---|---|
| Deploying a new release | Rolling pod restart | Planned (change window) |
| Applying configuration changes | Rolling pod restart | Planned (change window) |
| Pod stuck on CrashLoopBackOff | Single pod restart | Unplanned (investigate first) |
| Memory/CPU leak observed | Single pod restart (targeted) | Unplanned |
| Certificate rotation (gRPC mTLS) | Rolling pod restart | Planned (maintenance window) |
| After secrets rotation in Key Vault | Rolling pod restart | Planned |

---

## Prerequisites

| Resource | Details |
|---|---|
| **Kubernetes access** | `kubectl` context set to the target cluster and namespace (`users`). |
| **Azure CLI** | `az` logged in with `Contributor` or `AKS Cluster Admin` role. |
| **Monitoring access** | Grafana dashboard (see [Observability](../decisions/observability.md)) and Elastic/Kibana for log inspection. |
| **Slack channel** | `#platform-eng` (communication) and `#platform-sre` (coordination). |
| **Change window** | Confirm the current time falls within the approved change window (if applicable). |
| **PagerDuty** | Silence production alerts for `users-service` during the planned restart to avoid false-positive pages. |

---

## Pre-Restart Checklist

Check each item before starting the restart procedure.

### 1. Verify Auth Service Health

The Users Service has a hard runtime dependency on the Authentication Service. If Auth Service is degraded or unreachable, the restarted pods will mark themselves as `NotReady` once the JWKS local cache expires (5 minutes), cascading to a full service disruption.

```bash
# Check Auth Service health endpoint
curl -s -o /dev/null -w "%{http_code}" https://auth-service.platform.svc.cluster.local:5103/health/ready

# Expected: 200
```

```bash
# Alternative: check via Kubernetes readiness
kubectl -n auth get pods -l app=auth-service --field-selector status.phase=Running
kubectl -n auth wait --for=condition=Ready pods -l app=auth-service --timeout=30s
```

**If Auth Service is unhealthy:** Abort the restart. Notify the Auth Service on-call team via `#platform-sre` and follow the Auth Service incident response runbook. Do not restart the Users Service until Auth Service has been restored.

### 2. Verify Downstream Dependencies

| Dependency | Check Command | Expected |
|---|---|---|
| PostgreSQL primary | `kubectl -n users exec deploy/users-service -- pg_isready -h $(DB_HOST)` | `server accepts connections` |
| Service Bus | `az servicebus topic show --name auth-events --namespace <namespace>` | `status: Active` |
| Notification Service | `curl -s https://notification-service.platform.svc.cluster.local/health/ready` | `200` |

### 3. Check Event Processing Lag

```bash
# Query Prometheus metric via Grafana API or direct endpoint
# A significant backlog (> 1000 events) must drain before restarting
curl -s "http://prometheus.platform.svc.cluster.local:9090/api/v1/query?query=users_event_processing_lag_seconds" | jq '.data.result[].value[1]'
```

**Threshold:** If lag > 60 seconds or backlog > 500 unprocessed events, allow the service to catch up before proceeding. Notify `#platform-sre` of the delay.

### 4. Check Current Traffic Level

```bash
# Query request rate (requests per second) for the last 5 minutes
curl -s "http://prometheus.platform.svc.cluster.local:9090/api/v1/query?query=rate(http_requests_total{job='users-service'}[5m])" | jq '.data.result[].value[1]'
```

If traffic exceeds 75% of the aggregate replica capacity (9 pods x 200 RPS = 1800 RPS typical), consider scaling up temporarily before restarting (see [Scale Up Precaution](#scale-up-precaution) below).

### 5. Verify Istio Sidecar Presence

```bash
kubectl -n users get pods -l app=users-service -o jsonpath='{range .items[*]}{.metadata.name}{"\t"}{.status.containerStatuses[*].name}{"\n"}{end}'
```

Verify every pod has two containers: `users-api` and `istio-proxy`. A missing sidecar means the pod will not join the service mesh and will not receive traffic.

### 6. Notify the Team

Post a message in `#platform-eng`:

> [RUNBOOK] Initiating rolling restart of Users Service in {environment}. Estimated duration: 5-10 minutes. Expected impact: none (rolling update in progress). Monitoring: {Grafana dashboard link}.

### 7. Silence Non-Critical Alerts

Temporarily mute pager alerts for the following conditions in PagerDuty:

| Alert Condition | Rationale |
|---|---|
| `users_service_pod_restarting` | Expected during procedure |
| `users_event_processing_lag_seconds > 60` | Temporary spike during restart is normal |
| `users_http_error_rate > 1%` | Brief 503s during connection drain are acceptable |

Do **not** silence `users_auth_service_unreachable` -- that alert must remain active.

### 8. Scale Up Precaution (Optional)

If the service is running at elevated traffic, add one extra replica per region before beginning the restart to absorb the rolling replacement overhead:

```bash
kubectl -n users scale deployment users-service --replicas=4  # Currently 3 per region
```

Record the baseline replica count so you can scale back down after validation.

---

## Restart Procedure

### Rolling Pod Restart (Standard)

Use for planned restarts, deployments, or configuration changes. This method replaces pods one at a time, keeping the service available throughout.

**Step 1 -- Initiate rolling restart**

```bash
kubectl -n users rollout restart deployment/users-service
```

**Step 2 -- Monitor the rollout progress**

```bash
kubectl -n users rollout status deployment/users-service --watch
```

The command blocks and prints progress as each old pod is terminated and a new pod reaches `Ready`. Typical completion time: 3-7 minutes for 3 replicas.

**Step 3 -- Observe pod replacement in real time**

```bash
kubectl -n users get pods -l app=users-service -w
```

You will see each pod cycle through these phases:
```
Terminating → (graceful shutdown) → Completed
Pending → ContainerCreating → Running → (readiness probe) → Ready → (Istio iptables) → 1/1
```

**Step 4 -- Verify the rollout completed**

```bash
kubectl -n users rollout status deployment/users-service
# Expected output: deployment "users-service" successfully rolled out
```

---

### Single Pod Restart (Targeted)

Use when a specific pod is exhibiting issues (memory leak, high latency, repeated warnings) and you want to minimize churn.

**Step 1 -- Identify the unhealthy pod**

```bash
kubectl -n users get pods -l app=users-service
```

**Step 2 -- Delete the pod (Kubernetes ReplicaSet recreates it)**

```bash
kubectl -n users delete pod users-service-<random-suffix> --wait=false
```

The ReplicaSet controller creates a replacement immediately. Use `--wait=false` to avoid blocking on the old pod's termination grace period.

**Step 3 -- Monitor replacement**

```bash
kubectl -n users get pods -l app=users-service -w | grep <replacement-name>
```

---

## Graceful Shutdown Details

This section describes what happens when a pod receives the SIGTERM signal. Understanding this helps with troubleshooting slow-terminating pods.

### Shutdown Sequence (15-second window)

```
Time 0s  SIGTERM sent by kubelet
         ↓
Time 0s  Process receives SIGTERM
         ├── 1. Health endpoints return 503 (removed from service mesh)
         ├── 2. Istio sidecar drains in-flight HTTP/gRPC connections
         └── 3. Application shutdown sequence:
              ├── 3a. Stop accepting new HTTP requests
              ├── 3b. Drain active HTTP/gRPC connections (max 10s)
              ├── 3c. Stop event consumer (Service Bus message pump)
              ├── 3d. Complete processing current Service Bus messages
              │      └── Complete (Abandoned)PeekLock → Complete (if < 5 min lock)
              └── 3e. Close database connection pool gracefully
                      └── Return idle connections to pool
Time 10s PreStop hook (if configured) enters final wait
Time 15s SIGKILL sent by kubelet — force kill
```

### Important Behaviors

| Aspect | Detail |
|---|---|
| **In-flight HTTP requests** | Completed within the 10-second drain window. Requests exceeding this threshold receive a gateway timeout (504) from the Istio sidecar. |
| **Open HTTP connections** | Idle keep-alive connections are closed immediately. The new pod accepts new connections. |
| **Service Bus message processing** | The event consumer stops the message pump. Any message currently being processed is completed if possible (within PeekLock renewal interval). If processing cannot finish in time, the message is abandoned and redelivered to another pod. The deduplication table (`event_deduplication`) ensures at-most-once processing. |
| **DB connection pool** | Idle connections are closed. In-flight queries complete within the 10-second drain. Long-running queries (rare, < 1% of requests) that exceed the drain window are terminated. The new pod's connection pool re-establishes connections on first query. |
| **gRPC connections to Auth Service** | Existing mTLS channels are closed. The new pod re-establishes connections on the first JWT validation request. |

### PreStop Hook Configuration

```yaml
lifecycle:
  preStop:
    exec:
      command:
        - /bin/sh
        - -c
        - |
          echo "[$(date)] PreStop: waiting for in-flight requests to complete"
          # Give the readiness probe time to fail, removing this pod from the
          # service mesh before traffic stops flowing
          sleep 5
```

The 5-second sleep in the PreStop hook is deliberate -- it allows the readiness probe to fail (2 consecutive failures x 10s period = ~20s to be removed from EndpointSlice) before the process terminates. This prevents a burst of 502/503 errors from the Istio sidecar.

### Configurable Termination Parameters

| Parameter | Current Value | Description |
|---|---|---|
| `terminationGracePeriodSeconds` | 30s | Max time between SIGTERM and SIGKILL |
| Readiness probe failure threshold | 2 | Consecutive failures before removal |
| Readiness probe interval | 10s | Seconds between probes |

---

## Verification During Restart

Perform these checks while the rollout is in progress.

### 1. Verify Pod Readiness

```bash
# Watch pods transition to Ready
kubectl -n users get pods -l app=users-service -w
```

### 2. Verify Readiness Probe (Auth Service Dependency)

The readiness endpoint at `GET /api/health/ready` checks connectivity to Auth Service (via gRPC or JWKS cache), PostgreSQL, and Service Bus. This is the gating check that determines whether the pod receives traffic.

```bash
# Port-forward to a newly started pod and check its readiness
kubectl -n users port-forward pod/users-service-<new-pod> 7201:7201 &
curl -s http://localhost:7201/api/health/ready | jq .
kill %1
```

Expected response:
```json
{
  "status": "Healthy",
  "checks": [
    { "name": "database", "status": "Healthy" },
    { "name": "auth_service", "status": "Healthy", "cacheValid": true },
    { "name": "service_bus", "status": "Healthy" }
  ]
}
```

**If `auth_service` shows `Unhealthy` and `cacheValid` is `false`:** The new pod cannot reach Auth Service and its JWKS cache is empty. The pod will remain `NotReady` indefinitely. Escalate immediately to the Auth Service team and consider rolling back (see [Rollback](#rollback-if-the-restart-fails)).

### 3. Verify Istio Service Mesh Registration

```bash
# Confirm the new pod is in the EndpointSlice
kubectl -n users get endpointslices -l kubernetes.io/service-name=users-service -o yaml | grep -A 2 addresses
```

The output must include the new pod's IP address. If it is absent, the readiness probe is failing and the pod is not receiving traffic.

### 4. Monitor Error Rate During Restart

```bash
# Check for 503/504 errors during the drain window
curl -s "http://prometheus.platform.svc.cluster.local:9090/api/v1/query?query=rate(http_requests_total{job='users-service',status=~'5..'}[1m])" | jq '.data.result[].value[1]'
```

A brief spike of < 10 5xx responses during the 10-second drain window is acceptable. Sustained errors after the restart completes indicate a problem with the new pods.

### 5. Monitor Event Processing Lag

```bash
curl -s "http://prometheus.platform.svc.cluster.local:9090/api/v1/query?query=users_event_processing_lag_seconds" | jq '.data.result[].value[1]'
```

Lag may spike to 30-60 seconds during the restart while pods are cycling. It should return to < 10 seconds within 2 minutes of the rollout completing.

---

## Post-Restart Validation

After the rollout shows `successfully rolled out`, run the full validation suite.

### 1. All Pods Healthy

```bash
kubectl -n users get pods -l app=users-service
# Expected: all pods "Running" and "Ready (1/1)"
```

### 2. Auth Service Connectivity

```bash
# Trigger a JWT validation by executing a health check that calls Auth Service
kubectl -n users exec deploy/users-service -- /bin/sh -c \
  "wget -q -O- http://localhost:7201/api/health/ready | grep auth_service"

# Expected: "auth_service": "Healthy"
```

### 3. End-to-End API Smoke Test

Run a read-only smoke test against each region's endpoint to confirm the service is responding correctly.

```bash
# West Europe (primary)
curl -s -w "\nHTTP %{http_code}" \
  -H "Authorization: Bearer $(gcloud auth print-access-token)" \
  https://users.we.platform.internal/api/health/live

# Expected: {"status":"Healthy"} HTTP 200
```

```bash
# North Europe (secondary)
curl -s -w "\nHTTP %{http_code}" \
  -H "Authorization: Bearer $(gcloud auth print-access-token)" \
  https://users.ne.platform.internal/api/health/live

# Expected: {"status":"Healthy"} HTTP 200
```

### 4. Database Connectivity

```bash
kubectl -n users exec deploy/users-service -- /bin/sh -c \
  "wget -q -O- http://localhost:7201/api/health/ready | grep database"

# Expected: "database": "Healthy"
```

### 5. Event Processing Resumed

```bash
# Check that event processing counters are incrementing
curl -s "http://prometheus.platform.svc.cluster.local:9090/api/v1/query?query=rate(users_events_processed_total[1m])" | jq '.data.result[].value[1]'

# Expected: value > 0 (events are being processed)
```

### 6. Restore Alerting

Re-enable any alerts silenced during the pre-restart checklist. Verify active alerts:

```bash
curl -s "http://alertmanager.platform.svc.cluster.local:9093/api/v2/alerts" | jq '.data | length'
```

Confirm no alerting rules are firing for `users-service` except those pre-existing before the restart.

### 7. Scale Down (If Scaled Up)

If you added extra replicas during the pre-restart phase, scale back to the baseline:

```bash
kubectl -n users scale deployment users-service --replicas=<original-replica-count>
```

### 8. Report Completion

Post in `#platform-eng`:

> [RUNBOOK] Rolling restart of Users Service in {environment} completed successfully. Duration: {duration}. Auth Service connectivity verified. Event processing resumed. All smoke tests passed. Dashboard: {Grafana dashboard link}.

---

## Rollback: If the Restart Fails

If a pod fails to become `Ready`, the rollout is stuck, or error rates are elevated after the restart, roll back immediately.

### Rollback Triggers

| Condition | Action |
|---|---|
| A pod remains `CrashLoopBackOff` for > 2 minutes | Roll back |
| Readiness probe fails for > 60 seconds on any new pod | Roll back |
| Error rate above 5% sustained for > 2 minutes | Roll back |
| Auth Service reports `Unhealthy` on new pods with empty cache | Roll back |
| Event processing lag exceeds 300 seconds and rising | Roll back |

### Rollback via `kubectl rollout undo`

```bash
# Roll back to the previous revision
kubectl -n users rollout undo deployment/users-service

# Monitor the rollback
kubectl -n users rollout status deployment/users-service --watch
```

### Rollback to a Specific Revision

```bash
# List available revisions
kubectl -n users rollout history deployment/users-service

# Roll back to a specific revision (e.g., revision 3)
kubectl -n users rollout undo deployment/users-service --to-revision=3
```

### Rollback via Helm (if using Helm)

```bash
helm -n users rollback users-service <previous-revision-number>
```

### Post-Rollback Verification

1. Run the full Post-Restart Validation suite above.
2. Confirm all original pods are `Ready`.
3. Confirm Auth Service connectivity, event processing, and API health.
4. In `#platform-eng`, post:

> [ROLLBACK] Rolling restart of Users Service failed — rolled back to revision {N}. Root cause: {summary}. Triage ticket: {link}.

5. Create a post-incident ticket documenting the failure (see [Post-Incident Actions](#post-incident-actions)).

### Rollback If Auth Service Is the Cause

If the new pods are failing readiness checks because Auth Service is unreachable:

1. Do not roll back the Users Service -- Auth Service being down means old pods would also be affected once their JWKS cache expires.
2. Focus on restoring Auth Service first.
3. If Auth Service recovery is expected to take longer than 5 minutes, consider temporarily increasing the `Auth__JWKSCacheTtlMinutes` value for the Users Service via ConfigMap (requires another restart, so coordinate this as a recovery measure).

---

## Post-Incident Actions

If the restart triggered a rollback or caused user-facing impact:

1. **Open a post-incident review (PIR) ticket** with the following:
   - Timestamp of the restart attempt
   - Pre-restart metrics (traffic, event lag, dependency health)
   - Which step failed and the observed symptoms
   - Rollback method and duration
   - Grafana dashboard snapshot(s) of the incident window
   - Pod logs from the failing pods

2. **Capture logs from the failing pods** before they are garbage-collected:

```bash
kubectl -n users logs deploy/users-service --previous --tail=200 > users-service-previous-pod.log
kubectl -n users logs deploy/users-service --tail=500 > users-service-current-pod.log
```

3. **Review readiness probe failures** in the kubelet logs:

```bash
# Check kubelet events for the namespace
kubectl -n users get events --sort-by='.lastTimestamp' | grep -i 'unhealthy'
```

4. **Update this runbook** if the procedure was unclear or missing a step relevant to the failure.

---

## Related Documents

| Document | Description |
|---|---|
| [Deployment View](../architecture/deployment-view.md) | AKS topology, health check configuration, Auth Service dependency |
| [System Context](../architecture/context.md) | Auth Service dependency details, circuit breaker, JWKS cache behavior |
| [Events](../api/events.md) | Event processing guarantees, deduplication, message lock duration |
| [Variables & Configuration](../api/variables.md) | Environment variables, feature flags, Auth Service timeout settings |
| [Deployment Runbook](deployment.md) | Full deployment procedure for new releases |
| [Rollback Runbook](rollback.md) | General rollback procedures for failed deployments |
| [Incident Response](incident-response.md) | Incident classification, severity levels, and escalation paths |
| [Observability](../decisions/observability.md) | Metrics, dashboards, and alerting configuration |

---

## Revision History

| Date | Author | Changes |
|---|---|---|
| 2026-07-26 | Platform SRE Team | Initial version |
