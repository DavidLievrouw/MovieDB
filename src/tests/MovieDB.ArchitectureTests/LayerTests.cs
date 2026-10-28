using System.Xml.Linq;
using NetArchTest.Rules;
using Shouldly;

namespace MovieDB.ArchitectureTests;

public class LayerTests
{
    private static readonly string SrcDir = FindSrcDir();

    private static readonly string[] Features = ["MovieDB.Auth", "MovieDB.Movies"];

    /// <summary>
    /// Allowed project references per project (PLAN.md §4.2). The compiler drops unused references, so the
    /// csproj files are the source of truth rather than the compiled assemblies.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedProjectReferences = new()
    {
        ["MovieDB.Common"] = [],
        ["MovieDB.Ui"] = ["MovieDB.Common"],
        ["MovieDB.Infrastructure"] = ["MovieDB.Common"],
        ["MovieDB.Auth"] = ["MovieDB.Common", "MovieDB.Ui", "MovieDB.Infrastructure"],
        ["MovieDB.Movies"] = ["MovieDB.Common", "MovieDB.Ui", "MovieDB.Infrastructure"],
        ["MovieDB.Web"] = ["MovieDB.Common", "MovieDB.Ui", "MovieDB.Infrastructure", "MovieDB.Auth", "MovieDB.Movies"],
        ["tools/MovieDB.AccessImport"] = ["MovieDB.Common", "MovieDB.Infrastructure"],
    };

    public static TheoryData<string> Projects => [.. AllowedProjectReferences.Keys];

    [Theory]
    [MemberData(nameof(Projects))]
    public void Project_references_only_allowed_projects(string project)
    {
        var actual = ProjectReferences(project);

        actual.Except(AllowedProjectReferences[project]).ShouldBeEmpty();
    }

    [Fact]
    public void Features_do_not_reference_each_other()
    {
        foreach (var feature in Features)
        {
            ProjectReferences(feature).Intersect(Features).ShouldBeEmpty();
        }
    }

    [Fact]
    public void Production_projects_do_not_reference_tools_or_tests()
    {
        foreach (var project in AllowedProjectReferences.Keys.Where(p => !p.StartsWith("tools/")))
        {
            ProjectReferences(project).ShouldAllBe(r => !r.Contains("AccessImport") && !r.Contains("Tests"));
        }
    }

    [Fact]
    public void Common_has_no_package_dependency_on_ef_or_aspnetcore()
    {
        var packages = PackageReferences("MovieDB.Common");

        packages.ShouldNotContain(p =>
            p.StartsWith("Microsoft.EntityFrameworkCore") || p.StartsWith("Microsoft.AspNetCore")
        );
        FrameworkReferences("MovieDB.Common").ShouldBeEmpty();
    }

    [Fact]
    public void Common_types_do_not_depend_on_ef_or_aspnetcore()
    {
        var result = Types
            .InAssembly(typeof(MovieDB.Common.Error).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Ui_types_do_not_depend_on_features_or_infrastructure()
    {
        var result = Types
            .InAssembly(MovieDB.Ui.UiAssembly.Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "MovieDB.Auth",
                "MovieDB.Movies",
                "MovieDB.Infrastructure",
                "Microsoft.EntityFrameworkCore"
            )
            .GetResult();

        result.IsSuccessful.ShouldBeTrue();
    }

    private static string[] ProjectReferences(string project) =>
        Load(project)
            .Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension((string)e.Attribute("Include")!))
            .ToArray();

    private static string[] PackageReferences(string project) =>
        Load(project).Descendants("PackageReference").Select(e => (string)e.Attribute("Include")!).ToArray();

    private static string[] FrameworkReferences(string project) =>
        Load(project).Descendants("FrameworkReference").Select(e => (string)e.Attribute("Include")!).ToArray();

    private static XElement Load(string project)
    {
        var name = Path.GetFileName(project);
        return XDocument.Load(Path.Combine(SrcDir, project, name + ".csproj")).Root!;
    }

    private static string FindSrcDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MovieDB.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate src/MovieDB.slnx.");
    }
}
