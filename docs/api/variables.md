# Variables & Configuration

## Overview

The Users Service follows **12-factor app** principles. All configuration is provided via environment variables or Azure Key Vault references.

## Environment Variables

### Required

| Variable | Description | Example |
|---|---|---|
| `ConnectionStrings__UsersDb` | PostgreSQL connection string | `Host=...;Database=users;Username=...` |
| `AuthService__Endpoint` | Auth Service gRPC endpoint | `https://auth-service.platform.svc.cluster.local:5103` |
| `ServiceBus__ConnectionString` | Azure Service Bus connection string | `Endpoint=sb://...` |
| `ServiceBus__AuthEventsSubscription` | Subscription name for auth events | `users-service` |
| `ServiceBus__UsersEventsTopic` | Topic for publishing user events | `users-events` |
| `KeyVault__Uri` | Azure Key Vault URI | `https://platform-kv-we.vault.azure.net/` |

### Optional

| Variable | Default | Description |
|---|---|---|
| `Users__DefaultPageSize` | `20` | Default page size for list endpoints |
| `Users__MaxPageSize` | `100` | Maximum allowed page size |
| `Users__SoftDeleteRetentionDays` | `30` | Days before purging soft-deleted users |
| `Auth__JWKSCacheTtlMinutes` | `5` | JWKS local cache TTL |
| `Auth__GrpcTimeoutMs` | `500` | gRPC call timeout for token validation |
| `Auth__CircuitBreakerThreshold` | `5` | Consecutive failures before opening circuit |
| `Auth__CircuitBreakerDurationSeconds` | `30` | Circuit open duration |
| `GraphApi__SyncEnabled` | `false` | Enable Entra ID profile sync |
| `GraphApi__SyncSchedule` | `0 2 * * *` | Cron expression for nightly sync |
| `Logging__MinimumLevel` | `Information` | Minimum log level |

## Azure Key Vault Secrets

| Secret Name | Description | Rotation |
|---|---|---|
| `users-db-connection-string` | PostgreSQL connection string | 180 days |
| `users-service-bus-connection` | Service Bus connection string | 180 days |
| `auth-service-grpc-cert` | Client certificate for mTLS to Auth Service | 365 days |

## Feature Flags

| Flag | Default | Description |
|---|---|---|
| `GraphApiSync.Enabled` | `false` | Enable Entra ID profile enrichment |
| `EventPublishing.Enabled` | `true` | Publish user lifecycle events |
| `StrictRoleValidation.Enabled` | `true` | Reject unknown roles in requests |
| `SelfServiceDelete.Enabled` | `false` | Allow users to delete their own accounts |

## Environment-Specific Configuration

| Setting | dev | qa | staging | production |
|---|---|---|---|---|
| `Users__DefaultPageSize` | 10 | 20 | 20 | 20 |
| `Users__SoftDeleteRetentionDays` | 7 | 14 | 30 | 30 |
| `Auth__JWKSCacheTtlMinutes` | 1 | 5 | 5 | 5 |
| `Auth__GrpcTimeoutMs` | 2000 | 500 | 500 | 500 |
| `Logging__MinimumLevel` | Debug | Information | Information | Warning |
| `GraphApiSync.Enabled` | false | true | true | true |

## Related Documents

- [Technology Stack](../architecture/technology-stack.md)
- [Security Architecture](../architecture/security.md)
- [Local Development](../onboarding/local-development.md)
