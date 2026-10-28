using System.Reflection;
using Microsoft.AspNetCore.Components;
using MovieDB.Auth;
using MovieDB.Common;
using MovieDB.Infrastructure;
using MovieDB.Movies;
using Shouldly;

namespace MovieDB.ArchitectureTests;

/// <summary>
/// Only types that another production assembly (or the host) must use are public. Adding a type to these lists
/// needs a reason; tests reach internals through InternalsVisibleTo. Razor components are always public and are skipped.
/// </summary>
public class PublicSurfaceTests
{
    private static readonly Dictionary<Assembly, string[]> AllowedPublicTypes = new()
    {
        [typeof(UserClock).Assembly] =
        [
            "Error",
            "ErrorKind",
            "GenreId",
            "LocalUrl",
            "MediumId",
            "MovieId",
            "Result",
            "Result`1",
            "Unit",
            "UserClock",
            "UserId",
        ],
        [typeof(MovieDbContext).Assembly] =
        [
            "AuthAdmission",
            "AuthAttemptOutcome",
            "AuthExtensions",
            "AuthProtectionOptions",
            "AuthReservation",
            "AuthThrottleStore",
            "DatabaseConfiguration",
            "DatabaseMigrator",
            "DefaultUserOptions",
            "DefaultUserSeeder",
            "Genre",
            "Medium",
            "Movie",
            "MovieDbContext",
            "User",
        ],
        [typeof(AuthModule).Assembly] = ["AuthEndpoints", "AuthModule", "AuthOptions"],
        [typeof(MoviesModule).Assembly] = ["MoviesModule"],
    };

    /// <summary>Public types emitted by generators that cannot be made internal.</summary>
    private static readonly string[] GeneratedTypes = ["VogenTypesFactory", "_Imports"];

    public static TheoryData<string> Assemblies => [.. AllowedPublicTypes.Keys.Select(a => a.GetName().Name!)];

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void Assembly_exposes_only_the_allowed_public_types(string assemblyName)
    {
        var assembly = AllowedPublicTypes.Keys.Single(a => a.GetName().Name == assemblyName);

        var actual = assembly
            .GetExportedTypes()
            .Where(type =>
                !typeof(IComponent).IsAssignableFrom(type)
                && type.DeclaringType is null
                && !GeneratedTypes.Contains(type.Name)
            )
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal);

        actual.ShouldBe(AllowedPublicTypes[assembly].Order(StringComparer.Ordinal));
    }
}
