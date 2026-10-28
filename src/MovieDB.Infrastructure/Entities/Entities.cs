using MovieDB.Common;

namespace MovieDB.Infrastructure.Entities;

public sealed class Movie
{
    public MovieId Id { get; set; }
    public string Title { get; set; } = "";
    public int ReleaseYear { get; set; }
    public string Director { get; set; } = "";
    public GenreId GenreId { get; set; }
    public Genre Genre { get; set; } = null!;
    public int RuntimeMinutes { get; set; }
    public DateOnly WatchedOn { get; set; }
    public MediumId? MediumId { get; set; }
    public Medium? Medium { get; set; }
    public int? OverallScore { get; set; }
    public int? StoryScore { get; set; }
    public int? PictureScore { get; set; }
    public int? MusicScore { get; set; }
    public int? SoundScore { get; set; }
    public int? ExtrasScore { get; set; }
    public string? Review { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Concurrency token: 1 on insert, incremented by the application on every update.</summary>
    public int Version { get; set; } = 1;
}

public sealed class Genre
{
    public GenreId Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class Medium
{
    public MediumId Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class User
{
    public UserId Id { get; set; }
    public string UserName { get; set; } = "";
    public string NormalizedUserName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string SecurityStamp { get; set; } = "";
    public string ConcurrencyStamp { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
