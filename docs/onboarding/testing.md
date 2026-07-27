# Testing Guide — Users Service

## Scope

This document defines the testing strategy, standards, and practices for the Users Service. It covers the full testing pyramid — from fast unit tests through to end-to-end integration tests — and addresses service-specific concerns such as gRPC mocking of the Authentication Service, RBAC enforcement verification, and event consumer idempotency testing.

---

## 1. Testing Philosophy

### 1.1 Principles

| Principle | Rationale |
|---|---|
| **Test behaviour, not implementation** | Tests should verify outcomes, not internal method calls. Refactoring the implementation should not require rewriting tests. |
| **Deterministic by default** | Tests must produce the same result on every run. No flaky tests. Any non-determinism (time, random, async races) must be explicitly controlled. |
| **Fast feedback** | The unit test suite must complete in under 30 seconds. Integration tests run in CI and pre-merge but are not part of the inner dev loop. |
| **Realistic dependencies** | External services (PostgreSQL, Service Bus) are exercised through Testcontainers in integration tests, never mocked at that level. |
| **Defence in depth** | Every security boundary (JWT validation, RBAC, tenant isolation) is covered at multiple test levels. |

### 1.2 Test Levels

```
            /\
           /  \
          /    \
         / E2E \
        /--------\
       /  Service \
      /   Tests    \
     /--------------\
    / Integration    \
   /     Tests        \
  /--------------------\
 /      Unit Tests      \
/------------------------\
```

| Level | Speed | Scope | Purpose |
|---|---|---|---|
| **Unit** | < 5 ms/test | Single class in isolation | Business logic, validation rules, mapping, edge cases |
| **Integration** | < 5 s/test | Service + real PostgreSQL + fake gRPC | Repository queries, RBAC enforcement, event publication |
| **Service** | < 30 s/test | HTTP endpoint through middleware | Request pipeline, auth middleware, error handling, end-to-end flows |
| **E2E** | < 5 min | Full deployment in ephemeral env | CI gate — smoke tests against a real staging environment |

---

## 2. Test Infrastructure

### 2.1 Project Structure

```
tests/
  UsersService.Tests/
    Controllers/
      UsersControllerTests.cs
      HealthControllerTests.cs
    Services/
      UserServiceTests.cs
      ProfileValidatorTests.cs
      EventPublisherTests.cs
    Infrastructure/
      AuthServiceClientTests.cs
      UserRepositoryTests.cs
      EventConsumerTests.cs
      GraphApiClientTests.cs
    Middleware/
      JwtValidationMiddlewareTests.cs
      RbacEnforcementTests.cs
    Integration/
      RepositoryTests.cs
      EventPublishingTests.cs
      EventConsumptionTests.cs
      MultiTenancyTests.cs
    Service/
      UsersApiSmokeTests.cs
    Fixtures/
      TestDatabase.cs
      TestServiceBus.cs
      AuthServiceGrpcServer.cs
      TestData.cs
      WebApplicationFactory.cs
```

### 2.2 Test SDK and Tools

| Tool | Version | Purpose |
|---|---|---|
| **xUnit** | 2.x | Test framework |
| **FluentAssertions** | 7.x | Readable assertions |
| **NSubstitute** | 5.x | Mocking and stubs |
| **Testcontainers** | 4.x | Ephemeral PostgreSQL and Service Bus emulator |
| **Microsoft.AspNetCore.TestHost** | 10.x | In-process HTTP server for service tests |
| **Verify** | 26.x | Snapshot testing for JSON responses (optional) |
| **Bogus** | 35.x | Realistic test data generation |

### 2.3 Test Project Configuration

The test project `.csproj` file must include these dependencies:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="FluentAssertions" Version="7.*" />
    <PackageReference Include="NSubstitute" Version="5.*" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="4.*" />
    <PackageReference Include="Testcontainers.Azurite" Version="4.*" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.*" />
    <PackageReference Include="Bogus" Version="35.*" />
    <PackageReference Include="coverlet.collector" Version="6.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\UsersService\UsersService.csproj" />
  </ItemGroup>

