# Cómo Depurar — Users Service

**Servicio:** `users-service` | **Dominio:** `identity` | **Propietario:** Equipo de Ingeniería de Plataforma | **Última Actualización:** 2026-07-26

## Tabla de Contenidos

1. [Introducción](#1-introducción)
2. [Requisitos Previos](#2-requisitos-previos)
3. [Depuración de Fallos en Validación JWT](#3-depuración-de-fallos-en-validación-jwt)
4. [Rastreo de Solicitudes entre Servicios](#4-rastreo-de-solicitudes-entre-servicios)
5. [Depuración de Problemas del Consumidor de Eventos](#5-depuración-de-problemas-del-consumidor-de-eventos)
6. [Errores Comunes y Resoluciones](#6-errores-comunes-y-resoluciones)
7. [Configuración de Depuración en VS Code](#7-configuración-de-depuración-en-vs-code)
8. [Referencia de Comandos de Diagnóstico](#8-referencia-de-comandos-de-diagnóstico)
9. [Documentos Relacionados](#9-documentos-relacionados)

---

## 1. Introducción

Esta guía cubre las técnicas de depuración para el Users Service. Está dirigida a ingenieros de plataforma, SREs de guardia y desarrolladores que trabajan en el servicio.

El Users Service tiene tres superficies de ejecución principales que producen modos de falla distintos:

| Superficie | Modo de Falla | Síntomas Típicos |
|---|---|---|
| **Validación JWT** | Cada solicitud autenticada requiere un JWT válido. Si la validación falla, el servicio devuelve 401 o recurre a un estado degradado. | `401 Unauthorized`, `503 Service Unavailable`, pico en `users_jwt_validation_errors_total` |
| **Procesamiento de solicitudes** | Operaciones CRUD de usuarios, verificaciones RBAC, consultas a la base de datos y llamadas a servicios internos (gRPC, Service Bus). | `500 Internal Server Error`, latencia p99 lenta, `NpgsqlException`, timeouts |
| **Consumo de eventos** | Procesamiento en segundo plano de eventos de autenticación (`user.login`, `user.logout`, `token.revoked`) desde Azure Service Bus. | Marcas de tiempo `last_login_at` desactualizadas, alerta `users_event_processing_lag_seconds`, crecimiento de la cola de mensajes fallidos |

Comienza con la superficie que coincida con los síntomas, luego usa los comandos de diagnóstico en [Sección 8](#8-referencia-de-comandos-de-diagnóstico) para profundizar.

---

## 2. Requisitos Previos

### 2.1 Herramientas Requeridas

| Herramienta | Propósito | Verificación |
|---|---|---|
| .NET 10 SDK | Compilar, ejecutar y depurar localmente | `dotnet --version` debe mostrar `10.0.100+` |
| VS Code (o Rider/VS) | Depuración con puntos de interrupción, configuraciones de inicio | — |
| `curl` / `httpie` | Pruebas manuales de API | `curl --version` |
| `jq` | Análisis JSON desde la línea de comandos | `jq --version` |
| `kubectl` | Operaciones de clúster (staging/producción) | `kubectl version --short` |
| `grpcurl` | Introspección gRPC para Auth Service | `grpcurl --version` |
| Azure CLI | Service Bus, Key Vault, App Configuration | `az version` |
| `jwt-cli` o `jwt.ms` | Decodificar cargas útiles JWT sin validación | `npm install -g jwt-cli` o visita `https://jwt.ms` |

### 2.2 Configuración Específica del Entorno

| Configuración | Local (Desarrollo) | Producción |
|---|---|---|
| Emisor de Auth | `https://localhost:7103` | `https://auth.internal.platform` |
| Audiencia de Auth | `users-service-dev` | `users-service` |
| Endpoint gRPC de Auth | `https://localhost:5103` | `https://auth-service.platform.svc.cluster.local:5103` |
| TTL de Caché JWKS | 1 minuto | 5 minutos |
| Nivel de Registro | `Debug` | `Information` |

Estos valores se establecen en [`appsettings.Development.json`](../../src/UsersService/appsettings.Development.json) y [`appsettings.json`](../../src/UsersService/appsettings.json).

---

## 3. Depuración de Fallos en Validación JWT

### 3.1 Entendiendo el Pipeline de Validación

El Users Service valida los tokens JWT en **dos capas** (defensa en profundidad):

```
Cliente → API Gateway (validación perimetral) → Users Service (validación a nivel de servicio)
                                                      │
                                                      ├─ Verificar caché JWKS (local, en memoria)
                                                      │    ├─ Acierto → validar firma RS256 localmente
                                                      │    └─ Fallo → llamada gRPC al Auth Service
                                                      │               └─ En éxito → poblar caché
                                                      │
                                                      ├─ Extraer claims: sub, roles, tid
                                                      ├─ Aplicar RBAC: ¿el rol está permitido para este endpoint?
                                                      └─ Ejecutar consulta (con alcance de tenant_id del JWT)
```

La caché JWKS es el mecanismo crítico de resiliencia. Cuando el Auth Service no está accesible, la caché mantiene el servicio operativo durante su TTL configurado (5 minutos en producción, 1 minuto en desarrollo).

### 3.2 Fallos Comunes de Validación JWT

#### 3.2.1 Token Expirado

**Síntoma:** `401 Unauthorized` con detalle que contiene `"token has expired"` o `"SecurityTokenExpiredException"`.

El token de acceso tiene una duración de 15 minutos (configurable mediante `Auth:AccessTokenLifetimeMinutes` en el Auth Service). El cliente debe actualizarlo usando el token de actualización antes de que expire.

**Diagnóstico:**

```bash
# Decodificar el token para verificar la claim exp (sin validación de firma)
jwt decode <token>

# Buscar:
# {
#   "exp": 1690000000,
#   ...
# }
# Comparar con: date -d @1690000000
```

**Resolución:** El cliente debe llamar a `POST /api/auth/refresh` con un token de actualización válido para obtener un nuevo token de acceso.

#### 3.2.2 Emisor o Audiencia Incorrectos

**Síntoma:** `401 Unauthorized` con `"IDX10205: Issuer validation failed"` o `"IDX10214: Audience validation failed"`.

El servicio valida que `iss` coincida con `Auth:Issuer` y `aud` coincida con `Auth:Audience`. Esto es una mala configuración común cuando se apunta al entorno incorrecto.

**Diagnóstico:**

```bash
# Decodificar el token
jwt decode <token>

# Comparar contra los valores esperados:
#   Issuer:   https://auth.internal.platform (o https://localhost:7103 para desarrollo)
#   Audience: users-service (o users-service-dev para desarrollo)
```

**Verificar lo que el servicio espera:**

```bash
# Desde appsettings.json
cat src/UsersService/appsettings.json | jq '.Auth'

# O mediante el endpoint de salud (si está expuesto)
curl -s https://users-service.platform/api/health/ready | jq '.'
```

**Resolución:** Asegúrate de que el token fue emitido por la misma instancia del Auth Service en la que el Users Service está configurado para confiar. En desarrollo, ambos servicios deben usar valores coherentes. En producción, verifica las variables de entorno `Auth__Issuer` y `Auth__Audience` en el pod:

```bash
kubectl exec deploy/users-service -n platform -- env | grep Auth__
```

#### 3.2.3 Firma Inválida

**Síntoma:** `401 Unauthorized` con `"IDX10503: Signature validation failed"` o `"IDX10501: Signature validation failed. Unable to match key"`.

El token fue firmado con una clave que el servicio no reconoce. Causas comunes:

- El Auth Service rotó su clave de firma, pero el servicio está usando una caché JWKS desactualizada.
- El token proviene de una instancia diferente del Auth Service (ej., staging vs. producción).
- El token es un token de prueba autofirmado que nunca fue emitido por el Auth Service.

**Diagnóstico -- Obtener la clave pública esperada:**

```bash
# Obtener el JWKS del Auth Service
curl -s https://auth.internal.platform/.well-known/jwks.json | jq '.'

# Comparar el 'kid' en la cabecera del token con el 'kid' en el JWKS
jwt decode <token>   # Revisar header.kid
```

**Verificar la antigüedad de la caché JWKS en el Users Service:**

```bash
# Métrica de Prometheus
curl -s http://localhost:7201/metrics | grep users_jwks_cache_age_seconds
```

Si la caché es más antigua que el TTL configurado y el Auth Service no está accesible, la caché está desactualizada.

**Resolución:**

1. Verifica que el Auth Service esté saludable y que su endpoint JWKS sea accesible.
2. Si la clave fue rotada legítimamente, la caché se actualizará en la próxima llamada gRPC exitosa al Auth Service (dentro del TTL).
3. En una emergencia, puedes forzar la limpieza de la caché reiniciando los pods del Users Service:

```bash
kubectl rollout restart deployment/users-service -n platform
```

#### 3.2.4 Token Revocado (Lista Negra JTI)

**Síntoma:** `401 Unauthorized` con `"Token has been revoked"`.

El `jti` (ID del JWT) del token ha sido agregado a la lista negra mediante el flujo de cierre de sesión o actualización de token.

**Diagnóstico:** Esto es intencional. El token fue explícitamente revocado mediante un cierre de sesión, o una rotación de token de actualización detectó un ataque de repetición y revocó toda la familia de tokens.

**Resolución:** El cliente debe obtener un nuevo token iniciando sesión nuevamente.

#### 3.2.5 Claims Faltantes o Mal Formadas

**Síntoma:** `401 Unauthorized` o `403 Forbidden` con `"Missing required claim"` o `"Invalid claim format"`.

**Diagnóstico -- Inspeccionar las claims:**

```bash
jwt decode <token> | jq '.payload'

# Claims esperadas:
# - sub:  UUID del usuario (requerido)
# - roles: arreglo de cadenas (requerido para RBAC)
# - tid:  UUID del tenant (requerido para tenencia)
# - jti:  ID del token (requerido para verificación de revocación)
```

**Resolución:** Asegúrate de que el Auth Service esté configurado para incluir todas las claims requeridas en el JWT. Consulta la implementación de `TokenService.IssueTokensAsync()` en el Auth Service para ver el conjunto exacto de claims.

### 3.3 Análisis de Registros de Validación JWT

Busca en los registros de la aplicación eventos relacionados con JWT:

```bash
# Consulta de registro estructurado (Elasticsearch)
# Buscar fallos de validación JWT
index: "logs-platform-*"
"users_jwt_validation_errors_total"

# Mensajes de registro comunes para buscar:
# - "Token validation failed: ..."
# - "JWT expired"
# - "IDX10205: Issuer validation failed"
# - "IDX10503: Signature validation failed"
# - "AuthServiceClient: gRPC call failed, falling back to JWKS cache"
# - "JWKS cache miss, calling Auth Service..."

# Registros de desarrollo local
dotnet run --project src/UsersService 2>&1 | grep -i jwt
```

### 3.4 Prueba Rápida de Validación JWT

Usa las credenciales de prueba del Auth Service para verificar la validación JWT de extremo a extremo:

```bash
# 1. Iniciar sesión para obtener un token (desarrollo local)
TOKEN=$(curl -s -X POST https://localhost:5103/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "Platform@2026!"}' | jq -r '.accessToken')

# 2. Probar el token contra el Users Service
curl -s -o /dev/null -w "%{http_code}" \
  -H "Authorization: Bearer $TOKEN" \
  https://localhost:7201/api/users

# Esperado: 200

# 3. Probar con un token expirado/inválido
curl -s -o /dev/null -w "%{http_code}" \
  -H "Authorization: Bearer token-invalido" \
  https://localhost:7201/api/users

# Esperado: 401
```

---

## 4. Rastreo de Solicitudes entre Servicios

### 4.1 Rastreo Distribuido con OpenTelemetry

Cada solicitud al Users Service lleva un Contexto de Rastro W3C (cabecera `traceparent`). Esto permite correlacionar una sola solicitud de usuario a través de la API Gateway, Users Service, Auth Service, PostgreSQL y Service Bus.

**Formato del contexto de rastro:**

```
traceparent: 00-<trace-id>-<span-id>-01
```

### 4.2 Lectura de IDs de Rastro en Registros

El Users Service emite registros JSON estructurados con el ID de correlación enriquecido automáticamente por `Enrich.FromLogContext()` de Serilog.

**Ejemplo de línea de registro:**

```json
{
  "@timestamp": "2026-07-26T10:30:00.123Z",
  "level": "Error",
  "messageTemplate": "JWT validation failed for request {Method} {Path}",
  "message": "JWT validation failed for request GET /api/users",
  "properties": {
    "Method": "GET",
    "Path": "/api/users",
    "TraceId": "00-abcdef1234567890abcdef1234567890-abcdef1234567890-01",
    "SpanId": "abcdef1234567890",
    "StatusCode": 401,
    "tenant_id": "00000000-0000-0000-0000-000000000001",
    "requestor_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
  }
}
```

### 4.3 Correlación entre Servicios

Cuando una solicitud fluye del Users Service al Auth Service (validación gRPC), el contexto de rastro se propaga automáticamente mediante la instrumentación gRPC de OpenTelemetry.

**Pasos para correlacionar:**

1. Captura el `TraceId` de una entrada de registro de error del Users Service.
2. Busca en los registros del Auth Service el mismo `TraceId`:

```bash
# Consulta en Elasticsearch
index: "logs-platform-auth-*"
"TraceId": "00-abcdef1234567890abcdef1234567890-*"
```

3. Si el rastro está muestreado (10% de muestreo en producción), visualízalo en el colector de OpenTelemetry o Grafana Tempo:

```
https://grafana.internal/explore?traceId=abcdef1234567890abcdef1234567890
```

### 4.4 Agregar Atributos de Span Personalizados

Al agregar instrumentación a nuevas rutas de código, usa `ActivitySource` para crear spans:

```csharp
using System.Diagnostics;

public class UserService
{
    private static readonly ActivitySource ActivitySource = new("Platform.UsersService");

    public async Task<UserResult<UserDto>> CreateUserAsync(CreateUserRequest request, Guid tenantId, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("UserService.CreateUser");
        activity?.SetTag("tenant_id", tenantId.ToString());
        activity?.SetTag("username", request.Username);

        // ... cuerpo del método ...
    }
}
```

Las etiquetas establecidas en la actividad aparecen en Grafana Tempo / Jaeger y permiten filtrar por tenant o usuario.

### 4.5 Propagación Manual del ID de Rastro

Al depurar localmente sin un backend de rastreo, puedes inyectar tu propia cabecera `traceparent`:

```bash
curl -H "traceparent: 00-debug1234567890abcdef1234567890001-debugspan0000000001-01" \
  -H "Authorization: Bearer $TOKEN" \
  https://localhost:7201/api/users
```

Busca en los registros del servicio `TraceId` que contenga `"debug1234567890abcdef1234567890001"` para aislar los registros de tu solicitud.

---

## 5. Depuración de Problemas del Consumidor de Eventos

El Consumidor de Eventos es un `BackgroundService` que se suscribe al tópico `auth-events` en Azure Service Bus. Procesa eventos `user.login`, `user.logout` y `token.revoked`.

### 5.1 Arquitectura del Consumidor de Eventos

```
Azure Service Bus (tópico auth-events)
    │
    ├─ Suscripción habilitada para sesiones (ID de sesión = userId)
    │  ├─ Entrega en orden por usuario
    │  └─ Máximo 10 manejadores concurrentes por pod
    │
    ▼
Users Service: EventConsumer (BackgroundService)
    │
    ├─ Deserializar sobre del evento
    ├─ Verificar tabla de deduplicación (event_deduplication)
    │    ├─ Ya procesado → completar mensaje (sin operación)
    │    └─ Evento nuevo → procesar
    │
    ├─ Procesar evento:
    │    ├─ user.login  → UPDATE last_login_at
    │    ├─ user.logout → UPDATE last_logout_at
    │    └─ token.revoked → INSERT INTO token_revocations
    │
    ├─ Registrar en tabla de deduplicación
    └─ Completar mensaje en Service Bus
```

### 5.2 Verificar el Retraso en el Procesamiento de Eventos

La métrica de salud principal para el consumidor de eventos es `users_event_processing_lag_seconds`.

**Umbral de advertencia:** > 60 segundos durante 5 minutos
**Umbral crítico:** > 300 segundos

```bash
# Consulta de Prometheus
users_event_processing_lag_seconds

# Panel de Grafana
# Navegar a: Users Service → Event Processing
```

**Si el retraso está aumentando:**

1. Verifica el rendimiento del consumidor:
   ```bash
   # Prometheus — eventos procesados por segundo
   rate(users_events_processed_total[5m])
   ```

2. Verifica si hay procesamiento limitado o bloqueado:
   ```bash
   # Búsqueda en registros de aplicación
   grep -E "(EventProcessingException|DeadLetterException|MessageLockLost)" \
     <archivo_de_registro>
   ```

3. Inspecciona la cola de mensajes fallidos:
   ```bash
   az servicebus topic subscription show \
     --resource-group platform-rg \
     --namespace-name platform-sb \
     --topic-name auth-events \
     --subscription-name users-service \
     --query "deadLetteringOnMessageExpiration"

   # Ver mensajes en la cola de mensajes fallidos
   az servicebus topic subscription message peek \
     --resource-group platform-rg \
     --namespace-name platform-sb \
     --topic-name auth-events \
     --subscription-name users-service/$DeadLetterQueueName
   ```

### 5.3 Fallos Comunes del Consumidor de Eventos

#### 5.3.1 Mensaje Dañado

Un mensaje que no puede procesarse debido a problemas de esquema o datos.

**Síntomas:**
- `users_events_processed_total` se estabiliza mientras `ActiveMessages` en la suscripción crece
- Los registros muestran `EventProcessingException: Failed to deserialize event` o `DbException: Insert or update on table "users" violates foreign key constraint`
- Mensajes que aparecen en la cola de mensajes fallidos

**Diagnóstico:**

```bash
# Leer el cuerpo del mensaje fallido
az servicebus topic subscription message peek \
  --resource-group platform-rg \
  --namespace-name platform-sb \
  --topic-name auth-events \
  --subscription-name users-service/$DeadLetterQueueName | jq '.[0].body'
```

Buscar:
- Campo `userId` faltante
- JSON mal formado (comas adicionales/faltantes, cadenas sin comillas)
- `userId` que referencia un usuario que no existe (violación de clave foránea)
- Tipo de evento incorrecto (`user.unknown` en lugar de `user.login`)

**Resolución:**

1. Si el mensaje está genuinamente mal formado y no puede procesarse, elimínalo de la cola de mensajes fallidos:

```bash
az servicebus topic subscription message receive \
  --resource-group platform-rg \
  --namespace-name platform-sb \
  --topic-name auth-events \
  --subscription-name users-service/$DeadLetterQueueName \
  --count 1
```

2. Si el esquema ha cambiado (se agregaron nuevos campos), actualiza la lógica de deserialización en el Consumidor de Eventos y vuelve a implementar.

3. Si el problema fue un fallo transitorio de la base de datos (ej., timeout de conexión), reenvía los mensajes fallidos de vuelta a la suscripción principal (Azure Portal: Service Bus Explorer -> Dead-letter -> Re-send).

#### 5.3.2 Tormenta de Reintentos de Eventos Duplicados

Si los mismos eventos se redistribuyen repetidamente, la tabla de deduplicación (`event_deduplication`) crece rápidamente, pudiendo causar:
- Alto uso de memoria de la caché de deduplicación
- Rendimiento lento de `INSERT` a medida que la tabla crece
- Fallos de idempotencia con falsos positivos

**Diagnóstico:**

```sql
-- Verificar tasa de crecimiento de la tabla de deduplicación
SELECT COUNT(*), MIN(processed_at), MAX(processed_at)
FROM event_deduplication
WHERE processed_at > NOW() - INTERVAL '1 hour';

-- Verificar IDs de eventos duplicados
SELECT event_id, COUNT(*) as occurrence_count
FROM event_deduplication
WHERE processed_at > NOW() - INTERVAL '1 hour'
GROUP BY event_id
HAVING COUNT(*) > 1;
```

**Resolución:**

1. Verifica que la detección de duplicados de la suscripción de Service Bus esté habilitada (debería estarlo, pero una mala configuración puede causar reintentos).
2. Verifica que el Consumidor de Eventos complete los mensajes correctamente -- si falla al completar, Service Bus redistribuye después de que el bloqueo expire (predeterminado: 30 segundos).
3. Si la tabla de deduplicación es demasiado grande (> 100k entradas), es posible que el trabajo de limpieza nocturna necesite ajustes. Ejecuta una limpieza manual:

```sql
DELETE FROM event_deduplication
WHERE processed_at < NOW() - INTERVAL '7 days';
```

#### 5.3.3 Bloqueo de Mensaje Perdido

Si un evento tarda más de 5 minutos en procesarse (la duración máxima de bloqueo), Service Bus libera el bloqueo y otro consumidor puede recogerlo.

**Síntomas:**
- `MessageLockLostException` en los registros
- El mismo evento procesado múltiples veces (duplicados en actualizaciones de `last_login_at`)

**Resolución:**

1. Verifica si alguna consulta en particular es lenta (índice faltante en la tabla `users` para la ruta de actualización de eventos):

```sql
EXPLAIN ANALYZE UPDATE users
SET last_login_at = NOW()
WHERE id = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890';
```

2. Si la duración del bloqueo es consistentemente insuficiente, considera dividir el trabajo en operaciones más pequeñas o aumentar la duración del bloqueo en la configuración de la suscripción de Service Bus.

### 5.4 Simulación de Eventos Localmente

Para desarrollo, puedes simular eventos sin Azure Service Bus llamando directamente a la lógica de procesamiento de eventos:

```csharp
// En una sesión de prueba o depuración
var consumer = serviceProvider.GetRequiredService<IEventConsumer>();
await consumer.ProcessEventAsync(new AuthEvent
{
    Type = "user.login",
    UserId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
    Timestamp = DateTimeOffset.UtcNow
}, CancellationToken.None);
```

### 5.5 Métricas del Consumidor de Eventos

| Métrica | Tipo | Qué Te Indica |
|---|---|---|
| `users_events_processed_total` | Contador | Rendimiento -- debe ser > 0 cuando hay eventos en el bus |
| `users_event_processing_lag_seconds` | Medidor | Cuánto retraso tiene el consumidor |
| `users_event_processing_duration_seconds` | Histograma | Cuánto tiempo toma procesar cada evento |
| `users_event_dlq_count` | Medidor | Número de mensajes en la cola de mensajes fallidos |
| `users_event_deduplication_cache_size` | Medidor | Tamaño de la caché de deduplicación en memoria |

---

## 6. Errores Comunes y Resoluciones

### 6.1 Auth Service Inaccesible

**Estado HTTP:** `503 Service Unavailable` en todos los endpoints autenticados

**Síntomas:**
- `users_auth_service_grpc_latency` mostrando timeouts o `connection refused`
- `users_auth_service_grpc_errors_total` > 0
- `users_jwks_cache_age_seconds` > 300 (caché expirada)
- Sonda de readiness fallando en la verificación de `auth_service`

**Tabla de Análisis de Causa Raíz:**

| Observación | Causa Probable | Siguiente Paso |
|---|---|---|
| Pods del Auth Service en `CrashLoopBackOff` | Despliegue del Auth Service roto | Seguir el runbook del Auth Service |
| Pods del Auth Service funcionando pero puerto gRPC inaccesible | Problema de certificado mTLS o política de red | Verificar `kubectl describe endpoints auth-service -n platform` |
| gRPC accesible pero devuelve errores | Fallo de verificación de salud del Auth Service o sobrecarga | Verificar métricas `ConnectionErrors` y `RequestRate` del Auth Service |
| Auth Service saludable desde pod de depuración pero Users Service no puede conectar | Problema de enrutamiento de Service Mesh (Istio), certificado mTLS faltante, o fallo de resolución DNS | Verificar registros del proxy Istio en el pod de Users Service: `kubectl logs deploy/users-service -c istio-proxy -n platform` |
| gRPC saludable pero caché JWKS expirada | Partición de red entre Users Service y Auth Service, o lógica de actualización de caché JWKS rota | Verificar reglas de firewall, luego verificar tasa de error de `AuthServiceClient.GetJwksAsync()` |

**Pasos de resolución inmediata:**

1. Verifica si la caché JWKS sigue siendo válida:
   ```bash
   # Si < 300 segundos, el servicio sigue operativo desde la caché
   curl -s http://localhost:7201/metrics | grep users_jwks_cache_age_seconds
   ```

2. Verifica la conectividad desde un pod de depuración:
   ```bash
   kubectl run debug-pod --image=nicolaka/netshoot -n platform --rm -it -- /bin/bash
   grpcurl -insecure auth-service.platform.svc.cluster.local:5103 health.Health/Check
   ```

3. Si el Auth Service está caído y la caché ha expirado, consulta la [Sobrescritura de Emergencia del TTL de Caché](../../docs/runbooks/incident-response.md#option-b--extend-jwks-cache-ttl-emergency-override-only) en el runbook de respuesta a incidentes.

**Prevención:**

- Asegúrate de que el TTL de la caché JWKS (5 minutos) sea suficientemente largo para absorber cortes breves del Auth Service.
- Monitorea `users_auth_service_grpc_errors_total` para obtener una alerta temprana de problemas de conectividad antes de que la caché expire.
- Configura los ajustes adecuados de keepalive y timeout de gRPC en el `AuthServiceClient`:

```csharp
// Program.cs — configuración del canal gRPC
builder.Services.AddGrpcClient<AuthService.AuthServiceClient>(o =>
{
    o.Address = new Uri(configuration["Auth:GrpcEndpoint"]);
}).ConfigureChannel(o =>
{
    o.HttpHandler = new SocketsHttpHandler
    {
        KeepAlivePingDelay = TimeSpan.FromSeconds(5),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(2),
        ConnectTimeout = TimeSpan.FromSeconds(3)
    };
});
```

### 6.2 RBAC Denegado

**Estado HTTP:** `403 Forbidden`

**Síntoma:** El solicitante está autenticado pero no tiene el rol requerido para el endpoint.

**Diagnóstico:**

```bash
# Decodificar el JWT para ver la claim de roles
jwt decode <token> | jq '.payload.roles'

# Formato esperado: ["admin", "developer"]
```

**Verificar reglas RBAC para el endpoint:**

| Endpoint | Rol Requerido | Roles de tu Token | Resultado |
|---|---|---|---|
| `GET /api/users` | `admin` o `operator` | `["developer"]` | 403 |
| `POST /api/users` | `admin` | `["user"]` | 403 |
| `DELETE /api/users/{id}` | `admin` | `["operator"]` | 403 |
| `GET /api/users/{id}` (otro usuario) | `admin` o `operator` | `["user"]` | 403 |

**Resolución:** El solicitante necesita un token con el rol apropiado. Ya sea:
- Iniciar sesión como un usuario con el rol requerido.
- Un administrador debe asignar el rol faltante mediante `PUT /api/users/{id}` con `{"roles": ["admin"]}`.

**Mala Configuración Común -- La claim de roles es una cadena, no un arreglo:**

Si el Auth Service emite los roles como una cadena única en lugar de un arreglo, la verificación RBAC fallará:

```json
// Incorrecto — cadena, no arreglo
{ "roles": "admin" }

// Correcto — arreglo
{ "roles": ["admin"] }
```

Verifica el formato de la claim decodificando el JWT. Si el formato es incorrecto, corrige la emisión de claims en el `TokenService` del Auth Service.

### 6.3 Fallo de Conexión a la Base de Datos

**Estado HTTP:** `503 Service Unavailable` (la sonda de readiness falla)

**Síntomas:**
- `users_db_connection_errors_total` > 0
- Sonda de readiness (`/api/health/ready`) devolviendo `503`
- Registros: `NpgsqlException`, `connection failed`, `timeout`

**Diagnóstico:**

```bash
# 1. Verificar el pool de conexiones
curl -s http://localhost:7201/metrics | grep users_db_connection_pool

# 2. Verificar si la base de datos es accesible desde el pod
kubectl exec deploy/users-service -n platform -- \
  psql "$CONNECTION_STRING" -c "SELECT 1;"

# 3. Verificar la cadena de conexión (obtenida de Key Vault)
kubectl exec deploy/users-service -n platform -- env | grep ConnectionStrings__UsersDb
```

**Causas comunes y resoluciones:**

| Causa | Diagnóstico | Resolución |
|---|---|---|
| Pool de conexiones agotado | `users_db_connection_pool_size` al máximo (30) con > 0 errores | Matar consultas de larga duración: `SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE state != 'idle' AND query_start < NOW() - INTERVAL '5 minutes'` |
| Base de datos inaccesible | La conexión `psql` falla desde el pod | Verificar el estado de Azure PostgreSQL, considerar failover al servidor de respaldo |
| Cadena de conexión expirada | Credenciales rotadas recientemente en Key Vault | Los pods recogen nuevos secretos dentro del intervalo de sincronización. Forzar: `kubectl rollout restart deployment/users-service -n platform` |
| Política de red bloqueando salida | Otras llamadas salientes también fallan | Verificar `NetworkPolicy` y reglas de `azure-firewall` |
| Incompatibilidad de versión TLS | `NpgsqlException: SSL/TLS handshake failed` | Verificar que el servidor PostgreSQL permita TLS 1.3. El cliente Npgsql usa por defecto `SslMode.Require`. |

### 6.4 Limitación de Tasa

**Estado HTTP:** `429 Too Many Requests`

**Síntoma:** El cliente está enviando solicitudes más rápido que el límite configurado.

Nota: La limitación de tasa se aplica a nivel del Auth Service para endpoints de autenticación y en la API Gateway para el Users Service. El Users Service en sí mismo no implementa limitación de tasa.

**Resolución:**
- El cliente debe respetar la cabecera `Retry-After` y retroceder.
- Para operaciones masivas de emergencia (ej., sincronización de miles de usuarios), coordinar con Ingeniería de Plataforma para aumentar temporalmente el límite de tasa.

### 6.5 Usuario No Encontrado (404) vs. Prohibido (403)

El Users Service devuelve `404 Not Found` para usuarios inexistentes Y para usuarios que existen en un tenant diferente. Esto evita la enumeración de usuarios entre tenants.

**Diagnóstico:**

```bash
# Probar con token del Tenant A contra un usuario del Tenant B
curl -v -H "Authorization: Bearer $(token-para-tenant-a)" \
  https://users-service.platform/api/users/tenant-b-user-id

# Respuesta: 404 Not Found
# (El usuario existe pero es invisible para el Tenant A — comportamiento correcto)
```

**Si esperas que un usuario exista pero obtienes 404:**

1. Verifica que el usuario existe en el tenant correcto:
   ```sql
   SELECT id, tenant_id, username, deleted_at
   FROM users
   WHERE id = 'expected-uuid';
   ```

2. Verifica si el usuario fue eliminado lógicamente (`deleted_at IS NOT NULL`). Los usuarios eliminados lógicamente devuelven 404 a menos que el solicitante sea un administrador y use explícitamente el filtro `includeDeleted`.

3. Verifica que la claim `tid` del JWT coincida con el `tenant_id` del usuario. Ejecuta:

```bash
jwt decode <token> | jq '.payload.tid'
```

---

## 7. Configuración de Depuración en VS Code

### 7.1 Configuración de Inicio

Crea `.vscode/launch.json` en la raíz del repositorio:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Users Service (Development)",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/src/UsersService/bin/Debug/net10.0/Platform.UsersService.dll",
      "args": [],
      "cwd": "${workspaceFolder}/src/UsersService",
      "stopAtEntry": false,
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ASPNETCORE_URLS": "https://localhost:7201;http://localhost:7200",
        "Auth__Issuer": "https://localhost:7103",
        "Auth__Audience": "users-service-dev",
        "Auth__GrpcEndpoint": "https://localhost:5103",
        "ConnectionStrings__UsersDb": "Host=localhost;Port=5432;Database=users_dev;Username=users_svc;Password=dev_password"
      },
      "requireExactSource": false
    },
    {
      "name": "Users Service (Attach to Process)",
      "type": "coreclr",
      "request": "attach",
      "processName": "Platform.UsersService"
    },
    {
      "name": ".NET Core Attach (Remote)",
      "type": "coreclr",
      "request": "attach",
      "processId": "${command:pickRemoteProcess}",
      "pipeTransport": {
        "pipeCwd": "${workspaceFolder}",
        "pipeProgram": "kubectl",
        "pipeArgs": ["exec", "-n", "platform", "-i", "users-service-pod-name", "--"],
        "debuggerPath": "/vsdbg/vsdbg",
        "quoteArgs": true
      },
      "sourceFileMap": {
        "/app": "${workspaceFolder}"
      }
    }
  ]
}
```

### 7.2 Tarea de Compilación

Crea `.vscode/tasks.json`:

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "build",
      "command": "dotnet",
      "type": "process",
      "args": [
        "build",
        "${workspaceFolder}/src/UsersService/UsersService.csproj",
        "/property:GenerateFullPaths=true",
        "/consoleloggerparameters:NoSummary"
      ],
      "problemMatcher": "$msCompile"
    }
  ]
}
```

### 7.3 Depuración de Rutas de Código Clave

**Dónde colocar puntos de interrupción para escenarios comunes:**

| Qué Quieres Depurar | Archivo | Línea / Método |
|---|---|---|
| Punto de entrada de validación JWT | `AuthServiceClient.ValidateTokenAsync()` | Inicio del método |
| Recurso a la caché en validación JWT | `AuthServiceClient.ValidateTokenAsync()` | Lectura de caché JWKS |
| Aplicación de RBAC | Controller / middleware | Después de la extracción de claims, antes de la llamada a `IUserService` |
| Flujo de creación de usuario | `UserService.CreateUserAsync()` | Método completo |
| Validación de perfil | `ProfileValidator.ValidateAsync()` | Ejecución de reglas |
| Consulta a base de datos | `UserRepository.GetUsersAsync()` | Llamada a `QueryAsync` de Dapper |
| Consumo de eventos | `EventConsumer.ProcessEventAsync()` | Despacho de eventos |
| Llamada a cliente gRPC | Constructor de `AuthServiceClient` o llamada gRPC | Configuración de canal, ejecución de llamada |
| Procesamiento de mensajes de Service Bus | `EventConsumer.ConsumeMessageAsync()` | Deserialización de mensaje |

### 7.4 Depuración con Docker Compose

Si ejecutas tanto el Auth Service como el Users Service bajo Docker Compose, usa la siguiente configuración de `launch.json` para adjuntarte al contenedor en ejecución:

```json
{
  "name": "Attach to Docker (Users Service)",
  "type": "coreclr",
  "request": "attach",
  "processId": "1",
  "pipeTransport": {
    "pipeCwd": "${workspaceFolder}",
    "pipeProgram": "docker",
    "pipeArgs": ["exec", "-i", "users-service"],
    "debuggerPath": "/vsdbg/vsdbg",
    "quoteArgs": false
  },
  "sourceFileMap": {
    "/app": "${workspaceFolder}/src/UsersService"
  }
}
```

**Requisitos previos para depuración con Docker:**

1. Asegúrate de que la imagen Docker incluya `vsdbg` (el depurador de .NET). Agrega esto a tu `Dockerfile`:

```dockerfile
# Solo etapa de depuración
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS debug
RUN dotnet tool install --tool-path /tools dotnet-vsdbg
COPY --from=build /app /app
```

2. Ejecuta el contenedor con `--cap-add=SYS_PTRACE --security-opt seccomp=unconfined` para habilitar la depuración.

3. Adjunta VS Code al contenedor usando la configuración anterior.

### 7.5 Consejos de Depuración

**Hot Reload (desarrollo):** Usa `dotnet watch` para iteración rápida:

```bash
dotnet watch run --project src/UsersService
```

Esto reconstruye y reinicia automáticamente el servicio cuando guardas archivos fuente.

**Puntos de interrupción condicionales:** Al depurar el procesamiento de eventos para un usuario específico, establece un punto de interrupción condicional:

```
Condition: userId == Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890")
```

**Logpoints:** En lugar de agregar `Console.WriteLine` o `ILogger.LogDebug` durante la depuración, usa logpoints de VS Code (clic derecho en el margen -> "Add Logpoint"):

```
Processing event: {eventType} for user {userId}
```

Los logpoints no interrumpen -- imprimen en la consola de depuración sin detener la ejecución.

**Inspeccionar tráfico gRPC:** Usa la reflexión gRPC para inspeccionar la API gRPC del Auth Service:

```bash
grpcurl -plaintext localhost:5103 list
grpcurl -plaintext localhost:5103 describe auth.AuthService
```

### 7.6 Depuración del Auth Service en Paralelo

Dado que el Users Service tiene una dependencia estricta del Auth Service, a menudo necesitas depurar ambos. Ejecuta ambos servicios localmente:

```bash
# Terminal 1: Auth Service
cd ../authenthication-demo-backstage
dotnet run --project src/AuthService
# Escucha en https://localhost:7103, gRPC en https://localhost:5103

# Terminal 2: Users Service
cd ../users-demo-backstage
dotnet run --project src/UsersService
# Escucha en https://localhost:7201
```

O usa la configuración de inicio compuesta de VS Code:

```json
{
  "version": "0.2.0",
  "compounds": [
    {
      "name": "Both Services",
      "configurations": ["Auth Service", "Users Service (Development)"]
    }
  ],
  "configurations": [
    {
      "name": "Auth Service",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/../authenthication-demo-backstage/src/AuthService/bin/Debug/net10.0/Platform.AuthService.dll",
      "cwd": "${workspaceFolder}/../authenthication-demo-backstage/src/AuthService",
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ASPNETCORE_URLS": "https://localhost:7103;http://localhost:7102"
      }
    }
  ]
}
```

---

## 8. Referencia de Comandos de Diagnóstico

### 8.1 Salud del Servicio

```bash
# Sonda de liveness (proceso vivo?)
curl -s -o /dev/null -w "%{http_code}" https://users-service.platform/api/health/live

# Sonda de readiness (dependencias saludables?)
curl -s https://users-service.platform/api/health/ready | jq '.'

# Verificar el estado de cada dependencia
curl -s https://users-service.platform/api/health/ready | jq '.checks'
```

### 8.2 Kubernetes

```bash
# Estado de los pods
kubectl get pods -n platform -l app=users-service

# Registros del pod (últimas 100 líneas, en seguimiento)
kubectl logs -n platform -l app=users-service --tail=100 -f

# Registros del pod filtrados por ID de rastro
kubectl logs -n platform -l app=users-service | grep "abcdef1234567890"

# Registros del pod filtrados por tenant
kubectl logs -n platform -l app=users-service | grep '"tenant_id":"00000000-0000-0000-0000-000000000001"'

# Registros del pod filtrados por nivel de error
kubectl logs -n platform -l app=users-service | grep '"level":"Error"'

# Registros del proxy Istio (problemas mTLS)
kubectl logs -n platform -l app=users-service -c istio-proxy

# Ejecutar dentro del pod para diagnósticos de red
kubectl exec -n platform -it deploy/users-service -- /bin/bash

# Reiniciar pods (limpieza de caché, renovación de conexión)
kubectl rollout restart deployment/users-service -n platform

# Verificar variables de entorno
kubectl exec deploy/users-service -n platform -- env | sort
```

### 8.3 PostgreSQL

```sql
-- Verificar si el usuario existe
SELECT id, tenant_id, username, email, deleted_at
FROM users
WHERE id = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890';

-- Verificar conexiones activas
SELECT state, COUNT(*) as count
FROM pg_stat_activity
WHERE datname = 'usersdb'
GROUP BY state;

-- Verificar consultas de larga duración
SELECT pid, wait_event_type, state, 
       EXTRACT(EPOCH FROM (NOW() - query_start))::int AS seconds_running,
       LEFT(query, 150) AS query_short
FROM pg_stat_activity
WHERE state != 'idle'
  AND backend_type = 'client backend'
ORDER BY query_start;

-- Verificar tabla event_deduplication
SELECT COUNT(*), MIN(processed_at), MAX(processed_at)
FROM event_deduplication;

-- Verificar usuarios eliminados lógicamente
SELECT COUNT(*) as deleted_count
FROM users
WHERE deleted_at IS NOT NULL;

-- Verificar si RLS está habilitado
SELECT relname, relrowsecurity
FROM pg_class
WHERE relname IN ('users', 'event_deduplication', 'audit_logs');
```

### 8.4 Métricas de Prometheus

```bash
# Obtener todas las métricas (desde el pod o port-forward)
curl -s http://localhost:7201/metrics

# Errores de validación JWT
curl -s http://localhost:7201/metrics | grep users_jwt_validation_errors_total

# Tasa de solicitudes por código de estado
curl -s http://localhost:7201/metrics | grep users_requests_total

# Retraso en procesamiento de eventos
curl -s http://localhost:7201/metrics | grep users_event_processing_lag_seconds

# Latencia gRPC hacia Auth Service
curl -s http://localhost:7201/metrics | grep users_auth_validation_duration_seconds

# Estado del pool de conexiones
curl -s http://localhost:7201/metrics | grep users_db_connection
```

### 8.5 Azure Service Bus

```bash
# Verificar métricas de suscripción
az monitor metrics list \
  --resource /subscriptions/.../servicebus/.../topics/auth-events \
  --metric "ActiveMessages" "DeadLetterMessageCount" \
  --interval 5m

# Ver mensajes de la suscripción
az servicebus topic subscription message peek \
  --resource-group platform-rg \
  --namespace-name platform-sb \
  --topic-name auth-events \
  --subscription-name users-service \
  --count 5

# Ver cola de mensajes fallidos
az servicebus topic subscription message peek \
  --resource-group platform-rg \
  --namespace-name platform-sb \
  --topic-name auth-events \
  --subscription-name users-service/$DeadLetterQueueName \
  --count 5
```

### 8.6 Operaciones con Tokens

```bash
# Iniciar sesión (desarrollo local)
TOKEN=$(curl -s -X POST https://localhost:5103/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "Platform@2026!"}' | jq -r '.accessToken')

# Decodificar sin validación
jwt decode $TOKEN

# Decodificar con jq (independiente)
echo $TOKEN | cut -d'.' -f2 | base64 -d 2>/dev/null | jq '.'

# Probar contra Users Service
curl -s -H "Authorization: Bearer $TOKEN" \
  https://localhost:7201/api/users | jq '.'

# Verificar expiración del token
echo $TOKEN | cut -d'.' -f2 | base64 -d 2>/dev/null | jq -r '.exp' | xargs -I{} date -d @{}
```

---

## 9. Documentos Relacionados

- [Visión General de la Arquitectura](../architecture/overview.md) -- contexto de la plataforma
- [Arquitectura de Seguridad](../architecture/security.md) -- flujo de validación JWT y modelo de amenazas
- [Runbook de Respuesta a Incidentes](../runbooks/incident-response.md) -- playbooks de respuesta SEV-1/SEV-2
- [Vista de Despliegue](../architecture/deployment-view.md) -- topología, sondas de salud, rutas de degradación
- [API de Eventos](../api/events.md) -- esquemas de eventos, garantías de procesamiento, monitoreo
- [API de Usuarios](../api/users-api.md) -- referencia de endpoints, respuestas de error
- [Guía de Desarrollo Local](local-development.md) -- configuración del entorno de desarrollo
- [Guía de Pruebas](testing.md) -- ejecución de pruebas, patrones de prueba
