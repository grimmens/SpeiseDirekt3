using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SpeiseDirekt.Data;

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
    /// Starts the server (the app creates the schema itself via <c>EnsureCreated()</c> in the
    /// <see cref="SpeiseDirekt3.Program.E2EEnvironment"/> environment) and seeds the test data.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Accessing Services builds and starts the hosts (see CreateHost)
        _ = Services;

        await using var db = CreateSeedContext();
        await E2ESeedData.SeedAsync(db);
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

        // Applies to both hosts (TestServer and Kestrel), they are built from this builder
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
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
