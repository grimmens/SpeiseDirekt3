using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Localization;
using SpeiseDirekt.Data;

namespace SpeiseDirekt3.Components.Account
{
    /// <summary>
    /// The account e-mails (confirmation, password reset). Nothing is sent yet (<see cref="NoOpEmailSender"/>); the texts are
    /// already in the request's language (<c>AccountText</c>, <c>Email_*</c>), so a real sender only has to replace the
    /// transport. Remove the "else if (EmailSender is IdentityNoOpEmailSender)" block from RegisterConfirmation.razor after
    /// updating with a real implementation.
    /// </summary>
    internal sealed class IdentityNoOpEmailSender(IStringLocalizer<AccountText> T) : IEmailSender<ApplicationUser>
    {
        private readonly IEmailSender emailSender = new NoOpEmailSender();

        // The links arrive HTML-encoded from the pages (HtmlEncoder.Default.Encode(callbackUrl))
        public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
            emailSender.SendEmailAsync(email, T["Email_ConfirmSubject"],
                $"{WebUtility.HtmlEncode(T["Email_ConfirmText"])} <a href='{confirmationLink}'>{WebUtility.HtmlEncode(T["Email_ConfirmLink"])}</a>");

        public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
            emailSender.SendEmailAsync(email, T["Email_ResetSubject"],
                $"{WebUtility.HtmlEncode(T["Email_ResetText"])} <a href='{resetLink}'>{WebUtility.HtmlEncode(T["Email_ResetLink"])}</a>");

        public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
            emailSender.SendEmailAsync(email, T["Email_ResetSubject"], WebUtility.HtmlEncode(T["Email_ResetCodeText", resetCode]));
    }
}
