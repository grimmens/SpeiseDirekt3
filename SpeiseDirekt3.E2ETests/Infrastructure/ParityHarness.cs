using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace SpeiseDirekt3.E2ETests.Infrastructure;

/// <summary>
/// AP15b scratch harness for the visual parity check (never committed). With <c>E2E_PARITY=1</c> ids, e-mail addresses and
/// the server port are deterministic, so the "before" (Play CDN) and "after" (local tailwind.css) screenshots of the same
/// tour differ only by CSS. With <c>E2E_CLASS_LOG=file</c> every class token seen in a back-office document is appended to
/// the file (the same candidates the Play CDN compiled: classList of every [class] element after every DOM mutation).
/// </summary>
public static class ParityHarness
{
    public static bool On => Environment.GetEnvironmentVariable("E2E_PARITY") == "1";

    public static int Port =>
        On && int.TryParse(Environment.GetEnvironmentVariable("E2E_PARITY_PORT"), out var p) ? p : 0;

    private static int _guid;
    private static int _email;

    public static Guid NewGuid() =>
        On ? new Guid(Interlocked.Increment(ref _guid), 0x5d5d, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, 0) : Guid.NewGuid();

    public static string? Email(string prefix) =>
        On ? $"{prefix}-{Interlocked.Increment(ref _email):D4}@e2e.test" : null;

    private static readonly ConcurrentDictionary<string, byte> Seen = new();
    private static readonly object FileLock = new();

    private const string Script = """
        (() => {
          const seen = new Set();
          let queued = [];
          let timer = null;
          const flush = () => {
            timer = null;
            if (!queued.length || typeof window.__sdClassReport !== 'function') return;
            const t = queued.join(' ');
            queued = [];
            window.__sdClassReport(t);
          };
          const scan = () => {
            const r = document.documentElement;
            if (!r || r.hasAttribute('data-guest-page') || r.hasAttribute('data-landing-page') || r.hasAttribute('data-auth-page')) return;
            for (const el of document.querySelectorAll('[class]'))
              for (const c of el.classList)
                if (!seen.has(c)) { seen.add(c); queued.push(c); }
            if (queued.length && !timer) timer = setTimeout(flush, 30);
          };
          new MutationObserver(scan).observe(document, { attributes: true, attributeFilter: ['class'], childList: true, subtree: true });
          document.addEventListener('DOMContentLoaded', scan);
          window.addEventListener('load', scan);
        })();
        """;

    public static async Task<IBrowserContext> AttachAsync(IBrowserContext context)
    {
        var file = Environment.GetEnvironmentVariable("E2E_CLASS_LOG");
        if (string.IsNullOrEmpty(file)) return context;
        await context.ExposeFunctionAsync("__sdClassReport", (string tokens) =>
        {
            foreach (var t in tokens.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (Seen.TryAdd(t, 0))
                    lock (FileLock) File.AppendAllText(file, t + "\n");
        });
        await context.AddInitScriptAsync(Script);
        return context;
    }
}
