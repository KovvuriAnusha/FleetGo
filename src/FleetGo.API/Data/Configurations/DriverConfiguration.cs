using FleetGo.API.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FleetGo.API.Data.Configurations;

internal sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("Drivers");

        builder.HasKey(driver => driver.Id);

        builder.Property(driver => driver.UserId)
            .IsRequired();

        // The one-to-one relationship itself (including this FK's uniqueness) is
        // configured from the User side in UserConfiguration, which is the principal end.

        builder.Property(driver => driver.DriverCode)
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(driver => driver.DriverCode)
            .IsUnique();

        builder.Property(driver => driver.PhoneNumber)
            .HasMaxLength(30);

        builder.Property(driver => driver.IsActive)
            .IsRequired();

        builder.Property(driver => driver.CreatedAtUtc)
            .IsRequired();

        builder.Property(driver => driver.UpdatedAtUtc)
            .IsRequired();
    }
}

