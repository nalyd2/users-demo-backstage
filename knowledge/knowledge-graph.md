# Grafo de Conocimiento

## Descripcion General

Un grafo de conocimiento representa entidades y sus relaciones extraidas de la documentacion del Servicio de Usuarios. Permite consultas basadas en grafos como "Muestrame todos los servicios que dependen del Servicio de Usuarios" o "Que sucede si el Servicio de Autenticacion no esta disponible?"

## Tipos de Entidad

| Tipo de Entidad | Fuente | Ejemplo |
|---|---|---|
| **Servicio** | `catalog-info.yaml` -> `kind: Component` | `users-service`, `auth-service` |
| **API** | `catalog-info.yaml` -> `kind: API` | `users-api`, `auth-api` |
| **Sistema** | `catalog-info.yaml` -> `kind: System` | `user-management-system`, `iam-system` |
| **Dominio** | `catalog-info.yaml` -> `kind: Domain` | `identity` |
| **Recurso** | `catalog-info.yaml` -> `kind: Resource` | `users-database`, `auth-redis-cache` |
| **Tecnologia** | `docs/architecture/technology-stack.md` | `.NET 10`, `PostgreSQL 16`, `Dapper`, `Npgsql` |
| **ADR** | `docs/adr/*.md` | `ADR-001 -- PostgreSQL sobre MongoDB`, `ADR-002 -- Validacion JWT a nivel de Gateway vs. Servicio` |
| **Endpoint** | Rutas de `openapi.yaml` | `GET /api/users`, `POST /api/users`, `PUT /api/users/{id}` |
| **Evento** | `docs/api/events.md` | `user.created`, `user.updated`, `user.deleted` |
| **Evento Consumido** | `docs/api/events.md` | `user.login`, `user.logout`, `token.revoked` |
| **Perfil de Usuario** | `catalog-info.yaml` + esquema | `user (sub, roles, tid)`, `tenant` |
| **Rol** | Esquema de seguridad de `openapi.yaml` | `admin`, `operator`, `user` |
| **Inquilino** | Documentos de arquitectura, modelo RBAC | `tenant-uuid` -- limite de aislamiento de multi-inquilino |
| **Registro de Auditoria** | `docs/architecture/containers.md` | `tabla audit_log`, `tabla event_deduplication` |
| **Runbook** | `docs/runbooks/*.md` | `restart-service`, `incident-response` |
| **Equipo** | `catalog-info.yaml` -> `spec.owner` | `platform-engineering` |

## Tipos de Relacion

| Relacion | Campo Fuente | Ejemplo |
|---|---|---|
| `DEPENDS_ON` | `catalog-info.yaml` -> `spec.dependsOn` | `users-service -> auth-service` |
| `PROVIDES_API` | `catalog-info.yaml` -> `spec.providesApis` | `users-service -> users-api` |
| `CONSUMES_API` | `catalog-info.yaml` -> `spec.consumesApis` | `users-service -> auth-api` |
| `PART_OF` | `catalog-info.yaml` -> `spec.system` | `users-service -> user-management-system` |
| `OWNS` | `catalog-info.yaml` -> `spec.owner` | `platform-engineering -> users-service` |
| `PUBLISHES` | `docs/api/events.md` | `users-service -> user.created` |
| `SUBSCRIBES_TO` | `docs/api/events.md` | `users-service -> user.login` (de auth-service) |
| `USES_TECHNOLOGY` | `docs/architecture/technology-stack.md` | `users-service -> PostgreSQL 16` |
| `HAS_RUNBOOK` | Mapeado por convencion | `users-service -> restart-service` |
| `RELATED_ADR` | Enlaces de referencia cruzada | `users-service -> ADR-002` |
| `ENFORCES_RBAC` | `docs/architecture/security.md` | `users-service -> admin` |
| `STORES_IN` | `docs/architecture/containers.md` | `users-service -> users-database` |
| `VALIDATES_WITH` | `docs/architecture/context.md` | `users-service -> auth-service (JWT)` |
| `ISOLATES_TENANT` | `docs/architecture/security.md` | `users-service -> tenant` |
| `AUDITS_TO` | `docs/architecture/containers.md` | `users-service -> tabla audit_log` |

## Construccion del Grafo

### Pipeline de Extraccion de Fuentes

```yaml
# Conceptual -- no es codigo implementado
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
      source: todos los enlaces markdown
      output: relationships (related, documented_in)
```

### Base de Datos de Grafos

| Atributo | Valor |
|---|---|
| **Base de Datos** | Neo4j (gestionado) o Azure Cosmos DB API Gremlin |
| **Actualizacion** | Incremental en cada push a `main` |
| **Reconstruccion** | Reconstruccion completa nocturna a las 03:00 UTC |

## Consultas de Ejemplo

### Encontrar todos los dependientes del Servicio de Usuarios

```cypher
MATCH (s:Service {name: 'users-service'})<-[:DEPENDS_ON]-(dependent)
RETURN dependent.name, dependent.type
```

### Encontrar todas las APIs consumidas por el Sistema de Gestion de Usuarios

```cypher
MATCH (sys:System {name: 'user-management-system'})<-[:PART_OF]-(c:Component)-[:CONSUMES_API]->(api:API)
RETURN api.name, c.name
```

### Encontrar todos los eventos consumidos por el Servicio de Usuarios y sus fuentes

```cypher
MATCH (s:Service {name: 'users-service'})-[:SUBSCRIBES_TO]->(e:Event)<-[:PUBLISHES]-(publisher)
RETURN e.name AS Event, publisher.name AS Publisher
```

### Analisis de impacto: Que se rompe si el Servicio de Autenticacion se cae?

```cypher
MATCH (auth:Service {name: 'auth-service'})<-[:DEPENDS_ON]-(dependent:Service)
OPTIONAL MATCH (dependent)-[:VALIDATES_WITH]->(auth)
RETURN dependent.name, dependent.type,
       CASE WHEN dependent.name = 'users-service' THEN 'La validacion JWT falla despues de que expira el cache JWKS (TTL de 5 min)' ELSE 'Rotura de dependencia' END AS Impact
```

### RBAC -- que roles pueden eliminar usuarios?

```cypher
MATCH (s:Service {name: 'users-service'})-[:ENFORCES_RBAC]->(r:Role)
MATCH (s)-[:PROVIDES_API]->(api:API)
RETURN api.name, r.name AS Role, r.allowed_actions AS AllowedActions
```

### Aislamiento de inquilinos -- que recursos estan limitados por inquilino?

```cypher
MATCH (s:Service {name: 'users-service'})-[:STORES_IN]->(res:Resource)
MATCH (res)-[:ISOLATES_TENANT]->(t:Tenant)
RETURN res.name, t.name
```

## Documentos Relacionados

- [Sources of Truth](sources-of-truth.md)
- [RAG](rag.md)
- [Metadata](metadata.md)
