# Generated Topology Data

Infrastructure and dependency graphs produced by CI/CD for the Users Service.

| Artifact | Description |
|---|---|
| `dependency-graph.json` | Service dependency graph for Backstage visualization — users-service depends on auth-service, consumes auth-api, uses PostgreSQL users-database, and subscribes to Service Bus auth-events |
| `network-policy-report.json` | Kubernetes network policy compliance — validates that only API Gateway and allowed services can reach the Users Service pods |
| `component-map.svg` | Auto-generated C4 component diagram showing the Users Web API, Event Consumer, Profile Sync Worker, and their connections to PostgreSQL, Auth Service, Service Bus, and Azure AD |

**Not committed.**
