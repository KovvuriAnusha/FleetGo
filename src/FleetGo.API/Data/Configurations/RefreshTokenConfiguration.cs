using FleetGo.API.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FleetGo.API.Data.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        builder.HasKey(token => token.Id);

        builder.Property(token => token.UserId)
            .IsRequired();

        builder.Property(token => token.TokenHash)
            .HasMaxLength(32) // SHA-256 digest: fixed 32 bytes.
            .IsRequired();

        // Every refresh call looks a presented token up by its hash - this is the one
        // index the hot path depends on.
        builder.HasIndex(token => token.TokenHash)
            .IsUnique();

        builder.Property(token => token.ExpiresAtUtc)
            .IsRequired();

        builder.Property(token => token.CreatedAtUtc)
            .IsRequired();

        // Self-referencing "replaced by" pointer. Restrict (not Cascade) so deleting a
        // token row can never cascade into deleting the very row that replaced it.
        builder.HasOne<RefreshToken>()
            .WithMany()
            .HasForeignKey(token => token.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.Restrict);

        // Computed in memory from ExpiresAtUtc/RevokedAtUtc - not a column.
        builder.Ignore(token => token.IsActive);
    }
}

