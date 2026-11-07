using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MovieDB.Common;
using MovieDB.Infrastructure;
using MovieDB.Infrastructure.Entities;

namespace MovieDB.Benchmarks;

/// <summary>Values that the benchmarks use to query the seeded database.</summary>
/// <param name="MovieCount">The number of seeded movies.</param>
/// <param name="MiddleMovieId">The id of a movie in the middle of the default overview order.</param>
/// <param name="Title">The title of a movie that has a same-title duplicate with another director set.</param>
/// <param name="Year">The release year of that movie.</param>
/// <param name="Directors">The director names of that movie.</param>
/// <param name="MovieId">The id of that movie.</param>
/// <param name="SearchTerm">A title word that matches a few percent of the movies.</param>
/// <param name="TopGenres">The ids of the two most used genres.</param>
/// <param name="TopGenreNames">The names of the two most used genres.</param>
/// <param name="MaxMovieId">The highest movie id after seeding.</param>
/// <param name="MaxDirectorId">The highest director id after seeding.</param>
/// <param name="MaxMediumId">The highest medium id after seeding.</param>
internal sealed record SampleData(
    int MovieCount,
    int MiddleMovieId,
    string Title,
    int Year,
    IReadOnlyList<string> Directors,
    int MovieId,
    string SearchTerm,
    IReadOnlyList<int> TopGenres,
    IReadOnlyList<string> TopGenreNames,
    int MaxMovieId,
    int MaxDirectorId,
    int MaxMediumId
);

/// <summary>
/// Deterministic synthetic data with the shape of the real database (PLAN-4 §5.2): about 0.6 directors per movie,
/// one to three genres, a handful of media, long reviews, a few missing scores, several viewings on some days and a few
/// same-title movies with another set of directors. It never uses the real Access data.
/// </summary>
internal static class SyntheticData
{
    private const int Seed = 20260101;

    private static readonly string[] TitleWords =
    [
        "Nacht",
        "Dag",
        "Zee",
        "Stad",
        "Huis",
        "Weg",
        "Land",
        "Vuur",
        "Water",
        "Wind",
        "Maan",
        "Zon",
        "Ster",
        "Droom",
        "Reis",
        "Liefde",
        "Dood",
        "Leven",
        "Spel",
        "Koning",
        "Koningin",
        "Held",
        "Schaduw",
        "Licht",
        "Stilte",
        "Storm",
        "Winter",
        "Zomer",
        "Herfst",
        "Lente",
        "Vriend",
        "Vijand",
        "Geheim",
        "Wraak",
        "Eiland",
        "Brug",
        "Toren",
        "Poort",
        "Bos",
        "Berg",
        "Rivier",
        "Straat",
        "Moord",
        "Bruid",
        "Zoon",
        "Dochter",
        "Vader",
        "Moeder",
        "Broer",
        "Zus",
        "Café",
        "Élan",
        "Île",
        "Œuvre",
        "Naïef",
        "Garçon",
        "Müller",
        "Ångström",
        "Señor",
        "Crème",
        "Brûlée",
        "Façade",
        "Night",
        "Day",
        "Sea",
        "City",
        "House",
        "Road",
        "Fire",
        "Wind",
        "Moon",
        "Sun",
        "Star",
        "Dream",
        "Journey",
        "Love",
        "Death",
        "Life",
        "Game",
        "King",
        "Queen",
        "Hero",
        "Shadow",
        "Light",
        "Silence",
        "Storm",
        "Secret",
        "Revenge",
        "Island",
        "Bridge",
        "Tower",
        "Gate",
        "Forest",
        "Mountain",
        "River",
        "Street",
        "Murder",
        "Bride",
        "De",
        "Het",
        "Een",
        "Van",
        "In",
        "Op",
        "Naar",
        "Zonder",
        "Voor",
        "Na",
        "The",
        "Of",
        "And",
        "A",
        "Le",
        "La",
    ];

    private static readonly string[] FirstNames =
    [
        "Jan",
        "Piet",
        "Joris",
        "Wim",
        "Luc",
        "Marc",
        "Eric",
        "Koen",
        "Bart",
        "Dirk",
        "Steven",
        "Peter",
        "Tom",
        "Kris",
        "Anne",
        "Marie",
        "Els",
        "Ines",
        "Lies",
        "Sofie",
        "Eva",
        "Nele",
        "Greet",
        "Hilde",
        "Françoise",
        "Zoë",
        "Chloé",
        "André",
        "René",
        "Jürgen",
        "Søren",
        "Åke",
        "Łukasz",
        "Renée",
        "Amélie",
        "Jean-Pierre",
        "Hans",
        "Klaus",
        "Akira",
        "Hayao",
        "Satyajit",
        "Pedro",
        "Guillermo",
        "Alejandro",
        "Ingmar",
        "Roman",
        "Federico",
        "Sergio",
    ];

