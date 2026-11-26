using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SpeiseDirekt.Data;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt3.E2ETests.Infrastructure;

/// <summary>
/// Starts the Blazor app in the <c>E2E</c> environment (SQLite <b>file</b> instead of SQL Server)
/// on a real Kestrel port (Playwright needs a real socket). Every factory instance gets its own file,
/// which is deleted again in <see cref="DisposeAsync"/>.
/// </summary>
public sealed class E2EAppFactory : WebApplicationFactory<SpeiseDirekt3.Program>, IAsyncLifetime
{
    private IHost? _kestrelHost;

    /// <summary>Path of this instance's SQLite database file (in the temp directory).</summary>
    public string DatabasePath { get; } =
        Path.Combine(Path.GetTempPath(), $"speisedirekt-e2e-{Guid.NewGuid():N}.db");

    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath,
        // No connection pooling: otherwise pooled handles keep the file open and it cannot be deleted
        Pooling = false
    }.ToString();

    /// <summary>
    /// Clock used by the app (both hosts). System clock unless a test fixes the time;
    /// <see cref="E2ETestBase"/> resets it before every test.
    /// </summary>
    public E2ETestClock Clock { get; } = new();

    /// <summary>Base address of the Kestrel server, e.g. <c>http://127.0.0.1:41234</c>.</summary>
    public string ServerAddress { get; private set; } = string.Empty;

    /// <summary>
    /// The services of the Kestrel host — the one the browser talks to (<see cref="WebApplicationFactory{TEntryPoint}.Services"/>
    /// is the TestServer's). E.g. its <c>IConfiguration</c>, to switch a setting that is read per request.
    /// </summary>
    public IServiceProvider ServerServices => _kestrelHost?.Services ?? Services;

    /// <summary>
    /// Starts the server (the app creates the schema itself via <c>EnsureCreated()</c> in the
    /// <see cref="SpeiseDirekt3.Program.E2EEnvironment"/> environment) and seeds the test data.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Accessing Services builds and starts the hosts (see CreateHost)
        _ = Services;

        await using (var db = CreateSeedContext())
            await E2ESeedData.SeedAsync(db);

        // The app's startup sync (RoleSetupHostedService) ran when the hosts started, before the seed, so it found no
        // account for admin@e2e.test: run the same sync again now that the account exists
        using (var scope = Services.CreateScope())
        {
            var admins = scope.ServiceProvider.GetRequiredService<IOptions<AdminOptions>>().Value.GlobalAdminEmails;
            await scope.ServiceProvider.GetRequiredService<RoleSetup>().RunAsync(admins);
        }

        // The seed owner has more than the free plan's one menu: a paying customer
        await SetPaidAsync(E2ESeedData.OwnerId, true);
    }

    /// <summary>
    /// Gives the owner account <paramref name="ownerId"/> the role <c>PaidTenant</c> (unlimited menus and QR codes) or takes
    /// it away (the free plan: one menu, one QR code). Idempotent. The app reads it from the database at every check, so
    /// it counts at the next page load of an already signed-in owner or member.
    /// </summary>
    public Task SetPaidAsync(Guid ownerId, bool paid) => SetRoleAsync(ownerId.ToString(), AppRoles.PaidTenant, paid);

    /// <summary>
    /// Adds the Identity role <paramref name="role"/> to the account <paramref name="userId"/> or removes it, straight
    /// through Identity (no admin check, no startup sync; e.g. a second global admin for one test). Idempotent.
    /// </summary>
    public async Task SetRoleAsync(string userId, string role, bool member)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"No account {userId}.");
        if (await users.IsInRoleAsync(user, role) == member)
            return;

        var result = member ? await users.AddToRoleAsync(user, role) : await users.RemoveFromRoleAsync(user, role);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Role {role} of {userId} not changed: {string.Join(", ", result.Errors.Select(e => e.Code))}");
    }

    /// <summary>Whether the owner <paramref name="ownerId"/> is paid, as the app sees it (<see cref="ITenantPlanService"/>).</summary>
    public async Task<bool> IsPaidAsync(Guid ownerId)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITenantPlanService>().IsPaidAsync(ownerId.ToString());
    }

    /// <summary>
    /// A DbContext on the test database outside the app (no HttpContext). Writes are assigned to
    /// <paramref name="ownerId"/> (tenant owner); reads are filtered to that tenant like in the app.
    /// </summary>
    public ApplicationDbContext CreateDbContext(Guid ownerId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(ConnectionString)
            .Options;
        return new ApplicationDbContext(options, new FixedUserIdProvider(ownerId));
    }

    /// <summary>Seeds data directly into the database as tenant <paramref name="ownerId"/>.</summary>
    public async Task SeedAsync(Guid ownerId, Func<ApplicationDbContext, Task> seed)
    {
        await using var db = CreateDbContext(ownerId);
        await seed(db);
        await db.SaveChangesAsync();
    }

    private ApplicationDbContext CreateSeedContext() => CreateDbContext(E2ESeedData.OwnerId);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The app switches to SQLite + EnsureCreated in this environment (see Program.cs)
        builder.UseEnvironment(SpeiseDirekt3.Program.E2EEnvironment);
        builder.UseSetting("ConnectionStrings:e2e", ConnectionString);

        // The only global admin is the seeded admin@e2e.test, never the operator's address of appsettings.json. The
        // setting replaces the array's first entry (configuration arrays merge by index); PostConfigure makes the bound
        // list exactly this one address, whatever else appsettings.json lists
        builder.UseSetting($"{AdminOptions.SectionName}:{nameof(AdminOptions.GlobalAdminEmails)}:0", E2ESeedData.AdminEmail);

        // Applies to both hosts (TestServer and Kestrel), they are built from this builder
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.PostConfigure<AdminOptions>(o => o.GlobalAdminEmails = [E2ESeedData.AdminEmail]);
        });
    }

    /// <summary>
    /// .NET 9's WebApplicationFactory only offers an in-memory TestServer. We build the host a second
    /// time with Kestrel on a free loopback port for the browser and return the TestServer host to the base class.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var testHost = builder.Build();

        builder.ConfigureWebHost(b => b.UseKestrel(o => o.Listen(IPAddress.Loopback, 0)));
        _kestrelHost = builder.Build();
        _kestrelHost.Start();

        var server = _kestrelHost.Services.GetRequiredService<IServer>();
        ServerAddress = server.Features.Get<IServerAddressesFeature>()!.Addresses.First().TrimEnd('/');

        testHost.Start();
        return testHost;
    }

    public new async Task DisposeAsync()
    {
        if (_kestrelHost is not null)
        {
            await _kestrelHost.StopAsync();
            _kestrelHost.Dispose();
        }

        await base.DisposeAsync();

        DeleteDatabaseFiles();
    }

    private void DeleteDatabaseFiles()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { DatabasePath, DatabasePath + "-journal", DatabasePath + "-wal", DatabasePath + "-shm" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
