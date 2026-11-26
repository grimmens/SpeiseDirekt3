using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt3.Components
{
    /// <summary>
    /// Base component that exposes whether the tenant may create one more QR code (the free tier's limit,
    /// <see cref="ITenantPlanService.CanCreateQrCodeAsync"/>, read from the database).
    /// </summary>
    public class PaidTenantComponentBase : ComponentBase
    {
        /// <summary>
        /// Provides the current authentication state.
        /// </summary>
        [Inject]
        protected AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

        /// <summary>The plans (free / paid): the only check of the free limits.</summary>
        [Inject]
        protected ITenantPlanService Plans { get; set; } = default!;

        /// <summary>The tenant owner of the signed-in account (a member's owner, else the account itself).</summary>
        [Inject]
        protected IUserIdProvider UserIdProvider { get; set; } = default!;

        /// <summary>
        /// The currently authenticated user.
        /// </summary>
        protected ClaimsPrincipal? CurrentUser { get; private set; }

        /// <summary>
        /// True if the tenant may create one more QR code (paid, or below the free limit).
        /// False otherwise (including unauthenticated users).
        /// </summary>
        protected bool CanCreateQrCode { get; private set; }

        protected override async Task OnInitializedAsync() => await ReloadAuthState();

        public async Task ReloadAuthState()
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            CurrentUser = authState.User;

            CanCreateQrCode = CurrentUser?.Identity?.IsAuthenticated == true
                && await Plans.CanCreateQrCodeAsync(UserIdProvider.GetUserId());
        }
    }
}
