# Users Service — Documentation Home

Welcome to the technical documentation for the **Users Service** (`users-service`), the User Lifecycle Management microservice of the Internal Developer Platform (IDP).

This documentation is designed to be rendered in **Backstage via TechDocs** and follows the [Diátaxis](https://diataxis.fr/) framework.

---

## 📐 Architecture

Understand the system's design, context, and technical decisions.

| Document | Description |
|---|---|
| [Overview](architecture/overview.md) | High-level architectural overview and design principles |
| [System Context](architecture/context.md) | How the service fits into the broader platform ecosystem, including dependency on Auth Service |
| [Container View](architecture/containers.md) | Runtime containers, data stores, and their interactions |
| [Component View](architecture/components.md) | Internal component structure and design patterns |
| [Deployment View](architecture/deployment-view.md) | Multi-region deployment topology and infrastructure |
| [Security Architecture](architecture/security.md) | Authentication flow via Auth Service, authorization model, threat model |
| [Technology Stack](architecture/technology-stack.md) | Complete technology inventory with version matrix |

## 🔌 API Reference

Integrate with the service.

| Document | Description |
|---|---|
| [Users API](api/users-api.md) | CRUD endpoints for user profile management |
| [Events](api/events.md) | Domain events consumed from Auth Service and published by this service |
| [Variables & Configuration](api/variables.md) | Environment variables, configuration keys, and feature flags |

## 📋 Runbooks

Operational procedures for on-call engineers.

| Document | Description |
|---|---|
| [Restart Service](runbooks/restart-service.md) | Safe service restart procedure |
| [Deployment](runbooks/deployment.md) | Step-by-step deployment guide |
| [Incident Response](runbooks/incident-response.md) | Incident classification and response playbooks |
| [Rollback](runbooks/rollback.md) | Rollback procedure for failed deployments |
| [Operations](runbooks/operations.md) | Day-2 operational tasks and maintenance |

## 🔧 Onboarding

Get started as a developer on this service.

| Document | Description |
|---|---|
| [Developer Guide](onboarding/developer-guide.md) | Architecture walkthrough for new team members |
| [Local Development](onboarding/local-development.md) | Setting up a local development environment |
| [How to Debug](onboarding/how-to-debug.md) | Debugging techniques and common issues |
| [Testing](onboarding/testing.md) | Testing strategy, frameworks, and running tests |

## 📜 Architecture Decision Records

Key architectural decisions and their rationale.

| ADR | Description |
|---|---|
| [ADR-001](adr/ADR-001.md) | PostgreSQL over MongoDB for user profiles |
| [ADR-002](adr/ADR-002.md) | JWT Validation at Gateway vs. Service Level |
| [ADR-003](adr/ADR-003.md) | Event-Driven User State Synchronization |

## 📊 Decisions & Standards

Cross-cutting standards and governance documents.

| Document | Description |
|---|---|
| [Coding Standards](decisions/coding-standards.md) | Code style, patterns, and conventions |
| [Branching Strategy](decisions/branching.md) | Git branching model and release flow |
| [Versioning](decisions/versioning.md) | Semantic versioning policy |
| [Security Guidelines](decisions/security-guidelines.md) | Security requirements and compliance |
| [Observability](decisions/observability.md) | Logging, metrics, and tracing strategy |
| [Dependencies](decisions/dependencies.md) | Internal and external dependency map |
| [Ownership](decisions/ownership.md) | Team ownership and contact information |
| [Monitoring](decisions/monitoring.md) | Dashboards, alerts, and SLOs |
| [Roadmap](decisions/roadmap.md) | Planned features and milestones |
| [Future Integrations](decisions/future-integrations.md) | Planned integrations with other systems |
| [Risks](decisions/risks.md) | Known risks and mitigation strategies |
| [Known Limitations](decisions/known-limitations.md) | Current technical and operational constraints |
| [Glossary](decisions/glossary.md) | Terminology used across the platform |

---

## 🔗 Platform Links

| Resource | URL |
|---|---|
| Backstage Component | `https://backstage.internal/platform/component/users-service` |
| Source Code (Azure DevOps) | `https://dev.azure.com/platform/_git/users-service` |
| CI/CD Pipeline | `https://dev.azure.com/platform/_build?definitionId=101` |
| Grafana Dashboard | `https://grafana.internal/d/users/users-service` |
| On-Call (PagerDuty) | `https://pagerduty.internal/services/users-service` |

---

## 📞 Contact

| Role | Team | Slack |
|---|---|---|
| **Service Owner** | Platform Engineering | `#platform-eng` |
| **On-Call** | Platform SRE | `#platform-sre` |
| **Security** | InfoSec | `#infosec` |

---

_Maintained by the Platform Engineering Team. Last updated: 2026-07-26._
