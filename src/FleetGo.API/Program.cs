using System.Text;
using FleetGo.API.Auth;
using FleetGo.API.Auth.Options;
using FleetGo.API.Data;
using FleetGo.API.Data.Entities;
using FleetGo.API.Endpoints;
using FleetGo.API.Health;
using FleetGo.Shared;
using FleetGo.Shared.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Services (the composition root - everything the app can resolve is declared here)
// ---------------------------------------------------------------------------

// Injected instead of using DateTimeOffset.UtcNow directly, so time can be
// frozen in tests.
builder.Services.AddSingleton(TimeProvider.System);

// Serialise responses with the same source-generated metadata the mobile client
// uses to read them.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, FleetGoJsonSerializerContext.Default));

// RFC 9457 problem+json for every unhandled error and every bare status code,
// so clients get one predictable error shape instead of HTML error pages.
builder.Services.AddProblemDetails();

// ---------------------------------------------------------------------------
// Database
// ---------------------------------------------------------------------------

// FleetGoApiFactory (the API integration tests) registers FleetGoDbContext itself,
// backed by an in-memory SQLite connection, and runs with the environment set to
// "Testing". That registration must be the ONLY one FleetGoDbContext ever gets in that
// environment: EF Core composes every AddDbContext configuration action registered for a
// context type rather than letting a later call silently replace an earlier one, so if
// this block ran unconditionally, its SQL Server configuration - and the exception it
// throws when no connection string is configured - would still execute even after the
// test factory adds its own SQLite configuration on top, before any test can run.
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<FleetGoDbContext>(options =>
    {
        string? connectionString = builder.Configuration.GetConnectionString("FleetGoDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'ConnectionStrings:FleetGoDatabase' is not configured. " +
                "Set it locally with: dotnet user-secrets set \"ConnectionStrings:FleetGoDatabase\" \"<value>\" " +
                "(see docs/development-setup.md).");
        }

        options.UseSqlServer(connectionString);
    });
}

// ---------------------------------------------------------------------------
// Authentication
// ---------------------------------------------------------------------------

builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(
        options => Encoding.UTF8.GetByteCount(options.SigningKey ?? string.Empty) >= 32,
        "Jwt:SigningKey must be configured and be at least 32 bytes (256 bits) once UTF-8 encoded. " +
        "Set it locally with: dotnet user-secrets set \"Jwt:SigningKey\" \"<a long random string>\" " +
        "(see docs/development-setup.md).")
    .ValidateOnStart();

builder.Services
    .AddOptions<RefreshTokenOptions>()
    .Bind(builder.Configuration.GetSection(RefreshTokenOptions.SectionName));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// JwtBearerOptions is configured from IOptions<JwtOptions> - resolved from DI after the
// host is fully built - rather than by reading builder.Configuration directly up here.
// Reading configuration this early and baking it into a closure would freeze in whatever
// appsettings/user-secrets/environment-variable values exist at this exact line, which is
// BEFORE WebApplicationFactory (used by the integration tests) gets a chance to layer its
// own test configuration onto the builder. Binding through the options pipeline instead
// means this always reflects the final, fully-merged configuration.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptions) =>
    {
        JwtOptions options = jwtOptions.Value;

        // Keep JWT claim names ("sub", "email", ...) as issued instead of ASP.NET Core's
        // legacy remapping to long ClaimTypes URIs - what AuthEndpoints reads back matches
        // what JwtTokenService wrote.
        bearerOptions.MapInboundClaims = false;

        bearerOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// ---------------------------------------------------------------------------
// Health checks, OpenAPI
// ---------------------------------------------------------------------------

// Liveness/readiness probes. "ready" now also covers the database: an orchestrator
// should stop routing traffic to an instance that cannot reach SQL Server, even
// though the process itself ("live") is perfectly healthy.
builder.Services.AddHealthChecks()
    .AddCheck(
        name: "self",
        check: () => HealthCheckResult.Healthy("The API process is running."),
        tags: ["live", "ready"])
    .AddDbContextCheck<FleetGoDbContext>(name: "database", tags: ["ready"]);

// OpenAPI document generation. The document is served from /openapi/v1.json and
// rendered by Scalar in development.
builder.Services.AddOpenApi(ApiRoutes.Version, options =>
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        if (document.Info is not null)
        {
            document.Info.Title = "FleetGo API";
            document.Info.Version = ApiRoutes.Version;
            document.Info.Description =
                "Backend for the FleetGo driver and delivery application.";
        }

        return Task.CompletedTask;
    }));

WebApplication app = builder.Build();

// ---------------------------------------------------------------------------
// HTTP pipeline (order matters - each piece wraps the ones registered after it)
// ---------------------------------------------------------------------------

app.UseExceptionHandler();   // turns unhandled exceptions into problem+json
app.UseStatusCodePages();    // turns bare 404/405 responses into problem+json

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                 // /openapi/v1.json
    app.MapScalarApiReference();      // /scalar/v1 - interactive API reference
}
else
{
    // Only enforced outside development: the Android emulator talks to the host
    // over plain HTTP, and a redirect would break it.
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

// ---------------------------------------------------------------------------
// Endpoints
// ---------------------------------------------------------------------------

app.MapHealthChecks(ApiRoutes.Health, new HealthCheckOptions
{
    ResponseWriter = HealthReportResponseWriter.WriteAsync,
});

app.MapHealthChecks(ApiRoutes.HealthLive, new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = HealthReportResponseWriter.WriteAsync,
});

app.MapHealthChecks(ApiRoutes.HealthReady, new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthReportResponseWriter.WriteAsync,
});

app.MapSystemEndpoints();
app.MapAuthEndpoints();

app.Run();

/// <summary>
/// Exposed so the integration test project can boot this exact host through
/// <c>WebApplicationFactory&lt;Program&gt;</c>. Top-level statements generate an
/// internal Program class, which the test project could not otherwise reach.
/// </summary>
public partial class Program;

