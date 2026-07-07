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

    public static bool HasRevisionProtectionMetadata(NativeXmlPreserveBag? metadata)
    {
        if (metadata is null)
            return false;
        var (attrs, _) = XmlNativeBagSerializer.Deserialize(metadata.Get("workbookProtection"));
        return attrs.ContainsKey("lockRevision") || attrs.ContainsKey("revisionsPassword");
    }

    /// <summary>
    /// Encodes a legacy (pre-2013, XOR/rotate) password verifier for the ISO/IEC 29500
    /// <c>password</c> attribute. Delegates to <see cref="ProtectionPasswordHelper.ToLegacyPasswordHash"/>
    /// (the single canonical implementation of this algorithm) rather than duplicating it here.
    /// <paramref name="passwordOrHash"/> must be a value already known by the caller to be a
    /// genuine 4-hex-digit legacy hash (e.g. one read verbatim from an existing file's
    /// <c>password</c> attribute, or a value that was already hashed via
    /// <see cref="ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash"/> at the point the user
    /// typed it) — callers on the write path must NOT reach this method with untouched freshly-typed
    /// plaintext, because a real password such as "1234"/"abcd"/"c0de" is indistinguishable in shape
    /// from an actual hash (see <see cref="ProtectionPasswordHelper"/>'s own remarks). Both the sheet/workbook
    /// protection commands and the Allow-Edit-Range dialog now hash typed passwords immediately when
    /// they are set, so by the time <c>Sheet.ProtectionPassword</c>/
    /// <c>Workbook.StructureProtectionPassword</c>/an <c>AllowEditRangePasswords</c> value reaches
    /// this writer helper it is unconditionally already a hash and this method only ever round-trips
    /// one.
    /// </summary>
    public static string ToLegacyPasswordHash(string passwordOrHash) =>
        ProtectionPasswordHelper.ToLegacyPasswordHash(passwordOrHash);

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
