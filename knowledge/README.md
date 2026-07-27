# Preparacion de Base de Conocimiento para IA / RAG

## Proposito

Este directorio define la **arquitectura de documentos** para las funcionalidades impulsadas por IA que consumiran la documentacion del Servicio de Usuarios -- especificamente Generacion Aumentada por Recuperacion (RAG), grafos de conocimiento y busqueda semantica. No contiene codigo ni modelos de IA. Es la especificacion que un equipo de IA/ML utilizaria para indexar la documentacion de este repositorio.

## Principio Rector

> **La documentacion ES la fuente de verdad.** El pipeline de IA debe indexar exactamente lo que un humano lee -- nada menos, nada inventado.

## Documentos

| Documento | Descripcion |
|---|---|
| [indexing-strategy.md](indexing-strategy.md) | Que indexar, que excluir y por que |
| [chunking.md](chunking.md) | Como se dividen los documentos en fragmentos incrustables |
| [metadata.md](metadata.md) | Esquema de metadatos para cada fragmento de documento |
| [embeddings.md](embeddings.md) | Seleccion y configuracion del modelo de incrustacion |
| [rag.md](rag.md) | Arquitectura del pipeline RAG y estrategia de recuperacion |
| [knowledge-graph.md](knowledge-graph.md) | Reglas de extraccion de entidades y construccion de grafos |
| [sources-of-truth.md](sources-of-truth.md) | Fuentes canonicas y su precedencia |
| [document-priority.md](document-priority.md) | Orden de prioridad para indexacion y clasificacion de recuperacion |

## Casos de Uso de IA Objetivo

| Caso de Uso | Descripcion | Prioridad |
|---|---|---|
| **Preguntas y Respuestas sobre Gestion de Usuarios** | "Como creo un nuevo usuario y asigno roles?" -> respuesta desde la documentacion | P0 |
| **Consultas de Permisos** | "Que rol necesito para actualizar el perfil de un usuario?" -> recuperacion de matriz RBAC | P0 |
| **Asistencia en Incidentes** | "El Servicio de Usuarios muestra `503` al crear usuario -- que debo revisar?" -> extraccion de runbook | P0 |
| **Descubrimiento de Arquitectura** | "Muestrame todos los servicios que dependen del Servicio de Usuarios" -> recorrido de grafo de conocimiento | P1 |
| **Auditoria de Aislamiento de Inquilinos** | "Como estan aislados los usuarios entre inquilinos?" -> recuperacion de arquitectura de seguridad | P1 |
| **Copiloto de Incorporacion** | "Soy nuevo -- guiame en la configuracion de desarrollo local para el Servicio de Usuarios" -> tutorial guiado | P1 |
| **Analisis de Impacto de Dependencias** | "Si el Servicio de Autenticacion falla, como se degrada el Servicio de Usuarios?" -> analisis de dependencias y circuit-breaker | P2 |
| **Auditoria de Cumplimiento** | "Muestrame la politica de retencion de datos para campos PII" -> recuperacion entre documentos | P2 |

## Taxonomia de Documentos

La documentacion utiliza marcadores de taxonomia implicitos y explicitos que el pipeline de IA debe extraer:

```yaml
# Implicito en la jerarquia de navegacion de mkdocs.yml
Architecture > Security > Authentication Flow
Runbooks > Incident Response > Users Service Unavailable
API Reference > Users API > POST /api/users

# Explicito en el frontmatter del documento (cuando esta presente)
---
category: architecture
subcategory: security
tags: [jwt, rbac, tenant-isolation, multi-tenancy]
priority: high
audience: [developers, sre, operators]
---
```

## Documentos Relacionados

- [Sources of Truth](sources-of-truth.md)
- [Document Priority](document-priority.md)
- [RAG Architecture](rag.md)
