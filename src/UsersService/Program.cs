using Platform.UsersService.Controllers;
using Platform.UsersService.Services;
using Serilog;

// ============================================================================
// Users Service — Program Entry Point
// ============================================================================
// Minimal reference implementation demonstrating the structure of a .NET 10
// microservice for user lifecycle management in the Internal Developer Platform.
//
// In production, this would include:
// - PostgreSQL connection pool (NpgsqlDataSource) via Dapper
// - gRPC client to Auth Service for JWT validation
// - Azure Service Bus consumer (auth events) and publisher (user events)
// - Azure Key Vault secret retrieval
// - Microsoft Graph API client for Entra ID profile enrichment
// - OpenTelemetry tracing and metrics with Prometheus export
// - Health checks with real dependency probes
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

// Configure structured JSON logging
builder.Host.UseSerilog((context, config) =>
{
    config.ReadFrom.Configuration(context.Configuration)
          .Enrich.FromLogContext()
          .Enrich.WithMachineName()
          .WriteTo.Console(outputTemplate:
              "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
});

// ---------------------------------------------------------------------------
// Service Registration
// ---------------------------------------------------------------------------

// Application Services
builder.Services.AddSingleton<IUserService, UserService>();

// JWT Authentication (Bearer tokens issued by Auth Service)
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        var issuer = builder.Configuration["Auth:Issuer"] ?? "https://auth.internal.platform";
        var audience = builder.Configuration["Auth:Audience"] ?? "users-service";

        options.TokenValidationParameters = new()
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = false, // In production: validate against JWKS from Auth Service
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.RequireHttpsMetadata = false;
    });

builder.Services.AddAuthorization();

// OpenAPI / Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Users Service API",
        Version = "v1",
        Description = "User lifecycle management for the Internal Developer Platform."
    });
});

// Health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// ---------------------------------------------------------------------------
// Middleware Pipeline
// ---------------------------------------------------------------------------

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ---------------------------------------------------------------------------
// Endpoints
// ---------------------------------------------------------------------------

// User CRUD endpoints (all require JWT)
app.MapUsersEndpoints();

// Health endpoints
app.MapHealthEndpoints();

// ---------------------------------------------------------------------------
// Start
// ---------------------------------------------------------------------------

app.Logger.LogInformation("Users Service starting on {Urls}",
    string.Join(", ", app.Urls));

app.Run();
