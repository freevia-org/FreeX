namespace FreeX.Core.IO;

internal enum XlsxPrintSettingKind
{
    PrintArea,
    PrintTitles,
}

internal static class XlsxPrintSettingNameClassifier
{
    public static bool TryClassify(string? name, out XlsxPrintSettingKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var trimmed = name.Trim();
        var unprefixed = trimmed.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase)
            ? trimmed["_xlnm.".Length..]
            : trimmed;

        if (string.Equals(unprefixed, "Print_Area", StringComparison.OrdinalIgnoreCase))
        {
            kind = XlsxPrintSettingKind.PrintArea;
            return true;
        }

        if (string.Equals(unprefixed, "Print_Titles", StringComparison.OrdinalIgnoreCase))
        {
            kind = XlsxPrintSettingKind.PrintTitles;
            return true;
        }

        return false;
    }
}
