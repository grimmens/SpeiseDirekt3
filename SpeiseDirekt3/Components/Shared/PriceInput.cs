using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace SpeiseDirekt3.Components.Shared;

/// <summary>
/// A euro amount field for the back office (item price, variants, extras, combo price), instead of
/// <c>&lt;input type="number"&gt;</c>: a browser localizes number fields by its <i>own</i> UI language, so a German page in
/// an English browser read "5,90" as 590. This is a text field (<c>inputmode="decimal"</c>: the phone shows the
/// decimal keypad) in the page's culture — "5,90" (German) / "5.90" (English) — that accepts both separators: one
/// separator is always the decimal separator, with both ("1.234,50", "1,234.50") the last one is. "€" and spaces are
/// ignored, a sign is not accepted (prices are never negative). Use like <c>InputNumber</c>:
/// <c>&lt;PriceInput @bind-Value="Input.Price" id="…" class="sd-input" /&gt;</c>.
/// </summary>
public sealed class PriceInput : InputBase<decimal>
{
    [Parameter] public string? Placeholder { get; set; }

    /// <summary>The rendered &lt;input&gt; (e.g. to focus it).</summary>
    public ElementReference? Element { get; private set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "input");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "type", "text");
        builder.AddAttribute(3, "inputmode", "decimal");
        builder.AddAttribute(4, "autocomplete", "off");
        builder.AddAttribute(5, "placeholder", Placeholder ?? 0m.ToString("0.00", CultureInfo.CurrentCulture));
        builder.AddAttributeIfNotNullOrEmpty(6, "name", NameAttributeValue);
        builder.AddAttributeIfNotNullOrEmpty(7, "class", CssClass);
        builder.AddAttribute(8, "value", CurrentValueAsString);
        builder.AddAttribute(9, "onchange", EventCallback.Factory.CreateBinder<string?>(this, value => CurrentValueAsString = value, CurrentValueAsString));
        builder.SetUpdatesAttributeName("value");
        builder.AddElementReferenceCapture(10, reference => Element = reference);
        builder.CloseElement();
    }

    /// <summary>"5,90" / "5.90": two decimals in the page's culture, no group separator.</summary>
    protected override string? FormatValueAsString(decimal value) => value.ToString("0.00", CultureInfo.CurrentCulture);

    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out decimal result,
        [NotNullWhen(false)] out string? validationErrorMessage)
    {
        if (TryParse(value, out result))
        {
            validationErrorMessage = null;
            return true;
        }
        validationErrorMessage = string.Format(CultureInfo.CurrentCulture, SpeiseDirekt.ValidationText.PriceFormat,
            5.9m.ToString("0.00", CultureInfo.CurrentCulture));
        return false;
    }

    /// <summary>Reads "5,90", "5.90", "€ 5,90", "1.234,50", "1,234.50", "12"; null for anything else (also negatives).</summary>
    public static bool TryParse(string? text, out decimal price)
    {
        price = 0;
        var cleaned = new string((text ?? "").Where(c => !char.IsWhiteSpace(c) && c != '€').ToArray());
        if (cleaned.Length == 0)
            return false;
        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');
        var decimalAt = Math.Max(lastComma, lastDot);
        string normalized;
        if (decimalAt < 0)
            normalized = cleaned;
        else
        {
            var whole = cleaned[..decimalAt].Replace(",", "").Replace(".", "");
            normalized = whole + "." + cleaned[(decimalAt + 1)..];
        }
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out price);
    }
}

internal static class RenderTreeBuilderExtensions
{
    public static void AddAttributeIfNotNullOrEmpty(this RenderTreeBuilder builder, int sequence, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            builder.AddAttribute(sequence, name, value);
    }
}
