# Runbook de operaciones -- Users Service

**Servicio:** `users-service`
**Dominio:** Gestion del ciclo de vida del usuario
**Propietario:** Equipo de Ingenieria de Plataforma
**Ciclo de vida:** Produccion
**Objetivo de SLA:** 99.95% de disponibilidad
**Guardia:** PagerDuty — politica de escalamiento `#platform-eng` (respuesta en 15 min)

---

## Tabla de Contenidos

1. [Proposito y alcance](#1-proposito-y-alcance)
2. [Tareas de mantenimiento rutinario](#2-tareas-de-mantenimiento-rutinario)
3. [Monitoreo de sincronizacion de Entra ID](#3-monitoreo-de-sincronizacion-de-entra-id)
4. [Purga de eliminacion suave](#4-purga-de-eliminacion-suave)
5. [Planificacion de capacidad](#5-planificacion-de-capacidad)
6. [Ajuste de rendimiento](#6-ajuste-de-rendimiento)
7. [Verificacion de copias de seguridad](#7-verificacion-de-copias-de-seguridad)
8. [Monitoreo de verificaciones de salud](#8-monitoreo-de-verificaciones-de-salud)
9. [Automatizacion de runbooks](#9-automatizacion-de-runbooks)
10. [Escalamiento y soporte](#10-escalamiento-y-soporte)

---

## 1. Proposito y alcance

Este runbook documenta los procedimientos operativos recurrentes para el Users Service. Esta destinado a miembros del Equipo de Ingenieria de Plataforma, SREs e ingenieros de guardia que gestionan el servicio en produccion.

Todos los procedimientos asumen acceso autenticado a las siguientes consolas:

| Consola | URL |
|---|---|
| Azure Portal | `https://portal.azure.com/` — suscripcion `Platform-Prod` |
| Azure DevOps | `https://dev.azure.com/platform/` — proyecto `platform` |
| Grafana | `https://grafana.internal/d/users/users-service` |
| Kibana | `https://kibana.internal/s/platform` |
| Backstage | `https://backstage.internal/platform/component/users-service` |

**Requisitos previos para todos los procedimientos:**

- CLI de Azure (`az`) iniciada sesion en la suscripcion `Platform-Prod`
- `kubectl` configurado con el contexto del cluster AKS de produccion (`aks-platform-prod`)
- Acceso a Azure Key Vault `kv-platform-users-prod`
- Membresia en el grupo de Azure AD `platform-engineering`
- Cliente `psql` (compatible con PostgreSQL 16) instalado en el jumpbox
- `jq` para analisis JSON

---

## 2. Tareas de mantenimiento rutinario

El mantenimiento rutinario sigue una cadencia escalonada: verificaciones diarias realizadas por el ingeniero de guardia, revisiones semanales por el equipo del servicio y auditorias mensuales mas profundas.

### 2.1 Tareas diarias (guardia)

| Hora | Tarea | Herramienta | Duracion |
|---|---|---|---|
| 09:00 | Revisar dashboards en busca de anomalias | Grafana | 10 min |
| 09:15 | Verificar PagerDuty en busca de alertas nocturnas | PagerDuty | 5 min |
| 09:30 | Verificar que todos los pods esten `Running` y saludables | `kubectl` | 5 min |
| 09:45 | Confirmar que la sonda de readiness pase en todos los endpoints | Grafana / cURL | 5 min |
| 10:00 | Verificar la salud de la sincronizacion de Entra ID | Grafana | 5 min |

**Lista de verificacion de revision diaria del dashboard:**

1. Abrir el dashboard de Users Service en Grafana (`https://grafana.internal/d/users/users-service`).
2. Verificar que las siguientes metricas esten dentro de la linea base:

   | Metrica | Umbral de alerta | Notas |
   |---|---|---|
   | Tasa de solicitudes (p50/p95/p99) | p95 > 800ms | Mayor que auth debido a consultas de BD |
   | Tasa de error (4xx y 5xx) | > 1% del total de solicitudes | 4xx de fallos de autenticacion son esperados |
   | Conteo de conexiones PostgreSQL | > 80% de `max_connections` | Actualmente 50 por pool |
   | Latencia de llamadas a Graph API | p99 > 2s | La limitacion puede necesitar investigacion |
   | Backlog del bus de eventos | > 1,000 mensajes no consumidos | Indica retraso del consumidor |
   | Errores del trabajo de purga de eliminacion suave | Cualquier fallo en las ultimas 24h | Critico para cumplimiento de PII |

3. Verificar los logs de `users-service` en Kibana para patrones de error estructurados (`"@level": "Error"` o `"@level": "Fatal"`).
4. Verificar que la sincronizacion nocturna de Entra ID se completo exitosamente (ver Seccion 3).

**Verificacion de pods de Kubernetes:**

```bash
# Cambiar al cluster AKS de produccion
kubectl config use-context aks-platform-prod

# Verificar el estado de los pods en todas las zonas de disponibilidad
kubectl get pods -n idp-system -l app=users-service -o wide

# Salida esperada: 9 pods (3 por zona x 3 zonas), todos con estado "Running"

# Inspeccionar cualquier pod CrashLoopBackOff o Pending
kubectl describe pod -n idp-system -l app=users-service | grep -A 5 "Status:"

# Verificacion rapida de salud en todos los pods
kubectl get endpoints -n idp-system users-service
```

### 2.2 Tareas semanales (Equipo del servicio)

| Tarea | Frecuencia | Propietario |
|---|---|---|
| Revisar el retraso del consumidor de eventos y la cola de mensajes fallidos | Semanal (Lun) | Ingeniero de plataforma |
| Analizar el log de consultas lentas de PostgreSQL | Semanal (Mar) | Ingeniero backend |
| Verificar la limitacion de Graph API y el consumo de cuota | Semanal (Mie) | Ingeniero de plataforma |
| Revisar logs y metricas del trabajo de purga de eliminacion suave | Semanal (Jue) | Ingeniero backend |
| Revisar resultados de escaneo de vulnerabilidades de dependencias | Semanal (Vie) | Rotacion del equipo |

**Revision del retraso del consumidor de eventos:**

```bash
# Verificar metricas de suscripcion de Service Bus mediante CLI de Azure
az servicebus topic subscription show \
  --resource-group platform-prod-rg \
  --namespace-name sb-platform-prod \
  --topic-name auth-events \
  --subscription-name users-service \
  --query "{activeMessageCount:countDetails.activeMessageCount, deadLetterCount:countDetails.deadLetteredMessageCount, scheduledCount:countDetails.scheduledMessageCount}"

# Esperado: activeMessageCount < 100 durante horas laborales, acercandose a 0 durante periodos de bajo trafico

# Inspeccionar mensajes fallidos (si los hay)
az servicebus topic subscription show \
  --resource-group platform-prod-rg \
  --namespace-name sb-platform-prod \
  --topic-name auth-events \
  --subscription-name users-service \
  --query "deadLetteringOnMessageExpiration"

# Si el conteo de mensajes fallidos supera 50, investigar:
#   - Errores de deserializacion de mensajes (verificar Kibana para errores de EventProcessor)
#   - Mensajes envenenados (verificar cuerpo del mensaje y motivo de mensaje fallido)
#   - Timeouts de procesamiento (duracion predeterminada de bloqueo de mensaje: 30s)
```

**Analisis de consultas lentas de PostgreSQL:**

```sql
-- Iniciar sesion en el servidor PostgreSQL de produccion (credenciales de Key Vault)
-- PGPASSWORD recuperada mediante: az keyvault secret show ...

SELECT
  queryid,
  calls,
  total_exec_time / 1000 AS total_segundos,
  mean_exec_time / 1000 AS mean_ms,
  rows,
  shared_blks_hit,
  shared_blks_read,
  query
FROM pg_stat_statements
ORDER BY total_exec_time DESC
LIMIT 20;
```

Infractores frecuentes a vigilar:

- Consultas que faltan el filtro `tenant_id` (nunca deberia suceder — RLS lo aplica, pero un escaneo completo desperdicia E/S).
- Consultas contra la tabla `users` sin indice en `(tenant_id, deleted_at)` — el filtro de eliminacion suave debe usar este indice compuesto.
- Consultas en `user_sessions` que no filtran por `tenant_id` y `user_id`.

### 2.3 Tareas mensuales

| Tarea | Duracion esperada |
|---|---|
| Revision de capacidad y ajuste del plan de escalado | 45 min |
| Ejercicio de recuperacion ante desastres — conmutacion por error a North Europe | 60 min |
| Auditoria de vencimiento de certificados TLS (todas las capas) | 20 min |
| Verificacion de rotacion de token de cuenta de sincronizacion de Entra ID | 30 min |
| Ciclo de parches de dependencias (actualizaciones menores) | 120 min |
| Ejercicio de runbook de purga de eliminacion suave en staging | 30 min |

**Lista de verificacion mensual de capacidad:**

```bash
# 1. Revisar metricas de HPA en los ultimos 30 dias
kubectl get hpa users-service -n idp-system -o yaml

# 2. Verificar eventos del autoscaler del cluster
kubectl get events -n kube-system --field-selector reason=TriggeredAutoscaler \
  --sort-by=.lastTimestamp

# 3. Revisar el crecimiento de almacenamiento de PostgreSQL
az postgres flexible-server show \
  --name pg-users-prod \
  --resource-group platform-prod-rg \
  --query "{storageUsed:storage.storageUsedGB, storageLimit:storage.storageSizeGB, backupRetention:backup.backupRetentionDays}"
```

### 2.4 Tareas trimestrales

| Tarea | Duracion esperada |
|---|---|
| Ejercicio completo de recuperacion ante desastres | 4 horas |
| Prueba de regresion de referencia de rendimiento | 2 horas |
| Revision de accesos — Key Vault, PostgreSQL, RBAC de Kubernetes | 1 hora |
| Validacion de extremo a extremo de sincronizacion de Entra ID | 1 hora |
| Reunion de revision de arquitectura | 1 hora |
| Rotacion de cadena de conexion de PostgreSQL | 30 min |

---

## 3. Monitoreo de sincronizacion de Entra ID

### 3.1 Descripcion general

El Users Service enriquece los perfiles de usuario desde **Microsoft Entra ID (Azure AD)** mediante Microsoft Graph API. La sincronizacion se ejecuta en un horario configurable (predeterminado: nocturno a las 02:00 UTC, cron `0 2 * * *`) y esta controlada por el feature flag `GraphApiSync.Enabled`.

**Flujo de sincronizacion:**

```
1. El disparador de sincronizacion se activa (temporizador o manual)
2. Obtener todos los usuarios activos de PostgreSQL
3. Para cada usuario, llamar a Microsoft Graph API (GET /users/{id})
4. Actualizar campos del perfil: display_name, department, job_title, mobile_phone
5. Registrar discrepancias (usuarios en BD pero no en Entra ID, y viceversa)
6. Emitir evento users.sync.completed con resumen
```

**Importante:** Entra ID es la fuente autoritativa para la identidad corporativa. El Users Service nunca envia datos de perfil corriente arriba — solo lee.

### 3.2 Dashboard de salud de sincronizacion

El dashboard de Grafana `Users / Entra ID Sync` rastrea los siguientes paneles:

| Panel | Metrica | Advertencia | Critico |
|---|---|---|---|
| **Tasa de exito de sincronizacion** | `graph_api_sync_success_total / graph_api_sync_attempts_total` | < 99% | < 95% |
| **Duracion de sincronizacion** | `graph_api_sync_duration_seconds` | > 10 min | > 20 min |
| **Conteo de actualizaciones** | `graph_api_users_updated_total` en la ultima ejecucion | 0 (sin cambios) | N/A (cero es valido en la noche) |
| **Desglose de errores** | `graph_api_errors_total` por etiqueta `error_code` | > 5 errores | > 20 errores |
| **Estado de limitacion** | `graph_api_throttled_requests_total` | > 0 | > 10 en 5 min |
| **Cobertura de Entra ID** | `(graph_api_users_found / expected_users_total) * 100` | < 95% | < 90% |

**URL del dashboard:** `https://grafana.internal/d/users/entra-id-sync`

### 3.3 Verificacion diaria de sincronizacion

```bash
# Paso 1: Verificar la marca de tiempo de la ultima sincronizacion y el estado mediante logs
kubectl logs -n idp-system -l app=users-service --tail=500 --since=24h \
  | grep -E "SyncCompleted|SyncFailed|GraphApiSync" \
  | tail -20

# La salida esperada incluye una linea de log como:
# {"@level":"Information","message":"Entra ID sync completed","sync_duration_seconds":342,"users_updated":15,"users_skipped":2841,"errors":0,"@timestamp":"..."}

# Paso 2: Verificar que la sincronizacion publico su evento de finalizacion
kubectl logs -n idp-system -l app=users-service --tail=200 \
  | grep "users.sync.completed" \
  | tail -5

# Paso 3: Verificar la metrica directamente (si el port-forward de Prometheus esta disponible)
curl -s http://localhost:7201/metrics | grep graph_api_sync_
```

### 3.4 Investigacion de fallos de sincronizacion

**Modos de fallo comunes:**

| Sintoma | Causa probable | Remedio |
|---|---|---|
| `429 Too Many Requests` | Limitacion de Graph API | Verificar `graph_api_throttled_requests_total`. La sincronizacion retrocede exponencialmente (politica de reintentos de Polly: 3 reintentos, retraso base de 30s). Si la limitacion persiste, solicitar un aumento de cuota mediante ticket de soporte de Azure. |
| `401 Unauthorized` | Identidad administrada o principal de servicio vencida | Verificar la identidad administrada del pod: `az identity show --name users-service-identity --resource-group platform-prod-rg`. Verificar la asignacion de roles en Microsoft Graph. |
| `404 Not Found` | Usuario eliminado de Entra ID pero aun en PostgreSQL | La sincronizacion registra esto como una discrepancia. Revisar el informe de discrepancias (ver Seccion 3.5) y decidir si eliminar de forma suave el registro local. |
| Timeout > 30s | Latencia de red o degradacion de Graph API | Verificar `graph_api_sync_duration_seconds`. Considerar reducir el tamano del lote o aumentar la configuracion `GraphApi__SyncTimeoutSeconds` (predeterminado: 120). |

**Activacion manual de sincronizacion:**

```bash
# Activar el endpoint de sincronizacion (interno, no expuesto a traves de API Gateway)
# Requiere kubectl port-forward o acceso directo al pod
kubectl exec -n idp-system deploy/users-service -- \
  curl -s -X POST http://localhost:7201/api/internal/sync-entra-id \
    -H "X-Internal-Key: $(cat /etc/secrets/internal-api-key)"

# Monitorear la sincronizacion en tiempo real
kubectl logs -n idp-system -l app=users-service --tail=100 -f \
  | grep -E "Sync|GraphApi|entra"
```

### 3.5 Informe de discrepancias

La sincronizacion genera un informe de discrepancias almacenado en una tabla dedicada de PostgreSQL:

```sql
-- Consultar el ultimo informe de discrepancias de sincronizacion
SELECT
  sync_run_id,
  sync_timestamp,
  db_only_count,       -- Usuarios en PostgreSQL pero no en Entra ID
  entra_only_count,    -- Usuarios en Entra ID pero no en PostgreSQL
  field_mismatch_count -- Usuarios donde los campos difieren
FROM sync_discrepancy_reports
ORDER BY sync_timestamp DESC
LIMIT 5;

-- Ver discrepancias detalladas de campos
SELECT
  u.id,
  u.email,
  d.field_name,
  d.db_value,
  d.entra_value
FROM sync_field_mismatches d
JOIN users u ON u.id = d.user_id
WHERE d.sync_run_id = '<latest-run-id>'
ORDER BY u.email;
```

**Accion ante discrepancias:**

- **Usuarios en BD no en Entra ID:** Estos son probablemente cuentas de servicio no empleados o usuarios internos de la plataforma. Marcarlos con una etiqueta `source = 'platform'`. Si representan ex empleados, iniciar el flujo de trabajo de desvinculacion.
- **Usuarios en Entra ID no en BD:** Estos pueden ser nuevos empleados sincronizados desde el sistema de RRHH. Evaluar si necesitan un perfil de usuario en la plataforma. Si es asi, crear el perfil.
- **Discrepancias de campos:** La sincronizacion actualiza los campos de BD automaticamente. Revisar el volumen de discrepancias; un numero alto puede indicar una actualizacion masiva en el sistema de RRHH o un error de mapeo.

### 3.6 Ajuste de rendimiento de sincronizacion

```yaml
# Configuracion para sincronizacion de Graph API (appsettings.Production.json)
GraphApi:
  Sync:
    Enabled: true
    Schedule: "0 2 * * *"        # Nocturno a las 2 AM UTC
    TimeoutSeconds: 120
    BatchSize: 50                 # Maximo de usuarios por solicitud de lote
    Concurrency: 4                # Llamadas paralelas a Graph API
    RetryCount: 3                 # Retroceso exponencial (Polly)
    RetryBaseDelaySeconds: 30
    FieldMappings:
      display_name: "displayName"
      department: "department"
      job_title: "jobTitle"
      mobile_phone: "mobilePhone"
```

**Directrices de ajuste:**

| Problema | Ajuste |
|---|---|
| La duracion de sincronizacion supera los 20 min para 50k usuarios | Aumentar `Concurrency` a 8 (monitorear limitacion) |
| Errores 429 de Graph API | Disminuir `Concurrency` a 2 y aumentar `RetryBaseDelaySeconds` a 60 |
| La sincronizacion nunca termina antes del proximo inicio programado | Asegurar que `TimeoutSeconds` > duracion esperada; considerar ejecutar dos veces al dia en su lugar |
| Demasiadas actualizaciones innecesarias (0 cambios de campo) | La sincronizacion salta actualizaciones cuando todos los campos coinciden — verificar metrica `graph_api_users_updated_total`. Si es 0 consistentemente, la sincronizacion esta saludable. |

---

## 4. Purga de eliminacion suave

### 4.1 Descripcion general

El Users Service implementa un patron de **eliminacion suave**: cuando un usuario es eliminado, su registro se marca con `deleted_at = NOW()` y `is_active = false`. Los datos se retienen durante un periodo configurable (`SoftDeleteRetentionDays`, predeterminado: 30 dias) antes de ser purgados permanentemente.

Este diseno asegura:

- La integridad referencial se preserva (las FKEYs que referencian `users.id` permanecen validas).
- Una ventana de recuperacion esta disponible para eliminaciones accidentales.
- Un trabajo de purga programado maneja la eliminacion permanente y la anonimizacion de PII.

**Ciclo de vida de los datos:**

```
Usuario creado ──► Eliminado suave ──► Ventana de retencion (30 dias) ──► Purgado
                     │                       │
                     │ Puede restaurarse      │ Eliminacion permanente
                     │ (recuperar)            │ + actualizacion de pista de auditoria
                     ▼                       ▼
              deleted_at = NOW()        Registro eliminado de BD
              is_active = false         PII anonimizada en logs de auditoria
              Todas las consultas       (email → hash, display_name → "Deleted User")
              excluyen por defecto
              filtro
```

### 4.2 Configuracion del trabajo de purga

```yaml
# appsettings.Production.json
Users:
  SoftDeleteRetentionDays: 30       # Especifico del entorno (dev: 7, qa: 14, staging: 30, prod: 30)
PurgeJob:
  Schedule: "0 3 * * *"            # Diario a las 3 AM UTC
  BatchSize: 500                   # Usuarios purgados por lote
  BatchDelayMs: 100                # Pausa entre lotes para reducir carga de BD
  TimeoutMinutes: 30               # Tiempo de ejecucion maximo del trabajo
  DryRunEnabled: true              # Indicador de seguridad — ver Seccion 4.4
  AuditRetentionDays: 90           # Cuanto tiempo conservar registros de auditoria de usuarios purgados
```

### 4.3 Monitoreo de la salud del trabajo de purga

**Metricas clave (panel de Grafana: `Users / Purge Job`):**

| Metrica | Descripcion | Advertencia | Critico |
|---|---|---|---|
| `purge_job_success` | 1 si la ultima ejecucion tuvo exito, 0 si fallo | 0 (fallo) | — |
| `purge_job_duration_seconds` | Tiempo para completar | > 10 min | > 20 min |
| `purge_job_users_purged_total` | Usuarios eliminados en la ultima ejecucion | — | — |
| `purge_job_errors_total` | Errores encontrados | > 0 | > 5 |
| `purge_job_dry_run` | 1 si el modo de simulacion esta activado, 0 si es en vivo | — | — |

**Verificar que el trabajo de purga se ejecuto exitosamente:**

```bash
# Verificar el log del trabajo de purga mas reciente
kubectl logs -n idp-system -l app=users-service --tail=500 --since=36h \
  | grep "PurgeJob" \
  | tail -20

# La salida esperada incluye:
# {"@level":"Information","message":"Purge job completed","users_purged":42,"batches":3,"errors":0,"duration_seconds":14.2,"dryRun":false,"@timestamp":"..."}
# O (si el modo de simulacion esta activado):
# {"@level":"Information","message":"Purge job completed (DRY RUN)","candidates":42,"dryRun":true,"duration_seconds":12.1}
```

**Consulta directa a base de datos:**

```sql
-- Verificar cuantos usuarios estan pendientes de purga
SELECT COUNT(*) AS pending_purge_count
FROM users
WHERE deleted_at IS NOT NULL
  AND deleted_at < NOW() - INTERVAL '30 days';

-- Verificar el historial del trabajo de purga
SELECT
  run_timestamp,
  users_purged,
  errors,
  duration_seconds,
  dry_run
FROM purge_job_runs
ORDER BY run_timestamp DESC
LIMIT 10;
```

### 4.4 Modo de simulacion y despliegue canary

El trabajo de purga se ejecuta en **modo de simulacion** de forma predeterminada (`DryRunEnabled: true`). En modo de simulacion, el trabajo identifica candidatos para purgar pero no elimina ningun registro. Para habilitar la purga en vivo, el operador debe:

1. Verificar que los candidatos son correctos revisando el log de simulacion.
2. Establecer `PurgeJob__DryRunEnabled` a `false` mediante un ConfigMap de Kubernetes o variable de entorno.
3. Monitorear de cerca la primera ejecucion en vivo.
4. Reactivar el modo de simulacion despues de la confirmacion.

```bash
# Paso 1: Verificar candidatos de simulacion
kubectl logs -n idp-system -l app=users-service --tail=200 --since=36h \
  | grep "PurgeJob" | grep "dryRun" | grep "candidates"

# Paso 2: Revisar candidatos de muestra mediante base de datos
SELECT id, email, deleted_at
FROM users
WHERE deleted_at IS NOT NULL
  AND deleted_at < NOW() - INTERVAL '30 days'
LIMIT 10;

# Paso 3: Deshabilitar modo de simulacion (temporal — se revertira despues del proximo reinicio)
kubectl set env deployment users-service -n idp-system \
  PurgeJob__DryRunEnabled=false

# Paso 4: Verificar la ejecucion en vivo mediante logs (proxima ejecucion programada, o activar manualmente)
# Paso 5: Reactivar modo de simulacion
kubectl set env deployment users-service -n idp-system \
  PurgeJob__DryRunEnabled=true
```

**Activacion manual (para pruebas):**

```bash
# Activar trabajo de purga mediante endpoint interno
kubectl exec -n idp-system deploy/users-service -- \
  curl -s -X POST http://localhost:7201/api/internal/purge-users \
    -H "X-Internal-Key: $(cat /etc/secrets/internal-api-key)"

# Para simulacion:
kubectl exec -n idp-system deploy/users-service -- \
  curl -s -X POST "http://localhost:7201/api/internal/purge-users?dryRun=true" \
    -H "X-Internal-Key: $(cat /etc/secrets/internal-api-key)"
```

### 4.5 Anonimizacion de PII

Cuando un usuario es purgado, ocurren las siguientes transformaciones:

| Campo | Antes de la purga | Despues de la purga |
|---|---|---|
| `email` | `john.doe@company.com` | `sha256(user_id + salt)@purged.internal` |
| `display_name` | `John Doe` | `Deleted User` |
| `username` | `jdoe` | `deleted-{user_id_prefix}` |
| `mobile_phone` | `+1 555-0123` | `NULL` |
| `external_ids` (Entra ID OID) | `aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee` | `NULL` |
| Referencias de usuario en log de auditoria | `user_id` (UUID) | `user_id` preservado (clave necesaria para vincular eventos) |

**Verificacion de anonimizacion:**

```sql
-- Despues de una ejecucion de purga, confirmar que la PII se elimino
SELECT email, display_name, mobile_phone, external_ids
FROM audit_users_purged
WHERE purge_run_id = '<latest-run-id>'
LIMIT 5;
-- email debe contener '@purged.internal'
-- display_name debe ser 'Deleted User'
-- mobile_phone debe ser NULL
```

### 4.6 Restauracion de un usuario eliminado suave (recuperacion)

Si un usuario fue eliminado accidentalmente y la ventana de retencion no ha expirado, un administrador puede restaurarlo:

```bash
# Operacion de API REST (requiere rol de administrador)
curl -X POST https://api.internal.platform/api/users/{user-id}/restore \
  -H "Authorization: Bearer $JWT" \
  -H "Content-Type: application/json"
```

La operacion de restauracion:

1. Establece `deleted_at = NULL` e `is_active = true`.
2. Publica un evento `users.restored`.
3. Registra la accion de restauracion en la pista de auditoria.
4. NO re-enriquece desde Entra ID (se requiere sincronizacion manual).

**Despues de que expire la ventana de retencion, la restauracion es imposible.** Los datos han sido purgados permanentemente y la PII anonimizada.

---

## 5. Planificacion de capacidad

### 5.1 Linea base de capacidad actual

| Recurso | Asignacion actual | Utilizacion maxima | Margen |
|---|---|---|---|
| **Pool de nodos AKS** | 9 nodos (Standard_D4s_v5) | 60% CPU / 50% memoria | 40-50% |
| **Pods de Users Service** | 9 (3 por AZ x 3 zonas) | 35% CPU / 45% memoria | 55-65% |
| **PostgreSQL (Primario)** | 4 vCores, 16 GB RAM, 256 GB almacenamiento | 25% CPU / 40% conexiones / 60 GB usados | 60-75% |
| **PostgreSQL (Replica de lectura — NE)** | 2 vCores, 8 GB RAM | 10% CPU / 15% conexiones | 85%+ |
| **Service Bus (topico auth-events)** | Premium 1 MU | 150 msg/s pico | 65% |
| **Service Bus (topico users-events)** | Premium 1 MU | 30 msg/s pico | 85% |
| **Solicitudes de Graph API** | 500,000 / ventana movil de 30 dias | 120,000 usados (~24%) | 76% |

### 5.2 Disparadores y acciones de escalado

| Metrica | Umbral | Accion | Tiempo de respuesta |
|---|---|---|---|
| CPU del pod > 70% durante 5 min | HPA activa escalado (max 6 por AZ) | Automatico | 2 min |
| Latencia p95 de solicitud del pod > 800ms | HPA activa escalado | Automatico | 2 min |
| Memoria > 80% durante 5 min | HPA activa escalado | Automatico | 2 min |
| CPU del nodo AKS > 75% | Cluster Autoscaler agrega nodo (max 10 por AZ) | Automatico | 5 min |
| Conexiones PostgreSQL > 80% | Aumentar `max_connections` y monitorear vCores | Manual (planificado) | 30 min |
| Almacenamiento PostgreSQL > 75% | Solicitar aumento de almacenamiento; planificar mantenimiento de indices | Manual | 4 horas (ticket de Azure para aumento de almacenamiento) |
| Cuota de Graph API > 80% | Solicitar aumento de cuota mediante soporte de Azure | Manual (ticket) | 2-3 dias |

### 5.3 Procedimientos de escalado

**Autoscaler Horizontal de Pods (HPA):**

```bash
# Ver la configuracion actual de HPA
kubectl get hpa users-service -n idp-system -o yaml

# Configuracion esperada:
#   minReplicas: 3
#   maxReplicas: 6
#   metrics:
#     - type: Resource
#       resource:
#         name: cpu
#         target:
#           type: Utilization
#           averageUtilization: 70
#     - type: Resource
#       resource:
#         name: memory
#         target:
#           type: Utilization
#           averageUtilization: 80
```

**Escalado manual (preventivo para aumentos de trafico planificados, ej., evento de incorporacion):**

```bash
# Aumentar las replicas minimas antes de un evento de carga
kubectl scale deployment users-service -n idp-system --replicas=5

# Despues del evento, revertir
kubectl scale deployment users-service -n idp-system --replicas=3
```

**Escalado vertical de PostgreSQL:**

```bash
# Paso 1: Verificar el nivel actual y el almacenamiento
az postgres flexible-server show \
  --name pg-users-prod \
  --resource-group platform-prod-rg \
  --query "{sku:sku.name, storage:storage.storageSizeGB, storageUsed:storage.storageUsedGB}"

# Paso 2: Escalar computo (breve conmutacion por error — planificar durante ventana de mantenimiento)
az postgres flexible-server update \
  --name pg-users-prod \
  --resource-group platform-prod-rg \
  --sku-name Standard_D4ds_v5

# Paso 3: Escalar almacenamiento (sin tiempo de inactividad, pero irreversible)
az postgres flexible-server update \
  --name pg-users-prod \
  --resource-group platform-prod-rg \
  --storage-size 512

# Paso 4: Actualizar parametros del servidor
az postgres flexible-server parameter set \
  --name max_connections \
  --value 200 \
  --server-name pg-users-prod \
  --resource-group platform-prod-rg
```

### 5.4 Cadencia de revision de capacidad

| Tipo de revision | Frecuencia | Participantes | Entregable |
|---|---|---|---|
| Revision de dashboard | Diaria | Guardia | Verificacion de tendencia de metricas (captura de Grafana) |
| Analisis de tendencias | Semanal | Ingeniero de plataforma | Grafico de uso de recursos de 7 dias |
| Planificacion de capacidad | Mensual | SRE + equipo de plataforma | Recomendaciones de escalado |
| Prevision presupuestaria | Trimestral | Equipo de plataforma + FinOps | Proyeccion de costos y optimizacion |

### 5.5 Procedimiento de prueba de autoscalado

Ejecutar esto trimestralmente para validar que HPA y Cluster Autoscaler respondan correctamente:

```bash
# Paso 1: Desplegar un trabajo de prueba de carga en el entorno staging
kubectl apply -f k8s/staging/load-test/users-service-loadtest.yaml

# Paso 2: Monitorear el escalado de pods
watch -n 10 'kubectl get pods -n idp-system -l app=users-service'

# Paso 3: Verificar metricas de HPA
kubectl get hpa users-service -n idp-system -w

# Paso 4: Verificar que Cluster Autoscaler agregue nodos
kubectl get nodes -w

# Paso 5: Despues de que la prueba se complete, confirmar la reduccion de escala al minimo
kubectl get pods -n idp-system -l app=users-service

# Paso 6: Eliminar la prueba de carga
kubectl delete -f k8s/staging/load-test/users-service-loadtest.yaml
```

---

## 6. Ajuste de rendimiento

### 6.1 Lineas base de rendimiento clave

| Metrica | Objetivo | Advertencia | Critico | Fuente de medicion |
|---|---|---|---|---|
| Latencia P95 GET /api/users/{id} | < 200 ms | 400 ms | 800 ms | Grafana (http_request_duration_seconds) |
| Latencia P95 POST /api/users | < 300 ms | 500 ms | 1,000 ms | Grafana (http_request_duration_seconds) |
| P95 listar usuarios (paginado) | < 500 ms | 800 ms | 1,500 ms | Grafana (http_request_duration_seconds) |
| Latencia de validacion JWT | < 5 ms | 10 ms | 50 ms | Grafana (jwt_validation_duration_seconds) |
| Tiempo de consulta PostgreSQL (escritura) | < 30 ms | 60 ms | 150 ms | `pg_stat_statements` |
| Tiempo de consulta PostgreSQL (lectura) | < 10 ms | 25 ms | 75 ms | `pg_stat_statements` |
| Latencia de llamada a Graph API | < 500 ms | 1,000 ms | 2,000 ms | Grafana (graph_api_duration_seconds) |
| Procesamiento de mensajes de Service Bus | < 100 ms | 250 ms | 500 ms | Grafana (event_processing_duration_seconds) |
| Lote de trabajo de purga P95 | < 5 s | 15 s | 30 s | Grafana (purge_job_duration_seconds) |

### 6.2 Ajuste de PostgreSQL

**Configuracion actual:**

```ini
# Aplicada mediante el grupo de parametros de Azure Flexible Server "users-service-prod"

max_connections = 150                    # 50 por pod x 3 pods
shared_buffers = '4GB'                   # 25% de 16 GB RAM
effective_cache_size = '12GB'            # 75% de 16 GB RAM
work_mem = '8MB'                         # Reducido del valor predeterminado (busquedas simples, no OLAP)
maintenance_work_mem = '1GB'
random_page_cost = 1.1                   # Azure Premium SSD
effective_io_concurrency = 200
wal_buffers = '32MB'

# Especifico de Users Service
jit = on                                 # Beneficioso para consultas de informes complejas
enable_nestloop = on                     # Aceptable para busquedas tipicas de usuarios
parallel_query_workers = 2               # Limitar para evitar contención de E/S en el primario
```

**Indices criticos a verificar:**

```sql
-- Verificar que los indices esenciales existen y se estan usando
SELECT
  schemaname,
  tablename,
  indexname,
  idx_scan,
  idx_tup_read,
  idx_tup_fetch
FROM pg_stat_user_indexes
WHERE tablename IN ('users', 'user_sessions', 'audit_log')
ORDER BY idx_scan ASC;

-- Indices esperados en `users`:
--   ix_users_tenant_id_deleted_at (compuesto, parcial: WHERE deleted_at IS NULL)
--   ix_users_email (unico)
--   ix_users_tenant_id_username (unico compuesto)
--   ix_users_deleted_at (para consultas del trabajo de purga)
```

**Deteccion de indices faltantes (ejecutar semanalmente):**

```sql
-- Tablas con altos escaneos secuenciales = posible indice faltante
SELECT
  relname,
  seq_scan,
  seq_tup_read,
  idx_scan,
  CASE WHEN seq_scan > 0
    THEN ROUND(seq_tup_read::numeric / NULLIF(seq_scan, 0), 0)
    ELSE 0
  END AS avg_tuples_per_seq
FROM pg_stat_user_tables
WHERE seq_scan > 100                      <!-- Ignorar tablas con muy pocos escaneos -->
  AND seq_tup_read > 10000                <!-- Muchas filas leidas por escaneo -->
ORDER BY avg_tuples_per_seq DESC
LIMIT 10;
```

**Mantenimiento de tablas:**

```sql
-- Verificar hinchazon de tablas (ejecutar mensualmente)
SELECT
  schemaname,
  tablename,
  n_live_tup,
  n_dead_tup,
  ROUND(n_dead_tup::numeric / NULLIF(n_live_tup, 0) * 100, 1) AS dead_pct,
  last_autovacuum,
  last_autoanalyze
FROM pg_stat_user_tables
ORDER BY dead_pct DESC
LIMIT 10;

-- Si alguna tabla tiene > 20% de tuplas muertas y sin autovacuum reciente:
-- Vaciar manualmente la tabla
VACUUM (VERBOSE, ANALYZE) users;
```

### 6.3 Pool de conexiones

El servicio utiliza el pool de conexiones de Npgsql con la siguiente configuracion:

```ini
# Parametros de cadena de conexion
Host=pg-users-prod.postgres.database.azure.com;Database=usersdb;
Maximum Pool Size=50;                    # Por pod (3 pods x 50 = 150 max)
Connection Idle Lifetime=300;            # 5 min inactivo antes de expulsion del pool
Connection Pruning Interval=60;          # Verificar cada 60s conexiones inactivas
Multiplexing=false;                      # Deshabilitado — mezcla lectura/escritura reduce beneficio de multiplexacion
```

**Monitoreo del pool:**

```bash
# Metrica de Grafana: npgsql_connection_pool_total_connection_count
# Objetivo: conexiones activas < 75% del tamano del pool (37 de 50)

# Verificacion directa de PostgreSQL:
SELECT COUNT(*) AS active_connections
FROM pg_stat_activity
WHERE state = 'active'
  AND datname = 'usersdb';

SELECT COUNT(*) AS idle_connections
FROM pg_stat_activity
WHERE state = 'idle'
  AND datname = 'usersdb';
```

### 6.4 Ajuste de procesamiento de eventos

El consumidor de eventos procesa eventos de autenticacion (inicio de sesion, cierre de sesion, token revocado) de Azure Service Bus:

```yaml
# appsettings.Production.json
ServiceBus:
  EventProcessor:
    MaxConcurrentCalls: 10            # Maximo de mensajes procesados simultaneamente por pod
    PrefetchCount: 20                 # Mensajes pre-obtenidos para rendimiento
    AutoComplete: true                # Auto-completar en procesamiento exitoso
    MaxAutoLockRenewalDuration: "00:05:00"  # 5 min de renovacion de bloqueo
    RetryCount: 3                     # En fallo transitorio
    DeadLetterOnError: true           # Mensajes envenenados van a DLQ
```

**Directrices de ajuste de rendimiento:**

| Sintoma | Causa probable | Accion |
|---|---|---|
| Alto backlog de mensajes con CPU baja | `MaxConcurrentCalls` demasiado bajo | Aumentar a 20-30; monitorear pool de conexiones de BD |
| Alto conteo de conexiones de BD con backlog de mensajes | Consultas de BD lentas (se necesita ajuste de consultas) | Verificar `pg_stat_statements` para consultas lentas de procesamiento de eventos |
| Mensajes siendo enviados a cola de mensajes fallidos | Fallo de deserializacion o error de procesamiento | Inspeccionar DLQ, verificar logs de Kibana para errores de `EventProcessor` |
| Latencia de procesamiento > 500ms | Limitacion de Service Bus en 1 MU | Verificar metricas del namespace; considerar escalar a 2 MUs |

### 6.5 Compilacion JIT y calentamiento de inicio

El servicio admite un endpoint de calentamiento de inicio para reducir la latencia de inicio en frio despues del despliegue:

```bash
# Activar calentamiento (llamado por la sonda de inicio)
# Endpoint solo interno, no expuesto a traves de API Gateway
curl -X POST http://localhost:7201/api/internal/warmup \
  -H "X-Internal-Key: ..."

# Efecto esperado: la latencia de la primera solicitud disminuye de ~800ms a <100ms
```

### 6.6 Patrones de optimizacion de consultas

**Consulta de listar usuarios (la ruta de lectura mas comun):**

```sql
-- El servicio genera una consulta equivalente a:
SELECT id, tenant_id, email, display_name, roles, created_at, updated_at
FROM users
WHERE tenant_id = @tenantId
  AND deleted_at IS NULL
ORDER BY created_at DESC
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

-- Esto debe usar el indice compuesto ix_users_tenant_id_deleted_at
-- cubriendo (tenant_id, deleted_at DESC, created_at DESC) INCLUDE (email, display_name, roles)
```

**Consulta de candidatos a purga:**

```sql
SELECT id, email, display_name
FROM users
WHERE deleted_at IS NOT NULL
  AND deleted_at < @cutoffDate
ORDER BY deleted_at ASC
LIMIT @batchSize;

-- Usa ix_users_deleted_at (indice parcial en deleted_at IS NOT NULL)
```

---

## 7. Verificacion de copias de seguridad

### 7.1 Copias de seguridad de PostgreSQL

**Configuracion:**

| Atributo | Valor |
|---|---|
| **Tipo de copia de seguridad** | Administrada por Azure, con redundancia geografica |
| **Retencion** | 35 dias de recuperacion a un punto en el tiempo (PITR) |
| **Ventana de copia de seguridad** | 01:30 - 03:30 UTC |
| **Redundancia geografica** | Habilitada (copias replicadas a la region emparejada de North Europe) |

**Procedimiento de verificacion diaria:**

```bash
# Paso 1: Listar copias de seguridad recientes
az postgres flexible-server backup list \
  --name pg-users-prod \
  --resource-group platform-prod-rg \
  --query "[].{name:name, created:createdTime, size:backupSize}" \
  --output table

# Esperado: al menos una copia de seguridad completada en las ultimas 24 horas

# Paso 2: Verificar la fecha de restauracion de punto en el tiempo mas temprana
az postgres flexible-server show \
  --name pg-users-prod \
  --resource-group platform-prod-rg \
  --query "backup.earliestRestoreDate"

# Paso 3: Validar la integridad de la copia verificando el tamano de la copia mas reciente
# Un tamano de copia que caiga repentinamente a casi cero indica un problema
```

**Ejercicio de restauracion trimestral:**

```bash
# Paso 1: Establecer el punto de restauracion (24 horas atras para una instantanea reciente)
RESTORE_TIME=$(date -u -d "24 hours ago" +"%Y-%m-%dT%H:%M:%SZ")
RESTORE_NAME="pg-users-prod-restore-$(date +%Y%m%d)"

# Paso 2: Restaurar a una instancia temporal (toma 10-20 minutos)
az postgres flexible-server restore \
  --name "$RESTORE_NAME" \
  --resource-group platform-prod-rg \
  --source-server pg-users-prod \
  --restore-time "$RESTORE_TIME" \
  --zone 1

# Paso 3: Verificar la integridad de los datos
PGPASSWORD=$(az keyvault secret show --vault-name kv-platform-users-prod \
  --name restore-test-password --query value -o tsv)

psql "host=$RESTORE_NAME.postgres.database.azure.com \
  port=5432 dbname=usersdb user=restoretest password=$PGPASSWORD sslmode=require" \
  -c "SELECT 'users_count' AS metric, COUNT(*) AS value FROM users UNION ALL
      SELECT 'active_users', COUNT(*) FROM users WHERE deleted_at IS NULL UNION ALL
      SELECT 'deleted_users', COUNT(*) FROM users WHERE deleted_at IS NOT NULL UNION ALL
      SELECT 'user_sessions', COUNT(*) FROM user_sessions;"

# Paso 4: Verificar registros recientes al azar
psql ... -c "SELECT id, email, created_at FROM users ORDER BY created_at DESC LIMIT 5;"

# Paso 5: Verificar que las politicas RLS esten intactas
psql ... -c "
  SELECT schemaname, tablename, policyname, permissive, roles, cmd
  FROM pg_policies
  WHERE tablename = 'users';
"

# Paso 6: Eliminar la instancia de prueba
az postgres flexible-server delete \
  --name "$RESTORE_NAME" \
  --resource-group platform-prod-rg \
  --yes --no-wait
```

**Criterios de exito del ejercicio de restauracion:**

- Todos los recuentos de filas coinciden con la base de datos de origen en el punto de restauracion.
- Las politicas RLS estan presentes y coinciden con la configuracion esperada.
- Sin errores de corrupcion durante las consultas `SELECT`.
- La restauracion se completo dentro de la ventana de tiempo esperada.

### 7.2 Copias de seguridad del estado de la aplicacion

El Users Service es en gran medida **sin estado**, pero los siguientes datos con estado requieren consideracion de copia de seguridad:

| Componente con estado | Metodo de copia de seguridad | Verificacion | Frecuencia |
|---|---|---|---|
| PostgreSQL (primario) | Azure PITR (retencion de 35 dias) | Lista de copias diaria / ejercicio de restauracion trimestral | Ver 7.1 |
| Suscripciones de Service Bus | No se necesita copia de seguridad — los eventos son transitorios | N/A | N/A |
| Cache JWKS | Regenerada desde Auth Service al reiniciar | N/A | N/A |
| Configuracion (Key Vault) | Replicacion geografica de Azure Key Vault | Verificar estado de replicacion mensualmente | Mensual |

### 7.3 Copia de seguridad de Key Vault

Los secretos y certificados de Key Vault se respaldan mediante la replicacion de la plataforma Azure:

```bash
# Verificar el estado de replicacion geografica
az keyvault show \
  --name kv-platform-users-prod \
  --query "properties.enableSoftDelete"

# Esperado: true (eliminacion suave habilitada — ventana de recuperacion de 90 dias)

# Respaldar un secreto especifico (para archivo de cumplimiento)
az keyvault secret backup \
  --vault-name kv-platform-users-prod \
  --name users-db-connection-string \
  --file /tmp/backup-users-db-connection.secret

# Verificar el archivo de copia de seguridad
ls -la /tmp/backup-users-db-connection.secret
file /tmp/backup-users-db-connection.secret
# Esperado: archivo no vacio, identificable como formato de copia de seguridad de Azure Key Vault
```

### 7.4 Procedimiento de copia de seguridad para recuperacion ante desastres

En caso de una falla regional total (West Europe no disponible):

```bash
# Paso 1: Restaurar PostgreSQL desde copias con redundancia geografica a North Europe
az postgres flexible-server geo-restore \
  --name pg-users-prod-dr \
  --resource-group platform-prod-rg \
  --source-server pg-users-prod \
  --location northeurope

# Paso 2: Validar la base de datos restaurada
# (Ejecutar las mismas verificaciones de integridad que el ejercicio de restauracion trimestral en la Seccion 7.1)

# Paso 3: Apuntar la replica de lectura de North Europe al nuevo primario
# (Ver el runbook de despliegue para actualizaciones de DNS y cadena de conexion)

# Paso 4: Verificar la funcionalidad del servicio
curl -f -s -o /dev/null -w "%{http_code}" \
  https://users.internal.platform/api/health/ready

# Paso 5: Ejecutar operaciones sinteticas de usuario
# Crear, leer, actualizar, eliminar — prueba de ciclo de vida completo
```

---

## 8. Monitoreo de verificaciones de salud

### 8.1 Arquitectura de sondas

```
                                ┌──────────────────────────┐
                                │  Azure Traffic Manager    │
                                │  (intervalo 30s)         │
                                └──────┬───────────────────┘
                                       │ GET /api/health/ready
                                       ▼
┌──────────────┐           ┌──────────────────────┐
│ kubelet      │◄─────────►│ Pod de Users Service │
│ liveness     │  GET /api │                      │
│ (periodo 15s)│  /health/ │  ┌────────────────┐  │
│              │  live     │  │ Sonda de       │  │
│              │           │  │ readiness      │  │
│ kubelet      │  GET /api │  │  - PostgreSQL  │  │
│ readiness    │  /health/ │  │  - Auth Service│  │
│ (periodo 5s) │  ready    │  │  - Service Bus │  │
│              │           │  └────────────────┘  │
│ kubelet      │           │                      │
│ startup      │           └──────────────────────┘
│ (60s inicial)│
└──────────────┘
```

### 8.2 Endpoints de verificacion de salud

**Liveness (`GET /api/health/live`):**

```json
{
  "status": "Healthy",
  "checks": {
    "process": {
      "status": "Healthy",
      "latency_ms": 0.1
    }
  }
}
```

Sin verificaciones de dependencias — devuelve `200` mientras el proceso se este ejecutando.

**Readiness (`GET /api/health/ready`):**

```json
{
  "status": "Healthy",
  "checks": {
    "postgres": {
      "status": "Healthy",
      "latency_ms": 2.3
    },
    "auth_service": {
      "status": "Healthy",
      "latency_ms": 4.1
    },
    "service_bus": {
      "status": "Healthy",
      "latency_ms": 12.5
    }
  }
}
```

Devuelve `503` si alguna dependencia no esta saludable. Los endpoints obsoletos no se incluyen en las verificaciones de readiness.

**Umbrales de readiness:**

| Dependencia | Timeout | Conteo de fallos | Impacto |
|---|---|---|---|
| PostgreSQL | 3s | 3 consecutivos | El pod NO esta listo — sin trafico |
| Auth Service (gRPC) | 2s | 3 consecutivos | El pod NO esta listo — validacion JWT degradada |
| Service Bus | 5s | 3 consecutivos | El pod NO esta listo — publicacion de eventos degradada |
| Graph API | 5s | 3 consecutivos | El pod esta listo pero la sincronizacion esta degradada (no es una dependencia dura) |

### 8.3 Configuracion de sondas

```yaml
# Plantilla de despliegue — configuraciones actuales de produccion
readinessProbe:
  httpGet:
    path: /api/health/ready
    port: 7201
  initialDelaySeconds: 10
  periodSeconds: 5
  timeoutSeconds: 3
  successThreshold: 1
  failureThreshold: 3                     # 15s (3 x 5s) antes de la eliminacion del servicio

livenessProbe:
  httpGet:
    path: /api/health/live
    port: 7201
  initialDelaySeconds: 30
  periodSeconds: 15
  timeoutSeconds: 5
  successThreshold: 1
  failureThreshold: 3                     # 45s sin liveness = reinicio del contenedor

startupProbe:
  httpGet:
    path: /api/health/ready
    port: 7201
  initialDelaySeconds: 5
  periodSeconds: 10
  failureThreshold: 6                     # 60s de tiempo maximo de inicio
```

### 8.4 Reglas de alerta de Prometheus

```yaml
# prometheus/rules/users-service-alerts.yaml
groups:
  - name: users-service
    rules:
      - alert: UsersServiceDown
        expr: up{job="users-service"} == 0
        for: 1m
        labels:
          severity: critical
          team: platform-engineering
        annotations:
          summary: "El servicio de usuarios esta caido"
          description: "{{ $labels.instance }} ha estado inalcanzable por >1 minuto."

      - alert: UsersServiceHighErrorRate
        expr: |
          rate(http_requests_total{job="users-service", status=~"5.."}[5m])
          /
          rate(http_requests_total{job="users-service"}[5m])
          > 0.01
        for: 3m
        labels:
          severity: critical
        annotations:
          summary: "La tasa de error del servicio de usuarios supera el 1%"
          description: "La tasa de error es {{ $value | humanizePercentage }} en 5 minutos."

      - alert: UsersServiceHighLatency
        expr: |
          histogram_quantile(0.95,
            rate(http_request_duration_seconds_bucket{job="users-service"}[5m])
          ) > 0.8
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "La latencia p95 del servicio de usuarios supera los 800ms"

      - alert: UsersServiceAuthDown
        expr: users_auth_service_up{job="users-service"} == 0
        for: 1m
        labels:
          severity: critical
        annotations:
          summary: "Auth Service no esta accesible desde Users Service"

      - alert: UsersServicePostgresDown
        expr: pg_up{job="users-service"} == 0
        for: 1m
        labels:
          severity: critical
        annotations:
          summary: "PostgreSQL no esta accesible desde Users Service"

      - alert: UsersServiceSyncFailed
        expr: graph_api_sync_success_total{job="users-service"} == 0
        for: 24h
        labels:
          severity: warning
        annotations:
          summary: "La sincronizacion de Entra ID no ha tenido exito en 24 horas"

      - alert: UsersServicePurgeJobFailed
        expr: purge_job_success{job="users-service"} == 0
        for: 24h
        labels:
          severity: warning
        annotations:
          summary: "El trabajo de purga de eliminacion suave fallo en las ultimas 24 horas"

      - alert: UsersServiceHighConnectionCount
        expr: |
          pg_stat_database_numbackends{job="users-service", datname="usersdb"}
          > 120
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Las conexiones PostgreSQL superan el 80% del maximo (150)"

      - alert: UsersServiceEventConsumerBacklog
        expr: |
          azure_servicebus_subscription_active_messages
          > 1000
        for: 10m
        labels:
          severity: warning
        annotations:
          summary: "El backlog del consumidor de eventos supera los 1,000 mensajes"

      - alert: UsersServiceGraphApiThrottling
        expr: |
          rate(graph_api_throttled_requests_total{job="users-service"}[5m])
          > 0.1
        for: 10m
        labels:
          severity: warning
        annotations:
          summary: "Limitacion de Graph API detectada"
          description: "Solicitudes limitadas a {{ $value | humanizeRate }} por segundo."
```

### 8.5 Monitoreo sintetico

Las transacciones sinteticas se ejecutan cada 5 minutos desde dos ubicaciones externas para validar la funcionalidad de extremo a extremo:

```bash
# Verificacion de salud sintetica — simula operaciones del ciclo de vida del usuario
# Ejecutada mediante Pruebas de Disponibilidad de Azure Monitor

# Paso 1: Verificacion de liveness
curl -f -s -o /dev/null -w "%{http_code}" \
  https://users.internal.platform/api/health/live
# Esperado: 200

# Paso 2: Verificacion de readiness
curl -f -s -o /dev/null -w "%{http_code}" \
  https://users.internal.platform/api/health/ready
# Esperado: 200

# Paso 3: Listar usuarios (paginado, con ambito de inquilino, requiere JWT valido)
HEALTH_TOKEN=$(curl -s -X POST https://auth.internal.platform/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"healthcheck","password":"..."}' | jq -r '.access_token')

curl -s -H "Authorization: Bearer $HEALTH_TOKEN" \
  "https://users.internal.platform/api/users?pageSize=5" | \
  jq -e '.data | length > 0' > /dev/null

# Paso 4: Obtener un usuario especifico por ID (de la respuesta de lista)
USER_ID=$(curl -s -H "Authorization: Bearer $HEALTH_TOKEN" \
  "https://users.internal.platform/api/users?pageSize=1" | \
  jq -r '.data[0].id')

curl -s -H "Authorization: Bearer $HEALTH_TOKEN" \
  "https://users.internal.platform/api/users/$USER_ID" | \
  jq -e '.id == "'$USER_ID'"' > /dev/null
```

### 8.6 Comportamiento en modo degradado

Cuando una dependencia no esta saludable, el servicio entra en modo degradado:

| Dependencia no saludable | Comportamiento del servicio | Sonda de readiness |
|---|---|---|
| **PostgreSQL** | Todas las operaciones CRUD de usuario fallan con 503. La cache de validacion JWKS aun puede servir solicitudes durante 5 min (no se requiere llamada a Auth Service para validacion JWT de solo lectura). | No saludable |
| **Auth Service** | La cache JWKS sirve validacion de tokens hasta por 5 min. Despues de que la cache expire, todas las solicitudes autenticadas fallan con 503. El endpoint `/api/health/live` y los endpoints no autenticados aun funcionan. | No saludable (despues de que expire la cache) |
| **Service Bus** | La publicacion de eventos se pone en cola en proceso (buffer limitado: 5,000 eventos). Si el buffer se llena, los eventos mas antiguos se descartan. El consumo de eventos se pausa (los eventos de autenticacion se ignoran). | No saludable si el buffer > 80% |
| **Graph API** | La sincronizacion de Entra ID falla; los perfiles existentes continuan sirviendo datos desactualizados. Sin impacto en las operaciones CRUD. | Saludable (dependencia blanda — impacto solo en sincronizacion) |

---

## 9. Automatizacion de runbooks

Los siguientes procedimientos son candidatos para automatizacion mediante Azure Automation Runbooks o Azure DevOps Pipelines:

| Procedimiento | Estado actual | Objetivo de automatizacion | Prioridad |
|---|---|---|---|
| Verificacion de salud de sincronizacion de Entra ID | Manual (diario) | Alerta de Grafana + informe programado | Alta |
| Monitoreo del trabajo de purga de eliminacion suave | Manual (diario) | Notificacion basada en alertas | Alta |
| Ejercicio de restauracion de copia de seguridad de PostgreSQL | Manual (trimestral) | Pipeline de Azure DevOps | Alta |
| Generacion de informe de capacidad | Manual (mensual) | Informe programado de Grafana | Media |
| Escaneo de vulnerabilidades de dependencias | Automatizado (semanal) | Ya automatizado | Completa |
| Verificacion de salud de indices de PostgreSQL | Manual (semanal) | Script SQL programado + informe | Media |
| Informe de uso de cuota (Graph API) | Manual (mensual) | Runbook de Azure Automation | Baja |

---

## 10. Escalamiento y soporte

### 10.1 Rotacion de guardia

| Rol | Contacto | Tiempo de respuesta |
|---|---|---|
| Guardia primario (SRE) | PagerDuty `platform-primary` | 15 min |
| Guardia secundario (Plataforma) | PagerDuty `platform-secondary` | 30 min |
| Gerente de ingenieria | Slack `@platform-eng-manager` | 1 hora |
| InfoSec | Slack `#infosec` | Variable segun severidad |

### 10.2 Definiciones de severidad

| Severidad | Definicion | Respuesta | Escalar despues de |
|---|---|---|---|
| **SEV1** | Servicio no disponible o imposibilidad de leer/escribir perfiles de usuario | 15 min | 30 min |
| **SEV2** | Rendimiento degradado, errores elevados o interrupcion parcial de funcionalidad (ej., sincronizacion fallando, purga estancada) | 30 min | 2 horas |
| **SEV3** | Problema no critico, cosmetico o problema de un solo inquilino | Siguiente dia habil | 1 semana |
| **SEV4** | Error menor, mejora de documentacion | Siguiente sprint | N/A |

### 10.3 Lista de verificacion de transferencia

Al transferir al siguiente ingeniero de guardia:

- [ ] Estado actual del incidente (si lo hay) revisado y documentado
- [ ] Capturas de dashboard tomadas para cualquier anomalia en curso
- [ ] Lista de verificacion diaria de la Seccion 2.1 completada
- [ ] Estado de sincronizacion de Entra ID verificado como saludable
- [ ] Estado del trabajo de purga de eliminacion suave confirmado
- [ ] Rotacion de PagerDuty confirmada y reenviada
- [ ] Cualquier ventana de mantenimiento programada comunicada

### 10.4 Documentos relacionados

| Documento | Ubicacion |
|---|---|
| Runbook de respuesta a incidentes | `docs/runbooks/incident-response.md` |
| Runbook de despliegue | `docs/runbooks/deployment.md` |
| Runbook de revertir | `docs/runbooks/rollback.md` |
| Reinicio del servicio | `docs/runbooks/restart-service.md` |
| Arquitectura de seguridad | `docs/architecture/security.md` |
| Vista de despliegue | `docs/architecture/deployment-view.md` |
| Variables y configuracion | `docs/api/variables.md` |
| Referencia de eventos | `docs/api/events.md` |
| Configuracion de monitoreo | `docs/decisions/monitoring.md` |
| Decisiones de observabilidad | `docs/decisions/observability.md` |

---

*Mantenido por el Equipo de Ingenieria de Plataforma. Ultima actualizacion: 2026-07-26.*
*Para preguntas o correcciones, abra un issue o contacte a `#platform-eng` en Slack.*
