using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Numerics;
using System.Text;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
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

    /// <summary>
    /// A phone photo as JPEG: a noisy picture (so it is not trivially small) with an EXIF profile holding the GPS
    /// position, the time and the camera, an IPTC city, an XMP packet with a location and an ICC profile.
    /// </summary>
    private static byte[] CreatePhotoWithMetadata(int width, int height, ushort? orientation = null)
    {
        using var image = new Image<Rgb24>(width, height);
        var random = new Random(42);
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = new Rgb24((byte)(x * 255 / width), (byte)random.Next(256), (byte)(y * 255 / height));
            }
        });

        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(48, 1), new Rational(0, 1), new Rational(3600, 100)]);
        exif.SetValue(ExifTag.GPSLongitudeRef, "E");
        exif.SetValue(ExifTag.GPSLongitude, [new Rational(16, 1), new Rational(14, 1), new Rational(2400, 100)]);
        exif.SetValue(ExifTag.DateTimeOriginal, "2026:11:20 19:30:00");
        exif.SetValue(ExifTag.Make, "TestPhone");
        exif.SetValue(ExifTag.Model, "TestPhone 15 Pro");
        if (orientation is { } mode)
            exif.SetValue(ExifTag.Orientation, mode);
        image.Metadata.ExifProfile = exif;

        var iptc = new IptcProfile();
        iptc.SetValue(IptcTag.City, "Baden");
        image.Metadata.IptcProfile = iptc;

        image.Metadata.XmpProfile = new XmpProfile(Encoding.UTF8.GetBytes(
            "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>" +
            "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
            "<rdf:Description xmlns:photoshop=\"http://ns.adobe.com/photoshop/1.0/\" photoshop:City=\"Baden\"/>" +
            "</rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>"));

        image.Metadata.IccProfile = new IccProfile(MinimalIccProfile());

        // The input really carries everything the test then expects to be gone
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms, new JpegEncoder { Quality = 90 });
        ms.Position = 0;
        using var check = Image.Load(ms);
        check.Metadata.ExifProfile!.TryGetValue(ExifTag.GPSLatitude, out _).Should().BeTrue();
        check.Metadata.IptcProfile.Should().NotBeNull();
        check.Metadata.XmpProfile.Should().NotBeNull();
        check.Metadata.IccProfile.Should().NotBeNull();
        return ms.ToArray();
    }

    /// <summary>
    /// The smallest ICC profile a decoder accepts: the 128-byte header of an RGB display profile (v4.3, PCS XYZ,
    /// D50 illuminant) and an empty tag table. ImageSharp's typed ICC writer is internal, so the bytes are written here.
    /// </summary>
    private static byte[] MinimalIccProfile()
    {
        var icc = new byte[132];
        void Int(int offset, uint value)
        {
            icc[offset] = (byte)(value >> 24);
            icc[offset + 1] = (byte)(value >> 16);
            icc[offset + 2] = (byte)(value >> 8);
            icc[offset + 3] = (byte)value;
        }
        void Ascii(int offset, string value) => Encoding.ASCII.GetBytes(value).CopyTo(icc, offset);

        Int(0, (uint)icc.Length);   // size
        Int(8, 0x04300000);         // version 4.3
        Ascii(12, "mntr");          // display device
        Ascii(16, "RGB ");          // data colour space
        Ascii(20, "XYZ ");          // profile connection space
        Ascii(36, "acsp");          // file signature
        Int(64, 0);                 // rendering intent: perceptual
        Int(68, 0x0000F6D6);        // illuminant X 0.9642 (s15Fixed16)
        Int(72, 0x00010000);        // Y 1.0
        Int(76, 0x0000D32D);        // Z 0.8249
        Int(128, 0);                // tag count
        return icc;
    }

    /// <summary>Uploads <paramref name="bytes"/> and returns the stored picture as the app serves it.</summary>
    private async Task<byte[]> UploadAndFetchAsync(byte[] bytes, string fileName, string contentType)
    {
        using var content = CreateFileContent(bytes, fileName, contentType);
        var response = await Client.PostAsync("/api/images", content);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        var stored = await Client.GetAsync(result.GetProperty("url").GetString());
        stored.StatusCode.Should().Be(HttpStatusCode.OK);
        return await stored.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// AP16b, privacy policy point 3: an uploaded photo loses its embedded metadata — EXIF (GPS position, time,
    /// camera), IPTC and XMP, on the image and on every frame — whichever format the resize picks, and keeps its ICC
    /// profile (the colours).
    /// </summary>
    [Fact]
    public async Task Upload_PhotoWithMetadata_StoresItWithoutExifIptcAndXmp_KeepsIcc()
    {
        var stored = await UploadAndFetchAsync(CreatePhotoWithMetadata(640, 480), "photo.jpg", "image/jpeg");

        using var image = Image.Load(stored);
        image.Metadata.ExifProfile.Should().BeNull();
        image.Metadata.IptcProfile.Should().BeNull();
        image.Metadata.XmpProfile.Should().BeNull();
        foreach (var frame in image.Frames)
        {
            frame.Metadata.ExifProfile.Should().BeNull();
            frame.Metadata.IptcProfile.Should().BeNull();
            frame.Metadata.XmpProfile.Should().BeNull();
        }
        image.Metadata.IccProfile.Should().NotBeNull();

        // Not even as text somewhere in the file (a profile the decoder would not report)
        var text = Encoding.Latin1.GetString(stored);
        text.Should().NotContain("TestPhone").And.NotContain("Baden").And.NotContain("Exif\0\0");
    }

    /// <summary>
    /// The EXIF orientation leaves with the profile, so the picture is turned upright first: a landscape-encoded photo
    /// tagged "rotate 90°" (how phones store portrait photos) comes out as a portrait picture without the tag.
    /// </summary>
    [Fact]
    public async Task Upload_PhotoWithOrientationTag_IsStoredUpright()
    {
        var stored = await UploadAndFetchAsync(
            CreatePhotoWithMetadata(640, 320, ExifOrientationMode.RightTop), "portrait.jpg", "image/jpeg");

        using var image = Image.Load(stored);
        image.Metadata.ExifProfile.Should().BeNull();
        image.Height.Should().BeGreaterThan(image.Width);
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
