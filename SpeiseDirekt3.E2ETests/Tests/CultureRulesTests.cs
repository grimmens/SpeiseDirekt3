using System.Globalization;
using FluentAssertions;
using SpeiseDirekt3.Components.Shared;
using SpeiseDirekt3.Localization;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// The pure rules of AP12 without a server: which language an <c>Accept-Language</c> header gets
/// (<see cref="AppCultures.Select"/>), how a price field reads what a person typed (<see cref="PriceInput.TryParse"/>) and
/// how prices look (<see cref="UiFormat.Price"/>).
/// </summary>
public class CultureRulesTests
{
    [Theory]
    [InlineData(null, "de")]
    [InlineData("", "de")]
    [InlineData("   ", "de")]
    [InlineData("de-DE", "de")]
    [InlineData("de-AT", "de")]
    [InlineData("de-CH", "de")]
    [InlineData("DE", "de")]
    [InlineData("en", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("en-US,en;q=0.9", "en")]
    [InlineData("fr", "en")]
    [InlineData("fr-FR, en;q=0.8", "en")]
    [InlineData("fr-FR, de;q=0.8", "de")]
    [InlineData("fr-CH, de;q=0.9, en;q=0.8", "de")]
    [InlineData("en;q=0.4, de;q=0.6", "de")]
    [InlineData("de;q=0, en;q=0.1", "en")]
    [InlineData("it, *;q=0.1", "de")]
    [InlineData("ja, zh-CN;q=0.9", "en")]
    public void AcceptLanguage_PicksGermanOrEnglish(string? header, string expected) =>
        AppCultures.Select(header).Name.Should().Be(expected);

    [Theory]
    [InlineData("5,90", 5.90)]
    [InlineData("5.90", 5.90)]
    [InlineData("5,9", 5.9)]
    [InlineData("5", 5)]
    [InlineData(" 12 ", 12)]
    [InlineData("€ 5,90", 5.90)]
    [InlineData("5,90 €", 5.90)]
    [InlineData("1.234,50", 1234.50)]
    [InlineData("1,234.50", 1234.50)]
    [InlineData("0", 0)]
    [InlineData(",5", 0.5)]
    public void PriceInput_ReadsBothDecimalSeparators(string text, double expected)
    {
        PriceInput.TryParse(text, out var price).Should().BeTrue(text);
        price.Should().Be((decimal)expected, text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("5 Euro")]
    [InlineData(null)]
    public void PriceInput_RefusesWhatIsNoPrice(string? text) =>
        PriceInput.TryParse(text, out _).Should().BeFalse(text ?? "null");

    [Fact]
    public void Prices_FollowTheCulture_AndAreAlwaysEuros()
    {
        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = AppCultures.German;
            UiFormat.Price(5.9m).Should().Be("5,90 €");
            UiFormat.Price(1234.5m).Should().Be("1.234,50 €");
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = AppCultures.English;
            UiFormat.Price(5.9m).Should().Be("€5.90");
            UiFormat.Price(1234.5m).Should().Be("€1,234.50");
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before;
        }
    }
}
