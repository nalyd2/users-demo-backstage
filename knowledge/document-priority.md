# Prioridad de Documentos para Recuperacion con IA

## Niveles de Prioridad

| Prioridad | Descripcion | Uso en RAG |
|---|---|---|
| **P0 -- Critico** | Debe recuperarse primero para consultas relevantes | Siempre incluido en los resultados principales |
| **P1 -- Alto** | Altamente relevante para dominios especificos | Incluido cuando el dominio coincide |
| **P2 -- Medio** | Contexto util, no urgente | Incluido cuando Top-K > 3 |
| **P3 -- Bajo** | Informacion de contexto | Incluido solo si hay coincidencia explicita con la consulta |

## Matriz de Prioridad de Documentos

| Documento | Prioridad | Tipos de Consulta Principales |
|---|---|---|
| `docs/architecture/overview.md` | P1 | "Que es el Servicio de Usuarios?", "Resumen de arquitectura" |
| `docs/architecture/context.md` | P1 | "Como encaja el Servicio de Usuarios en la plataforma?", "De que depende el Servicio de Usuarios?" |
| `docs/architecture/containers.md` | P1 | "Que bases de datos usa el Servicio de Usuarios?", "Contenedores y entornos de ejecucion" |
| `docs/architecture/components.md` | P1 | "Como esta estructurado internamente el Servicio de Usuarios?", "Que componentes tiene?" |
| `docs/architecture/deployment-view.md` | P2 | "Donde esta desplegado el Servicio de Usuarios?" |
| `docs/architecture/security.md` | P0 | "Como se valida JWT?", "Cual es el modelo RBAC?", "Como se aplica el aislamiento de inquilinos?", "Manejo de PII" |
| `docs/architecture/technology-stack.md` | P2 | "Que version de .NET?", "Inventario de tecnologia", "Versiones de dependencias" |
| `docs/api/users-api.md` | P0 | "Como creo/actualizo/elimino usuarios?", "Referencia de API", "Que campos son obligatorios?" |
| `docs/api/events.md` | P1 | "Que eventos publica el Servicio de Usuarios?", "Eventos de autenticacion consumidos", "Esquema de eventos" |
| `docs/api/variables.md` | P1 | "Como configuro el Servicio de Usuarios?", "Variables de entorno" |
| `docs/runbooks/incident-response.md` | P0 | "El Servicio de Usuarios esta caido -- que hago?" |
| `docs/runbooks/restart-service.md` | P0 | "Como reinicio el servicio?" |
| `docs/runbooks/deployment.md` | P1 | "Como despliego cambios?" |
| `docs/runbooks/rollback.md` | P0 | "Como revierto un despliegue fallido?" |
| `docs/runbooks/operations.md` | P2 | "Como roto las credenciales de la base de datos?", "Mantenimiento rutinario", "Solucion de problemas de sincronizacion de perfiles" |
| `docs/adr/*.md` | P2 | "Por que elegimos PostgreSQL sobre MongoDB?", "Justificacion de arquitectura", "ADR-002 Validacion JWT" |
| `docs/onboarding/*.md` | P2 | "Como configuro el entorno de desarrollo local?", "Preguntas de nuevos desarrolladores" |
| `docs/decisions/security-guidelines.md` | P1 | "Requisitos de seguridad", "Cual es nuestra politica de seguridad?" |
| `docs/decisions/dependencies.md` | P1 | "De que depende el Servicio de Usuarios?", "Dependencia del Servicio de Autenticacion" |
| `docs/decisions/*` (otros) | P3 | Preguntas generales de politicas |
| `README.md` | P1 | Preguntas de nivel inicial, resumen de endpoints, relaciones de la plataforma |
| `openapi.yaml` | P0 | Consultas de especificacion de API, esquema de endpoints, formas de solicitud/respuesta |
| `mkdocs.yml` | P3 | Consultas de estructura de documentacion |

## Prioridad en el Pipeline de Recuperacion

La puntuacion de prioridad se combina con la puntuacion de similitud vectorial:

```
puntuacion_final = (0.7 × similitud_coseno) + (0.3 × puntuacion_prioridad_normalizada)
```

Donde `puntuacion_prioridad_normalizada` = P0:1.0, P1:0.75, P2:0.5, P3:0.25

## Documentos Relacionados

- [Sources of Truth](sources-of-truth.md)
- [RAG](rag.md)
- [Indexing Strategy](indexing-strategy.md)
