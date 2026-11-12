using System.IO;

namespace FreeX.App.Host.Tests;

// FreeX-specific shim: binds the app-neutral resx/placeholder mechanics
// (ResxResourceTestSupport, shared) to the shared FreeX.App.Localization Resources directory.
internal static class LocalizationResourceTestSupport
{
    public static string ResourceDirectory =>
        Path.GetDirectoryName(
            WorkspaceFileLocator.Find("src", "FreeX.App.Localization", "Resources", "Strings.resx"))
        ?? throw new DirectoryNotFoundException("Could not locate FreeX.App.Localization Resources directory.");

    public static Dictionary<string, string> ReadResxValues(string fileName) =>
        ResxResourceTestSupport.ReadResxValues(ResourceDirectory, fileName);

    // The effective neutral catalog is immutable for the life of the process and every consumer
    // only reads it, so compute it once instead of once per culture-theory case.
    private static readonly Lazy<Dictionary<string, string>> EffectiveNeutralValues = new(
        () => UiText.GetNeutralResourceKeys()
            .ToDictionary(key => key, UiText.GetNeutral, StringComparer.Ordinal),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static Dictionary<string, string> ReadEffectiveNeutralValues() => EffectiveNeutralValues.Value;

    public static HashSet<string> CompositePlaceholderTokens(string value) =>
        ResxResourceTestSupport.CompositePlaceholderTokens(value);

    public static int AccessKeyCount(string value) =>
        ResxResourceTestSupport.AccessKeyCount(value);

    public static int CountAsciiLettersOutsideCompositePlaceholders(string value) =>
        ResxResourceTestSupport.CountAsciiLettersOutsideCompositePlaceholders(value);
}
