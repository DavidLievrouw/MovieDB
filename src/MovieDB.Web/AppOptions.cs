using System.ComponentModel.DataAnnotations;

namespace MovieDB.Web;

public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>IANA time zone that defines "today" and the display of timestamps.</summary>
    [Required]
    public string TimeZone { get; set; } = "Europe/Brussels";
}
