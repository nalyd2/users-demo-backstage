# Estrategia de Indexacion

## Que Indexar

Los siguientes tipos de documentos estan **incluidos** en el indice de busqueda de IA:

| Tipo de Documento | Prioridad | Justificacion |
|---|---|---|
| Documentos de arquitectura (`docs/architecture/`) | **Alta** | Conocimiento central -- diseno del sistema, contexto, contenedores, dependencia JWT del Servicio de Autenticacion |
| Referencia de API (`docs/api/`) | **Alta** | Consultados frecuentemente -- especificaciones de endpoints para CRUD de usuarios, eventos, variables de configuracion |
| Runbooks (`docs/runbooks/`) | **Alta** | Respuesta a incidentes -- critico para IA operativa cuando el Servicio de Usuarios se degrada |
| ADRs (`docs/adr/`) | **Media** | Contexto de decisiones -- util para entender por que se eligio PostgreSQL sobre MongoDB, diseno de validacion JWT dual |
| Incorporacion (`docs/onboarding/`) | **Media** | Habilitacion de desarrolladores -- importante para nuevos miembros del equipo que configuran el servicio |
| Decisiones (`docs/decisions/`) | **Media** | Politicas y estandares -- definicion de RBAC, politicas de dependencias, directrices de seguridad |
| `README.md` (raiz) | **Alta** | Punto de entrada -- resumen general, resumen de endpoints y enlaces rapidos para el Servicio de Usuarios |
| `openapi.yaml` | **Media** | Especificacion de API estructurada -- consultable por endpoint para operaciones de gestion de usuarios |
| `catalog-info.yaml` | **Baja** | Metadatos de Backstage -- propiedad, vinculacion de sistema/dominio, declaraciones de dependencias |

## Que NO Indexar

| Excluido | Razon |
|---|---|
| Directorio `generated/` | Ya auto-generado; indexar la fuente, no la salida |
| `.gitignore`, `.editorconfig` | Configuracion de herramientas, no documentacion |
| `LICENSE` | Texto legal -- sin valor de recuperacion para IA |
| `Dockerfile`, `azure-pipelines.yml` | Similares a codigo; pueden referenciarse pero no fragmentarse |
| Codigo fuente (`src/`) | Excluido del indice de documentacion; la busqueda de codigo es un sistema separado |
| Codigo de prueba (`tests/`) | Excluido; no relevante para preguntas y respuestas de documentacion |

## Actualizacion del Indice

| Disparador | Accion |
|---|---|
| Push a la rama `main` | Re-indexacion completa de archivos modificados |
| Fusion de PR | Actualizacion incremental del indice |
| Nocturno (02:00 UTC) | Re-indexacion completa (recuperacion de actualizaciones perdidas) |
| Disparador manual | Re-indexacion completa mediante pipeline de Azure DevOps |

## Documentos Relacionados

- [Chunking](chunking.md)
- [Document Priority](document-priority.md)
- [Sources of Truth](sources-of-truth.md)
