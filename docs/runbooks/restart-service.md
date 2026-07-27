# Reinicio del Servicio -- Users Service

**Propietario del documento:** Equipo SRE de Plataforma
**Clasificacion:** Interno / Operaciones
**Audiencia principal:** SRE de guardia, Ingenieria de Plataforma

---

## Tabla de Contenidos

1. [Objetivo](#objetivo)
2. [Cundo usar este runbook](#cuando-usar-este-runbook)
3. [Requisitos previos](#requisitos-previos)
4. [Lista de verificacion previa al reinicio](#lista-de-verificacion-previa-al-reinicio)
5. [Procedimiento de reinicio](#procedimiento-de-reinicio)
    - [Reinicio gradual de pods (Estandar)](#reinicio-gradual-de-pods-estandar)
    - [Reinicio de un solo pod (Dirigido)](#reinicio-de-un-solo-pod-dirigido)
6. [Detalles del apagado gradual](#detalles-del-apagado-gradual)
7. [Verificacion durante el reinicio](#verificacion-durante-el-reinicio)
8. [Validacion posterior al reinicio](#validacion-posterior-al-reinicio)
9. [Revertir: Si el reinicio falla](#revertir-si-el-reinicio-falla)
10. [Acciones posteriores al incidente](#acciones-posteriores-al-incidente)

---

## Objetivo

Reiniciar de forma segura el Users Service con impacto cero o minimo para los usuarios de la plataforma. El procedimiento asegura que las solicitudes en curso se completen, los eventos en cola se drenen y la conectividad con el Auth Service -- la dependencia critica del servicio -- se restablezca antes de que la nueva instancia atienda trafico.

---

## Cuando usar este runbook

| Escenario | Tipo de reinicio | Urgencia |
|---|---|---|
| Despliegue de una nueva version | Reinicio gradual de pods | Planificado (ventana de cambios) |
| Aplicacion de cambios de configuracion | Reinicio gradual de pods | Planificado (ventana de cambios) |
| Pod atascado en CrashLoopBackOff | Reinicio de un solo pod | No planificado (investigar primero) |
| Fuga de memoria/CPU observada | Reinicio de un solo pod (dirigido) | No planificado |
| Rotacion de certificados (gRPC mTLS) | Reinicio gradual de pods | Planificado (ventana de mantenimiento) |
| Despues de rotacion de secretos en Key Vault | Reinicio gradual de pods | Planificado |

---

## Requisitos previos

| Recurso | Detalles |
|---|---|
| **Acceso a Kubernetes** | Contexto de `kubectl` configurado para el cluster y namespace objetivo (`users`). |
| **CLI de Azure** | `az` iniciado sesion con rol `Contributor` o `AKS Cluster Admin`. |
| **Acceso a monitoreo** | Dashboard de Grafana (ver [Observabilidad](../decisions/observability.md)) y Elastic/Kibana para inspeccion de logs. |
| **Canal de Slack** | `#platform-eng` (comunicacion) y `#platform-sre` (coordinacion). |
| **Ventana de cambios** | Confirmar que la hora actual esta dentro de la ventana de cambios aprobada (si aplica). |
| **PagerDuty** | Silenciar alertas de produccion para `users-service` durante el reinicio planificado para evitar falsos positivos. |

---

## Lista de verificacion previa al reinicio

Verifique cada elemento antes de iniciar el procedimiento de reinicio.

### 1. Verificar el estado del Auth Service

El Users Service tiene una dependencia critica en tiempo de ejecucion del Authentication Service. Si el Auth Service esta degradado o inalcanzable, los pods reiniciados se marcaran como `NotReady` una vez que expire la cache local de JWKS (5 minutos), lo que provocara una interrupcion total del servicio.

```bash
# Verificar el endpoint de estado del Auth Service
curl -s -o /dev/null -w "%{http_code}" https://auth-service.platform.svc.cluster.local:5103/health/ready

# Esperado: 200
```

```bash
# Alternativa: verificar mediante el readiness de Kubernetes
kubectl -n auth get pods -l app=auth-service --field-selector status.phase=Running
kubectl -n auth wait --for=condition=Ready pods -l app=auth-service --timeout=30s
```

**Si el Auth Service no esta saludable:** Abortar el reinicio. Notificar al equipo de guardia del Auth Service a traves de `#platform-sre` y seguir el runbook de respuesta a incidentes del Auth Service. No reiniciar el Users Service hasta que el Auth Service haya sido restaurado.

### 2. Verificar dependencias posteriores

| Dependencia | Comando de verificacion | Esperado |
|---|---|---|
| PostgreSQL primario | `kubectl -n users exec deploy/users-service -- pg_isready -h $(DB_HOST)` | `server accepts connections` |
| Service Bus | `az servicebus topic show --name auth-events --namespace <namespace>` | `status: Active` |
| Notification Service | `curl -s https://notification-service.platform.svc.cluster.local/health/ready` | `200` |

### 3. Verificar el retraso en el procesamiento de eventos

```bash
# Consultar la metrica de Prometheus via API de Grafana o endpoint directo
# Un backlog significativo (> 1000 eventos) debe drenarse antes de reiniciar
curl -s "http://prometheus.platform.svc.cluster.local:9090/api/v1/query?query=users_event_processing_lag_seconds" | jq '.data.result[].value[1]'
```

**Umbral:** Si el retraso es > 60 segundos o el backlog es > 500 eventos no procesados, permita que el servicio se ponga al dia antes de continuar. Notificar a `#platform-sre` sobre la demora.

### 4. Verificar el nivel de trafico actual

```bash
# Consultar la tasa de solicitudes (solicitudes por segundo) de los ultimos 5 minutos
curl -s "http://prometheus.platform.svc.cluster.local:9090/api/v1/query?query=rate(http_requests_total{job='users-service'}[5m])" | jq '.data.result[].value[1]'
```

Si el trafico supera el 75% de la capacidad agregada de las replicas (9 pods x 200 RPS = 1800 RPS tipico), considere escalar temporalmente antes de reiniciar (ver [Precaucion de escalado](#precaucion-de-escalado) a continuacion).

### 5. Verificar la presencia del sidecar de Istio

```bash
kubectl -n users get pods -l app=users-service -o jsonpath='{range .items[*]}{.metadata.name}{"\t"}{.status.containerStatuses[*].name}{"\n"}{end}'
```

Verifique que cada pod tenga dos contenedores: `users-api` y `istio-proxy`. La falta de un sidecar significa que el pod no se unira a la malla de servicios y no recibira trafico.

### 6. Notificar al equipo

Publicar un mensaje en `#platform-eng`:

> [RUNBOOK] Iniciando reinicio gradual de Users Service en {environment}. Duracion estimada: 5-10 minutos. Impacto esperado: ninguno (actualizacion gradual en progreso). Monitoreo: {Grafana dashboard link}.

### 7. Silenciar alertas no criticas

Silenciar temporalmente las alertas de pager para las siguientes condiciones en PagerDuty:

| Condicion de alerta | Justificacion |
|---|---|
| `users_service_pod_restarting` | Esperado durante el procedimiento |
| `users_event_processing_lag_seconds > 60` | Pico temporal durante el reinicio es normal |
| `users_http_error_rate > 1%` | Breves 503 durante el drenaje de conexiones son aceptables |

No silenciar `users_auth_service_unreachable` -- esa alerta debe permanecer activa.

### 8. Precaucion de escalado (Opcional)

Si el servicio esta operando con trafico elevado, agregue una replica adicional por region antes de comenzar el reinicio para absorber la sobrecarga del reemplazo gradual:

```bash
kubectl -n users scale deployment users-service --replicas=4  # Actualmente 3 por region
```

Registrar el numero de replicas base para poder reducirlo nuevamente despues de la validacion.

---

## Procedimiento de reinicio

### Reinicio gradual de pods (Estandar)

Usar para reinicios planificados, despliegues o cambios de configuracion. Este metodo reemplaza los pods de uno en uno, manteniendo el servicio disponible durante todo el proceso.

**Paso 1 -- Iniciar el reinicio gradual**

```bash
kubectl -n users rollout restart deployment/users-service
```

**Paso 2 -- Monitorear el progreso del despliegue**

```bash
kubectl -n users rollout status deployment/users-service --watch
```

El comando se bloquea y muestra el progreso a medida que cada pod antiguo se termina y un nuevo pod alcanza el estado `Ready`. Tiempo tipico de finalizacion: 3-7 minutos para 3 replicas.

**Paso 3 -- Observar el reemplazo de pods en tiempo real**

```bash
kubectl -n users get pods -l app=users-service -w
```

Verá cada pod pasar por estas fases:
```
Terminating → (graceful shutdown) → Completed
Pending → ContainerCreating → Running → (readiness probe) → Ready → (Istio iptables) → 1/1
```

**Paso 4 -- Verificar que el despliegue gradual se completo**

```bash
kubectl -n users rollout status deployment/users-service
# Salida esperada: deployment "users-service" successfully rolled out
```

---

### Reinicio de un solo pod (Dirigido)

Usar cuando un pod especifico presenta problemas (fuga de memoria, alta latencia, advertencias repetidas) y se desea minimizar la rotacion.

**Paso 1 -- Identificar el pod no saludable**

```bash
kubectl -n users get pods -l app=users-service
```

**Paso 2 -- Eliminar el pod (Kubernetes ReplicaSet lo recrea)**

```bash
kubectl -n users delete pod users-service-<random-suffix> --wait=false
```

El controlador ReplicaSet crea un reemplazo inmediatamente. Use `--wait=false` para evitar bloquearse en el periodo de terminacion del pod antiguo.

**Paso 3 -- Monitorear el reemplazo**

```bash
kubectl -n users get pods -l app=users-service -w | grep <replacement-name>
```

---

## Detalles del apagado gradual

Esta seccion describe lo que sucede cuando un pod recibe la senal SIGTERM. Comprender esto ayuda a solucionar problemas con pods que terminan lentamente.

### Secuencia de apagado (ventana de 15 segundos)

```
Tiempo 0s  SIGTERM enviado por kubelet
         ↓
Tiempo 0s  El proceso recibe SIGTERM
         ├── 1. Los endpoints de salud devuelven 503 (eliminado de la malla de servicios)
         ├── 2. El sidecar de Istio drena las conexiones HTTP/gRPC en curso
         └── 3. Secuencia de apagado de la aplicacion:
              ├── 3a. Dejar de aceptar nuevas solicitudes HTTP
              ├── 3b. Drenar conexiones HTTP/gRPC activas (max 10s)
              ├── 3c. Detener el consumidor de eventos (bomba de mensajes de Service Bus)
              ├── 3d. Completar el procesamiento de mensajes actuales de Service Bus
              │      └── Complete (Abandoned)PeekLock → Complete (si < 5 min de bloqueo)
              └── 3e. Cerrar el pool de conexiones de base de datos de forma ordenada
                      └── Devolver conexiones inactivas al pool
Tiempo 10s El hook PreStop (si esta configurado) entra en espera final
Tiempo 15s SIGKILL enviado por kubelet — terminacion forzada
```

### Comportamientos importantes

| Aspecto | Detalle |
|---|---|
| **Solicitudes HTTP en curso** | Se completan dentro de la ventana de drenaje de 10 segundos. Las solicitudes que exceden este umbral reciben un timeout de puerta de enlace (504) del sidecar de Istio. |
| **Conexiones HTTP abiertas** | Las conexiones keep-alive inactivas se cierran inmediatamente. El nuevo pod acepta nuevas conexiones. |
| **Procesamiento de mensajes de Service Bus** | El consumidor de eventos detiene la bomba de mensajes. Cualquier mensaje que se este procesando se completa si es posible (dentro del intervalo de renovacion de PeekLock). Si el procesamiento no puede finalizar a tiempo, el mensaje se abandona y se reentrega a otro pod. La tabla de deduplicacion (`event_deduplication`) garantiza un procesamiento como-maximo-una-vez. |
| **Pool de conexiones de BD** | Las conexiones inactivas se cierran. Las consultas en curso se completan dentro de la ventana de drenaje de 10 segundos. Las consultas de larga duracion (raras, < 1% de las solicitudes) que exceden la ventana de drenaje se terminan. El pool de conexiones del nuevo pod restablece las conexiones en la primera consulta. |
| **Conexiones gRPC al Auth Service** | Los canales mTLS existentes se cierran. El nuevo pod restablece las conexiones en la primera solicitud de validacion de JWT. |

### Configuracion del hook PreStop

```yaml
lifecycle:
  preStop:
    exec:
      command:
        - /bin/sh
        - -c
        - |
          echo "[$(date)] PreStop: waiting for in-flight requests to complete"
          # Give the readiness probe time to fail, removing this pod from the
          # service mesh before traffic stops flowing
          sleep 5
```

La pausa de 5 segundos en el hook PreStop es deliberada -- permite que la sonda de readiness falle (2 fallos consecutivos x periodo de 10s = ~20s para ser eliminado de EndpointSlice) antes de que el proceso termine. Esto evita una rafaga de errores 502/503 del sidecar de Istio.

### Parametros de terminacion configurables

| Parametro | Valor actual | Descripcion |
|---|---|---|
| `terminationGracePeriodSeconds` | 30s | Tiempo maximo entre SIGTERM y SIGKILL |
| Umbral de fallo de la sonda readiness | 2 | Fallos consecutivos antes de la eliminacion |
| Intervalo de la sonda readiness | 10s | Segundos entre sondas |

---

## Verificacion durante el reinicio

Realice estas verificaciones mientras el despliegue gradual esta en progreso.

### 1. Verificar el estado de los pods

```bash
# Observar la transicion de pods a Ready
kubectl -n users get pods -l app=users-service -w
```

### 2. Verificar la sonda de readiness (dependencia del Auth Service)

El endpoint de readiness en `GET /api/health/ready` verifica la conectividad con Auth Service (via gRPC o cache JWKS), PostgreSQL y Service Bus. Esta es la verificacion que determina si el pod recibe trafico.

```bash
# Port-forward a un pod recien iniciado y verificar su readiness
kubectl -n users port-forward pod/users-service-<new-pod> 7201:7201 &
curl -s http://localhost:7201/api/health/ready | jq .
kill %1
```

Respuesta esperada:
```json
{
  "status": "Healthy",
  "checks": [
    { "name": "database", "status": "Healthy" },
    { "name": "auth_service", "status": "Healthy", "cacheValid": true },
    { "name": "service_bus", "status": "Healthy" }
  ]
}
```

**Si `auth_service` muestra `Unhealthy` y `cacheValid` es `false`:** El nuevo pod no puede alcanzar el Auth Service y su cache JWKS esta vacia. El pod permanecera `NotReady` indefinidamente. Escalar inmediatamente al equipo del Auth Service y considerar la revertir (ver [Revertir](#revertir-si-el-reinicio-falla)).

### 3. Verificar el registro en la malla de servicios Istio

```bash
# Confirmar que el nuevo pod esta en el EndpointSlice
kubectl -n users get endpointslices -l kubernetes.io/service-name=users-service -o yaml | grep -A 2 addresses
```

La salida debe incluir la direccion IP del nuevo pod. Si esta ausente, la sonda de readiness esta fallando y el pod no esta recibiendo trafico.

### 4. Monitorear la tasa de error durante el reinicio

```bash
# Verificar errores 503/504 durante la ventana de drenaje
curl -s "http://prometheus.platform.svc.cluster.local:9090/api/v1/query?query=rate(http_requests_total{job='users-service',status=~'5..'}[1m])" | jq '.data.result[].value[1]'
```

Un breve pico de < 10 respuestas 5xx durante la ventana de drenaje de 10 segundos es aceptable. Errores sostenidos despues de que se complete el reinicio indican un problema con los nuevos pods.

### 5. Monitorear el retraso en el procesamiento de eventos

```bash
curl -s "http://prometheus.platform.svc.cluster.local:9090/api/v1/query?query=users_event_processing_lag_seconds" | jq '.data.result[].value[1]'
```

El retraso puede aumentar a 30-60 segundos durante el reinicio mientras los pods se reciclan. Debe volver a < 10 segundos dentro de los 2 minutos posteriores a la finalizacion del despliegue gradual.

---

## Validacion posterior al reinicio

Despues de que el despliegue muestre `successfully rolled out`, ejecute el conjunto completo de validacion.

### 1. Todos los pods saludables

```bash
kubectl -n users get pods -l app=users-service
# Esperado: todos los pods "Running" y "Ready (1/1)"
```

### 2. Conectividad con Auth Service

```bash
# Activar una validacion JWT ejecutando una verificacion de salud que llame al Auth Service
kubectl -n users exec deploy/users-service -- /bin/sh -c \
  "wget -q -O- http://localhost:7201/api/health/ready | grep auth_service"

# Esperado: "auth_service": "Healthy"
```

### 3. Prueba de humo API de extremo a extremo

Ejecutar una prueba de humo de solo lectura contra el endpoint de cada region para confirmar que el servicio responde correctamente.

```bash
# West Europe (primario)
curl -s -w "\nHTTP %{http_code}" \
  -H "Authorization: Bearer $(gcloud auth print-access-token)" \
  https://users.we.platform.internal/api/health/live

# Esperado: {"status":"Healthy"} HTTP 200
```

```bash
# North Europe (secundario)
curl -s -w "\nHTTP %{http_code}" \
  -H "Authorization: Bearer $(gcloud auth print-access-token)" \
  https://users.ne.platform.internal/api/health/live

# Esperado: {"status":"Healthy"} HTTP 200
```

### 4. Conectividad de base de datos

```bash
kubectl -n users exec deploy/users-service -- /bin/sh -c \
  "wget -q -O- http://localhost:7201/api/health/ready | grep database"

# Esperado: "database": "Healthy"
```

### 5. Procesamiento de eventos reanudado

```bash
# Verificar que los contadores de procesamiento de eventos esten incrementando
curl -s "http://prometheus.platform.svc.cluster.local:9090/api/v1/query?query=rate(users_events_processed_total[1m])" | jq '.data.result[].value[1]'

# Esperado: valor > 0 (los eventos se estan procesando)
```

### 6. Restaurar alertas

Reactivar las alertas silenciadas durante la lista de verificacion previa al reinicio. Verificar las alertas activas:

```bash
curl -s "http://alertmanager.platform.svc.cluster.local:9093/api/v2/alerts" | jq '.data | length'
```

Confirmar que no hay reglas de alerta disparadas para `users-service` excepto aquellas preexistentes antes del reinicio.

### 7. Reducir escala (Si se escalo)

Si se agregaron replicas adicionales durante la fase previa al reinicio, volver a la linea base:

```bash
kubectl -n users scale deployment users-service --replicas=<original-replica-count>
```

### 8. Reportar finalizacion

Publicar en `#platform-eng`:

> [RUNBOOK] Reinicio gradual de Users Service en {environment} completado exitosamente. Duracion: {duration}. Conectividad con Auth Service verificada. Procesamiento de eventos reanudado. Todas las pruebas de humo pasaron. Dashboard: {Grafana dashboard link}.

---

## Revertir: Si el reinicio falla

Si un pod no logra estar `Ready`, el despliegue gradual se queda atascado o las tasas de error son elevadas despues del reinicio, revertir inmediatamente.

### Disparadores de revertir

| Condicion | Accion |
|---|---|
| Un pod permanece en `CrashLoopBackOff` por > 2 minutos | Revertir |
| La sonda de readiness falla por > 60 segundos en cualquier pod nuevo | Revertir |
| Tasa de error superior al 5% sostenida por > 2 minutos | Revertir |
| Auth Service reporta `Unhealthy` en pods nuevos con cache vacia | Revertir |
| El retraso de procesamiento de eventos supera los 300 segundos y sigue aumentando | Revertir |

### Revertir mediante `kubectl rollout undo`

```bash
# Revertir a la revision anterior
kubectl -n users rollout undo deployment/users-service

# Monitorear la revertir
kubectl -n users rollout status deployment/users-service --watch
```

### Revertir a una revision especifica

```bash
# Listar revisiones disponibles
kubectl -n users rollout history deployment/users-service

# Revertir a una revision especifica (ej., revision 3)
kubectl -n users rollout undo deployment/users-service --to-revision=3
```

### Revertir mediante Helm (si se usa Helm)

```bash
helm -n users rollback users-service <previous-revision-number>
```

### Verificacion posterior a la revertir

1. Ejecutar el conjunto completo de Validacion posterior al reinicio anterior.
2. Confirmar que todos los pods originales esten `Ready`.
3. Confirmar la conectividad con Auth Service, el procesamiento de eventos y el estado de la API.
4. En `#platform-eng`, publicar:

> [ROLLBACK] Reinicio gradual de Users Service fallo — se revirtio a la revision {N}. Causa raiz: {summary}. Ticket de triage: {link}.

5. Crear un ticket post-incidente documentando el fallo (ver [Acciones posteriores al incidente](#acciones-posteriores-al-incidente)).

### Revertir si el Auth Service es la causa

Si los nuevos pods estan fallando las verificaciones de readiness porque el Auth Service es inalcanzable:

1. No revertir el Users Service -- que el Auth Service este caido significa que los pods antiguos tambien se verian afectados una vez que su cache JWKS expire.
2. Enfocarse en restaurar primero el Auth Service.
3. Si se espera que la recuperacion del Auth Service tome mas de 5 minutos, considere aumentar temporalmente el valor de `Auth__JWKSCacheTtlMinutes` para el Users Service mediante ConfigMap (requiere otro reinicio, por lo que debe coordinarse como medida de recuperacion).

---

## Acciones posteriores al incidente

Si el reinicio provoco una revertir o causo impacto visible para el usuario:

1. **Abrir un ticket de revision post-incidente (PIR)** con lo siguiente:
   - Marca de tiempo del intento de reinicio
   - Metricas previas al reinicio (trafico, retraso de eventos, salud de dependencias)
   - Que paso fallo y los sintomas observados
   - Metodo de revertir y duracion
   - Captura(s) del dashboard de Grafana de la ventana del incidente
   - Logs de los pods fallidos

2. **Capturar logs de los pods fallidos** antes de que sean recolectados como basura:

```bash
kubectl -n users logs deploy/users-service --previous --tail=200 > users-service-previous-pod.log
kubectl -n users logs deploy/users-service --tail=500 > users-service-current-pod.log
```

3. **Revisar fallos de la sonda de readiness** en los logs de kubelet:

```bash
# Verificar eventos de kubelet para el namespace
kubectl -n users get events --sort-by='.lastTimestamp' | grep -i 'unhealthy'
```

4. **Actualizar este runbook** si el procedimiento no fue claro o faltaba un paso relevante para el fallo.

---

## Documentos relacionados

| Documento | Descripcion |
|---|---|
| [Vista de despliegue](../architecture/deployment-view.md) | Topologia de AKS, configuracion de verificaciones de salud, dependencia del Auth Service |
| [Contexto del sistema](../architecture/context.md) | Detalles de dependencia del Auth Service, circuit breaker, comportamiento de cache JWKS |
| [Eventos](../api/events.md) | Garantias de procesamiento de eventos, deduplicacion, duracion del bloqueo de mensajes |
| [Variables y configuracion](../api/variables.md) | Variables de entorno, feature flags, configuraciones de timeout del Auth Service |
| [Runbook de despliegue](deployment.md) | Procedimiento completo de despliegue para nuevas versiones |
| [Runbook de revertir](rollback.md) | Procedimientos generales de revertir para despliegues fallidos |
| [Respuesta a incidentes](incident-response.md) | Clasificacion de incidentes, niveles de severidad y rutas de escalamiento |
| [Observabilidad](../decisions/observability.md) | Metricas, dashboards y configuracion de alertas |

---

## Historial de revisiones

| Fecha | Autor | Cambios |
|---|---|---|
| 2026-07-26 | Equipo SRE de Plataforma | Version inicial |
