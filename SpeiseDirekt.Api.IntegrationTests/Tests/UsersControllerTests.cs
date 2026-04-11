using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

public class UsersControllerTests : BaseIntegrationTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public UsersControllerTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task GetAll_ReturnsEmptyList_WhenNoUsers()
    {
        var response = await Client.GetAsync("/api/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var users = Deserialize<List<TenantUser>>(body);
        users.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WithWeakPassword()
    {
        var dto = new { Email = "weak@test.com", DisplayName = "Test", Role = 3, Password = "123" };
        var content = new StringContent(JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/users", content);

        // Identity UserManager rejects weak passwords
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_ThenGetAll_ReturnsCreatedUser()
    {
        var dto = new { Email = "employee@test.com", DisplayName = "Test Employee", Role = 3, Password = "TestPass123!" };
        var content = new StringContent(JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var createResponse = await Client.PostAsync("/api/users", content);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await Client.GetAsync("/api/users");
        var body = await response.Content.ReadAsStringAsync();
        var users = Deserialize<List<TenantUser>>(body);
        users.Should().HaveCount(1);
        users[0].DisplayName.Should().Be("Test Employee");
    }

    [Fact]
    public async Task Update_ExistingUser_ChangesRole()
    {
        // Create a user first
        var createDto = new { Email = "manager@test.com", DisplayName = "Manager", Role = 1, Password = "TestPass123!" };
        var createContent = new StringContent(JsonSerializer.Serialize(createDto, JsonOptions), Encoding.UTF8, "application/json");
        var createResponse = await Client.PostAsync("/api/users", createContent);
        var createBody = await createResponse.Content.ReadAsStringAsync();
        var created = Deserialize<TenantUser>(createBody);

        // Update role to Cashier
        var updateDto = new { Role = 2, IsActive = true };
        var updateContent = new StringContent(JsonSerializer.Serialize(updateDto, JsonOptions), Encoding.UTF8, "application/json");
        var updateResponse = await Client.PutAsync($"/api/users/{created.Id}", updateContent);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_ExistingUser_DeactivatesUser()
    {
        // Create user
        var createDto = new { Email = "todelete@test.com", DisplayName = "Delete Me", Role = 3, Password = "TestPass123!" };
        var createContent = new StringContent(JsonSerializer.Serialize(createDto, JsonOptions), Encoding.UTF8, "application/json");
        var createResponse = await Client.PostAsync("/api/users", createContent);
        var createBody = await createResponse.Content.ReadAsStringAsync();
        var created = Deserialize<TenantUser>(createBody);

        // Delete (deactivate)
        var deleteResponse = await Client.DeleteAsync($"/api/users/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify still exists but inactive
        var getResponse = await Client.GetAsync($"/api/users/{created.Id}");
        var getBody = await getResponse.Content.ReadAsStringAsync();
        var user = Deserialize<TenantUser>(getBody);
        user.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Get_NonExistentUser_Returns404()
    {
        var response = await Client.GetAsync($"/api/users/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
