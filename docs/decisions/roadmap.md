# Hoja de Ruta — Users Service

- **Estado:** Aprobado
- **Propietario:** Equipo de Platform Engineering
- **Última actualización:** 2026-07-20

## Visión General

Este documento define la hoja de ruta de características planificadas para el Users Service hasta Q1 2027. Las características están organizadas por trimestre con hitos, criterios de éxito y dependencias.

---

## Q3 2026 (Julio — Septiembre)

### 1. Enriquecimiento de Perfil con Microsoft Graph API

**Descripción:** Integrar con Microsoft Graph API para enriquecer perfiles de usuario con datos de Entra ID, incluyendo foto de perfil, departamento, gerente, título del puesto y ubicación de oficina. El enriquecimiento se ejecuta asíncronamente en la creación de usuario y en un ciclo de actualización programado.

**Hitos:**

| Hito | Fecha Objetivo | Entregable |
|---|---|---|
| Revisión de diseño | 2026-07-25 | Arquitectura de integración, modelo de permisos |
| Cliente Graph API | 2026-08-10 | Cliente HTTP autenticado, gestión de tokens |
| Enriquecimiento de perfil en creación de usuario | 2026-08-30 | Pipeline de enriquecimiento asíncrono desencadenado por evento user.created |
| Actualización programada de enriquecimiento | 2026-09-15 | Trabajo diario en segundo plano para actualización de perfiles obsoletos |
| Capa de caché | 2026-09-25 | Caché Redis de respuestas de Graph API (TTL de 1 hora) |
| Lanzamiento GA | 2026-09-30 | Enriquecimiento de perfil habilitado para todos los usuarios |

**Criterios de Éxito:**
- El enriquecimiento de perfil se completa para el 95% de los usuarios dentro de los 5 minutos posteriores a la creación.
- Límites de tasa de Graph API respetados (máx. 10,000 solicitudes por cada 10 minutos por inquilino).
- Tasa de aciertos de caché > 80% para perfiles de acceso frecuente.
- Degradación gradual: los usuarios creados cuando Graph API no está disponible usan solo datos locales.

### 2. Modelo RBAC Mejorado

**Descripción:** Extender el modelo de control de acceso basado en roles de los tres roles actuales (Admin, Operator, User) para admitir roles personalizados con permisos granulares. Introducir conjuntos de permisos que puedan componerse en roles.

**Hitos:**

| Hito | Fecha Objetivo | Entregable |
|---|---|---|
| Diseño de esquema RBAC | 2026-08-01 | Modelo de permisos, jerarquía de roles |
| API CRUD de roles personalizados | 2026-08-20 | `POST/GET/PUT/DELETE /api/v2/roles` |
| Asignación de permisos | 2026-09-05 | Asignar permisos a roles, validar en middleware |
| Asignación de roles a usuarios | 2026-09-20 | `POST /api/v2/users/{id}/roles` |
| Lanzamiento GA | 2026-09-30 | RBAC mejorado con roles personalizados |

### 3. Paginación por Cursor para Endpoints de Listado

**Descripción:** Reemplazar la paginación basada en desplazamiento con paginación basada en cursor para todos los endpoints de listado. Mejora de rendimiento para grandes conjuntos de datos y paginación consistente en toda la plataforma.

**Hitos:**

| Hito | Fecha Objetivo | Entregable |
|---|---|---|
| Implementación de paginación por cursor | 2026-08-10 | Cursor codificado en Base64URL, paginación keyset |
| Migración de endpoints existentes | 2026-08-25 | Todos los endpoints de listado usan paginación por cursor |
| Obsolecencia de v1 compatible hacia atrás | 2026-09-01 | Paginación por desplazamiento obsoleta con encabezado Sunset |

---

## Q4 2026 (Octubre — Diciembre)

### 1. Aprovisionamiento SCIM 2.0

**Descripción:** Implementar endpoints de servidor SCIM 2.0 para aprovisionamiento automatizado de usuarios desde proveedores de identidad (Entra ID, Okta). Ver future-integrations.md para detalles.

### 2. Operaciones de Usuario por Lote

**Descripción:** Soporte para operaciones masivas de creación, actualización y eliminación de usuarios (hasta 1000 usuarios por solicitud). Incluye entrada CSV/JSON, resumen de validación, informe de errores y procesamiento idempotente.

**Hitos:**

| Hito | Fecha Objetivo | Entregable |
|---|---|---|
| Revisión de diseño | 2026-10-01 | Esquema de operación por lote, modelo de error |
| Creación por lote | 2026-10-20 | `POST /api/v2/users/bulk` |
| Actualización por lote | 2026-11-05 | `PATCH /api/v2/users/bulk` |
| Soft-delete por lote | 2026-11-15 | `POST /api/v2/users/bulk/delete` |
| Informe de resultados | 2026-12-01 | Informe de éxito/error por elemento |
| Lanzamiento GA | 2026-12-15 | Operaciones por lote disponibles |

### 3. Registro de Auditoría para Mutaciones de Usuario

**Descripción:** Implementar registro de auditoría completo para todas las mutaciones de usuario, capturando estado anterior/posterior, identidad del actor, marca de tiempo y dirección IP. Los registros de auditoría son inmutables y se almacenan separadamente de los logs operativos.

---

## Q1 2027 (Enero — Marzo)

### 1. Gestión Avanzada de Inquilinos

**Descripción:** Creación de inquilinos en autoservicio, gestión de configuración de inquilinos, banderas de funcionalidad a nivel de inquilino e informes de uso de inquilinos.

**Hitos:**

| Hito | Fecha Objetivo | Entregable |
|---|---|---|
| API de creación de inquilinos | 2027-01-15 | `POST /api/v2/tenants` |
| Configuración de inquilinos | 2027-02-01 | Banderas de funcionalidad, configuraciones por inquilino |
| Informes de uso de inquilinos | 2027-02-15 | Métricas de uso por inquilino |
| Lanzamiento GA | 2027-03-01 | Gestión de inquilinos en autoservicio |

### 2. Exportación de Datos de Usuario (Portabilidad GDPR)

**Descripción:** Implementar portabilidad de datos del Artículo 20 del GDPR: exportar todos los datos de usuario en formato JSON legible por máquina, incluyendo perfil, roles, historial de actividad.

### 3. Trabajo de Purga de Hard-Delete

**Descripción:** Trabajo en segundo plano que elimina permanentemente usuarios que han estado en soft-delete por más de 90 días (requisito de cumplimiento). Incluye período de retención configurable, notificación previa a la eliminación y registro de auditoría.

---

## Consideraciones Futuras

- **Grupos de Usuarios:** Gestión de grupos con soporte de grupos anidados.
- **Delegación de Usuarios:** Delegación temporal de acceso entre usuarios.
- **Gestión de Perfil de Autoservicio:** Permitir a los usuarios actualizar sus propios campos de perfil.
- **Flujos de Aprobación:** Aprobación de múltiples pasos para cambios de rol de usuario.
- **Sincronización de Grupos de Entra ID:** Sincronización automática de grupos de usuarios desde Entra ID.
