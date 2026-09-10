using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FleetGo.API.Tests;

/// <summary>
/// Boots the API in memory for testing.
/// <para>
/// Overriding the environment to "Testing" means the tests never depend on
/// development-only middleware, and it gives later phases one obvious place to
/// swap real dependencies (SQL Server, payment gateway) for test doubles.
/// </para>
/// </summary>
public sealed class FleetGoApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }
}
