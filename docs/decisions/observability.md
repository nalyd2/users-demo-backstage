# Observabilidad — Users Service

- **Estado:** Aprobado
- **Propietario:** Equipo de Platform Engineering
- **Última actualización:** 2026-07-20

## Visión General

El Users Service implementa logs estructurados, métricas y trazabilidad distribuida para proporcionar visibilidad sobre las operaciones de gestión de usuarios, aislamiento de inquilinos, procesamiento de eventos e integración con Graph API. Toda la telemetría utiliza OpenTelemetry y se alinea con los estándares de observabilidad de la plataforma.

## Pilar 1: Logs Estructurados (Serilog)

### Configuración

Toda la salida de logs está en formato JSON mediante Serilog. Niveles mínimos: Information por defecto, `Microsoft.EntityFrameworkCore` Warning, `System.Net.Http.HttpClient` Warning.

### Niveles de Log

| Nivel | Uso |
|---|---|
| **Verbose** | Diagnósticos solo para desarrollo |
| **Debug** | Solución de problemas detallada; deshabilitado por defecto en producción |
| **Information** | Operaciones CRUD de usuario, cambios de inquilino, procesamiento de eventos |
| **Warning** | Uso de ruta degradada, límite de tasa próximo, uso de endpoint obsoleto |
| **Error** | Operación fallida, fallo de Graph API, fallo de procesamiento de eventos |
| **Fatal** | El servicio no puede iniciar, base de datos no accesible, estado irrecuperable |

### Categorías de Eventos

| Categoría | Rango de ID de Evento | Descripción |
|---|---|---|
| Operaciones de Usuario | 1000-1999 | Crear, leer, actualizar, eliminar, soft-delete, restaurar |
| Operaciones de Inquilino | 2000-2999 | Creación de inquilino, configuración, cambios de estado |
| Eventos de Auth | 3000-3999 | Eventos de auth consumidos (login, logout, token revocado) |
| Eventos de Usuario | 4000-4999 | Eventos de usuario publicados (creado, actualizado, eliminado) |
| Graph API | 5000-5999 | Llamadas a Microsoft Graph API, enriquecimiento de perfil |
| Salud del Sistema | 6000-6999 | Inicio, apagado, verificaciones de salud |

### Eventos Obligatorios

```csharp
public static partial class LogMessages
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "User {UserId} created in tenant {TenantId} by {ActorId}")]
    public static partial void UserCreated(this ILogger logger, Guid userId, Guid tenantId, Guid actorId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "User {UserId} soft-deleted in tenant {TenantId} by {ActorId}")]
    public static partial void UserSoftDeleted(this ILogger logger, Guid userId, Guid tenantId, Guid actorId);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Auth event {EventType} consumed for user {UserId}")]
    public static partial void AuthEventConsumed(this ILogger logger, string eventType, Guid userId);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Warning,
        Message = "Graph API enrichment failed for user {UserId}: {Error}")]
    public static partial void GraphApiEnrichmentFailed(this ILogger logger, Guid userId, string error);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information,
        Message = "User event {EventType} published for user {UserId}")]
    public static partial void UserEventPublished(this ILogger logger, string eventType, Guid userId);
}
```

## Pilar 2: Métricas (Prometheus / OpenTelemetry)

### Nomenclatura de Métricas

```
users_<componente>_<nombre_metrica>_<unidad>
```

### Métricas Requeridas

#### Operaciones de Usuario

| Nombre de Métrica | Tipo | Etiquetas | Descripción |
|---|---|---|---|
| `users_crud_operations_total` | Contador | operation (create/read/update/delete/restore), result | Conteo de operaciones CRUD de usuario |
| `users_crud_duration_seconds` | Histograma | operation, result | Latencia de operación CRUD |
| `users_active_total` | Indicador | tenant_id | Conteo actual de usuarios activos (no eliminados) |
| `users_soft_deleted_total` | Indicador | tenant_id | Conteo actual de usuarios en soft-delete |

#### Eventos de Auth

