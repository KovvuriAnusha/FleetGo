using FleetGo.API.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FleetGo.API.Data.Configurations;

internal sealed class OtpCodeConfiguration : IEntityTypeConfiguration<OtpCode>
{
    public void Configure(EntityTypeBuilder<OtpCode> builder)
    {
        builder.ToTable("OtpCodes");

        builder.HasKey(otp => otp.Id);

        builder.Property(otp => otp.UserId)
            .IsRequired();

        builder.Property(otp => otp.Purpose)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(otp => otp.Salt)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(otp => otp.CodeHash)
            .HasMaxLength(32) // SHA-256/HMAC-SHA256 digest: fixed 32 bytes.
            .IsRequired();

        builder.Property(otp => otp.CreatedAtUtc)
            .IsRequired();

        builder.Property(otp => otp.ExpiresAtUtc)
            .IsRequired();

        builder.Property(otp => otp.AttemptCount)
            .IsRequired();

        builder.HasOne(otp => otp.User)
            .WithMany()
            .HasForeignKey(otp => otp.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Every request and verify call looks up "the current active code for this user and
        // purpose" - a non-unique index (many historical rows share a UserId/Purpose; only
        // application logic, never a database constraint, keeps at most one of them active)
        // covering exactly that access pattern, with CreatedAtUtc so "the most recent one"
        // is an index-ordered read rather than a sort.
        builder.HasIndex(otp => new { otp.UserId, otp.Purpose, otp.CreatedAtUtc });

        // Computed in memory from ConsumedAtUtc/InvalidatedAtUtc/ExpiresAtUtc - not a column.
        builder.Ignore(otp => otp.IsActive);
    }
}
