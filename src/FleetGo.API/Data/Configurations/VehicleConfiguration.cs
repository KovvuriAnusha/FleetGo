using FleetGo.API.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FleetGo.API.Data.Configurations;

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("Vehicles");

        builder.HasKey(vehicle => vehicle.Id);

        builder.Property(vehicle => vehicle.RegistrationNumber)
            .HasMaxLength(20)
            .IsRequired();

        // A registration number identifies one physical vehicle - two rows for the same
        // plate would be a data-entry bug, not a legitimate scenario.
        builder.HasIndex(vehicle => vehicle.RegistrationNumber)
            .IsUnique();

        builder.Property(vehicle => vehicle.Make)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(vehicle => vehicle.Model)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(vehicle => vehicle.Year);

        builder.Property(vehicle => vehicle.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(vehicle => vehicle.CreatedAtUtc)
            .IsRequired();

        builder.Property(vehicle => vehicle.UpdatedAtUtc)
            .IsRequired();

        // Optional assignment: a vehicle can sit unassigned. SetNull (not Cascade) so
        // deactivating/removing a driver profile never deletes the vehicle itself - it just
        // becomes unassigned again, which is the same state a brand-new vehicle starts in.
        builder.HasOne(vehicle => vehicle.Driver)
            .WithMany()
            .HasForeignKey(vehicle => vehicle.DriverId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
