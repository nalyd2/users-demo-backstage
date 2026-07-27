# Dependency Inventory — Users Service

- **Status:** Approved
- **Owner:** Platform Engineering Team
- **Last Updated:** 2026-07-20

## Dependency Graph Overview

The Users Service depends on the Auth Service for JWT validation and consumes events from the Auth Service. It maintains its own isolated PostgreSQL database and publishes user events for downstream consumers.

```mermaid
graph TB
    subgraph "Platform Services"
        auth["Auth Service<br/>(auth-service)"]
        users["Users Service<br/>(users-service)"]
        gateway["API Gateway"]
        audit["Audit Service"]
        notify["Notification Service"]
    end

    subgraph "Azure Infrastructure"
        pg_users["PostgreSQL 16<br/>Users DB (Isolated)"]
        sb["Azure Service Bus<br/>Premium"]
        kv["Azure Key Vault"]
        graph_api["Microsoft Graph API"]
        acr["Azure Container Registry"]
        aks["Azure Kubernetes Service"]
    end

    subgraph "External"
        entra_id["Microsoft Entra ID"]
    end

    %% Dependency: Users depends on Auth for JWT validation
    users -.->|"JWKS / JWT Validation"| auth
    users -->|"Npgsql / TLS 1.3"| pg_users
    users -->|"Azure.Messaging.ServiceBus"| sb
    users -->|"Azure.Security.KeyVault"| kv
    users -->|"Microsoft Graph SDK"| graph_api

    %% Event flows
    auth -->|"auth-events (login, logout)"| sb
    sb -->|"user.created, user.updated, user.deleted"| users
    sb -->|"user events"| audit
    sb -->|"user events"| notify

    %% Auth -> Users indirect dependency
    gateway -->|"JWT Bearer Token"| users
    auth -->|"JWKS endpoint"| gateway

    style users fill:#6BBF59,color:#fff
    style auth fill:#4A90D9,color:#fff
    style pg_users fill:#336791,color:#fff
    style graph_api fill:#0078D4,color:#fff
```

### Users Service to Auth Service Dependency

The critical dependency of Users Service on Auth Service:

| Direction | Mechanism | Description |
|---|---|---|
| Users Service -> Auth Service | JWKS endpoint (`.well-known/jwks.json`) | Users Service fetches and caches Auth Service's public keys for local JWT signature verification. No direct synchronous HTTP call exists; the JWKS endpoint is polled periodically (cache TTL: 5 minutes). |
| Users Service <- Auth Service | Events via Service Bus topic `auth-events` | Users Service consumes `user.login`, `user.logout`, and `token.revoked` events to update user session state and trigger profile actions. |

**Impact of Auth Service unavailability:** If the Auth Service is unavailable:
- New JWKS keys cannot be fetched (cached keys continue to work for up to 5 minutes).
- After cache expiry, JWT validation fails and all requests to Users Service are rejected.
- Auth events are not received (users session state becomes stale).
- User CRUD operations that don't require fresh token validation continue to work (cached JWKS).

---

## 1. Runtime Dependencies

### 1.1 Users Database (PostgreSQL 16) — Isolated

| Attribute | Detail |
|---|---|
| **Service** | Azure Database for PostgreSQL — Flexible Server |
| **SKU** | General Purpose, 4 vCores, 32 GB RAM, 512 GB SSD |
| **Version** | 16.x |
| **Purpose** | Persistent storage for user profiles, tenant data, RBAC assignments, soft-delete tracking |
| **Isolation** | This database is EXCLUSIVE to the Users Service. No other service has direct access. Auth Service uses a separate database instance. |
| **Connection** | Npgsql 9.x, TLS 1.3, SCRAM-SHA-256 authentication |
| **Pool** | Min 10, Max 50 connections per instance |
| **High Availability** | Zone-redundant standby (West Europe), read replica in North Europe |
| **Backup** | 35-day point-in-time restore, geo-redundant |
| **Degraded Mode** | If database is unreachable, all user operations fail. Read-only operations may use the read replica if configured. |

### 1.2 Azure Service Bus

| Attribute | Detail |
|---|---|
| **Service** | Azure Service Bus Premium |
| **Topics consumed** | `auth-events` (subscription: `users-service-auth-events`) |
| **Topics published** | `user-events` (for consumers: audit, notification services) |
| **Events consumed** | `user.login`, `user.logout`, `token.revoked` |
| **Events published** | `user.created`, `user.updated`, `user.deleted`, `user.restored` |
| **Retention** | 7 days |
| **Dead-Letter** | After 10 failed delivery attempts, events moved to DLQ |
| **Degraded Mode** | If Service Bus is unavailable, consumed events are queued by Azure (up to 7 days). Published events are dropped with a warning. |

