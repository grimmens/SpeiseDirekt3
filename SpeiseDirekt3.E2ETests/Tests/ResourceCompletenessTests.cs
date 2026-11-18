using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// The resource files of AP12, without a browser: every neutral (English) resource of the app and of the shared core has
/// a German twin with exactly the same keys, no empty values and the same <c>{n}</c> placeholders; and every key the
/// code asks a localizer for (<c>T["Key"]</c>, <c>T.Plural(n, "Key")</c>) exists in that localizer's resource.
/// </summary>
public class ResourceCompletenessTests
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");

    /// <summary>The generated resource classes (<c>Resources/*Text.resx</c>) of the app and of SpeiseDirekt.Model.</summary>
    public static IEnumerable<Type> ResourceClasses =>
        new[] { typeof(SpeiseDirekt3.Program).Assembly, typeof(SpeiseDirekt.ValidationText).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Name.EndsWith("Text", StringComparison.Ordinal)
                        && t.GetProperty("ResourceManager", BindingFlags.Public | BindingFlags.Static)?.PropertyType == typeof(ResourceManager))
            .OrderBy(t => t.FullName);

    public static TheoryData<string> ResourceNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var type in ResourceClasses) data.Add(type.FullName!);
            return data;
        }
    }

    private static Dictionary<string, string> Entries(Type resource, CultureInfo culture)
    {
        var manager = (ResourceManager)resource.GetProperty("ResourceManager", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        // tryParents: false — only this culture's own file (the German satellite must have every key itself)
        var set = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        set.Should().NotBeNull($"{resource.Name} has resources for '{culture.Name}'");
        return set!.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => e.Value as string ?? "");
    }

    private static string[] Placeholders(string text) =>
        Regex.Matches(text, @"\{(\d+)(?:[,:][^}]*)?\}").Select(m => m.Groups[1].Value).Distinct().Order().ToArray();

    [Fact]
    public void TheResourcesAreFound()
    {
        var names = ResourceClasses.Select(t => t.Name).ToArray();
        names.Should().Contain(["CommonText", "ValidationText"]);
    }

    [Theory]
    [MemberData(nameof(ResourceNames))]
    public void GermanTwin_HasTheSameKeys_NoEmptyValues_AndTheSamePlaceholders(string resourceName)
    {
        var type = ResourceClasses.Single(t => t.FullName == resourceName);
        var english = Entries(type, CultureInfo.InvariantCulture);
        var german = Entries(type, German);

        english.Should().NotBeEmpty();
        german.Keys.Should().BeEquivalentTo(english.Keys, $"{type.Name}.de.resx has exactly the keys of {type.Name}.resx");
        foreach (var (key, text) in english)
        {
            text.Should().NotBeNullOrWhiteSpace($"{type.Name}.{key} (English) has a text");
            german[key].Should().NotBeNullOrWhiteSpace($"{type.Name}.{key} (German) has a text");
            Placeholders(german[key]).Should().Equal(Placeholders(text), $"{type.Name}.{key} uses the same placeholders in both languages");
            // Plurals: the suffixes _One/_Other are reserved for the two forms of one count (LocalizerExtensions.Plural)
            var twin = key.EndsWith("_One") ? key[..^"_One".Length] + "_Other"
                : key.EndsWith("_Other") ? key[..^"_Other".Length] + "_One" : null;
            if (twin is not null)
                english.Should().ContainKey(twin, $"{type.Name}.{key} is a plural form and needs its twin");
        }
    }

    /// <summary>Keys built from an enum at run time: every <c>TenantRole</c> has its display name (<c>CommonText.Role_*</c>).</summary>
    [Fact]
    public void EveryRole_HasADisplayName()
    {
        var common = Entries(typeof(SpeiseDirekt3.CommonText), CultureInfo.InvariantCulture);
        foreach (var role in Enum.GetNames<SpeiseDirekt.Model.TenantRole>())
            common.Should().ContainKey($"Role_{role}");
        common.Should().ContainKey("Role_Owner");
    }

    // --- Keys used in code ---

    /// <summary>The repository root (the folder with SpeiseDirekt3.sln), found from the test binaries upwards.</summary>
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SpeiseDirekt3.sln")))
                return dir.FullName;
        throw new InvalidOperationException("SpeiseDirekt3.sln not found above " + AppContext.BaseDirectory);
    }

    // @inject IStringLocalizer<ShellText> T  /  [Inject] … IStringLocalizer<ShellText> T { get; set; }  /  (IStringLocalizer<ShellText> t, …)
    private static readonly Regex Injection = new(@"IStringLocalizer<(\w+)>\s+(\w+)", RegexOptions.Compiled);

    [Fact]
    public void EveryKeyUsedInCode_ExistsInItsResource()
    {
        var app = Path.Combine(RepositoryRoot(), "SpeiseDirekt3");
        var resources = ResourceClasses.ToDictionary(t => t.Name, t => Entries(t, CultureInfo.InvariantCulture).Keys.ToHashSet());
        var files = Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".razor") || f.EndsWith(".cs"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

        var missing = new List<string>();
        var checkedKeys = 0;
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            foreach (Match injection in Injection.Matches(source))
            {
                var (resource, variable) = (injection.Groups[1].Value, injection.Groups[2].Value);
                if (!resources.TryGetValue(resource, out var keys))
                {
                    missing.Add($"{Path.GetFileName(file)}: unknown resource {resource}");
                    continue;
                }
                var name = Regex.Escape(variable);
                foreach (Match use in Regex.Matches(source, $@"(?<![\w.]){name}\[""(\w+)"""))
                {
                    checkedKeys++;
                    if (!keys.Contains(use.Groups[1].Value))
                        missing.Add($"{Path.GetFileName(file)}: {resource}.{use.Groups[1].Value}");
                }
                foreach (Match use in Regex.Matches(source, $@"(?<![\w.]){name}\.Plural\([^;]*?,\s*""(\w+)"""))
                    foreach (var key in new[] { use.Groups[1].Value + "_One", use.Groups[1].Value + "_Other" })
                    {
                        checkedKeys++;
                        if (!keys.Contains(key))
                            missing.Add($"{Path.GetFileName(file)}: {resource}.{key}");
                    }
            }
        }

        checkedKeys.Should().BeGreaterThan(0, "the scan finds the localizer calls");
        missing.Should().BeEmpty();
    }
}
