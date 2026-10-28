using MovieDB.Common;
using Shouldly;

namespace MovieDB.UnitTests.Common;

public sealed class UserClockTests
{
    private const string Brussels = "Europe/Brussels";

    [Theory]
    [InlineData("2025-12-31T22:59:59Z", "2025-12-31")]
    [InlineData("2025-12-31T23:00:00Z", "2026-01-01")]
    [InlineData("2025-06-30T21:59:59Z", "2025-06-30")]
    [InlineData("2025-06-30T22:00:00Z", "2025-07-01")]
    public void Today_follows_the_configured_time_zone_across_midnight(string utc, string expected)
    {
        var clock = new UserClock(TimeProvider.System, UserClock.ResolveZone(Brussels));

        clock
            .TodayAt(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void CurrentYear_uses_the_time_provider()
    {
        var time = new FixedTimeProvider(
            DateTimeOffset.Parse("2025-12-31T23:30:00Z", System.Globalization.CultureInfo.InvariantCulture)
        );
        var clock = new UserClock(time, UserClock.ResolveZone(Brussels));

        clock.CurrentYear.ShouldBe(2026);
        clock.Today.ShouldBe(new DateOnly(2026, 1, 1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Not/AZone")]
    public void Invalid_zone_ids_are_rejected(string? id)
    {
        UserClock.TryResolveZone(id, out var zone).ShouldBeFalse();
        zone.ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => UserClock.ResolveZone(id));
    }

    [Fact]
    public void Valid_zone_id_resolves()
    {
        UserClock.TryResolveZone(Brussels, out var zone).ShouldBeTrue();
        zone.ShouldNotBeNull();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
