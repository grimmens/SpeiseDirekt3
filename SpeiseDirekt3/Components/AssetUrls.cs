using Microsoft.AspNetCore.Components;

namespace SpeiseDirekt3.Components;

public static class AssetUrls
{
    /// <summary>
    /// The fingerprinted URL of a static asset (<c>Assets[path]</c>), always starting with "/": the documents of the
    /// guest pages and the landing page have no <c>&lt;base href&gt;</c>.
    /// </summary>
    public static string Absolute(this ResourceAssetCollection assets, string path)
    {
        var url = assets[path];
        return url.StartsWith('/') ? url : "/" + url;
    }
}
