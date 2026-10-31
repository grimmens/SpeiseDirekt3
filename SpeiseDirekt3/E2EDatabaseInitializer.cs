using SpeiseDirekt.Data;

namespace SpeiseDirekt3
{
    /// <summary>
    /// Only registered in the <see cref="Program.E2EEnvironment"/> environment: creates the SQLite schema
    /// with <c>EnsureCreated()</c> at startup (no migrations; they are SQL Server specific).
    /// </summary>
    public class E2EDatabaseInitializer : IHostedService
    {
        // EnsureCreated is not safe against concurrent calls on the same database. Test hosts
        // (WebApplicationFactory builds the app twice) may start in parallel in one process.
        private static readonly SemaphoreSlim Lock = new(1, 1);

        private readonly IServiceProvider _services;

        public E2EDatabaseInitializer(IServiceProvider services)
        {
            _services = services;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await Lock.WaitAsync(cancellationToken);
            try
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.EnsureCreatedAsync(cancellationToken);
            }
            finally
            {
                Lock.Release();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
