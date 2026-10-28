using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MovieDB.Infrastructure.Auth;
using MovieDB.Infrastructure.Entities;

namespace MovieDB.Infrastructure.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(x => x.Id).HasName("pk_users");
        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.UserName).HasColumnName("user_name").HasMaxLength(256);
        builder.Property(x => x.NormalizedUserName).HasColumnName("normalized_user_name").HasMaxLength(256);
        builder.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(512);
        builder.Property(x => x.SecurityStamp).HasColumnName("security_stamp").HasMaxLength(64);
        builder.Property(x => x.ConcurrencyStamp).HasColumnName("concurrency_stamp").HasMaxLength(64);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime(6)");
        builder.HasIndex(x => x.NormalizedUserName).IsUnique().HasDatabaseName("ux_users_normalized_user_name");
    }
}

internal sealed class AuthThrottleStateConfiguration : IEntityTypeConfiguration<AuthThrottleState>
{
    public void Configure(EntityTypeBuilder<AuthThrottleState> builder)
    {
        builder.ToTable("auth_throttle_states");
        builder.HasKey(x => x.Key).HasName("pk_auth_throttle_states");
        builder
            .Property(x => x.Key)
            .HasColumnName("key")
            .HasMaxLength(80)
            .HasCharSet("ascii")
            .UseCollation("ascii_bin")
            .ValueGeneratedNever();
        builder.Property(x => x.WindowStart).HasColumnName("window_start").HasColumnType("datetime(6)");
        builder.Property(x => x.Attempts).HasColumnName("attempts");
        builder.Property(x => x.Failures).HasColumnName("failures");
        builder.Property(x => x.BlockedUntil).HasColumnName("blocked_until").HasColumnType("datetime(6)");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("datetime(6)");
        builder.Property(x => x.LastFailure).HasColumnName("last_failure").HasColumnType("datetime(6)");
        builder
            .Property(x => x.LastAccount)
            .HasColumnName("last_account")
            .HasMaxLength(80)
            .HasCharSet("ascii")
            .UseCollation("ascii_bin");
        builder.Property(x => x.AccountChanges).HasColumnName("account_changes");
        builder.Property(x => x.LastSuccess).HasColumnName("last_success").HasColumnType("datetime(6)");
        builder.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_auth_throttle_states_expires_at");
    }
}

internal sealed class DataProtectionKeyConfiguration : IEntityTypeConfiguration<DataProtectionKey>
{
    public void Configure(EntityTypeBuilder<DataProtectionKey> builder)
    {
        builder.ToTable("data_protection_keys");
        builder.HasKey(x => x.Id).HasName("pk_data_protection_keys");
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.FriendlyName).HasColumnName("friendly_name").HasMaxLength(200);
        builder.Property(x => x.Xml).HasColumnName("xml").HasColumnType("longtext");
    }
}
