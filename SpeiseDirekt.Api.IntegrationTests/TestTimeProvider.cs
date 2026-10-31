namespace SpeiseDirekt.Api.IntegrationTests;

/// <summary>
/// TimeProvider for tests. Behaves like <see cref="TimeProvider.System"/> until a fixed local time
/// is set via <see cref="SetLocalNow"/>; <see cref="Reset"/> switches back to the system clock.
/// A fixed time is interpreted in UTC as the local time zone, so results do not depend on the
/// time zone of the machine running the tests.
/// </summary>
public class TestTimeProvider : TimeProvider
{
    private DateTimeOffset? _fixedUtcNow;

    public void SetLocalNow(DateTime localNow) =>
        _fixedUtcNow = new DateTimeOffset(DateTime.SpecifyKind(localNow, DateTimeKind.Unspecified), TimeSpan.Zero);

    public void Reset() => _fixedUtcNow = null;

    public override DateTimeOffset GetUtcNow() => _fixedUtcNow ?? TimeProvider.System.GetUtcNow();

    public override TimeZoneInfo LocalTimeZone => _fixedUtcNow.HasValue ? TimeZoneInfo.Utc : TimeProvider.System.LocalTimeZone;
}
