# Users Service — Inicio de Documentacion

Bienvenido a la documentacion tecnica del **Users Service** (`users-service`), el microservicio de Gestion del Ciclo de Vida de Usuarios de la Plataforma Interna de Desarrollo (IDP).

Esta documentacion esta disenada para renderizarse en **Backstage via TechDocs** y sigue el marco de trabajo [Diataxis](https://diataxis.fr/).

---

## 📐 Arquitectura

Comprende el diseno del sistema, su contexto y las decisiones tecnicas.

| Documento | Descripcion |
|---|---|
| [Vision General](architecture/overview.md) | Vision arquitectonica de alto nivel y principios de diseno |
| [Contexto del Sistema](architecture/context.md) | Como encaja el servicio en el ecosistema mas amplio de la plataforma, incluyendo la dependencia del Auth Service |
| [Vista de Contenedores](architecture/containers.md) | Contenedores en tiempo de ejecucion, almacenes de datos y sus interacciones |
| [Vista de Componentes](architecture/components.md) | Estructura interna de componentes y patrones de diseno |
| [Vista de Despliegue](architecture/deployment-view.md) | Topologia de despliegue multi-region e infraestructura |
| [Arquitectura de Seguridad](architecture/security.md) | Flujo de autenticacion via Auth Service, modelo de autorizacion, modelo de amenazas |
| [Stack Tecnologico](architecture/technology-stack.md) | Inventario tecnologico completo con matriz de versiones |

## 🔌 Referencia de la API

Integracion con el servicio.

| Documento | Descripcion |
|---|---|
| [API de Usuarios](api/users-api.md) | Endpoints CRUD para la gestion de perfiles de usuario |
| [Eventos](api/events.md) | Eventos de dominio consumidos del Auth Service y publicados por este servicio |
| [Variables y Configuracion](api/variables.md) | Variables de entorno, claves de configuracion y feature flags |

## 📋 Runbooks

Procedimientos operativos para ingenieros de guardia.

| Documento | Descripcion |
|---|---|
| [Reinicio del Servicio](runbooks/restart-service.md) | Procedimiento seguro de reinicio del servicio |
| [Despliegue](runbooks/deployment.md) | Guia de despliegue paso a paso |
| [Respuesta a Incidentes](runbooks/incident-response.md) | Clasificacion de incidentes y playbooks de respuesta |
| [Rollback](runbooks/rollback.md) | Procedimiento de rollback para despliegues fallidos |
| [Operaciones](runbooks/operations.md) | Tareas operativas del dia a dia y mantenimiento |

## 🔧 Onboarding

Introduccion para desarrolladores en este servicio.

| Documento | Descripcion |
|---|---|
| [Guia para Desarrolladores](onboarding/developer-guide.md) | Recorrido por la arquitectura para nuevos miembros del equipo |
| [Desarrollo Local](onboarding/local-development.md) | Configuracion de un entorno de desarrollo local |
| [Como Depurar](onboarding/how-to-debug.md) | Tecnicas de depuracion y problemas comunes |
| [Pruebas](onboarding/testing.md) | Estrategia de pruebas, frameworks y ejecucion de pruebas |

## 📜 Registro de Decisiones Arquitectonicas (ADRs)

Decisiones arquitectonicas clave y su fundamentacion.

| ADR | Descripcion |
|---|---|
| [ADR-001](adr/ADR-001.md) | PostgreSQL sobre MongoDB para perfiles de usuario |
| [ADR-002](adr/ADR-002.md) | Validacion JWT en Gateway vs. Nivel de Servicio |
| [ADR-003](adr/ADR-003.md) | Sincronizacion de Estado de Usuario Basada en Eventos |

## 📊 Decisiones y Estandares

Estandares transversales y documentos de gobierno.

| Documento | Descripcion |
|---|---|
| [Estandares de Codificacion](decisions/coding-standards.md) | Estilo de codigo, patrones y convenciones |
| [Estrategia de Ramificacion](decisions/branching.md) | Modelo de ramificacion Git y flujo de versiones |
| [Versionado](decisions/versioning.md) | Politica de versionado semantico |
| [Directrices de Seguridad](decisions/security-guidelines.md) | Requisitos de seguridad y cumplimiento |
| [Observabilidad](decisions/observability.md) | Estrategia de logging, metricas y tracing |
| [Dependencias](decisions/dependencies.md) | Mapa de dependencias internas y externas |
| [Propiedad](decisions/ownership.md) | Propiedad del equipo e informacion de contacto |
| [Monitoreo](decisions/monitoring.md) | Dashboards, alertas y SLOs |
| [Roadmap](decisions/roadmap.md) | Funcionalidades planificadas e hitos |
| [Integraciones Futuras](decisions/future-integrations.md) | Integraciones planificadas con otros sistemas |
| [Riesgos](decisions/risks.md) | Riesgos conocidos y estrategias de mitigacion |
| [Limitaciones Conocidas](decisions/known-limitations.md) | Limitaciones tecnicas y operativas actuales |
| [Glosario](decisions/glossary.md) | Terminologia utilizada en toda la plataforma |

---

## 🔗 Enlaces de la Plataforma

| Recurso | URL |
|---|---|
| Componente en Backstage | `https://backstage.internal/platform/component/users-service` |
| Codigo Fuente (Azure DevOps) | `https://dev.azure.com/platform/_git/users-service` |
| Pipeline CI/CD | `https://dev.azure.com/platform/_build?definitionId=101` |
| Dashboard Grafana | `https://grafana.internal/d/users/users-service` |
| Guardia (PagerDuty) | `https://pagerduty.internal/services/users-service` |

---

## 📞 Contacto

| Rol | Equipo | Slack |
|---|---|---|
| **Propietario del Servicio** | Platform Engineering | `#platform-eng` |
| **Guardia** | Platform SRE | `#platform-sre` |
| **Seguridad** | InfoSec | `#infosec` |

---

_Mantenido por el Platform Engineering Team. Ultima actualizacion: 2026-07-26._
