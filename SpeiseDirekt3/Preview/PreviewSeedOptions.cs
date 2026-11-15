namespace SpeiseDirekt3.Preview;

/// <summary>
/// Demo data for the PR preview (<c>ASPNETCORE_ENVIRONMENT=Preview</c>, section <c>PreviewSeed</c> in
/// <c>appsettings.Preview.json</c>): on every start of the preview app <see cref="PreviewSeeder"/> adds a few
/// menus and QR codes to the tenant of <see cref="Email"/> unless they exist already. The hosted service is only
/// registered in that environment, so production never sees this.
/// </summary>
public sealed class PreviewSeedOptions
{
    public const string SectionName = "PreviewSeed";

    public bool Enabled { get; set; }

    /// <summary>E-mail of the user whose tenant receives the demo data; a sub-account seeds its owner's tenant.</summary>
    public string? Email { get; set; }
}
