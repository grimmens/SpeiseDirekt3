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
        // Factory (and its clock) is shared by all tests of a class: start every test on the system clock
        Factory.Clock.Reset();

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

    /// <summary>GET request without an authenticated user (see <see cref="TestAuthHandler.AnonymousHeader"/>).</summary>
    protected static HttpRequestMessage AnonymousGet(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(TestAuthHandler.AnonymousHeader, "1");
        return request;
    }

    protected T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)!;
}
