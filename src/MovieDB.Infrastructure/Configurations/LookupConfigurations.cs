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

internal sealed class MovieGenreConfiguration : IEntityTypeConfiguration<MovieGenre>
{
    public void Configure(EntityTypeBuilder<MovieGenre> builder)
    {
        builder.ToTable("movie_genres");
        builder.HasKey(x => new { x.MovieId, x.GenreId }).HasName("pk_movie_genres");
        builder.Property(x => x.MovieId).HasColumnName("movie_id");
        builder.Property(x => x.GenreId).HasColumnName("genre_id");
        builder
            .HasOne(x => x.Movie)
            .WithMany(x => x.GenreLinks)
            .HasForeignKey(x => x.MovieId)
            .HasConstraintName("fk_movie_genres_movies")
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne(x => x.Genre)
            .WithMany()
            .HasForeignKey(x => x.GenreId)
            .HasConstraintName("fk_movie_genres_genres")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.GenreId).HasDatabaseName("ix_movie_genres_genre_id");
    }
}

internal sealed class DirectorConfiguration : IEntityTypeConfiguration<Director>
{
    public void Configure(EntityTypeBuilder<Director> builder)
    {
        builder.ToTable("directors");
        builder.HasKey(x => x.Id).HasName("pk_directors");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(150);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
        builder.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_directors_name");
    }
}

internal sealed class MovieDirectorConfiguration : IEntityTypeConfiguration<MovieDirector>
{
    public void Configure(EntityTypeBuilder<MovieDirector> builder)
    {
        builder.ToTable("movie_directors");
        builder.HasKey(x => new { x.MovieId, x.DirectorId }).HasName("pk_movie_directors");
        builder.Property(x => x.MovieId).HasColumnName("movie_id");
        builder.Property(x => x.DirectorId).HasColumnName("director_id");
        builder.Property(x => x.Position).HasColumnName("position").HasColumnType("tinyint");
        builder
            .HasOne(x => x.Movie)
            .WithMany(x => x.DirectorLinks)
            .HasForeignKey(x => x.MovieId)
            .HasConstraintName("fk_movie_directors_movies")
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne(x => x.Director)
            .WithMany()
            .HasForeignKey(x => x.DirectorId)
            .HasConstraintName("fk_movie_directors_directors")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.DirectorId).HasDatabaseName("ix_movie_directors_director_id");
        builder
            .HasIndex(x => new { x.MovieId, x.Position })
            .IsUnique()
            .HasDatabaseName("ux_movie_directors_movie_position");
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
