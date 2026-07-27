# Datos de Topología Generados

Grafos de infraestructura y dependencias producidos por CI/CD para el Servicio de Usuarios.

| Artefacto | Descripción |
|---|---|
| `dependency-graph.json` | Grafo de dependencias del servicio para visualización en Backstage — users-service depende de auth-service, consume auth-api, usa la base de datos PostgreSQL users-database y se suscribe a los eventos de Service Bus auth-events |
| `network-policy-report.json` | Cumplimiento de políticas de red de Kubernetes — valida que solo API Gateway y los servicios permitidos puedan alcanzar los pods del Servicio de Usuarios |
| `component-map.svg` | Diagrama de componentes C4 generado automáticamente que muestra la API Web de Usuarios, el Consumidor de Eventos, el Trabajador de Sincronización de Perfiles y sus conexiones con PostgreSQL, Auth Service, Service Bus y Azure AD |

**No confirmado en el repositorio.**
