namespace SpeiseDirekt3.Components.MenuItem;

/// <summary>
/// Configuration section <c>MenuItemGenerator</c>. <see cref="Enabled"/> shows the "Generate Menu Items"
/// button on the items page; it needs an Ollama server, so it is on in Development only
/// (<c>appsettings.Development.json</c>) and off everywhere else, including the E2E environment.
/// </summary>
public sealed class MenuItemGeneratorOptions
{
    public const string SectionName = "MenuItemGenerator";

    public bool Enabled { get; set; }
}
