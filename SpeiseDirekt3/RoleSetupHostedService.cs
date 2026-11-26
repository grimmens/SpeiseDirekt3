using Microsoft.Extensions.Options;
using SpeiseDirekt.Infrastructure;

namespace SpeiseDirekt3
{
    /// <summary>
    /// Runs <see cref="RoleSetup"/> once at startup (awaited before requests are served; in the E2E environment after
    /// <see cref="E2EDatabaseInitializer"/>, which is registered first): both Identity roles exist and the global admins
    /// match <c>Admin:GlobalAdminEmails</c>. A failure is logged and does not stop the app.
    /// </summary>
    public sealed class RoleSetupHostedService(
        IServiceProvider services,
        IOptions<AdminOptions> options,
        ILogger<RoleSetupHostedService> logger) : IHostedService
    {
        // Test hosts (WebApplicationFactory builds the app twice) may start in parallel on one database, like
        // E2EDatabaseInitializer
        private static readonly SemaphoreSlim Lock = new(1, 1);

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await Lock.WaitAsync(cancellationToken);
            try
            {
                using var scope = services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<RoleSetup>()
                    .RunAsync(options.Value.GlobalAdminEmails, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Role setup failed, the app starts without the GlobalAdmin sync");
            }
            finally
            {
                Lock.Release();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
