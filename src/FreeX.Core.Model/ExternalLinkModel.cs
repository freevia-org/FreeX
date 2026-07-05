using System.Xml.Linq;

namespace FreeX.Core.Model;

/// <summary>
/// Metadata for an external workbook link package part.
/// </summary>
public sealed class ExternalLinkModel
{
    public string PackagePart { get; set; } = "";
    public string? TargetUri { get; set; }
    public string? TargetMode { get; set; }

    /// <summary>
    /// Sheet names of the external workbook, in the order Excel assigns them
    /// <c>externalBook/sheetNames/sheetName</c> 0-based index positions (i.e. index 0 is the
    /// sheet <c>[Book.xlsx]Sheet1!A1</c>-style references address as workbook-index 1). Empty when
    /// the source file did not cache any sheet names (e.g. a DDE/OLE link, or a broken reference).
    /// </summary>
    public List<string> SheetNames { get; } = [];

    /// <summary>
    /// Defined names (named ranges/formulas) the external workbook exposed, from
    /// <c>externalBook/definedNames/definedName</c>. Lets formula evaluation and autocomplete
    /// resolve <c>[Book.xlsx]!MyName</c> without needing the source file open.
    /// </summary>
    public List<ExternalDefinedNameModel> DefinedNames { get; } = [];

    /// <summary>
    /// Cached cell values captured the last time the external workbook was refreshed, from
    /// <c>externalBook/sheetDataSet/sheetData</c>. One entry per referenced external sheet; used as
    /// a fallback so formulas like <c>[Book.xlsx]Sheet1!A1</c> can still show a value when the
    /// source workbook is unavailable, exactly as Excel does.
    /// </summary>
    public List<ExternalCachedSheetModel> CachedSheetData { get; } = [];

