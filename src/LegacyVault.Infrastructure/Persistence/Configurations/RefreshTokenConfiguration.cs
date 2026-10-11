using LegacyVault.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Persistence.Configurations;

/// <summary>Cấu hình schema refresh token.</summary>
public sealed class RefreshTokenConfiguration
    : IEntityTypeConfiguration<RefreshToken>
{
    private const string TableName = "RefreshTokens";

    /// <summary>Ánh xạ entity, index và quan hệ người dùng.</summary>
    /// <param name="builder">Builder mapping refresh token.</param>
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable(TableName);
        builder.HasKey(token => token.Id);

        builder.Property(token => token.TokenHash)
            .HasMaxLength(RefreshTokenRules.HashLength)
            .IsFixedLength()
            .IsRequired();

        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => token.FamilyId);

        builder.HasOne(token => token.User)
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}