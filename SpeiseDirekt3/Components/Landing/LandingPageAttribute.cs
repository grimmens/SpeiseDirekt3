namespace SpeiseDirekt3.Components.Landing;

/// <summary>
/// Marks the landing page (<c>/</c>). <c>App.razor</c> then renders the landing document
/// (<see cref="OwnDocumentAttribute"/>): <c>&lt;html lang="de" data-landing-page&gt;</c>, <c>css/fonts.css</c>,
/// <c>css/brand.css</c>, <c>css/landing.css</c> and the ES module <c>js/landing.js</c>. Always combine it with
/// <c>[ExcludeFromInteractiveRouting]</c>.
/// </summary>
public sealed class LandingPageAttribute : OwnDocumentAttribute
{
    public override PageDocument Document => PageDocument.Landing;
}
