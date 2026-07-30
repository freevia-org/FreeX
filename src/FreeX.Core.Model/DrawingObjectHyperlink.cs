namespace FreeX.Core.Model;

/// <summary>
/// An object-level hyperlink on a drawing object's non-visual properties (<c>xdr:cNvPr</c>) --
/// ECMA-376 Part 1 §20.1.2.2.23 <c>CT_Hyperlink</c> / <c>&lt;a:hlinkClick&gt;</c> -- carried by
/// <see cref="DrawingShapeModel.Hyperlink"/>, <see cref="TextBoxModel.Hyperlink"/>, and
/// <see cref="PictureModel.Hyperlink"/>.
/// <para>
/// Unlike a CELL hyperlink (<see cref="HyperlinkMetadata"/>, spreadsheetML's own
/// <c>CT_Hyperlink</c>, which has a dedicated "location" attribute for an internal target),
/// DrawingML's <c>a:hlinkClick</c> has no "location" attribute at all: BOTH an external
/// ("Existing File or Web Page") target and an internal ("Place in This Document") target are
/// carried identically -- as the <c>r:id</c> relationship's Target, with <see cref="TargetMode"/>
/// "External" for the former and omitted (OPC default "Internal") for the latter. This mirrors
/// exactly what <c>XlsxWorksheetDrawingObjectWriter.ReadOldDrawingObjectHyperlinksByName</c> and
/// <c>AddObjectHyperlinkRelationship</c> already read/write for a SOURCE-LOADED object's
/// relationship -- this record gives that same (Target, TargetMode) shape a home on the MODEL so a
/// clone/paste/newly-authored object (which has no source package to re-read from) can carry a
/// hyperlink too. R97-model-drawing-hyperlink-2-2.
/// </para>
/// </summary>
/// <param name="Target">
/// The relationship's Target: an absolute/relative URI or file path for an external link, or a
/// workbook-internal reference (e.g. <c>"Sheet2!A1"</c> or a defined name) for an internal
/// ("Place in This Document") link. Always non-empty.
/// </param>
/// <param name="TargetMode">
/// The relationship's TargetMode attribute value, verbatim -- <c>"External"</c> for an external
/// link, or <see langword="null"/> for an internal link (the OPC default when the attribute is
/// omitted; matches how the existing writer/reader already treat this field for source-loaded
/// objects).
/// </param>
/// <param name="Tooltip">
/// The <c>a:hlinkClick@tooltip</c> attribute (Excel's hyperlink "ScreenTip"), or
/// <see langword="null"/> when none was authored.
/// </param>
public sealed record DrawingObjectHyperlink(
    string Target,
    string? TargetMode = null,
    string? Tooltip = null);
