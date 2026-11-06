using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpeiseDirekt.Data;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt3.Preview;

/// <summary>
/// Runs <see cref="PreviewSeeder"/> once when the preview app starts. In the CI preview job that is right after
/// the fresh copy of the production database was made and the PR's migrations were applied to it, so the demo
/// data always sits on the current schema. A failure is logged and does not stop the app.
/// </summary>
public sealed class PreviewSeedHostedService(
    IServiceProvider services,
    IHostEnvironment environment,
    IOptions<PreviewSeedOptions> options,
    TimeProvider clock,
    ILogger<PreviewSeedHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // The service is only registered in the preview environment; this guards against a stray registration.
        if (!environment.IsEnvironment(Program.PreviewEnvironment))
            return;

        var settings = options.Value;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Email))
        {
            logger.LogInformation("Preview seed: disabled (PreviewSeed:Enabled or PreviewSeed:Email not set)");
            return;
        }

        try
        {
            using var scope = services.CreateScope();
            var dbOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
            var result = await PreviewSeeder.SeedAsync(
                id => new ApplicationDbContext(dbOptions, new StaticUserIdProvider(id)),
                settings.Email, clock, cancellationToken);

            if (!result.UserFound)
                logger.LogWarning("Preview seed: user {Email} not found, nothing seeded", settings.Email);
            else
                logger.LogInformation(
                    "Preview seed: tenant {OwnerId} ({Email}): {MenusAdded} menus and {QrCodesAdded} QR codes added, the tenant now has {MenusTotal} menus and {QrCodesTotal} QR codes",
                    result.OwnerId, settings.Email, result.MenusAdded, result.QrCodesAdded, result.MenusTotal, result.QrCodesTotal);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Preview seed failed, the app starts without the demo data");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Acts as a fixed tenant owner outside a request (no HttpContext).</summary>
    private sealed class StaticUserIdProvider(Guid userId) : IUserIdProvider
    {
        private readonly string _userId = userId.ToString();
        public string GetUserId() => _userId;
        public string GetActualUserId() => _userId;
    }
}
