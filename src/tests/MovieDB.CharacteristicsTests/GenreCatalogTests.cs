using Microsoft.EntityFrameworkCore;
using MovieDB.Infrastructure;
using MovieDB.Movies;
using Shouldly;

namespace MovieDB.CharacteristicsTests;

[Collection(MariaDbCollection.Name)]
public sealed class GenreCatalogTests
{
    private readonly MariaDbFixture _fixture;

    public GenreCatalogTests(MariaDbFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class CountingFactory : IDbContextFactory<MovieDbContext>
    {
        private readonly IDbContextFactory<MovieDbContext> _inner;

        public CountingFactory(IDbContextFactory<MovieDbContext> inner, int failures)
        {
            _inner = inner;
            Failures = failures;
        }

        public int Calls { get; private set; }

        public int Failures { get; private set; }

        public MovieDbContext CreateDbContext() => throw new NotSupportedException();

        public Task<MovieDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Failures > 0)
            {
                Failures--;
                throw new InvalidOperationException("Simulated failure.");
            }

            return _inner.CreateDbContextAsync(cancellationToken);
        }
    }

    private async Task<CountingFactory> CreateFactoryAsync(int failures = 0)
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        return new CountingFactory(MariaDbFixture.CreateFactory(connectionString), failures);
    }

    [Fact]
    public async Task The_catalog_has_every_seeded_genre_alphabetically_with_its_stable_id()
    {
        var catalog = new GenreCatalog(await CreateFactoryAsync());

        var genres = await catalog.ListAsync(TestContext.Current.CancellationToken);

        genres.Count.ShouldBe(MovieGenres.All.Count);
        genres.Select(g => g.Name).ShouldBe(genres.Select(g => g.Name).Order(StringComparer.OrdinalIgnoreCase));
        genres.Select(g => (g.Id, g.Name)).Order().ShouldBe(MovieGenres.All.Order());
    }

    [Fact]
    public async Task The_catalog_loads_once_for_many_and_concurrent_calls()
    {
        var factory = await CreateFactoryAsync();
        var catalog = new GenreCatalog(factory);
        var ct = TestContext.Current.CancellationToken;

        var lists = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => catalog.ListAsync(ct)));
        await catalog.ListAsync(ct);

        factory.Calls.ShouldBe(1);
        lists.ShouldAllBe(l => ReferenceEquals(l, lists[0]));
    }

    [Fact]
    public async Task A_failed_load_is_not_cached_and_the_next_call_tries_again()
    {
        var factory = await CreateFactoryAsync(failures: 1);
        var catalog = new GenreCatalog(factory);
        var ct = TestContext.Current.CancellationToken;

        await Should.ThrowAsync<InvalidOperationException>(() => catalog.ListAsync(ct));
        var genres = await catalog.ListAsync(ct);

        genres.Count.ShouldBe(MovieGenres.All.Count);
        factory.Calls.ShouldBe(2);
    }
}
