using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt3.E2ETests.Infrastructure;

/// <summary>User id provider for seeding outside a request (saving tenant entities requires a user).</summary>
internal sealed class FixedUserIdProvider : IUserIdProvider
{
    private readonly string _userId;
    public FixedUserIdProvider(Guid userId) => _userId = userId.ToString();
    public string GetUserId() => _userId;
    public string GetActualUserId() => _userId;
}
