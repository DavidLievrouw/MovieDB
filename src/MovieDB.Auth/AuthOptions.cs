namespace MovieDB.Auth;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>Marks the auth cookie Secure. Switch off only for plain-http local development.</summary>
    public bool SecureCookies { get; set; } = true;
}
