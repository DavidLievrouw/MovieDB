using System.ComponentModel.DataAnnotations;

namespace MovieDB.Infrastructure.Auth;

public sealed class AuthProtectionOptions
{
    public const string Section = "Auth:Protection";

    [Range(1, 1000)]
    public int AccountFailures { get; set; } = 5;

    [Range(1, 3600)]
    public int AccountWindowSeconds { get; set; } = 900;

    [Range(1, 1000)]
    public int IpAttempts { get; set; } = 20;

    [Range(1, 1000)]
    public int DeviceAttempts { get; set; } = 10;

    [Range(1, 3600)]
    public int SourceWindowSeconds { get; set; } = 300;

    [Range(1, 10000)]
    public int GlobalAttempts { get; set; } = 60;

    [Range(1, 3600)]
    public int GlobalWindowSeconds { get; set; } = 60;

    [Range(1, 32)]
    public int ConcurrentPasswordChecks { get; set; } = 4;

    [Range(1, 3600)]
    public int MaximumCooldownSeconds { get; set; } = 60;

    [Range(1, 168)]
    public int ReputationHours { get; set; } = 24;

    [Range(1, 90)]
    public int DeviceCookieDays { get; set; } = 30;
}
