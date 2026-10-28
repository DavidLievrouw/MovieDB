using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MovieDB.Infrastructure;

/// <summary>Design-time only (dotnet ef). Reads ConnectionStrings__MovieDb from the environment.</summary>
internal sealed class MovieDbContextDesignFactory : IDesignTimeDbContextFactory<MovieDbContext>
{
    public MovieDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__MovieDb")
            ?? "Server=localhost;Database=moviedb_design;User Id=design;";
        var options = new DbContextOptionsBuilder<MovieDbContext>();
        DatabaseConfiguration.Configure(options, connectionString);
        return new MovieDbContext(options.Options);
    }
}
