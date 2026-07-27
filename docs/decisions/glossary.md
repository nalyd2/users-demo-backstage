# Glosario — Users Service

- **Estado:** Aprobado
- **Propietario:** Equipo de Platform Engineering
- **Última actualización:** 2026-07-20

## Visión General

Este glosario define la terminología utilizada en toda la documentación, el código fuente y los procesos operativos del Users Service. Los términos reflejan el dominio de gestión de usuarios, la multi-tenencia, RBAC y la arquitectura basada en eventos.

---

### Actor (Actor)

La identidad (usuario o principal de servicio) que realizó una operación. Cada mutación en el Users Service se registra con una identidad de actor extraída del reclamo `sub` del JWT. El actor es distinto del usuario objetivo (el sujeto de la operación). Los registros de auditoría siempre capturan tanto al actor como al objetivo.

### Eventos de Auth (Auth Events)

Eventos publicados por el Auth Service en el tema `auth-events` de Azure Service Bus. El Users Service consume eventos `user.login`, `user.logout` y `token.revoked` para actualizar el estado de sesión del usuario y desencadenar acciones del ciclo de vida. Los eventos de auth llevan un contexto de traza para trazabilidad distribuida de extremo a extremo a través de los límites del servicio.

### Paginación por Cursor (Cursor Pagination)

Una técnica de paginación que utiliza un cursor opaco (un puntero codificado en base64 a un registro específico) en lugar de números de página o desplazamientos. El Users Service utiliza paginación basada en cursor para todos los endpoints de listado. La paginación por cursor ofrece resultados consistentes incluso cuando los datos cambian entre páginas, evita el costo de rendimiento de la paginación basada en desplazamiento para grandes conjuntos de datos y previene ataques de enumeración de recursos.

```json
{
  "data": [...],
  "pagination": {
    "next_cursor": "eyJpZCI6ICIxMjM0In0=",
    "prev_cursor": null,
    "has_more": true
  }
}
```

### Enriquecimiento de Perfil de Entra ID (Entra ID Profile Enrichment)

El proceso asíncrono de enriquecer perfiles de usuario con datos de Microsoft Graph API. Cuando se crea un usuario, el Users Service intenta obtener atributos adicionales del perfil (departamento, título del puesto, gerente, ubicación de oficina, foto de perfil) desde Entra ID usando Microsoft Graph API. El enriquecimiento no es bloqueante: la creación de usuario se realiza incluso si Graph API no está disponible. Los datos enriquecidos se almacenan en caché y se actualizan según un programa configurable.

### Hard-Delete (Borrado Permanente)

Eliminación permanente de un registro de usuario de la base de datos. El Users Service no expone una API pública de hard-delete (ver known-limitations.md). El hard-delete es realizado por un trabajo programado de purga que elimina permanentemente usuarios que han estado en soft-delete por más de 90 días (el período de retención es configurable). El hard-delete es irreversible y se registra como un evento de auditoría crítico.

### Procesamiento de Eventos Idempotente (Idempotent Event Processing)

La propiedad de que procesar el mismo evento múltiples veces produce el mismo resultado que procesarlo una vez. El Users Service implementa el procesamiento de eventos idempotente mediante el seguimiento de IDs de eventos procesados en un almacén de desduplicación (Redis). Si el mismo evento de auth se entrega dos veces (entrega al menos una vez de Azure Service Bus), el segundo intento se ignora silenciosamente. Esto garantiza consistencia incluso durante reinicios del consumidor o interrupciones de red.

### Validación JWT (Contexto del Users Service)

El proceso de verificar que un JWT entrante fue emitido por el Auth Service y no ha sido manipulado. El Users Service valida los JWT mediante: (1) obtención de las claves públicas del Auth Service desde el endpoint JWKS, (2) verificación de la firma del JWT usando la clave identificada por el encabezado `kid`, (3) verificación de la expiración del JWT (`exp`), (4) validación de que el reclamo `aud` coincida con la audiencia del Users Service, y (5) verificación de que el JWT no ha sido revocado (mediante la lista negra de tokens). Las claves JWKS se almacenan en caché durante 5 minutos para minimizar la dependencia del Auth Service.

### Multi-Tenencia (Multi-Tenancy)

Una arquitectura donde una sola instancia del Users Service sirve a múltiples inquilinos (organizaciones o clientes) con estricto aislamiento de datos. Cada registro de usuario incluye un campo `tenant_id` que identifica al inquilino propietario. Todas las consultas en la capa de repositorio incluyen un filtro `WHERE tenant_id = @tenantId` para prevenir el acceso a datos entre inquilinos. El ID de inquilino se extrae de los reclamos del JWT (no de los parámetros de solicitud) para prevenir la suplantación de inquilinos.

### RBAC (Control de Acceso Basado en Roles)

El modelo de autorización utilizado por el Users Service. Tres roles incorporados definen la jerarquía de permisos:

