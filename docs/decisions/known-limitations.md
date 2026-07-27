# Limitaciones Conocidas — Users Service

- **Estado:** Aprobado
- **Propietario:** Equipo de Platform Engineering
- **Última actualización:** 2026-07-20

## Visión General

Este documento cataloga las limitaciones conocidas actuales del Users Service. Cada entrada incluye impacto, motivo y ruta de resolución planificada.

---

## L01: Sin Gestión de Perfil de Autoservicio

| Atributo | Valor |
|---|---|
| **ID de Limitación** | U-L01 |
| **Impacto** | Los usuarios no pueden actualizar sus propios campos de perfil (nombre, correo electrónico, teléfono). Todos los cambios de perfil deben ser realizados por un Administrador u Operador. |
| **Motivo** | La gestión de perfiles de autoservicio requiere una interfaz de usuario orientada al usuario y reglas de validación cuidadosas (ej., el cambio de correo electrónico requiere verificación). |
| **Solución Alternativa** | Los usuarios pueden solicitar cambios de perfil mediante ticket de soporte; los Operadores ejecutan el cambio. |
| **Ruta de Resolución** | Planificado para Q1 2027. |

---

## L02: Sin Operaciones por Lote

| Atributo | Valor |
|---|---|
| **ID de Limitación** | U-L02 |
| **Impacto** | Los administradores deben crear, actualizar o eliminar usuarios uno a la vez. Las operaciones por lote para migraciones grandes (1000+ usuarios) requieren scripts personalizados o acceso directo a la base de datos. |
| **Motivo** | Las operaciones por lote requieren un diseño cuidadoso para idempotencia, informes de errores y límites de transacción. |
| **Solución Alternativa** | Script mediante llamadas API (limitado por límite de tasa). Scripts de migración directa a base de datos para operaciones únicas. |
| **Ruta de Resolución** | Planificado para Q4 2026. Ver roadmap.md. |

---

## L03: Sin Aprovisionamiento SCIM

| Atributo | Valor |
|---|---|
| **ID de Limitación** | U-L03 |
| **Impacto** | No hay aprovisionamiento automatizado de usuarios desde proveedores de identidad (Entra ID, Okta). Los usuarios deben crearse manualmente o mediante API. El desaprovisionamiento requiere acción manual. |
| **Motivo** | Implementación de SCIM 2.0 diferida a Q4 2026. |
| **Solución Alternativa** | Usuarios creados mediante API. Los usuarios de Entra ID se crean en el primer inicio de sesión mediante consumo de eventos de auth. |
| **Ruta de Resolución** | SCIM 2.0 planificado para Q4 2026. Ver future-integrations.md. |

---

## L04: Sin Gestión de Grupos

| Atributo | Valor |
|---|---|
| **ID de Limitación** | U-L04 |
| **Impacto** | No hay soporte para grupos de usuarios. La autorización basada en roles es solo por usuario. |
| **Motivo** | La gestión de grupos se difirió a una versión futura. |
| **Solución Alternativa** | Asignar roles directamente a los usuarios. Para equipos grandes, usar infraestructura como código (Terraform) para asignaciones de roles. |
| **Ruta de Resolución** | Grupos planificados para Q1 2027. |

---

## L05: Sin Búsqueda Avanzada / Búsqueda de Texto Completo

| Atributo | Valor |
|---|---|
| **ID de Limitación** | U-L05 |
| **Impacto** | La búsqueda de usuarios se limita a coincidencia exacta o por prefijo en campos de correo electrónico y nombre. No hay búsqueda de texto completo en los campos del perfil. |
| **Motivo** | La búsqueda de texto completo requiere índices tsvector de PostgreSQL o infraestructura de búsqueda dedicada (Elasticsearch). |
| **Solución Alternativa** | Usar consultas de coincidencia exacta con paginación por cursor. |
| **Ruta de Resolución** | Planificado para Q1 2027. |

---

## L06: Sin Flujos de Aprobación para Cambios de Rol

| Atributo | Valor |
|---|---|
| **ID de Limitación** | U-L06 |
| **Impacto** | Los cambios de rol surten efecto inmediatamente. No hay un flujo de aprobación para escalaciones de rol sensibles (ej., Usuario a Admin). |
| **Motivo** | La infraestructura de flujo de aprobación (notificación, máquina de estados, escalación) aún no está implementada. |
| **Solución Alternativa** | Aplicado operativamente: los Operadores deben coordinar los cambios de rol mediante Slack o tickets de soporte. El registro de auditoría proporciona supervisión retrospectiva. |
| **Ruta de Resolución** | Planificado para 2027. |

