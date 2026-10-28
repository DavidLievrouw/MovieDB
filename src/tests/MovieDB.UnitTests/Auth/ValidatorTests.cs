using MovieDB.Auth;
using Shouldly;

namespace MovieDB.UnitTests.Auth;

public sealed class ValidatorTests
{
    private static readonly LoginFormValidator Login = new();
    private static readonly ChangePasswordValidator Change = new();

    [Fact]
    public void Login_accepts_a_username_and_password_without_remember_me()
    {
        Login.Validate(new LoginForm { Username = "admin", Password = "demodemo" }).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, "x")]
    [InlineData("", "x")]
    [InlineData("admin", null)]
    [InlineData("admin", "")]
    public void Login_rejects_missing_values(string? username, string? password)
    {
        Login.Validate(new LoginForm { Username = username, Password = password }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Login_rejects_oversized_values()
    {
        Login.Validate(new LoginForm { Username = new string('a', 256), Password = "x" }).IsValid.ShouldBeFalse();
        Login.Validate(new LoginForm { Username = "admin", Password = new string('a', 257) }).IsValid.ShouldBeFalse();
    }

    private static ChangePasswordModel Valid() =>
        new()
        {
            CurrentPassword = "demodemo",
            NewPassword = "a brand new one",
            ConfirmPassword = "a brand new one",
        };

    [Fact]
    public void Change_password_accepts_a_valid_model() => Change.Validate(Valid()).IsValid.ShouldBeTrue();

    [Fact]
    public void Change_password_rejects_a_short_new_password_with_a_dutch_message()
    {
        var model = Valid();
        model.NewPassword = model.ConfirmPassword = "short";

        var result = Change.Validate(model);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.StartsWith("Het nieuwe wachtwoord moet minstens"));
    }

    [Fact]
    public void Change_password_rejects_a_mismatching_confirmation()
    {
        var model = Valid();
        model.ConfirmPassword = "something else";

        Change.Validate(model).Errors.ShouldContain(e => e.ErrorMessage == "De wachtwoorden komen niet overeen.");
    }

    [Fact]
    public void Change_password_rejects_reusing_the_current_password()
    {
        var model = Valid();
        model.NewPassword = model.ConfirmPassword = model.CurrentPassword;

        Change
            .Validate(model)
            .Errors.ShouldContain(e => e.ErrorMessage == "Het nieuwe wachtwoord moet verschillen van het huidige.");
    }

    [Fact]
    public void Change_password_rejects_missing_values()
    {
        Change.Validate(new ChangePasswordModel()).IsValid.ShouldBeFalse();
    }
}
