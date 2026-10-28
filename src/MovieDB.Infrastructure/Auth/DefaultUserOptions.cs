namespace MovieDB.Infrastructure.Auth;

public sealed class DefaultUserOptions
{
    public const string Section = "Auth";

    /// <summary>
    /// Password of the default <c>admin</c> user, used only when the <c>users</c> table is empty. Environment variable
    /// <c>Auth__DefaultPassword</c>; never stored in source.
    /// </summary>
    public string? DefaultPassword { get; set; }
}
