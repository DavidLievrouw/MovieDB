using MovieDB.Infrastructure;
using Shouldly;

namespace MovieDB.UnitTests.Infrastructure;

public sealed class DatabaseConfigurationTests
{
    [Theory]
    [InlineData("Server=localhost;Database=moviedb;CharSet=utf8mb4")]
    [InlineData("Server=localhost;Database=moviedb;CharSet=UTF8MB4")]
    public void RequireUtf8mb4_accepts_utf8mb4(string connectionString) =>
        DatabaseConfiguration.RequireUtf8mb4(connectionString).ShouldBe(connectionString);

    [Theory]
    [InlineData("Server=localhost;Database=moviedb")]
    [InlineData("Server=localhost;Database=moviedb;CharSet=latin1")]
    public void RequireUtf8mb4_rejects_other_character_sets(string connectionString) =>
        Should.Throw<InvalidOperationException>(() => DatabaseConfiguration.RequireUtf8mb4(connectionString));
}
