namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static Dictionary<string, string> CleanNativeAttributes(Dictionary<string, string>? attributes) =>
        CleanNativeAttributesCore(attributes);

    private static Dictionary<string, string> CleanNativeAttributesForSave(Dictionary<string, string>? attributes) =>
        CleanNativeAttributesCore(attributes);

    private static Dictionary<string, string> CleanNativeAttributesCore(Dictionary<string, string>? attributes) =>
        (attributes ?? new Dictionary<string, string>())
        .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
