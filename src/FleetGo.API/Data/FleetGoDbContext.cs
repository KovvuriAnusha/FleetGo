using FleetGo.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FleetGo.API.Data;

/// <summary>
/// FleetGo's EF Core context. Phase 2 keeps it to the three tables authentication needs;
/// the domain tables (vehicles, routes, stops, ...) arrive with the phases that need them,
/// each via its own <see cref="IEntityTypeConfiguration{TEntity}"/> picked up by
/// <see cref="ApplyConfigurationsFromAssembly"/> below - so growing the schema never means
/// growing this class.
/// </summary>
public sealed class FleetGoDbContext : DbContext
{
    public FleetGoDbContext(DbContextOptions<FleetGoDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Driver> Drivers => Set<Driver>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FleetGoDbContext).Assembly);
    }
}

