# Riesgos — Users Service

- **Estado:** Aprobado
- **Propietario:** Equipo de Platform Engineering
- **Última actualización:** 2026-07-20

## Visión General

Este documento cataloga los riesgos identificados para el Users Service. Cada riesgo incluye severidad, probabilidad, impacto y mitigaciones planificadas. Los riesgos se revisan trimestralmente.

---

## Registro de Riesgos

### U-R01: Auth Service No Disponible (Falla la Validación JWT)

| Atributo | Valor |
|---|---|
| **ID de Riesgo** | U-R01 |
| **Categoría** | Dependencia Externa / Disponibilidad |
| **Severidad** | Crítica |
| **Probabilidad** | Posible |
| **Impacto** | Crítico — Users Service no puede validar tokens JWT entrantes porque el endpoint JWKS no está accesible. Las claves JWKS en caché proporcionan una ventana de gracia de 5 minutos. Después de la expiración de la caché, todas las solicitudes de API fallan en autenticación y son rechazadas. Todas las operaciones de gestión de usuarios están bloqueadas. |
| **Detección** | Alerta de fallo de obtención de JWKS. Pico de tasa de error en todos los endpoints. Alerta de PagerDuty en AuthServiceDown se propaga al monitoreo del Users Service. |
| **Mitigación** | 1. Documento JWKS almacenado en caché en memoria con TTL de 5 minutos. La validación de tokens continúa durante la vida útil de la caché. 2. Reintento con retroceso exponencial en fallo de obtención de JWKS. 3. El endpoint de salud (`/health`) del Users Service refleja el estado de la dependencia del Auth Service. 4. El despliegue multirregión asegura que la obtención de JWKS pueda recurrir al endpoint de la región secundaria. |
| **Contingencia** | 1. Alertar inmediatamente al equipo de Auth Service de guardia. 2. Si la interrupción excede los 5 minutos, todas las llamadas API al Users Service fallarán. 3. Considerar extender el TTL de la caché si el RTO del Auth Service excede los 5 minutos (requiere aprobación del líder de ingeniería). 4. Post-incidente: verificar consistencia de datos y reproducir eventos de auth perdidos. |
| **Objetivo de Tiempo de Recuperación** | 5 minutos (ventana de gracia de JWKS en caché) |

---

### U-R02: Fallo de Base de Datos PostgreSQL

| Atributo | Valor |
|---|---|
| **ID de Riesgo** | U-R02 |
| **Categoría** | Infraestructura / Dependencia |
| **Severidad** | Alta |
| **Probabilidad** | Poco probable |
| **Impacto** | Mayor — todas las operaciones de usuario fallan (crear, leer, actualizar, eliminar, listar). El consumo de eventos falla (sin persistencia para eventos entrantes). Los JWT de usuarios existentes no pueden verificarse (requiere DB para validación de roles). |
| **Mitigación** | 1. HA redundante por zona con conmutación automática por error (< 60 segundos). 2. Pool de conexiones con lógica de reintento. 3. Restauración puntual de 35 días. 4. Réplica de lectura en región secundaria. |
| **Contingencia** | Iniciar conmutación por error redundante por zona. Si la región primaria falla, conmutación por error geográfica a réplica de lectura. |

---

### U-R03: Violación de Aislamiento de Inquilino

| Atributo | Valor |
|---|---|
| **ID de Riesgo** | U-R03 |
| **Categoría** | Seguridad / Fuga de Datos |
| **Severidad** | Crítica |
| **Probabilidad** | Rara |
| **Impacto** | Crítico — usuarios del inquilino A pueden acceder a datos del inquilino B. Exposición de PII, incumplimiento regulatorio (GDPR), daño reputacional. |
| **Detección** | 1. Registro de consultas SQL para patrones anómalos de acceso entre inquilinos. 2. Pruebas de penetración regulares para aislamiento de inquilino. 3. Pruebas de integración automatizadas verifican el aislamiento de inquilino para cada endpoint. |
| **Mitigación** | 1. Aislamiento de inquilino aplicado en la capa de repositorio: cada consulta incluye `WHERE tenant_id = @tenantId`. 2. ID de inquilino extraído de los reclamos del JWT (no del cuerpo de la solicitud) para prevenir manipulación. 3. Pruebas de integración para cada endpoint validan que el acceso entre inquilinos esté bloqueado. 4. RLS (Seguridad a Nivel de Fila) de base de datos como defensa en profundidad. 5. Todos los identificadores de usuario incluyen prefijo tenant_id en los registros de auditoría. |

