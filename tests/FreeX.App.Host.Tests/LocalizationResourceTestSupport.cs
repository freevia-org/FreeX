namespace FreeX.App.Host.Tests;

// FreeX-specific shim: binds the app-neutral resx/placeholder mechanics
// (ResxResourceTestSupport, shared) to FreeX's own Resources directory.
internal static class LocalizationResourceTestSupport
{
    public static string ResourceDirectory =>
        DialogSourceTestSupport.FindHostSourceDirectory("Resources", "Strings.resx");

    public static Dictionary<string, string> ReadResxValues(string fileName) =>
        ResxResourceTestSupport.ReadResxValues(ResourceDirectory, fileName);

    public static HashSet<string> CompositePlaceholderTokens(string value) =>
        ResxResourceTestSupport.CompositePlaceholderTokens(value);

    public static int AccessKeyCount(string value) =>
        ResxResourceTestSupport.AccessKeyCount(value);

    public static int CountAsciiLettersOutsideCompositePlaceholders(string value) =>
        ResxResourceTestSupport.CountAsciiLettersOutsideCompositePlaceholders(value);
}
