using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;
using SpeiseDirekt.Data;

namespace SpeiseDirekt3.Components.Account
{
    public sealed class IdentityUserAccessor(UserManager<ApplicationUser> userManager, IdentityRedirectManager redirectManager,
        IStringLocalizer<AccountText> T)
    {
        public async Task<ApplicationUser> GetRequiredUserAsync(HttpContext context)
        {
            var user = await userManager.GetUserAsync(context.User);

            if (user is null)
            {
                redirectManager.RedirectToWithStatus("Account/InvalidUser", T["Status_UnableToLoadUser", userManager.GetUserId(context.User) ?? ""], context, isError: true);
            }

            return user;
        }
    }
}
