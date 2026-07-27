# Estrategia de Monitoreo — Users Service

- **Propietario:** Equipo de Platform Engineering
- **Última actualización:** 2026-07-20
- **Versión:** 1.0

## Propósito y Alcance

Este documento define la estrategia de monitoreo para el Users Service. Cubre métricas, paneles, alertas, SLOs y políticas de presupuesto de errores específicas para operaciones de gestión de usuarios, procesamiento de eventos e integración con Graph API.

## Objetivos de Nivel de Servicio (SLO)

### Objetivo de Disponibilidad

| SLO | Objetivo | Ventana |
|---|---|---|
| Disponibilidad | 99.95% | 30 días continuos |
| Tasa de éxito de lectura de usuarios | >= 99.9% | 7 días continuos |
| Tasa de éxito de escritura de usuarios | >= 99.5% | 7 días continuos |
| Tasa de éxito de procesamiento de eventos | >= 99.9% | 7 días continuos |
| Latencia CRUD de usuario (p99) | <= 500 ms | 7 días continuos |
| Retraso de procesamiento de eventos | <= 30 segundos | 7 días continuos |

### Definiciones de SLI

#### SLI de Disponibilidad
```
solicitudes_exitosas = conteo de respuestas HTTP 2xx + 4xx
disponibilidad = solicitudes_exitosas / total_solicitudes (excluyendo sondeos de salud)
```

#### SLI de Retraso de Procesamiento de Eventos
```
retraso = timestamp_actual - timestamp_encolado_evento (por evento)
retraso_sli_p99 = p99 de todos los retrasos de procesamiento de eventos durante 5 minutos
```

## Métricas Clave

### Métricas de Operaciones de Usuario

| Métrica | Tipo | Etiquetas | Descripción |
|---|---|---|---|
| `users_read_requests_total` | Contador | endpoint, result, tenant_id | Conteo de solicitudes de lectura de usuario |
| `users_write_requests_total` | Contador | operation (create/update/delete/restore), result | Conteo de operaciones de escritura de usuario |
| `users_request_duration_seconds` | Histograma | operation, endpoint | Latencia de solicitud |
| `users_active_total` | Indicador | tenant_id | Conteo actual de usuarios activos |
| `users_soft_deleted_total` | Indicador | tenant_id | Conteo actual de usuarios en soft-delete |

### Métricas de Procesamiento de Eventos

| Métrica | Tipo | Etiquetas | Descripción |
|---|---|---|---|
| `users_auth_events_consumed_total` | Contador | event_type, result | Eventos de auth consumidos de Service Bus |
| `users_auth_events_lag_seconds` | Indicador | event_type | Retraso actual de procesamiento para eventos de auth |
| `users_auth_events_lag_seconds_bucket` | Histograma | event_type | Distribución del retraso de procesamiento de eventos |
| `users_events_published_total` | Contador | event_type, result | Eventos de usuario publicados |
| `users_events_publish_duration_seconds` | Histograma | event_type | Latencia de publicación de eventos |
| `users_dead_letter_queue_depth` | Indicador | topic | Conteo actual de mensajes en DLQ |

### Métricas de Graph API

| Métrica | Tipo | Etiquetas | Descripción |
|---|---|---|---|
| `users_graph_api_calls_total` | Contador | operation, result | Conteo de llamadas a Graph API |
| `users_graph_api_duration_seconds` | Histograma | operation | Latencia de Graph API |
| `users_graph_api_errors_total` | Contador | operation, error_code | Errores de Graph API |
| `users_graph_api_cache_hit_ratio` | Indicador | — | Tasa de aciertos de caché de respuestas de Graph API |

### Métricas del Sistema

| Métrica | Tipo | Etiquetas | Descripción |
|---|---|---|---|
| `users_requests_total` | Contador | endpoint, method, status_code | Conteo de solicitudes HTTP |
| `users_request_duration_seconds` | Histograma | endpoint, method | Latencia de solicitud HTTP |
| `users_db_connection_pool_utilization` | Indicador | host | Uso del pool de conexiones de base de datos |
| `users_db_query_duration_seconds` | Histograma | operation, table | Latencia de consulta a base de datos |
| `users_db_errors_total` | Contador | operation, error_code | Errores de base de datos |

## Paneles de Grafana

### Panel General del Users Service

**Propósito:** Panel principal para el SRE de guardia.

