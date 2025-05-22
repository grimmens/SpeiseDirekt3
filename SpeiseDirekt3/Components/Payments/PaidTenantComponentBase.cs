using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace SpeiseDirekt3.Components
{
    /// <summary>
    /// Base component that checks the "PaidTenant" authorization policy
    /// and exposes IsPaidTenant.
    /// </summary>
    public class PaidTenantComponentBase : ComponentBase
    {
        /// <summary>
        /// Provides the current authentication state.
        /// </summary>
        [Inject]
        protected AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

        /// <summary>
        /// Used to check authorization policies.
        /// </summary>
        [Inject]
        protected IAuthorizationService AuthorizationService { get; set; } = default!;

        /// <summary>
        /// The currently authenticated user.
        /// </summary>
        protected ClaimsPrincipal? CurrentUser { get; private set; }

        /// <summary>
        /// True if the current user satisfies the "PaidTenant" policy.
        /// False otherwise (including unauthenticated users).
        /// </summary>
        protected bool CanCreateQrCode { get; private set; }

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            CurrentUser = authState.User;

            if (CurrentUser?.Identity?.IsAuthenticated == true)
            {
                var result = await AuthorizationService.AuthorizeAsync(
                    CurrentUser,
                    policyName: "PaidTenant");

                CanCreateQrCode = result.Succeeded;
            }
        }

        public async Task ReloadAuthState()
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            CurrentUser = authState.User;

            if (CurrentUser?.Identity?.IsAuthenticated == true)
            {
                var result = await AuthorizationService.AuthorizeAsync(
                    CurrentUser,
                    policyName: "PaidTenant");

                CanCreateQrCode = result.Succeeded;
            }
        }
    }
}
