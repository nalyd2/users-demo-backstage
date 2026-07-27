# Guía del Desarrollador -- Users Service

Esta guía es el punto de entrada para los ingenieros que trabajan en el **Users Service** (`users-service`). Cubre la arquitectura, la organización del código, instrucciones paso a paso para agregar endpoints, el modelo RBAC, el patrón de consumidor de eventos y los problemas comunes que enfrentan los nuevos miembros del equipo.

---

## Tabla de Contenidos

- [Recorrido Arquitectónico](#recorrido-arquitectónico)
    - [Dependencia del Authentication Service](#dependencia-del-authentication-service)
    - [Distribución Hexagonal (Puertos y Adaptadores)](#distribución-hexagonal-puertos-y-adaptadores)
    - [Ciclo de Vida de una Solicitud](#ciclo-de-vida-de-una-solicitud)
- [Organización del Código](#organización-del-código)
    - [Estructura del Código Fuente](#estructura-del-código-fuente)
    - [Convención de Namespaces](#convención-de-namespaces)
    - [Referencia Rápida de Archivos Clave](#referencia-rápida-de-archivos-clave)
- [Cómo Agregar un Nuevo Endpoint](#cómo-agregar-un-nuevo-endpoint)
    - [Paso 1: Definir DTOs de Solicitud/Respuesta](#paso-1-definir-dtos-de-solicitudrespuesta)
    - [Paso 2: Agregar la Ruta](#paso-2-agregar-la-ruta)
    - [Paso 3: Conectar el Servicio de Aplicación](#paso-3-conectar-el-servicio-de-aplicación)
    - [Paso 4: Agregar Validación](#paso-4-agregar-validación)
    - [Paso 5: Registrar Dependencias](#paso-5-registrar-dependencias)
- [Aplicación de RBAC](#aplicación-de-rbac)
    - [Autorización por Política de Roles](#autorización-por-política-de-roles)
    - [Verificaciones a Nivel de Endpoint](#verificaciones-a-nivel-de-endpoint)
    - [Permisos a Nivel de Campo en Actualizaciones](#permisos-a-nivel-de-campo-en-actualizaciones)
- [Patrón de Consumidor de Eventos](#patrón-de-consumidor-de-eventos)
    - [Eventos Consumidos (desde Auth Service)](#eventos-consumidos-desde-auth-service)
    - [Eventos Publicados (hacia la Plataforma)](#eventos-publicados-hacia-la-plataforma)
    - [Escribir un Nuevo Consumidor](#escribir-un-nuevo-consumidor)
- [Problemas Comunes](#problemas-comunes)
- [Documentos Relacionados](#documentos-relacionados)

---

## Recorrido Arquitectónico

### Dependencia del Authentication Service

El Users Service tiene una **dependencia estricta en tiempo de ejecución** del [Authentication Service](https://backstage.internal/platform/component/auth-service) (`auth-service`). No emite, firma ni gestiona tokens -- es únicamente un **servicio consumidor de JWT**.

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Users Service                                │
│                                                                     │
│  ┌──────────────┐    gRPC / mTLS     ┌──────────────────────────┐   │
│  │  Controller   │ ──────────────────▶│  AuthServiceClient       │   │
│  │  (Minimal API)│                    │  (IAuthServiceClient)    │   │
│  └──────┬───────┘                    └───────────┬──────────────┘   │
│         │                                        │                  │
│         │ JWT en cabecera Authorization            │ ValidateToken() │
│         ▼                                        ▼                  │
│  ┌──────────────┐                    ┌──────────────────────────┐   │
│  │  UserService  │                    │  JWKS Cache (TTL 5 min)  │   │
│  │  (Capa App)   │                    └──────────────────────────┘   │
│  └──────┬───────┘                                                    │
│         │                                                            │
│         ▼                                                            │
│  ┌──────────────┐                                                    │
│  │  PostgreSQL   │                                                    │
│  └──────────────┘                                                    │
└─────────────────────────────────────────────────────────────────────┘
           │
           │ HTTP gRPC
           ▼
┌──────────────────────────────┐
│   Authentication Service     │
│                              │
│  ┌──────────────────────┐   │
│  │  TokenService         │   │
│  │  - Emisión (RS256)    │   │
│  │  - Validación         │   │
│  │  - Rotación           │   │
│  └──────────────────────┘   │
│                              │
│  ┌──────────────────────┐   │
│  │  AuthService          │   │
│  │  - Verificación cred.  │   │
│  │  - Flujo de refresh    │   │
│  │  - Publicación eventos │   │
│  └──────────────────────┘   │
└──────────────────────────────┘
```

**Lo que esto significa para ti como desarrollador:**

1. **Cada solicitud autenticada lleva un JWT emitido por `auth-service`.** El JWT contiene claims (`sub`, `roles`, `tid`, `jti`) que el Users Service extrae y en los que confía para la autorización.
2. **La validación del token se intenta primero contra `auth-service` vía gRPC.** Si la llamada falla, el servicio recurre a una **caché JWKS local** con un TTL de 5 minutos. Después de que la caché expire y el Auth Service siga inaccesible, el servicio devuelve `503 Service Unavailable` para todos los endpoints autenticados.
3. **No existe un endpoint de "inicio de sesión" en este servicio.** La autenticación es manejada completamente por `auth-service`. El Users Service solo gestiona los *perfiles* de usuario (nombre, correo electrónico, departamento, roles).
4. **El servicio se suscribe a `auth-events`** en Azure Service Bus para actualizar el estado de actividad del usuario (último inicio de sesión, último cierre de sesión) sin realizar sondeos.

**Modos de falla que debes comprender:**

| Escenario | Efecto | Mitigación |
|---|---|---|
| Auth Service caído < 5 min | La caché JWKS local sirve la validación | Sin impacto visible |
| Auth Service caído > 5 min | La validación del token falla; el servicio devuelve 503 | Alerta PagerDuty; failover de SRE |
| Timeout de llamada gRPC (500 ms) | Recurso a la caché JWKS | Configurado en `Auth__GrpcTimeoutMs` |
| Circuit breaker abierto (30 s) | Todas las validaciones usan la caché local | Sonda de semi-apertura automática restaura gRPC |

### Distribución Hexagonal (Puertos y Adaptadores)

El código sigue el patrón de **Arquitectura Hexagonal**:

```
src/UsersService/
├── Models/           DTOs de dominio y registros de solicitud/respuesta
├── Services/         Servicios de aplicación (puertos)
│   └── IUserService  Interfaz de puerto
├── Repositories/     Acceso a datos (adaptadores, salientes)
├── Endpoints/        Definiciones de rutas Minimal API (adaptadores, entrantes)
├── Middleware/       Middleware de validación JWT y autorización
├── EventHandlers/    Manejadores de mensajes de Service Bus
├── Configuration/    Clases de opciones y enlace
└── Program.cs        Raíz de composición
```

| Capa | Carpeta | Rol |
|---|---|---|
| **Dominio** | `Models/` | Registros de datos puros -- sin comportamiento. `UserDto`, `UserEntity`, `CreateUserRequest`, `UpdateUserRequest`. |
| **Aplicación (Puerto)** | `Services/` | Interfaces como `IUserService` definen lo que hace el servicio. Las implementaciones en la misma carpeta orquestan el flujo de trabajo. |
| **Infraestructura (Adaptador)** | `Repositories/`, `Middleware/`, `EventHandlers/` | Implementaciones concretas de los puertos. `UserRepository` se comunica con PostgreSQL. `AuthServiceClient` llama a gRPC. |
| **Adaptador de Entrada** | `Endpoints/` | Grupos de rutas Minimal API de ASP.NET Core que traducen HTTP a llamadas de la capa de aplicación. |

### Ciclo de Vida de una Solicitud

Una solicitud autenticada típica fluye a través de estas capas:

```
HTTP Request
    │
    ▼
┌─────────────────────────────┐
│ 1. Autenticación JWT        │  Middleware valida JWT (gRPC o caché JWKS)
│    Middleware                │  Extrae ClaimsPrincipal (sub, roles, tid)
└──────────┬──────────────────┘
           ▼
┌─────────────────────────────┐
│ 2. Endpoint (Manejador Ruta)│  Método Minimal API (estático, agrupado)
│                              │  - Deserializa el cuerpo de la solicitud
│                              │  - Llama a IUserService
│                              │  - Mapea el resultado a respuesta HTTP
└──────────┬──────────────────┘
           ▼
┌─────────────────────────────┐
│ 3. Servicio de Aplicación    │  Implementación de IUserService
│                              │  - Llama a ProfileValidator
│                              │  - Llama a IUserRepository
│                              │  - Llama a IEventPublisher
│                              │  - Llama a INotificationClient
└──────────┬──────────────────┘
           ▼
┌─────────────────────────────┐
│ 4. Repositorio / Adaptador   │  Dapper + Npgsql a PostgreSQL
│                              │  - Consultas parametrizadas
│                              │  - Alcance por tenant_id
│                              │  - Cláusula WHERE de borrado lógico
└─────────────────────────────┘
```

---

## Organización del Código

### Estructura del Código Fuente

```
src/UsersService/
├── Program.cs                       # Raíz de composición, pipeline de middleware, registro DI
├── UsersService.csproj              # Proyecto .NET 10 con paquetes NuGet esenciales
├── appsettings.json                 # Configuración de producción
├── appsettings.Development.json     # Sobrescrituras locales
│
├── Models/
│   ├── User.cs                      # UserDto (respuesta API), UserEntity (entidad BD), ToDto()
│   ├── CreateUserRequest.cs         # DTO de solicitud POST
│   └── UpdateUserRequest.cs         # DTO de solicitud PUT (todos los campos opcionales)
│
├── Services/
│   ├── IUserService.cs              # Interfaz de puerto + UserResult<T>, PaginatedList<T>
│   └── UserService.cs               # Implementación del servicio de aplicación
│
├── Middleware/
│   ├── JwtValidationMiddleware.cs   # Valida JWT, popula HttpContext.Items
│   ├── TenantContextMiddleware.cs   # Extrae tid, enriquece TenantContext con ámbito
│   └── RequestLoggingMiddleware.cs  # ID de correlación, enriquecimiento de logging estructurado
│
├── Endpoints/
│   ├── UserEndpoints.cs             # Grupo de rutas para /api/users
│   └── HealthEndpoints.cs           # Grupo de rutas para /api/health
│
├── Repositories/
│   ├── IUserRepository.cs           # Puerto de acceso a datos
│   └── UserRepository.cs            # Implementación con Dapper
│
├── EventHandlers/
│   ├── AuthEventConsumer.cs         # BackgroundService que procesa auth-events
│   ├── IEventPublisher.cs           # Puerto para publicar eventos de usuario
│   └── EventPublisher.cs            # Publicador de Service Bus
│
├── Configuration/
│   ├── AuthOptions.cs               # Configuración fuertemente tipada de Auth
│   ├── UsersOptions.cs              # Configuración fuertemente tipada de Users
│   └── ServiceBusOptions.cs         # Configuración fuertemente tipada de Service Bus
│
└── Observability/
    ├── MetricsRegistry.cs           # Contadores, histogramas y medidores de Prometheus
    ├── ActivitySources.cs           # Fuentes de actividad de OpenTelemetry
    └── LogEnrichers.cs              # Enriquecedores de Serilog (tenant, ID de correlación)
```

### Convención de Namespaces

Todo el código reside bajo `Platform.UsersService.*`. El namespace se mapea directamente a la carpeta:

| Carpeta | Namespace |
|---|---|
| `Models/` | `Platform.UsersService.Models` |
| `Services/` | `Platform.UsersService.Services` |
| `Endpoints/` | `Platform.UsersService.Endpoints` |
| `Repositories/` | `Platform.UsersService.Repositories` |
| `EventHandlers/` | `Platform.UsersService.EventHandlers` |

Las clases de implementación internas se marcan como `internal sealed` -- los consumidores dependen de interfaces, nunca de implementaciones concretas.

### Referencia Rápida de Archivos Clave

| Archivo | Qué contiene | Por qué lo tocarás |
|---|---|---|
| `Program.cs` | Registro de servicios, pipeline de middleware, montaje de endpoints | Agregar una nueva dependencia o middleware |
| `Models/CreateUserRequest.cs` | Registro `CreateUserRequest` | Extender el esquema de creación de usuarios |
| `Models/UpdateUserRequest.cs` | Registro `UpdateUserRequest` (todo opcional) | Agregar campos editables |
| `Models/User.cs` | Registros `UserDto` y `UserEntity` | Cambiar la forma de la respuesta API o columnas de BD |
| `Services/IUserService.cs` | Interfaz de puerto, `UserResult<T>`, `PaginatedList<T>` | Agregar una nueva operación |
| `Services/UserService.cs` | Orquestador de lógica de aplicación | Implementar nuevas reglas de negocio |
| `Middleware/JwtValidationMiddleware.cs` | Validación JWT con respaldo gRPC + JWKS | Cambiar el comportamiento de autenticación |
| `Endpoints/UserEndpoints.cs` | Grupo de rutas para `/api/users` | Agregar un nuevo endpoint a la API de usuarios |
| `Endpoints/HealthEndpoints.cs` | Sondeos de liveness + readiness | Agregar una verificación de dependencia |
| `Repositories/UserRepository.cs` | SQL con Dapper para todas las consultas de usuarios | Escribir nuevas consultas o cambiar el esquema |
| `EventHandlers/AuthEventConsumer.cs` | Procesador de suscripción a Service Bus | Agregar un nuevo evento consumido |
| `EventHandlers/EventPublisher.cs` | Publicación de eventos del ciclo de vida del usuario | Agregar un nuevo evento publicado |
| `appsettings.json` | Valores predeterminados independientes del entorno | Cambiar TTL de caché, tamaños de página |
| `appsettings.Development.json` | Sobrescrituras para desarrollo local | Configurar PostgreSQL local |

---

## Cómo Agregar un Nuevo Endpoint

Este tutorial agrega un endpoint `PATCH /api/users/{userId}/status` que permite a un administrador activar o desactivar una cuenta de usuario. El patrón aplica a cualquier operación nueva.

### Paso 1: Definir DTOs de Solicitud/Respuesta

Archivo: `src/UsersService/Models/UpdateUserStatusRequest.cs`

```csharp
namespace Platform.UsersService.Models;

/// <summary>
/// Solicitud para cambiar el estado de la cuenta de un usuario.
/// </summary>
public sealed record UpdateUserStatusRequest
{
    /// <summary>Nuevo estado de la cuenta. Valores válidos: "active", "inactive".</summary>
    public required string Status { get; init; }
}
```

Agrega el campo de estado a `UserDto` y `UserEntity` en `Models/User.cs`:

```csharp
// En UserDto
public string Status { get; init; } = "active";

// En UserEntity
public string Status { get; init; } = "active";
```

Actualiza `ToDto()` en `UserEntity` para que incluya el campo de estado.

### Paso 2: Agregar la Ruta

Archivo: `src/UsersService/Endpoints/UserEndpoints.cs`

Agrega un método estático dentro de la clase existente `UserEndpoints` y regístralo en el grupo de rutas:

```csharp
public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users")
            .WithOpenApi()
            .RequireAuthorization();

        // Rutas existentes...
        group.MapGet("/", GetUsersAsync);
        group.MapGet("/{userId:guid}", GetUserByIdAsync);
        group.MapPost("/", CreateUserAsync);
        group.MapPut("/{userId:guid}", UpdateUserAsync);
        group.MapDelete("/{userId:guid}", DeleteUserAsync);

        // NUEVA RUTA
        group.MapPatch("/{userId:guid}/status", UpdateUserStatusAsync)
            .WithName("UpdateUserStatus")
            .WithDescription("Activa o desactiva una cuenta de usuario. Solo administradores.")
            .Produces<UserDto>(200)
            .Produces<ProblemDetails>(400)
            .Produces<ProblemDetails>(403)
            .Produces<ProblemDetails>(404);

        return group;
    }

    private static async Task<IResult> UpdateUserStatusAsync(
        Guid userId,
        [FromBody] UpdateUserStatusRequest request,
        IUserService userService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        // La aplicación de RBAC ocurre dentro de UserService vía el claims principal
        var result = await userService.UpdateUserStatusAsync(
            userId,
            request.Status,
            httpContext.User,
            ct);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.Problem(
                type: $"https://errors.internal.platform/{(result.StatusCode == 404 ? "not-found" : "validation-error")}",
                title: result.StatusCode switch { 404 => "Not Found", 403 => "Forbidden", _ => "Bad Request" },
                statusCode: result.StatusCode,
                detail: result.ErrorMessage);
    }
}
```

Monta el grupo de endpoints en `Program.cs` -- si el grupo ya está siendo llamado (ej., `app.MapUserEndpoints()`), la nueva ruta se incluye automáticamente.

### Paso 3: Conectar el Servicio de Aplicación

Agrega el método a `IUserService`:

```csharp
Task<UserResult<UserDto>> UpdateUserStatusAsync(
    Guid userId, string newStatus, ClaimsPrincipal principal, CancellationToken ct);
```

Impleméntalo en `UserService`:

```csharp
public async Task<UserResult<UserDto>> UpdateUserStatusAsync(
    Guid userId, string newStatus, ClaimsPrincipal principal, CancellationToken ct)
{
    // 1. RBAC -- solo admin puede cambiar el estado
    var roles = principal.FindAll("roles").Select(c => c.Value).ToArray();
    if (!roles.Contains("admin"))
    {
        return UserResult<UserDto>.Failure("Se requiere rol de administrador para cambiar el estado de la cuenta.", 403);
    }

    // 2. Validar el valor del estado
    if (newStatus != "active" && newStatus != "inactive")
    {
        return UserResult<UserDto>.Failure("El estado debe ser 'active' o 'inactive'.", 400);
    }

    // 3. Extraer ID del tenant del JWT (nunca de la entrada del usuario)
    var tenantId = Guid.Parse(principal.FindFirstValue("tid")!);
    var actorId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    // 4. Cargar el usuario (incluye el alcance del tenant)
    var existing = await _userRepository.GetUserByIdAsync(userId, tenantId, ct);
    if (existing is null)
    {
        return UserResult<UserDto>.Failure("Usuario no encontrado.", 404);
    }

    // 5. Aplicar la actualización
    var updated = existing with { Status = newStatus, UpdatedAt = DateTimeOffset.UtcNow };
    await _userRepository.UpdateUserAsync(updated, ct);

    // 6. Registro de auditoría
    await _userRepository.InsertAuditLogAsync(new AuditLogEntry
    {
        UserId = userId, Action = "status_changed",
        Changes = JsonSerializer.SerializeToElement(new { from = existing.Status, to = newStatus }),
        ActorId = actorId, PerformedAt = DateTimeOffset.UtcNow
    }, ct);

    // 7. Publicar evento
    await _eventPublisher.PublishAsync(new UserStatusChanged(userId, newStatus, tenantId, actorId), ct);

    _logger.LogInformation("El estado del usuario {UserId} cambió a {NewStatus} por {ActorId}", userId, newStatus, actorId);

    return UserResult<UserDto>.Success(updated.ToDto());
}
```

### Paso 4: Agregar Validación

El endpoint anterior valida que `Status` sea un valor permitido. Para validaciones más complejas, registra un validador de `FluentValidation`:

```csharp
public class UpdateUserStatusValidator : AbstractValidator<UpdateUserStatusRequest>
{
    public UpdateUserStatusValidator()
    {
        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => s is "active" or "inactive")
            .WithMessage("El estado debe ser 'active' o 'inactive'.");
    }
}
```

Regístralo en `Program.cs`:

```csharp
builder.Services.AddValidatorsFromAssemblyContaining<UpdateUserStatusValidator>();
```

### Paso 5: Registrar Dependencias

Si tu endpoint necesita una nueva dependencia externa (ej., un cliente gRPC para otro servicio), regístrala en `Program.cs`:

```csharp
// Ejemplo: nuevo cliente gRPC
builder.Services.AddGrpcClient<SomeService.SomeServiceClient>(o =>
{
    o.Address = new Uri(builder.Configuration["SomeService:Endpoint"]!);
});
```

---

## Aplicación de RBAC

El Users Service utiliza un modelo de autorización de **dos capas**:

### Autorización por Política de Roles

Las [políticas de autorización](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies) de ASP.NET Core mapean los roles del JWT al acceso a endpoints. Registro de políticas en `Program.cs`:

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy =>
        policy.RequireRole("admin"))
    .AddPolicy("AdminOrOperator", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.IsInRole("admin") || ctx.User.IsInRole("operator")));
```

Aplica políticas a grupos de rutas:

```csharp
group.MapPost("/", CreateUserAsync).RequireAuthorization("AdminOnly");
group.MapGet("/", GetUsersAsync).RequireAuthorization("AdminOrOperator");
```

### Verificaciones a Nivel de Endpoint

Para reglas más detalladas (ej., "el usuario puede leerse a sí mismo"), la capa de aplicación inspecciona directamente el `ClaimsPrincipal`:

```csharp
public async Task<UserResult<UserDto>> GetUserByIdAsync(
    Guid userId, ClaimsPrincipal principal, CancellationToken ct)
{
    var requestorId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
    var roles = principal.FindAll("roles").Select(c => c.Value).ToArray();
    var isAdmin = roles.Contains("admin");
    var isOperator = roles.Contains("operator");
    var isSelf = userId == requestorId;

    if (!isAdmin && !isOperator && !isSelf)
    {
        return UserResult<UserDto>.Failure("Acceso denegado.", 403);
    }

    // ... continuar con consulta con alcance de tenant
}
```

### Permisos a Nivel de Campo en Actualizaciones

El endpoint `PUT /api/users/{id}` tiene reglas a nivel de campo dependiendo del rol del solicitante. Estas se aplican dentro del servicio de aplicación, no a nivel de endpoint:

```csharp
public async Task<UserResult<UserDto>> UpdateUserAsync(
    Guid userId, UpdateUserRequest request, ClaimsPrincipal principal, CancellationToken ct)
{
    var isAdmin = principal.IsInRole("admin");
    var isOperator = principal.IsInRole("operator");
    var isSelf = userId == GetUserId(principal);

    if (!isAdmin && !isOperator && !isSelf)
        return UserResult<UserDto>.Failure("Acceso denegado.", 403);

    var existing = await _userRepository.GetUserByIdAsync(userId, tenantId, ct);
    if (existing is null)
        return UserResult<UserDto>.Failure("Usuario no encontrado.", 404);

    // Construir actualización de forma selectiva según el rol
    var updated = existing with
    {
        Email = request.Email ?? existing.Email,
        DisplayName = request.DisplayName ?? existing.DisplayName,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    // Solo administrador puede cambiar roles
    if (request.Roles is not null)
    {
        if (!isAdmin)
            return UserResult<UserDto>.Failure("Solo los administradores pueden cambiar roles.", 403);
        updated = updated with { Roles = request.Roles };
    }

    // El operador y el propio usuario no pueden cambiar departamento ni título
    if (!isAdmin && request.Department is not null)
        return UserResult<UserDto>.Failure("Solo los administradores pueden cambiar el departamento.", 403);
    if (!isAdmin && request.JobTitle is not null)
        return UserResult<UserDto>.Failure("Solo los administradores pueden cambiar el título del puesto.", 403);

    // Aplicar campos solo de administrador
    if (isAdmin)
    {
        updated = updated with
        {
            Department = request.Department ?? existing.Department,
            JobTitle = request.JobTitle ?? existing.JobTitle
        };
    }

    await _userRepository.UpdateUserAsync(updated, ct);
    // ...
}
```

**Matriz RBAC (reproducida de [Vista de Componentes](architecture/components.md)):**

| Acción | `admin` | `operator` | `user` |
|---|---|---|---|
| `GET /api/users` | Todos | Todos | -- |
| `GET /api/users/{id}` | Cualquiera | Cualquiera | Solo propio |
| `POST /api/users` | Crear | -- | -- |
| `PUT /api/users/{id}` | Cualquiera (todos los campos) | Campos limitados | Propio (limitado) |
| `DELETE /api/users/{id}` | Eliminar | -- | -- |

---

## Patrón de Consumidor de Eventos

El servicio ejecuta un `BackgroundService` que se suscribe al tópico `auth-events` de Azure Service Bus. Así es como el Users Service se mantiene informado sobre la actividad de autenticación sin consultar al Auth Service.

### Eventos Consumidos (desde Auth Service)

| Evento | Acción | Idempotencia |
|---|---|---|
| `user.login` | `UPDATE users SET last_login_at = @timestamp WHERE id = @userId` | Dedup por `eventId` |
| `user.logout` | `UPDATE users SET last_logout_at = @timestamp WHERE id = @userId` | Dedup por `eventId` |
| `token.revoked` | `INSERT INTO token_revocations (user_id, event_id, revoked_at)` | Dedup por `eventId` |

### Eventos Publicados (hacia la Plataforma)

| Evento | Disparador | Carga útil |
|---|---|---|
| `user.created` | Éxito de `POST /api/users` | `{ userId, username, email, tenantId, actorId }` |
| `user.updated` | Éxito de `PUT /api/users/{id}` | `{ userId, changedFields[], actorId }` |
| `user.deleted` | Éxito de `DELETE /api/users/{id}` | `{ userId, actorId }` |

### Escribir un Nuevo Consumidor

Para consumir un nuevo tipo de evento del tópico `auth-events`:

**1. Agrega el manejador en `AuthEventConsumer.cs`:**

```csharp
private async Task HandleUserLoginAsync(ProcessMessageEventArgs args, CancellationToken ct)
{
    using var activity = _activitySource.StartActivity("AuthEventConsumer.HandleUserLogin");
    var body = Encoding.UTF8.GetString(args.Message.Body);

    var envelope = JsonSerializer.Deserialize<EventEnvelope<LoginEventData>>(body);
    if (envelope is null) { await args.DeadLetterMessageAsync(args.Message); return; }

    // Deduplicación
    if (await _eventDeduplication.IsProcessedAsync(envelope.EventId, ct))
    {
        await args.CompleteMessageAsync(args.Message);
        return;
    }

    await _userRepository.UpdateLastLoginAsync(envelope.Data.UserId, envelope.Data.Timestamp, ct);
    await _eventDeduplication.MarkProcessedAsync(envelope.EventId, ct);

    _metrics.EventProcessed("user.login", "success");
    _logger.LogInformation("Procesado user.login para {UserId}", envelope.Data.UserId);

    await args.CompleteMessageAsync(args.Message);
}
```

**2. Registra el manejador en el mapa de despacho de mensajes (dentro de `AuthEventConsumer`):**

```csharp
private static readonly Dictionary<string, Func<ProcessMessageEventArgs, CancellationToken, Task>> Handlers = new()
{
    ["user.login"] = (ctx, ct) => new AuthEventConsumer(/*...*/).HandleUserLoginAsync(ctx, ct),
    ["user.logout"] = /* ... */,
    ["token.revoked"] = /* ... */,
};
```

**3. Agrega una verificación de deduplicación** -- la tabla `event_deduplication` evita el doble procesamiento cuando Service Bus entrega el mismo mensaje más de una vez (garantía de al menos una vez).

**Garantías de procesamiento:**

| Garantía | Mecanismo |
|---|---|
| Al menos una vez | Service Bus PeekLock + renovación automática (máx. 5 min) |
| En orden por usuario | Tópico habilitado para sesiones (ID de sesión = `userId`) |
| Idempotencia | Tabla `event_deduplication(event_id PK)` |
| Mensajes fallidos | Después de 10 fallos de entrega |

---

## Problemas Comunes

### 1. Olvidar el Alcance del ID de Tenant

Cada consulta **debe** incluir `tenant_id`. El ID del tenant proviene del JWT (claim `tid`), nunca de la entrada del usuario. Violación = FUGA DE DATOS ENTRE TENANTS (severidad: crítica).

```csharp
// INCORRECTO -- un atacante puede pasar cualquier tenant
SELECT * FROM users WHERE id = @userId;

// CORRECTO
SELECT * FROM users WHERE id = @userId AND tenant_id = @tenantId;
```

### 2. Exponer Tipos de Entidad Internos a la API

`UserEntity` contiene campos internos de la base de datos (`DeletedAt`, `TenantId`) que nunca deben serializarse en las respuestas de la API. Siempre mapea a `UserDto` mediante `ToDto()`.

```csharp
// INCORRECTO -- expone DeletedAt a los consumidores de la API
return Results.Ok(userEntity);

// CORRECTO
return Results.Ok(userEntity.ToDto());
```

### 3. Omitir la Idempotencia en los Manejadores de Eventos

Service Bus garantiza la entrega al menos una vez. Sin deduplicación, el mismo evento `user.login` podría actualizar `last_login_at` dos veces con el mismo valor (inofensivo pero derrochador) -- o peor, procesar un evento `user.deleted` dos veces y fallar en el segundo intento. Siempre verifica primero la tabla `event_deduplication`.

### 4. Usar el Método HTTP Incorrecto para Actualizaciones Parciales

Usa `PATCH` para actualizaciones parciales, no `POST` ni `PUT` sobrecargado. El `PUT /api/users/{id}` existente ya soporta actualizaciones parciales mediante campos opcionales, pero los nuevos endpoints que modifiquen un subconjunto de campos deben preferir `PATCH`.

### 5. No Propagar el ID de Correlación

Cada solicitud lleva una cabecera `trace-id` desde la API Gateway. Si realizas llamadas salientes (gRPC al Auth Service, HTTP a Graph API), propaga este ID para que el rastro distribuido esté completo:

```csharp
using var activity = _activitySource.StartActivity("UserService.CreateUser");
activity?.SetTag("user.id", userId.ToString());
activity?.SetTag("tenant.id", tenantId.ToString());
```

### 6. Omitir la Capa de Aplicación

Los endpoints deben llamar a `IUserService` -- nunca deben llamar a `IUserRepository` directamente. La capa de aplicación es donde ocurren RBAC, validación, registro de auditoría y publicación de eventos. Omitirla significa omitir los controles de seguridad.

```csharp
// INCORRECTO -- omite RBAC y auditoría
group.MapPost("/", async (Guid id, IUserRepository repo) => { ... });

// CORRECTO
group.MapPost("/", async (Guid id, IUserService svc) => { ... });
```

### 7. Usar Verificaciones `is null` en Campos Opcionales de `UpdateUserRequest`

`UpdateUserRequest` usa campos anulables para distinguir "no proporcionado" de "establecido como nulo". Un campo `string?` que es `null` significa "no actualices este campo". Esto se rompe si el cliente quiere explícitamente limpiar un valor:

```csharp
// Esto no puede distinguir "no cambiar departamento" de "limpiar departamento".
// Solución: usar una unión discriminada o una lista separada de campos a limpiar.
```

Convención actual: si un campo es `null` en `UpdateUserRequest`, no se actualiza. La mayoría de los campos no soportan la limpieza de valores. Si agregas un campo que se pueda limpiar, agrega un booleano separado o usa `JsonIgnoreCondition.WhenWritingNull` en el cliente.

### 8. Valores de Configuración Hardcodeados

Nunca hardcodees cadenas de conexión, URLs de endpoints o timeouts. Cada valor específico del entorno pertenece a `appsettings.json`, `appsettings.{Environment}.json` o Azure Key Vault.

```csharp
// INCORRECTO
var timeout = TimeSpan.FromMilliseconds(500);

// CORRECTO
var timeoutMs = configuration.GetValue<int>("Auth:GrpcTimeoutMs", 500);
var timeout = TimeSpan.FromMilliseconds(timeoutMs);
```

### 9. Ignorar el Circuit Breaker en Llamadas gRPC al Auth Service

El `AuthServiceClient` tiene un circuit breaker configurado con 5 fallos consecutivos y una duración de apertura de 30 segundos. Si omites este cliente y llamas al Auth Service directamente, pierdes la protección del circuito y podrías causar fallos en cascada bajo carga.

### 10. Agregar Endpoints Que Requieran Autenticación pero Olvidar `RequireAuthorization()`

Los nuevos manejadores de ruta agregados a un grupo que ya tiene `.RequireAuthorization()` heredan el requisito. Sin embargo, si creas un nuevo grupo de rutas (ej., una nueva consola de administración), recuerda llamar a `.RequireAuthorization()` en él, o tu endpoint será accesible públicamente.

---

## Documentos Relacionados

- [Visión General de la Arquitectura](architecture/overview.md) -- diseño y principios del sistema
- [Contexto del Sistema](architecture/context.md) -- interacciones con sistemas externos
- [Vista de Componentes](architecture/components.md) -- estructura interna de componentes
- [Arquitectura de Seguridad](architecture/security.md) -- validación JWT y modelo RBAC
- [Vista de Contenedores](architecture/containers.md) -- contenedores en tiempo de ejecución y almacenes de datos
- [API de Usuarios](api/users-api.md) -- referencia completa de endpoints
- [Eventos](api/events.md) -- esquemas de eventos consumidos y publicados
- [Variables y Configuración](api/variables.md) -- variables de entorno y flags de funcionalidad
- [Desarrollo Local](onboarding/local-development.md) -- configuración del entorno de desarrollo
- [Cómo Depurar](onboarding/how-to-debug.md) -- técnicas de depuración
- [Pruebas](onboarding/testing.md) -- estrategia de pruebas
- [Estándares de Codificación](decisions/coding-standards.md) -- convenciones de código
- [ADR-002 -- Validación JWT a Nivel de Puerta de Enlace vs. Servicio](adr/ADR-002.md)
- [ADR-003 -- Sincronización del Estado del Usuario Impulsada por Eventos](adr/ADR-003.md)
