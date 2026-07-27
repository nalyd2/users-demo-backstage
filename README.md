# Users Service (`users-service`)

> **Part of the Internal Developer Platform (IDP)** — User Management domain.
> Registered in Backstage via `catalog-info.yaml`. Documentation rendered with TechDocs.

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](./LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Lifecycle](https://img.shields.io/badge/lifecycle-production-green.svg)](./catalog-info.yaml)
[![Backstage](https://img.shields.io/badge/Backstage-Registered-6868FF.svg)](./catalog-info.yaml)

---

## 📖 Overview

The **Users Service** (`users-service`) manages the complete user lifecycle for the Internal Developer Platform (IDP). It provides CRUD operations on user profiles, integrates with the [Authentication Service](https://backstage.internal/platform/component/auth-service) for JWT validation, and consumes authentication events to maintain an up‑to‑date view of user activity.

| Attribute | Value |
|---|---|
| **System** | `user-management-system` |
| **Domain** | `identity` |
| **Owner** | Platform Engineering Team |
| **Lifecycle** | `production` |
| **Technology** | .NET 10, ASP.NET Core Minimal APIs |
| **Authentication** | JWT validation via Auth Service |
| **Database** | PostgreSQL (Users DB) |

---

## 🧭 Quick Links

| Resource | Location |
|---|---|
| 📘 **TechDocs** (rendered in Backstage) | [`docs/index.md`](./docs/index.md) |
| 🗂️ **Catalog definition** | [`catalog-info.yaml`](./catalog-info.yaml) |
| 🔌 **OpenAPI 3.1 spec** | [`openapi.yaml`](./openapi.yaml) |
| 📐 **Architecture docs** | [`docs/architecture/`](./docs/architecture/) |
| 📋 **Runbooks** | [`docs/runbooks/`](./docs/runbooks/) |
| 🔧 **Onboarding** | [`docs/onboarding/`](./docs/onboarding/) |
| 📜 **ADRs** | [`docs/adr/`](./docs/adr/) |
| 🧠 **AI / RAG preparation** | [`knowledge/`](./knowledge/) |
| 📦 **Generated artifacts** | [`generated/`](./generated/) |
| 🚀 **CI/CD pipeline** | [`azure-pipelines.yml`](./azure-pipelines.yml) |

---

## 👥 Endpoints

| Method | Path | Description |
|---|---|---|
| `GET`    | `/api/users`        | List all users (paginated, filterable) |
| `GET`    | `/api/users/{id}`   | Get a single user by ID |
| `POST`   | `/api/users`        | Create a new user profile |
| `PUT`    | `/api/users/{id}`   | Update an existing user profile |
| `DELETE` | `/api/users/{id}`   | Soft-delete a user profile |
| `GET`    | `/api/health`       | Liveness and readiness probe |

All mutating endpoints require a valid JWT issued by the Authentication Service.

Full API reference: [`docs/api/users-api.md`](./docs/api/users-api.md)

---

## 🔗 Platform Relationships

```
                      DependsOn
users-service ─────────────────────► auth-service
                           JWT validation via POST /api/auth/refresh

                      ConsumesApi
users-service ─────────────────────► auth-api

                      Subscribes
users-service ─────────────────────► auth-service
               user.login, user.logout events
```

See [`docs/decisions/dependencies.md`](./docs/decisions/dependencies.md) for the full dependency map.

---

## 🔑 Authentication Flow

```
Client                     Users Service              Auth Service
  │                              │                          │
  │  GET /api/users              │                          │
  │  (Authorization: Bearer JWT) │                          │
  │─────────────────────────────►│                          │
  │                              │                          │
  │                              │ Validate JWT             │
  │                              │ (local RS256 public key) │
  │                              │                          │
  │                              │ (if JWT expired)         │
  │                              │ POST /api/auth/refresh   │
  │                              │─────────────────────────►│
  │                              │◄─────────────────────────│
  │                              │                          │
  │◄─────────────────────────────│                          │
  │  200 OK + [users]            │                          │
```

---

## 🚀 Getting Started (Local)

```bash
# Prerequisites: .NET 10 SDK
dotnet restore src/UsersService/UsersService.csproj
dotnet run --project src/UsersService/UsersService.csproj

# The service listens on https://localhost:7201
# Swagger UI: https://localhost:7201/swagger
```

Detailed instructions: [`docs/onboarding/local-development.md`](./docs/onboarding/local-development.md)

---

## 🧱 Repository Structure

```
.
├── src/                    # Application source code (.NET 10)
├── tests/                  # Unit & integration tests
├── docs/                   # All documentation (TechDocs)
├── generated/              # CI/CD-generated artifacts
├── knowledge/              # AI/RAG indexing preparation
├── catalog-info.yaml       # Backstage entity registration
├── mkdocs.yml              # TechDocs / MkDocs configuration
├── openapi.yaml            # OpenAPI 3.1 specification
├── azure-pipelines.yml     # CI/CD pipeline (Azure DevOps)
├── Dockerfile              # Container image definition
└── .editorconfig           # Shared code-style conventions
```

---

## 📊 Platform Context

This service is one component of a larger enterprise platform. The documentation in this repository assumes the existence of other services — particularly the Authentication Service — infrastructure, and tools that may not be present in this standalone reference implementation. For the full platform architecture, see [`docs/architecture/context.md`](./docs/architecture/context.md).

---

## 📄 License

MIT — see [LICENSE](./LICENSE).

---

_Maintained by the Platform Engineering Team. For questions, open an issue or contact `#platform-eng` on Slack._
