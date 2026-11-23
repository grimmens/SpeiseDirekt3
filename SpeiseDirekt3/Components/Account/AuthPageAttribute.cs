namespace SpeiseDirekt3.Components.Account;

/// <summary>
/// Marks an anonymous account page (<c>/Account/Login</c>, <c>/Account/Register</c>, <c>ForgotPassword</c>,
/// <c>ResetPassword</c>, <c>Lockout</c>, <c>AccessDenied</c>, …). <c>App.razor</c> then renders the account document
/// (<see cref="OwnDocumentAttribute"/>, AP13): <c>&lt;html lang data-auth-page&gt;</c>, <c>css/fonts.css</c>,
/// <c>css/brand.css</c>, <c>css/auth.css</c> — the landing page's look, no Tailwind CDN, Bootstrap or
/// <c>blazor.web.js</c>. Combine it with <c>@layout AuthLayout</c>. The pages are static SSR anyway (every
/// <c>/Account</c> page carries <c>[ExcludeFromInteractiveRouting]</c>); <c>Account/Manage</c> keeps the back office.
/// </summary>
public sealed class AuthPageAttribute : OwnDocumentAttribute
{
    public override PageDocument Document => PageDocument.Auth;
}
