using MovieDB.AccessImport;
using Shouldly;

namespace MovieDB.UnitTests.Import;

public sealed class DuplicateResolverTests
{
    private static CleanMovie Movie(
        int id,
        string title = "Title",
        int year = 2000,
        string director = "Director",
        DateOnly? watchedOn = null
    ) =>
        new(
            id,
            title,
            year,
            director,
            "Drama",
            100,
            watchedOn ?? new DateOnly(2020, 1, 1),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );

    [Fact]
    public void Oldest_viewing_is_kept()
    {
        var (kept, groups) = DuplicateResolver.Resolve([
            Movie(1, watchedOn: new DateOnly(2021, 1, 1)),
            Movie(2, watchedOn: new DateOnly(2019, 1, 1)),
        ]);

        kept.Select(m => m.Id).ShouldBe([2]);
        groups.ShouldHaveSingleItem().KeptId.ShouldBe(2);
        groups[0].DroppedIds.ShouldBe([1]);
    }

    [Fact]
    public void Same_date_keeps_the_lowest_id()
    {
        var (kept, groups) = DuplicateResolver.Resolve([Movie(9), Movie(3), Movie(5)]);

        kept.Single().Id.ShouldBe(3);
        groups.Single().DroppedIds.ShouldBe([5, 9]);
    }

    [Theory]
    [InlineData("TITLE", "director")]
    [InlineData("Títle", "Dîrector")]
    [InlineData("Title ", "Director")]
    public void Case_accent_and_trailing_space_variants_are_duplicates(string title, string director)
    {
        var (kept, groups) = DuplicateResolver.Resolve([Movie(1), Movie(2, title, director: director)]);

        kept.Count.ShouldBe(1);
        groups.Count.ShouldBe(1);
    }

    [Fact]
    public void Different_year_or_director_is_a_different_movie()
    {
        var (kept, groups) = DuplicateResolver.Resolve([
            Movie(1),
            Movie(2, year: 2001),
            Movie(3, director: "Someone Else"),
        ]);

        kept.Count.ShouldBe(3);
        groups.ShouldBeEmpty();
    }

    [Fact]
    public void Result_is_ordered_by_id()
    {
        var (kept, _) = DuplicateResolver.Resolve([Movie(5, "E"), Movie(2, "B"), Movie(9, "I")]);

        kept.Select(m => m.Id).ShouldBe([2, 5, 9]);
    }
}

public sealed class AttributeCorrectionTests
{
    private static CleanMovie Movie(int id, string title = "Goldeneye", int runtime = 145, string genre = "Actie") =>
        new(
            id,
            title,
            1995,
            "Director",
            genre,
            runtime,
            new DateOnly(2020, 1, 1),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );

    [Fact]
    public void Corrections_apply_when_the_expected_value_is_found()
    {
        var (movies, outcomes) = AttributeCorrections.Apply(
            [Movie(33), Movie(148, runtime: 132), Movie(308, genre: "Actie")],
            AttributeCorrections.Known
        );

        movies.Single(m => m.Id == 33).Title.ShouldBe("GoldenEye");
        movies.Single(m => m.Id == 33).RuntimeMinutes.ShouldBe(130);
        movies.Single(m => m.Id == 148).RuntimeMinutes.ShouldBe(133);
        movies.Single(m => m.Id == 308).Genre.ShouldBe("Sci-fi");
        outcomes.Count(o => o.Applied).ShouldBe(4);
    }

    [Fact]
    public void Corrections_are_skipped_when_the_source_differs_or_the_row_is_absent()
    {
        var (movies, outcomes) = AttributeCorrections.Apply(
            [Movie(33, title: "GoldenEye", runtime: 120)],
            AttributeCorrections.Known
        );

        movies.Single().RuntimeMinutes.ShouldBe(120);
        outcomes.ShouldAllBe(o => !o.Applied);
        outcomes.Count.ShouldBe(AttributeCorrections.Known.Count);
    }

    [Fact]
    public void Known_corrections_are_the_six_imdb_verified_ones() =>
        AttributeCorrections
            .Known.Select(c => (c.FilmId, c.Field))
            .ShouldBe([
                (33, "Title"),
                (33, "Runtime"),
                (148, "Runtime"),
                (308, "Genre"),
                (328, "Runtime"),
                (765, "Runtime"),
            ]);
}

