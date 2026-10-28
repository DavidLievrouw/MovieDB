using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MovieDB.Infrastructure.Auth;

namespace MovieDB.Auth;

internal sealed class AuthProtectionCleanup : BackgroundService
{
    private readonly AuthThrottleStore _store;
    private readonly TimeProvider _clock;
    private readonly ILogger<AuthProtectionCleanup> _logger;

    public AuthProtectionCleanup(AuthThrottleStore store, TimeProvider clock, ILogger<AuthProtectionCleanup> logger)
    {
        _store = store;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var result = await _store.CleanupAsync(stoppingToken);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning("Authentication protection cleanup failed: {Reason}", result.Error.Message);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown cancels the timer or the active cleanup.
        }
    }
}
