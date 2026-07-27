# Guía de Pruebas — Users Service

## Alcance

Este documento define la estrategia, los estándares y las prácticas de pruebas para el Users Service. Cubre la pirámide completa de pruebas — desde pruebas unitarias rápidas hasta pruebas de integración de extremo a extremo — y aborda preocupaciones específicas del servicio como la simulación gRPC del Authentication Service, la verificación de la aplicación de RBAC y las pruebas de idempotencia del consumidor de eventos.

---

## 1. Filosofía de Pruebas

### 1.1 Principios

| Principio | Justificación |
|---|---|
| **Probar comportamiento, no implementación** | Las pruebas deben verificar resultados, no llamadas a métodos internos. Refactorizar la implementación no debería requerir reescribir las pruebas. |
| **Determinista por defecto** | Las pruebas deben producir el mismo resultado en cada ejecución. Sin pruebas inconsistentes. Cualquier no-determinismo (tiempo, aleatoriedad, condiciones de carrera asíncronas) debe estar explícitamente controlado. |
| **Retroalimentación rápida** | El conjunto de pruebas unitarias debe completarse en menos de 30 segundos. Las pruebas de integración se ejecutan en CI y antes de la fusión, pero no forman parte del bucle de desarrollo interno. |
| **Dependencias realistas** | Los servicios externos (PostgreSQL, Service Bus) se ejercitan a través de Testcontainers en las pruebas de integración, nunca simulados a ese nivel. |
| **Defensa en profundidad** | Cada límite de seguridad (validación JWT, RBAC, aislamiento de tenants) está cubierto en múltiples niveles de prueba. |

### 1.2 Niveles de Prueba

```
            /\
           /  \
          /    \
         / E2E \
        /--------\
       /  Servicio \
      /   Pruebas   \
     /--------------\
    / Integración    \
   /   Pruebas        \
  /--------------------\
 /  Pruebas Unitarias   \
/------------------------\
```

| Nivel | Velocidad | Alcance | Propósito |
|---|---|---|---|
| **Unitaria** | < 5 ms/prueba | Clase única aislada | Lógica de negocio, reglas de validación, mapeo, casos límite |
| **Integración** | < 5 s/prueba | Servicio + PostgreSQL real + gRPC ficticio | Consultas de repositorio, aplicación de RBAC, publicación de eventos |
| **Servicio** | < 30 s/prueba | Endpoint HTTP a través del middleware | Pipeline de solicitudes, middleware de autenticación, manejo de errores, flujos de extremo a extremo |
| **E2E** | < 5 min | Despliegue completo en entorno efímero | Puerta de CI — pruebas de humo contra un entorno de staging real |

---

## 2. Infraestructura de Pruebas

### 2.1 Estructura del Proyecto

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

### 2.2 SDK y Herramientas de Prueba

| Herramienta | Versión | Propósito |
|---|---|---|
| **xUnit** | 2.x | Framework de pruebas |
| **FluentAssertions** | 7.x | Afirmaciones legibles |
| **NSubstitute** | 5.x | Simulación y stubs |
| **Testcontainers** | 4.x | PostgreSQL efímero y emulador de Service Bus |
| **Microsoft.AspNetCore.TestHost** | 10.x | Servidor HTTP en proceso para pruebas de servicio |
| **Verify** | 26.x | Pruebas de instantáneas para respuestas JSON (opcional) |
| **Bogus** | 35.x | Generación realista de datos de prueba |

### 2.3 Configuración del Proyecto de Pruebas

El archivo `.csproj` del proyecto de pruebas debe incluir estas dependencias:

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

### 2.4 Fixtures de Colección

