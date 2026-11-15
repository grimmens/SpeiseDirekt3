using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Model
{
    /// <summary>
    /// An optional absolute web address: empty is valid, anything else must be an absolute <c>http://</c> or
    /// <c>https://</c> URL with a host (so "example.com" or "javascript:…" are rejected).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
    public sealed class HttpUrlAttribute : ValidationAttribute
    {
        public HttpUrlAttribute()
            : base("Enter a full web address starting with https:// (or http://).") { }

        public static bool IsHttpUrl(string? value) =>
            Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && !string.IsNullOrEmpty(uri.Host);

        public override bool IsValid(object? value) =>
            value is null || value is string text && (string.IsNullOrWhiteSpace(text) || IsHttpUrl(text));
    }

    /// <summary>An optional e-mail address: empty is valid, otherwise the rules of <see cref="EmailAddressAttribute"/>.</summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
    public sealed class OptionalEmailAttribute : ValidationAttribute
    {
        private static readonly EmailAddressAttribute Email = new();

        public OptionalEmailAttribute()
            : base("Enter a valid e-mail address.") { }

        public override bool IsValid(object? value) =>
            value is null || value is string text && (string.IsNullOrWhiteSpace(text) || Email.IsValid(text.Trim()));
    }
}
