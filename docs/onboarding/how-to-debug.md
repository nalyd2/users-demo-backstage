# How to Debug — Users Service

**Service:** `users-service` | **Domain:** `identity` | **Owner:** Platform Engineering Team | **Last Updated:** 2026-07-26

## Table of Contents

1. [Introduction](#1-introduction)
2. [Prerequisites](#2-prerequisites)
3. [Debugging JWT Validation Failures](#3-debugging-jwt-validation-failures)
4. [Tracing Requests Across Services](#4-tracing-requests-across-services)
5. [Debugging Event Consumer Issues](#5-debugging-event-consumer-issues)
6. [Common Errors and Resolutions](#6-common-errors-and-resolutions)
7. [VS Code Debugging Setup](#7-vs-code-debugging-setup)
8. [Diagnostic Commands Reference](#8-diagnostic-commands-reference)
9. [Related Documents](#9-related-documents)

---

## 1. Introduction

This guide covers debugging techniques for the Users Service. It is aimed at platform engineers, on-call SREs, and developers working on the service.

The Users Service has three primary runtime surfaces that produce distinct failure modes:

| Surface | Failure Mode | Typical Symptoms |
|---|---|---|
| **JWT validation** | Every authenticated request requires a valid JWT. If validation fails, the service returns 401 or falls back to a degraded state. | `401 Unauthorized`, `503 Service Unavailable`, `users_jwt_validation_errors_total` spiking |
| **Request processing** | User CRUD operations, RBAC checks, database queries, and internal service calls (gRPC, Service Bus). | `500 Internal Server Error`, slow p99 latency, `NpgsqlException`, timeouts |
| **Event consumption** | Background processing of auth events (`user.login`, `user.logout`, `token.revoked`) from Azure Service Bus. | Stale `last_login_at` timestamps, `users_event_processing_lag_seconds` alert, dead-letter queue growth |

Start with the surface that matches the symptoms, then use the diagnostic commands in [Section 8](#8-diagnostic-commands-reference) to drill in.

---

## 2. Prerequisites

### 2.1 Required Tools

| Tool | Purpose | Verification |
|---|---|---|
| .NET 10 SDK | Build, run, and debug locally | `dotnet --version` should show `10.0.100+` |
| VS Code (or Rider/VS) | Breakpoint debugging, launch configs | — |
| `curl` / `httpie` | Manual API testing | `curl --version` |
| `jq` | JSON parsing from command line | `jq --version` |
| `kubectl` | Cluster operations (staging/prod) | `kubectl version --short` |
| `grpcurl` | gRPC introspection for Auth Service | `grpcurl --version` |
| Azure CLI | Service Bus, Key Vault, App Configuration | `az version` |
| `jwt-cli` or `jwt.ms` | Decode JWT payloads without validation | `npm install -g jwt-cli` or visit `https://jwt.ms` |

### 2.2 Environment-Specific Configuration

| Setting | Local (Development) | Production |
|---|---|---|
| Auth Issuer | `https://localhost:7103` | `https://auth.internal.platform` |
| Auth Audience | `users-service-dev` | `users-service` |
| Auth gRPC Endpoint | `https://localhost:5103` | `https://auth-service.platform.svc.cluster.local:5103` |
| JWKS Cache TTL | 1 minute | 5 minutes |
| Log Level | `Debug` | `Information` |

These values are set in [`appsettings.Development.json`](../../src/UsersService/appsettings.Development.json) and [`appsettings.json`](../../src/UsersService/appsettings.json).

---

## 3. Debugging JWT Validation Failures

### 3.1 Understanding the Validation Pipeline

The Users Service validates JWT tokens at **two layers** (defense in depth):

```
Client → API Gateway (edge validation) → Users Service (service-level validation)
                                              │
                                              ├─ Check JWKS cache (local, in-memory)
                                              │    ├─ Hit  → validate RS256 signature locally
                                              │    └─ Miss → gRPC call to Auth Service
                                              │               └─ On success → populate cache
                                              │
                                              ├─ Extract claims: sub, roles, tid
                                              ├─ Enforce RBAC: is role allowed for this endpoint?
                                              └─ Execute query (scoped to tenant_id from JWT)
```

The JWKS cache is the critical resilience mechanism. When the Auth Service is unreachable, the cache keeps the service operational for its configured TTL (5 minutes in production, 1 minute in development).

### 3.2 Common JWT Validation Failures

#### 3.2.1 Expired Token

**Symptom:** `401 Unauthorized` with detail containing `"token has expired"` or `"SecurityTokenExpiredException"`.

The access token has a 15-minute lifetime (configurable via `Auth:AccessTokenLifetimeMinutes` in the Auth Service). The client must refresh using the refresh token before expiry.

**Diagnosis:**

```bash
# Decode the token to check exp claim (no signature validation)
jwt decode <token>

# Look for:
# {
#   "exp": 1690000000,
#   ...
# }
# Compare to: date -d @1690000000
```

**Resolution:** The client must call `POST /api/auth/refresh` with a valid refresh token to obtain a new access token.

#### 3.2.2 Wrong Issuer or Audience

**Symptom:** `401 Unauthorized` with `"IDX10205: Issuer validation failed"` or `"IDX10214: Audience validation failed"`.

The service validates that `iss` matches `Auth:Issuer` and `aud` matches `Auth:Audience`. This is a common misconfiguration when pointing to the wrong environment.

**Diagnosis:**

```bash
# Decode the token
jwt decode <token>

# Compare against expected values:
#   Issuer:   https://auth.internal.platform (or https://localhost:7103 for dev)
#   Audience: users-service (or users-service-dev for dev)
```

**Check what the service expects:**

```bash
# From appsettings.json
cat src/UsersService/appsettings.json | jq '.Auth'

# Or via the health endpoint (if exposed)
curl -s https://users-service.platform/api/health/ready | jq '.'
```

**Resolution:** Ensure the token was issued by the same Auth Service instance the Users Service is configured to trust. In development, both services must use consistent values. In production, verify the environment variables `Auth__Issuer` and `Auth__Audience` on the pod:

```bash
kubectl exec deploy/users-service -n platform -- env | grep Auth__
```

#### 3.2.3 Invalid Signature

**Symptom:** `401 Unauthorized` with `"IDX10503: Signature validation failed"` or `"IDX10501: Signature validation failed. Unable to match key"`.

The token was signed with a key that the service does not recognize. Common causes:

- The Auth Service rotated its signing key, but the service is using a stale JWKS cache.
- The token is from a different Auth Service instance (e.g., staging vs. production).
- The token is a self-signed test token that was never issued by Auth Service.

**Diagnosis -- Fetch the expected public key:**

```bash
# Fetch the JWKS from the Auth Service
curl -s https://auth.internal.platform/.well-known/jwks.json | jq '.'

# Compare the 'kid' in the token header with the 'kid' in the JWKS
jwt decode <token>   # Look at header.kid
```

**Check the JWKS cache age in the Users Service:**

```bash
# Prometheus metric
curl -s http://localhost:7201/metrics | grep users_jwks_cache_age_seconds
```

If the cache is older than the configured TTL and the Auth Service is unreachable, the cache is stale.

**Resolution:**

1. Verify the Auth Service is healthy and its JWKS endpoint is reachable.
2. If the key was legitimately rotated, the cache will refresh on the next successful gRPC call to Auth Service (within the TTL).
3. In an emergency, you can force a cache clear by restarting the Users Service pods:

```bash
kubectl rollout restart deployment/users-service -n platform
```

#### 3.2.4 Revoked Token (JTI Blacklist)

**Symptom:** `401 Unauthorized` with `"Token has been revoked"`.

The token's `jti` (JWT ID) has been added to the blacklist via the logout or token refresh flow.

**Diagnosis:** This is intentional. The token was either explicitly revoked via logout, or a refresh token rotation detected a replay attack and revoked the entire token family.

**Resolution:** The client must obtain a new token by logging in again.

#### 3.2.5 Missing or Malformed Claims

**Symptom:** `401 Unauthorized` or `403 Forbidden` with `"Missing required claim"` or `"Invalid claim format"`.

**Diagnosis -- Inspect the claims:**

```bash
jwt decode <token> | jq '.payload'

# Expected claims:
# - sub:  user UUID (required)
# - roles: array of strings (required for RBAC)
# - tid:  tenant UUID (required for tenancy)
# - jti:  token ID (required for revocation check)
```

**Resolution:** Ensure the Auth Service is configured to include all required claims in the JWT. See the `TokenService.IssueTokensAsync()` implementation in the Auth Service for the exact claim set.

### 3.3 JWT Validation Log Analysis

Search the application logs for JWT-related events:

```bash
# Structured log query (Elasticsearch)
# Search for JWT validation failures
index: "logs-platform-*"
"users_jwt_validation_errors_total"

# Common log messages to grep for:
# - "Token validation failed: ..."
# - "JWT expired"
# - "IDX10205: Issuer validation failed"
# - "IDX10503: Signature validation failed"
# - "AuthServiceClient: gRPC call failed, falling back to JWKS cache"
# - "JWKS cache miss, calling Auth Service..."

# Local development logs
dotnet run --project src/UsersService 2>&1 | grep -i jwt
```

### 3.4 Quick JWT Validation Test

Use the Auth Service's test credentials to verify end-to-end JWT validation:

```bash
# 1. Login to get a token (local development)
TOKEN=$(curl -s -X POST https://localhost:5103/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "Platform@2026!"}' | jq -r '.accessToken')

# 2. Test the token against Users Service
curl -s -o /dev/null -w "%{http_code}" \
  -H "Authorization: Bearer $TOKEN" \
  https://localhost:7201/api/users

# Expected: 200

# 3. Test with an expired/invalid token
curl -s -o /dev/null -w "%{http_code}" \
  -H "Authorization: Bearer invalid-token" \
  https://localhost:7201/api/users

# Expected: 401
```

---

## 4. Tracing Requests Across Services

### 4.1 Distributed Tracing with OpenTelemetry

Every request to the Users Service carries a W3C Trace Context (`traceparent` header). This allows correlating a single user request across the API Gateway, Users Service, Auth Service, PostgreSQL, and Service Bus.

**Trace context format:**

```
traceparent: 00-<trace-id>-<span-id>-01
```

### 4.2 Reading Trace IDs in Logs

The Users Service emits structured JSON logs with the correlation ID automatically enriched by Serilog's `Enrich.FromLogContext()`.

**Example log line:**

```json
{
  "@timestamp": "2026-07-26T10:30:00.123Z",
  "level": "Error",
  "messageTemplate": "JWT validation failed for request {Method} {Path}",
  "message": "JWT validation failed for request GET /api/users",
  "properties": {
    "Method": "GET",
    "Path": "/api/users",
    "TraceId": "00-abcdef1234567890abcdef1234567890-abcdef1234567890-01",
    "SpanId": "abcdef1234567890",
    "StatusCode": 401,
    "tenant_id": "00000000-0000-0000-0000-000000000001",
    "requestor_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
  }
}
```

### 4.3 Correlating Across Services

When a request flows from Users Service to Auth Service (gRPC validation), the trace context propagates automatically via the OpenTelemetry gRPC instrumentation.

**Steps to correlate:**

1. Capture the `TraceId` from a Users Service error log entry.
2. Search the Auth Service logs for the same `TraceId`:

```bash
# Elasticsearch query
index: "logs-platform-auth-*"
"TraceId": "00-abcdef1234567890abcdef1234567890-*"
```

3. If the trace is sampled (10% sampling in production), view it in the OpenTelemetry collector or Grafana Tempo:

```
https://grafana.internal/explore?traceId=abcdef1234567890abcdef1234567890
```

### 4.4 Adding Custom Span Attributes

When adding instrumentation to new code paths, use `ActivitySource` to create spans:

```csharp
using System.Diagnostics;

public class UserService
{
    private static readonly ActivitySource ActivitySource = new("Platform.UsersService");

    public async Task<UserResult<UserDto>> CreateUserAsync(CreateUserRequest request, Guid tenantId, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("UserService.CreateUser");
        activity?.SetTag("tenant_id", tenantId.ToString());
        activity?.SetTag("username", request.Username);

        // ... method body ...
    }
}
```

Tags set on the activity appear in Grafana Tempo / Jaeger and make filtering by tenant or user possible.

### 4.5 Manual Trace ID Propagation

When debugging locally without a trace backend, you can inject your own `traceparent` header:

```bash
curl -H "traceparent: 00-debug1234567890abcdef1234567890001-debugspan0000000001-01" \
  -H "Authorization: Bearer $TOKEN" \
  https://localhost:7201/api/users
```

Search the service logs for `TraceId` containing `"debug1234567890abcdef1234567890001"` to isolate your request's logs.

---

## 5. Debugging Event Consumer Issues

The Event Consumer is a `BackgroundService` that subscribes to `auth-events` topic on Azure Service Bus. It processes `user.login`, `user.logout`, and `token.revoked` events.

### 5.1 Architecture of the Event Consumer

```
Azure Service Bus (auth-events topic)
    │
    ├─ Session-enabled subscription (session ID = userId)
    │  ├─ In-order delivery per user
    │  └─ Max 10 concurrent handlers per pod
    │
    ▼
Users Service: EventConsumer (BackgroundService)
    │
    ├─ Deserialize event envelope
    ├─ Check deduplication table (event_deduplication)
    │    ├─ Already processed → complete message (no-op)
    │    └─ New event → process
    │
    ├─ Process event:
    │    ├─ user.login  → UPDATE last_login_at
    │    ├─ user.logout → UPDATE last_logout_at
    │    └─ token.revoked → INSERT INTO token_revocations
    │
    ├─ Record in deduplication table
    └─ Complete message on Service Bus
```

### 5.2 Checking Event Processing Lag

The primary health metric for the event consumer is `users_event_processing_lag_seconds`.

**Warning threshold:** > 60 seconds for 5 minutes
**Critical threshold:** > 300 seconds

```bash
# Prometheus query
users_event_processing_lag_seconds

# Grafana dashboard
# Navigate to: Users Service → Event Processing
```

**If lag is increasing:**

1. Check consumer throughput:
   ```bash
   # Prometheus — events processed per second
   rate(users_events_processed_total[5m])
   ```

2. Check for throttled or blocked processing:
   ```bash
   # Application log search
   grep -E "(EventProcessingException|DeadLetterException|MessageLockLost)" \
     <logfile>
   ```

3. Inspect the dead-letter queue:
   ```bash
   az servicebus topic subscription show \
     --resource-group platform-rg \
     --namespace-name platform-sb \
     --topic-name auth-events \
     --subscription-name users-service \
     --query "deadLetteringOnMessageExpiration"

   # Peek at dead-letter messages
   az servicebus topic subscription message peek \
     --resource-group platform-rg \
     --namespace-name platform-sb \
     --topic-name auth-events \
     --subscription-name users-service/$DeadLetterQueueName
   ```

### 5.3 Common Event Consumer Failures

#### 5.3.1 Poison Message

A message that cannot be processed due to schema or data issues.

**Symptoms:**
- `users_events_processed_total` flatlines while `ActiveMessages` in the subscription grows
- Logs show `EventProcessingException: Failed to deserialize event` or `DbException: Insert or update on table "users" violates foreign key constraint`
- Messages appearing in dead-letter queue

**Diagnosis:**

```bash
# Read the dead-letter message body
az servicebus topic subscription message peek \
  --resource-group platform-rg \
  --namespace-name platform-sb \
  --topic-name auth-events \
  --subscription-name users-service/$DeadLetterQueueName | jq '.[0].body'
```

Look for:
- Missing `userId` field
- Malformed JSON (extra/missing commas, unquoted strings)
- `userId` referencing a user that does not exist (foreign key violation)
- Wrong event type (`user.unknown` instead of `user.login`)

**Resolution:**

1. If the message is genuinely malformed and cannot be processed, remove it from the dead-letter queue:

```bash
az servicebus topic subscription message receive \
  --resource-group platform-rg \
  --namespace-name platform-sb \
  --topic-name auth-events \
  --subscription-name users-service/$DeadLetterQueueName \
  --count 1
```

2. If the schema has changed (new fields added), update the deserialization logic in the Event Consumer and redeploy.

3. If the issue was a transient database failure (e.g., connection timeout), replay the dead-letter messages by forwarding them back to the main subscription (Azure Portal: Service Bus Explorer -> Dead-letter -> Re-send).

#### 5.3.2 Duplicate Event Replay Storm

If the same events are redelivered repeatedly, the deduplication table (`event_deduplication`) grows rapidly, potentially causing:
- High memory usage from the deduplication cache
- Slow `INSERT` performance as the table grows
- False-positive idempotency failures

**Diagnosis:**

```sql
-- Check deduplication table growth rate
SELECT COUNT(*), MIN(processed_at), MAX(processed_at)
FROM event_deduplication
WHERE processed_at > NOW() - INTERVAL '1 hour';

-- Check for duplicate event IDs
SELECT event_id, COUNT(*) as occurrence_count
FROM event_deduplication
WHERE processed_at > NOW() - INTERVAL '1 hour'
GROUP BY event_id
HAVING COUNT(*) > 1;
```

**Resolution:**

1. Verify the Service Bus subscription's duplicate detection is enabled (it should be, but a misconfiguration can cause replays).
2. Check that the Event Consumer completes messages successfully -- if it fails to complete, Service Bus redelivers after the lock expires (default: 30 seconds).
3. If the deduplication table is oversized (> 100k entries), the nightly cleanup job may need tuning. Run a manual cleanup:

```sql
DELETE FROM event_deduplication
WHERE processed_at < NOW() - INTERVAL '7 days';
```

#### 5.3.3 Message Lock Lost

If an event takes longer than 5 minutes to process (the max lock duration), Service Bus releases the lock and another consumer may pick it up.

**Symptoms:**
- `MessageLockLostException` in logs
- Same event processed multiple times (duplicates in `last_login_at` updates)

**Resolution:**

1. Check if a particular query is slow (missing index on `users` table for the event update path):

```sql
EXPLAIN ANALYZE UPDATE users
SET last_login_at = NOW()
WHERE id = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890';
```

2. If lock duration is consistently insufficient, consider breaking the work into smaller operations or increasing the lock duration in the Service Bus subscription configuration.

### 5.4 Simulating Events Locally

For development, you can simulate events without an Azure Service Bus by calling the event processing logic directly:

```csharp
// In a test or debug session
var consumer = serviceProvider.GetRequiredService<IEventConsumer>();
await consumer.ProcessEventAsync(new AuthEvent
{
    Type = "user.login",
    UserId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
    Timestamp = DateTimeOffset.UtcNow
}, CancellationToken.None);
```

### 5.5 Event Consumer Metrics

| Metric | Type | What It Tells You |
|---|---|---|
| `users_events_processed_total` | Counter | Throughput — should be > 0 when events are on the bus |
| `users_event_processing_lag_seconds` | Gauge | How far behind the consumer is |
| `users_event_processing_duration_seconds` | Histogram | How long each event takes to process |
| `users_event_dlq_count` | Gauge | Number of messages in dead-letter queue |
| `users_event_deduplication_cache_size` | Gauge | Size of the in-memory deduplication cache |

---

## 6. Common Errors and Resolutions

### 6.1 Auth Service Unreachable

**HTTP Status:** `503 Service Unavailable` on all authenticated endpoints

**Symptoms:**
- `users_auth_service_grpc_latency` showing timeouts or `connection refused`
- `users_auth_service_grpc_errors_total` > 0
- `users_jwks_cache_age_seconds` > 300 (cache expired)
- Readiness probe failing on `auth_service` check

**Root Cause Analysis Table:**

| Observation | Likely Cause | Next Step |
|---|---|---|
| Auth Service pods in `CrashLoopBackOff` | Auth Service broken deployment | Follow Auth Service runbook |
| Auth Service pods running but gRPC port unreachable | mTLS certificate issue or network policy | Check `kubectl describe endpoints auth-service -n platform` |
| gRPC reachable but returns errors | Auth Service health check failure or overloaded | Check Auth Service `ConnectionErrors` and `RequestRate` metrics |
| Auth Service healthy from debug pod but Users Service cannot connect | Service mesh (Istio) routing issue, missing mTLS certificate, or DNS resolution failure | Check Istio proxy logs on Users Service pod: `kubectl logs deploy/users-service -c istio-proxy -n platform` |
| gRPC healthy but JWKS cache expired | Network partition between Users Service and Auth Service, or JWKS cache refresh logic broken | Check firewall rules, then check `AuthServiceClient.GetJwksAsync()` error rate |

**Immediate resolution steps:**

1. Check if the JWKS cache is still valid:
   ```bash
   # If < 300 seconds, the service is still operational from cache
   curl -s http://localhost:7201/metrics | grep users_jwks_cache_age_seconds
   ```

2. Verify connectivity from a debug pod:
   ```bash
   kubectl run debug-pod --image=nicolaka/netshoot -n platform --rm -it -- /bin/bash
   grpcurl -insecure auth-service.platform.svc.cluster.local:5103 health.Health/Check
   ```

3. If the Auth Service is down and the cache has expired, see the [Emergency Cache TTL Override](../../docs/runbooks/incident-response.md#option-b--extend-jwks-cache-ttl-emergency-override-only) in the incident response runbook.

**Prevention:**

- Ensure the JWKS cache TTL (5 minutes) is long enough to absorb brief Auth Service outages.
- Monitor `users_auth_service_grpc_errors_total` for early warning of connectivity issues before the cache expires.
- Configure proper gRPC keepalive and timeout settings on the `AuthServiceClient`:

```csharp
// Program.cs — gRPC channel configuration
builder.Services.AddGrpcClient<AuthService.AuthServiceClient>(o =>
{
    o.Address = new Uri(configuration["Auth:GrpcEndpoint"]);
}).ConfigureChannel(o =>
{
    o.HttpHandler = new SocketsHttpHandler
    {
        KeepAlivePingDelay = TimeSpan.FromSeconds(5),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(2),
        ConnectTimeout = TimeSpan.FromSeconds(3)
    };
});
```

### 6.2 RBAC Denied

**HTTP Status:** `403 Forbidden`

**Symptom:** The caller is authenticated but does not have the required role for the endpoint.

**Diagnosis:**

```bash
# Decode the JWT to see the roles claim
jwt decode <token> | jq '.payload.roles'

# Expected format: ["admin", "developer"]
```

**Check RBAC rules for the endpoint:**

| Endpoint | Required Role | Your Token's Roles | Result |
|---|---|---|---|
| `GET /api/users` | `admin` or `operator` | `["developer"]` | 403 |
| `POST /api/users` | `admin` | `["user"]` | 403 |
| `DELETE /api/users/{id}` | `admin` | `["operator"]` | 403 |
| `GET /api/users/{id}` (other user) | `admin` or `operator` | `["user"]` | 403 |

**Resolution:** The caller needs a token with the appropriate role. Either:
- Log in as a user with the required role.
- An admin must assign the missing role via `PUT /api/users/{id}` with `{"roles": ["admin"]}`.

**Common Misconfiguration -- Roles claim is a string, not an array:**

If the Auth Service issues roles as a single string instead of an array, the RBAC check will fail:

```json
// Wrong — string, not array
{ "roles": "admin" }

// Correct — array
{ "roles": ["admin"] }
```

Verify the claim format by decoding the JWT. If the format is wrong, fix the claim emission in the Auth Service's `TokenService`.

### 6.3 Database Connection Failure

**HTTP Status:** `503 Service Unavailable` (readiness probe fails)

**Symptoms:**
- `users_db_connection_errors_total` > 0
- Readiness probe (`/api/health/ready`) returning `503`
- Logs: `NpgsqlException`, `connection failed`, `timeout`

**Diagnosis:**

```bash
# 1. Check the connection pool
curl -s http://localhost:7201/metrics | grep users_db_connection_pool

# 2. Check if the database is reachable from the pod
kubectl exec deploy/users-service -n platform -- \
  psql "$CONNECTION_STRING" -c "SELECT 1;"

# 3. Check the connection string (pulled from Key Vault)
kubectl exec deploy/users-service -n platform -- env | grep ConnectionStrings__UsersDb
```

**Common causes and resolutions:**

| Cause | Diagnosis | Resolution |
|---|---|---|
| Connection pool exhausted | `users_db_connection_pool_size` at max (30) with > 0 errors | Kill long-running queries: `SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE state != 'idle' AND query_start < NOW() - INTERVAL '5 minutes'` |
| Database unreachable | `psql` connection fails from pod | Check Azure PostgreSQL status, consider failover to standby |
| Connection string expired | Recently rotated credentials in Key Vault | Pods pick up new secrets within the sync interval. Force: `kubectl rollout restart deployment/users-service -n platform` |
| Network policy blocking outbound | Other outbound calls also fail | Check `NetworkPolicy` and `azure-firewall` rules |
| TLS version mismatch | `NpgsqlException: SSL/TLS handshake failed` | Verify PostgreSQL server allows TLS 1.3. The Npgsql client defaults to `SslMode.Require`. |

### 6.4 Rate Limiting

**HTTP Status:** `429 Too Many Requests`

**Symptom:** The client is sending requests faster than the configured limit.

Note: Rate limiting is enforced at the Auth Service level for authentication endpoints and at the API Gateway for the Users Service. The Users Service itself does not implement rate limiting.

**Resolution:**
- The client must respect the `Retry-After` header and back off.
- For emergency bulk operations (e.g., syncing thousands of users), coordinate with Platform Engineering to temporarily increase the rate limit.

### 6.5 User Not Found (404) vs. Forbidden (403)

The Users Service returns `404 Not Found` for non-existent users AND for users that exist in a different tenant. This prevents user enumeration across tenants.

**Diagnosis:**

```bash
# Test with Tenant A token against Tenant B's user
curl -v -H "Authorization: Bearer $(token-for-tenant-a)" \
  https://users-service.platform/api/users/tenant-b-user-id

# Response: 404 Not Found
# (The user exists but is invisible to Tenant A — correct behavior)
```

**If you expect a user to exist but get 404:**

1. Verify the user exists in the correct tenant:
   ```sql
   SELECT id, tenant_id, username, deleted_at
   FROM users
   WHERE id = 'expected-uuid';
   ```

2. Check if the user was soft-deleted (`deleted_at IS NOT NULL`). Soft-deleted users return 404 unless the caller is an admin and explicitly uses the `includeDeleted` filter.

3. Check that the JWT's `tid` claim matches the user's `tenant_id`. Run:

```bash
jwt decode <token> | jq '.payload.tid'
```

---

## 7. VS Code Debugging Setup

### 7.1 Launch Configuration

Create `.vscode/launch.json` in the repository root:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Users Service (Development)",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/src/UsersService/bin/Debug/net10.0/Platform.UsersService.dll",
      "args": [],
      "cwd": "${workspaceFolder}/src/UsersService",
      "stopAtEntry": false,
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ASPNETCORE_URLS": "https://localhost:7201;http://localhost:7200",
        "Auth__Issuer": "https://localhost:7103",
        "Auth__Audience": "users-service-dev",
        "Auth__GrpcEndpoint": "https://localhost:5103",
        "ConnectionStrings__UsersDb": "Host=localhost;Port=5432;Database=users_dev;Username=users_svc;Password=dev_password"
      },
      "requireExactSource": false
    },
    {
      "name": "Users Service (Attach to Process)",
      "type": "coreclr",
      "request": "attach",
      "processName": "Platform.UsersService"
    },
    {
      "name": ".NET Core Attach (Remote)",
      "type": "coreclr",
      "request": "attach",
      "processId": "${command:pickRemoteProcess}",
      "pipeTransport": {
        "pipeCwd": "${workspaceFolder}",
        "pipeProgram": "kubectl",
        "pipeArgs": ["exec", "-n", "platform", "-i", "users-service-pod-name", "--"],
        "debuggerPath": "/vsdbg/vsdbg",
        "quoteArgs": true
      },
      "sourceFileMap": {
        "/app": "${workspaceFolder}"
      }
    }
  ]
}
```

### 7.2 Build Task

Create `.vscode/tasks.json`:

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "build",
      "command": "dotnet",
      "type": "process",
      "args": [
        "build",
        "${workspaceFolder}/src/UsersService/UsersService.csproj",
        "/property:GenerateFullPaths=true",
        "/consoleloggerparameters:NoSummary"
      ],
      "problemMatcher": "$msCompile"
    }
  ]
}
```

### 7.3 Debugging Key Code Paths

**Where to set breakpoints for common scenarios:**

| What You Want to Debug | File | Line / Method |
|---|---|---|
| JWT validation entry point | `AuthServiceClient.ValidateTokenAsync()` | Start of method |
| JWT validation fallback to cache | `AuthServiceClient.ValidateTokenAsync()` | JWKS cache read |
| RBAC enforcement | Controller / middleware | After claims extraction, before `IUserService` call |
| User creation flow | `UserService.CreateUserAsync()` | Whole method |
| Profile validation | `ProfileValidator.ValidateAsync()` | Rules execution |
| Database query | `UserRepository.GetUsersAsync()` | Dapper `QueryAsync` call |
| Event consumption | `EventConsumer.ProcessEventAsync()` | Event dispatch |
| gRPC client call | `AuthServiceClient` constructor or gRPC call | Channel configuration, call execution |
| Service Bus message processing | `EventConsumer.ConsumeMessageAsync()` | Message deserialization |

### 7.4 Debugging with Docker Compose

If running both the Auth Service and Users Service under Docker Compose, use the following `launch.json` configuration to attach to the running container:

```json
{
  "name": "Attach to Docker (Users Service)",
  "type": "coreclr",
  "request": "attach",
  "processId": "1",
  "pipeTransport": {
    "pipeCwd": "${workspaceFolder}",
    "pipeProgram": "docker",
    "pipeArgs": ["exec", "-i", "users-service"],
    "debuggerPath": "/vsdbg/vsdbg",
    "quoteArgs": false
  },
  "sourceFileMap": {
    "/app": "${workspaceFolder}/src/UsersService"
  }
}
```

**Prerequisites for Docker debugging:**

1. Ensure the Docker image includes `vsdbg` (the .NET debugger). Add this to your `Dockerfile`:

```dockerfile
# Debug stage only
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS debug
RUN dotnet tool install --tool-path /tools dotnet-vsdbg
COPY --from=build /app /app
```

2. Run the container with `--cap-add=SYS_PTRACE --security-opt seccomp=unconfined` to enable debugging.

3. Attach VS Code to the container using the configuration above.

### 7.5 Debugging Tips

**Hot Reload (development):** Use `dotnet watch` for fast iteration:

```bash
dotnet watch run --project src/UsersService
```

This automatically rebuilds and restarts the service when you save source files.

**Conditional breakpoints:** When debugging event processing for a specific user, set a conditional breakpoint:

```
Condition: userId == Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890")
```

**Logpoints:** Instead of adding `Console.WriteLine` or `ILogger.LogDebug` during debugging, use VS Code logpoints (right-click the gutter -> "Add Logpoint"):

```
Processing event: {eventType} for user {userId}
```

Logpoints are non-breaking -- they print to the debug console without stopping execution.

**Inspect gRPC traffic:** Use gRPC reflection to inspect the Auth Service's gRPC API:

```bash
grpcurl -plaintext localhost:5103 list
grpcurl -plaintext localhost:5103 describe auth.AuthService
```

### 7.6 Debugging the Auth Service Alongside

Since the Users Service has a hard dependency on the Auth Service, you often need to debug both. Run both services locally:

```bash
# Terminal 1: Auth Service
cd ../authenthication-demo-backstage
dotnet run --project src/AuthService
# Listens on https://localhost:7103, gRPC on https://localhost:5103

# Terminal 2: Users Service
cd ../users-demo-backstage
dotnet run --project src/UsersService
# Listens on https://localhost:7201
```

Or use the VS Code compound launch configuration:

```json
{
  "version": "0.2.0",
  "compounds": [
    {
      "name": "Both Services",
      "configurations": ["Auth Service", "Users Service (Development)"]
    }
  ],
  "configurations": [
    {
      "name": "Auth Service",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/../authenthication-demo-backstage/src/AuthService/bin/Debug/net10.0/Platform.AuthService.dll",
      "cwd": "${workspaceFolder}/../authenthication-demo-backstage/src/AuthService",
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ASPNETCORE_URLS": "https://localhost:7103;http://localhost:7102"
      }
    }
  ]
}
```

---

## 8. Diagnostic Commands Reference

### 8.1 Service Health

```bash
# Liveness probe (process alive?)
curl -s -o /dev/null -w "%{http_code}" https://users-service.platform/api/health/live

# Readiness probe (dependencies healthy?)
curl -s https://users-service.platform/api/health/ready | jq '.'

# Check each dependency status
curl -s https://users-service.platform/api/health/ready | jq '.checks'
```

### 8.2 Kubernetes

```bash
# Pod status
kubectl get pods -n platform -l app=users-service

# Pod logs (last 100 lines, follow)
kubectl logs -n platform -l app=users-service --tail=100 -f

# Pod logs filtered by trace ID
kubectl logs -n platform -l app=users-service | grep "abcdef1234567890"

# Pod logs filtered by tenant
kubectl logs -n platform -l app=users-service | grep '"tenant_id":"00000000-0000-0000-0000-000000000001"'

# Pod logs filtered by error level
kubectl logs -n platform -l app=users-service | grep '"level":"Error"'

# Istio proxy logs (mTLS issues)
kubectl logs -n platform -l app=users-service -c istio-proxy

# Exec into pod for network diagnostics
kubectl exec -n platform -it deploy/users-service -- /bin/bash

# Restart pods (cache clear, connection refresh)
kubectl rollout restart deployment/users-service -n platform

# Check environment variables
kubectl exec deploy/users-service -n platform -- env | sort
```

### 8.3 PostgreSQL

```sql
-- Check user exists
SELECT id, tenant_id, username, email, deleted_at
FROM users
WHERE id = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890';

-- Check active connections
SELECT state, COUNT(*) as count
FROM pg_stat_activity
WHERE datname = 'usersdb'
GROUP BY state;

-- Check long-running queries
SELECT pid, wait_event_type, state, 
       EXTRACT(EPOCH FROM (NOW() - query_start))::int AS seconds_running,
       LEFT(query, 150) AS query_short
FROM pg_stat_activity
WHERE state != 'idle'
  AND backend_type = 'client backend'
ORDER BY query_start;

-- Check event_deduplication table
SELECT COUNT(*), MIN(processed_at), MAX(processed_at)
FROM event_deduplication;

-- Check soft-deleted users
SELECT COUNT(*) as deleted_count
FROM users
WHERE deleted_at IS NOT NULL;

-- Verify RLS is enabled
SELECT relname, relrowsecurity
FROM pg_class
WHERE relname IN ('users', 'event_deduplication', 'audit_logs');
```

### 8.4 Prometheus Metrics

```bash
# Scrape all metrics (from the pod or port-forward)
curl -s http://localhost:7201/metrics

# JWT validation errors
curl -s http://localhost:7201/metrics | grep users_jwt_validation_errors_total

# Request rate by status code
curl -s http://localhost:7201/metrics | grep users_requests_total

# Event processing lag
curl -s http://localhost:7201/metrics | grep users_event_processing_lag_seconds

# gRPC latency to Auth Service
curl -s http://localhost:7201/metrics | grep users_auth_validation_duration_seconds

# Connection pool status
curl -s http://localhost:7201/metrics | grep users_db_connection
```

### 8.5 Azure Service Bus

```bash
# Check subscription metrics
az monitor metrics list \
  --resource /subscriptions/.../servicebus/.../topics/auth-events \
  --metric "ActiveMessages" "DeadLetterMessageCount" \
  --interval 5m

# Peek at subscription messages
az servicebus topic subscription message peek \
  --resource-group platform-rg \
  --namespace-name platform-sb \
  --topic-name auth-events \
  --subscription-name users-service \
  --count 5

# Peek at dead-letter queue
az servicebus topic subscription message peek \
  --resource-group platform-rg \
  --namespace-name platform-sb \
  --topic-name auth-events \
  --subscription-name users-service/$DeadLetterQueueName \
  --count 5
```

### 8.6 Token Operations

```bash
# Login (local dev)
TOKEN=$(curl -s -X POST https://localhost:5103/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "Platform@2026!"}' | jq -r '.accessToken')

# Decode without validation
jwt decode $TOKEN

# Decode with jq (standalone)
echo $TOKEN | cut -d'.' -f2 | base64 -d 2>/dev/null | jq '.'

# Test against Users Service
curl -s -H "Authorization: Bearer $TOKEN" \
  https://localhost:7201/api/users | jq '.'

# Check token expiry
echo $TOKEN | cut -d'.' -f2 | base64 -d 2>/dev/null | jq -r '.exp' | xargs -I{} date -d @{}
```

---

## 9. Related Documents

- [Architecture Overview](../architecture/overview.md) -- platform context
- [Security Architecture](../architecture/security.md) -- JWT validation flow and threat model
- [Incident Response Runbook](../runbooks/incident-response.md) -- SEV-1/SEV-2 response playbooks
- [Deployment View](../architecture/deployment-view.md) -- topology, health probes, degradation paths
- [Events API](../api/events.md) -- event schema, processing guarantees, monitoring
- [Users API](../api/users-api.md) -- endpoint reference, error responses
- [Local Development Guide](local-development.md) -- setting up the development environment
- [Testing Guide](testing.md) -- running tests, testing patterns
