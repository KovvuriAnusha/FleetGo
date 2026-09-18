using FleetGo.API.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FleetGo.API.Data.Configurations;

internal sealed class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> builder)
    {
        builder.ToTable("Packages");

        builder.HasKey(package => package.Id);

        builder.Property(package => package.StopId)
            .IsRequired();

        builder.Property(package => package.TrackingNumber)
            .HasMaxLength(40)
            .IsRequired();

        // A tracking number is the customer/carrier-facing identifier - two packages
        // sharing one would make "where is my package" unanswerable.
        builder.HasIndex(package => package.TrackingNumber)
            .IsUnique();

        builder.Property(package => package.Description)
            .HasMaxLength(300);

        builder.Property(package => package.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(package => package.CreatedAtUtc)
            .IsRequired();

        builder.Property(package => package.UpdatedAtUtc)
            .IsRequired();

        // Required: a package only ever exists at a stop. Restrict - package delivery
        // history must survive a stop-level operation the same way stops survive a
        // route-level one (see StopConfiguration).
        builder.HasOne(package => package.Stop)
            .WithMany(stop => stop.Packages)
            .HasForeignKey(package => package.StopId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
