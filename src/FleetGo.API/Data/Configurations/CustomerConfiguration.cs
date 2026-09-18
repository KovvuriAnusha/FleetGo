using FleetGo.API.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FleetGo.API.Data.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");

        builder.HasKey(customer => customer.Id);

        builder.Property(customer => customer.Name)
            .HasMaxLength(200)
            .IsRequired();

        // Supports the name-search filter on the list endpoint (see CustomerEndpoints) -
        // not unique, since two different customers can share a display name.
        builder.HasIndex(customer => customer.Name);

        builder.Property(customer => customer.PhoneNumber)
            .HasMaxLength(30);

        builder.Property(customer => customer.Email)
            .HasMaxLength(256);

        builder.Property(customer => customer.AddressLine1)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(customer => customer.AddressLine2)
            .HasMaxLength(200);

        builder.Property(customer => customer.City)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(customer => customer.State)
            .HasMaxLength(100);

        builder.Property(customer => customer.PostalCode)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(customer => customer.Country)
            .HasMaxLength(60);

        builder.Property(customer => customer.CreatedAtUtc)
            .IsRequired();

        builder.Property(customer => customer.UpdatedAtUtc)
            .IsRequired();
    }
}