| Panel | Métrica / Consulta | Visualización |
|---|---|---|
| Estado del Servicio | `users_service_up` | Estadística (verde/rojo) |
| Tasa de Solicitudes | `rate(users_requests_total[5m])` | Serie temporal |
| Tasa de Error (5xx) | `rate(users_requests_total{status=~"5.."}[5m]) / rate(users_requests_total[5m]) * 100` | Serie temporal |
| Latencia P50/P95/P99 | `histogram_quantile(0.99, rate(users_request_duration_seconds_bucket[5m]))` | Serie temporal |
| Usuarios Activos | `users_active_total` | Estadística + serie temporal |
| Retraso de Procesamiento de Eventos | `users_auth_events_lag_seconds` | Serie temporal (por tipo de evento) |
| Cola de Soft-Delete | `users_soft_deleted_total` | Serie temporal |
| Pool de Conexiones DB | `users_db_connection_pool_utilization` | Indicador |

### Panel de Procesamiento de Eventos

**Propósito:** Monitorear el consumo de eventos de auth y la salud de publicación de eventos de usuario.

| Panel | Métrica |
|---|---|
| Tasa de Eventos de Auth Consumidos | `rate(users_auth_events_consumed_total[5m])` por event_type |
| Retraso de Procesamiento de Eventos | `users_auth_events_lag_seconds` por event_type |
| Duración de Procesamiento de Eventos | `histogram_quantile(0.99, rate(users_auth_events_processing_duration_seconds_bucket[5m]))` |
| Tasa de Eventos de Usuario Publicados | `rate(users_events_published_total[5m])` por event_type |
| Profundidad de Cola de Mensajes Fallidos | `users_dead_letter_queue_depth` |

### Panel de Salud de Graph API

**Propósito:** Monitorear la salud de la integración con Microsoft Graph API.

| Panel | Métrica |
|---|---|
| Tasa de Llamadas a Graph API | `rate(users_graph_api_calls_total[5m])` por operation |
| Tasa de Error de Graph API | `rate(users_graph_api_errors_total[5m])` por error_code |
| Latencia P99 de Graph API | `histogram_quantile(0.99, rate(users_graph_api_duration_seconds_bucket[5m]))` |
| Tasa de Aciertos de Caché | `users_graph_api_cache_hit_ratio` |

## Reglas de Alerta de Prometheus

### Alertas Críticas (Página)

| Alerta | Condición | Durante | Descripción |
|---|---|---|---|
| UsersServiceDown | `absent(users_service_up == 1)` | 1m | El servicio está caído |
| UsersServiceHighErrorRate | Tasa 5xx > 2% | 2m | La tasa de error supera el umbral |
| UsersServiceP99LatencyBreached | p99 > 1000ms | 3m | Latencia alta detectada |
| UsersServiceAuthDepFailed | Fallo al obtener JWKS de Auth Service | 1m | No se pueden validar tokens |
| UsersServiceDatabaseDown | Fallo en verificación de salud de DB | 30s | Base de datos no accesible |

### Alertas de Advertencia (Slack)

| Alerta | Condición | Durante |
|---|---|---|
| EventProcessingLagHigh | Retraso > 60 segundos | 5m |
| EventProcessingErrorRate | Errores de procesamiento de eventos > 5% | 5m |
| GraphApiErrorRate | Errores de Graph API > 10% | 5m |
| DbConnectionPoolHigh | Utilización del pool > 80% | 5m |
| SoftDeleteQueueGrowing | Conteo de soft-delete creciendo > 10%/hora | 1h |
| DqlMessagesAccumulating | Profundidad DLQ > 100 | 5m |

## Integración con PagerDuty

- **Servicio:** Users Service Producción
- **Tipo de integración:** Prometheus Alertmanager
- **Escalación:** SRE Primario -> SRE Secundario (15 min) -> Gerente de Ingeniería (15 min)
- **Enriquecimiento de alertas:** Enlace al panel de Grafana, enlace al runbook, detalles de la alerta

## Política de Presupuesto de Errores

### Cálculo

Para 99.95% de disponibilidad en una ventana de 30 días:
```
indisponibilidad_permitida = 30 * 24 * 3600 * (1 - 0.9995) = 1296 segundos
```

### Consecuencias del Presupuesto

| Estado del Presupuesto | Política |
|---|---|
| >= 50% restante | Despliegues normales |
| 20%-50% | Los despliegues requieren aprobación de SRE |
| 5%-20% | Congelación de despliegues excepto hotfixes |
| < 5% | Congelación total, solo enfoque en confiabilidad |
| Agotado (0%) | Congelación completa, se requiere post-mortem |

### Revisión Trimestral de SLO

La revisión cubre el logro de SLO, patrones de consumo del presupuesto, efectividad de la política y ajuste de objetivos.
