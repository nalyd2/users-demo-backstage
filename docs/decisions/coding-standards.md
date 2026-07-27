# Coding Standards — Users Service

## Scope

Applies to all C# source code within the `UsersService` solution targeting .NET 10 and C# 13. These standards mirror the Auth Service coding standards with adaptations for the users domain, including entity management, multi-tenancy, and soft-delete patterns.

## Naming Conventions

| Category | Convention | Example |
|---|---|---|
| Classes, Records, Structs | PascalCase | `UserService`, `UserProfile`, `TenantContext` |
| Interfaces | IPascalCase | `IUserRepository`, `ITenantProvider` |
| Methods, Properties, Events | PascalCase | `CreateUserAsync()`, `IsSoftDeleted` |
| Private fields | `_camelCase` | `_userRepository`, `_logger` |
| Private static fields | `s_camelCase` | `s_defaultPageSize` |
| Local variables, parameters | camelCase | `userProfile`, `createdUser` |
| Constants | PascalCase | `DefaultPageSize`, `MaxBatchSize` |
| Static readonly | PascalCase | `ValidRoles`, `SupportedLocales` |
| Enum members | PascalCase | `UserRole.Admin`, `UserStatus.Active` |
| Files | Match public type name | `UserService.cs`, `IUserRepository.cs` |

Prohibited: Hungarian notation, underscores in public members, abbreviations beyond well-known set (`Id`, `Dto`, `Http`, `Json`, `Db`).

## Null Handling

- Nullable reference types enabled globally (`<Nullable>enable</Nullable>`).
- `ArgumentNullException.ThrowIfNull()` at all public API boundaries.
- Avoid `null` coalescing for control flow; prefer pattern matching.
- All service parameters and return types annotated with nullable annotations.

```csharp
public Result<UserProfile, Error> UpdateUserProfile(
    Guid userId, UpdateProfileRequest request)
{
    ArgumentNullException.ThrowIfNull(request);
    // ...
}
```

## Async Patterns

- All I/O methods return `Task<T>` or `ValueTask<T>`.
- `ConfigureAwait(false)` in library code; not required in controllers.
- CancellationToken as last parameter in all async I/O methods.
- `Task.WhenAll` for parallel independent calls.
- `async void` is prohibited; use `FireAndForgetHandler` for fire-and-forget scenarios.

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

## Result&lt;T, E&gt; Pattern

All service-layer methods return `Result<T, E>` (via FluentResults or similar) rather than throwing exceptions for domain-level failures.

```csharp
public async Task<Result<UserProfile, Error>> CreateUserAsync(
    CreateUserRequest request, CancellationToken ct)
{
    // Validation
    if (await _userRepository.EmailExistsAsync(request.Email, ct))
        return new ConflictError("Email already exists");

    // Domain logic
    var user = UserProfile.Create(request.Email, request.FirstName, request.LastName, _tenantProvider.TenantId);
    var created = await _userRepository.AddAsync(user, ct);

    // Event publishing
    await _eventPublisher.PublishAsync(new UserCreatedEvent(created), ct);

    return created;
}
```

## Dependency Injection

- Services registered by interface with scoped or transient lifetimes.
- `AddDbContextPool<T>` for DbContext registrations.
- Typed HttpClient pattern for external HTTP calls (Auth Service JWKS, Microsoft Graph).
- Never inject `IServiceProvider` in application code (Service Locator anti-pattern).
- All external dependencies must be interfaces for testability.

```csharp
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddHttpClient<IGraphApiClient, GraphApiClient>();
builder.Services.AddPooledDbContextFactory<UsersDbContext>(options =>
    options.UseNpgsql(connectionString));
```

## Logging

- Use `ILogger<T>` with compile-time source generators (`[LoggerMessage]`).
- Structured JSON logging via Serilog.
- Never interpolate strings in log calls.
- Event categories: User Operations (1000-1999), Tenant Operations (2000-2999), Auth Events (3000-3999), Graph API (4000-4999).

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

## Code Review Checklist

1. All public methods have XML doc comments.
2. No TODO or HACK comments survive in production code.
3. CancellationToken is plumbed through all async call chains.
4. No magic strings/numbers — use constants or configuration.
5. Soft-delete queries include the `deleted_at IS NULL` filter.
6. Tenant isolation is verified for every query (tenant_id filter).
7. RBAC authorization is validated for every endpoint.
8. Consumed auth events have idempotency keys.
9. Published user events include correlation ID for tracing.
10. Unit tests cover success, error, and edge case paths.
11. Integration tests include Auth Service JWKS mock for token validation.

## File Structure

```
src/
  UsersService.Core/          — Domain models, interfaces, enums, value objects
  UsersService.Application/   — Use cases, DTOs, mappers, validators
  UsersService.Infrastructure/— Implementations (DB, HTTP (Graph API), event bus)
  UsersService.Api/           — Controllers, middleware, configuration
  UsersService.Worker/        — Background services (event consumers, cleanup jobs)
tests/
  UsersService.UnitTests/     — xUnit + NSubstitute
  UsersService.IntegrationTests/ — Testcontainers + WireMock
```
