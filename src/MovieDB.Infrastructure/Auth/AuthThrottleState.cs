namespace MovieDB.Infrastructure.Auth;

internal sealed class AuthThrottleState
{
    public string Key { get; set; } = "";
    public DateTime WindowStart { get; set; } = DateTime.UnixEpoch;
    public int Attempts { get; set; }
    public int Failures { get; set; }
    public DateTime BlockedUntil { get; set; } = DateTime.UnixEpoch;
    public DateTime ExpiresAt { get; set; } = DateTime.UnixEpoch;
    public DateTime LastFailure { get; set; } = DateTime.UnixEpoch;
    public string? LastAccount { get; set; }
    public int AccountChanges { get; set; }
    public DateTime? LastSuccess { get; set; }
}
