using MovieDB.Infrastructure;
using Shouldly;

namespace MovieDB.UnitTests.Infrastructure;

public sealed class MovieGenresTests
{
    [Fact]
    public void The_list_has_the_35_genres_with_stable_ids_1_to_35()
    {
        MovieGenres.All.Select(g => g.Id).ShouldBe(Enumerable.Range(1, 35));
    }

    [Fact]
    public void The_names_are_unique_non_empty_and_fit_the_column()
    {
        MovieGenres.All.Select(g => g.Name).ShouldAllBe(n => n.Length > 0 && n.Length <= MovieGenres.MaxNameLength);
        MovieGenres.All.Select(g => g.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(35);
    }

    [Fact]
    public void The_first_21_ids_are_the_migrated_alphabetical_list()
    {
        var first = MovieGenres.All.Take(21).Select(g => g.Name).ToList();

        first.ShouldBe([
            "Actie",
            "Andere",
            "Animatie",
            "Avontuur",
            "Bond",
            "Detective",
            "Detective-thriller",
            "Documentaire",
            "Drama",
            "Horror",
            "Kinder",
            "Komedie",
            "Melodrama",
            "Misdaad",
            "Oorlogs",
            "Rampen",
            "Romantisch",
            "Sci-fi",
            "Thriller",
            "Western",
            "Zwarte komedie",
        ]);
    }

    [Fact]
    public void A_movie_has_at_most_four_genres() => MovieGenres.MaxPerMovie.ShouldBe(4);
}
