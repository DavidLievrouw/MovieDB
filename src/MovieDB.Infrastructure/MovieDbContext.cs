using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MovieDB.Common;
using MovieDB.Infrastructure.Auth;
using MovieDB.Infrastructure.Entities;
using Vogen;

namespace MovieDB.Infrastructure;

public sealed class MovieDbContext(DbContextOptions<MovieDbContext> options)
    : DbContext(options),
        IDataProtectionKeyContext
{
    /// <summary>The database collation: case- and accent-insensitive, so uniqueness and search behave as the owner expects.</summary>
    public const string Collation = "utf8mb4_uca1400_ai_ci";

    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Medium> Media => Set<Medium>();
    public DbSet<User> Users => Set<User>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    internal DbSet<AuthThrottleState> AuthThrottleStates => Set<AuthThrottleState>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.RegisterAllInVogenEfCoreConverters();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasCharSet("utf8mb4")
            .UseCollation(Collation)
            .ApplyConfigurationsFromAssembly(typeof(MovieDbContext).Assembly);
    }
}

[EfCoreConverter<MovieId>]
[EfCoreConverter<GenreId>]
[EfCoreConverter<MediumId>]
[EfCoreConverter<UserId>]
internal sealed partial class VogenEfCoreConverters;
