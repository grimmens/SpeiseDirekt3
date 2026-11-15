namespace SpeiseDirekt3.Components.Guest;

/// <summary>
/// Per request (scoped): the language of the guest document. <c>App.razor</c> renders <c>&lt;html lang&gt;</c> from it and
/// re-renders when a guest page sets it (<see cref="GuestMenuView"/> with the menu's language code). Static SSR writes
/// the HTML only after every component has rendered, so the final <c>lang</c> is in the response — the same mechanism
/// <c>HeadOutlet</c> uses. Notice pages leave the default <c>de</c>.
/// </summary>
public sealed class GuestDocument
{
    public string Language { get; private set; } = "de";

    public event Action? Changed;

    public void SetLanguage(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code == Language)
            return;
        Language = code;
        Changed?.Invoke();
    }
}