    private static readonly string[] LastNames =
    [
        "Janssens",
        "Peeters",
        "Maes",
        "Jacobs",
        "Mertens",
        "Willems",
        "Claes",
        "Goossens",
        "Wouters",
        "De Smet",
        "Van den Berg",
        "Vandenberghe",
        "Dubois",
        "Lambert",
        "Dupont",
        "Leclercq",
        "Müller",
        "Schröder",
        "Lindqvist",
        "Bergström",
        "Kowalski",
        "Nowak",
        "García",
        "Fernández",
        "Rossi",
        "Ferrari",
        "Tarkovsky",
        "Kurosawa",
        "Miyazaki",
        "Bergman",
        "Polanski",
        "Fellini",
        "Leone",
        "Almodóvar",
        "Del Toro",
        "Iñárritu",
        "Ray",
        "Truffaut",
        "Godard",
        "Bresson",
        "Éluard",
        "Duchêne",
        "Lefèvre",
        "Boulanger",
        "Zielinski",
        "Åkesson",
        "Østergaard",
        "Haas",
        "Weber",
    ];

    private static readonly string[] Media =
    [
        "Bioscoop",
        "TV",
        "DVD",
        "Blu-ray",
        "Netflix",
        "Streaming",
        "Videotheek",
        "Laptop",
    ];

    private static readonly string[] ReviewWords =
    [
        "prachtig",
        "trage",
        "verhaal",
        "acteurs",
        "beeld",
        "muziek",
        "geluid",
        "regisseur",
        "scène",
        "einde",
        "spanning",
        "humor",
        "dialogen",
        "sfeer",
        "personages",
        "ritme",
        "camera",
        "decor",
        "kostuums",
        "vertolking",
        "verrassend",
        "voorspelbaar",
        "meeslepend",
        "teleurstellend",
        "indrukwekkend",
        "eigenzinnig",
        "klassieker",
        "film",
        "tegelijk",
        "bovendien",
        "echter",
        "hoewel",
        "omdat",
        "daarom",
        "toch",
        "vooral",
        "nogal",
        "heel",
    ];

    /// <summary>Seeds a migrated database.</summary>
    /// <param name="factory">The context factory of the database.</param>
    /// <param name="movieCount">The number of movies.</param>
    public static async Task<SampleData> SeedAsync(IDbContextFactory<MovieDbContext> factory, int movieCount)
    {
        var random = new Random(Seed);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var directorCount = Math.Max(1, (int)Math.Round(movieCount * 715.0 / 1203.0));
        var directors = BuildDirectors(random, directorCount, now);
        var media = Media.Select(name => new Medium { Name = name, CreatedAt = now }).ToList();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Directors.AddRange(directors);
            db.Media.AddRange(media);
            await db.SaveChangesAsync();
        }

        var dayPool = BuildDatePool(random, Math.Max(1, (int)(movieCount * 0.7)));
        var usedKeys = new HashSet<(string Title, int Year, string DirectorsKey)>();
        var movies = new List<Movie>(movieCount);
        for (var i = 0; i < movieCount; i++)
        {
            Movie movie;
            do
            {
                movie = BuildMovie(random, i, directors, media, dayPool, now);
            } while (!usedKeys.Add((movie.Title.ToUpperInvariant(), movie.ReleaseYear, movie.DirectorsKey)));

            movies.Add(movie);
        }

        AddSameTitleMovies(random, movies, directors, media, dayPool, now, usedKeys);

        foreach (var batch in movies.Chunk(500))
        {
            await using var db = await factory.CreateDbContextAsync();
            db.Movies.AddRange(batch);
            await db.SaveChangesAsync();
        }

