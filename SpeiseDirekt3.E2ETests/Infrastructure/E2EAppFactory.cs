using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpeiseDirekt.Data;

namespace SpeiseDirekt3.E2ETests.Infrastructure;

/// <summary>
/// Starts the Blazor app on a real Kestrel port (Playwright needs a real socket) with a
/// SQLite <b>file</b> instead of SQL Server. Every factory instance gets its own file, which is
/// deleted again in <see cref="DisposeAsync"/>.
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

    /// <summary>Base address of the Kestrel server, e.g. <c>http://127.0.0.1:41234</c>.</summary>
    public string ServerAddress { get; private set; } = string.Empty;

    /// <summary>Creates the schema, seeds the data and starts the server.</summary>
    public async Task InitializeAsync()
    {
        await using (var db = CreateSeedContext())
        {
            await db.Database.EnsureCreatedAsync();
            await E2ESeedData.SeedAsync(db);
        }

        // Accessing Services builds and starts the hosts (see CreateHost)
        _ = Services;
    }

    /// <summary>A DbContext outside the app (no HttpContext) that writes as the seed tenant owner.</summary>
    private ApplicationDbContext CreateSeedContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(ConnectionString)
            .Options;
        return new ApplicationDbContext(options, new FixedUserIdProvider(E2ESeedData.OwnerId));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        // Program.cs requires a "server" connection string; the SQL Server registration is replaced below
        builder.UseSetting("ConnectionStrings:server", "unused");

        builder.ConfigureServices(services =>
        {
            var efDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                         || d.ServiceType == typeof(DbContextOptions)
                         || d.ServiceType == typeof(IDbContextFactory<ApplicationDbContext>)
                         || d.ServiceType == typeof(ApplicationDbContext)
                         || d.ServiceType.FullName?.Contains("EntityFrameworkCore") == true)
                .ToList();
            foreach (var descriptor in efDescriptors)
                services.Remove(descriptor);

            // Same lifetimes as Program.cs
            services.AddDbContext<ApplicationDbContext>(
                o => o.UseSqlite(ConnectionString),
                contextLifetime: ServiceLifetime.Transient);
            services.AddDbContextFactory<ApplicationDbContext>(
                o => o.UseSqlite(ConnectionString),
                lifetime: ServiceLifetime.Scoped);
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