---

### U-R04: Limitación de Tasa o Interrupción de Microsoft Graph API

| Atributo | Valor |
|---|---|
| **ID de Riesgo** | U-R04 |
| **Categoría** | Dependencia Externa |
| **Severidad** | Media |
| **Probabilidad** | Posible |
| **Impacto** | Moderado — el enriquecimiento de perfil se retrasa o se omite. Los perfiles de usuario se sirven con datos almacenados localmente (pueden estar desactualizados). |
| **Mitigación** | 1. Respuestas de Graph API almacenadas en caché por 1 hora. 2. Reintento con retroceso exponencial en limitación de tasa (encabezado Retry-After). 3. El enriquecimiento de perfil es asíncrono y no bloqueante; las operaciones CRUD de usuario no se ven afectadas. 4. Patrón de disyuntor (circuit breaker) previene fallos en cascada. |

---

### U-R05: Acumulación de Procesamiento de Eventos

| Atributo | Valor |
|---|---|
| **ID de Riesgo** | U-R05 |
| **Categoría** | Procesamiento / Latencia |
| **Severidad** | Media |
| **Probabilidad** | Posible |
| **Impacto** | Moderado — los eventos de auth (login/logout) no se procesan en tiempo real. El estado de sesión del usuario se vuelve obsoleto. Los eventos de login pueden procesarse después de la expiración del token, causando estado inconsistente. |
| **Detección** | 1. La métrica `users_auth_events_lag_seconds` alerta cuando el retraso supera el umbral. 2. Monitoreo de profundidad de DLQ. |
| **Mitigación** | 1. Los consumidores de eventos se ejecutan como servicios en segundo plano independientes con paralelismo configurable. 2. Cada consumidor de eventos tiene su propio pipeline de procesamiento (sin bloqueo entre eventos). 3. Los eventos son idempotentes: procesar el mismo evento dos veces es seguro. 4. Autoescalado para consumidores de eventos basado en la profundidad de la cola. |

---

### U-R06: Pérdida de Datos por Trabajo de Purga de Soft-Delete

| Atributo | Valor |
|---|---|
| **ID de Riesgo** | U-R06 |
| **Categoría** | Integridad de Datos |
| **Severidad** | Alta |
| **Probabilidad** | Poco probable |
| **Impacto** | Mayor — un trabajo de purga configurado incorrectamente elimina permanentemente datos de usuario que deberían haberse retenido. Incumplimiento regulatorio si se violan los requisitos de retención. |
| **Detección** | 1. Conteos del trabajo de purga registrados antes y después de la ejecución. 2. Volumen de purga anómalo desencadena revisión manual. 3. Registro de auditoría de todos los registros purgados. |
| **Mitigación** | 1. Modo de simulación (dry-run): previsualizar registros a purgar antes de la eliminación real. 2. Período de retención configurable (por defecto: 90 días). 3. Tamaño máximo de lote por ejecución para limitar el radio de explosión. 4. El trabajo de purga requiere confirmación manual en producción. 5. Existe respaldo de base de datos antes de la ejecución de la purga. |

---

### U-R07: Incompatibilidad de Esquema de Eventos

