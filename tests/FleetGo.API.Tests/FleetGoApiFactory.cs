using FleetGo.API.Auth;
using FleetGo.API.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FleetGo.API.Tests;

/// <summary>
/// Boots the API in memory for testing.
/// <para>
/// Overriding the environment to "Testing" means the tests never depend on
/// development-only middleware, and it gives later phases one obvious place to
/// swap real dependencies (SQL Server, payment gateway) for test doubles.
/// </para>
/// <para>
/// Phase 2 adds two more swaps, both applied here rather than in the tests
/// themselves: a fixed JWT signing key (the real one only ever exists in a
/// developer's user-secrets or a deployment's environment variables - tests must
/// not depend on either being set), and SQLite in place of SQL Server (see the
/// package reference in FleetGo.API.Tests.csproj for why SQLite rather than the
/// EF Core InMemory provider).
/// </para>
/// </summary>
public sealed class FleetGoApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// 32 bytes once UTF-8 encoded - the minimum JwtOptions' own startup validation
    /// requires. Used for nothing except signing tokens created and verified within
    /// a single test run.
    /// </summary>
    public const string TestJwtSigningKey = "test-signing-key-not-for-any-real-use-0123456789";

    /// <summary>Otp:HashingKey - see TestJwtSigningKey above for why a fixed test value lives here rather than in configuration.</summary>
    public const string TestOtpHashingKey = "test-otp-hashing-key-not-for-any-real-use-01234567";

    /// <summary>
    /// Records every code <see cref="Auth.OtpService"/> would have sent, in place of the real
    /// <see cref="IOtpSender"/> (which, outside Development, does not even exist - see
    /// Program.cs). Public so a test can read back the exact code that was generated.
    /// </summary>
    public RecordingOtpSender OtpSender { get; } = new();

    // Kept open for the lifetime of the factory: an in-memory SQLite database exists
    // only as long as at least one connection to it is open, and every DbContext
    // instance created during a test must see the same database.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = TestJwtSigningKey,
                ["Otp:HashingKey"] = TestOtpHashingKey,
                // No resend cooldown in tests: a test that requests two OTPs back-to-back
                // (e.g. to prove the second invalidates the first) must not be at the mercy
                // of a real-world cooldown. A dedicated rate-limiting test overrides the two
                // limits below with a much stricter value via WithWebHostBuilder - see
                // OtpRateLimitingTests - so the generous defaults here never mask that
                // behaviour, they just keep every *other* test from tripping the limiter by
                // sharing one TestServer (and therefore one limiter partition) across an
                // entire test class.
                ["Otp:ResendCooldownSeconds"] = "0",
                ["Otp:RequestRateLimit:PermitLimit"] = "1000",
                ["Otp:RequestRateLimit:WindowSeconds"] = "60",
                ["Otp:VerifyRateLimit:PermitLimit"] = "1000",
                ["Otp:VerifyRateLimit:WindowSeconds"] = "60",
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<FleetGoDbContext>>();

            _connection.Open();
            services.AddDbContext<FleetGoDbContext>(options => options.UseSqlite(_connection));

            // Program.cs never registers IOtpSender outside Development/Testing, and even in
            // Testing it would register DevelopmentOtpSender (which only logs) - swapped here
            // for one that remembers what it was asked to send, so tests can read the actual
            // generated code back.
            services.RemoveAll<IOtpSender>();
            services.AddSingleton<IOtpSender>(OtpSender);

            using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<FleetGoDbContext>().Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}

