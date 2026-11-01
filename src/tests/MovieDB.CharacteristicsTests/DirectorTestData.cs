using Microsoft.EntityFrameworkCore;
using MovieDB.Infrastructure;
using MovieDB.Infrastructure.Entities;

namespace MovieDB.CharacteristicsTests;

/// <summary>Builds the director rows, links and key of a synthetic movie that is written straight through EF.</summary>
internal static class DirectorTestData
{
    private static readonly DateTime Created = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Finds or creates the named directors (saving the new ones) and assigns links and key to the movie.</summary>
    /// <param name="context">The context to look up and save the directors with.</param>
    /// <param name="movie">The movie to give directors.</param>
    /// <param name="names">The director names, in display order.</param>
    public static async Task AssignAsync(MovieDbContext context, Movie movie, IReadOnlyList<string> names)
    {
        var directors = new List<Director>();
        foreach (var name in names)
        {
            var director =
                await context.Directors.FirstOrDefaultAsync(d => d.Name == name, TestContext.Current.CancellationToken)
                ?? context.Directors.Add(new Director { Name = name, CreatedAt = Created }).Entity;
            directors.Add(director);
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        movie.DirectorLinks =
        [
            .. directors.Select((d, index) => new MovieDirector { DirectorId = d.Id, Position = index + 1 }),
        ];
        movie.DirectorsKey = DirectorsKey.Of(directors.Select(d => d.Id));
    }
}
