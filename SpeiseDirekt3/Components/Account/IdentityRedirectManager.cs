using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;

namespace SpeiseDirekt3.Components.Account
{
    public sealed class IdentityRedirectManager(NavigationManager navigationManager)
    {
        public const string StatusCookieName = "Identity.StatusMessage";

        private static readonly CookieBuilder StatusCookieBuilder = new()
        {
            SameSite = SameSiteMode.Strict,
            HttpOnly = true,
            IsEssential = true,
            MaxAge = TimeSpan.FromSeconds(5),
        };

        [DoesNotReturn]
        public void RedirectTo(string? uri)
        {
            uri ??= "";
            
            // Prevent open redirects.
            if (!Uri.IsWellFormedUriString(uri, UriKind.Relative))
            {
                // Handle absolute URIs that might have different schemes
                if (Uri.TryCreate(uri, UriKind.Absolute, out var absoluteUri))
                {
                    // Check if it's the same host but different scheme
                    var baseUri = new Uri(navigationManager.BaseUri);
                    if (absoluteUri.Host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase))
                    {
                        // Use the path and query from the absolute URI
                        uri = absoluteUri.PathAndQuery;
                    }
                    else
                    {
                        // External URL - redirect to home for security
                        uri = "/";
                    }
                }
                else
                {
                    // If it's not a well-formed relative URI and not a valid absolute URI,
                    // try to convert it anyway (this was the original behavior)
                    try
                    {
                        uri = navigationManager.ToBaseRelativePath(uri);
                    }
                    catch (ArgumentException)
                    {
                        // If conversion fails, redirect to home
                        uri = "/";
                    }
                }
            }
            
            navigationManager.NavigateTo(uri);
            throw new InvalidOperationException($"{nameof(IdentityRedirectManager)} can only be used during static rendering.");
        }

        [DoesNotReturn]
        public void RedirectTo(string uri, Dictionary<string, object?> queryParameters)
        {
            var uriWithoutQuery = navigationManager.ToAbsoluteUri(uri).GetLeftPart(UriPartial.Path);
            var newUri = navigationManager.GetUriWithQueryParameters(uriWithoutQuery, queryParameters);
            RedirectTo(newUri);
        }

        /// <summary>
        /// Redirects and leaves <paramref name="message"/> for the next page's <c>StatusMessage</c> (a short-lived
        /// cookie). <paramref name="isError"/> shows it in red. The text is localized by the caller.
        /// </summary>
        [DoesNotReturn]
        public void RedirectToWithStatus(string uri, string message, HttpContext context, bool isError = false)
        {
            context.Response.Cookies.Append(StatusCookieName, (isError ? ErrorPrefix : SuccessPrefix) + message, StatusCookieBuilder.Build(context));
            RedirectTo(uri);
        }

        // The cookie says whether the message is an error (the visible text no longer starts with "Error", which
        // would not work in German)
        private const string ErrorPrefix = "e:";
        private const string SuccessPrefix = "s:";

        /// <summary>The message of a status cookie and whether it is an error.</summary>
        public static (string Message, bool IsError) ReadStatus(string cookie) =>
            cookie.StartsWith(ErrorPrefix, StringComparison.Ordinal) ? (cookie[ErrorPrefix.Length..], true)
            : cookie.StartsWith(SuccessPrefix, StringComparison.Ordinal) ? (cookie[SuccessPrefix.Length..], false)
            : (cookie, cookie.StartsWith("Error", StringComparison.Ordinal));

        private string CurrentPath => navigationManager.ToAbsoluteUri(navigationManager.Uri).GetLeftPart(UriPartial.Path);

        [DoesNotReturn]
        public void RedirectToCurrentPage() => RedirectTo(CurrentPath);

        [DoesNotReturn]
        public void RedirectToCurrentPageWithStatus(string message, HttpContext context, bool isError = false)
            => RedirectToWithStatus(CurrentPath, message, context, isError);
    }
}