---

## L07: Sin Hard-Delete para Usuarios (Solo Soft-Delete)

| Atributo | Valor |
|---|---|
| **ID de Limitación** | U-L07 |
| **Impacto** | Los usuarios se eliminan con soft-delete (marcados como eliminados, datos retenidos). No hay API para eliminar permanentemente datos de usuario. El derecho al olvido del GDPR requiere una operación manual de base de datos. |
| **Motivo** | El hard-delete requiere una eliminación en cascada cuidadosa de datos relacionados y verificación de cumplimiento. |
| **Solución Alternativa** | Eliminación manual de base de datos por DBA con solicitud registrada. Los usuarios en soft-delete se purgan automáticamente después de 90 días (ver trabajo de purga en roadmap.md). |
| **Ruta de Resolución** | API de hard-delete planificada para Q1 2027. |

---

## L08: Sin Exportación de Datos de Usuario (Portabilidad GDPR)

| Atributo | Valor |
|---|---|
| **ID de Limitación** | U-L08 |
| **Impacto** | Los usuarios no pueden exportar sus datos personales en un formato legible por máquina (Artículo 20 del GDPR). Las solicitudes deben cumplirse manualmente. |
| **Motivo** | El endpoint de exportación de datos requiere un diseño cuidadoso para el alcance (qué datos se incluyen), formato (esquema JSON) y mecanismo de entrega. |
| **Solución Alternativa** | Exportación manual de base de datos por DBA con aprobación legal. |
| **Ruta de Resolución** | Planificado para Q1 2027. Ver roadmap.md. |

---

## L09: Sin Sincronización de Grupos de Entra ID

| Atributo | Valor |
|---|---|
| **ID de Limitación** | U-L09 |
| **Impacto** | La membresía de grupos de usuarios en Entra ID no se sincroniza con el Users Service. Los roles RBAC deben asignarse de forma independiente. |
| **Motivo** | La sincronización de grupos requiere un trabajo en segundo plano programado con seguimiento de cambios delta y resolución de conflictos. |
| **Solución Alternativa** | Asignar roles a los usuarios individualmente mediante API. |
| **Ruta de Resolución** | Planificado para 2027. |

---

## L10: Sin Visor de Registros de Auditoría

| Atributo | Valor |
|---|---|
| **ID de Limitación** | U-L10 |
| **Impacto** | Los registros de auditoría se escriben en almacenamiento pero no hay un visor incorporado ni interfaz de búsqueda para datos de auditoría. Investigar el historial de cambios de un usuario requiere consultar el almacén de auditoría directamente. |
| **Motivo** | El visor de registros de auditoría se difirió a una versión futura. |
| **Solución Alternativa** | Consultar datos de auditoría mediante Azure Monitor o directamente desde el almacenamiento de auditoría. |
| **Ruta de Resolución** | Planificado para 2027. |

---

## L11: Sin Soporte de Webhook para Eventos de Usuario

| Atributo | Valor |
|---|---|
| **ID de Limitación** | U-L11 |
| **Impacto** | Los sistemas externos no pueden recibir notificaciones de eventos de usuario en tiempo real mediante webhooks. Deben consultar la API o integrarse con Azure Service Bus directamente. |
| **Motivo** | La infraestructura de entrega de webhooks (registro, reintento, firma, desduplicación) no está implementada. |
| **Solución Alternativa** | Consumir eventos de usuario del tema `user-events` de Azure Service Bus directamente. |
| **Ruta de Resolución** | Planificado para 2027. |

---

## Resumen de Limitaciones

| ID | Limitación | Impacto | Resolución |
|---|---|---|---|
| U-L01 | Sin perfil de autoservicio | Medio | Q1 2027 |
| U-L02 | Sin operaciones por lote | Medio | Q4 2026 |
| U-L03 | Sin aprovisionamiento SCIM | Alto | Q4 2026 |
| U-L04 | Sin gestión de grupos | Medio | Q1 2027 |
| U-L05 | Sin búsqueda de texto completo | Bajo | Q1 2027 |
| U-L06 | Sin flujos de aprobación | Medio | 2027 |
| U-L07 | Sin API de hard-delete | Medio | Q1 2027 |
| U-L08 | Sin exportación de datos | Medio | Q1 2027 |
| U-L09 | Sin sincronización de grupos de Entra ID | Medio | 2027 |
| U-L10 | Sin visor de registros de auditoría | Bajo | 2027 |
| U-L11 | Sin soporte de webhook | Medio | 2027 |
