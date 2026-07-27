# Knowledge Graph

## Overview

A knowledge graph represents entities and their relationships extracted from the Users Service documentation. It enables graph-based queries like "Show me all services that depend on the Users Service" or "What happens if the Auth Service is unavailable?"

## Entity Types

| Entity Type | Source | Example |
|---|---|---|
| **Service** | `catalog-info.yaml` -> `kind: Component` | `users-service`, `auth-service` |
| **API** | `catalog-info.yaml` -> `kind: API` | `users-api`, `auth-api` |
| **System** | `catalog-info.yaml` -> `kind: System` | `user-management-system`, `iam-system` |
| **Domain** | `catalog-info.yaml` -> `kind: Domain` | `identity` |
| **Resource** | `catalog-info.yaml` -> `kind: Resource` | `users-database`, `auth-redis-cache` |
| **Technology** | `docs/architecture/technology-stack.md` | `.NET 10`, `PostgreSQL 16`, `Dapper`, `Npgsql` |
| **ADR** | `docs/adr/*.md` | `ADR-001 — PostgreSQL over MongoDB`, `ADR-002 — JWT Validation at Gateway vs. Service Level` |
| **Endpoint** | `openapi.yaml` paths | `GET /api/users`, `POST /api/users`, `PUT /api/users/{id}` |
| **Event** | `docs/api/events.md` | `user.created`, `user.updated`, `user.deleted` |
| **Consumed Event** | `docs/api/events.md` | `user.login`, `user.logout`, `token.revoked` |
| **User Profile** | `catalog-info.yaml` + schema | `user (sub, roles, tid)`, `tenant` |
| **Role** | `openapi.yaml` security scheme | `admin`, `operator`, `user` |
| **Tenant** | Architecture docs, RBAC model | `tenant-uuid` — multi-tenancy isolation boundary |
| **Audit Log** | `docs/architecture/containers.md` | `audit_log table`, `event_deduplication table` |
| **Runbook** | `docs/runbooks/*.md` | `restart-service`, `incident-response` |
| **Team** | `catalog-info.yaml` -> `spec.owner` | `platform-engineering` |

## Relationship Types

| Relationship | Source Field | Example |
|---|---|---|
| `DEPENDS_ON` | `catalog-info.yaml` -> `spec.dependsOn` | `users-service -> auth-service` |
| `PROVIDES_API` | `catalog-info.yaml` -> `spec.providesApis` | `users-service -> users-api` |
| `CONSUMES_API` | `catalog-info.yaml` -> `spec.consumesApis` | `users-service -> auth-api` |
| `PART_OF` | `catalog-info.yaml` -> `spec.system` | `users-service -> user-management-system` |
| `OWNS` | `catalog-info.yaml` -> `spec.owner` | `platform-engineering -> users-service` |
| `PUBLISHES` | `docs/api/events.md` | `users-service -> user.created` |
| `SUBSCRIBES_TO` | `docs/api/events.md` | `users-service -> user.login` (from auth-service) |
| `USES_TECHNOLOGY` | `docs/architecture/technology-stack.md` | `users-service -> PostgreSQL 16` |
| `HAS_RUNBOOK` | Mapped by convention | `users-service -> restart-service` |
| `RELATED_ADR` | Cross-reference links | `users-service -> ADR-002` |
| `ENFORCES_RBAC` | `docs/architecture/security.md` | `users-service -> admin` |
| `STORES_IN` | `docs/architecture/containers.md` | `users-service -> users-database` |
| `VALIDATES_WITH` | `docs/architecture/context.md` | `users-service -> auth-service (JWT)` |
| `ISOLATES_TENANT` | `docs/architecture/security.md` | `users-service -> tenant` |
| `AUDITS_TO` | `docs/architecture/containers.md` | `users-service -> audit_log table` |

## Graph Construction

### Source Extraction Pipeline

```yaml
# Conceptual — not implemented code
pipeline:
  - extract_catalog_entities:
      source: catalog-info.yaml
      output: nodes (Service, API, System, Domain, Resource) + relationships

  - extract_openapi:
      source: openapi.yaml
      output: nodes (Endpoint) + relationships (service -> endpoint)

  - extract_events:
      source: docs/api/events.md
      output: nodes (Event, Consumed Event) + relationships (publishes, subscribes)

  - extract_technologies:
      source: docs/architecture/technology-stack.md
      output: nodes (Technology) + relationships (uses_technology)

  - extract_rbac:
      source: docs/architecture/security.md + openapi.yaml
      output: nodes (Role) + relationships (enforces_rbac, requires_role)

  - extract_tenants:
      source: docs/architecture/security.md
      output: nodes (Tenant) + relationships (isolates_tenant)

  - extract_cross_references:
      source: all markdown links
      output: relationships (related, documented_in)
```

### Graph Database

| Attribute | Value |
|---|---|
| **Database** | Neo4j (managed) or Azure Cosmos DB Gremlin API |
| **Update** | Incremental on each push to `main` |
| **Rebuild** | Full rebuild nightly at 03:00 UTC |

## Example Queries

### Find all dependents of the Users Service

```cypher
MATCH (s:Service {name: 'users-service'})<-[:DEPENDS_ON]-(dependent)
RETURN dependent.name, dependent.type
```

### Find all APIs consumed by the User Management System

```cypher
MATCH (sys:System {name: 'user-management-system'})<-[:PART_OF]-(c:Component)-[:CONSUMES_API]->(api:API)
RETURN api.name, c.name
```

### Find all events consumed by the Users Service and their sources

```cypher
MATCH (s:Service {name: 'users-service'})-[:SUBSCRIBES_TO]->(e:Event)<-[:PUBLISHES]-(publisher)
RETURN e.name AS Event, publisher.name AS Publisher
```

### Impact analysis: What breaks if the Auth Service goes down?

```cypher
MATCH (auth:Service {name: 'auth-service'})<-[:DEPENDS_ON]-(dependent:Service)
OPTIONAL MATCH (dependent)-[:VALIDATES_WITH]->(auth)
RETURN dependent.name, dependent.type,
       CASE WHEN dependent.name = 'users-service' THEN 'JWT validation fails after JWKS cache expires (5 min TTL)' ELSE 'Dependency break' END AS Impact
```

### RBAC — which roles can delete users?

```cypher
MATCH (s:Service {name: 'users-service'})-[:ENFORCES_RBAC]->(r:Role)
MATCH (s)-[:PROVIDES_API]->(api:API)
RETURN api.name, r.name AS Role, r.allowed_actions AS AllowedActions
```

### Tenant isolation — which resources are scoped per tenant?

```cypher
MATCH (s:Service {name: 'users-service'})-[:STORES_IN]->(res:Resource)
MATCH (res)-[:ISOLATES_TENANT]->(t:Tenant)
RETURN res.name, t.name
```

## Related Documents

- [Sources of Truth](sources-of-truth.md)
- [RAG](rag.md)
- [Metadata](metadata.md)
