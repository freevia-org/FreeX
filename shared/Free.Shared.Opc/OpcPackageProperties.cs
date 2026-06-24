using System.Globalization;

namespace Free.Shared.Opc;

/// <summary>
/// OPC package-property part constants in both conventions, plus W3CDTF date helpers.
/// <para>
/// <b>OPC PartName convention</b> (leading slash, e.g. <c>/docProps/core.xml</c>): used wherever
/// the OPC spec requires an absolute PartName — <c>[Content_Types].xml</c> Override/@PartName,
/// package-level relationship Target values that are PartNames, etc.  FreeW uses this form.
/// </para>
/// <para>
/// <b>ZIP-entry convention</b> (no leading slash, e.g. <c>docProps/core.xml</c>): used when
/// opening entries via <see cref="System.IO.Compression.ZipArchive.GetEntry"/>, which expects
/// the raw ZIP central-directory name.  FreeX uses this form.
/// </para>
/// </summary>
public static class OpcPackageProperties
{
    // ── OPC PartNames (leading slash) ──────────────────────────────────────────
    // Use these wherever the OPC spec requires an absolute PartName
    // (e.g. [Content_Types].xml Override/@PartName).

    /// <summary>OPC PartName of the core-properties part (Dublin Core + cp vocabulary).</summary>
    public const string CorePropertiesPartName = "/docProps/core.xml";

    /// <summary>OPC PartName of the extended-properties part (application / company metadata).</summary>
    public const string ExtendedPropertiesPartName = "/docProps/app.xml";

    /// <summary>OPC PartName of the custom-properties part (arbitrary name/value pairs).</summary>
    public const string CustomPropertiesPartName = "/docProps/custom.xml";

    // ── ZIP-entry names (no leading slash) ────────────────────────────────────
    // Use these with ZipArchive.GetEntry() / ZipArchive.CreateEntry().

    /// <summary>ZIP-entry name of the core-properties part (no leading slash).</summary>
    public const string CorePropertiesZipEntry = "docProps/core.xml";

    /// <summary>ZIP-entry name of the extended-properties part (no leading slash).</summary>
    public const string ExtendedPropertiesZipEntry = "docProps/app.xml";

    /// <summary>ZIP-entry name of the custom-properties part (no leading slash).</summary>
    public const string CustomPropertiesZipEntry = "docProps/custom.xml";

    // ── Content types ──────────────────────────────────────────────────────────

    /// <summary>OPC content type for <c>docProps/core.xml</c>.</summary>
    public const string CorePropertiesContentType =
        "application/vnd.openxmlformats-package.core-properties+xml";

    /// <summary>OPC content type for <c>docProps/app.xml</c>.</summary>
    public const string ExtendedPropertiesContentType =
        "application/vnd.openxmlformats-officedocument.extended-properties+xml";

    /// <summary>OPC content type for <c>docProps/custom.xml</c>.</summary>
    public const string CustomPropertiesContentType =
        "application/vnd.openxmlformats-officedocument.custom-properties+xml";

    // ── Relationship types ─────────────────────────────────────────────────────

    /// <summary>Package-level relationship type for <c>docProps/core.xml</c>.</summary>
    public const string CorePropertiesRelationshipType =
        "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";

    /// <summary>Package-level relationship type for <c>docProps/app.xml</c>.</summary>
    public const string ExtendedPropertiesRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties";

    /// <summary>Package-level relationship type for <c>docProps/custom.xml</c>.</summary>
    public const string CustomPropertiesRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties";

    // ── W3CDTF date helpers ────────────────────────────────────────────────────
    // Authoritative implementation, moved verbatim from FreeW (Ooxml.cs).

    /// <summary>
    /// Formats <paramref name="value"/> as a W3CDTF timestamp (UTC, second precision, trailing
    /// <c>Z</c>) as required by <c>dcterms:created</c> / <c>dcterms:modified</c> in
    /// <c>docProps/core.xml</c>.
    /// </summary>
    public static string ToW3CDtf(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a W3CDTF / ISO-8601 timestamp string; returns <see langword="null"/> if the value
    /// is absent or cannot be parsed.
    /// </summary>
    public static DateTimeOffset? ParseW3CDtf(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var v) ? v : null;
}
