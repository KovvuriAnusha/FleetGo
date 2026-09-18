using FleetGo.API.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FleetGo.API.Data.Configurations;

internal sealed class StopConfiguration : IEntityTypeConfiguration<Stop>
{
    public void Configure(EntityTypeBuilder<Stop> builder)
    {
        builder.ToTable("Stops");

        builder.HasKey(stop => stop.Id);

        builder.Property(stop => stop.RouteId)
            .IsRequired();

        builder.Property(stop => stop.CustomerId)
            .IsRequired();

        builder.Property(stop => stop.Sequence)
            .IsRequired();

        // Two stops at the same position on the same route would make "the next stop" an
        // ambiguous question - enforced here, not just in application code.
        builder.HasIndex(stop => new { stop.RouteId, stop.Sequence })
            .IsUnique();

        builder.Property(stop => stop.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(stop => stop.DeliveryNotes)
            .HasMaxLength(500);

        builder.Property(stop => stop.CreatedAtUtc)
            .IsRequired();

        builder.Property(stop => stop.UpdatedAtUtc)
            .IsRequired();

        // Required: a stop only ever exists as part of a route. Restrict, not Cascade - a
        // route's stops are its operational history; deleting the route (there is no
        // endpoint that does this today - see RouteEndpoints) must not silently take every
        // stop, and therefore every package, with it.
        builder.HasOne(stop => stop.Route)
            .WithMany(route => route.Stops)
            .HasForeignKey(stop => stop.RouteId)
            .OnDelete(DeleteBehavior.Restrict);

        // Required: every stop visits a customer. Restrict for the same reason - a customer
        // record must not be deletable out from under delivery history that references it.
        builder.HasOne(stop => stop.Customer)
            .WithMany()
            .HasForeignKey(stop => stop.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
