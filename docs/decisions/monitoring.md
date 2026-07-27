# Monitoring Strategy — Users Service

- **Owner:** Platform Engineering Team
- **Last Updated:** 2026-07-20
- **Version:** 1.0

## Purpose and Scope

This document defines the monitoring strategy for the Users Service. It covers metrics, dashboards, alerting, SLOs, and error budget policies specific to user management operations, event processing, and Graph API integration.

## Service Level Objectives

### Availability Target

| SLO | Target | Window |
|---|---|---|
| Availability | 99.95% | Rolling 30 days |
| User read success rate | >= 99.9% | Rolling 7 days |
| User write success rate | >= 99.5% | Rolling 7 days |
| Event processing success rate | >= 99.9% | Rolling 7 days |
| User CRUD latency (p99) | <= 500 ms | Rolling 7 days |
| Event processing lag | <= 30 seconds | Rolling 7 days |

### SLI Definitions

#### Availability SLI
```
good_requests = count of HTTP 2xx + 4xx responses
availability = good_requests / total_requests (excluding health probes)
```

#### Event Processing Lag SLI
```
lag = current_timestamp - event_enqueued_time (per event)
lag_sli_p99 = p99 of all event processing lags over 5 minutes
```

## Key Metrics

### User Operations Metrics

| Metric | Type | Labels | Description |
|---|---|---|---|
| `users_read_requests_total` | Counter | endpoint, result, tenant_id | User read request count |
| `users_write_requests_total` | Counter | operation (create/update/delete/restore), result | User write operation count |
| `users_request_duration_seconds` | Histogram | operation, endpoint | Request latency |
| `users_active_total` | Gauge | tenant_id | Current active user count |
| `users_soft_deleted_total` | Gauge | tenant_id | Current soft-deleted user count |

### Event Processing Metrics

| Metric | Type | Labels | Description |
|---|---|---|---|
| `users_auth_events_consumed_total` | Counter | event_type, result | Auth events consumed from Service Bus |
| `users_auth_events_lag_seconds` | Gauge | event_type | Current processing lag for auth events |
| `users_auth_events_lag_seconds_bucket` | Histogram | event_type | Distribution of event processing lag |
| `users_events_published_total` | Counter | event_type, result | User events published |
| `users_events_publish_duration_seconds` | Histogram | event_type | Event publishing latency |
| `users_dead_letter_queue_depth` | Gauge | topic | Current DLQ message count |

### Graph API Metrics

| Metric | Type | Labels | Description |
|---|---|---|---|
| `users_graph_api_calls_total` | Counter | operation, result | Graph API call count |
| `users_graph_api_duration_seconds` | Histogram | operation | Graph API latency |
| `users_graph_api_errors_total` | Counter | operation, error_code | Graph API errors |
| `users_graph_api_cache_hit_ratio` | Gauge | — | Graph API response cache hit rate |

### System Metrics

| Metric | Type | Labels | Description |
|---|---|---|---|
| `users_requests_total` | Counter | endpoint, method, status_code | HTTP request count |
| `users_request_duration_seconds` | Histogram | endpoint, method | HTTP request latency |
| `users_db_connection_pool_utilization` | Gauge | host | Database connection pool usage |
| `users_db_query_duration_seconds` | Histogram | operation, table | Database query latency |
| `users_db_errors_total` | Counter | operation, error_code | Database errors |

## Grafana Dashboards

### Users Service Overview Dashboard

**Purpose:** Primary dashboard for on-call SRE.

| Panel | Metric / Query | Visualization |
|---|---|---|
| Service Status | `users_service_up` | Stat (green/red) |
| Request Rate | `rate(users_requests_total[5m])` | Time series |
| Error Rate (5xx) | `rate(users_requests_total{status=~"5.."}[5m]) / rate(users_requests_total[5m]) * 100` | Time series |
| P50/P95/P99 Latency | `histogram_quantile(0.99, rate(users_request_duration_seconds_bucket[5m]))` | Time series |
| Active Users | `users_active_total` | Stat + time series |
| Event Processing Lag | `users_auth_events_lag_seconds` | Time series (by event type) |
| Soft-Delete Queue | `users_soft_deleted_total` | Time series |
| DB Connection Pool | `users_db_connection_pool_utilization` | Gauge |

### Event Processing Dashboard

**Purpose:** Monitor auth event consumption and user event publishing health.

| Panel | Metric |
|---|---|
| Auth Events Consumed Rate | `rate(users_auth_events_consumed_total[5m])` by event_type |
| Event Processing Lag | `users_auth_events_lag_seconds` by event_type |
| Event Processing Duration | `histogram_quantile(0.99, rate(users_auth_events_processing_duration_seconds_bucket[5m]))` |
| User Events Published Rate | `rate(users_events_published_total[5m])` by event_type |
| Dead Letter Queue Depth | `users_dead_letter_queue_depth` |

### Graph API Health Dashboard

**Purpose:** Monitor Microsoft Graph API integration health.

| Panel | Metric |
|---|---|
| Graph API Call Rate | `rate(users_graph_api_calls_total[5m])` by operation |
| Graph API Error Rate | `rate(users_graph_api_errors_total[5m])` by error_code |
| Graph API Latency P99 | `histogram_quantile(0.99, rate(users_graph_api_duration_seconds_bucket[5m]))` |
| Cache Hit Ratio | `users_graph_api_cache_hit_ratio` |

## Prometheus Alert Rules

### Critical Alerts (Page)

| Alert | Condition | For | Description |
|---|---|---|---|
| UsersServiceDown | `absent(users_service_up == 1)` | 1m | Service is down |
| UsersServiceHighErrorRate | 5xx rate > 2% | 2m | Error rate exceeds threshold |
| UsersServiceP99LatencyBreached | p99 > 1000ms | 3m | High latency detected |
| UsersServiceAuthDepFailed | Auth Service JWKS fetch failing | 1m | Cannot validate tokens |
| UsersServiceDatabaseDown | DB health check failing | 30s | Database unreachable |

### Warning Alerts (Slack)

| Alert | Condition | For |
|---|---|---|
| EventProcessingLagHigh | Lag > 60 seconds | 5m |
| EventProcessingErrorRate | Event processing errors > 5% | 5m |
| GraphApiErrorRate | Graph API errors > 10% | 5m |
| DbConnectionPoolHigh | Pool utilization > 80% | 5m |
| SoftDeleteQueueGrowing | Soft-deleted count growing > 10%/hour | 1h |
| DqlMessagesAccumulating | DLQ depth > 100 | 5m |

## PagerDuty Integration

- **Service:** Users Service Production
- **Integration type:** Prometheus Alertmanager
- **Escalation:** SRE Primary -> SRE Secondary (15 min) -> Engineering Manager (15 min)
- **Alert enrichment:** Grafana dashboard link, runbook link, alert details

## Error Budget Policy

### Calculation

For 99.95% availability over a 30-day window:
```
allowable_unavailability = 30 * 24 * 3600 * (1 - 0.9995) = 1296 seconds
```

### Budget Consequences

| Budget State | Policy |
|---|---|
| >= 50% remaining | Normal deployments |
| 20%-50% | Deployments require SRE approval |
| 5%-20% | Deployment freeze except hotfixes |
| < 5% | Full freeze, reliability focus only |
| Exhausted (0%) | Complete freeze, post-mortem required |

### Quarterly SLO Review

Review covers SLO attainment, budget consumption patterns, policy effectiveness, and target tuning.