Todas las pruebas de integración comparten una base de datos y un emulador de Service Bus mediante fixtures de colección de xUnit. Esto evita iniciar un contenedor por prueba.

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
        // Ejecutar todos los scripts SQL de migración contra la base de datos de prueba.
        // Consultar docs/runbooks/deployment.md para el inventario de migraciones.
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
// Definir una colección que agrupa las pruebas de integración que comparten los mismos fixtures.
[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<TestDatabase>, ICollectionFixture<TestServiceBus>
{
}
```

---

## 3. Pruebas Unitarias

### 3.1 Alcance

Las pruebas unitarias cubren **lógica de negocio pura** y **reglas de dominio** de forma aislada. Todas las dependencias externas (repositorios, clientes gRPC, Service Bus) se simulan.

**Qué probar unitariamente:**

- `ProfileValidator` — formato de nombre de usuario, formato de correo electrónico, lógica de unicidad, validez de roles
- `UserService` — lógica de orquestación, manejo de errores, mapeo
- `UserEntity.ToDto()` — mapeo de entidad a DTO
- Comportamiento de respaldo de `AuthServiceClient` (cuando la caché JWKS es válida)
- Evaluación de reglas RBAC (sin middleware)

**Qué NO probar unitariamente:**

- Consultas a la base de datos (cubiertas por pruebas de integración)
- Serialización y enrutamiento HTTP (cubiertos por pruebas de servicio)
- Protocolo de transmisión gRPC (cubierto por pruebas de integración con un servidor ficticio)

### 3.2 Ejemplo: Pruebas de ProfileValidator

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
    [InlineData("a", false)]                 // demasiado corto
    [InlineData("John.Doe", false)]           // mayúsculas no permitidas
    [InlineData("john doe", false)]           // espacio no permitido
    [InlineData("john@doe", false)]           // @ no permitido
    [InlineData("a_b.c-d", true)]            // guiones bajos, puntos, guiones permitidos
    [InlineData("", false)]                   // vacío
    [InlineData(null, false)]                 // nulo
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

### 3.3 Ejemplo: Pruebas de UserService

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
            .Returns(ValidationResult.FromErrors(new[] { new ValidationError("Username", "INVALID_USERNAME", "Demasiado corto") }));

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

### 3.4 Simulación del Cliente gRPC del Auth Service

El `AuthServiceClient` implementa `IAuthServiceClient`. En las pruebas unitarias, simula la **interfaz**, no el canal gRPC.

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
        .Returns(Task.FromResult<ClaimsPrincipal?>(null)); // simular fallo gRPC

    authClient.GetJwksAsync(Arg.Any<CancellationToken>())
        .Returns(Task.FromResult<JwksDocument?>(new JwksDocument { /* claves en caché */ }));

    // El middleware debería intentar gRPC primero, luego recurrir a JWKS.
    // Probar el middleware de forma aislada con un token ficticio.
}
```

**Importante:** Nunca simules `GrpcChannel` o `Grpc.Core.CallInvoker` directamente. El `AuthServiceClient` es un adaptador; simula en el límite del adaptador.

---

## 4. Pruebas de Integración

### 4.1 Alcance

Las pruebas de integración verifican el servicio contra **infraestructura real** ejecutándose en contenedores efímeros mediante Testcontainers. Cubren:

- Acceso a datos del repositorio (Dapper + PostgreSQL)
- Publicación y consumo de eventos (Service Bus)
- Aplicación de RBAC de extremo a extremo a través del pipeline HTTP
- Aislamiento de tenants y comportamiento de borrado lógico
- Validación gRPC JWKS con un servidor gRPC ficticio

### 4.2 Configuración de Testcontainers

Se requieren dos contenedores para las pruebas de integración:

| Contenedor | Imagen | Propósito |
|---|---|---|
| **PostgreSQL** | `postgres:16-alpine` | Almacén de datos de usuario |
| **Azurite** | `mcr.microsoft.com/azure-storage/azurite:3.33` | Emulador de Service Bus |

**Secuencia de inicio de pruebas:**

1. Iniciar contenedor PostgreSQL
2. Ejecutar migraciones contra él
3. Iniciar contenedor Azurite (emula la cola de Service Bus)
4. Sembrar datos de prueba
5. Ejecutar pruebas
6. Desechar contenedores (automático mediante `IAsyncLifetime`)

### 4.3 Ejemplo: Pruebas de Repositorio

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
        byDefault.Should().BeNull("los usuarios eliminados lógicamente se excluyen por defecto");

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
        takenInB.Should().BeFalse("los nombres de usuario tienen alcance por tenant");
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

### 4.4 Fábrica de Datos de Prueba

Usa Bogus para generar datos de prueba realistas y no deterministas:

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

## 5. Simulación del gRPC del Auth Service

### 5.1 Enfoque de Servidor gRPC Ficticio

Para las pruebas de integración y a nivel de servicio, inicia un **servidor gRPC ficticio** en proceso que implemente la RPC `ValidateToken`. Esto evita una dependencia real del Authentication Service mientras se ejercita el código real del cliente gRPC en el servicio.

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
        // Almacenar las claims para que el manejador ficticio las devuelva en la próxima llamada ValidateToken.
    }

    public void SetupFailure(string reason = "INVALID_TOKEN")
    {
        // Configurar el ficticio para que devuelva un estado de error.
    }

    public void SetupLatency(TimeSpan delay)
    {
        // Configurar el ficticio para que introduzca latencia artificial.
    }

    private WebApplication BuildServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{_port}");

        builder.Services.AddGrpc();
        // Registrar un FakeTokenValidationService que implemente el mismo proto
        // pero devuelva respuestas configurables.

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

