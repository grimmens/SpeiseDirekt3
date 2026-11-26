namespace SpeiseDirekt.Infrastructure;

/// <summary>
/// Section <c>Admin</c> of the web app's configuration (bound there only; the API has no admin).
/// </summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// The e-mail addresses of the global admins (<see cref="AppRoles.GlobalAdmin"/>). A full sync at every start
    /// (<see cref="RoleSetup"/>): listed accounts get the role, every other account loses it, an address without an
    /// account is skipped. Empty = nobody is global admin.
    /// </summary>
    public string[] GlobalAdminEmails { get; set; } = [];
}
