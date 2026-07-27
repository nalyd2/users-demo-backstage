# Estándares de Codificación — Users Service

## Alcance

Aplica a todo el código fuente C# dentro de la solución `UsersService` orientado a .NET 10 y C# 13. Estos estándares reflejan los estándares de codificación del Auth Service con adaptaciones para el dominio de usuarios, incluyendo gestión de entidades, multi-tenencia y patrones de soft-delete.

## Convenciones de Nomenclatura

| Categoría | Convención | Ejemplo |
|---|---|---|
| Clases, Records, Structs | PascalCase | `UserService`, `UserProfile`, `TenantContext` |
| Interfaces | IPascalCase | `IUserRepository`, `ITenantProvider` |
| Métodos, Propiedades, Eventos | PascalCase | `CreateUserAsync()`, `IsSoftDeleted` |
| Campos privados | `_camelCase` | `_userRepository`, `_logger` |
| Campos estáticos privados | `s_camelCase` | `s_defaultPageSize` |
| Variables locales, parámetros | camelCase | `userProfile`, `createdUser` |
| Constantes | PascalCase | `DefaultPageSize`, `MaxBatchSize` |
| Static readonly | PascalCase | `ValidRoles`, `SupportedLocales` |
| Miembros de Enum | PascalCase | `UserRole.Admin`, `UserStatus.Active` |
| Archivos | Coincidir con el nombre del tipo público | `UserService.cs`, `IUserRepository.cs` |

Prohibido: notación húngara, guiones bajos en miembros públicos, abreviaciones más allá del conjunto conocido (`Id`, `Dto`, `Http`, `Json`, `Db`).

## Manejo de Nulos

- Tipos de referencia anulables habilitados globalmente (`<Nullable>enable</Nullable>`).
- `ArgumentNullException.ThrowIfNull()` en todos los límites de API pública.
- Evitar coalescencia nula para flujo de control; preferir coincidencia de patrones.
- Todos los parámetros de servicio y tipos de retorno anotados con anotaciones anulables.

```csharp
public Result<UserProfile, Error> UpdateUserProfile(
    Guid userId, UpdateProfileRequest request)
{
    ArgumentNullException.ThrowIfNull(request);
    // ...
}
```

## Patrones Asíncronos

- Todos los métodos de E/S devuelven `Task<T>` o `ValueTask<T>`.
- `ConfigureAwait(false)` en código de librería; no requerido en controladores.
- CancellationToken como último parámetro en todos los métodos de E/S asíncronos.
- `Task.WhenAll` para llamadas independientes paralelas.
- `async void` está prohibido; usar `FireAndForgetHandler` para escenarios de disparar y olvidar.

```csharp
public async Task<Result<UserProfile>> GetUserAsync(
    Guid userId, CancellationToken ct)
{
    var user = await _userRepository.GetByIdAsync(userId, ct)
        .ConfigureAwait(false);
    if (user is null)
        return new NotFoundError($"User {userId} not found");
    return user;
}
```

## Patrón Result&lt;T, E&gt;

Todos los métodos de la capa de servicio devuelven `Result<T, E>` (mediante FluentResults o similar) en lugar de lanzar excepciones para fallos a nivel de dominio.

```csharp
public async Task<Result<UserProfile, Error>> CreateUserAsync(
    CreateUserRequest request, CancellationToken ct)
{
    // Validación
    if (await _userRepository.EmailExistsAsync(request.Email, ct))
        return new ConflictError("Email already exists");

    // Lógica de dominio
    var user = UserProfile.Create(request.Email, request.FirstName, request.LastName, _tenantProvider.TenantId);
    var created = await _userRepository.AddAsync(user, ct);

    // Publicación de eventos
    await _eventPublisher.PublishAsync(new UserCreatedEvent(created), ct);

    return created;
}
```

## Inyección de Dependencias

- Servicios registrados por interfaz con tiempos de vida scoped o transient.
- `AddDbContextPool<T>` para registros de DbContext.
- Patrón HttpClient tipado para llamadas HTTP externas (Auth Service JWKS, Microsoft Graph).
- Nunca inyectar `IServiceProvider` en código de aplicación (anti-patrón Service Locator).
- Todas las dependencias externas deben ser interfaces para facilitar pruebas.

```csharp
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddHttpClient<IGraphApiClient, GraphApiClient>();
builder.Services.AddPooledDbContextFactory<UsersDbContext>(options =>
    options.UseNpgsql(connectionString));
```

## Registro de Logs

- Usar `ILogger<T>` con generadores de código fuente en tiempo de compilación (`[LoggerMessage]`).
- Logs estructurados en JSON mediante Serilog.
- Nunca interpolar cadenas en llamadas de log.
- Categorías de eventos: Operaciones de Usuario (1000-1999), Operaciones de Inquilino (2000-2999), Eventos de Auth (3000-3999), Graph API (4000-4999).

```csharp
public static partial class LogMessages
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "User {UserId} created in tenant {TenantId}")]
    public static partial void UserCreated(this ILogger logger, Guid userId, Guid tenantId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "User {UserId} soft-deleted by {DeletedByUserId}")]
    public static partial void UserSoftDeleted(this ILogger logger, Guid userId, Guid deletedByUserId);
}
```

## Lista de Verificación de Revisión de Código

1. Todos los métodos públicos tienen comentarios XML doc.
2. No hay comentarios TODO o HACK en código de producción.
3. CancellationToken se propaga a través de todas las cadenas de llamadas asíncronas.
4. Sin cadenas/números mágicos — usar constantes o configuración.
5. Las consultas de soft-delete incluyen el filtro `deleted_at IS NULL`.
6. El aislamiento de inquilino se verifica para cada consulta (filtro tenant_id).
7. La autorización RBAC se valida para cada endpoint.
8. Los eventos de auth consumidos tienen claves de idempotencia.
9. Los eventos de usuario publicados incluyen ID de correlación para trazabilidad.
10. Las pruebas unitarias cubren rutas de éxito, error y casos límite.
11. Las pruebas de integración incluyen un mock de JWKS del Auth Service para validación de tokens.

## Estructura de Archivos

```
src/
  UsersService.Core/          — Modelos de dominio, interfaces, enums, objetos de valor
  UsersService.Application/   — Casos de uso, DTOs, mapeadores, validadores
  UsersService.Infrastructure/— Implementaciones (DB, HTTP (Graph API), bus de eventos)
  UsersService.Api/           — Controladores, middleware, configuración
  UsersService.Worker/        — Servicios en segundo plano (consumidores de eventos, trabajos de limpieza)
tests/
  UsersService.UnitTests/     — xUnit + NSubstitute
  UsersService.IntegrationTests/ — Testcontainers + WireMock
```