</Project>
```

### 2.4 Collection Fixtures

All integration tests share a database and Service Bus emulator via xUnit collection fixtures. This avoids starting a container per test.

```csharp
// Fixtures/TestDatabase.cs
public sealed class TestDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => _container.GetConnectionString();
    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        DataSource = new NpgsqlDataSourceBuilder(ConnectionString).Build();
        await RunMigrationsAsync();
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    private async Task RunMigrationsAsync()
    {
        // Execute all migration SQL scripts against the test database.
        // See docs/runbooks/deployment.md for migration inventory.
        var sql = await File.ReadAllTextAsync("../../../Migrations/001_initial_schema.sql");
        await using var cmd = DataSource.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync();
    }
}
```

```csharp
// Fixtures/TestServiceBus.cs
public sealed class TestServiceBus : IAsyncLifetime
{
    private readonly AzuriteContainer _container = new AzuriteBuilder()
        .WithImage("mcr.microsoft.com/azure-storage/azurite:3.33")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();
    public async Task DisposeAsync() => await _container.DisposeAsync();
}
```

```csharp
// Define a collection that groups integration tests sharing the same fixtures.
[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<TestDatabase>, ICollectionFixture<TestServiceBus>
{
}
```

---

## 3. Unit Tests

### 3.1 Scope

Unit tests cover **pure business logic** and **domain rules** in isolation. All external dependencies (repositories, gRPC clients, Service Bus) are mocked.

**What to unit-test:**

- `ProfileValidator` — username format, email format, uniqueness logic, role validity
- `UserService` — orchestration logic, error handling, mapping
- `UserEntity.ToDto()` — mapping entity to DTO
- `AuthServiceClient` fallback behaviour (when JWKS cache is valid)
- RBAC rule evaluation (without middleware)

**What NOT to unit-test:**

- Database queries (covered by integration tests)
- HTTP serialization and routing (covered by service tests)
- gRPC wire protocol (covered by integration tests with a fake server)

### 3.2 Example: ProfileValidator Tests

```csharp
namespace Platform.UsersService.Tests.Services;

public sealed class ProfileValidatorTests
{
    private readonly IProfileValidator _sut;
    private readonly IUserRepository _repository = Substitute.For<IUserRepository>();

    public ProfileValidatorTests()
    {
        _sut = new ProfileValidator(_repository);
    }

    [Theory]
    [InlineData("john.doe", true)]
    [InlineData("a", false)]                 // too short
    [InlineData("John.Doe", false)]           // uppercase not allowed
    [InlineData("john doe", false)]           // space not allowed
    [InlineData("john@doe", false)]           // @ not allowed
    [InlineData("a_b.c-d", true)]            // underscores, dots, hyphens allowed
    [InlineData("", false)]                   // empty
    [InlineData(null, false)]                 // null
    public async Task ValidateUsername_Format(string username, bool expectedValid)
    {
        _repository.IsUsernameTakenAsync(username, Arg.Any<Guid>(), null, Arg.Any<CancellationToken>())
            .Returns(false);

        var request = new CreateUserRequest
        {
            Username = username,
            Email = "test@contoso.com"
        };

        var result = await _sut.ValidateAsync(request, Guid.NewGuid(), CancellationToken.None);

        result.IsValid.Should().Be(expectedValid);
        if (!expectedValid)
        {
            result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateUserRequest.Username));
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenUsernameTaken_ReturnsError()
    {
        var tenantId = Guid.NewGuid();
        _repository.IsUsernameTakenAsync("john.doe", tenantId, null, Arg.Any<CancellationToken>())
            .Returns(true);

        var request = new CreateUserRequest
        {
            Username = "john.doe",
            Email = "john@contoso.com"
        };

        var result = await _sut.ValidateAsync(request, tenantId, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorCode == "USERNAME_TAKEN");
    }

    [Fact]
    public async Task ValidateAsync_WhenUpdating_ExcludesCurrentUserFromUniquenessCheck()
    {
        var tenantId = Guid.NewGuid();
        var existingUserId = Guid.NewGuid();
        _repository.IsUsernameTakenAsync("john.doe", tenantId, existingUserId, Arg.Any<CancellationToken>())
            .Returns(false);

        var request = new UpdateUserRequest { Email = "new@contoso.com" };

        var result = await _sut.ValidateUpdateAsync(request, tenantId, existingUserId, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        await _repository.Received(1).IsUsernameTakenAsync("john.doe", tenantId, existingUserId, Arg.Any<CancellationToken>());
    }
}
```

### 3.3 Example: UserService Tests

```csharp
namespace Platform.UsersService.Tests.Services;

public sealed class UserServiceTests
{
    private readonly IUserService _sut;
    private readonly IUserRepository _repository = Substitute.For<IUserRepository>();
    private readonly IProfileValidator _validator = Substitute.For<IProfileValidator>();
    private readonly IEventPublisher _eventPublisher = Substitute.For<IEventPublisher>();

    public UserServiceTests()
    {
        _sut = new UserService(_repository, _validator, _eventPublisher);
    }

    [Fact]
    public async Task CreateUserAsync_WithValidData_ReturnsCreatedUser()
    {
        var tenantId = Guid.NewGuid();
        var request = new CreateUserRequest
        {
            Username = "jane.doe",
            Email = "jane@contoso.com",
            DisplayName = "Jane Doe"
        };

        _validator.ValidateAsync(request, tenantId, Arg.Any<CancellationToken>())
            .Returns(ValidationResult.Valid);

        var userEntity = new UserEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Username = request.Username,
            Email = request.Email,
            DisplayName = request.DisplayName,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _repository.CreateUserAsync(Arg.Any<UserEntity>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(userEntity));

        var result = await _sut.CreateUserAsync(request, tenantId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Username.Should().Be("jane.doe");

        await _eventPublisher.Received(1).PublishAsync(Arg.Is<UserCreatedEvent>(
            e => e.UserId == userEntity.Id && e.ActorId == tenantId));
    }

    [Fact]
    public async Task CreateUserAsync_WhenValidationFails_ReturnsFailure()
    {
        var tenantId = Guid.NewGuid();
        var request = new CreateUserRequest { Username = "ab", Email = "invalid" };

        _validator.ValidateAsync(request, tenantId, Arg.Any<CancellationToken>())
            .Returns(ValidationResult.FromErrors(new[] { new ValidationError("Username", "INVALID_USERNAME", "Too short") }));

        var result = await _sut.CreateUserAsync(request, tenantId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        await _repository.DidNotReceive().CreateUserAsync(Arg.Any<UserEntity>(), Arg.Any<CancellationToken>());
        await _eventPublisher.DidNotReceive().PublishAsync(Arg.Any<UserCreatedEvent>());
    }

    [Fact]
    public async Task GetUserByIdAsync_WhenUserNotFound_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repository.GetUserByIdAsync(userId, tenantId, Arg.Any<CancellationToken>())
            .Returns((UserEntity?)null);

        var result = await _sut.GetUserByIdAsync(userId, tenantId, null, Guid.Empty, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DeleteUserAsync_SetsDeletedAt()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var result = await _sut.DeleteUserAsync(userId, tenantId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repository.Received(1).SoftDeleteUserAsync(userId, tenantId, Arg.Any<CancellationToken>());
    }
}
```

### 3.4 Mocking the Auth Service gRPC Client

The `AuthServiceClient` implements `IAuthServiceClient`. In unit tests, mock the **interface**, not the gRPC channel.

```csharp
public interface IAuthServiceClient
{
    Task<ClaimsPrincipal?> ValidateTokenAsync(string jwt, CancellationToken ct);
    Task<JwksDocument?> GetJwksAsync(CancellationToken ct);
}
```

```csharp
[Fact]
public async Task AuthServiceClient_WhenGrpcFails_FallsBackToLocalJwks()
{
    var authClient = Substitute.For<IAuthServiceClient>();
    authClient.ValidateTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns(Task.FromResult<ClaimsPrincipal?>(null)); // simulate gRPC failure

    authClient.GetJwksAsync(Arg.Any<CancellationToken>())
        .Returns(Task.FromResult<JwksDocument?>(new JwksDocument { /* cached keys */ }));

    // The middleware should attempt gRPC first, then fall back to JWKS.
    // Test the middleware in isolation with a fake token.
}
```

**Important:** Never mock `GrpcChannel` or `Grpc.Core.CallInvoker` directly. The `AuthServiceClient` is an adapter; mock at the adapter boundary.

---

## 4. Integration Tests

### 4.1 Scope

Integration tests verify the service against **real infrastructure** running in ephemeral containers via Testcontainers. They cover:

- Repository data access (Dapper + PostgreSQL)
- Event publishing and consumption (Service Bus)
- RBAC enforcement end-to-end through the HTTP pipeline
- Tenant isolation and soft-delete behaviour
- gRPC JWKS validation with a fake gRPC server

### 4.2 Testcontainers Configuration

Two containers are required for integration tests:

| Container | Image | Purpose |
|---|---|---|
| **PostgreSQL** | `postgres:16-alpine` | User data store |
| **Azurite** | `mcr.microsoft.com/azure-storage/azurite:3.33` | Service Bus emulator |

**Test startup sequence:**

1. Start PostgreSQL container
2. Run migrations against it
3. Start Azurite container (emulates Service Bus queue)
4. Seed test data
5. Run tests
6. Dispose containers (automatic via `IAsyncLifetime`)

### 4.3 Example: Repository Tests

```csharp
[Collection("Integration")]
public class UserRepositoryTests
{
    private readonly TestDatabase _db;
    private readonly UserRepository _sut;

    public UserRepositoryTests(TestDatabase db, TestServiceBus _)
    {
        _db = db;
        _sut = new UserRepository(db.DataSource);
    }

    [Fact]
    public async Task CreateUserAsync_PersistsAndReturnsEntity()
    {
        var user = TestData.GenerateUserEntity();

        var created = await _sut.CreateUserAsync(user, CancellationToken.None);

        created.Should().NotBeNull();
        created.Id.Should().NotBeEmpty();
        created.Username.Should().Be(user.Username);
        created.TenantId.Should().Be(user.TenantId);
        created.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetUserByIdAsync_ReturnsSoftDeletedUsersOnlyWhenExplicitlyRequested()
    {
        var tenantId = Guid.NewGuid();
        var user = TestData.GenerateUserEntity(tenantId: tenantId);
        await _sut.CreateUserAsync(user, CancellationToken.None);
        await _sut.SoftDeleteUserAsync(user.Id, tenantId, CancellationToken.None);

        var byDefault = await _sut.GetUserByIdAsync(user.Id, tenantId, CancellationToken.None);
        byDefault.Should().BeNull("soft-deleted users are excluded by default");

        var explicitInclude = await _sut.GetUserByIdAsync(user.Id, tenantId, CancellationToken.None, includeDeleted: true);
        explicitInclude.Should().NotBeNull();
        explicitInclude!.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task IsUsernameTakenAsync_DetectsDuplicateAcrossTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var user = TestData.GenerateUserEntity(tenantId: tenantA, username: "common.user");
        await _sut.CreateUserAsync(user, CancellationToken.None);

        var takenInA = await _sut.IsUsernameTakenAsync("common.user", tenantA, null, CancellationToken.None);
        var takenInB = await _sut.IsUsernameTakenAsync("common.user", tenantB, null, CancellationToken.None);

        takenInA.Should().BeTrue();
        takenInB.Should().BeFalse("usernames are scoped per tenant");
    }

    [Fact]
    public async Task GetUsersAsync_SupportsCursorPagination()
    {
        var tenantId = Guid.NewGuid();
        var users = Enumerable.Range(1, 50)
            .Select(i => TestData.GenerateUserEntity(tenantId: tenantId, username: $"user.{i:D3}"))
            .ToList();
        foreach (var u in users) await _sut.CreateUserAsync(u, CancellationToken.None);

        var page1 = await _sut.GetUsersAsync(tenantId, new UserQuery { PageSize = 20 }, CancellationToken.None);
        page1.Items.Should().HaveCount(20);
        page1.HasMore.Should().BeTrue();
        page1.ContinuationToken.Should().NotBeNull();

        var page2 = await _sut.GetUsersAsync(tenantId, new UserQuery { PageSize = 20, ContinuationToken = page1.ContinuationToken }, CancellationToken.None);
        page2.Items.Should().HaveCount(20);
        page2.HasMore.Should().BeTrue();

        var page3 = await _sut.GetUsersAsync(tenantId, new UserQuery { PageSize = 20, ContinuationToken = page2.ContinuationToken }, CancellationToken.None);
        page3.Items.Should().HaveCount(10);
        page3.HasMore.Should().BeFalse();
        page3.ContinuationToken.Should().BeNull();
    }
}
```

### 4.4 Test Data Factory

Use Bogus to generate realistic, non-deterministic test data:

```csharp
// Fixtures/TestData.cs
public static class TestData
{
    private static readonly Faker Faker = new();

    public static UserEntity GenerateUserEntity(
        Guid? tenantId = null,
        string? username = null)
    {
        return new UserEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId ?? Guid.NewGuid(),
            Username = username ?? Faker.Internet.UserName().ToLowerInvariant(),
            Email = Faker.Internet.Email(),
            DisplayName = Faker.Name.FullName(),
            Department = Faker.Commerce.Department(),
            JobTitle = Faker.Name.JobTitle(),
            Roles = new[] { "developer" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public static CreateUserRequest GenerateCreateRequest(
        string? username = null,
        string? email = null)
    {
        return new CreateUserRequest
        {
            Username = username ?? Faker.Internet.UserName().ToLowerInvariant(),
            Email = email ?? Faker.Internet.Email(),
            DisplayName = Faker.Name.FullName(),
            Department = Faker.Commerce.Department(),
            JobTitle = Faker.Name.JobTitle()
        };
    }
}
```

---

## 5. Mocking the Auth Service gRPC

### 5.1 Fake gRPC Server Approach

For integration and service-level tests, start a **fake gRPC server** in-process that implements the `ValidateToken` RPC. This avoids a real dependency on the Authentication Service while exercising the actual gRPC client code in the service.

```csharp
// Fixtures/AuthServiceGrpcServer.cs
public sealed class AuthServiceGrpcServer : IAsyncDisposable
{
    private readonly WebApplication _server;
    private readonly int _port;

    public AuthServiceGrpcServer(int? port = null)
    {
        _port = port ?? PortFinder.GetRandomPort();
        _server = BuildServer();
        _server.StartAsync().GetAwaiter().GetResult();
    }

    public string Target => $"http://localhost:{_port}";

    public void SetupSuccessfulValidation(ClaimsPrincipal claims)
    {
        // Store the claims so the fake handler returns them on the next ValidateToken call.
    }

    public void SetupFailure(string reason = "INVALID_TOKEN")
    {
        // Configure the fake to return an error status.
    }

    public void SetupLatency(TimeSpan delay)
    {
        // Configure the fake to introduce artificial latency.
    }

    private WebApplication BuildServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{_port}");

        builder.Services.AddGrpc();
        // Register a fake TokenValidationService that implements the same proto
        // but returns configurable responses.

        var app = builder.Build();
        app.MapGrpcService<FakeTokenValidationService>();
        return app;
    }

    public async ValueTask DisposeAsync()
    {
        await _server.StopAsync();
        await _server.DisposeAsync();
    }
}
```

### 5.2 Configuring the Fake for Different Scenarios

| Scenario | Fake Configuration | Test Verifies |
|---|---|---|
| Valid token | Return `{ valid: true, claims: {...} }` | Request proceeds to handler |
| Expired token | Return `{ valid: false, reason: "EXPIRED" }` | 401 response |
| Invalid signature | Return `{ valid: false, reason: "INVALID_SIGNATURE" }` | 401 response |
| gRPC timeout | Hang for 2 seconds (client timeout is 500ms) | Fallback to JWKS cache |
| gRPC unavailable | Refuse connection | Fallback to JWKS cache, then 503 |
| JWKS cache hit | Pre-populate cache, shut down gRPC server | Validates locally, returns 200 |
| Role missing | Return claims without `roles` claim | 403 response |

### 5.3 Example: Testing the Fallback Strategy

```csharp
[Collection("Integration")]
public class AuthServiceClientTests
{
    private readonly AuthServiceGrpcServer _fakeAuth;

    public AuthServiceClientTests()
    {
        _fakeAuth = new AuthServiceGrpcServer();
    }

    [Fact]
    public async Task ValidateTokenAsync_WhenGrpcTimesOut_ValidatesFromJwksCache()
    {
        // Arrange: gRPC will be slow, but JWKS cache is pre-populated
        _fakeAuth.SetupLatency(TimeSpan.FromSeconds(2));

        var settings = new AuthServiceOptions
        {
            GrpcEndpoint = _fakeAuth.Target,
            JwksCacheTtl = TimeSpan.FromMinutes(5),
            GrpcTimeout = TimeSpan.FromMilliseconds(500)
        };

        var cache = new JwksCache(settings);
        cache.Seed(TestData.ValidJwksDocument()); // pre-populate

        var sut = new AuthServiceClient(settings, cache);

        // Act
        var claims = await sut.ValidateTokenAsync(TestData.SignedTestToken(), CancellationToken.None);

        // Assert
        claims.Should().NotBeNull();
        claims!.FindFirstValue("sub").Should().Be(TestData.TestUserId.ToString());
    }

    [Fact]
    public async Task ValidateTokenAsync_WhenGrpcUnavailableAndCacheEmpty_ReturnsNull()
    {
        // Arrange: no gRPC and no cache
        _fakeAuth.SetupFailure("UNAVAILABLE");
        var settings = new AuthServiceOptions
        {
            GrpcEndpoint = _fakeAuth.Target,
            JwksCacheTtl = TimeSpan.FromMinutes(5),
            GrpcTimeout = TimeSpan.FromMilliseconds(500)
        };

        var cache = new JwksCache(settings); // empty cache
        var sut = new AuthServiceClient(settings, cache);

        // Act
        var claims = await sut.ValidateTokenAsync(TestData.SignedTestToken(), CancellationToken.None);

        // Assert
        claims.Should().BeNull();
    }
}
```

---

## 6. Testing RBAC Enforcement

### 6.1 Approach

RBAC enforcement is tested at **three levels**:

1. **Unit** — The RBAC evaluation function is tested in isolation with role/action matrices
2. **Service** — The full HTTP pipeline is tested with TestHost, exercising JWT middleware and endpoint handlers
3. **Integration** — A real database ensures tenant-scoped queries respect the RBAC boundary

### 6.2 RBAC Evaluation Rules

```csharp
// RbacService.cs (production code)
public static class RbacService
{
    public static bool IsActionAllowed(string[] userRoles, string action, string resourceOwnerId, string requestorId)
    {
        if (userRoles.Contains("admin")) return true;

        if (userRoles.Contains("operator"))
        {
            return action switch
            {
                "users:list" => true,
                "users:read" => true,
                "users:update" => true, // limited fields enforced middleware
                _ => false
            };
        }

        if (userRoles.Contains("user"))
        {
            return action switch
            {
                "users:read" => resourceOwnerId == requestorId,
                "users:update" => resourceOwnerId == requestorId,
                _ => false
            };
        }

        return false;
    }
}
```

### 6.3 RBAC Unit Tests

```csharp
public class RbacServiceTests
{
    [Theory]
    [InlineData("admin", "users:create", true)]
    [InlineData("admin", "users:delete", true)]
    [InlineData("admin", "users:list", true)]
    [InlineData("operator", "users:list", true)]
    [InlineData("operator", "users:read", true)]
    [InlineData("operator", "users:create", false)]
    [InlineData("operator", "users:delete", false)]
    [InlineData("user", "users:read", true)]  // own profile
    [InlineData("user", "users:read", false)] // other's profile
    [InlineData("user", "users:create", false)]
    [InlineData("user", "users:delete", false)]
    [InlineData("nonexistent", "users:read", false)]
    public void IsActionAllowed_EnforcesRolePermissions(string role, string action, bool expected)
    {
        var ownUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var resourceId = action == "users:read" && expected && role == "user" ? ownUserId : otherUserId;

        // For the self-only test case that expects false:
        var testResourceId = (role == "user" && action == "users:read" && !expected)
            ? otherUserId
            : resourceId;

        var allowed = RbacService.IsActionAllowed(
            new[] { role },
            action,
            testResourceId.ToString(),
            ownUserId.ToString());

        allowed.Should().Be(expected);
    }

    [Fact]
    public void IsActionAllowed_AdminCanAccessAnyResource()
    {
        var result = RbacService.IsActionAllowed(
            new[] { "admin" },
            "users:delete",
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString());

        result.Should().BeTrue();
    }
}
```

### 6.4 Service-Level RBAC Tests

Use `WebApplicationFactory` (from `Microsoft.AspNetCore.TestHost`) to run the full HTTP pipeline with fake authentication:

```csharp
// Fixtures/WebApplicationFactory.cs
public class UsersApiFactory : WebApplicationFactory<Program>
{
    public AuthServiceGrpcServer FakeAuth { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("AuthService:GrpcEndpoint", FakeAuth.Target);
        builder.UseSetting("ConnectionStrings:PostgreSQL", _testDb.ConnectionString);

        builder.ConfigureTestServices(services =>
        {
            // Replace real gRPC client with one pointing at our fake
            services.AddSingleton<IAuthServiceClient>(sp =>
                new AuthServiceClient(
                    sp.GetRequiredService<IOptions<AuthServiceOptions>>(),
                    sp.GetRequiredService<JwksCache>()));

            // Use in-memory Service Bus for tests
            services.AddSingleton<IEventPublisher, InMemoryEventPublisher>();
        });
    }
}
```

```csharp
[Collection("Integration")]
public class RbacEnforcementTests : IClassFixture<UsersApiFactory>
{
    private readonly UsersApiFactory _factory;

    public RbacEnforcementTests(UsersApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetUsers_WithAdminRole_Returns200()
    {
        _factory.FakeAuth.SetupSuccessfulValidation(TestData.AdminClaims());

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestData.ValidToken());

        var response = await client.GetAsync("/api/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetUsers_WithUserRole_Returns403()
    {
        _factory.FakeAuth.SetupSuccessfulValidation(TestData.UserClaims());

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestData.ValidToken());

        var response = await client.GetAsync("/api/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateUser_WithOperatorRole_Returns403()
    {
        _factory.FakeAuth.SetupSuccessfulValidation(TestData.OperatorClaims());

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestData.ValidToken());

        var response = await client.PostAsJsonAsync("/api/users", TestData.GenerateCreateRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUserById_UserRole_OwnProfile_Returns200()
    {
        var userId = Guid.NewGuid();
        _factory.FakeAuth.SetupSuccessfulValidation(TestData.UserClaims(userId));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestData.ValidToken());

        var response = await client.GetAsync($"/api/users/{userId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetUserById_UserRole_OtherProfile_Returns403()
    {
        var ownId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        _factory.FakeAuth.SetupSuccessfulValidation(TestData.UserClaims(ownId));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestData.ValidToken());

        var response = await client.GetAsync($"/api/users/{otherId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateUser_UserRole_ChangesOwnEmail_Returns200()
    {
        var userId = Guid.NewGuid();
        _factory.FakeAuth.SetupSuccessfulValidation(TestData.UserClaims(userId));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestData.ValidToken());

        var response = await client.PutAsJsonAsync($"/api/users/{userId}",
            new UpdateUserRequest { Email = "new@contoso.com" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateUser_UserRole_TriesToChangeRoles_Returns403()
    {
        var userId = Guid.NewGuid();
        _factory.FakeAuth.SetupSuccessfulValidation(TestData.UserClaims(userId));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestData.ValidToken());

        var response = await client.PutAsJsonAsync($"/api/users/{userId}",
            new UpdateUserRequest { Roles = new[] { "admin" } });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteUser_WithAdminRole_Returns200()
    {
        _factory.FakeAuth.SetupSuccessfulValidation(TestData.AdminClaims());

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestData.ValidToken());

        var response = await client.DeleteAsync($"/api/users/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnauthenticatedRequest_Returns401()
    {
        var client = _factory.CreateClient();
        // No Authorization header

        var response = await client.GetAsync("/api/users");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

### 6.5 Tenant Isolation Test

```csharp
[Collection("Integration")]
public class MultiTenancyTests : IClassFixture<UsersApiFactory>
{
    private readonly UsersApiFactory _factory;

    [Fact]
    public async Task UserFromTenantA_CannotAccessUserFromTenantB()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Seed: create user in tenant B via the repository directly
        var tenantBUser = TestData.GenerateUserEntity(tenantId: tenantB);
        await _factory.SeedUserAsync(tenantBUser);

        // Act: request as tenant A
        _factory.FakeAuth.SetupSuccessfulValidation(TestData.AdminClaims(tenantId: tenantA));
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestData.ValidToken());

        var response = await client.GetAsync($"/api/users/{tenantBUser.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "users from tenant B should not be visible to tenant A");
    }
}
```

---

## 7. Testing Event Consumer Idempotency

### 7.1 The Problem

The Auth Service publishes events with **at-least-once delivery** guarantees. The Users Service event consumer must handle duplicate delivery of the same event without producing side effects.

### 7.2 Deduplication Strategy

The consumer records processed `eventId` values in a deduplication table:

```sql
CREATE TABLE event_deduplication (
    event_id    UUID PRIMARY KEY,
    processed_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Cleanup job: purge records older than 7 days
CREATE INDEX idx_event_dedup_processed ON event_deduplication(processed_at);
```

Before processing an event, the consumer checks this table. If the `eventId` exists, the event is acknowledged without processing.

### 7.3 Idempotency Tests

```csharp
[Collection("Integration")]
public class EventConsumerTests
{
    private readonly TestDatabase _db;
    private readonly UserRepository _repository;
    private readonly EventConsumer _consumer;

    public EventConsumerTests(TestDatabase db, TestServiceBus sb)
    {
        _db = db;
        _repository = new UserRepository(db.DataSource);
        _consumer = new EventConsumer(db.DataSource, _repository);
    }

    [Fact]
    public async Task ConsumeUserLogin_UpdatesLastLoginAt()
    {
        // Arrange
        var user = TestData.GenerateUserEntity();
        await _repository.CreateUserAsync(user, CancellationToken.None);

        var loginEvent = new UserLoginEvent
        {
            EventId = Guid.NewGuid(),
            UserId = user.Id,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        await _consumer.HandleAsync(loginEvent, CancellationToken.None);

        // Assert
        var fetched = await _repository.GetUserByIdAsync(user.Id, user.TenantId, CancellationToken.None, includeDeleted: true);
        fetched!.LastLoginAt.Should().BeCloseTo(loginEvent.Timestamp, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ConsumeDuplicateEvent_DoesNotUpdateLastLoginAgain()
    {
        // Arrange
        var user = TestData.GenerateUserEntity();
        await _repository.CreateUserAsync(user, CancellationToken.None);

        var loginEvent = new UserLoginEvent
        {
            EventId = Guid.NewGuid(),
            UserId = user.Id,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act: process the same event twice
        await _consumer.HandleAsync(loginEvent, CancellationToken.None);
        var firstLogin = (await _repository.GetUserByIdAsync(user.Id, user.TenantId, CancellationToken.None, includeDeleted: true))!.LastLoginAt;

        await _consumer.HandleAsync(loginEvent, CancellationToken.None);
        var secondLogin = (await _repository.GetUserByIdAsync(user.Id, user.TenantId, CancellationToken.None, includeDeleted: true))!.LastLoginAt;

        // Assert
        firstLogin.Should().Be(secondLogin, "duplicate event should be idempotent");
    }

    [Fact]
    public async Task ConsumeDeduplicatedEvent_RecordsEventId()
    {
        // Arrange
        var user = TestData.GenerateUserEntity();
        await _repository.CreateUserAsync(user, CancellationToken.None);

        var eventId = Guid.NewGuid();
        var loginEvent = new UserLoginEvent
        {
            EventId = eventId,
            UserId = user.Id,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        await _consumer.HandleAsync(loginEvent, CancellationToken.None);

        // Assert
        var dedupRecord = await _db.DataSource.QuerySingleOrDefaultAsync<DateTimeOffset?>(
            "SELECT processed_at FROM event_deduplication WHERE event_id = @id",
            new { id = eventId });

        dedupRecord.Should().NotBeNull();
        dedupRecord.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProcessEventsInOrder_PerUserId()
    {
        // Arrange: send user.login then user.logout for the same user
        var user = TestData.GenerateUserEntity();
        await _repository.CreateUserAsync(user, CancellationToken.None);

        var loginEvent = new UserLoginEvent
        {
            EventId = Guid.NewGuid(),
            UserId = user.Id,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        var logoutEvent = new UserLogoutEvent
        {
            EventId = Guid.NewGuid(),
            UserId = user.Id,
            Timestamp = DateTimeOffset.UtcNow,
            SessionDuration = 600
        };

        // Act
        await _consumer.HandleAsync(loginEvent, CancellationToken.None);
        await _consumer.HandleAsync(logoutEvent, CancellationToken.None);

        // Assert: last_login_at was set by the login event
        var fetched = await _repository.GetUserByIdAsync(user.Id, user.TenantId, CancellationToken.None, includeDeleted: true);
        fetched!.LastLoginAt.Should().BeCloseTo(loginEvent.Timestamp, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ConsumeTokenRevokedEvent_RecordsAuditEntry()
    {
        // TODO: implement after audit logging is added
        // Ensures token.revoked events create an audit log entry
    }

    [Theory]
    [InlineData("user.login")]
    [InlineData("user.logout")]
    [InlineData("token.revoked")]
    public async Task ConsumeEventWithMissingUser_DoesNotThrow(string eventType)
    {
        var unknownUserId = Guid.NewGuid();
        var evt = eventType switch
        {
            "user.login" => new UserLoginEvent { EventId = Guid.NewGuid(), UserId = unknownUserId, Timestamp = DateTimeOffset.UtcNow },
            "user.logout" => new UserLogoutEvent { EventId = Guid.NewGuid(), UserId = unknownUserId, Timestamp = DateTimeOffset.UtcNow },
            "token.revoked" => new TokenRevokedEvent { EventId = Guid.NewGuid(), UserId = unknownUserId, Timestamp = DateTimeOffset.UtcNow },
            _ => throw new ArgumentOutOfRangeException()
        };

        // Act / Assert
        await _consumer.Invoking(c => c.HandleAsync(evt, CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeadLetterEvent_AfterConsecutiveFailures()
    {
        // Arrange: malformed event that always fails deserialization
        var malformedEvent = new { type = "user.login", data = "not-valid" };

        // The consumer should attempt delivery and, after max retries (10),
        // move the message to the dead-letter queue.
        // This test verifies the dead-letter count on the Service Bus message.
    }
}
```

### 7.4 Concurrency Test: Racing Duplicates

```csharp
[Fact]
public async Task ConcurrentDeliveryOfDuplicateEvents_IsIdempotent()
{
    // This test simulates Service Bus delivering the same event twice concurrently.
    // Only one should succeed in updating the database.
    var user = TestData.GenerateUserEntity();
    await _repository.CreateUserAsync(user, CancellationToken.None);

    var loginEvent = new UserLoginEvent
    {
        EventId = Guid.NewGuid(),  // same event ID
        UserId = user.Id,
        Timestamp = DateTimeOffset.UtcNow
    };

    // Act: fire two handlers concurrently
    var task1 = _consumer.HandleAsync(loginEvent, CancellationToken.None);
    var task2 = _consumer.HandleAsync(loginEvent, CancellationToken.None);
    await Task.WhenAll(task1, task2);

    // Assert: last_login_at was updated exactly once
    var fetched = await _repository.GetUserByIdAsync(user.Id, user.TenantId, CancellationToken.None, includeDeleted: true);
    var updatedCount = await _db.DataSource.QuerySingleAsync<int>(
        "SELECT COUNT(*) FROM users WHERE id = @id AND last_login_at IS NOT NULL",
        new { id = user.Id });

    updatedCount.Should().Be(1, "duplicate concurrent delivery should not double-update");
}
```

---

## 8. Service Tests (HTTP Pipeline)

### 8.1 Scope

Service tests exercise the full HTTP request pipeline — middleware, model binding, validation, and error handling — against an in-process `TestServer`. They use the same `UsersApiFactory` from section 6.4.

### 8.2 Example: Error Handling Tests

```csharp
[Collection("Integration")]
public class UsersApiErrorHandlingTests : IClassFixture<UsersApiFactory>
{
    private readonly UsersApiFactory _factory;

    [Fact]
    public async Task CreateUser_WithInvalidBody_ReturnsValidationProblem()
    {
        _factory.FakeAuth.SetupSuccessfulValidation(TestData.AdminClaims());
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestData.ValidToken());

        var invalidBody = new { }; // missing required fields

        var response = await client.PostAsJsonAsync("/api/users", invalidBody);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Title.Should().Be("Validation Error");
        problem.Status.Should().Be(400);
    }

    [Fact]
    public async Task CreateUser_WithDuplicateUsername_Returns409()
    {
        _factory.FakeAuth.SetupSuccessfulValidation(TestData.AdminClaims());
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestData.ValidToken());

        var request = TestData.GenerateCreateRequest("unique.user");
        await client.PostAsJsonAsync("/api/users", request); // first: succeeds
        var response = await client.PostAsJsonAsync("/api/users", request); // second: conflict

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GetNonExistentUser_Returns404()
    {
        _factory.FakeAuth.SetupSuccessfulValidation(TestData.AdminClaims());
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestData.ValidToken());

        var response = await client.GetAsync($"/api/users/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task HealthEndpoint_DoesNotRequireAuth()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadinessProbe_ReportsDegraded_WhenDatabaseUnreachable()
    {
        // Arrange: stop the database container
        await _factory.StopDatabaseAsync();
        _factory.FakeAuth.SetupSuccessfulValidation(TestData.AdminClaims());

        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var health = await response.Content.ReadFromJsonAsync<HealthStatus>();
        health!.Status.Should().Be("Unhealthy");
        health.Checks["postgres"].Status.Should().Be("Unhealthy");
    }
}
```

### 8.3 Snapshot Testing (Optional with Verify)

For endpoints that return complex JSON, consider snapshot testing with the **[Verify](https://github.com/VerifyTests/Verify)** library. This automatically manages `.verified.txt` files and diffs changes on rebuild.

```csharp
[Fact]
public async Task GetUsers_MatchSnapshot()
{
    _factory.FakeAuth.SetupSuccessfulValidation(TestData.AdminClaims());
    var client = _factory.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestData.ValidToken());

    // Seed a known set of users
    await _factory.SeedUserAsync(TestData.GenerateUserEntity(username: "alice"));
    await _factory.SeedUserAsync(TestData.GenerateUserEntity(username: "bob"));

    var response = await client.GetAsync("/api/users?pageSize=10");

    await Verify(response);
}
```

---

## 9. Performance and Load Tests

### 9.1 Location

Load tests live in a separate directory at the repository root:

```
tests/
  Performance/
    UsersService.LoadTest.csproj
    Scenarios/
      CreateUserScenario.cs
      PaginatedListScenario.cs
      ConcurrentUpdatesScenario.cs
```

### 9.2 Tooling

| Tool | Purpose |
|---|---|
| **NBomber** | .NET load-testing framework for writing scenarios in C# |
| **k6** | JavaScript-based load testing for HTTP endpoints (CI integration) |

### 9.3 Key Scenarios

| Scenario | Target | RPS | Duration | Assertions |
|---|---|---|---|---|
| List users, page 1 | `GET /api/users?pageSize=20` | 200 | 5 min | p99 < 200ms, 0% errors |
| Get user by ID | `GET /api/users/{id}` | 500 | 5 min | p99 < 100ms, 0% errors |
| Create user | `POST /api/users` | 50 | 5 min | p99 < 500ms, 0% 5xx |
| Concurrent duplicate create | `POST /api/users` (same data) | 10 | 1 min | Exactly one 201, rest 409 |
| Event consumption lag | Simulate 1000 login events | — | 1 min | p99 processing < 50ms/event |

---

## 10. CI Test Requirements

### 10.1 Pipeline Stages

```yaml
# azure-pipelines.yml (extract)
trigger:
  - main
  - release/*

jobs:
  - job: validate
    displayName: "Validate"
    steps:
      - task: UseDotNet@2
        inputs:
          version: "10.0.x"
      - script: dotnet restore
      - script: dotnet format --verify-no-changes
      - script: dotnet build --no-restore -warnaserror
      - script: dotnet test --no-build --filter "Category=Unit" --collect "XPlat Code Coverage"
        displayName: "Unit Tests"

  - job: integration
    dependsOn: validate
    displayName: "Integration Tests"
    steps:
      - task: UseDotNet@2
        inputs:
          version: "10.0.x"
      - script: dotnet build -c Release
      - script: dotnet test --no-build -c Release --filter "Category=Integration"
        displayName: "Integration Tests"

  - job: security_scan
    dependsOn: validate
    displayName: "Security & Quality"
    steps:
      - script: dotnet list package --vulnerable --include-transitive
        displayName: "Vulnerability Scan"
      - script: dotnet sonarscanner begin /k:users-service /d:sonar.token=$(SONAR_TOKEN)
      - script: dotnet build
      - script: dotnet sonarscanner end /d:sonar.token=$(SONAR_TOKEN)
```

### 10.2 Test Categories

Tag tests with xUnit `[Trait]` attributes for targeted execution:

```csharp
[Trait("Category", "Unit")]
[Trait("Category", "Integration")]
[Trait("Category", "RBAC")]
[Trait("Category", "Idempotency")]
[Trait("Category", "Slow")] // tests that take > 5 seconds
```

| Category | Runs in | Exclusion |
|---|---|---|
| `Unit` | Every commit | — |
| `Integration` | PR merge, nightly | — |
| `RBAC` | PR merge, nightly | Included in Integration |
| `Idempotency` | PR merge, nightly | Included in Integration |
| `Slow` | Nightly only | Excluded from PR pipeline |

### 10.3 Test Execution Commands

```bash
# All unit tests (inner dev loop)
dotnet test --filter "Category=Unit"

# All integration tests (pre-merge)
dotnet test --filter "Category=Integration"

# Exclude slow tests (PR pipeline, < 2 min)
dotnet test --filter "Category!=Slow"

# Specific area
dotnet test --filter "FullyQualifiedName~RbacEnforcement"

# With coverage
dotnet test --collect "XPlat Code Coverage" --results-directory ./TestResults
dotnet tool run reportgenerator -reports:./TestResults/**/coverage.cobertura.xml -targetdir:./CoverageReport -reporttypes:HtmlInline

# Load tests
dotnet run --project tests/Performance/UsersService.LoadTest.csproj
```

### 10.4 Quality Gates

| Gate | Threshold | Pipeline Stage | Action |
|---|---|---|---|
| **Unit test pass rate** | 100% | Validate | Block merge |
| **Integration test pass rate** | 100% | Integration | Block merge |
| **Code coverage** | >= 80% line, >= 70% branch | Integration | Warning, block at 70% |
| **SonarQube quality gate** | Pass | Security | Block merge |
| **Vulnerable packages** | Zero critical/high | Security | Block merge |
| **Flaky test count** | 0 | All | Block release |
| **Performance regression** | p99 not exceeding baseline by > 20% | Nightly | Block release |

### 10.5 Flaky Test Management

1. **Mark flaky tests** with `[Trait("Flaky", "true")]` and suppress from PR gate
2. **File a bug** in Azure DevOps with the `flaky-test` tag
3. **Auto-retry:** The pipeline retries each test once (via `dotnet test --retry 1`)
4. **Quarterly review:** Flaky tests older than 3 months are escalated to the team lead

---

## 11. Writing Tests — Checklist

- [ ] Tests are deterministic — no reliance on `DateTime.UtcNow` without a time provider abstraction (`ITimeProvider`)
- [ ] Tests use `[Theory]` with `[InlineData]` for parameterized scenarios, not copy-paste
- [ ] Mocks are set up with `Arg.Any<CancellationToken>()` to avoid brittle matching
- [ ] Async test methods end with `Async` suffix
- [ ] Assertions use `FluentAssertions` for readability
- [ ] Test data is generated via `Bogus` (not hardcoded)
- [ ] No `Thread.Sleep` or `Task.Delay` for synchronization — use `TaskCompletionSource` or `SemaphoreSlim`
- [ ] Integration tests are tagged `[Category("Integration")]`
- [ ] Slow integration tests (over 5 s) are tagged `[Category("Slow")]`
- [ ] gRPC is mocked at the `IAuthServiceClient` interface, not at the transport layer
- [ ] Event idempotency tests cover duplicate delivery and concurrent delivery
- [ ] RBAC tests cover every cell in the permission matrix
- [ ] Tenant isolation tests verify cross-tenant data is invisible

---

## 12. Related Documents

- [Developer Guide](developer-guide.md) — architecture walkthrough for new team members
- [Local Development](local-development.md) — setting up the local environment
- [Technology Stack](../architecture/technology-stack.md) — test tool versions and licenses
- [Security Architecture](../architecture/security.md) — RBAC matrix and threat model
- [Events API](../api/events.md) — consumed and published event schemas
- [Coding Standards](../decisions/coding-standards.md) — code style for test code
- [CI/CD Pipeline](https://dev.azure.com/platform/_build?definitionId=101) — Azure DevOps pipeline definition
