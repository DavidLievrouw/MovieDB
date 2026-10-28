using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MovieDB.Infrastructure.Entities;

namespace MovieDB.Infrastructure.Configurations;

internal sealed class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    private static readonly string[] ScoreColumns =
    [
        "overall_score",
        "story_score",
        "picture_score",
        "music_score",
        "sound_score",
        "extras_score",
    ];

    public void Configure(EntityTypeBuilder<Movie> builder)
    {
        builder.ToTable(
            "movies",
            table =>
            {
                table.HasCheckConstraint("ck_movies_runtime_minutes", "`runtime_minutes` BETWEEN 1 AND 999");
                foreach (var column in ScoreColumns)
                {
                    table.HasCheckConstraint(
                        $"ck_movies_{column}",
                        $"`{column}` IS NULL OR `{column}` BETWEEN 1 AND 10"
                    );
                }
            }
        );

        builder.HasKey(x => x.Id).HasName("pk_movies");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200);
        builder.Property(x => x.ReleaseYear).HasColumnName("release_year").HasColumnType("smallint");
        builder.Property(x => x.Director).HasColumnName("director").HasMaxLength(150);
        builder.Property(x => x.GenreId).HasColumnName("genre_id");
        builder.Property(x => x.RuntimeMinutes).HasColumnName("runtime_minutes").HasColumnType("smallint");
        builder.Property(x => x.WatchedOn).HasColumnName("watched_on").HasColumnType("date");
        builder.Property(x => x.MediumId).HasColumnName("medium_id");
        builder.Property(x => x.OverallScore).HasColumnName("overall_score").HasColumnType("tinyint");
        builder.Property(x => x.StoryScore).HasColumnName("story_score").HasColumnType("tinyint");
        builder.Property(x => x.PictureScore).HasColumnName("picture_score").HasColumnType("tinyint");
        builder.Property(x => x.MusicScore).HasColumnName("music_score").HasColumnType("tinyint");
        builder.Property(x => x.SoundScore).HasColumnName("sound_score").HasColumnType("tinyint");
        builder.Property(x => x.ExtrasScore).HasColumnName("extras_score").HasColumnType("tinyint");
        builder.Property(x => x.Review).HasColumnName("review").HasColumnType("mediumtext");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime(6)");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().ValueGeneratedNever();

        builder
            .HasOne(x => x.Genre)
            .WithMany()
            .HasForeignKey(x => x.GenreId)
            .HasConstraintName("fk_movies_genres")
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne(x => x.Medium)
            .WithMany()
            .HasForeignKey(x => x.MediumId)
            .HasConstraintName("fk_movies_media")
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(x => new
            {
                x.Title,
                x.ReleaseYear,
                x.Director,
            })
            .IsUnique()
            .HasDatabaseName("ux_movies_title_year_director");
        builder.HasIndex(x => x.GenreId).HasDatabaseName("ix_movies_genre_id");
        builder.HasIndex(x => x.MediumId).HasDatabaseName("ix_movies_medium_id");
    }
}
