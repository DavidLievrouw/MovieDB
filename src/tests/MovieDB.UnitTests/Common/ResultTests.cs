using MovieDB.Common;
using Shouldly;

namespace MovieDB.UnitTests.Common;

public class ResultTests
{
    [Fact]
    public void Ok_exposes_the_value()
    {
        var result = Result<int>.Ok(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
        Should.Throw<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Error_converts_implicitly_to_a_failed_result()
    {
        Result<int> result = Error.NotFound();

        result.IsSuccess.ShouldBeFalse();
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void External_service_errors_have_their_own_kinds_and_no_field_messages()
    {
        var errors = new (Error Error, ErrorKind Kind)[]
        {
            (Error.NotConfigured("no token"), ErrorKind.NotConfigured),
            (Error.Unavailable("down"), ErrorKind.Unavailable),
            (Error.RateLimited("slow down"), ErrorKind.RateLimited),
            (Error.InvalidResponse("garbage"), ErrorKind.InvalidResponse),
            (Error.Misconfigured("bad token"), ErrorKind.Misconfigured),
        };

        foreach (var (error, kind) in errors)
        {
            error.Kind.ShouldBe(kind);
            error.Fields.ShouldBeNull();
            error.Message.ShouldNotBeNullOrWhiteSpace();
        }

        errors.Select(e => e.Kind).Distinct().Count().ShouldBe(errors.Length);
    }

    [Fact]
    public void Validation_error_carries_the_field_messages()
    {
        var error = Error.Validation("Title", "Titel is verplicht.");

        error.Kind.ShouldBe(ErrorKind.Validation);
        error.Fields.ShouldNotBeNull();
        error.Fields["Title"].ShouldBe(["Titel is verplicht."]);
    }
}
