using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SpeiseDirekt.Data;

namespace SpeiseDirekt.Api.IntegrationTests;

public abstract class BaseIntegrationTest : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    protected readonly CustomWebApplicationFactory Factory;
    protected readonly HttpClient Client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    protected BaseIntegrationTest(CustomWebApplicationFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await TestSeedData.SeedAsync(db, CustomWebApplicationFactory.TestUserId);
    }

    public async Task DisposeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureDeletedAsync();
    }

    protected T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)!;
}
