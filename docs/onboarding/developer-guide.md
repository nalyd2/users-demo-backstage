# Developer Guide -- Users Service

This guide is the entry point for engineers working on the **Users Service** (`users-service`). It covers the architecture, codebase layout, step-by-step instructions for adding endpoints, the RBAC model, the event consumer pattern, and the pitfalls that trip up new team members.

---

## Table of Contents

- [Architecture Walkthrough](#architecture-walkthrough)
    - [Dependency on the Authentication Service](#dependency-on-the-authentication-service)
    - [Hexagonal (Ports & Adapters) Layout](#hexagonal-ports--adapters-layout)
    - [Request Lifecycle](#request-lifecycle)
- [Code Organization](#code-organization)
    - [Source Layout](#source-layout)
    - [Namespace Convention](#namespace-convention)
    - [Key Files Cheat Sheet](#key-files-cheat-sheet)
- [How to Add a New Endpoint](#how-to-add-a-new-endpoint)
    - [Step 1: Define Request / Response DTOs](#step-1-define-request--response-dtos)
    - [Step 2: Add the Route](#step-2-add-the-route)
    - [Step 3: Wire the Application Service](#step-3-wire-the-application-service)
    - [Step 4: Add Validation](#step-4-add-validation)
    - [Step 5: Register Dependencies](#step-5-register-dependencies)
- [RBAC Enforcement](#rbac-enforcement)
    - [Role Policy Authorisation](#role-policy-authorisation)
    - [Endpoint-Level Checks](#endpoint-level-checks)
    - [Field-Level Permissions on Update](#field-level-permissions-on-update)
- [Event Consumer Pattern](#event-consumer-pattern)
    - [Consumed Events (from Auth Service)](#consumed-events-from-auth-service)
    - [Published Events (to the Platform)](#published-events-to-the-platform)
    - [Writing a New Consumer](#writing-a-new-consumer)
- [Common Pitfalls](#common-pitfalls)
- [Related Documents](#related-documents)

---

## Architecture Walkthrough

### Dependency on the Authentication Service

The Users Service has a **hard runtime dependency** on the [Authentication Service](https://backstage.internal/platform/component/auth-service) (`auth-service`). It does not issue, sign, or manage tokens -- it is a **JWT-consuming service** only.

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Users Service                                │
│                                                                     │
│  ┌──────────────┐    gRPC / mTLS     ┌──────────────────────────┐   │
│  │  Controller   │ ──────────────────▶│  AuthServiceClient       │   │
│  │  (Minimal API)│                    │  (IAuthServiceClient)    │   │
│  └──────┬───────┘                    └───────────┬──────────────┘   │
│         │                                        │                  │
│         │ JWT in Authorization header              │ ValidateToken() │
│         ▼                                        ▼                  │
│  ┌──────────────┐                    ┌──────────────────────────┐   │
│  │  UserService  │                    │  JWKS Cache (5 min TTL)  │   │
│  │  (App Layer)  │                    └──────────────────────────┘   │
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
│  │  - Issuance (RS256)   │   │
│  │  - Validation         │   │
│  │  - Rotation           │   │
│  └──────────────────────┘   │
│                              │
│  ┌──────────────────────┐   │
│  │  AuthService          │   │
│  │  - Credential verify   │   │
│  │  - Refresh flow       │   │
│  │  - Event pub (SBus)   │   │
│  └──────────────────────┘   │
└──────────────────────────────┘
```

**What this means for you as a developer:**

1. **Every authenticated request carries a JWT issued by `auth-service`.** The JWT contains claims (`sub`, `roles`, `tid`, `jti`) that the Users Service extracts and trusts for authorisation.
2. **Token validation is attempted against `auth-service` via gRPC first.** If the call fails, the service falls back to a **local JWKS cache** with a 5-minute TTL. After the cache expires and the Auth Service remains unreachable, the service returns `503 Service Unavailable` for all authenticated endpoints.
3. **There is no "login" endpoint in this service.** Authentication is handled entirely by `auth-service`. The Users Service only manages user *profiles* (name, email, department, roles).
4. **The service subscribes to `auth-events`** on Azure Service Bus to update user activity state (last login, last logout) without polling.

**Failure modes to understand:**

| Scenario | Effect | Mitigation |
|---|---|---|
| Auth Service down < 5 min | Local JWKS cache serves validation | No visible impact |
| Auth Service down > 5 min | Token validation fails; service returns 503 | PagerDuty alert; SRE failover |
| gRPC call timeout (500 ms) | Fall back to JWKS cache | Configured in `Auth__GrpcTimeoutMs` |
| Circuit breaker open (30 s) | All validations hit local cache | Automatic half-open probe restores gRPC |

### Hexagonal (Ports & Adapters) Layout

The codebase follows the **Hexagonal Architecture** pattern:

```
src/UsersService/
├── Models/           Domain DTOs and request/response records
├── Services/         Application services (ports)
│   └── IUserService  Port interface
├── Repositories/     Data access (adapters, outbound)
├── Endpoints/        Minimal API route definitions (adapters, inbound)
├── Middleware/       JWT validation and authorisation middleware
├── EventHandlers/    Service Bus message handlers
├── Configuration/    Options classes and binding
└── Program.cs        Composition root
```

| Layer | Folder | Role |
|---|---|---|
| **Domain** | `Models/` | Pure data records -- no behaviour. `UserDto`, `UserEntity`, `CreateUserRequest`, `UpdateUserRequest`. |
| **Application (Port)** | `Services/` | Interfaces like `IUserService` define what the service does. Implementations in the same folder orchestrate the workflow. |
| **Infrastructure (Adapter)** | `Repositories/`, `Middleware/`, `EventHandlers/` | Concrete implementations of ports. `UserRepository` talks to PostgreSQL. `AuthServiceClient` calls gRPC. |
| **Inbound Adapter** | `Endpoints/` | ASP.NET Core Minimal API route groups that translate HTTP into application-layer calls. |

### Request Lifecycle

A typical authenticated request flows through these layers:

```
HTTP Request
    │
    ▼
┌─────────────────────────────┐
│ 1. JWT Authentication       │  Middleware validates JWT (gRPC or JWKS cache)
│    Middleware                │  Extracts ClaimsPrincipal (sub, roles, tid)
└──────────┬──────────────────┘
           ▼
┌─────────────────────────────┐
│ 2. Endpoint (Route Handler) │  Minimal API method (static, grouped)
│                              │  - Deserialises request body
│                              │  - Calls IUserService
│                              │  - Maps result to HTTP response
└──────────┬──────────────────┘
           ▼
┌─────────────────────────────┐
│ 3. Application Service      │  IUserService implementation
│                              │  - Calls ProfileValidator
│                              │  - Calls IUserRepository
│                              │  - Calls IEventPublisher
│                              │  - Calls INotificationClient
└──────────┬──────────────────┘
           ▼
┌─────────────────────────────┐
│ 4. Repository / Adapter     │  Dapper + Npgsql to PostgreSQL
│                              │  - Parameterised queries
│                              │  - tenant_id scoping
│                              │  - Soft-delete WHERE clause
└─────────────────────────────┘
```

---

## Code Organization

### Source Layout

```
src/UsersService/
├── Program.cs                       # Composition root, middleware pipeline, DI registration
├── UsersService.csproj              # .NET 10 project with essential NuGet packages
├── appsettings.json                 # Production configuration
├── appsettings.Development.json     # Local overrides
│
├── Models/
│   ├── User.cs                      # UserDto (API response), UserEntity (DB entity), ToDto()
│   ├── CreateUserRequest.cs         # POST request DTO
│   └── UpdateUserRequest.cs         # PUT request DTO (all fields optional)
│
├── Services/
│   ├── IUserService.cs              # Port interface + UserResult<T>, PaginatedList<T>
│   └── UserService.cs               # Application service implementation
│
├── Middleware/
│   ├── JwtValidationMiddleware.cs   # Validates JWT, populates HttpContext.Items
│   ├── TenantContextMiddleware.cs   # Extracts tid, enriches scoped TenantContext
│   └── RequestLoggingMiddleware.cs  # Correlation ID, structured logging enrichment
│
├── Endpoints/
│   ├── UserEndpoints.cs             # Route group for /api/users
│   └── HealthEndpoints.cs           # Route group for /api/health
│
├── Repositories/
│   ├── IUserRepository.cs           # Data access port
│   └── UserRepository.cs            # Dapper implementation
│
├── EventHandlers/
│   ├── AuthEventConsumer.cs         # BackgroundService processing auth-events
│   ├── IEventPublisher.cs           # Port for publishing user events
│   └── EventPublisher.cs            # Service Bus publisher
│
├── Configuration/
│   ├── AuthOptions.cs               # Strong-typed Auth configuration
│   ├── UsersOptions.cs              # Strong-typed Users configuration
│   └── ServiceBusOptions.cs         # Strong-typed Service Bus configuration
│
└── Observability/
    ├── MetricsRegistry.cs           # Prometheus counters, histograms, gauges
    ├── ActivitySources.cs           # OpenTelemetry activity sources
    └── LogEnrichers.cs              # Serilog enrichers (tenant, correlation ID)
```

### Namespace Convention

All code lives under `Platform.UsersService.*`. The namespace maps directly to the folder:

| Folder | Namespace |
|---|---|
| `Models/` | `Platform.UsersService.Models` |
| `Services/` | `Platform.UsersService.Services` |
| `Endpoints/` | `Platform.UsersService.Endpoints` |
| `Repositories/` | `Platform.UsersService.Repositories` |
| `EventHandlers/` | `Platform.UsersService.EventHandlers` |

Internal implementation classes are marked `internal sealed` -- consumers depend on interfaces, never concretions.

### Key Files Cheat Sheet

| File | What it contains | Why you'll touch it |
|---|---|---|
| `Program.cs` | Service registration, middleware pipeline, endpoint mounting | Adding a new dependency or middleware |
| `Models/CreateUserRequest.cs` | `CreateUserRequest` record | Extending the user creation schema |
| `Models/UpdateUserRequest.cs` | `UpdateUserRequest` record (all optional) | Adding editable fields |
| `Models/User.cs` | `UserDto` and `UserEntity` records | Changing the API response shape or DB columns |
| `Services/IUserService.cs` | Port interface, `UserResult<T>`, `PaginatedList<T>` | Adding a new operation |
| `Services/UserService.cs` | Application logic orchestrator | Implementing new business rules |
| `Middleware/JwtValidationMiddleware.cs` | JWT validation with gRPC + JWKS fallback | Changing auth behaviour |
| `Endpoints/UserEndpoints.cs` | Route group for `/api/users` | Adding a new endpoint to the users API |
| `Endpoints/HealthEndpoints.cs` | Liveness + readiness probes | Adding a dependency check |
| `Repositories/UserRepository.cs` | Dapper SQL for all user queries | Writing new queries or changing schema |
| `EventHandlers/AuthEventConsumer.cs` | Service Bus subscription processor | Adding a new consumed event |
| `EventHandlers/EventPublisher.cs` | Publishing user lifecycle events | Adding a new published event |
| `appsettings.json` | Environment-agnostic defaults | Changing cache TTLs, page sizes |
| `appsettings.Development.json` | Local development overrides | Configuring local PostgreSQL |

---

## How to Add a New Endpoint

This walkthrough adds a `PATCH /api/users/{userId}/status` endpoint that allows an admin to activate or deactivate a user account. The pattern applies to any new operation.

### Step 1: Define Request / Response DTOs

File: `src/UsersService/Models/UpdateUserStatusRequest.cs`

```csharp
namespace Platform.UsersService.Models;

/// <summary>
/// Request to change a user's account status.
/// </summary>
public sealed record UpdateUserStatusRequest
{
    /// <summary>New account status. Valid values: "active", "inactive".</summary>
    public required string Status { get; init; }
}
```

Add the status field to `UserDto` and `UserEntity` in `Models/User.cs`:

```csharp
// In UserDto
public string Status { get; init; } = "active";

// In UserEntity
public string Status { get; init; } = "active";
```

Update `ToDto()` in `UserEntity` to carry the status field.

### Step 2: Add the Route

File: `src/UsersService/Endpoints/UserEndpoints.cs`

Add a static method inside the existing `UserEndpoints` class and register it in the route group:

```csharp
public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users")
            .WithOpenApi()
            .RequireAuthorization();

        // Existing routes...
        group.MapGet("/", GetUsersAsync);
        group.MapGet("/{userId:guid}", GetUserByIdAsync);
        group.MapPost("/", CreateUserAsync);
        group.MapPut("/{userId:guid}", UpdateUserAsync);
        group.MapDelete("/{userId:guid}", DeleteUserAsync);

        // NEW ROUTE
        group.MapPatch("/{userId:guid}/status", UpdateUserStatusAsync)
            .WithName("UpdateUserStatus")
            .WithDescription("Activates or deactivates a user account. Admin only.")
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
        // RBAC enforcement happens inside UserService via the claims principal
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

Mount the endpoint group in `Program.cs` -- if the group is already called (e.g., `app.MapUserEndpoints()`), the new route is automatically included.

### Step 3: Wire the Application Service

Add the method to `IUserService`:

```csharp
Task<UserResult<UserDto>> UpdateUserStatusAsync(
    Guid userId, string newStatus, ClaimsPrincipal principal, CancellationToken ct);
```

Implement it in `UserService`:

```csharp
public async Task<UserResult<UserDto>> UpdateUserStatusAsync(
    Guid userId, string newStatus, ClaimsPrincipal principal, CancellationToken ct)
{
    // 1. RBAC -- only admin can change status
    var roles = principal.FindAll("roles").Select(c => c.Value).ToArray();
    if (!roles.Contains("admin"))
    {
        return UserResult<UserDto>.Failure("Admin role is required to change account status.", 403);
    }

    // 2. Validate status value
    if (newStatus != "active" && newStatus != "inactive")
    {
        return UserResult<UserDto>.Failure("Status must be 'active' or 'inactive'.", 400);
    }

    // 3. Extract tenant ID from JWT (never from user input)
    var tenantId = Guid.Parse(principal.FindFirstValue("tid")!);
    var actorId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    // 4. Load the user (includes tenant scoping)
    var existing = await _userRepository.GetUserByIdAsync(userId, tenantId, ct);
    if (existing is null)
    {
        return UserResult<UserDto>.Failure("User not found.", 404);
    }

    // 5. Apply the update
    var updated = existing with { Status = newStatus, UpdatedAt = DateTimeOffset.UtcNow };
    await _userRepository.UpdateUserAsync(updated, ct);

    // 6. Audit log
    await _userRepository.InsertAuditLogAsync(new AuditLogEntry
    {
        UserId = userId, Action = "status_changed",
        Changes = JsonSerializer.SerializeToElement(new { from = existing.Status, to = newStatus }),
        ActorId = actorId, PerformedAt = DateTimeOffset.UtcNow
    }, ct);

    // 7. Publish event
    await _eventPublisher.PublishAsync(new UserStatusChanged(userId, newStatus, tenantId, actorId), ct);

    _logger.LogInformation("User {UserId} status changed to {NewStatus} by {ActorId}", userId, newStatus, actorId);

    return UserResult<UserDto>.Success(updated.ToDto());
}
```

### Step 4: Add Validation

The endpoint above validates `Status` as an allowed value. For more complex validation, register a `FluentValidation` validator:

```csharp
public class UpdateUserStatusValidator : AbstractValidator<UpdateUserStatusRequest>
{
    public UpdateUserStatusValidator()
    {
        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => s is "active" or "inactive")
            .WithMessage("Status must be 'active' or 'inactive'.");
    }
}
```

Register it in `Program.cs`:

```csharp
builder.Services.AddValidatorsFromAssemblyContaining<UpdateUserStatusValidator>();
```

### Step 5: Register Dependencies

If your endpoint needs a new external dependency (e.g., a gRPC client to another service), register it in `Program.cs`:

```csharp
// Example: new gRPC client
builder.Services.AddGrpcClient<SomeService.SomeServiceClient>(o =>
{
    o.Address = new Uri(builder.Configuration["SomeService:Endpoint"]!);
});
```

---

## RBAC Enforcement

The Users Service uses a **two-layer** authorisation model:

### Role Policy Authorisation

ASP.NET Core [authorisation policies](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies) map JWT roles to endpoint access. Policy registration in `Program.cs`:

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy =>
        policy.RequireRole("admin"))
    .AddPolicy("AdminOrOperator", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.IsInRole("admin") || ctx.User.IsInRole("operator")));
```

Apply policies to route groups:

```csharp
group.MapPost("/", CreateUserAsync).RequireAuthorization("AdminOnly");
group.MapGet("/", GetUsersAsync).RequireAuthorization("AdminOrOperator");
```

### Endpoint-Level Checks

For finer-grained rules (e.g., "user can read self"), the application layer inspects the `ClaimsPrincipal` directly:

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
        return UserResult<UserDto>.Failure("Access denied.", 403);
    }

    // ... proceed with tenant-scoped query
}
```

### Field-Level Permissions on Update

The `PUT /api/users/{id}` endpoint has field-level rules depending on the caller's role. These are enforced inside the application service, not at the endpoint level:

```csharp
public async Task<UserResult<UserDto>> UpdateUserAsync(
    Guid userId, UpdateUserRequest request, ClaimsPrincipal principal, CancellationToken ct)
{
    var isAdmin = principal.IsInRole("admin");
    var isOperator = principal.IsInRole("operator");
    var isSelf = userId == GetUserId(principal);

    if (!isAdmin && !isOperator && !isSelf)
        return UserResult<UserDto>.Failure("Access denied.", 403);

    var existing = await _userRepository.GetUserByIdAsync(userId, tenantId, ct);
    if (existing is null)
        return UserResult<UserDto>.Failure("User not found.", 404);

    // Build update selectively based on role
    var updated = existing with
    {
        Email = request.Email ?? existing.Email,
        DisplayName = request.DisplayName ?? existing.DisplayName,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    // Only admin can change roles
    if (request.Roles is not null)
    {
        if (!isAdmin)
            return UserResult<UserDto>.Failure("Only admins can change roles.", 403);
        updated = updated with { Roles = request.Roles };
    }

    // Operator and self cannot change department or job title
    if (!isAdmin && request.Department is not null)
        return UserResult<UserDto>.Failure("Only admins can change department.", 403);
    if (!isAdmin && request.JobTitle is not null)
        return UserResult<UserDto>.Failure("Only admins can change job title.", 403);

    // Apply admin-only fields
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

**RBAC Matrix (reproduced from [Component View](architecture/components.md)):**

| Action | `admin` | `operator` | `user` |
|---|---|---|---|
| `GET /api/users` | All | All | -- |
| `GET /api/users/{id}` | Any | Any | Self only |
| `POST /api/users` | Create | -- | -- |
| `PUT /api/users/{id}` | Any (all fields) | Limited fields | Self (limited) |
| `DELETE /api/users/{id}` | Delete | -- | -- |

---

## Event Consumer Pattern

The service runs a `BackgroundService` that subscribes to the `auth-events` Azure Service Bus topic. This is how the Users Service stays informed about authentication activity without polling the Auth Service.

### Consumed Events (from Auth Service)

| Event | Action | Idempotency |
|---|---|---|
| `user.login` | `UPDATE users SET last_login_at = @timestamp WHERE id = @userId` | `eventId` dedup |
| `user.logout` | `UPDATE users SET last_logout_at = @timestamp WHERE id = @userId` | `eventId` dedup |
| `token.revoked` | `INSERT INTO token_revocations (user_id, event_id, revoked_at)` | `eventId` dedup |

### Published Events (to the Platform)

| Event | Trigger | Payload |
|---|---|---|
| `user.created` | `POST /api/users` success | `{ userId, username, email, tenantId, actorId }` |
| `user.updated` | `PUT /api/users/{id}` success | `{ userId, changedFields[], actorId }` |
| `user.deleted` | `DELETE /api/users/{id}` success | `{ userId, actorId }` |

### Writing a New Consumer

To consume a new event type from the `auth-events` topic:

**1. Add the handler in `AuthEventConsumer.cs`:**

```csharp
private async Task HandleUserLoginAsync(ProcessMessageEventArgs args, CancellationToken ct)
{
    using var activity = _activitySource.StartActivity("AuthEventConsumer.HandleUserLogin");
    var body = Encoding.UTF8.GetString(args.Message.Body);

    var envelope = JsonSerializer.Deserialize<EventEnvelope<LoginEventData>>(body);
    if (envelope is null) { await args.DeadLetterMessageAsync(args.Message); return; }

    // Deduplication
    if (await _eventDeduplication.IsProcessedAsync(envelope.EventId, ct))
    {
        await args.CompleteMessageAsync(args.Message);
        return;
    }

    await _userRepository.UpdateLastLoginAsync(envelope.Data.UserId, envelope.Data.Timestamp, ct);
    await _eventDeduplication.MarkProcessedAsync(envelope.EventId, ct);

    _metrics.EventProcessed("user.login", "success");
    _logger.LogInformation("Processed user.login for {UserId}", envelope.Data.UserId);

    await args.CompleteMessageAsync(args.Message);
}
```

**2. Register the handler in the message dispatch map (inside `AuthEventConsumer`):**

```csharp
private static readonly Dictionary<string, Func<ProcessMessageEventArgs, CancellationToken, Task>> Handlers = new()
{
    ["user.login"] = (ctx, ct) => new AuthEventConsumer(/*...*/).HandleUserLoginAsync(ctx, ct),
    ["user.logout"] = /* ... */,
    ["token.revoked"] = /* ... */,
};
```

**3. Add a deduplication check** -- the `event_deduplication` table prevents double-processing when Service Bus delivers the same message more than once (at-least-once guarantee).

**Processing guarantees:**

| Guarantee | Mechanism |
|---|---|
| At-least-once | Service Bus PeekLock + auto-renew (max 5 min) |
| In-order per user | Session-enabled topic (session ID = `userId`) |
| Idempotency | `event_deduplication(event_id PK)` table |
| Dead-letter | After 10 delivery failures |

---

## Common Pitfalls

### 1. Forgetting Tenant ID Scoping

Every query **must** include `tenant_id`. The tenant ID comes from the JWT (`tid` claim), never from user input. Violation = CROSS-TENANT DATA LEAKAGE (severity: critical).

```csharp
// WRONG -- attacker can pass any tenant
SELECT * FROM users WHERE id = @userId;

// RIGHT
SELECT * FROM users WHERE id = @userId AND tenant_id = @tenantId;
```

### 2. Exposing Internal Entity Types to the API

`UserEntity` contains database-internal fields (`DeletedAt`, `TenantId`) that must never be serialised to API responses. Always map to `UserDto` via `ToDto()`.

```csharp
// WRONG -- exposes DeletedAt to API consumers
return Results.Ok(userEntity);

// RIGHT
return Results.Ok(userEntity.ToDto());
```

### 3. Skipping Idempotency for Event Handlers

Service Bus guarantees at-least-once delivery. Without deduplication, the same `user.login` event could update `last_login_at` twice with the same value (harmless but wasteful) -- or worse, process a `user.deleted` event twice and fail on the second attempt. Always check the `event_deduplication` table first.

### 4. Using the Wrong HTTP Method for Partial Updates

Use `PATCH` for partial updates, not `POST` or overloaded `PUT`. The existing `PUT /api/users/{id}` already supports partial updates via optional fields, but new endpoints that modify a subset of fields should prefer `PATCH`.

### 5. Not Propagating the Correlation ID

Every request carries a `trace-id` header from the API Gateway. If you make outbound calls (gRPC to Auth Service, HTTP to Graph API), propagate this ID so the distributed trace is complete:

```csharp
using var activity = _activitySource.StartActivity("UserService.CreateUser");
activity?.SetTag("user.id", userId.ToString());
activity?.SetTag("tenant.id", tenantId.ToString());
```

### 6. Bypassing the Application Layer

Endpoints must call `IUserService` -- they should never call `IUserRepository` directly. The application layer is where RBAC, validation, audit logging, and event publishing happen. Skipping it means skipping security controls.

```csharp
// WRONG -- bypasses RBAC and audit
group.MapPost("/", async (Guid id, IUserRepository repo) => { ... });

// RIGHT
group.MapPost("/", async (Guid id, IUserService svc) => { ... });
```

### 7. Using `is null` Checks on Optional Fields in `UpdateUserRequest`

`UpdateUserRequest` uses nullable fields to distinguish "not provided" from "set to null". A `string?` field that is `null` means "do not update this field". This breaks if the client explicitly wants to clear a value:

```csharp
// This cannot distinguish "don't change department" from "clear department".
// Solution: use a discriminated union or a separate ClearFields list.
```

Current convention: if a field is `null` in `UpdateUserRequest`, it is not updated. Clearing a field is not supported for most fields. If you add a clearable field, add a separate boolean or use `JsonIgnoreCondition.WhenWritingNull` on the client.

### 8. Hard-Coded Configuration Values

Never hard-code connection strings, endpoint URLs, or timeouts. Every environment-specific value belongs in `appsettings.json`, `appsettings.{Environment}.json`, or Azure Key Vault.

```csharp
// WRONG
var timeout = TimeSpan.FromMilliseconds(500);

// RIGHT
var timeoutMs = configuration.GetValue<int>("Auth:GrpcTimeoutMs", 500);
var timeout = TimeSpan.FromMilliseconds(timeoutMs);
```

### 9. Ignoring the Circuit Breaker on Auth Service gRPC Calls

The `AuthServiceClient` has a circuit breaker configured at 5 consecutive failures with a 30-second open duration. If you bypass this client and call the Auth Service directly, you lose circuit protection and could cascade failures under load.

### 10. Adding Endpoints That Require Authentication but Forgetting `RequireAuthorization()`

New route handlers added to a group that already has `.RequireAuthorization()` inherit the requirement. However, if you create a new route group (e.g., a new admin console), remember to call `.RequireAuthorization()` on it, or your endpoint will be publicly accessible.

---

## Related Documents

- [Architecture Overview](architecture/overview.md) -- system design and principles
- [System Context](architecture/context.md) -- external system interactions
- [Component View](architecture/components.md) -- internal component structure
- [Security Architecture](architecture/security.md) -- JWT validation and RBAC model
- [Container View](architecture/containers.md) -- runtime containers and data stores
- [Users API](api/users-api.md) -- full endpoint reference
- [Events](api/events.md) -- consumed and published event schemas
- [Variables & Configuration](api/variables.md) -- environment variables and feature flags
- [Local Development](onboarding/local-development.md) -- setting up the development environment
- [How to Debug](onboarding/how-to-debug.md) -- debugging techniques
- [Testing](onboarding/testing.md) -- testing strategy
- [Coding Standards](decisions/coding-standards.md) -- code conventions
- [ADR-002 -- JWT Validation at Gateway vs. Service Level](adr/ADR-002.md)
- [ADR-003 -- Event-Driven User State Synchronization](adr/ADR-003.md)
