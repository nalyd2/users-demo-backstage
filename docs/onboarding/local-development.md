# Local Development Guide

This guide walks through setting up a local development environment for the Users Service, including its companion Authentication Service and the infrastructure needed for integration tests.

---

## Table of Contents

- [Prerequisites](#prerequisites)
- [Repository Layout](#repository-layout)
- [Setting Up the Environment](#setting-up-the-environment)
- [Running the Auth Service (JWT Issuer Mock)](#running-the-auth-service-jwt-issuer-mock)
- [Running the Users Service](#running-the-users-service)
- [JWT Validation Stub for Local Development](#jwt-validation-stub-for-local-development)
- [End-to-End Test Flow](#end-to-end-test-flow)
- [Using Testcontainers for PostgreSQL](#using-testcontainers-for-postgresql)
- [Configuration Reference](#configuration-reference)
- [Troubleshooting](#troubleshooting)

---

## Prerequisites

| Tool | Minimum Version | Purpose |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0 | Build and run both services |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | 24+ | PostgreSQL container via Testcontainers |
| [Git](https://git-scm.com/) | Latest | Source control |
| An IDE (VS Code, Rider, Visual Studio) | Any | Editing and debugging |

**Verify the installation:**

```bash
dotnet --version
# Expected: 10.0.x

docker --version
# Expected: Docker version 24.x or later

git --version
```

> The service targets `net10.0`. If you are on an earlier SDK version, install the .NET 10 SDK from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0).

---

## Repository Layout

The Users Service and the Auth Service live in separate repositories. Both must be cloned for a full local setup.

```
C:\Efra-proyects\
├── users-demo-backstage\          # Users Service (this repo)
│   ├── src\UsersService\          # .NET 10 web application
│   │   ├── Controllers\           # Minimal API endpoint definitions
│   │   ├── Models\                # DTOs and entities
│   │   ├── Services\              # Business logic (IUserService / UserService)
│   │   ├── Program.cs             # Application entry point
│   │   └── appsettings*.json      # Configuration
│   ├── tests\                     # Unit & integration tests
│   ├── docs\                      # TechDocs documentation
│   └── openapi.yaml               # OpenAPI 3.1 specification
│
└── authenthication-demo-backstage\ # Auth Service (companion, separate repo)
    └── src\AuthService\           # .NET 10 web application
```

---

## Setting Up the Environment

Clone both repositories:

```bash
# From your workspace root
git clone <users-service-repo-url> users-demo-backstage
git clone <auth-service-repo-url> authenthication-demo-backstage
```

> If you have already cloned the repos, skip this step and ensure both are on the `main` branch with the latest changes.

Restore dependencies for both projects:

```bash
dotnet restore c:/Efra-proyects/users-demo-backstage/src/UsersService/UsersService.csproj
dotnet restore c:/Efra-proyects/authenthication-demo-backstage/src/AuthService/AuthService.csproj
```

Build both to confirm no compilation errors:

```bash
dotnet build c:/Efra-proyects/users-demo-backstage/src/UsersService/UsersService.csproj --no-restore
dotnet build c:/Efra-proyects/authenthication-demo-backstage/src/AuthService/AuthService.csproj --no-restore
```

---

## Running the Auth Service (JWT Issuer Mock)

The Users Service does not issue its own tokens. Every authenticated request requires a JWT issued by the Authentication Service. For local development you **run the real Auth Service** as a sidecar process. It operates in a self-contained mode that does not require PostgreSQL, Redis, or any other external dependency.

### Starting the Auth Service

```bash
dotnet run --project c:/Efra-proyects/authenthication-demo-backstage/src/AuthService/AuthService.csproj
```

### What the Auth Service Does in Development Mode

| Aspect | Behavior |
|---|---|
| **Port** | `https://localhost:7103` (configured in `appsettings.Development.json`) |
| **Key generation** | An ephemeral 2048-bit RSA key pair is generated on startup. The private key lives only in memory and is discarded when the process exits. |
| **Demo credentials** | Username: `admin`, Password: `Platform@2026!` |
| **Token lifetime** | Access tokens: 60 minutes. Refresh tokens: 30 days. |
| **Data storage** | All state (refresh tokens, revoked JTIs) is in-memory. Restarting the service clears everything. |
| **OIDC Discovery** | `https://localhost:7103/.well-known/openid-configuration` |
| **JWKS Endpoint** | `https://localhost:7103/.well-known/jwks.json` |

### Verifying the Auth Service Is Running

```bash
curl -k https://localhost:7103/api/health/live
# Expected: {"status":"Healthy","timestamp":"..."}
```

The Swagger UI is available at `https://localhost:7103/swagger`.

---

## Running the Users Service

### Starting the Service

```bash
dotnet run --project c:/Efra-proyects/users-demo-backstage/src/UsersService/UsersService.csproj
```

### Behavior in Development Mode

| Aspect | Behavior |
|---|---|
| **Port** | `https://localhost:7201` |
| **Data store** | An in-memory `List<UserEntity>` with two seeded demo users (admin and jane.dev). No database is required. |
| **JWT validation** | Signature validation is **disabled** (see [JWT Validation Stub](#jwt-validation-stub-for-local-development)). |
| **Auth Service dependency** | The service does **not** call the Auth Service at runtime in development. Access tokens from the Auth Service are validated locally using the configured issuer and audience values only. |
| **Swagger UI** | `https://localhost:7201/swagger` |

### Verifying the Users Service Is Running

```bash
curl -k https://localhost:7201/api/health/live
# Expected: {"status":"Healthy","timestamp":"..."}
```

---

## JWT Validation Stub for Local Development

In production, the Users Service validates every JWT by:

1. Fetching the JWKS document from the Auth Service's `/.well-known/jwks.json` endpoint.
2. Using the RSA public key from the JWKS to verify the token's RS256 signature.
3. Checking issuer, audience, expiry, and the JWT ID against a blacklist.

In development, signature validation is **disabled**. This is controlled in `Program.cs`:

```csharp
options.TokenValidationParameters = new()
{
    ValidateIssuer = true,
    ValidIssuer = issuer,
    ValidateAudience = true,
    ValidAudience = audience,
    ValidateLifetime = true,
    ValidateIssuerSigningKey = false,  // <-- stub: no signature check
    ClockSkew = TimeSpan.FromSeconds(30)
};
```

### What This Means

| Setting | Production | Development |
|---|---|---|
| `ValidateIssuerSigningKey` | `true` — validates against JWKS | `false` — **skipped** |
| `RequireHttpsMetadata` | `true` | `false` (allows `http` metadata URLs) |
| Signature verification | RSA-256 against Auth Service public key | **None** — any JWT with matching issuer/audience is accepted |

This approach lets you develop and test without running a full JWKS infrastructure. The **issuer, audience, and lifetime claims are still enforced**, so tokens must be structurally valid and not expired.

### Obtaining a JWT for Manual Testing

Use the Auth Service's `/api/auth/login` endpoint to get a token that works against the Users Service's development configuration:

```bash
curl -k -X POST https://localhost:7103/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Platform@2026!"}'
```

**Response:**

```json
{
  "accessToken": "eyJhbGciOiJSUzI1NiIsIm...",
  "tokenType": "Bearer",
  "expiresIn": 3600,
  "refreshToken": "AbCdEf..."
}
```

Copy the `accessToken` value and use it against Users Service endpoints:

```bash
curl -k https://localhost:7201/api/users \
  -H "Authorization: Bearer eyJhbGciOiJSUzI1NiIsIm..."
```

### Important Caveat

Because signature validation is disabled, **any JWT you create locally** (for example, with a tool like `jwt.io`) that has the correct issuer (`https://localhost:7103`) and audience (`users-service-dev`) will be accepted. This is intentional for development convenience but means you should not expose the development port to untrusted networks. CI and staging environments validate signatures properly.

---

## End-to-End Test Flow

A typical local development workflow:

1. **Start the Auth Service** (in one terminal):
   ```bash
   dotnet run --project c:/Efra-proyects/authenthication-demo-backstage/src/AuthService/AuthService.csproj
   ```

2. **Start the Users Service** (in another terminal):
   ```bash
   dotnet run --project c:/Efra-proyects/users-demo-backstage/src/UsersService/UsersService.csproj
   ```

3. **Get a token**:
   ```bash
   TOKEN=$(curl -sk -X POST https://localhost:7103/api/auth/login \
     -H "Content-Type: application/json" \
     -d '{"username":"admin","password":"Platform@2026!"}' | \
     python -c "import sys,json; print(json.load(sys.stdin)['accessToken'])")
   ```

4. **Call the Users Service**:
   ```bash
   curl -sk https://localhost:7201/api/users -H "Authorization: Bearer $TOKEN"
   ```

5. **Stop both services** with `Ctrl+C` in each terminal.

---

## Using Testcontainers for PostgreSQL

While the Users Service currently uses an in-memory store for development, integration tests should exercise the real PostgreSQL persistence path. [Testcontainers](https://testcontainers.com/) provides throwaway PostgreSQL containers that start on demand and shut down when the test finishes.

### Adding Testcontainers to the Test Project

The `tests/` directory is set up for test projects. To add PostgreSQL integration tests, create a test project or add these packages to an existing one:

```bash
dotnet add tests/UsersService.Tests/UsersService.Tests.csproj package Testcontainers.PostgreSql
dotnet add tests/UsersService.Tests/UsersService.Tests.csproj package Npgsql
dotnet add tests/UsersService.Tests/UsersService.Tests.csproj package Dapper
dotnet add tests/UsersService.Tests/UsersService.Tests.csproj package xunit
```

### Database Fixture Pattern

Use an `IClassFixture` in xUnit to share a single PostgreSQL container across test methods within a test class:

```csharp
using Testcontainers.PostgreSql;
using Npgsql;
using Dapper;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("users_test")
        .WithUsername("test_user")
        .WithPassword("test_password")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await RunMigrations();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    private async Task RunMigrations()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string schema = """
            CREATE TABLE IF NOT EXISTS users (
                id UUID PRIMARY KEY,
                tenant_id UUID NOT NULL,
                username VARCHAR(100) NOT NULL,
                email VARCHAR(255) NOT NULL,
                display_name VARCHAR(200),
                department VARCHAR(200),
                job_title VARCHAR(200),
                roles TEXT[] NOT NULL DEFAULT '{}',
                last_login_at TIMESTAMPTZ,
                deleted_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE(tenant_id, username)
            );

            CREATE INDEX idx_users_tenant_id ON users(tenant_id);
            CREATE INDEX idx_users_deleted_at ON users(deleted_at) WHERE deleted_at IS NULL;
        """;

        await connection.ExecuteAsync(schema);
    }
}
```

### Integration Test Example

```csharp
public sealed class UserRepositoryTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public UserRepositoryTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateUser_Should_Persist_And_Be_Retrievable()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);

        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var inserted = await connection.ExecuteAsync("""
            INSERT INTO users (id, tenant_id, username, email, roles)
            VALUES (@Id, @TenantId, @Username, @Email, @Roles)
            """, new
        {
            Id = userId,
            TenantId = tenantId,
            Username = "testuser",
            Email = "testuser@example.com",
            Roles = new[] { "developer" }
        });

        Assert.Equal(1, inserted);

        var user = await connection.QueryFirstOrDefaultAsync("""
            SELECT id, username, email FROM users WHERE id = @Id
            """, new { Id = userId });

        Assert.NotNull(user);
        Assert.Equal("testuser", user.username);
    }
}
```

### Running Tests with Testcontainers

```bash
# Ensure Docker Desktop is running, then execute:
dotnet test tests/UsersService.Tests/UsersService.Tests.csproj
```

Testcontainers automatically:
- Pulls the `postgres:16-alpine` image on first run (cached afterward)
- Starts a container on a random available port
- Executes migrations from the fixture
- Runs all test methods against that container
- Stops and removes the container when the fixture disposes

> **Performance:** The first run downloads the PostgreSQL image (~100 MB). Subsequent runs start in under 2 seconds. If you need to share one container across multiple test classes, use a `CollectionFixture` instead of `IClassFixture`.

### Configuration for Testcontainers

| Builder Method | Description | Default |
|---|---|---|
| `WithImage("postgres:16-alpine")` | PostgreSQL image tag | `postgres:16-alpine` |
| `WithDatabase("users_test")` | Database name | `test` |
| `WithUsername("test_user")` | Database user | `test` |
| `WithPassword("test_password")` | User password | `test` |
| `WithCleanUp(true)` | Remove container after disposal | `true` |
| `WithPortBinding(5432, true)` | Expose on a random host port (default) | Random |

> When running locally alongside a manually started PostgreSQL instance, Testcontainers assigns a random host port to avoid conflicts. The `GetConnectionString()` method returns the correct connection string with the dynamic port.

---

## Configuration Reference

### Development Overrides (`appsettings.Development.json`)

| Key | Users Service | Auth Service |
|---|---|---|
| **Auth:Issuer** | `https://localhost:7103` | `https://localhost:7103` |
| **Auth:Audience** | `users-service-dev` | `platform-api-dev` |
| **Auth:AccessTokenLifetimeMinutes** | (uses default 15) | `60` (longer window for debugging) |
| **ConnectionStrings:UsersDb** | `Host=localhost;Port=5432;Database=users_dev;Username=users_svc;Password=dev_password` | N/A |
| **ConnectionStrings:AuthDb** | N/A | `Host=localhost;Port=5432;Database=auth_dev;Username=auth_svc;Password=dev_password` |
| **Serilog minimum level** | `Debug` | `Debug` |

### Port Assignments

| Service | Development | Production (internal) |
|---|---|---|
| Users Service HTTPS | `7201` | `443` |
| Auth Service HTTPS | `7103` | `443` |
| PostgreSQL | `5432` | `5432` |

### Environment Variables

Both services respect standard ASP.NET Core environment variables:

```bash
# Override the listening URLs
ASPNETCORE_URLS=https://localhost:7201

# Set the environment (defaults to "Production" if not set)
ASPNETCORE_ENVIRONMENT=Development
```

---

## Troubleshooting

### "Failed to bind to address https://localhost:7201"

Port conflict. Check what is using the port:

```bash
netstat -ano | findstr :7201
```

Kill the conflicting process or change the port via `Properties/launchSettings.json` or the `ASPNETCORE_URLS` environment variable:

```bash
ASPNETCORE_URLS=https://localhost:7202 dotnet run --project src/UsersService/UsersService.csproj
```

### "Unable to find a matching algorithm" on `dotnet restore`

The project uses floating versions (`10.*`) for some packages. Ensure you have the .NET 10 SDK installed and that the NuGet feed includes the .NET 10 targeting packs. Run:

```bash
dotnet --list-sdks
dotnet restore --force-evaluate
```

### "Authorization: Bearer token" returns 401 Unauthorized

Check the following:

1. The token has not expired. The Auth Service development config uses a 60-minute access token lifetime. Get a fresh token.
2. The token's audience matches `users-service-dev`. Tokens issued by the Auth Service in development use audience `platform-api-dev` by default. The Users Service expects `users-service-dev` (configured in `appsettings.Development.json`). Verify both are aligned.
3. The issuer in the token (`https://localhost:7103`) matches the `Auth:Issuer` value in the Users Service configuration.

To inspect a JWT's claims, decode its payload (the second base64 segment):

```bash
# Decode JWT payload (paste your token)
echo "PASTE_YOUR_TOKEN_HERE" | cut -d. -f2 | python -c "import sys,base64,json; padded=sys.stdin.read().strip()+'=='; print(json.dumps(json.loads(base64.urlsafe_b64decode(padded)),indent=2))"
```

### Docker Desktop is not running (Testcontainers)

Testcontainers requires a running Docker daemon. If tests fail with a Docker connectivity error:

1. Start Docker Desktop.
2. Wait for the Docker engine status to show "Running".
3. Re-run the tests.

To verify Docker is available:

```bash
docker info
```

### "401" on Auth Service endpoints after restart

The Auth Service stores refresh tokens and revoked JTI IDs in memory. Restarting the service clears all state. Obtain a new token by calling `/api/auth/login` again.

### "Connectivity error" between services

If you are running both services in separate terminals on the same machine, ensure:

- The Auth Service is started **before** the Users Service.
- Both are using HTTPS with self-signed dev certificates. ASP.NET Core generates these automatically on first run. If prompted, trust the dev certificate:

```bash
dotnet dev-certs https --trust
```

### No `launchSettings.json` exists

Both projects can be run directly with `dotnet run` as shown above. If you prefer Visual Studio launch profiles, you can add a `Properties/launchSettings.json`:

```json
{
  "profiles": {
    "UsersService": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "launchUrl": "swagger",
      "applicationUrl": "https://localhost:7201",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

---

## Related Documents

- [Developer Guide](developer-guide.md) — architecture walkthrough for new team members
- [Testing Guide](testing.md) — testing strategy, frameworks, and running tests
- [How to Debug](how-to-debug.md) — debugging techniques and common issues
- [Security Architecture](../architecture/security.md) — authentication flow and authorization model
- [System Context](../architecture/context.md) — how the service fits into the platform
- [Variables & Configuration](../api/variables.md) — full configuration key reference
