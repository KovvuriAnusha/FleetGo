using System.Net.Http.Headers;
using System.Net.Http.Json;
using FleetGo.API.Data;
using FleetGo.API.Data.Entities;
using FleetGo.Shared;
using FleetGo.Shared.Contracts.Auth;
using FleetGo.Shared.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FleetGo.API.Tests;

/// <summary>
/// Shared seeding/login helpers for the fleet endpoint test classes (Vehicle, Customer,
/// Route, Stop, Package). Factors out the "create a user, log in, get a bearer token"
/// pattern that <see cref="AuthEndpointsTests"/> established per-class, since every fleet
/// test class needs an authenticated driver (and several need a second, independent one to
/// prove isolation between drivers).
/// </summary>
internal static class FleetTestSupport
{
    public const string Password = "correct horse battery staple 42!";

    /// <summary>Creates a uniquely-emailed, active user with a driver profile directly in the database.</summary>
    public static async Task<(string Email, Guid DriverId)> SeedDriverAsync(FleetGoApiFactory factory, CancellationToken ct)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        FleetGoDbContext db = scope.ServiceProvider.GetRequiredService<FleetGoDbContext>();
        IPasswordHasher<User> passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        string email = $"driver-{Guid.NewGuid():N}@example.com";
        DateTime now = DateTime.UtcNow;
        Guid driverId = Guid.NewGuid();

        User user = new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = "Test",
            LastName = "Driver",
            IsActive = true,
            PasswordHash = string.Empty,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, Password);
        user.Driver = new Driver
        {
            Id = driverId,
            UserId = user.Id,
            DriverCode = $"D-{Guid.NewGuid():N}"[..8],
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return (email, driverId);
    }

    /// <summary>Creates a uniquely-emailed, active user with NO driver profile (e.g. future dispatch/back-office staff).</summary>
    public static async Task<string> SeedNonDriverUserAsync(FleetGoApiFactory factory, CancellationToken ct)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        FleetGoDbContext db = scope.ServiceProvider.GetRequiredService<FleetGoDbContext>();
        IPasswordHasher<User> passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        string email = $"staff-{Guid.NewGuid():N}@example.com";
        DateTime now = DateTime.UtcNow;

        User user = new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = "Test",
            LastName = "Staff",
            IsActive = true,
            PasswordHash = string.Empty,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, Password);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return email;
    }

    public static async Task<TokenResponse> LoginAsync(HttpClient client, string email, CancellationToken ct)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.AuthLogin,
            new LoginRequest(email, Password),
            FleetGoJsonSerializerContext.Default.LoginRequest,
            ct);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(FleetGoJsonSerializerContext.Default.TokenResponse, ct)
            ?? throw new InvalidOperationException("Login did not return a token response.");
    }

    /// <summary>Creates a driver, logs in, and returns an authenticated client plus the driver's id/email.</summary>
    public static async Task<(HttpClient Client, Guid DriverId, string Email)> CreateAuthenticatedDriverClientAsync(
        FleetGoApiFactory factory, CancellationToken ct)
    {
        (string email, Guid driverId) = await SeedDriverAsync(factory, ct);
        HttpClient client = factory.CreateClient();
        TokenResponse tokens = await LoginAsync(client, email, ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return (client, driverId, email);
    }

    /// <summary>Creates a non-driver user, logs in, and returns an authenticated client (no driver profile).</summary>
    public static async Task<HttpClient> CreateAuthenticatedNonDriverClientAsync(FleetGoApiFactory factory, CancellationToken ct)
    {
        string email = await SeedNonDriverUserAsync(factory, ct);
        HttpClient client = factory.CreateClient();
        TokenResponse tokens = await LoginAsync(client, email, ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    /// <summary>Inserts a customer directly in the database and returns its id.</summary>
    public static async Task<Guid> SeedCustomerAsync(FleetGoApiFactory factory, CancellationToken ct, string? name = null)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        FleetGoDbContext db = scope.ServiceProvider.GetRequiredService<FleetGoDbContext>();

        DateTime now = DateTime.UtcNow;
        Customer customer = new()
        {
            Id = Guid.NewGuid(),
            Name = name ?? $"Customer {Guid.NewGuid():N}"[..16],
            AddressLine1 = "1 Test Street",
            City = "Testville",
            PostalCode = "00000",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);

        return customer.Id;
    }

    /// <summary>Inserts a route owned by <paramref name="driverId"/> directly in the database and returns its id.</summary>
    public static async Task<Guid> SeedRouteAsync(
        FleetGoApiFactory factory,
        CancellationToken ct,
        Guid driverId,
        RouteStatus status = RouteStatus.Planned,
        DateOnly? routeDate = null,
        string? routeNumber = null)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        FleetGoDbContext db = scope.ServiceProvider.GetRequiredService<FleetGoDbContext>();

        DateTime now = DateTime.UtcNow;
        Route route = new()
        {
            Id = Guid.NewGuid(),
            RouteNumber = routeNumber ?? $"R-{Guid.NewGuid():N}"[..12],
            DriverId = driverId,
            RouteDate = routeDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            Status = status,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.Routes.Add(route);
        await db.SaveChangesAsync(ct);

        return route.Id;
    }

    /// <summary>Inserts a stop on <paramref name="routeId"/> directly in the database and returns its id.</summary>
    public static async Task<Guid> SeedStopAsync(
        FleetGoApiFactory factory,
        CancellationToken ct,
        Guid routeId,
        Guid customerId,
        int sequence,
        StopStatus status = StopStatus.Pending)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        FleetGoDbContext db = scope.ServiceProvider.GetRequiredService<FleetGoDbContext>();

        DateTime now = DateTime.UtcNow;
        Stop stop = new()
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            CustomerId = customerId,
            Sequence = sequence,
            Status = status,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.Stops.Add(stop);
        await db.SaveChangesAsync(ct);

        return stop.Id;
    }

    /// <summary>Inserts a package at <paramref name="stopId"/> directly in the database and returns its id.</summary>
    public static async Task<Guid> SeedPackageAsync(
        FleetGoApiFactory factory,
        CancellationToken ct,
        Guid stopId,
        string? trackingNumber = null,
        PackageStatus status = PackageStatus.Pending)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        FleetGoDbContext db = scope.ServiceProvider.GetRequiredService<FleetGoDbContext>();

        DateTime now = DateTime.UtcNow;
        Package package = new()
        {
            Id = Guid.NewGuid(),
            StopId = stopId,
            TrackingNumber = trackingNumber ?? $"TRK-{Guid.NewGuid():N}"[..16],
            Status = status,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.Packages.Add(package);
        await db.SaveChangesAsync(ct);

        return package.Id;
    }
}
