using System.Globalization;
using System.Text;

namespace SpeiseDirekt3.Components.QrCodes
{
    /// <summary>
    /// URLs and file names around a QR code: the guest link it encodes (<c>/qr/{id}</c>), its PNG
    /// (<c>/Image/qr/{id}</c>, served by <c>ImageController</c>) and the download variant of that PNG.
    /// Paths are relative to the app's base URI.
    /// </summary>
    public static class QrCodeLinks
    {
        /// <summary>Relative path of the guest page the QR code points to.</summary>
        public static string GuestPath(Guid qrId) => $"qr/{qrId}";

        /// <summary>Relative path of the QR code PNG (shown inline).</summary>
        public static string ImagePath(Guid qrId) => $"Image/qr/{qrId}";

        /// <summary>Relative path of the QR code PNG as an attachment named after <paramref name="title"/>.</summary>
        public static string DownloadPath(Guid qrId, string? title) =>
            $"Image/qr/{qrId}?download=1&name={Uri.EscapeDataString(DownloadFileName(title))}";

        /// <summary>File name for a downloaded QR code, e.g. <c>qr-tisch-1.png</c> for "Tisch 1".</summary>
        public static string DownloadFileName(string? title)
        {
            var slug = Slug(title);
            return slug.Length == 0 ? "qr-code.png" : $"qr-{slug}.png";
        }

        /// <summary>
        /// Accepts a client-supplied download name only if it is a plain <c>.png</c> file name made of
        /// lower-case letters, digits and dashes (what <see cref="DownloadFileName"/> produces); otherwise null.
        /// </summary>
        public static string? SanitizeDownloadName(string? name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 120 || !name.EndsWith(".png", StringComparison.Ordinal))
                return null;
            var stem = name[..^4];
            return stem.Length > 0 && stem.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-') ? name : null;
        }

        private static string Slug(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            var text = title.Trim().ToLowerInvariant()
                .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
            var builder = new StringBuilder();
            foreach (var c in text.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue; // drop accents: é → e
                if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
                    builder.Append(c);
                else if (builder.Length > 0 && builder[^1] != '-')
                    builder.Append('-');
            }
            var slug = builder.ToString().Trim('-');
            return slug.Length > 60 ? slug[..60].TrimEnd('-') : slug;
        }
    }
}