public sealed class ImportPlannerTests
{
    [Fact]
    public void Plan_counts_rows_drops_duplicates_and_seeds_suggestions()
    {
        var plan = ImportPlanner.Plan([
            SyntheticFilms.Film(1, title: "A", genre: "Drama"),
            SyntheticFilms.Film(
                2,
                title: "a",
                genre: "DRAMA",
                watchedOn: new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
            ),
            SyntheticFilms.Film(3, title: "B", genre: "Ongekend", medium: "Laserdisc"),
        ]);

        plan.HasErrors.ShouldBeFalse();
        plan.SourceRows.ShouldBe(3);
        plan.Movies.Select(m => m.Id).ShouldBe([1, 3]);
        plan.DroppedRows.ShouldBe(1);
        plan.Genres.Count.ShouldBe(ImportPlanner.KnownGenres.Count + 1);
        plan.Genres.ShouldContain("Ongekend");
        plan.Media.ShouldContain("Laserdisc");
        plan.Media.Count.ShouldBe(ImportPlanner.KnownMedia.Count + 1);
    }

    [Fact]
    public void Genre_and_medium_values_resolve_to_the_canonical_suggestion()
    {
        var plan = ImportPlanner.Plan([SyntheticFilms.Film(1, genre: " drama ", medium: "dvd")]);

        plan.Movies.Single().Genre.ShouldBe("Drama");
        plan.Movies.Single().Medium.ShouldBe("DVD");
        plan.Genres.Count.ShouldBe(ImportPlanner.KnownGenres.Count);
    }

    [Fact]
    public void Errors_from_all_rows_are_collected_and_duplicate_ids_are_rejected()
    {
        var plan = ImportPlanner.Plan([
            SyntheticFilms.Film(1, duration: "x"),
            SyntheticFilms.Film(2, year: "x"),
            SyntheticFilms.Film(3),
            SyntheticFilms.Film(3, title: "Other"),
        ]);

        plan.HasErrors.ShouldBeTrue();
        plan.Errors.Select(e => e.FilmId).Distinct().ShouldBe([1, 2, 3], ignoreOrder: true);
    }

    [Fact]
    public void Stats_describe_the_planned_movies()
    {
        var plan = ImportPlanner.Plan([
            SyntheticFilms.Film(1, extras: "n/a", medium: null, review: null),
            SyntheticFilms.Film(
                2,
                title: "Other",
                watchedOn: new DateTime(2019, 3, 4, 0, 0, 0, DateTimeKind.Unspecified),
                overall: 0
            ),
        ]);

        var stats = plan.Stats;
        stats.Movies.ShouldBe(2);
        stats.NullExtras.ShouldBe(1);
        stats.NullMedium.ShouldBe(1);
        stats.NullReview.ShouldBe(1);
        stats.NullOverall.ShouldBe(1);
        stats.MaxId.ShouldBe(2);
        stats.MinWatchedOn.ShouldBe(new DateOnly(2019, 3, 4));
    }

    [Fact]
    public void Report_contains_counts_but_no_film_content()
    {
        var plan = ImportPlanner.Plan([
            SyntheticFilms.Film(1, title: "SECRET-TITLE", director: "SECRET-DIRECTOR", review: "SECRET-REVIEW"),
            SyntheticFilms.Film(2, title: "secret-title", director: "SECRET-DIRECTOR", review: "SECRET-OTHER"),
        ]);

        var report = string.Join('\n', ImportReport.Render(plan, null, dryRun: true));

        report.ShouldContain("DRY RUN");
        report.ShouldContain("1 <- 2");
        report.ShouldContain("n/a (dry run)");
        report.ShouldNotContain("SECRET", Case.Insensitive);
    }
}

public sealed class DumpComposerTests
{
    [Fact]
    public void Dump_is_guarded_and_wrapped_in_a_transaction()
    {
        var script = DumpComposer.Compose(
            "INSERT INTO `movies` VALUES (1);\r\n",
            "FilmsBase.mdb",
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            "20260101000000_Initial",
            new DataStats(1, 2, 3, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, null, null, 1)
        );

        script.ShouldContain("-- Source: FilmsBase.mdb");
        script.ShouldContain("-- Rows: movies=1, genres=2, media=3");
        script.ShouldContain("latest <> '20260101000000_Initial'");
        script.ShouldContain("SIGNAL SQLSTATE '45000'");
        script
            .IndexOf("START TRANSACTION;", StringComparison.Ordinal)
            .ShouldBeLessThan(script.IndexOf("INSERT INTO `movies`", StringComparison.Ordinal));
        script.ShouldEndWith("\nCOMMIT;\n");
        script.ShouldNotContain("\r");
    }
}
