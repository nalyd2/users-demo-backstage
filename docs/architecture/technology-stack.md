# Technology Stack

## Scope

Complete inventory of technologies used by the Users Service, including runtime, libraries, infrastructure, and development tools. Serves as the authoritative reference for dependency management.

## Technology Lifecycle Policy

| Phase | Description | Action |
|---|---|---|
| **Adopt** | Recommended for new projects | Use freely |
| **Trial** | Under evaluation | Use in non-critical paths |
| **Hold** | In use but not for new work | Plan migration |
| **Deprecate** | Being phased out | Migrate away |

---

## 1. Runtime & Framework

| Technology | Version | Phase | Purpose |
|---|---|---|---|
| **.NET SDK** | 10.0.100 | Adopt | Runtime and base class library |
| **ASP.NET Core** | 10.0 | Adopt | Web API framework (Minimal APIs) |
| **C#** | 13 | Adopt | Primary programming language |
| **gRPC** | 2.x | Adopt | Client for Auth Service token validation |

## 2. Core Libraries

| Package | Version | Phase | Purpose |
|---|---|---|---|
| `Dapper` | 2.x | Adopt | Lightweight ORM for PostgreSQL |
| `Npgsql` | 9.x | Adopt | .NET data provider for PostgreSQL |
| `FluentValidation` | 11.x | Adopt | Request DTO validation |
| `Azure.Messaging.ServiceBus` | 7.x | Adopt | Event publisher and consumer |
| `Azure.Security.KeyVault.Secrets` | 4.x | Adopt | Key Vault secret retrieval |
| `Azure.Identity` | 1.x | Adopt | Managed Identity authentication |
| `Microsoft.Graph` | 5.x | Adopt | Entra ID profile enrichment |
| `Grpc.Net.Client` | 2.x | Adopt | gRPC client for Auth Service |
| `Polly` | 8.x | Adopt | Resilience policies (circuit breaker, retry) |
| `MessagePack` | 2.x | Adopt | Binary serialization for caching |

## 3. Observability

| Technology | Version | Phase | Purpose |
|---|---|---|---|
| **OpenTelemetry SDK** | 1.x | Adopt | Distributed tracing (W3C Trace Context) |
| **Serilog** | 8.x | Adopt | Structured JSON logging |
| **Prometheus.Client** | 5.x | Adopt | Metrics exposition |

## 4. Infrastructure (Azure)

| Service | SKU / Tier | Purpose |
|---|---|---|
| **Azure Kubernetes Service** | Standard_D4s_v5 | Container orchestration |
| **Azure Database for PostgreSQL** | Flexible Server, General Purpose | User profile storage |
| **Azure Service Bus** | Premium, zone-redundant | Event publishing and subscription |
| **Azure Key Vault** | Standard | Connection strings and secrets |
| **Azure Traffic Manager** | Priority routing | Multi-region failover |
| **Azure Container Registry** | Premium, geo-replicated | Docker image storage |

## 5. Development & Quality

| Technology | Version | Phase | Purpose |
|---|---|---|---|
| **xUnit** | 2.x | Adopt | Unit and integration testing |
| **FluentAssertions** | 7.x | Adopt | Readable test assertions |
| **NSubstitute** | 5.x | Adopt | Mocking framework |
| **Testcontainers** | 4.x | Adopt | Integration tests with PostgreSQL |
| **SonarQube** | Cloud | Adopt | Static code analysis |
| **Mend (WhiteSource)** | Cloud | Adopt | Open-source vulnerability scanning |

## 6. CI/CD

| Technology | Purpose |
|---|---|
| **Azure DevOps Pipelines** | CI/CD orchestration |
| **Docker BuildX** | Multi-arch container image builds |
| **Cosign** | Container image signing |

## 7. Documentation

| Technology | Purpose |
|---|---|
| **MkDocs** | Static site generator |
| **Material for MkDocs** | Theme and UI |
| **Mermaid** | Diagrams-as-code |
| **OpenAPI 3.1** | API specification |
| **Swagger UI** | Interactive API exploration (dev only) |

## Version Compatibility Matrix

| .NET Version | C# Version | ASP.NET Core | Support Until |
|---|---|---|---|
| 10.0 | 13 | 10.0 | LTS — Nov 2027 |
| 9.0 | 13 | 9.0 | STS — May 2026 |
| 8.0 | 12 | 8.0 | LTS — Nov 2026 |

> **Current target:** .NET 10.0

## Dependency Update Policy

| Update Type | Frequency | Approval |
|---|---|---|
| **Patch (security)** | Within 48 hours | Auto-merge if CI passes |
| **Patch (non-security)** | Weekly | Auto-merge |
| **Minor** | Monthly | Team lead review |
| **Major** | Quarterly (planned) | Architecture review |
| **.NET SDK** | Within 2 weeks | CI + staging validation |

## Related Documents

- [Architecture Overview](overview.md)
- [ADR-001 — PostgreSQL over MongoDB](../adr/ADR-001.md)
- [Coding Standards](../decisions/coding-standards.md)
- [Security Guidelines](../decisions/security-guidelines.md)
