# Observability — Users Service

- **Status:** Approved
- **Owner:** Platform Engineering Team
- **Last Updated:** 2026-07-20

## Overview

The Users Service implements structured logging, metrics, and distributed tracing to provide visibility into user management operations, tenant isolation, event processing, and Graph API integration. All telemetry uses OpenTelemetry and aligns with the platform observability standards.

## Pillar 1: Structured Logging (Serilog)

### Configuration

All log output is JSON format via Serilog. Minimum levels: Default Information, `Microsoft.EntityFrameworkCore` Warning, `System.Net.Http.HttpClient` Warning.

### Log Levels

| Level | Usage |
|---|---|
| **Verbose** | Development-only diagnostics |
| **Debug** | Detailed troubleshooting; disabled by default in production |
| **Information** | User CRUD operations, tenant changes, event processing |
| **Warning** | Degraded path usage, rate limit approaching, deprecated endpoint usage |
| **Error** | Failed operation, Graph API failure, event processing failure |
| **Fatal** | Service fails to start, database unreachable, unrecoverable state |

### Event Categories

| Category | Event ID Range | Description |
|---|---|---|
| User Operations | 1000-1999 | Create, read, update, delete, soft-delete, restore |
| Tenant Operations | 2000-2999 | Tenant creation, configuration, status changes |
| Auth Events | 3000-3999 | Consumed auth events (login, logout, token revoked) |
| User Events | 4000-4999 | Published user events (created, updated, deleted) |
| Graph API | 5000-5999 | Microsoft Graph API calls, profile enrichment |
| System Health | 6000-6999 | Startup, shutdown, health checks |

### Mandatory Events

```csharp
public static partial class LogMessages
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "User {UserId} created in tenant {TenantId} by {ActorId}")]
    public static partial void UserCreated(this ILogger logger, Guid userId, Guid tenantId, Guid actorId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "User {UserId} soft-deleted in tenant {TenantId} by {ActorId}")]
    public static partial void UserSoftDeleted(this ILogger logger, Guid userId, Guid tenantId, Guid actorId);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Auth event {EventType} consumed for user {UserId}")]
    public static partial void AuthEventConsumed(this ILogger logger, string eventType, Guid userId);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Warning,
        Message = "Graph API enrichment failed for user {UserId}: {Error}")]
    public static partial void GraphApiEnrichmentFailed(this ILogger logger, Guid userId, string error);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information,
        Message = "User event {EventType} published for user {UserId}")]
    public static partial void UserEventPublished(this ILogger logger, string eventType, Guid userId);
}
```

## Pillar 2: Metrics (Prometheus / OpenTelemetry)

### Metric Naming

```
users_<component>_<metric_name>_<unit>
```

### Required Metrics

#### User Operations

| Metric Name | Type | Labels | Description |
|---|---|---|---|
| `users_crud_operations_total` | Counter | operation (create/read/update/delete/restore), result | User CRUD operation count |
| `users_crud_duration_seconds` | Histogram | operation, result | CRUD operation latency |
| `users_active_total` | Gauge | tenant_id | Current active (non-deleted) user count |
| `users_soft_deleted_total` | Gauge | tenant_id | Currently soft-deleted user count |

#### Auth Events

| Metric Name | Type | Labels | Description |
|---|---|---|---|
| `users_auth_events_consumed_total` | Counter | event_type (login/logout/token_revoked), result | Auth events consumed |
| `users_auth_events_processing_duration_seconds` | Histogram | event_type | Auth event processing latency |
| `users_auth_events_lag_seconds` | Gauge | event_type | Current lag between event publish and consumption |

#### User Events Published

| Metric Name | Type | Labels | Description |
|---|---|---|---|
| `users_events_published_total` | Counter | event_type (created/updated/deleted) | User events published to Service Bus |
| `users_events_publish_duration_seconds` | Histogram | event_type | Event publishing latency |

#### Graph API

| Metric Name | Type | Labels | Description |
|---|---|---|---|
| `users_graph_api_calls_total` | Counter | operation, result | Graph API call count |
| `users_graph_api_duration_seconds` | Histogram | operation | Graph API call latency |
| `users_graph_api_enrichment_total` | Counter | result | Profile enrichment attempt count |

#### Tenant Operations

| Metric Name | Type | Labels | Description |
|---|---|---|---|
| `users_tenants_total` | Gauge | status | Total tenant count by status |
| `users_tenants_operations_total` | Counter | operation, result | Tenant management operations |

#### System

| Metric Name | Type | Labels | Description |
|---|---|---|---|
| `users_requests_total` | Counter | endpoint, method, status_code | Total HTTP requests |
| `users_request_duration_seconds` | Histogram | endpoint, method, status_code | Request latency |
| `users_db_connection_pool_size` | Gauge | host | Database connection pool size |

## Pillar 3: Distributed Tracing (OpenTelemetry)

### Trace Context Propagation

- W3C Trace Context standard (`traceparent` / `tracestate` headers).
- Incoming auth events carry trace context from Auth Service (via Service Bus application properties).
- Outgoing user events propagate trace context to downstream consumers.
- All outbound HTTP (Graph API) propagates trace context.

### Required Spans

| Span Name | Attributes |
|---|---|
| `GET /api/v{version}/users/{id}` | user_id, tenant_id |
| `POST /api/v{version}/users` | tenant_id, role |
| `PATCH /api/v{version}/users/{id}` | user_id, fields_changed |
| `DELETE /api/v{version}/users/{id}` | user_id, permanent |
| `UserService.CreateUser` | tenant_id, role |
| `UserService.SoftDeleteUser` | user_id |
| `AuthEventConsumer.ProcessEvent` | event_type, event_id |
| `GraphApiClient.EnrichProfile` | user_id, graph_user_id |
| `UserEventPublisher.Publish` | event_type, user_id |

### Sampling Strategy

| Traffic | Rate |
|---|---|
| Healthy requests (2xx) | 10% |
| Client errors (4xx) | 50% |
| Server errors (5xx) | 100% |
| Auth event processing | 100% (all events important for tracing) |
| Graph API calls | 25% |
| Health check endpoints | 0% |

## Alert Thresholds

| Alert | Condition | Severity |
|---|---|---|
| High error rate | Error rate > 5% over 5 minutes | Critical |
| Event processing lag > 5 minutes | Event lag > 300 seconds | Warning |
| Graph API failure rate | Error rate > 10% over 5 minutes | Warning |
| High user mutation rate | Create/update/delete rate > 100/s | Warning |
| P99 latency > 1000ms | Latency > 1s for 5 minutes | Critical |
| Soft-delete queue depth | Pending purge > 10000 | Warning |
