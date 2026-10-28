using Shouldly;

namespace MovieDB.BrowserTests;

/// <summary>Playwright flows are added from milestone M4 on.</summary>
public class ProjectWiringTests
{
    [Fact]
    public void Host_exposes_program_for_test_hosting() =>
        typeof(Program).Assembly.GetName().Name.ShouldBe("MovieDB.Web");
}
