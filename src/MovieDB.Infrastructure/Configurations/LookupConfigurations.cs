using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MovieDB.Infrastructure.Entities;

namespace MovieDB.Infrastructure.Configurations;

internal sealed class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.ToTable("genres");
        builder.HasKey(x => x.Id).HasName("pk_genres");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(50);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
        builder.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_genres_name");
    }
}

internal sealed class MediumConfiguration : IEntityTypeConfiguration<Medium>
{
    public void Configure(EntityTypeBuilder<Medium> builder)
    {
        builder.ToTable("media");
        builder.HasKey(x => x.Id).HasName("pk_media");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(50);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
        builder.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_media_name");
    }
}