### 5.2 Configuración del Ficticio para Diferentes Escenarios

| Escenario | Configuración del Ficticio | La Prueba Verifica |
|---|---|---|
| Token válido | Devolver `{ valid: true, claims: {...} }` | La solicitud procede al manejador |
| Token expirado | Devolver `{ valid: false, reason: "EXPIRED" }` | Respuesta 401 |
| Firma inválida | Devolver `{ valid: false, reason: "INVALID_SIGNATURE" }` | Respuesta 401 |
| Timeout gRPC | Esperar 2 segundos (el timeout del cliente es 500ms) | Recurso a la caché JWKS |
| gRPC no disponible | Rechazar conexión | Recurso a la caché JWKS, luego 503 |
| Acierto en caché JWKS | Precargar caché, apagar servidor gRPC | Valida localmente, devuelve 200 |
| Rol faltante | Devolver claims sin la claim `roles` | Respuesta 403 |

### 5.3 Ejemplo: Prueba de la Estrategia de Respaldo

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
        // Arrange: gRPC será lento, pero la caché JWKS está precargada
        _fakeAuth.SetupLatency(TimeSpan.FromSeconds(2));

        var settings = new AuthServiceOptions
        {
            GrpcEndpoint = _fakeAuth.Target,
            JwksCacheTtl = TimeSpan.FromMinutes(5),
            GrpcTimeout = TimeSpan.FromMilliseconds(500)
        };

        var cache = new JwksCache(settings);
        cache.Seed(TestData.ValidJwksDocument()); // precargar

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
        // Arrange: sin gRPC y sin caché
        _fakeAuth.SetupFailure("UNAVAILABLE");
        var settings = new AuthServiceOptions
        {
            GrpcEndpoint = _fakeAuth.Target,
            JwksCacheTtl = TimeSpan.FromMinutes(5),
            GrpcTimeout = TimeSpan.FromMilliseconds(500)
        };

        var cache = new JwksCache(settings); // caché vacía
        var sut = new AuthServiceClient(settings, cache);

        // Act
        var claims = await sut.ValidateTokenAsync(TestData.SignedTestToken(), CancellationToken.None);

        // Assert
        claims.Should().BeNull();
    }
}
```

---

## 6. Pruebas de Aplicación de RBAC

### 6.1 Enfoque

La aplicación de RBAC se prueba en **tres niveles**:

1. **Unitaria** — La función de evaluación RBAC se prueba de forma aislada con matrices de rol/acción
2. **Servicio** — El pipeline HTTP completo se prueba con TestHost, ejercitando el middleware JWT y los manejadores de endpoints
3. **Integración** — Una base de datos real asegura que las consultas con alcance de tenant respeten el límite RBAC

### 6.2 Reglas de Evaluación RBAC

```csharp
// RbacService.cs (código de producción)
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
                "users:update" => true, // campos limitados aplicados por middleware
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

