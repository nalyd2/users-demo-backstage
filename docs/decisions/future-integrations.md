# Integraciones Futuras — Users Service

- **Estado:** Borrador (Exploratorio)
- **Propietario:** Equipo de Platform Engineering
- **Última actualización:** 2026-07-20

## Visión General

Este documento cataloga las integraciones planificadas y exploratorias para el Users Service. Estas representan direcciones estratégicas para las capacidades de gestión de usuarios.

---

## 1. Aprovisionamiento SCIM 2.0 (Entrante desde IdPs)

### Motivación

Implementar un servidor SCIM 2.0 (RFC 7643, RFC 7644) para habilitar el aprovisionamiento automatizado de usuarios desde proveedores de identidad como Entra ID, Okta y Azure AD B2C. Esto elimina la creación manual de usuarios y garantiza la revocación oportuna de accesos.

### Enfoque de Integración

- Implementar endpoints SCIM 2.0: `POST /scim/v2/Users`, `GET /scim/v2/Users`, `PATCH /scim/v2/Users/{id}`, `DELETE /scim/v2/Users/{id}`.
- Compatibilidad con el esquema SCIM base con extensiones personalizadas para tenant_id y roles.
- Capa de mapeo de atributos entre el esquema SCIM y el modelo de perfil del Users Service.
- Mapeo de membresía de grupos SCIM a roles RBAC.
- Orientado a eventos: las operaciones SCIM publican eventos `user.created`, `user.updated`, `user.deleted`.

### Consideraciones de Implementación

| Aspecto | Detalle |
|---|---|
| Esquema | Usuario base + extensión de Usuario Empresarial + extensión de inquilino personalizada |
| Paginación | Paginación por cursor para `GET /Users` y `GET /Groups` |
| Lotes | Operaciones por lote RFC 7644 (maxOperations configurable) |
| Autenticación | OAuth 2.0 Bearer Token (clientes SCIM preconfigurados) |
| Filtrado | Compatibilidad con parámetro `filter` para userName, externalId, active |

### Riesgos

- El aprovisionamiento SCIM de Entra ID requiere descubrimiento de esquema específico y mapeo de atributos.
- La interpretación de los estándares SCIM varía entre proveedores; se requieren pruebas específicas por proveedor.
- Las operaciones por lote requieren manejo cuidadoso de errores e idempotencia.

### Esfuerzo Estimado: 6-8 semanas.

---

## 2. Aprovisionamiento SCIM 2.0 (Saliente a Sistemas Posteriores)

### Motivación

Publicar datos de perfil de usuario a sistemas de RRHH posteriores, plataformas de gobierno de identidad y servicios de directorio mediante llamadas de cliente SCIM 2.0.

### Enfoque de Integración

- Implementar cliente SCIM 2.0 que publique cambios de usuario en endpoints SCIM configurados.
- Reintento con retroceso exponencial para llamadas fallidas.
- Cola de mensajes fallidos (dead-letter) para destinos con fallos persistentes.

### Esfuerzo Estimado: 4-6 semanas.

---

## 3. Integración con Almacén de Usuarios de Okta / Auth0

### Motivación

Sincronizar perfiles de usuario con el directorio universal de Okta o Auth0 para organizaciones que utilizan estos como su almacén de identidad principal en lugar de Entra ID.

### Enfoque de Integración

- Implementar adaptador de sincronización de almacén de usuarios con API de Okta o API de Gestión de Auth0.
- Sincronización completa en la configuración inicial, sincronización delta mediante eventos webhook o sondeo programado.
- Estrategia de resolución de conflictos: última escritura gana con prioridad de origen configurable.

### Esfuerzo Estimado: 4-6 semanas por proveedor.

---

## 4. Integración con Sistemas de RRHH Externos (Workday, BambooHR)

### Motivación

Automatizar la gestión del ciclo de vida del usuario basada en eventos de RRHH: contratación (crear usuario), transferencia (actualizar departamento/rol), terminación (desactivar usuario).

### Enfoque de Integración

- Sincronización programada (diaria) desde la API del sistema de RRHH.
- Sincronización basada en eventos mediante webhook (si el sistema de RRHH lo admite).
- Mapeo de atributos de RRHH al modelo de perfil del Users Service.
- Flujo de aprobación para cambios desencadenados por RRHH.

### Riesgos

- Los sistemas de RRHH tienen diferentes modelos de datos y capacidades de API.
- La calidad de los datos de RRHH puede requerir validación y limpieza antes de su aplicación.
- Cumplimiento con regulaciones de retención de datos y privacidad.

### Esfuerzo Estimado: 6-8 semanas por sistema de RRHH.

---

## 5. Grupos de Usuarios y Membresía Dinámica de Grupos

### Motivación

Compatibilidad con autorización basada en grupos tanto con membresía estática (asignada manualmente) como dinámica (basada en reglas).

### Enfoque de Integración

- API CRUD de grupos: `POST/GET/PUT/DELETE /api/v2/groups`.
- Reglas de grupos dinámicos: membresía basada en expresiones (ej., `department == "Engineering"`).
- Motor de evaluación de membresía que evalúa reglas en la creación/actualización de usuario y según programación.
- Los cambios de membresía de grupo publican eventos para consumidores posteriores.

### Esfuerzo Estimado: 8-10 semanas.

---

## 6. Flujos de Aprobación para Cambios de Usuario

### Motivación

Habilitar flujos de aprobación de varios pasos para operaciones de usuario sensibles: cambios de rol, escalaciones de permisos, recuperación de cuenta.

### Enfoque de Integración

- Máquina de estados de flujo de trabajo: pendiente -> aprobado/rechazado -> ejecutado/revertido.
- Notificaciones a aprobadores mediante el servicio de notificaciones (correo electrónico, Slack).
- Cadenas de aprobación configurables (aprobador único, múltiples aprobadores, aprobación del gerente).
- Registro de auditoría para todas las acciones de aprobación.

### Esfuerzo Estimado: 6-8 semanas.

---

## 7. Integración con Entra ID Identity Protection

### Motivación

Integrar señales de Entra ID Identity Protection (usuario riesgoso, inicio de sesión riesgoso) para marcar o restringir automáticamente cuentas de usuario.

### Enfoque de Integración

- Consultar la API de Entra ID Identity Protection para detecciones de riesgo.
- Mapear niveles de riesgo a acciones del Users Service: riesgo bajo (solo registrar), riesgo medio (marcar usuario), riesgo alto (restringir usuario).
- Orientado a eventos: publicar evento `user.risk_assessed` para respuesta posterior.

### Esfuerzo Estimado: 4-6 semanas.

---

## Matriz de Prioridades de Integración

| Integración | Valor | Esfuerzo | Riesgo | Prioridad |
|---|---|---|---|---|
| SCIM 2.0 (Entrante) | Alto | Medio | Medio | Q4 2026 |
| Grupos de Usuarios | Alto | Alto | Medio | Q1 2027 |
| Integración con Sistemas de RRHH | Alto | Alto | Medio | Q2 2027 |
| Flujos de Aprobación | Medio | Medio | Bajo | Q2 2027 |
| SCIM 2.0 (Saliente) | Medio | Medio | Medio | TBD |
| Integración Okta/Auth0 | Medio | Medio | Medio | TBD |
| Identity Protection | Medio | Medio | Bajo | TBD |
