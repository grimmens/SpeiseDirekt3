using Microsoft.AspNetCore.Authorization;

namespace SpeiseDirekt.Infrastructure;

public static class RolePolicyExtensions
{
    /// <summary>
    /// The policies of the Identity roles (<see cref="AppRoles"/>): <see cref="PolicyNames.PaidTenant"/> (the tenant is
    /// paid, <see cref="PaidTenantHandler"/>) and <see cref="PolicyNames.GlobalAdmin"/> (the account is a global admin,
    /// <see cref="GlobalAdminHandler"/>). Both handlers read the database; the host registers them.
    /// </summary>
    public static void AddRolePolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(PolicyNames.PaidTenant, policy =>
            policy.Requirements.Add(new PaidTenantRequirement()));
        options.AddPolicy(PolicyNames.GlobalAdmin, policy =>
            policy.Requirements.Add(new GlobalAdminRequirement()));
    }
}
