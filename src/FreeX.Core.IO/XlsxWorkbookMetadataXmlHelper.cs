using System.Globalization;
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
    /// <c>password</c> attribute. <paramref name="passwordOrHash"/> must be a value already known
    /// by the caller to be a genuine 4-hex-digit legacy hash (e.g. one read verbatim from an
    /// existing file's <c>password</c> attribute and being round-tripped unchanged) — callers must
    /// NOT pass a freshly-typed plaintext password here on the strength of it merely looking like
    /// hex, because a real password such as "1234"/"abcd"/"c0de" is indistinguishable in shape
    /// from an actual hash (see <see cref="IsLegacyPasswordHash"/>). When in doubt, callers should
    /// track provenance explicitly (e.g. "this string came from XML we just read" vs. "this string
    /// is what the user typed into the Protect dialog") rather than relying on this method to guess.
    /// </summary>
    public static string ToLegacyPasswordHash(string passwordOrHash)
    {
        if (IsLegacyPasswordHash(passwordOrHash))
            return passwordOrHash.ToUpperInvariant();

        var hash = 0;
        for (var i = 0; i < passwordOrHash.Length; i++)
        {
            var value = passwordOrHash[i] << (i + 1);
            var rotatedBits = value >> 15;
            value &= 0x7fff;
            hash ^= value | rotatedBits;
        }

        hash ^= passwordOrHash.Length;
        hash ^= 0xCE4B;
        return hash.ToString("X4", CultureInfo.InvariantCulture);
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

    /// <summary>
    /// Heuristically identifies whether <paramref name="value"/> could be an existing legacy
    /// password verifier (as opposed to a freshly-typed plaintext password) so that
    /// <see cref="ToLegacyPasswordHash"/> can avoid re-hashing a value that is already a hash.
    /// A genuine legacy hash is always exactly 4 hex digits: the XOR/rotate algorithm always
    /// formats its result with <c>"X4"</c>, i.e. exactly 4 hex digits, zero-padded. A shorter
    /// hex-looking string (1-3 characters, e.g. "1", "ab", "abc") can therefore never be a genuine
    /// legacy hash and is always a real plaintext password — that case is unambiguous and rejected
    /// here.
    /// <para>
    /// A plaintext password that happens to be exactly 4 hex characters (e.g. "1234", "abcd",
    /// "c0de", "dead", "beef") is genuinely indistinguishable from a real hash by shape alone: this
    /// is a structural ambiguity in the legacy format itself, not something a smarter predicate can
    /// resolve from the string in isolation. Callers that know the provenance of the value (loaded
    /// verbatim from a file's <c>password</c> attribute vs. freshly typed by the user) must use
    /// that context instead of relying on this heuristic — see the caller notes on
    /// <see cref="ToLegacyPasswordHash"/>.
    /// </para>
    /// </summary>
    private static bool IsLegacyPasswordHash(string value) =>
        value.Length == 4 &&
        value.All(ch =>
            ch is >= '0' and <= '9' ||
            ch is >= 'A' and <= 'F' ||
            ch is >= 'a' and <= 'f');
}