| Rol | Permisos |
|---|---|
| **Admin** | Acceso completo: crear/leer/actualizar/eliminar usuarios, gestionar roles, gestionar configuración de inquilino, ver registros de auditoría |
| **Operator** | Acceso operativo: crear/leer/actualizar usuarios (no puede eliminar, no puede gestionar roles ni configuración de inquilino) |
| **User** | Acceso de autoservicio: leer su propio perfil, actualizar su propio perfil (futuro), ver sus propios roles |

Los roles se aplican a nivel de middleware mediante el atributo `[Authorize(Roles = "...")]` y se validan contra el reclamo `roles` del JWT. Roles personalizados con conjuntos de permisos granulares están planificados para Q3 2026.

### Jerarquía de Roles (Role Hierarchy)

La relación entre roles RBAC donde un rol con mayores privilegios hereda los permisos de roles con menores privilegios. En el Users Service: Admin hereda los permisos de Operator, Operator hereda los permisos de User. La jerarquía de roles se aplica en el middleware de autorización y no es configurable por el usuario en la implementación actual.

### Estado de Sesión (Session State)

El registro de si un usuario está actualmente "activo" (tiene una sesión de inicio de sesión activa) o "inactivo" (cerró sesión o la sesión expiró). El estado de sesión se deriva de los eventos de auth (el inicio de sesión establece el estado como activo, el cierre de sesión establece el estado como inactivo) y se almacena en la base de datos del Users Service. El estado de sesión se utiliza para decisiones de autorización (ej., denegar acceso a usuarios inactivos) y para informes (ej., conteo de usuarios activos por inquilino).

### Soft-Delete (Borrado Lógico)

Un patrón de eliminación donde los registros se marcan como eliminados (se establece una marca de tiempo `deleted_at`) en lugar de eliminarse físicamente de la base de datos. Los usuarios en soft-delete se excluyen de todos los resultados de consulta por defecto (las consultas incluyen `WHERE deleted_at IS NULL`). El soft-delete permite la recuperación de datos dentro de la ventana de retención (90 días) y proporciona un registro de auditoría de los eventos de eliminación. Un trabajo programado de purga elimina permanentemente los usuarios en soft-delete después de que expire el período de retención.

### Aislamiento de Inquilino (Tenant Isolation)

El mecanismo de aplicación que evita que usuarios de un inquilino accedan a datos pertenecientes a otro inquilino. El Users Service implementa aislamiento de inquilino en múltiples capas:

1. **Capa de autenticación:** El ID de inquilino está incrustado en el JWT por el Auth Service y no puede ser modificado por el cliente.
2. **Capa de repositorio:** Cada consulta a la base de datos incluye un parámetro de filtro `tenant_id`.
3. **Capa de base de datos (defensa en profundidad):** Las políticas de seguridad a nivel de fila (RLS) de PostgreSQL aplican el aislamiento de inquilino a nivel de base de datos, proporcionando protección incluso si se omite la capa de aplicación.
4. **Pruebas:** Las pruebas de integración automatizadas verifican que el acceso entre inquilinos siempre devuelva 403 Forbidden.

### Inquilino (Tenant)

Una unidad organizativa aislada dentro del Users Service. Cada inquilino tiene sus propios usuarios, roles, configuraciones y banderas de funcionalidad. Los inquilinos se identifican mediante un `tenant_id` UUID que delimita todas las operaciones. Los inquilinos se crean mediante la API de gestión de inquilinos (ver roadmap.md).

### Eventos de Usuario (User Events)

Eventos publicados por el Users Service en el tema `user-events` de Azure Service Bus. Estos eventos notifican a los servicios posteriores sobre cambios en el ciclo de vida del usuario: `user.created`, `user.updated`, `user.deleted`, `user.restored`. Cada evento incluye el ID de usuario, ID de actor, marca de tiempo, ID de correlación y una carga útil de campos cambiados. Los consumidores incluyen el Audit Service (para registro de auditoría), Notification Service (para notificaciones por correo electrónico/Slack) y Auth Service (para invalidación de tokens al eliminar usuario).

### Perfil de Usuario (User Profile)

La colección de atributos de usuario almacenados y gestionados por el Users Service. El perfil de usuario incluye campos de identidad centrales (ID de usuario, correo electrónico, nombre, apellido), campos organizativos (ID de inquilino, roles, departamento, título del puesto, gerente) y campos del sistema (estado, created_at, updated_at, deleted_at, last_login_at).

### UUIDv4

Identificador Único Universal versión 4 — un identificador de 128 bits generado utilizando números aleatorios. El Users Service utiliza UUIDv4 para todos los identificadores de recursos (IDs de usuario, IDs de inquilino, IDs de rol). Los identificadores UUIDv4 no son secuenciales y no pueden ser enumerados, proporcionando protección contra ataques de adivinación de recursos.

### Escalamiento Vertical (Vertical Scaling)

Escalamiento mediante el aumento de recursos (CPU, memoria) de instancias existentes en lugar de añadir más instancias. El Users Service utiliza escalamiento horizontal (añadir réplicas), pero el escalamiento vertical está disponible como contingencia operativa para picos de carga inesperados hasta que el escalamiento horizontal se ponga al día.
