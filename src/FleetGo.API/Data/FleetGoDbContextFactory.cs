using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FleetGo.API.Data;

/// <summary>
/// Lets EF Core design-time tools (<c>dotnet ef migrations add</c>, <c>dotnet ef database
/// update</c>) construct a <see cref="FleetGoDbContext"/> without booting the whole web
/// host - <c>Program.cs</c> never runs, so there is no <see cref="WebApplication"/>,
/// no DI container, and no server listening.
/// <para>
/// Reads configuration the same way the real host does (appsettings + user secrets +
/// environment variables), so a connection string set via <c>dotnet user-secrets</c>
/// is picked up here too. When none is configured - a fresh checkout, before the
/// developer has set anything - falls back to a placeholder connection string. That
/// string is never used to actually connect: <c>migrations add</c> only needs a
/// syntactically valid one to generate the migration's C# and SQL from the model.
/// </para>
/// </summary>
public sealed class FleetGoDbContextFactory : IDesignTimeDbContextFactory<FleetGoDbContext>
{
    /// <summary>
    /// Design-time-only fallback. Not a secret - it grants access to nothing - it just
    /// keeps `dotnet ef migrations add` working on a machine that has not configured a
    /// real connection string yet.
    /// </summary>
    private const string DesignTimeFallbackConnectionString =
        "Server=localhost;Database=FleetGo_DesignTime;TrustServerCertificate=True;Trusted_Connection=True;";

    public FleetGoDbContext CreateDbContext(string[] args)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile(
                $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"}.json",
                optional: true)
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        string connectionString =
            configuration.GetConnectionString("FleetGoDatabase")
            ?? DesignTimeFallbackConnectionString;

        DbContextOptionsBuilder<FleetGoDbContext> optionsBuilder = new();
        optionsBuilder.UseSqlServer(connectionString);

        return new FleetGoDbContext(optionsBuilder.Options);
    }
}