    /// <summary>
    /// Finds the 0-based <see cref="SheetNames"/> index for <paramref name="sheetName"/>
    /// (case-insensitive, matching Excel's sheet-name comparison rules), or <see langword="null"/>
    /// when the external book did not cache a sheet by that name.
    /// </summary>
    public int? TryFindSheetIndex(string sheetName)
    {
        for (var i = 0; i < SheetNames.Count; i++)
        {
            if (string.Equals(SheetNames[i], sheetName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return null;
    }

    /// <summary>
    /// Looks up the cached value for (1-based) <paramref name="row"/>/<paramref name="col"/> on the
    /// external sheet at 0-based <paramref name="sheetIndex"/>. Returns <see langword="false"/> when
    /// there is no cached sheet-data entry for that sheet, or the source file never cached that
    /// particular cell (Excel only caches cells actually referenced by a formula at last refresh).
    /// </summary>
    public bool TryGetCachedValue(int sheetIndex, uint row, uint col, out ScalarValue? value)
    {
        foreach (var sheetData in CachedSheetData)
        {
            if (sheetData.SheetId != sheetIndex)
                continue;

            return sheetData.Values.TryGetValue((row, col), out value);
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Looks up the cached value for (1-based) <paramref name="row"/>/<paramref name="col"/> on the
    /// external sheet named <paramref name="sheetName"/> (case-insensitive). Returns
    /// <see langword="false"/> when the sheet name is unknown or the cell was never cached.
    /// </summary>
    public bool TryGetCachedValue(string sheetName, uint row, uint col, out ScalarValue? value)
    {
        var sheetIndex = TryFindSheetIndex(sheetName);
        if (sheetIndex is null)
        {
            value = null;
            return false;
        }

        return TryGetCachedValue(sheetIndex.Value, row, col, out value);
    }

    /// <summary>
    /// Parses an <c>externalBook/sheetNames</c> element (ECMA-376 §18.14.9 CT_ExternalSheetNames)
    /// into a list of sheet names in <c>sheetName</c> document order (which matches the 0-based
    /// index that <c>[Book.xlsx]Sheet1!A1</c>-style references address).
    /// </summary>
    public static List<string> ParseSheetNames(XElement? sheetNames)
    {
        var result = new List<string>();
        if (sheetNames is null)
            return result;

        foreach (var sheetName in sheetNames.Elements())
        {
            if (sheetName.Name.LocalName != "sheetName")
                continue;

            result.Add(sheetName.Attribute("val")?.Value ?? "");
        }

        return result;
    }

    /// <summary>
    /// Parses an <c>externalBook/definedNames</c> element (ECMA-376 §18.14.4 CT_ExternalDefinedName)
    /// into <see cref="ExternalDefinedNameModel"/> entries.
    /// </summary>
    public static List<ExternalDefinedNameModel> ParseDefinedNames(XElement? definedNames)
    {
        var result = new List<ExternalDefinedNameModel>();
        if (definedNames is null)
            return result;

        foreach (var definedName in definedNames.Elements())
        {
            if (definedName.Name.LocalName != "definedName")
                continue;

            var name = definedName.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var model = new ExternalDefinedNameModel
            {
                Name = name,
                RefersTo = definedName.Attribute("refersTo")?.Value,
            };

            var sheetIdText = definedName.Attribute("sheetId")?.Value;
            if (sheetIdText is not null &&
                int.TryParse(sheetIdText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var sheetId))
            {
                model.SheetId = sheetId;
            }

            result.Add(model);
        }

        return result;
    }

    /// <summary>
    /// Parses an <c>externalBook/sheetDataSet</c> element (ECMA-376 §18.14.7 CT_ExternalSheetDataSet)
    /// into <see cref="ExternalCachedSheetModel"/> entries — the cached cell values Excel captured
    /// the last time the external workbook was refreshed.
    /// </summary>
    public static List<ExternalCachedSheetModel> ParseSheetDataSet(XElement? sheetDataSet)
    {
        var result = new List<ExternalCachedSheetModel>();
        if (sheetDataSet is null)
            return result;

        foreach (var sheetData in sheetDataSet.Elements())
        {
            if (sheetData.Name.LocalName != "sheetData")
                continue;

            var sheetIdText = sheetData.Attribute("sheetId")?.Value;
            if (sheetIdText is null ||
                !int.TryParse(sheetIdText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var sheetId))
            {
                continue;
            }

            var cachedSheet = new ExternalCachedSheetModel { SheetId = sheetId };
            foreach (var row in sheetData.Elements())
            {
                if (row.Name.LocalName != "row")
                    continue;

                foreach (var cell in row.Elements())
                {
                    if (cell.Name.LocalName != "cell")
                        continue;

                    var reference = cell.Attribute("r")?.Value;
                    if (string.IsNullOrWhiteSpace(reference) ||
                        !CellAddress.TryParse(reference, default, out var address))
                    {
                        continue;
                    }

                    var value = ParseCachedCellValue(cell);
                    if (value is not null)
                        cachedSheet.Values[(address.Row, address.Col)] = value;
                }
            }

            result.Add(cachedSheet);
        }

        return result;
    }

    private static ScalarValue? ParseCachedCellValue(XElement cell)
    {
        var valueText = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value;
        if (valueText is null)
            return null;

        var type = cell.Attribute("t")?.Value;
        return type switch
        {
            "str" => new TextValue(valueText),
            "b" => new BoolValue(valueText is "1" or "true" or "TRUE"),
            "e" => new ErrorValue(valueText),
            _ => double.TryParse(
                    valueText,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var number)
                ? new NumberValue(number)
                : null,
        };
    }
}

/// <summary>
/// A single defined name (named range or named formula) cached from an external workbook link's
/// <c>externalBook/definedNames/definedName</c> element.
/// </summary>
public sealed class ExternalDefinedNameModel
{
    public string Name { get; set; } = "";

    /// <summary>The name's "refers to" formula text, exactly as the external workbook stored it.</summary>
    public string? RefersTo { get; set; }

    /// <summary>
    /// 0-based index into the external book's <see cref="ExternalLinkModel.SheetNames"/> when this
    /// is a sheet-scoped name; <see langword="null"/> when the name is workbook-scoped.
    /// </summary>
    public int? SheetId { get; set; }
}

/// <summary>
/// Cached cell values for one sheet of an external workbook link, from a single
/// <c>externalBook/sheetDataSet/sheetData</c> element.
/// </summary>
public sealed class ExternalCachedSheetModel
{
    /// <summary>0-based index into the external book's <see cref="ExternalLinkModel.SheetNames"/>.</summary>
    public int SheetId { get; set; }

    /// <summary>
    /// Cached cell values keyed by 1-based (Row, Col), matching <see cref="CellAddress"/>'s
    /// row/column convention. Only cells Excel actually cached (i.e. cells with a <c>&lt;cell&gt;</c>
    /// entry in <c>sheetData</c>) are present.
    /// </summary>
    public Dictionary<(uint Row, uint Col), ScalarValue> Values { get; } = [];
}
