using FluentValidation;
using MovieDB.Infrastructure.Auth;

namespace MovieDB.Auth;

internal sealed record LoginForm
{
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? ReturnUrl { get; init; }

    /// <summary>An unchecked checkbox is absent from the posted form.</summary>
    public bool RememberMe { get; init; }
}

internal sealed class LoginFormValidator : AbstractValidator<LoginForm>
{
    public const int MaxUsernameLength = 255;
    public const int MaxPasswordLength = 256;

    public LoginFormValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(MaxUsernameLength);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(MaxPasswordLength);
    }
}

internal sealed class ChangePasswordModel
{
    public string? CurrentPassword { get; set; }

    public string? NewPassword { get; set; }

    public string? ConfirmPassword { get; set; }

    public string? ReturnUrl { get; set; }
}

internal sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordModel>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty()
            .WithMessage("Geef je huidige wachtwoord in.")
            .MaximumLength(LoginFormValidator.MaxPasswordLength)
            .WithMessage($"Het wachtwoord mag hoogstens {LoginFormValidator.MaxPasswordLength} tekens bevatten.");
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .WithMessage("Geef een nieuw wachtwoord in.")
            .MinimumLength(AuthExtensions.MinimumPasswordLength)
            .WithMessage($"Het nieuwe wachtwoord moet minstens {AuthExtensions.MinimumPasswordLength} tekens bevatten.")
            .MaximumLength(LoginFormValidator.MaxPasswordLength)
            .WithMessage($"Het wachtwoord mag hoogstens {LoginFormValidator.MaxPasswordLength} tekens bevatten.")
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("Het nieuwe wachtwoord moet verschillen van het huidige.");
        RuleFor(x => x.ConfirmPassword).Equal(x => x.NewPassword).WithMessage("De wachtwoorden komen niet overeen.");
    }
}
