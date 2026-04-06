using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

public class ImageUploadControllerTests : BaseIntegrationTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ImageUploadControllerTests(CustomWebApplicationFactory factory) : base(factory) { }

    private static byte[] CreateMinimalPng()
    {
        using var image = new Image<Rgba32>(1, 1, new Rgba32(255, 0, 0));
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static MultipartFormDataContent CreateFileContent(byte[] bytes, string fileName, string contentType)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        return content;
    }

    [Fact]
    public async Task Upload_ValidImage_Returns201()
    {
        var pngBytes = CreateMinimalPng();
        using var content = CreateFileContent(pngBytes, "test.png", "image/png");

        var response = await Client.PostAsync("/api/images", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        result.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
        result.GetProperty("url").GetString().Should().Contain("/api/images/");
    }

    [Fact]
    public async Task Upload_InvalidExtension_Returns400()
    {
        var textBytes = "hello world"u8.ToArray();
        using var content = CreateFileContent(textBytes, "test.txt", "text/plain");

        var response = await Client.PostAsync("/api/images", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_TooLarge_Returns400()
    {
        var largeBytes = new byte[6 * 1024 * 1024]; // 6MB exceeds 5MB limit
        using var content = CreateFileContent(largeBytes, "large.png", "image/png");

        var response = await Client.PostAsync("/api/images", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetImage_ExistingImage_Returns200()
    {
        var response = await Client.GetAsync($"/api/images/{TestSeedData.ImageId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetImage_NonExistent_Returns404()
    {
        var randomId = Guid.NewGuid();

        var response = await Client.GetAsync($"/api/images/{randomId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteImage_Existing_Returns204()
    {
        var response = await Client.DeleteAsync($"/api/images/{TestSeedData.ImageId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's gone
        var getResponse = await Client.GetAsync($"/api/images/{TestSeedData.ImageId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteImage_NonExistent_Returns404()
    {
        var randomId = Guid.NewGuid();

        var response = await Client.DeleteAsync($"/api/images/{randomId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
