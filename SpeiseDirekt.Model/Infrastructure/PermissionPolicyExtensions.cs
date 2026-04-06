using Microsoft.AspNetCore.Authorization;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Infrastructure;

public static class PermissionPolicyExtensions
{
    public static void AddPermissionPolicies(this AuthorizationOptions options)
    {
        // Generate a policy for each Permission enum value
        foreach (Permission perm in Enum.GetValues<Permission>())
        {
            if (perm == Permission.None) continue;

            var policyName = GetPolicyName(perm);
            options.AddPolicy(policyName, policy =>
                policy.Requirements.Add(new PermissionRequirement(perm)));
        }
    }

    /// <summary>
    /// Converts a Permission enum value to a policy name.
    /// e.g. MenusView -> "CanViewMenus", MenusCreate -> "CanCreateMenus"
    /// </summary>
    public static string GetPolicyName(Permission permission)
    {
        var name = permission.ToString();

        // Pattern: {Area}{Action} -> Can{Action}{Area}
        // e.g. MenusView -> CanViewMenus, CategoriesCreate -> CanCreateCategories
        var actions = new[] { "View", "Create", "Edit", "Delete", "Manage" };

        foreach (var action in actions)
        {
            if (name.EndsWith(action))
            {
                var area = name[..^action.Length];
                return $"Can{action}{area}";
            }
        }

        return $"Can{name}";
    }
}
