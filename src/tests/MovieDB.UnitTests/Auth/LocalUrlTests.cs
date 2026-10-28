using MovieDB.Common;
using Shouldly;

namespace MovieDB.UnitTests.Auth;

public sealed class LocalUrlTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/films")]
    [InlineData("/films/5?x=1")]
    [InlineData("/films/nieuw#top")]
    public void Relative_paths_are_local(string url) => LocalUrl.IsLocal(url).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("films")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("https://evil.example/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/films\r\nSet-Cookie: x=1")]
    [InlineData("/a\\b")]
    public void Everything_else_is_not_local(string? url) => LocalUrl.IsLocal(url).ShouldBeFalse();

    [Fact]
    public void OrDefault_falls_back_for_non_local_urls()
    {
        LocalUrl.OrDefault("/films").ShouldBe("/films");
        LocalUrl.OrDefault("//evil.example").ShouldBe("/");
        LocalUrl.OrDefault(null, "/films").ShouldBe("/films");
    }
}
