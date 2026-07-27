# Guía de Desarrollo Local

Esta guía explica cómo configurar un entorno de desarrollo local para el Users Service, incluyendo su Authentication Service complementario y la infraestructura necesaria para las pruebas de integración.

---

## Tabla de Contenidos

- [Requisitos Previos](#requisitos-previos)
- [Estructura del Repositorio](#estructura-del-repositorio)
- [Configuración del Entorno](#configuración-del-entorno)
- [Ejecutar el Auth Service (Simulacro de Emisor JWT)](#ejecutar-el-auth-service-simulacro-de-emisor-jwt)
- [Ejecutar el Users Service](#ejecutar-el-users-service)
- [Simulacro de Validación JWT para Desarrollo Local](#simulacro-de-validación-jwt-para-desarrollo-local)
- [Flujo de Prueba de Extremo a Extremo](#flujo-de-prueba-de-extremo-a-extremo)
- [Uso de Testcontainers para PostgreSQL](#uso-de-testcontainers-para-postgresql)
- [Referencia de Configuración](#referencia-de-configuración)
- [Solución de Problemas](#solución-de-problemas)

---

## Requisitos Previos

| Herramienta | Versión Mínima | Propósito |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0 | Compilar y ejecutar ambos servicios |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | 24+ | Contenedor PostgreSQL vía Testcontainers |
| [Git](https://git-scm.com/) | Última | Control de versiones |
| Un IDE (VS Code, Rider, Visual Studio) | Cualquiera | Edición y depuración |

**Verificar la instalación:**

```bash
dotnet --version
# Esperado: 10.0.x

docker --version
# Esperado: Docker version 24.x o superior

git --version
```

> El servicio apunta a `net10.0`. Si tienes una versión anterior del SDK, instala el SDK de .NET 10 desde [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0).

---

## Estructura del Repositorio

El Users Service y el Auth Service residen en repositorios separados. Ambos deben clonarse para una configuración local completa.

```
C:\Efra-proyects\
├── users-demo-backstage\          # Users Service (este repositorio)
│   ├── src\UsersService\          # Aplicación web .NET 10
│   │   ├── Controllers\           # Definiciones de endpoints Minimal API
│   │   ├── Models\                # DTOs y entidades
│   │   ├── Services\              # Lógica de negocio (IUserService / UserService)
│   │   ├── Program.cs             # Punto de entrada de la aplicación
│   │   └── appsettings*.json      # Configuración
│   ├── tests\                     # Pruebas unitarias y de integración
│   ├── docs\                      # Documentación TechDocs
│   └── openapi.yaml               # Especificación OpenAPI 3.1
│
└── authenthication-demo-backstage\ # Auth Service (compañero, repositorio separado)
    └── src\AuthService\           # Aplicación web .NET 10
```

---

## Configuración del Entorno

Clona ambos repositorios:

```bash
# Desde la raíz de tu espacio de trabajo
git clone <url-del-repositorio-users-service> users-demo-backstage
git clone <url-del-repositorio-auth-service> authenthication-demo-backstage
```

> Si ya has clonado los repositorios, omite este paso y asegúrate de que ambos estén en la rama `main` con los últimos cambios.

Restaura las dependencias de ambos proyectos:

```bash
dotnet restore c:/Efra-proyects/users-demo-backstage/src/UsersService/UsersService.csproj
dotnet restore c:/Efra-proyects/authenthication-demo-backstage/src/AuthService/AuthService.csproj
```

Compila ambos para confirmar que no hay errores de compilación:

```bash
dotnet build c:/Efra-proyects/users-demo-backstage/src/UsersService/UsersService.csproj --no-restore
dotnet build c:/Efra-proyects/authenthication-demo-backstage/src/AuthService/AuthService.csproj --no-restore
```

---

## Ejecutar el Auth Service (Simulacro de Emisor JWT)

El Users Service no emite sus propios tokens. Cada solicitud autenticada requiere un JWT emitido por el Authentication Service. Para el desarrollo local **ejecutas el Auth Service real** como un proceso secundario. Opera en un modo autónomo que no requiere PostgreSQL, Redis ni ninguna otra dependencia externa.

### Iniciar el Auth Service

```bash
dotnet run --project c:/Efra-proyects/authenthication-demo-backstage/src/AuthService/AuthService.csproj
```

### Qué Hace el Auth Service en Modo Desarrollo

| Aspecto | Comportamiento |
|---|---|
| **Puerto** | `https://localhost:7103` (configurado en `appsettings.Development.json`) |
| **Generación de claves** | Se genera un par de claves RSA efímero de 2048 bits al iniciar. La clave privada vive solo en memoria y se descarta cuando el proceso termina. |
| **Credenciales de demostración** | Usuario: `admin`, Contraseña: `Platform@2026!` |
| **Duración del token** | Tokens de acceso: 60 minutos. Tokens de actualización: 30 días. |
| **Almacenamiento de datos** | Todo el estado (tokens de actualización, JTIs revocados) está en memoria. Reiniciar el servicio limpia todo. |
| **Descubrimiento OIDC** | `https://localhost:7103/.well-known/openid-configuration` |
| **Endpoint JWKS** | `https://localhost:7103/.well-known/jwks.json` |

### Verificar que el Auth Service Está Funcionando

```bash
curl -k https://localhost:7103/api/health/live
# Esperado: {"status":"Healthy","timestamp":"..."}
```

La interfaz de Swagger está disponible en `https://localhost:7103/swagger`.

---

## Ejecutar el Users Service

### Iniciar el Servicio

```bash
dotnet run --project c:/Efra-proyects/users-demo-backstage/src/UsersService/UsersService.csproj
```

### Comportamiento en Modo Desarrollo

| Aspecto | Comportamiento |
|---|---|
| **Puerto** | `https://localhost:7201` |
| **Almacenamiento de datos** | Un `List<UserEntity>` en memoria con dos usuarios de demostración predefinidos (admin y jane.dev). No se requiere base de datos. |
| **Validación JWT** | La validación de firma está **deshabilitada** (ver [Simulacro de Validación JWT](#simulacro-de-validación-jwt-para-desarrollo-local)). |
| **Dependencia del Auth Service** | El servicio **no** llama al Auth Service en tiempo de ejecución en desarrollo. Los tokens de acceso del Auth Service se validan localmente usando solo los valores de emisor y audiencia configurados. |
| **Swagger UI** | `https://localhost:7201/swagger` |

### Verificar que el Users Service Está Funcionando

```bash
curl -k https://localhost:7201/api/health/live
# Esperado: {"status":"Healthy","timestamp":"..."}
```

---

## Simulacro de Validación JWT para Desarrollo Local

En producción, el Users Service valida cada JWT mediante:

1. Obteniendo el documento JWKS del endpoint `/.well-known/jwks.json` del Auth Service.
2. Usando la clave pública RSA del JWKS para verificar la firma RS256 del token.
3. Verificando el emisor, la audiencia, la expiración y el ID del JWT contra una lista negra.

En desarrollo, la validación de firma está **deshabilitada**. Esto se controla en `Program.cs`:

```csharp
options.TokenValidationParameters = new()
{
    ValidateIssuer = true,
    ValidIssuer = issuer,
    ValidateAudience = true,
    ValidAudience = audience,
    ValidateLifetime = true,
    ValidateIssuerSigningKey = false,  // <-- simulacro: sin verificación de firma
    ClockSkew = TimeSpan.FromSeconds(30)
};
```

### Qué Significa Esto

| Configuración | Producción | Desarrollo |
|---|---|---|
| `ValidateIssuerSigningKey` | `true` -- valida contra JWKS | `false` -- **omitido** |
| `RequireHttpsMetadata` | `true` | `false` (permite URLs de metadatos `http`) |
| Verificación de firma | RSA-256 contra la clave pública del Auth Service | **Ninguna** -- cualquier JWT con emisor/audiencia coincidentes es aceptado |

Este enfoque te permite desarrollar y probar sin ejecutar una infraestructura JWKS completa. Las **claims de emisor, audiencia y duración aún se aplican**, por lo que los tokens deben ser estructuralmente válidos y no estar expirados.

### Obtener un JWT para Pruebas Manuales

Usa el endpoint `/api/auth/login` del Auth Service para obtener un token que funcione con la configuración de desarrollo del Users Service:

```bash
curl -k -X POST https://localhost:7103/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Platform@2026!"}'
```

**Respuesta:**

```json
{
  "accessToken": "eyJhbGciOiJSUzI1NiIsIm...",
  "tokenType": "Bearer",
  "expiresIn": 3600,
  "refreshToken": "AbCdEf..."
}
```

Copia el valor de `accessToken` y úsalo contra los endpoints del Users Service:

```bash
curl -k https://localhost:7201/api/users \
  -H "Authorization: Bearer eyJhbGciOiJSUzI1NiIsIm..."
```

### Advertencia Importante

Debido a que la validación de firma está deshabilitada, **cualquier JWT que crees localmente** (por ejemplo, con una herramienta como `jwt.io`) que tenga el emisor correcto (`https://localhost:7103`) y la audiencia correcta (`users-service-dev`) será aceptado. Esto es intencional para la comodidad en desarrollo, pero significa que no debes exponer el puerto de desarrollo a redes no confiables. Los entornos de CI y staging validan las firmas correctamente.

---

## Flujo de Prueba de Extremo a Extremo

Un flujo de trabajo de desarrollo local típico:

1. **Inicia el Auth Service** (en una terminal):
   ```bash
   dotnet run --project c:/Efra-proyects/authenthication-demo-backstage/src/AuthService/AuthService.csproj
   ```

2. **Inicia el Users Service** (en otra terminal):
   ```bash
   dotnet run --project c:/Efra-proyects/users-demo-backstage/src/UsersService/UsersService.csproj
   ```

3. **Obtén un token**:
   ```bash
   TOKEN=$(curl -sk -X POST https://localhost:7103/api/auth/login \
     -H "Content-Type: application/json" \
     -d '{"username":"admin","password":"Platform@2026!"}' | \
     python -c "import sys,json; print(json.load(sys.stdin)['accessToken'])")
   ```

4. **Llama al Users Service**:
   ```bash
   curl -sk https://localhost:7201/api/users -H "Authorization: Bearer $TOKEN"
   ```

5. **Detén ambos servicios** con `Ctrl+C` en cada terminal.

---

## Uso de Testcontainers para PostgreSQL

Si bien el Users Service actualmente usa un almacenamiento en memoria para desarrollo, las pruebas de integración deben ejercitar la ruta real de persistencia con PostgreSQL. [Testcontainers](https://testcontainers.com/) proporciona contenedores PostgreSQL desechables que se inician bajo demanda y se detienen cuando la prueba finaliza.

### Agregar Testcontainers al Proyecto de Pruebas

El directorio `tests/` está configurado para proyectos de prueba. Para agregar pruebas de integración con PostgreSQL, crea un proyecto de prueba o agrega estos paquetes a uno existente:

```bash
dotnet add tests/UsersService.Tests/UsersService.Tests.csproj package Testcontainers.PostgreSql
dotnet add tests/UsersService.Tests/UsersService.Tests.csproj package Npgsql
dotnet add tests/UsersService.Tests/UsersService.Tests.csproj package Dapper
dotnet add tests/UsersService.Tests/UsersService.Tests.csproj package xunit
```

### Patrón de Fixture de Base de Datos

Usa un `IClassFixture` en xUnit para compartir un único contenedor PostgreSQL entre los métodos de prueba dentro de una clase de prueba:

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

### Ejemplo de Prueba de Integración

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

### Ejecutar Pruebas con Testcontainers

```bash
# Asegúrate de que Docker Desktop esté funcionando, luego ejecuta:
dotnet test tests/UsersService.Tests/UsersService.Tests.csproj
```

Testcontainers automáticamente:
- Descarga la imagen `postgres:16-alpine` en la primera ejecución (se almacena en caché después)
- Inicia un contenedor en un puerto disponible aleatorio
- Ejecuta las migraciones desde el fixture
- Ejecuta todos los métodos de prueba contra ese contenedor
- Detiene y elimina el contenedor cuando el fixture se descarta

> **Rendimiento:** La primera ejecución descarga la imagen de PostgreSQL (~100 MB). Las ejecuciones posteriores se inician en menos de 2 segundos. Si necesitas compartir un contenedor entre varias clases de prueba, usa un `CollectionFixture` en lugar de `IClassFixture`.

### Configuración para Testcontainers

| Método del Constructor | Descripción | Valor Predeterminado |
|---|---|---|
| `WithImage("postgres:16-alpine")` | Etiqueta de la imagen PostgreSQL | `postgres:16-alpine` |
| `WithDatabase("users_test")` | Nombre de la base de datos | `test` |
| `WithUsername("test_user")` | Usuario de la base de datos | `test` |
| `WithPassword("test_password")` | Contraseña del usuario | `test` |
| `WithCleanUp(true)` | Eliminar contenedor después de desechar | `true` |
| `WithPortBinding(5432, true)` | Exponer en un puerto host aleatorio (predeterminado) | Aleatorio |

> Cuando se ejecuta localmente junto con una instancia de PostgreSQL iniciada manualmente, Testcontainers asigna un puerto host aleatorio para evitar conflictos. El método `GetConnectionString()` devuelve la cadena de conexión correcta con el puerto dinámico.

---

## Referencia de Configuración

### Sobrescrituras de Desarrollo (`appsettings.Development.json`)

| Clave | Users Service | Auth Service |
|---|---|---|
| **Auth:Issuer** | `https://localhost:7103` | `https://localhost:7103` |
| **Auth:Audience** | `users-service-dev` | `platform-api-dev` |
| **Auth:AccessTokenLifetimeMinutes** | (usa el valor predeterminado 15) | `60` (ventana más larga para depuración) |
| **ConnectionStrings:UsersDb** | `Host=localhost;Port=5432;Database=users_dev;Username=users_svc;Password=dev_password` | N/A |
| **ConnectionStrings:AuthDb** | N/A | `Host=localhost;Port=5432;Database=auth_dev;Username=auth_svc;Password=dev_password` |
| **Nivel mínimo de Serilog** | `Debug` | `Debug` |

### Asignación de Puertos

| Servicio | Desarrollo | Producción (interno) |
|---|---|---|
| Users Service HTTPS | `7201` | `443` |
| Auth Service HTTPS | `7103` | `443` |
| PostgreSQL | `5432` | `5432` |

### Variables de Entorno

Ambos servicios respetan las variables de entorno estándar de ASP.NET Core:

```bash
# Sobrescribir las URLs de escucha
ASPNETCORE_URLS=https://localhost:7201

# Establecer el entorno (el valor predeterminado es "Production" si no se establece)
ASPNETCORE_ENVIRONMENT=Development
```

---

## Solución de Problemas

### "Failed to bind to address https://localhost:7201"

Conflicto de puerto. Verifica qué está usando el puerto:

```bash
netstat -ano | findstr :7201
```

Finaliza el proceso en conflicto o cambia el puerto mediante `Properties/launchSettings.json` o la variable de entorno `ASPNETCORE_URLS`:

```bash
ASPNETCORE_URLS=https://localhost:7202 dotnet run --project src/UsersService/UsersService.csproj
```

### "Unable to find a matching algorithm" en `dotnet restore`

El proyecto usa versiones flotantes (`10.*`) para algunos paquetes. Asegúrate de tener instalado el SDK de .NET 10 y que la fuente NuGet incluya los paquetes de destino de .NET 10. Ejecuta:

```bash
dotnet --list-sdks
dotnet restore --force-evaluate
```

### "Authorization: Bearer token" devuelve 401 Unauthorized

Verifica lo siguiente:

1. El token no ha expirado. La configuración de desarrollo del Auth Service usa una duración de token de acceso de 60 minutos. Obtén un token nuevo.
2. La audiencia del token coincide con `users-service-dev`. Los tokens emitidos por el Auth Service en desarrollo usan la audiencia `platform-api-dev` por defecto. El Users Service espera `users-service-dev` (configurado en `appsettings.Development.json`). Verifica que ambos estén alineados.
3. El emisor en el token (`https://localhost:7103`) coincide con el valor de `Auth:Issuer` en la configuración del Users Service.

Para inspeccionar las claims de un JWT, decodifica su carga útil (el segundo segmento base64):

```bash
# Decodificar la carga útil del JWT (pega tu token)
echo "PEGA_TU_TOKEN_AQUI" | cut -d. -f2 | python -c "import sys,base64,json; padded=sys.stdin.read().strip()+'=='; print(json.dumps(json.loads(base64.urlsafe_b64decode(padded)),indent=2))"
```

### Docker Desktop no está ejecutándose (Testcontainers)

Testcontainers requiere un demonio Docker en ejecución. Si las pruebas fallan con un error de conectividad Docker:

1. Inicia Docker Desktop.
2. Espera a que el estado del motor Docker muestre "Running".
3. Vuelve a ejecutar las pruebas.

Para verificar que Docker está disponible:

```bash
docker info
```

### "401" en endpoints del Auth Service después de reiniciar

El Auth Service almacena tokens de actualización e IDs JTI revocados en memoria. Reiniciar el servicio limpia todo el estado. Obtén un nuevo token llamando a `/api/auth/login` nuevamente.

### "Error de conectividad" entre servicios

Si estás ejecutando ambos servicios en terminales separadas en la misma máquina, asegúrate de:

- El Auth Service se inicia **antes** que el Users Service.
- Ambos usan HTTPS con certificados de desarrollo autofirmados. ASP.NET Core los genera automáticamente en la primera ejecución. Si se solicita, confía en el certificado de desarrollo:

```bash
dotnet dev-certs https --trust
```

### No existe `launchSettings.json`

Ambos proyectos se pueden ejecutar directamente con `dotnet run` como se muestra arriba. Si prefieres perfiles de inicio de Visual Studio, puedes agregar un `Properties/launchSettings.json`:

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

## Documentos Relacionados

- [Guía del Desarrollador](developer-guide.md) -- recorrido arquitectónico para nuevos miembros del equipo
- [Guía de Pruebas](testing.md) -- estrategia de pruebas, frameworks y ejecución de pruebas
- [Cómo Depurar](how-to-debug.md) -- técnicas de depuración y problemas comunes
- [Arquitectura de Seguridad](../architecture/security.md) -- flujo de autenticación y modelo de autorización
- [Contexto del Sistema](../architecture/context.md) -- cómo encaja el servicio en la plataforma
- [Variables y Configuración](../api/variables.md) -- referencia completa de claves de configuración
