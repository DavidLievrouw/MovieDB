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
    public void Validation_error_carries_the_field_messages()
    {
        var error = Error.Validation("Title", "Titel is verplicht.");

        error.Kind.ShouldBe(ErrorKind.Validation);
        error.Fields.ShouldNotBeNull();
        error.Fields["Title"].ShouldBe(["Titel is verplicht."]);
    }
}
