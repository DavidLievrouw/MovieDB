using MovieDB.Common;
using MovieDB.Infrastructure;
using MovieDB.Movies.Lookup;
using Shouldly;

namespace MovieDB.UnitTests.Movies.Lookup;

public sealed class LookupMessagesAndGenreMapTests
{
    [Fact]
    public void Every_mapped_genre_exists_in_the_closed_genre_list()
    {
        foreach (var id in TmdbGenreMap.MappedIds)
        {
            TmdbGenreMap.TryGetName(id, out var name).ShouldBeTrue();
            MovieGenres.All.Select(g => g.Name).ShouldContain(name, $"TMDB genre {id} maps to an unknown genre.");
        }
    }

    [Fact]
    public void Tv_movie_and_unknown_ids_are_not_mapped()
    {
        TmdbGenreMap.TryGetName(TmdbGenreMap.TvMovieId, out _).ShouldBeFalse();
        TmdbGenreMap.TryGetName(-1, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(28, "Actie")]
    [InlineData(878, "Sci-fi")]
    [InlineData(10752, "Oorlogs")]
    [InlineData(36, "Historisch")]
    public void Maps_by_tmdb_id(int id, string expected)
    {
        TmdbGenreMap.TryGetName(id, out var name).ShouldBeTrue();
        name.ShouldBe(expected);
    }

    [Theory]
    [InlineData(ErrorKind.NotConfigured)]
    [InlineData(ErrorKind.NotFound)]
    [InlineData(ErrorKind.Misconfigured)]
    [InlineData(ErrorKind.RateLimited)]
    [InlineData(ErrorKind.Unavailable)]
    [InlineData(ErrorKind.InvalidResponse)]
    public void Every_provider_error_kind_has_a_dutch_message_that_hides_the_technical_one(ErrorKind kind)
    {
        const string technical = "TECHNICAL-DETAIL";

        var message = LookupMessages.For(kind);

        message.ShouldNotBeNullOrWhiteSpace();
        message.ShouldNotContain(technical);
        new Error(kind, technical).Message.ShouldBe(technical);
    }

    [Fact]
    public void Credential_and_connectivity_failures_have_different_messages()
    {
        LookupMessages.For(ErrorKind.Misconfigured).ShouldNotBe(LookupMessages.For(ErrorKind.Unavailable));
    }

    [Fact]
    public void No_results_message_quotes_the_text()
    {
        LookupMessages
            .NoResults("matirx")
            .ShouldBe("Geen films gevonden voor \"matirx\". Controleer de spelling of probeer de originele titel.");
    }
}
