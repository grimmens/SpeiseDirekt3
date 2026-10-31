namespace SpeiseDirekt3.E2ETests.Infrastructure;

/// <summary>
/// TimeProvider for the app under test (same idea as <c>TestTimeProvider</c> in the API tests).
/// Behaves like <see cref="TimeProvider.System"/> until a fixed local time is set via
/// <see cref="SetLocalNow"/>; <see cref="Reset"/> switches back to the system clock. A fixed time is
/// interpreted with UTC as the local time zone, so results do not depend on the machine's time zone.
/// </summary>
public sealed class E2ETestClock : TimeProvider
{
    private DateTimeOffset? _fixedUtcNow;

    public void SetLocalNow(DateTime localNow) =>
        _fixedUtcNow = new DateTimeOffset(DateTime.SpecifyKind(localNow, DateTimeKind.Unspecified), TimeSpan.Zero);

    public void Reset() => _fixedUtcNow = null;

    public override DateTimeOffset GetUtcNow() => _fixedUtcNow ?? TimeProvider.System.GetUtcNow();

    public override TimeZoneInfo LocalTimeZone => _fixedUtcNow.HasValue ? TimeZoneInfo.Utc : TimeProvider.System.LocalTimeZone;
}
