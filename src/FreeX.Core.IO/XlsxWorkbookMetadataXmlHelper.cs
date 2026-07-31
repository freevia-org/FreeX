using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookMetadataXmlHelper
{
    public static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    public static int? ClampWorkbookViewInteger(int? value, int min, int max) =>
        value is { } intValue ? Math.Clamp(intValue, min, max) : null;

    // Excel treats an activeTab/firstSheet that points at a hidden or veryHidden sheet as invalid
    // (it silently redirects, or flags the file for repair on open). Range-clamp the requested
    // index first, then — if the resulting sheet is hidden — redirect to the first VISIBLE sheet
    // in document order (a valid workbook always has at least one). A value that already points at
    // a visible sheet is left untouched. Shared by the primary workbookView writer and by every
    // additional (multi-window) workbookView so a sheet-count/visibility change reconciles both.
    public static int? ClampToVisibleSheetIndex(Workbook workbook, int? value)
    {
        var clamped = ClampWorkbookViewInteger(value, 0, Math.Max(0, workbook.Sheets.Count - 1));
        if (clamped is not { } index || workbook.Sheets.Count == 0)
            return clamped;

        var target = workbook.Sheets[index];
        if (!target.IsHidden && !target.IsVeryHidden)
            return clamped;

        for (var i = 0; i < workbook.Sheets.Count; i++)
        {
            if (!workbook.Sheets[i].IsHidden && !workbook.Sheets[i].IsVeryHidden)
                return i;
        }

        // No visible sheet at all — shouldn't happen in a valid workbook, but fall back to the
        // originally clamped value rather than throwing.
        return clamped;
    }

    public static bool HasRevisionProtectionMetadata(NativeXmlPreserveBag? metadata)
    {
        if (metadata is null)
            return false;
        var (attrs, _) = XmlNativeBagSerializer.Deserialize(metadata.Get("workbookProtection"));
        return attrs.ContainsKey("lockRevision") || attrs.ContainsKey("revisionsPassword");
    }

    public static bool TrySetNativeAttribute(XElement element, string name, string value)
    {
        try
        {
            element.SetAttributeValue(XName.Get(name), value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    public static void ApplyNativeAttributes(
        XElement element,
        IEnumerable<KeyValuePair<string, string>> attributes,
        params string[] excludedNames)
    {
        foreach (var attribute in attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Key) ||
                excludedNames.Contains(attribute.Key, StringComparer.Ordinal))
            {
                continue;
            }

            TrySetNativeAttribute(element, attribute.Key, attribute.Value);
        }
    }
}
