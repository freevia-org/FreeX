namespace FreeX.Core.Model;

/// <summary>
/// Vertical alignment of a rich-text run within its cell — maps to the OOXML
/// <c>&lt;vertAlign val="superscript|subscript"/&gt;</c> element inside <c>&lt;rPr&gt;</c>.
/// </summary>
public enum CellTextRunVertAlign
{
    /// <summary>Normal baseline position.</summary>
    None,
    /// <summary>Raised above the baseline (Excel: superscript).</summary>
    Superscript,
    /// <summary>Lowered below the baseline (Excel: subscript).</summary>
    Subscript,
}

/// <summary>
/// A single formatted run of text inside a rich-text cell.
/// All formatting properties are nullable; a null value means "inherit from the cell's
/// <see cref="CellStyle"/>".  Only deviating properties need to be set.
/// </summary>
/// <remarks>
/// Mirrors the OOXML <c>&lt;r&gt;&lt;rPr&gt;…&lt;/rPr&gt;&lt;t&gt;…&lt;/t&gt;&lt;/r&gt;</c>
/// structure inside an inline-string <c>&lt;is&gt;</c> or shared-string <c>&lt;si&gt;</c> element.
/// Modelled after <c>HeaderFooterFormattedRun</c> in PageContentRenderModel.cs.
/// </remarks>
public sealed record CellTextRun(
    string Text,
    bool? Bold,
    bool? Italic,
    bool? Underline,
    bool? Strikethrough,
    string? FontName,
    double? FontSize,
    CellColor? FontColor,
    CellTextRunVertAlign VertAlign = CellTextRunVertAlign.None);