| Nombre de Métrica | Tipo | Etiquetas | Descripción |
|---|---|---|---|
| `users_auth_events_consumed_total` | Contador | event_type (login/logout/token_revoked), result | Eventos de auth consumidos |
| `users_auth_events_processing_duration_seconds` | Histograma | event_type | Latencia de procesamiento de eventos de auth |
| `users_auth_events_lag_seconds` | Indicador | event_type | Retraso actual entre publicación y consumo del evento |

#### Eventos de Usuario Publicados

| Nombre de Métrica | Tipo | Etiquetas | Descripción |
|---|---|---|---|
| `users_events_published_total` | Contador | event_type (created/updated/deleted) | Eventos de usuario publicados en Service Bus |
| `users_events_publish_duration_seconds` | Histograma | event_type | Latencia de publicación de eventos |

#### Graph API

| Nombre de Métrica | Tipo | Etiquetas | Descripción |
|---|---|---|---|
| `users_graph_api_calls_total` | Contador | operation, result | Conteo de llamadas a Graph API |
| `users_graph_api_duration_seconds` | Histograma | operation | Latencia de llamada a Graph API |
| `users_graph_api_enrichment_total` | Contador | result | Conteo de intentos de enriquecimiento de perfil |

#### Operaciones de Inquilino

| Nombre de Métrica | Tipo | Etiquetas | Descripción |
|---|---|---|---|
| `users_tenants_total` | Indicador | status | Conteo total de inquilinos por estado |
| `users_tenants_operations_total` | Contador | operation, result | Operaciones de gestión de inquilinos |

#### Sistema

| Nombre de Métrica | Tipo | Etiquetas | Descripción |
|---|---|---|---|
| `users_requests_total` | Contador | endpoint, method, status_code | Total de solicitudes HTTP |
| `users_request_duration_seconds` | Histograma | endpoint, method, status_code | Latencia de solicitud |
| `users_db_connection_pool_size` | Indicador | host | Tamaño del pool de conexiones de base de datos |

## Pilar 3: Trazabilidad Distribuida (OpenTelemetry)

### Propagación de Contexto de Traza

- Estándar W3C Trace Context (encabezados `traceparent` / `tracestate`).
- Los eventos de auth entrantes traen contexto de traza desde Auth Service (mediante propiedades de aplicación de Service Bus).
- Los eventos de usuario salientes propagan contexto de traza a consumidores posteriores.
- Todo HTTP saliente (Graph API) propaga contexto de traza.

### Spans Requeridos

| Nombre del Span | Atributos |
|---|---|
| `GET /api/v{version}/users/{id}` | user_id, tenant_id |
| `POST /api/v{version}/users` | tenant_id, role |
| `PATCH /api/v{version}/users/{id}` | user_id, fields_changed |
| `DELETE /api/v{version}/users/{id}` | user_id, permanent |
| `UserService.CreateUser` | tenant_id, role |
| `UserService.SoftDeleteUser` | user_id |
| `AuthEventConsumer.ProcessEvent` | event_type, event_id |
| `GraphApiClient.EnrichProfile` | user_id, graph_user_id |
| `UserEventPublisher.Publish` | event_type, user_id |

### Estrategia de Muestreo

| Tráfico | Tasa |
|---|---|
| Solicitudes saludables (2xx) | 10% |
| Errores de cliente (4xx) | 50% |
| Errores de servidor (5xx) | 100% |
| Procesamiento de eventos de auth | 100% (todos los eventos importantes para trazabilidad) |
| Llamadas a Graph API | 25% |
| Endpoints de verificación de salud | 0% |

## Umbrales de Alerta

| Alerta | Condición | Severidad |
|---|---|---|
| Tasa de error alta | Tasa de error > 5% durante 5 minutos | Crítica |
| Retraso de procesamiento de eventos > 5 minutos | Retraso de evento > 300 segundos | Advertencia |
| Tasa de fallo de Graph API | Tasa de error > 10% durante 5 minutos | Advertencia |
| Tasa alta de mutación de usuario | Tasa de creación/actualización/eliminación > 100/s | Advertencia |
| Latencia P99 > 1000ms | Latencia > 1s durante 5 minutos | Crítica |
| Profundidad de cola de soft-delete | Purga pendiente > 10000 | Advertencia |
