// PaidTenantHandler.cs
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Infrastructure;


public class PaidTenantHandler : AuthorizationHandler<PaidTenantRequirement>
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PaidTenantHandler(ApplicationDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PaidTenantRequirement requirement)
    {
        // 1) Get the tenant owner’s ID (sub-accounts use TenantOwnerId claim)
        var tenantId = context.User.FindFirstValue("TenantOwnerId")
                    ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(tenantId))
            return;

        // 2) Load their subscription
        var sub = await _db.TenantSubscriptions
                           .AsNoTracking()
                           .FirstOrDefaultAsync(x => x.TenantId == tenantId);

        var isPaid = sub?.IsPaid == true
                     && (sub.SubscriptionEnd == null || sub.SubscriptionEnd > DateTime.UtcNow);

        if (isPaid)
        {
            // paid tenants bypass the limit
            context.Succeed(requirement);
            return;
        }

        // 3) free tenants: count existing codes
        var count = await _db.QRCodes
                             .AsNoTracking()
                             .CountAsync(q => q.ApplicationUserId.ToString() == tenantId);

        if (count < 100)
        {
            // first QR code always allowed
            context.Succeed(requirement);
        }
        // else: do nothing → authorization fails
    }
}

public class PaidTenantRequirement : IAuthorizationRequirement
{
    // marker class — no properties needed
}

