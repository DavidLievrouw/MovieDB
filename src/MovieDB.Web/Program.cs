using MovieDB.Auth;
using MovieDB.Movies;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog(
    (context, configuration) => configuration.ReadFrom.Configuration(context.Configuration).WriteTo.Console()
);

builder.Services.AddAuthServices().AddMoviesServices();

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok()).AllowAnonymous();

await app.RunAsync();

public partial class Program;
