using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MovieDB.Infrastructure;
using MySqlConnector;
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
    [InlineData("Server=localhost;Maximum Pool Size=20")]
    [InlineData("Server=localhost;MaximumPoolSize=20")]
    [InlineData("Server=localhost;Max Pool Size=20")]
    [InlineData("Server=localhost;maxpoolsize=20")]
    public void ContainsPoolSize_recognizes_every_name_of_the_option(string connectionString) =>
        DatabaseConfiguration.ContainsPoolSize(connectionString).ShouldBeTrue();

    [Fact]
    public void ContainsPoolSize_is_false_when_the_option_is_not_set() =>
        DatabaseConfiguration.ContainsPoolSize("Server=localhost;Database=moviedb;CharSet=utf8mb4").ShouldBeFalse();

    [Theory]
    [InlineData("Server=localhost;Database=moviedb;CharSet=utf8mb4", 8u)]
    [InlineData("Server=localhost;Database=moviedb;CharSet=utf8mb4;Maximum Pool Size=20", 20u)]
    public void Configure_limits_the_connection_pool_unless_the_connection_string_sets_it(
        string connectionString,
        uint expected
    )
    {
        var options = new DbContextOptionsBuilder<MovieDbContext>();

        DatabaseConfiguration.Configure(options, connectionString);

        var connection = RelationalOptionsExtension.Extract(options.Options).ConnectionString!;
        new MySqlConnectionStringBuilder(connection).MaximumPoolSize.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Server=localhost;Database=moviedb")]
    [InlineData("Server=localhost;Database=moviedb;CharSet=latin1")]
    public void RequireUtf8mb4_rejects_other_character_sets(string connectionString) =>
        Should.Throw<InvalidOperationException>(() => DatabaseConfiguration.RequireUtf8mb4(connectionString));
}
