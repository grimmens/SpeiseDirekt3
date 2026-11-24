using System.Net;
using FluentAssertions;
using SpeiseDirekt.ServiceImplementation;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

/// <summary>
/// <see cref="VisitorKeyService"/> (pure, no database; AP15a): the guests' daily visitor key — HMAC-SHA256 over tenant,
/// client address and user agent with an in-memory salt that is replaced at local midnight and on every restart.
/// End to end (views and clicks of the API) in <see cref="MenuDisplayControllerTests"/>.
/// </summary>
public class VisitorKeyServiceTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly IPAddress Address = IPAddress.Parse("203.0.113.7");
    private const string Agent = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)";

    private static (VisitorKeyService Keys, TestTimeProvider Clock) Create(DateTime localNow)
    {
        var clock = new TestTimeProvider();
        clock.SetLocalNow(localNow);
        return (new VisitorKeyService(clock), clock);
    }

    [Fact]
    public void TheSameVisitor_GetsTheSameKey_SixteenLowerCaseHexCharacters()
    {
        var (keys, _) = Create(new DateTime(2026, 11, 23, 12, 0, 0));

        var key = keys.KeyFor(TenantA, Address, Agent);

        key.Should().MatchRegex("^[0-9a-f]{16}$");
        keys.KeyFor(TenantA, IPAddress.Parse("203.0.113.7"), Agent).Should().Be(key);
        // An IPv4 client seen through a dual-stack socket is the same client
        keys.KeyFor(TenantA, Address.MapToIPv6(), Agent).Should().Be(key);
    }

    [Fact]
    public void AnotherTenant_Address_OrUserAgent_GivesAnotherKey()
    {
        var (keys, _) = Create(new DateTime(2026, 11, 23, 12, 0, 0));
        var key = keys.KeyFor(TenantA, Address, Agent);

        keys.KeyFor(TenantB, Address, Agent).Should().NotBe(key, "a guest is never linked across two restaurants");
        keys.KeyFor(TenantA, IPAddress.Parse("203.0.113.8"), Agent).Should().NotBe(key);
        keys.KeyFor(TenantA, Address, Agent + " Safari").Should().NotBe(key);
        keys.KeyFor(TenantA, null, null).Should().MatchRegex("^[0-9a-f]{16}$", "a request without address or agent still gets a key");
    }

    [Fact]
    public void TheKey_ChangesAtLocalMidnight_AndAfterARestart()
    {
        var (keys, clock) = Create(new DateTime(2026, 11, 23, 0, 0, 0));
        var morning = keys.KeyFor(TenantA, Address, Agent);

        clock.SetLocalNow(new DateTime(2026, 11, 23, 23, 59, 59));
        keys.KeyFor(TenantA, Address, Agent).Should().Be(morning, "the whole day has one salt");

        clock.SetLocalNow(new DateTime(2026, 11, 24, 0, 0, 0));
        var nextDay = keys.KeyFor(TenantA, Address, Agent);
        nextDay.Should().NotBe(morning, "a new salt at midnight");

        // Back on the 23rd (a clock correction): that day's salt is gone, it is not derived from the date
        clock.SetLocalNow(new DateTime(2026, 11, 23, 12, 0, 0));
        keys.KeyFor(TenantA, Address, Agent).Should().NotBe(morning).And.NotBe(nextDay);

        // A restart: a new instance has a new salt, the same day
        var (restarted, _) = Create(new DateTime(2026, 11, 23, 12, 0, 0));
        restarted.KeyFor(TenantA, Address, Agent).Should().NotBe(morning);
    }
}