| Atributo | Valor |
|---|---|
| **ID de Riesgo** | U-R07 |
| **Categoría** | Integración |
| **Severidad** | Media |
| **Probabilidad** | Poco probable |
| **Impacto** | Mayor — si Auth Service publica eventos de auth con un nuevo esquema que Users Service no puede analizar, todo el procesamiento de eventos falla. El estado de sesión del usuario se vuelve permanentemente obsoleto hasta que se resuelva el problema. |
| **Detección** | 1. Tasa de fallo de deserialización de eventos monitoreada. 2. Pico de tasa de error en el consumidor de eventos. |
| **Mitigación** | 1. Los eventos incluyen número de versión del esquema. 2. Negociación de versión de esquema: los consumidores declaran versiones compatibles, los productores usan la versión mutuamente compatible más alta. 3. Evolución de esquema compatible hacia atrás: los nuevos campos son opcionales, nunca se eliminan. 4. Pruebas de integración entre los esquemas de eventos de Auth Service y Users Service ejecutadas en CI. |

---

### U-R08: Escalación de Permisos RBAC

| Atributo | Valor |
|---|---|
| **ID de Riesgo** | U-R08 |
| **Categoría** | Seguridad |
| **Severidad** | Alta |
| **Probabilidad** | Rara |
| **Impacto** | Mayor — un usuario con rol Operator escala al rol Admin y obtiene acceso no autorizado a la configuración del inquilino o datos de usuario. |
| **Detección** | 1. Eventos de cambio de rol registrados y monitoreados. 2. Detección de anomalías en asignaciones de roles. 3. El registro de auditoría requiere justificación para cambios de rol a Admin. |
| **Mitigación** | 1. Los cambios de rol requieren aprobación de múltiples pasos (Admin aprueba cambios de rol de Operator, Admin separado aprueba cambios de rol de Admin). 2. La asignación de roles se registra con identidad del actor y marca de tiempo. 3. Principio de mínimo privilegio: el rol por defecto es User, la escalación requiere aprobación explícita. 4. Las pruebas automatizadas verifican que se aplique la jerarquía de roles. |

---

### U-R09: Factor de Riesgo del Equipo (Bus Factor)

| Atributo | Valor |
|---|---|
| **ID de Riesgo** | U-R09 |
| **Categoría** | Organizacional |
| **Severidad** | Media |
| **Probabilidad** | Poco probable |
| **Impacto** | Mayor — pérdida de miembros clave del equipo familiarizados con aislamiento de inquilino, procesamiento de eventos e integración con Graph API. |
| **Mitigación** | 1. Infraestructura como código (Terraform, Helm). 2. Runbooks completos para respuesta a incidentes y operaciones. 3. La revisión de código asegura que múltiples miembros del equipo entiendan cada componente. 4. Sesiones de capacitación cruzada cada sprint. 5. Rotación de guardia para experiencia operativa. |

---

## Resumen de Riesgos

| ID | Descripción | Severidad | Probabilidad | Impacto | Mitigación |
|---|---|---|---|---|---|
| U-R01 | Auth Service no disponible | Crítica | Posible | Crítico | JWKS en caché (gracia de 5 min) |
| U-R02 | Fallo de PostgreSQL | Alta | Poco probable | Mayor | HA redundante por zona, réplica de lectura |
| U-R03 | Violación de aislamiento de inquilino | Crítica | Rara | Crítico | Aplicación en capa de repositorio, RLS |
| U-R04 | Limitación de Graph API | Media | Posible | Moderado | Caché, disyuntor |
| U-R05 | Acumulación de procesamiento de eventos | Media | Posible | Moderado | Autoescalado, eventos idempotentes |
| U-R06 | Pérdida de datos por purga | Alta | Poco probable | Mayor | Simulación, confirmación manual |
| U-R07 | Incompatibilidad de esquema de eventos | Media | Poco probable | Mayor | Versionado de esquema |
| U-R08 | Escalación de permisos RBAC | Alta | Rara | Mayor | Aprobación multi-paso, auditoría |
| U-R09 | Factor de riesgo del equipo | Media | Poco probable | Mayor | Documentación, capacitación cruzada |
