using Free.Shared.Pdf;

namespace Free.Shared.Pdf.Skia;

/// <summary>
/// Opens a caller-selected, single-face TrueType/OpenType font for an authored family and style.
/// Return null to retain system font resolution. Each non-null result must be a fresh readable
/// stream positioned at its beginning; the writer consumes and disposes it, including on failure.
/// The writer caches the resulting native font only for the current export. Supplying invalid
/// font data fails that export rather than silently substituting another face.
/// </summary>
public delegate Stream? PdfFontSourceResolver(string? fontFamily, PdfFontFace face);
