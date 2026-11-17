namespace SpeiseDirekt3.Components.Layout;

/// <summary>
/// The signed-in user as the back-office chrome shows it (user menu, drawer): a sub-account's
/// <c>TenantUser.DisplayName</c> or the e-mail, plus the role ("Owner" for the tenant owner).
/// </summary>
public sealed record ShellUser(string Email, string? DisplayName, string? Role)
{
    /// <summary>What the chrome shows as the name: the display name, else the e-mail.</summary>
    public string Name => DisplayName ?? Email;

    /// <summary>The full name and e-mail on hover (the chrome truncates long names).</summary>
    public string Title => DisplayName is null ? Email : $"{DisplayName} ({Email})";

    /// <summary>One or two letters for the avatar: "Max Kellner" → "MK", "anna.berger@…" → "AB", "owner-2c75@…" → "O".</summary>
    public string Initials
    {
        get
        {
            var source = DisplayName ?? Email.Split('@')[0];
            var letters = source
                .Split([' ', '.', '-', '_', '+'], StringSplitOptions.RemoveEmptyEntries)
                .Where(part => char.IsLetter(part[0]))
                .Take(2)
                .Select(part => char.ToUpperInvariant(part[0]));
            var initials = string.Concat(letters);
            return initials.Length > 0 ? initials : "?";
        }
    }
}