### 6.3 Pruebas Unitarias de RBAC

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
    [InlineData("user", "users:read", true)]  // perfil propio
    [InlineData("user", "users:read", false)] // perfil de otro
    [InlineData("user", "users:create", false)]
    [InlineData("user", "users:delete", false)]
    [InlineData("nonexistent", "users:read", false)]
    public void IsActionAllowed_EnforcesRolePermissions(string role, string action, bool expected)
    {
        var ownUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var resourceId = action == "users:read" && expected && role == "user" ? ownUserId : otherUserId;

        // Para el caso de prueba de solo propio que espera false:
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

### 6.4 Pruebas RBAC a Nivel de Servicio

Usa `WebApplicationFactory` (de `Microsoft.AspNetCore.TestHost`) para ejecutar el pipeline HTTP completo con autenticación ficticia:

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
            // Reemplazar el cliente gRPC real por uno que apunte a nuestro ficticio
            services.AddSingleton<IAuthServiceClient>(sp =>
                new AuthServiceClient(
                    sp.GetRequiredService<IOptions<AuthServiceOptions>>(),
                    sp.GetRequiredService<JwksCache>()));

            // Usar Service Bus en memoria para pruebas
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
        // Sin cabecera Authorization

        var response = await client.GetAsync("/api/users");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

### 6.5 Prueba de Aislamiento de Tenants

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

        // Sembrar: crear usuario en el tenant B directamente a través del repositorio
        var tenantBUser = TestData.GenerateUserEntity(tenantId: tenantB);
        await _factory.SeedUserAsync(tenantBUser);

        // Act: solicitar como tenant A
        _factory.FakeAuth.SetupSuccessfulValidation(TestData.AdminClaims(tenantId: tenantA));
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestData.ValidToken());

        var response = await client.GetAsync($"/api/users/{tenantBUser.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "los usuarios del tenant B no deberían ser visibles para el tenant A");
    }
}
```

---

## 7. Pruebas de Idempotencia del Consumidor de Eventos

### 7.1 El Problema

El Auth Service publica eventos con garantías de **entrega al menos una vez**. El consumidor de eventos del Users Service debe manejar la entrega duplicada del mismo evento sin producir efectos secundarios.

### 7.2 Estrategia de Deduplicación

El consumidor registra los valores de `eventId` procesados en una tabla de deduplicación:

```sql
CREATE TABLE event_deduplication (
    event_id    UUID PRIMARY KEY,
    processed_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Trabajo de limpieza: purgar registros con más de 7 días
CREATE INDEX idx_event_dedup_processed ON event_deduplication(processed_at);
```

Antes de procesar un evento, el consumidor verifica esta tabla. Si el `eventId` existe, el evento se reconoce sin procesamiento.

### 7.3 Pruebas de Idempotencia

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

        // Act: procesar el mismo evento dos veces
        await _consumer.HandleAsync(loginEvent, CancellationToken.None);
        var firstLogin = (await _repository.GetUserByIdAsync(user.Id, user.TenantId, CancellationToken.None, includeDeleted: true))!.LastLoginAt;

        await _consumer.HandleAsync(loginEvent, CancellationToken.None);
        var secondLogin = (await _repository.GetUserByIdAsync(user.Id, user.TenantId, CancellationToken.None, includeDeleted: true))!.LastLoginAt;

        // Assert
        firstLogin.Should().Be(secondLogin, "el evento duplicado debe ser idempotente");
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
        // Arrange: enviar user.login luego user.logout para el mismo usuario
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

        // Assert: last_login_at fue establecido por el evento de inicio de sesión
        var fetched = await _repository.GetUserByIdAsync(user.Id, user.TenantId, CancellationToken.None, includeDeleted: true);
        fetched!.LastLoginAt.Should().BeCloseTo(loginEvent.Timestamp, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ConsumeTokenRevokedEvent_RecordsAuditEntry()
    {
        // TODO: implementar después de que se agregue el registro de auditoría
        // Asegura que los eventos token.revoked creen una entrada de registro de auditoría
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
        // Arrange: evento mal formado que siempre falla la deserialización
        var malformedEvent = new { type = "user.login", data = "not-valid" };

        // El consumidor debería intentar la entrega y, después de los reintentos máximos (10),
        // mover el mensaje a la cola de mensajes fallidos.
        // Esta prueba verifica el conteo de mensajes fallidos en el mensaje de Service Bus.
    }
}
```

### 7.4 Prueba de Concurrencia: Duplicados en Competencia

```csharp
[Fact]
public async Task ConcurrentDeliveryOfDuplicateEvents_IsIdempotent()
{
    // Esta prueba simula que Service Bus entrega el mismo evento dos veces de forma concurrente.
    // Solo uno debería tener éxito en actualizar la base de datos.
    var user = TestData.GenerateUserEntity();
    await _repository.CreateUserAsync(user, CancellationToken.None);

    var loginEvent = new UserLoginEvent
    {
        EventId = Guid.NewGuid(),  // mismo ID de evento
        UserId = user.Id,
        Timestamp = DateTimeOffset.UtcNow
    };

    // Act: ejecutar dos manejadores concurrentemente
    var task1 = _consumer.HandleAsync(loginEvent, CancellationToken.None);
    var task2 = _consumer.HandleAsync(loginEvent, CancellationToken.None);
    await Task.WhenAll(task1, task2);

    // Assert: last_login_at se actualizó exactamente una vez
    var fetched = await _repository.GetUserByIdAsync(user.Id, user.TenantId, CancellationToken.None, includeDeleted: true);
    var updatedCount = await _db.DataSource.QuerySingleAsync<int>(
        "SELECT COUNT(*) FROM users WHERE id = @id AND last_login_at IS NOT NULL",
        new { id = user.Id });

    updatedCount.Should().Be(1, "la entrega concurrente duplicada no debería actualizar dos veces");
}
```

---

## 8. Pruebas de Servicio (Pipeline HTTP)

### 8.1 Alcance

Las pruebas de servicio ejercitan el pipeline completo de solicitudes HTTP — middleware, enlace de modelos, validación y manejo de errores — contra un `TestServer` en proceso. Usan el mismo `UsersApiFactory` de la sección 6.4.

### 8.2 Ejemplo: Pruebas de Manejo de Errores

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

        var invalidBody = new { }; // campos requeridos faltantes

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
        await client.PostAsJsonAsync("/api/users", request); // primero: éxito
        var response = await client.PostAsJsonAsync("/api/users", request); // segundo: conflicto

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
        // Arrange: detener el contenedor de la base de datos
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

### 8.3 Pruebas de Instantáneas (Opcional con Verify)

Para endpoints que devuelven JSON complejo, considera las pruebas de instantáneas con la librería **[Verify](https://github.com/VerifyTests/Verify)**. Esto gestiona automáticamente los archivos `.verified.txt` y muestra las diferencias al reconstruir.

```csharp
[Fact]
public async Task GetUsers_MatchSnapshot()
{
    _factory.FakeAuth.SetupSuccessfulValidation(TestData.AdminClaims());
    var client = _factory.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestData.ValidToken());

    // Sembrar un conjunto conocido de usuarios
    await _factory.SeedUserAsync(TestData.GenerateUserEntity(username: "alice"));
    await _factory.SeedUserAsync(TestData.GenerateUserEntity(username: "bob"));

    var response = await client.GetAsync("/api/users?pageSize=10");

    await Verify(response);
}
```

---

## 9. Pruebas de Rendimiento y Carga

### 9.1 Ubicación

Las pruebas de carga residen en un directorio separado en la raíz del repositorio:

```
tests/
  Performance/
    UsersService.LoadTest.csproj
    Scenarios/
      CreateUserScenario.cs
      PaginatedListScenario.cs
      ConcurrentUpdatesScenario.cs
```

### 9.2 Herramientas

| Herramienta | Propósito |
|---|---|
| **NBomber** | Framework de pruebas de carga .NET para escribir escenarios en C# |
| **k6** | Pruebas de carga basadas en JavaScript para endpoints HTTP (integración CI) |

### 9.3 Escenarios Clave

| Escenario | Objetivo | RPS | Duración | Afirmaciones |
|---|---|---|---|---|
| Listar usuarios, página 1 | `GET /api/users?pageSize=20` | 200 | 5 min | p99 < 200ms, 0% errores |
| Obtener usuario por ID | `GET /api/users/{id}` | 500 | 5 min | p99 < 100ms, 0% errores |
| Crear usuario | `POST /api/users` | 50 | 5 min | p99 < 500ms, 0% 5xx |
| Creación duplicada concurrente | `POST /api/users` (mismos datos) | 10 | 1 min | Exactamente un 201, resto 409 |
| Retraso en consumo de eventos | Simular 1000 eventos de inicio de sesión | — | 1 min | p99 procesamiento < 50ms/evento |

---

## 10. Requisitos de Pruebas en CI

### 10.1 Etapas del Pipeline

```yaml
# azure-pipelines.yml (extracto)
trigger:
  - main
  - release/*

jobs:
  - job: validate
    displayName: "Validar"
    steps:
      - task: UseDotNet@2
        inputs:
          version: "10.0.x"
      - script: dotnet restore
      - script: dotnet format --verify-no-changes
      - script: dotnet build --no-restore -warnaserror
      - script: dotnet test --no-build --filter "Category=Unit" --collect "XPlat Code Coverage"
        displayName: "Pruebas Unitarias"

  - job: integration
    dependsOn: validate
    displayName: "Pruebas de Integración"
    steps:
      - task: UseDotNet@2
        inputs:
          version: "10.0.x"
      - script: dotnet build -c Release
      - script: dotnet test --no-build -c Release --filter "Category=Integration"
        displayName: "Pruebas de Integración"

  - job: security_scan
    dependsOn: validate
    displayName: "Seguridad y Calidad"
    steps:
      - script: dotnet list package --vulnerable --include-transitive
        displayName: "Escaneo de Vulnerabilidades"
      - script: dotnet sonarscanner begin /k:users-service /d:sonar.token=$(SONAR_TOKEN)
      - script: dotnet build
      - script: dotnet sonarscanner end /d:sonar.token=$(SONAR_TOKEN)
```

### 10.2 Categorías de Pruebas

Etiqueta las pruebas con atributos `[Trait]` de xUnit para ejecución selectiva:

```csharp
[Trait("Category", "Unit")]
[Trait("Category", "Integration")]
[Trait("Category", "RBAC")]
[Trait("Category", "Idempotency")]
[Trait("Category", "Slow")] // pruebas que toman > 5 segundos
```

| Categoría | Se Ejecuta En | Exclusión |
|---|---|---|
| `Unit` | Cada commit | — |
| `Integration` | Fusión PR, nocturno | — |
| `RBAC` | Fusión PR, nocturno | Incluida en Integration |
| `Idempotency` | Fusión PR, nocturno | Incluida en Integration |
| `Slow` | Solo nocturno | Excluida del pipeline PR |

### 10.3 Comandos de Ejecución de Pruebas

```bash
# Todas las pruebas unitarias (bucle de desarrollo interno)
dotnet test --filter "Category=Unit"

# Todas las pruebas de integración (pre-fusión)
dotnet test --filter "Category=Integration"

# Excluir pruebas lentas (pipeline PR, < 2 min)
dotnet test --filter "Category!=Slow"

# Área específica
dotnet test --filter "FullyQualifiedName~RbacEnforcement"

# Con cobertura
dotnet test --collect "XPlat Code Coverage" --results-directory ./TestResults
dotnet tool run reportgenerator -reports:./TestResults/**/coverage.cobertura.xml -targetdir:./CoverageReport -reporttypes:HtmlInline

# Pruebas de carga
dotnet run --project tests/Performance/UsersService.LoadTest.csproj
```

### 10.4 Puertas de Calidad

| Puerta | Umbral | Etapa del Pipeline | Acción |
|---|---|---|---|
| **Tasa de aprobación de pruebas unitarias** | 100% | Validar | Bloquear fusión |
| **Tasa de aprobación de pruebas de integración** | 100% | Integration | Bloquear fusión |
| **Cobertura de código** | >= 80% línea, >= 70% rama | Integration | Advertencia, bloquear al 70% |
| **Puerta de calidad SonarQube** | Aprobado | Security | Bloquear fusión |
| **Paquetes vulnerables** | Cero críticos/altos | Security | Bloquear fusión |
| **Conteo de pruebas inconsistentes** | 0 | Todas | Bloquear lanzamiento |
| **Regresión de rendimiento** | p99 que no exceda la línea base en > 20% | Nocturno | Bloquear lanzamiento |

### 10.5 Gestión de Pruebas Inconsistentes

1. **Marcar pruebas inconsistentes** con `[Trait("Flaky", "true")]` y suprimir de la puerta PR
2. **Registrar un bug** en Azure DevOps con la etiqueta `flaky-test`
3. **Reintento automático:** El pipeline reintenta cada prueba una vez (mediante `dotnet test --retry 1`)
4. **Revisión trimestral:** Las pruebas inconsistentes con más de 3 meses se escalan al líder del equipo

---

## 11. Lista de Verificación para Escribir Pruebas

- [ ] Las pruebas son deterministas — sin dependencia de `DateTime.UtcNow` sin una abstracción de proveedor de tiempo (`ITimeProvider`)
- [ ] Las pruebas usan `[Theory]` con `[InlineData]` para escenarios parametrizados, no copiar y pegar
- [ ] Los simulacros se configuran con `Arg.Any<CancellationToken>()` para evitar coincidencias frágiles
- [ ] Los métodos de prueba asíncronos terminan con el sufijo `Async`
- [ ] Las afirmaciones usan `FluentAssertions` para legibilidad
- [ ] Los datos de prueba se generan mediante `Bogus` (no hardcodeados)
- [ ] Sin `Thread.Sleep` o `Task.Delay` para sincronización — usar `TaskCompletionSource` o `SemaphoreSlim`
- [ ] Las pruebas de integración están etiquetadas `[Category("Integration")]`
- [ ] Las pruebas de integración lentas (más de 5 s) están etiquetadas `[Category("Slow")]`
- [ ] gRPC se simula en la interfaz `IAuthServiceClient`, no en la capa de transporte
- [ ] Las pruebas de idempotencia de eventos cubren entrega duplicada y entrega concurrente
- [ ] Las pruebas RBAC cubren cada celda de la matriz de permisos
- [ ] Las pruebas de aislamiento de tenants verifican que los datos entre tenants sean invisibles

---

## 12. Documentos Relacionados

- [Guía del Desarrollador](developer-guide.md) -- recorrido arquitectónico para nuevos miembros del equipo
- [Desarrollo Local](local-development.md) -- configuración del entorno local
- [Stack Tecnológico](../architecture/technology-stack.md) -- versiones de herramientas de prueba y licencias
- [Arquitectura de Seguridad](../architecture/security.md) -- matriz RBAC y modelo de amenazas
- [API de Eventos](../api/events.md) -- esquemas de eventos consumidos y publicados
- [Estándares de Codificación](../decisions/coding-standards.md) -- estilo de código para código de prueba
- [Pipeline CI/CD](https://dev.azure.com/platform/_build?definitionId=101) -- definición del pipeline de Azure DevOps