### 1.3 Microsoft Graph API

| Attribute | Detail |
|---|---|
| **Service** | Microsoft Graph API v1.0 |
| **Purpose** | Profile enrichment: fetch user photo, department, manager, and organization data from Entra ID |
| **Permissions** | `User.Read.All` (read profiles), `User.ReadWrite.All` (update profiles) |
| **Authentication** | OAuth 2.0 client credentials grant with client secret (stored in Key Vault) |
| **Rate Limits** | Microsoft Graph: 10,000 requests per 10 minutes per tenant |
| **Cache** | Graph API responses cached for 1 hour to reduce API calls |
| **Degraded Mode** | If Graph API is unavailable, profile enrichment is skipped. User profiles still served with locally-stored data. |

### 1.4 Azure Key Vault

| Attribute | Detail |
|---|---|
| **Purpose** | Stores database connection string, Service Bus connection string, Graph API client secret |
| **Access** | Azure Managed Identity |
| **Degraded Mode** | Cached secrets continue to work; service cannot start if Key Vault is unreachable at startup |

### 1.5 Auth Service (Indirect Dependency)

| Attribute | Detail |
|---|---|
| **Purpose** | JWT token validation via JWKS endpoint |
| **Connection** | HTTP GET to `https://auth.example.com/.well-known/jwks.json` (polled every 5 minutes) |
| **Caching** | JWKS document cached in memory with 5-minute TTL |
| **Degraded Mode** | Cached JWKS keys work for up to 5 minutes. After cache expiry, all requests fail authentication. |
| **Fallback** | None — the Auth Service is the single identity source |

---

## 2. Build Dependencies (NuGet Packages)

### 2.1 Runtime NuGet Packages

| Package | Version | Purpose |
|---|---|---|
| Npgsql | 9.* | PostgreSQL data provider |
| Npgsql.EntityFrameworkCore.PostgreSQL | 9.* | EF Core provider for PostgreSQL |
| Microsoft.EntityFrameworkCore | 10.* | ORM for data access |
| Azure.Messaging.ServiceBus | 7.* | Event publishing and consumption |
| Azure.Security.KeyVault.Secrets | 4.* | Secret retrieval |
| Azure.Identity | 1.* | Managed Identity authentication |
| Microsoft.Graph | 5.* | Microsoft Graph API client |
| Microsoft.Graph.Core | 3.* | Graph API core HTTP infrastructure |
| FluentValidation | 11.* | Request DTO validation |
| FluentValidation.DependencyInjectionExtensions | 11.* | DI integration |
| Serilog.AspNetCore | 8.* | Structured logging |
| OpenTelemetry.Exporter.Prometheus.AspNetCore | 1.* | Prometheus metrics |
| OpenTelemetry.Extensions.Hosting | 1.* | OTEL integration |

### 2.2 Test Packages

| Package | Purpose |
|---|---|
| xunit | Test framework |
| NSubstitute | Mocking |
| FluentAssertions | Readable assertions |
| Testcontainers.PostgreSql | Ephemeral PostgreSQL for integration tests |
| WireMock.Net | HTTP endpoint mocking (for JWKS and Graph API) |

---

## 3. Deployment Dependencies

| Resource | Configuration |
|---|---|
| AKS | Standard_D4s_v5 nodes, 3 zones, 4 replicas per zone |
| ACR | `acrplatform.azurecr.io/users-service:{tag}` |
| Helm | Deployment: 4 replicas, requests 500m CPU / 512 MiB, limits 2000m CPU / 2 GiB |
| HPA | Target CPU 70%, min 4, max 10 per zone |
| PDB | Min available: 2 per zone |

---

## 4. Dependency Summary

| Dependency | Type | Critical? | Degraded Mode |
|---|---|---|---|
| PostgreSQL (Users DB) | Runtime | Yes | No — all operations blocked |
| Auth Service (JWKS) | Runtime (indirect) | Yes | 5-minute cached JWKS grace window |
| Service Bus | Runtime | No | Events queued (consume) or dropped (publish) |
| Microsoft Graph API | Runtime | No | Profile enrichment skipped |
| Azure Key Vault | Runtime | Startup-critical | Cached secrets |
| NuGet packages | Build | — | Pinned versions with lock file |
| AKS / ACR | Deployment | — | Blue/Green deployment |
