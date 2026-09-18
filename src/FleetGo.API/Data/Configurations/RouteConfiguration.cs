using FleetGo.API.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Route = FleetGo.API.Data.Entities.Route;

namespace FleetGo.API.Data.Configurations;

internal sealed class RouteConfiguration : IEntityTypeConfiguration<Route>
{
    public void Configure(EntityTypeBuilder<Route> builder)
    {
        builder.ToTable("Routes");

        builder.HasKey(route => route.Id);

        builder.Property(route => route.RouteNumber)
            .HasMaxLength(30)
            .IsRequired();

        builder.HasIndex(route => route.RouteNumber)
            .IsUnique();

        builder.Property(route => route.DriverId)
            .IsRequired();

        builder.Property(route => route.VehicleId);

        builder.Property(route => route.RouteDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(route => route.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(route => route.CreatedAtUtc)
            .IsRequired();

        builder.Property(route => route.UpdatedAtUtc)
            .IsRequired();

        // The most common query this phase makes ("this driver's routes on this date") is a
        // composite lookup, so it gets a composite index rather than relying on the
        // single-column DriverId index the FK below creates by convention.
        builder.HasIndex(route => new { route.DriverId, route.RouteDate });

        // Required: a route always belongs to exactly one driver. Restrict (not Cascade) -
        // a route is operational history, and deleting a driver profile must never silently
        // delete the routes they worked. The application does not expose a way to delete a
        // driver profile at all today, but the schema stays safe regardless of how a delete
        // is attempted (a future admin tool, a direct database operation, ...).
        builder.HasOne(route => route.Driver)
            .WithMany()
            .HasForeignKey(route => route.DriverId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional: a route can be planned before a vehicle is picked. SetNull so removing a
        // vehicle from the fleet leaves the route intact, just unassigned again.
        builder.HasOne(route => route.Vehicle)
            .WithMany()
            .HasForeignKey(route => route.VehicleId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
