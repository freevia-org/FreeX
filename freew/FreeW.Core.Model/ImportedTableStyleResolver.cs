using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace FreeW.Core.Model;

/// <summary>
/// The conditional formatting a table style contributes to one cell after Word's <c>w:tblLook</c>
/// cascade. Null members mean "the style says nothing", so the caller keeps its own value.
/// </summary>
public sealed record ImportedTableStyleCellFormat(
    string? FillHex,
    bool? Bold,
    bool? Italic,
    string? ColorHex)
{
    public static readonly ImportedTableStyleCellFormat Empty = new(null, null, null, null);
}

/// <summary>
/// Render-only resolution of an imported <c>w:style w:type="table"</c> definition
/// (<see cref="DocumentStyle.PreservedTableStyleXml"/>). The document's own definition is authoritative
/// over FreeW's built-in gallery entry of the same id: Word paints what styles.xml says, including
/// "no banding" when the style declares no <c>w:tblStyleRowBandSize</c>. Nothing resolved here is written
/// back into the model, so the package round-trips unchanged.
/// </summary>
public sealed class ImportedTableStyle
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly ConditionalWeakTable<DocumentStyle, ParsedStyle> ParsedStyles = new();

    private readonly Dictionary<string, ImportedTableStyleCellFormat> conditions;

    private ImportedTableStyle(
        Dictionary<string, ImportedTableStyleCellFormat> conditions,
        int rowBandSize,
        int columnBandSize,
        ParagraphFormatting paragraph)
    {
        this.conditions = conditions;
        RowBandSize = rowBandSize;
        ColumnBandSize = columnBandSize;
        Paragraph = paragraph;
    }

    /// <summary>Rows per horizontal band; 0 when the style chain declares none (Word then paints no bands).</summary>
    public int RowBandSize { get; }

    /// <summary>Columns per vertical band; 0 when the style chain declares none.</summary>
    public int ColumnBandSize { get; }

    /// <summary>The table style's own paragraph properties (<c>w:style/w:pPr</c>) through its based-on chain.</summary>
    public ParagraphFormatting Paragraph { get; }

    /// <summary>
    /// Resolves the imported definition for <paramref name="styleId"/>, or null when the document carries
    /// no imported table style of that id (FreeW-authored tables keep using the built-in gallery).
    /// </summary>
    public static ImportedTableStyle? TryResolve(
        IReadOnlyDictionary<string, DocumentStyle> styles,
        string? styleId)
    {
        ArgumentNullException.ThrowIfNull(styles);
        if (string.IsNullOrWhiteSpace(styleId)
            || !styles.TryGetValue(styleId, out var leaf)
            || leaf.Type != StyleType.Table
            || leaf.PreservedTableStyleXml is not { Length: > 0 })
        {
            // A table with no (resolvable) w:tblStyle uses the package's default table style
            // (Word's "Table Normal"). Only imported packages carry one, so FreeW-authored tables
            // keep their gallery/legacy look.
            leaf = styles.Values.FirstOrDefault(style =>
                style is { Type: StyleType.Table, IsDefault: true, PreservedTableStyleXml.Length: > 0 });
            if (leaf is null)
                return null;
        }

        var chain = new List<DocumentStyle>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = leaf; current is not null && seen.Add(current.Id);)
        {
            chain.Add(current);
            current = current.BasedOnStyleId is { Length: > 0 } parentId
                && styles.TryGetValue(parentId, out var parent)
                && parent.Type == StyleType.Table
                    ? parent
                    : null;
        }

        var conditions = new Dictionary<string, ImportedTableStyleCellFormat>(StringComparer.Ordinal);
        var rowBandSize = 0;
        var columnBandSize = 0;
        var paragraph = ParagraphFormatting.Default;
        string? borderColorToken = null;
        for (var index = chain.Count - 1; index >= 0; index--)
        {
            var parsed = ParsedStyles.GetValue(chain[index], Parse);
            borderColorToken = parsed.BorderColorToken ?? borderColorToken;
            rowBandSize = parsed.RowBandSize ?? rowBandSize;
            columnBandSize = parsed.ColumnBandSize ?? columnBandSize;
            foreach (var (type, format) in parsed.Conditions)
            {
                conditions[type] = conditions.TryGetValue(type, out var inherited)
                    ? Overlay(inherited, format)
                    : format;
            }

            var own = chain[index].Paragraph;
            paragraph = paragraph with
            {
                SpaceBeforePt = own.SpaceBeforeIsSet ? own.SpaceBeforePt : paragraph.SpaceBeforePt,
                SpaceBeforeIsSet = paragraph.SpaceBeforeIsSet || own.SpaceBeforeIsSet,
                SpaceAfterPt = own.SpaceAfterIsSet ? own.SpaceAfterPt : paragraph.SpaceAfterPt,
                SpaceAfterIsSet = paragraph.SpaceAfterIsSet || own.SpaceAfterIsSet,
                LineSpacing = own.LineSpacingIsSet ? own.LineSpacing : paragraph.LineSpacing,
                LineRule = own.LineSpacingIsSet ? own.LineRule : paragraph.LineRule,
                LineHeightPt = own.LineSpacingIsSet ? own.LineHeightPt : paragraph.LineHeightPt,
                LineSpacingIsSet = paragraph.LineSpacingIsSet || own.LineSpacingIsSet,
            };
        }

        return new ImportedTableStyle(conditions, rowBandSize, columnBandSize, paragraph)
        {
            BorderColorToken = borderColorToken
        };
    }

    /// <summary>Colour token ("auto" or RRGGBB) of the style's visible <c>w:tblBorders</c>, or null.</summary>
    public string? BorderColorToken { get; private init; }

    /// <summary>
    /// Applies Word's conditional-format precedence (whole table, vertical bands, horizontal bands, first/last
    /// column, first/last row, corner cells) for one cell under the table's <c>w:tblLook</c> toggles.
    /// </summary>
    public ImportedTableStyleCellFormat ResolveCell(
        TableFormatting look,
        int rowIndex,
        int rowCount,
        int gridColumnIndex,
        int gridSpan,
        int gridColumnCount)
    {
        ArgumentNullException.ThrowIfNull(look);

        var isFirstRow = look.HeaderRow && rowIndex == 0;
        var isLastRow = look.LastRow && rowCount > 1 && rowIndex == rowCount - 1;
        var isFirstColumn = look.FirstColumn && gridColumnIndex == 0;
        var isLastColumn = look.LastColumn
            && gridColumnCount > 1
            && gridColumnIndex + Math.Max(1, gridSpan) >= gridColumnCount;

        var result = ImportedTableStyleCellFormat.Empty;
        void Apply(string type)
        {
            if (conditions.TryGetValue(type, out var format))
                result = Overlay(result, format);
        }

        Apply("wholeTable");
        if (look.BandedColumns && ColumnBandSize > 0 && !isFirstColumn && !isLastColumn)
        {
            var bodyColumn = gridColumnIndex - (look.FirstColumn ? 1 : 0);
            if (bodyColumn >= 0)
                Apply(bodyColumn / ColumnBandSize % 2 == 0 ? "band1Vert" : "band2Vert");
        }
        if (look.BandedRows && RowBandSize > 0 && !isFirstRow && !isLastRow)
        {
            var bodyRow = rowIndex - (look.HeaderRow ? 1 : 0);
            if (bodyRow >= 0)
                Apply(bodyRow / RowBandSize % 2 == 0 ? "band1Horz" : "band2Horz");
        }
        if (isFirstColumn)
            Apply("firstCol");
        if (isLastColumn)
            Apply("lastCol");
        if (isFirstRow)
            Apply("firstRow");
        if (isLastRow)
            Apply("lastRow");
        if (isFirstRow && isFirstColumn)
            Apply("nwCell");
        if (isFirstRow && isLastColumn)
            Apply("neCell");
        if (isLastRow && isFirstColumn)
            Apply("swCell");
        if (isLastRow && isLastColumn)
            Apply("seCell");

        // An explicit "auto"/cleared fill is carried as an empty string through the cascade.
        return result.FillHex is { Length: 0 } ? result with { FillHex = null } : result;
    }

    private static ImportedTableStyleCellFormat Overlay(
        ImportedTableStyleCellFormat inherited,
        ImportedTableStyleCellFormat over) =>
        new(
            over.FillHex ?? inherited.FillHex,
            over.Bold ?? inherited.Bold,
            over.Italic ?? inherited.Italic,
            over.ColorHex ?? inherited.ColorHex);

    private static ParsedStyle Parse(DocumentStyle style)
    {
        var conditions = new Dictionary<string, ImportedTableStyleCellFormat>(StringComparer.Ordinal);
        XElement root;
        try
        {
            root = XElement.Parse(style.PreservedTableStyleXml ?? "<none/>");
        }
        catch (System.Xml.XmlException)
        {
            return new ParsedStyle(conditions, null, null, null);
        }

        var tblPr = root.Element(W + "tblPr");
        var whole = ReadFormat(root.Element(W + "tcPr"), root.Element(W + "rPr"));
        if (whole != ImportedTableStyleCellFormat.Empty)
            conditions["wholeTable"] = whole;

        foreach (var conditional in root.Elements(W + "tblStylePr"))
        {
            var type = NormalizeConditionType(conditional.Attribute(W + "type")?.Value);
            if (type is null)
                continue;
            var format = ReadFormat(conditional.Element(W + "tcPr"), conditional.Element(W + "rPr"));
            conditions[type] = conditions.TryGetValue(type, out var existing)
                ? Overlay(existing, format)
                : format;
        }

        var borderToken = tblPr?.Element(W + "tblBorders")?.Elements()
            .FirstOrDefault(edge => edge.Attribute(W + "val")?.Value is not (null or "nil" or "none"))
            ?.Attribute(W + "color")?.Value;
        if (borderToken is null && tblPr?.Element(W + "tblBorders")?.Elements()
                .Any(edge => edge.Attribute(W + "val")?.Value is not (null or "nil" or "none")) == true)
        {
            borderToken = "auto";
        }

        return new ParsedStyle(
            conditions,
            ReadBandSize(tblPr?.Element(W + "tblStyleRowBandSize")),
            ReadBandSize(tblPr?.Element(W + "tblStyleColBandSize")),
            borderToken);
    }

    private static string? NormalizeConditionType(string? type) => type switch
    {
        "wholeTable" or "firstRow" or "lastRow" or "firstCol" or "lastCol"
            or "band1Vert" or "band2Vert" or "band1Horz" or "band2Horz"
            or "nwCell" or "neCell" or "swCell" or "seCell" => type,
        // Word ignores any other spelling (e.g. "band1H"), so those conditions paint nothing.
        _ => null
    };

    private static int? ReadBandSize(XElement? element) =>
        element?.Attribute(W + "val")?.Value is { } value
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)
            ? Math.Max(0, size)
            : null;

    private static ImportedTableStyleCellFormat ReadFormat(XElement? tcPr, XElement? rPr)
    {
        string? fill = null;
        if (tcPr?.Element(W + "shd") is { } shd)
        {
            var value = shd.Attribute(W + "fill")?.Value;
            var pattern = shd.Attribute(W + "val")?.Value;
            if (string.Equals(pattern, "solid", StringComparison.OrdinalIgnoreCase)
                && shd.Attribute(W + "color")?.Value is { } solidColor
                && IsHexColor(solidColor))
            {
                fill = "#" + solidColor.ToUpperInvariant();
            }
            else if (value is not null)
            {
                fill = IsHexColor(value) ? "#" + value.ToUpperInvariant() : string.Empty;
            }
        }

        string? color = null;
        if (rPr?.Element(W + "color")?.Attribute(W + "val")?.Value is { } colorValue && IsHexColor(colorValue))
            color = "#" + colorValue.ToUpperInvariant();

        return new ImportedTableStyleCellFormat(
            fill,
            ReadToggle(rPr?.Element(W + "b")),
            ReadToggle(rPr?.Element(W + "i")),
            color);
    }

    private static bool? ReadToggle(XElement? element)
    {
        if (element is null)
            return null;
        return element.Attribute(W + "val")?.Value is not ("0" or "false" or "off");
    }

    private static bool IsHexColor(string value) =>
        value.Length == 6 && value.All(Uri.IsHexDigit);

    private sealed record ParsedStyle(
        Dictionary<string, ImportedTableStyleCellFormat> Conditions,
        int? RowBandSize,
        int? ColumnBandSize,
        string? BorderColorToken);
}