        return await DescribeAsync(factory, movieCount);
    }

    private static List<Director> BuildDirectors(Random random, int count, DateTime now)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directors = new List<Director>(count);
        while (directors.Count < count)
        {
            var name = FirstNames[random.Next(FirstNames.Length)] + " " + LastNames[random.Next(LastNames.Length)];
            if (names.Count >= FirstNames.Length * LastNames.Length / 2)
            {
                name += " " + (directors.Count + 1).ToString(CultureInfo.InvariantCulture);
            }

            if (names.Add(RemoveDiacritics(name)))
            {
                directors.Add(new Director { Name = name, CreatedAt = now });
            }
        }

        return directors;
    }

    private static string RemoveDiacritics(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static List<DateOnly> BuildDatePool(Random random, int size)
    {
        var start = new DateOnly(2000, 1, 1);
        var days = new DateOnly(2025, 12, 31).DayNumber - start.DayNumber;
        return Enumerable.Range(0, size).Select(_ => start.AddDays(random.Next(days + 1))).ToList();
    }

    private static Movie BuildMovie(
        Random random,
        int index,
        List<Director> directors,
        List<Medium> media,
        List<DateOnly> dayPool,
        DateTime now
    )
    {
        var words = random.Next(1, 4);
        var title = string.Join(
            ' ',
            Enumerable.Range(0, words).Select(_ => TitleWords[random.Next(TitleWords.Length)])
        );
        var chosen = new List<Director>();
        // The first movies take every director once, so none is unused; later movies pick with a skew towards a few.
        chosen.Add(
            index < directors.Count
                ? directors[index]
                : directors[(int)(directors.Count * Math.Pow(random.NextDouble(), 2))]
        );
        if (random.NextDouble() < 0.004)
        {
            var second = directors[random.Next(directors.Count)];
            if (!chosen.Contains(second))
            {
                chosen.Add(second);
            }
        }

        var movie = new Movie
        {
            Title = title,
            ReleaseYear = random.Next(1950, 2026),
            RuntimeMinutes = random.Next(60, 241),
            WatchedOn = dayPool[random.Next(dayPool.Count)],
            MediumId = random.NextDouble() < 0.05 ? null : media[random.Next(media.Count)].Id,
            OverallScore = Score(random),
            StoryScore = Score(random),
            PictureScore = Score(random),
            MusicScore = Score(random),
            SoundScore = Score(random),
            Review = Review(random),
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
            DirectorsKey = DirectorsKey.Of(chosen.Select(d => d.Id)),
        };
        var position = 1;
        foreach (var director in chosen)
        {
            movie.DirectorLinks.Add(new MovieDirector { DirectorId = director.Id, Position = position++ });
        }

        var genreCount = random.Next(1, 4);
        var genreIds = new HashSet<int>();
        while (genreIds.Count < genreCount)
        {
            genreIds.Add(MovieGenres.All[(int)(MovieGenres.All.Count * Math.Pow(random.NextDouble(), 2))].Id);
        }

        foreach (var genreId in genreIds)
        {
            movie.GenreLinks.Add(new MovieGenre { GenreId = GenreId.From(genreId) });
        }

        return movie;
    }

    private static void AddSameTitleMovies(
        Random random,
        List<Movie> movies,
        List<Director> directors,
        List<Medium> media,
        List<DateOnly> dayPool,
        DateTime now,
        HashSet<(string Title, int Year, string DirectorsKey)> usedKeys
    )
    {
        var originals = Math.Max(1, movies.Count / 60);
        for (var i = 0; i < originals; i++)
        {
            var original = movies[random.Next(movies.Count)];
            var copy = BuildMovie(random, int.MaxValue, directors, media, dayPool, now);
            copy.Title = original.Title;
            copy.ReleaseYear = original.ReleaseYear;
            if (usedKeys.Add((copy.Title.ToUpperInvariant(), copy.ReleaseYear, copy.DirectorsKey)))
            {
                movies.Add(copy);
            }
        }
    }

    private static int? Score(Random random) => random.NextDouble() < 0.01 ? null : random.Next(1, 11);

    private static string Review(Random random)
    {
        var length = random.Next(200, 2001);
        var builder = new StringBuilder(length + 20);
        while (builder.Length < length)
        {
            builder.Append(ReviewWords[random.Next(ReviewWords.Length)]).Append(random.Next(14) == 0 ? ". " : " ");
        }

        return builder.ToString().TrimEnd();
    }

    private static async Task<SampleData> DescribeAsync(IDbContextFactory<MovieDbContext> factory, int movieCount)
    {
        await using var db = await factory.CreateDbContextAsync();
        var total = await db.Movies.CountAsync();
        var middle = await db
            .Movies.AsNoTracking()
            .OrderByDescending(m => m.WatchedOn)
            .ThenBy(m => m.Title)
            .Skip(total / 2)
            .Select(m => m.Id)
            .FirstAsync();

        var duplicate = await db
            .Movies.AsNoTracking()
            .GroupBy(m => new { m.Title, m.ReleaseYear })
            .Where(g => g.Count() > 1)
            .Select(g => new { g.Key.Title, g.Key.ReleaseYear })
            .OrderBy(g => g.Title)
            .FirstAsync();
        var sample = await db
            .Movies.AsNoTracking()
            .Where(m => m.Title == duplicate.Title && m.ReleaseYear == duplicate.ReleaseYear)
            .OrderBy(m => m.Id)
            .Select(m => new
            {
                m.Id,
                Directors = m.DirectorLinks.OrderBy(l => l.Position).Select(l => l.Director.Name).ToList(),
            })
            .FirstAsync();

        var topGenres = await db
            .MovieGenreLinks.AsNoTracking()
            .GroupBy(l => l.GenreId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Id)
            .Take(2)
            .ToListAsync();
        var genreIds = topGenres.ConvertAll(g => g.Id.Value);
        var maxMovie = await db.Movies.MaxAsync(m => m.Id);
        var maxDirector = await db.Directors.MaxAsync(d => d.Id);
        var maxMedium = await db.Media.MaxAsync(m => m.Id);

        return new SampleData(
            movieCount,
            middle.Value,
            duplicate.Title,
            duplicate.ReleaseYear,
            sample.Directors,
            sample.Id.Value,
            "Nacht",
            genreIds,
            MovieGenres.All.Where(g => genreIds.Contains(g.Id)).Select(g => g.Name).ToList(),
            maxMovie.Value,
            maxDirector.Value,
            maxMedium.Value
        );
    }
}
