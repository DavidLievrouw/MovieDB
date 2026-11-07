using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MovieDB.Movies.Lookup;
using Shouldly;

namespace MovieDB.UnitTests.Movies.Lookup;

public sealed class TmdbOptionsValidatorTests
{
    private static readonly TmdbOptionsValidator Validator = new();

    [Fact]
    public void Defaults_are_valid_and_not_configured()
    {
        var options = new TmdbOptions();

        Validator.Validate(null, options).Succeeded.ShouldBeTrue();
        options.Token.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_token_is_valid(string? token)
    {
        Validator.Validate(null, new TmdbOptions { ApiReadAccessToken = token }).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Token_is_trimmed_when_used()
    {
        new TmdbOptions { ApiReadAccessToken = "  abc.def  " }.Token.ShouldBe("abc.def");
    }

    [Theory]
    [InlineData("abc def")]
    [InlineData("abc\ndef")]
    [InlineData("abc\tdef")]
    public void Token_with_inner_whitespace_or_control_characters_is_invalid(string token)
    {
        Validator.Validate(null, new TmdbOptions { ApiReadAccessToken = token }).Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://api.themoviedb.org/3/", true)]
    [InlineData("https://example.test/v3/", true)]
    [InlineData("http://api.themoviedb.org/3/", false)]
    [InlineData("https://api.themoviedb.org/3", false)]
    [InlineData("api.themoviedb.org/3/", false)]
    [InlineData("/3/", false)]
    [InlineData("", false)]
    [InlineData("https://user:pw@api.themoviedb.org/3/", false)]
    [InlineData("https://api.themoviedb.org/3/?x=1", false)]
    public void BaseUrl_must_be_absolute_https_ending_in_a_slash(string url, bool valid)
    {
        Validator.Validate(null, new TmdbOptions { BaseUrl = url }).Succeeded.ShouldBe(valid);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    [InlineData(-5, false)]
    public void TimeoutSeconds_is_between_one_and_thirty(int seconds, bool valid)
    {
        Validator.Validate(null, new TmdbOptions { TimeoutSeconds = seconds }).Succeeded.ShouldBe(valid);
    }

    [Fact]
    public void Validation_messages_never_contain_the_token()
    {
        var result = Validator.Validate(
            null,
            new TmdbOptions
            {
                ApiReadAccessToken = "tok en",
                BaseUrl = "nope",
                TimeoutSeconds = 0,
            }
        );

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldNotContain("tok en");
    }

    [Fact]
    public void Invalid_configuration_fails_when_the_options_are_resolved()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Tmdb:BaseUrl"] = "http://insecure/" })
            .Build();
        var services = new ServiceCollection();
        services.AddOptions<TmdbOptions>().Bind(configuration.GetSection(TmdbOptions.Section));
        services.AddSingleton<IValidateOptions<TmdbOptions>, TmdbOptionsValidator>();
        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<TmdbOptions>>().Value);
    }
}
