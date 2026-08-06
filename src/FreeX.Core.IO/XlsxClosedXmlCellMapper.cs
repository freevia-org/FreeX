using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxClosedXmlCellMapper
{
    private static readonly FieldInfo? XlCellValueNumberField =
        typeof(XLCellValue).GetField("_value", BindingFlags.Instance | BindingFlags.NonPublic);

    // OADate (1900-epoch) serial for 1904-01-01 — the day-count offset between Excel's two date
    // systems. ClosedXML always exposes/consumes true calendar DateTimes (it resolves the
    // workbook's date1904 flag internally), but our formula layer's 1904-aware functions
    // (YEAR/MONTH/DAY/EDATE/DATEDIF/... — see BuiltInFunctions.DateTime.cs / ExcelDateSystem)
    // interpret a stored serial as day-count-since-1904-01-01 when Workbook.Uses1904DateSystem is
    // true. So a cell's stored serial must agree with that convention: for a 1904-system workbook,
    // convert the true DateTime to a 1904-epoch-relative serial here (not the default 1900-epoch
    // OADate), and do the mirror-image conversion in MapValueInverse.
    private const double Date1904EpochOADate = 1462;

    // Excel's documented largest/smallest representable cell number (±9.99999999999999E+307;
    // see Microsoft's "Excel specifications and limits"). A finite double whose magnitude exceeds this
    // is not a value Excel itself could ever hold in a numeric cell -- and, separately, ClosedXML's own
    // numeric writer only round-trips ~15 significant digits, so a value this close to the true
    // double.MaxValue/MinValue boundary (e.g. double.MaxValue itself, serialized as
    // "1.79769313486232E+308") re-parses on load as +/-Infinity, which ClosedXML's XLCellValue
    // constructor then rejects outright. Gating MapValueInverse's DateTimeValue-fallback numeric path
    // on this bound keeps the fix scoped to genuinely Excel-representable out-of-range date serials
    // (e.g. a Paste-Special-Add result like DateTimeValue(10045306)) while still falling back to the
    // safe string form for values Excel could never represent as a number anyway.
    private const double MaxExcelRepresentableNumber = 9.99999999999999E+307;

    public static ScalarValue MapValue(IXLCell xlCell, bool uses1904DateSystem = false)
    {
        if (xlCell.Value.IsDateTime)
        {
            try { return MapDateTimeValue(xlCell.GetDateTime(), uses1904DateSystem); }
            catch (ArgumentException)
            {
                return TryGetUnifiedNumber(xlCell.Value, out var serial)
                    ? new NumberValue(serial)
                    : ErrorValue.Num;
            }
        }

        return MapValue(xlCell.Value, uses1904DateSystem);
    }

    public static ScalarValue MapFormulaValue(IXLCell xlCell, bool uses1904DateSystem = false)
    {
        ScalarValue value;
        try
        {
            value = MapValue(xlCell, uses1904DateSystem);
        }
        catch (NotImplementedException ex) when (ShouldUseCachedExternalFormulaValue(xlCell, ex))
        {
            value = MapValue(xlCell.CachedValue, uses1904DateSystem);
        }

        // ClosedXML resolves the OOXML _xHHHH_ escaping when reading shared strings, but NOT when reading
        // the cached <v> of a string-valued formula (t="str"). Excel and ClosedXML write characters that
        // cannot be emitted literally there — notably astral-plane characters such as emoji, one _xHHHH_
        // per UTF-16 code unit — so without this the literal escape leaks into the cell value on a full
        // rebuild (e.g. "🎉" surfaces as "_xD83C__xDF89_"). Decode here, scoped to the formula-cached path,
        // so an already-decoded shared string is never decoded a second time.
        return value is TextValue text
            ? new TextValue(DecodeXmlEscapedText(text.Value))
            : value;
    }

    // ClosedXML's internal placeholder text for a What-If Data Table cell (<f t="dataTable" .../>)
    // is built from a broken interpolated-string template (XLCellFormula.DataTable1D/DataTable2D:
    // $"{{TABLE({arg2},{arg}}}" -- missing the ')' before the final '}'), so IXLCell.FormulaA1 for
    // such a cell comes back syntactically invalid, e.g. "{TABLE(C1,B1}" (unbalanced brace, no
    // closing paren, no leading '='). Recognize that exact malformed shape and repair it to a
    // well-formed "TABLE(arg1,arg2)" string instead of ever storing/re-emitting the broken text
    // (see R86-io-shared-array-formula-5-1). No real Excel formula can ever take this shape (a
    // literal curly brace only appears as ClosedXML's un-escaped placeholder wrapper here -- a
    // genuine array formula's FormulaA1 is never wrapped in braces), so this is an unambiguous,
    // safe signature to match.
    private static readonly Regex DataTableFormulaTextPattern =
        new(@"^\{TABLE\(([^,{}]*),([^,{}]*)\}$", RegexOptions.Compiled);

    public static string NormalizeFormulaText(string formulaText)
    {
        var normalized = formulaText.StartsWith("=", StringComparison.Ordinal)
            ? formulaText[1..]
            : formulaText;

        normalized = normalized
            .Replace("_xlfn.", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_xlws.", "", StringComparison.OrdinalIgnoreCase);

        var dataTableMatch = DataTableFormulaTextPattern.Match(normalized);
        if (dataTableMatch.Success)
        {
            return $"TABLE({dataTableMatch.Groups[1].Value},{dataTableMatch.Groups[2].Value})";
        }

        return normalized;
    }

    private static bool ShouldUseCachedExternalFormulaValue(IXLCell xlCell, NotImplementedException ex) =>
        xlCell.FormulaA1.Contains('[', StringComparison.Ordinal) ||
        ex.Message.Contains("References from other files", StringComparison.OrdinalIgnoreCase);

    public static ScalarValue MapValue(XLCellValue xlValue, bool uses1904DateSystem = false)
    {
        if (xlValue.IsBlank) return BlankValue.Instance;
        if (xlValue.IsNumber) return new NumberValue(xlValue.GetNumber());
        if (xlValue.IsText) return new TextValue(DecodeUnresolvedXmlHexEscapes(xlValue.GetText()));
        if (xlValue.IsBoolean) return new BoolValue(xlValue.GetBoolean());
        if (xlValue.IsDateTime)
        {
            try { return MapDateTimeValue(xlValue.GetDateTime(), uses1904DateSystem); }
            catch (ArgumentException)
            {
                try { return new NumberValue(xlValue.GetNumber()); }
                catch { return ErrorValue.Num; }
            }
        }
        if (xlValue.IsTimeSpan)
        {
            // Excel stores times-of-day and elapsed durations as a fraction-of-a-day serial number;
            // ClosedXML surfaces a number with a time/duration format as a TimeSpan. Keep it numeric like
            // Excel (with the exact serial) instead of letting it fall through to TextValue("9:00:00").
            return TryGetUnifiedNumber(xlValue, out var serial)
                ? new NumberValue(serial)
                : new NumberValue(xlValue.GetTimeSpan().TotalDays);
        }
        if (xlValue.IsError) return MapErrorValue(xlValue.GetError());
        return new TextValue(xlValue.ToString());
    }

    private static readonly Regex XmlEscapedCodeUnitRegex =
        new("_x([0-9A-Fa-f]{4})_", RegexOptions.Compiled);

    // Reverses the OOXML _xHHHH_ escaping (one entry per UTF-16 code unit). This mirrors ClosedXML's own
    // XmlEncoder.DecodeString and is the exact inverse of the encoder that produced the cached value:
    // decoding matches left-to-right preserves genuine text, because the encoder guards a literal "_xHHHH_"
    // run by escaping its leading underscore as _x005F_. So "_x0041_" is stored as "_x005F_x0041_" and
    // decodes back to "_x0041_" ("_" + the now-unmatched "x0041_") rather than collapsing to "A". Adjacent
    // decoded surrogate halves recombine naturally into their code point in the resulting UTF-16 string.
    private static string DecodeXmlEscapedText(string text)
    {
        if (text.IndexOf("_x", StringComparison.Ordinal) < 0)
            return text;

        return XmlEscapedCodeUnitRegex.Replace(
            text,
            static match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());
    }

    public static XLCellValue MapValueInverse(ScalarValue value, bool uses1904DateSystem = false) => value switch
    {
        NumberValue n when double.IsFinite(n.Value) => n.Value,
        NumberValue n => n.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        TextValue t => t.Value,
        BoolValue b => b.Value,
        DateTimeValue dt when TryMapDateTimeValue(dt, uses1904DateSystem, out var dateTime) => dateTime,
        // TryMapDateTimeValue fails either because the serial is non-finite (NaN/Infinity -- cannot be
        // written as valid XML in any form), or because it is finite but outside DateTime.FromOADate's
        // representable range (e.g. a Paste-Special-Add result like DateTimeValue(10045306)), or because
        // it is finite but beyond even Excel's own representable number range (MaxExcelRepresentableNumber
        // -- see its comment: values this close to double.MaxValue/MinValue re-parse as +/-Infinity after
        // ClosedXML's own numeric round-trip, which would crash on reload). A date is just a number with a
        // display format in Excel/OOXML, so a merely out-of-DateTime-range-but-Excel-representable serial
        // must still round-trip as a NUMBER cell (ISNUMBER/SUM-compatible), not degrade to a TEXT cell.
        DateTimeValue dt when double.IsFinite(dt.Value) && Math.Abs(dt.Value) <= MaxExcelRepresentableNumber => dt.Value,
        DateTimeValue dt => dt.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        // #CIRCULAR! is a FreeX-only sentinel (RecalcEngine.AddCyclicCell), not a valid OOXML error
        // code at all. Real Excel never writes "#CIRCULAR!" to disk: with iterative calculation off
        // (FreeX's default, non-iterative path — the only path that ever stamps this value), Excel
        // persists a plain 0 in the cell for a non-iterative circular reference. Map to 0 here to match
        // what Excel itself would round-trip; the in-app grid/error-checking display of "#CIRCULAR!" is
        // unaffected because that reads the live ScalarValue.Circular, not this xlsx-serialization path.
        ErrorValue e when e.Code.Equals("#CIRCULAR!", StringComparison.OrdinalIgnoreCase) => 0d,
        // #SPILL!/#CALC! (and the newer Excel-365 #FIELD!/#CONNECT!/#UNKNOWN!/#BLOCKED!/
        // #GETTING_DATA codes) ARE valid OOXML error codes (Excel round-trips them verbatim), but
        // ClosedXML 0.105.0's XLError enum only defines the 7 "classic" codes (NullValue,
        // DivisionByZero, IncompatibleValue, CellReference, NameNotRecognized, NumberInvalid,
        // NoValueAvailable) — there is no XLError member that serializes as any of these, so
        // MapErrorValueInverse cannot represent them as a true error cell. Silently downgrading to
        // #N/A would swap in a different, wrong-but-valid error with no indication anything changed.
        // Preserve the exact code as visible text instead: a saved cell reading literally "#SPILL!"
        // or "#GETTING_DATA" is honest about what happened, unlike a cell that silently became "#N/A".
        ErrorValue e when e.Code.Equals("#SPILL!", StringComparison.OrdinalIgnoreCase) ||
                          e.Code.Equals("#CALC!", StringComparison.OrdinalIgnoreCase) ||
                          e.Code.Equals("#FIELD!", StringComparison.OrdinalIgnoreCase) ||
                          e.Code.Equals("#CONNECT!", StringComparison.OrdinalIgnoreCase) ||
                          e.Code.Equals("#UNKNOWN!", StringComparison.OrdinalIgnoreCase) ||
                          e.Code.Equals("#BLOCKED!", StringComparison.OrdinalIgnoreCase) ||
                          e.Code.Equals("#GETTING_DATA", StringComparison.OrdinalIgnoreCase) => e.Code,
        ErrorValue e => MapErrorValueInverse(e),
        _ => Blank.Value
    };

    // Converts a true calendar DateTime (as ClosedXML surfaces it, already correcting for the
    // workbook's date1904 flag) into the internal ScalarValue serial. The internal convention must
    // match how the 1904-aware date functions interpret a stored serial: Excel 1900 serial when
    // Uses1904DateSystem is false, 1904-epoch-relative (day-count since 1904-01-01) when true.
    //
    // The 1900 branch must go through DateTimeValue.FromDateTime, NOT a bare ToOADate(): ClosedXML
    // hands back the TRUE Excel calendar date for an early-1900 serial (stored serial 15 surfaces as
    // 1900-01-15), and OADate places every date in 1900-01-01..1900-02-28 one day later than its
    // Excel serial — so ToOADate() here loaded an Excel-authored 1/15/1900 cell as serial 16, which
    // then rendered and computed as 1/16/1900. The 1904 branch needs no such correction: that
    // calendar has no phantom leap day, and ToOADate() - 1462 is exactly (date - 1904-01-01).
    private static ScalarValue MapDateTimeValue(DateTime dateTime, bool uses1904DateSystem) =>
        uses1904DateSystem
            ? new DateTimeValue(dateTime.ToOADate() - Date1904EpochOADate)
            : DateTimeValue.FromDateTime(dateTime);

    private static bool TryMapDateTimeValue(DateTimeValue value, bool uses1904DateSystem, out DateTime dateTime)
    {
        dateTime = default;
        if (!double.IsFinite(value.Value))
            return false;

        try
        {
            // Mirror image of MapDateTimeValue, so the serial ClosedXML writes back is the one the
            // model holds (see that method's note on the 1900 phantom-leap-day offset).
            dateTime = uses1904DateSystem
                ? DateTime.FromOADate(value.Value + Date1904EpochOADate)
                : value.ToDateTime();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static CellStyle MapStyle(IXLStyle xlStyle, WorkbookTheme theme) =>
        MapStyle(xlStyle, theme, DefaultIndexedColors);

    public static CellStyle MapStyle(IXLStyle xlStyle, WorkbookTheme theme, WorkbookIndexedColorPalette indexedColors)
    {
        return new CellStyle
        {
            FontName = xlStyle.Font.FontName,
            FontSize = IsSupportedFontSize(xlStyle.Font.FontSize)
                ? xlStyle.Font.FontSize
                : CellStyle.Default.FontSize,
            Bold = xlStyle.Font.Bold,
            Italic = xlStyle.Font.Italic,
            Underline = xlStyle.Font.Underline != XLFontUnderlineValues.None,
            Strikethrough = xlStyle.Font.Strikethrough,
            Superscript = xlStyle.Font.VerticalAlignment == XLFontVerticalTextAlignmentValues.Superscript,
            Subscript   = xlStyle.Font.VerticalAlignment == XLFontVerticalTextAlignmentValues.Subscript,
            FontColor = MapColor(xlStyle.Font.FontColor, theme, indexedColors),
            FontThemeColor = MapThemeColorReference(xlStyle.Font.FontColor),
            DoubleUnderline = xlStyle.Font.Underline is XLFontUnderlineValues.Double or XLFontUnderlineValues.DoubleAccounting,
            FontScheme = xlStyle.Font.FontScheme switch
            {
                XLFontScheme.Minor => CellFontScheme.Minor,
                XLFontScheme.Major => CellFontScheme.Major,
                _ => CellFontScheme.None,
            },
            // Raw OOXML charset/family codes — a direct cast round-trips faithfully since the
            // underlying enum values match the raw numeric codes (mirrors the rich-text-run
            // charset/family handling in XlsxFileAdapter.Save.cs). A font that never specified
            // either reads back ClosedXML's own "unset" sentinels (Default=1 / Swiss=2), which
            // matches CellStyle.Default so ApplyStyle emits neither attribute on save.
            Charset = (int)xlStyle.Font.FontCharSet,
            FontFamily = (int)xlStyle.Font.FontFamilyNumbering,
            FillColor = xlStyle.Fill.PatternType != XLFillPatternValues.None
                ? (CellColor?)MapColor(xlStyle.Fill.BackgroundColor, theme, indexedColors)
                : null,
            FillThemeColor = xlStyle.Fill.PatternType != XLFillPatternValues.None
                ? MapThemeColorReference(xlStyle.Fill.BackgroundColor)
                : null,
            FillPatternStyle = MapFillPatternStyle(xlStyle.Fill.PatternType),
            FillPatternColor = xlStyle.Fill.PatternType is XLFillPatternValues.None or XLFillPatternValues.Solid
                ? null
                : MapColor(xlStyle.Fill.PatternColor, theme, indexedColors),
            FillPatternThemeColor = xlStyle.Fill.PatternType is XLFillPatternValues.None or XLFillPatternValues.Solid
                ? null
                : MapThemeColorReference(xlStyle.Fill.PatternColor),
            BorderTop = MapBorder(xlStyle.Border.TopBorder, xlStyle.Border.TopBorderColor, theme, indexedColors),
            BorderRight = MapBorder(xlStyle.Border.RightBorder, xlStyle.Border.RightBorderColor, theme, indexedColors),
            BorderBottom = MapBorder(xlStyle.Border.BottomBorder, xlStyle.Border.BottomBorderColor, theme, indexedColors),
            BorderLeft = MapBorder(xlStyle.Border.LeftBorder, xlStyle.Border.LeftBorderColor, theme, indexedColors),
            BorderDiagonalDown = xlStyle.Border.DiagonalDown
                ? MapBorder(xlStyle.Border.DiagonalBorder, xlStyle.Border.DiagonalBorderColor, theme, indexedColors)
                : default,
            BorderDiagonalUp = xlStyle.Border.DiagonalUp
                ? MapBorder(xlStyle.Border.DiagonalBorder, xlStyle.Border.DiagonalBorderColor, theme, indexedColors)
                : default,
            NumberFormat = MapNumberFormat(xlStyle.NumberFormat),
            HorizontalAlignment = xlStyle.Alignment.Horizontal switch
            {
                XLAlignmentHorizontalValues.General => HorizontalAlignment.General,
                XLAlignmentHorizontalValues.Left => HorizontalAlignment.Left,
                XLAlignmentHorizontalValues.Center => HorizontalAlignment.Center,
                XLAlignmentHorizontalValues.Right => HorizontalAlignment.Right,
                XLAlignmentHorizontalValues.Justify => HorizontalAlignment.Justify,
                XLAlignmentHorizontalValues.Distributed => HorizontalAlignment.Distributed,
                XLAlignmentHorizontalValues.Fill => HorizontalAlignment.Fill,
                // FreeX.Core.Model.HorizontalAlignment has no dedicated "Center Across Selection"
                // member, so mapping centerContinuous straight to General would silently flip the
                // text from centered to flush-left. Map it to plain Center instead: it keeps the
                // text visually centered in its own cell (the closest available approximation to
                // Excel's cross-cell centering) rather than discarding the alignment entirely.
                XLAlignmentHorizontalValues.CenterContinuous => HorizontalAlignment.Center,
                _ => HorizontalAlignment.General,
            },
            VerticalAlignment = xlStyle.Alignment.Vertical switch
            {
                XLAlignmentVerticalValues.Top => VerticalAlignment.Top,
                XLAlignmentVerticalValues.Center => VerticalAlignment.Center,
                XLAlignmentVerticalValues.Bottom => VerticalAlignment.Bottom,
                XLAlignmentVerticalValues.Justify => VerticalAlignment.Justify,
                XLAlignmentVerticalValues.Distributed => VerticalAlignment.Distributed,
                _ => VerticalAlignment.Bottom,
            },
            ReadingOrder = xlStyle.Alignment.ReadingOrder switch
            {
                XLAlignmentReadingOrderValues.LeftToRight => CellReadingOrder.LeftToRight,
                XLAlignmentReadingOrderValues.RightToLeft => CellReadingOrder.RightToLeft,
                _ => CellReadingOrder.Context,
            },
            WrapText = xlStyle.Alignment.WrapText,
            ShrinkToFit = xlStyle.Alignment.ShrinkToFit,
            IndentLevel = Math.Clamp(xlStyle.Alignment.Indent, 0, 15),
            TextRotation = IsSupportedTextRotation(xlStyle.Alignment.TextRotation)
                ? xlStyle.Alignment.TextRotation
                : 0,
            Locked = xlStyle.Protection.Locked,
            Hidden = xlStyle.Protection.Hidden,
        };
    }

    public static void ApplyStyle(IXLCell xlCell, CellStyle style) =>
        ApplyStyle(xlCell.Style, style);

    /// <summary>
    /// Reads the ECMA-376 <c>xf@quotePrefix</c> flag (the leading-apostrophe forced-text marker,
    /// e.g. a part number entered as <c>'04512</c>) from a ClosedXML cell's style, for round-tripping
    /// onto <see cref="Cell.QuotePrefix"/>. This flag is per-cell/per-value (not visual formatting),
    /// so it is modelled on <see cref="Cell"/> rather than <see cref="CellStyle"/>.
    /// </summary>
    /// <remarks>
    /// Wired into the per-cell load loop (XlsxFileAdapter.cs) and the per-cell full-save loop
    /// (XlsxFileAdapter.Save.cs) so the flag flows through <see cref="Cell.QuotePrefix"/> on real
    /// load/save. The patch-save path (XlsxFileAdapter.SourcePackageSnapshot.cs) never touches
    /// cellXfs, so it already preserves quotePrefix verbatim for cells whose value doesn't change.
    /// </remarks>
    public static bool MapQuotePrefix(IXLCell xlCell) => xlCell.Style.IncludeQuotePrefix;

    /// <summary>
    /// Writes the ECMA-376 <c>xf@quotePrefix</c> flag back onto a ClosedXML cell's style from
    /// <see cref="Cell.QuotePrefix"/>. Only sets it when true, mirroring
    /// <see cref="ApplyStyle(IXLStyle, CellStyle)"/>'s convention of never touching a property that is
    /// already at its default (false) so unrelated cells are not perturbed.
    /// </summary>
    public static void ApplyQuotePrefix(IXLCell xlCell, bool quotePrefix)
    {
        if (quotePrefix)
            xlCell.Style.IncludeQuotePrefix = true;
    }

    // ClosedXML's SetHyperlink stamps its built-in Hyperlink style (theme-10 blue font + single underline)
    // onto the cell, overriding the modelled font. Re-apply the modelled font afterward, forcing every
    // property unconditionally — ApplyStyle skips values equal to the default (e.g. a black font colour),
    // which would otherwise leave the theme-10 colour and the forced underline in place.
    public static void ApplyHyperlinkFontOverride(IXLCell xlCell, CellStyle style)
    {
        var font = xlCell.Style.Font;
        font.Bold = style.Bold;
        font.Italic = style.Italic;
        font.Underline = style.Underline ? XLFontUnderlineValues.Single : XLFontUnderlineValues.None;
        font.Strikethrough = style.Strikethrough;
        if (IsSupportedFontSize(style.FontSize))
            font.FontSize = style.FontSize;
        if (!string.IsNullOrEmpty(style.FontName))
            font.FontName = style.FontName;
        font.FontColor = XLColor.FromArgb(255, style.FontColor.R, style.FontColor.G, style.FontColor.B);
    }

    public static void ApplyStyle(IXLStyle xlStyle, CellStyle style)
    {
        var def = CellStyle.Default;

        if (style.Bold != def.Bold) xlStyle.Font.Bold = style.Bold;
        if (style.Italic != def.Italic) xlStyle.Font.Italic = style.Italic;
        if (style.Underline != def.Underline || style.DoubleUnderline != def.DoubleUnderline)
            xlStyle.Font.Underline = style.DoubleUnderline
                ? XLFontUnderlineValues.Double
                : style.Underline
                    ? XLFontUnderlineValues.Single
                    : XLFontUnderlineValues.None;
        if (style.Strikethrough != def.Strikethrough)
            xlStyle.Font.Strikethrough = style.Strikethrough;
        if (style.Superscript != def.Superscript || style.Subscript != def.Subscript)
            xlStyle.Font.VerticalAlignment = style.Superscript
                ? XLFontVerticalTextAlignmentValues.Superscript
                : style.Subscript
                    ? XLFontVerticalTextAlignmentValues.Subscript
                    : XLFontVerticalTextAlignmentValues.Baseline;
        if (style.FontSize != def.FontSize && IsSupportedFontSize(style.FontSize))
            xlStyle.Font.FontSize = style.FontSize;
        if (style.FontName != def.FontName) xlStyle.Font.FontName = style.FontName;
        if (style.FontThemeColor is { } fontThemeColor)
            xlStyle.Font.FontColor = ToXLColor(fontThemeColor);
        else if (style.FontColor != def.FontColor)
            xlStyle.Font.FontColor = XLColor.FromArgb(255, style.FontColor.R, style.FontColor.G, style.FontColor.B);
        if (style.FontScheme != def.FontScheme)
            xlStyle.Font.FontScheme = style.FontScheme switch
            {
                CellFontScheme.Minor => XLFontScheme.Minor,
                CellFontScheme.Major => XLFontScheme.Major,
                _ => XLFontScheme.None,
            };
        // Raw OOXML charset/family codes — only set when they differ from CellStyle.Default's
        // sentinels (which are themselves ClosedXML's own "unset" values), so a plain font that
        // never carried either attribute keeps emitting neither on save (see MapStyle).
        if (style.Charset != def.Charset)
            xlStyle.Font.FontCharSet = (XLFontCharSet)style.Charset;
        if (style.FontFamily != def.FontFamily)
            xlStyle.Font.FontFamilyNumbering = (XLFontFamilyNumberingValues)style.FontFamily;

        if (style.GradientFill is { } gradientFill)
        {
            // ClosedXML has no gradient-fill API of its own: the real gradient XML is restored
            // after the ClosedXML save by XlsxStylesheetMetadataPreserver.MergeStylesheetGradientFills,
            // which matches a source xf to its rebuilt counterpart by a signature of font/border/
            // numFmt/alignment/protection (fillId is deliberately excluded from that signature).
            // If we leave the fill untouched here, a cell whose ONLY formatting is the gradient is
            // indistinguishable from CellStyle.Default, so ClosedXML collapses it into the shared
            // default style and omits its <c> element entirely — the cell (and its restorable xf
            // slot) vanishes before the preserver ever runs. Stamp a solid placeholder (using the
            // gradient's first stop color, perturbed by ComputeGradientPlaceholderColor so two
            // distinct gradients that merely share a first stop don't collide) so the cell keeps
            // its own distinct, restorable cellXf; the preserver overwrites this placeholder with
            // the real gradient content afterward.
            var placeholderColor = ComputeGradientPlaceholderColor(gradientFill);
            xlStyle.Fill.PatternType = XLFillPatternValues.Solid;
            xlStyle.Fill.BackgroundColor = XLColor.FromArgb(255, placeholderColor.R, placeholderColor.G, placeholderColor.B);
        }
        else if (style.FillPatternStyle != CellFillPatternStyle.None)
        {
            xlStyle.Fill.PatternType = MapFillPatternStyleInverse(style.FillPatternStyle);
            if (style.FillThemeColor is { } fillThemeColor)
                xlStyle.Fill.BackgroundColor = ToXLColor(fillThemeColor);
            else if (style.FillColor.HasValue)
                xlStyle.Fill.BackgroundColor = XLColor.FromArgb(255, style.FillColor.Value.R, style.FillColor.Value.G, style.FillColor.Value.B);
            if (style.FillPatternThemeColor is { } fillPatternThemeColor)
                xlStyle.Fill.PatternColor = ToXLColor(fillPatternThemeColor);
            else if (style.FillPatternColor.HasValue)
                xlStyle.Fill.PatternColor = XLColor.FromArgb(255, style.FillPatternColor.Value.R, style.FillPatternColor.Value.G, style.FillPatternColor.Value.B);
        }
        else if (style.FillThemeColor is { } solidFillThemeColor)
        {
            xlStyle.Fill.PatternType = XLFillPatternValues.Solid;
            xlStyle.Fill.BackgroundColor = ToXLColor(solidFillThemeColor);
        }
        else if (style.FillColor.HasValue)
        {
            xlStyle.Fill.PatternType = XLFillPatternValues.Solid;
            xlStyle.Fill.BackgroundColor = XLColor.FromArgb(255, style.FillColor.Value.R, style.FillColor.Value.G, style.FillColor.Value.B);
        }

        if (style.BorderTop.Style != BorderStyle.None)
        {
            xlStyle.Border.TopBorder = MapBorderStyleInverse(style.BorderTop.Style);
            xlStyle.Border.TopBorderColor = ToXLBorderColor(style.BorderTop);
        }
        if (style.BorderRight.Style != BorderStyle.None)
        {
            xlStyle.Border.RightBorder = MapBorderStyleInverse(style.BorderRight.Style);
            xlStyle.Border.RightBorderColor = ToXLBorderColor(style.BorderRight);
        }
        if (style.BorderBottom.Style != BorderStyle.None)
        {
            xlStyle.Border.BottomBorder = MapBorderStyleInverse(style.BorderBottom.Style);
            xlStyle.Border.BottomBorderColor = ToXLBorderColor(style.BorderBottom);
        }
        if (style.BorderLeft.Style != BorderStyle.None)
        {
            xlStyle.Border.LeftBorder = MapBorderStyleInverse(style.BorderLeft.Style);
            xlStyle.Border.LeftBorderColor = ToXLBorderColor(style.BorderLeft);
        }
        if (style.BorderDiagonalDown.Style != BorderStyle.None || style.BorderDiagonalUp.Style != BorderStyle.None)
        {
            // OOXML: diagonal border style/color is shared; diagonalDown/diagonalUp flags select which lines to draw.
            var diagBorder = style.BorderDiagonalDown.Style != BorderStyle.None ? style.BorderDiagonalDown : style.BorderDiagonalUp;
            xlStyle.Border.DiagonalBorder = MapBorderStyleInverse(diagBorder.Style);
            xlStyle.Border.DiagonalBorderColor = ToXLBorderColor(diagBorder);
            xlStyle.Border.DiagonalDown = style.BorderDiagonalDown.Style != BorderStyle.None;
            xlStyle.Border.DiagonalUp = style.BorderDiagonalUp.Style != BorderStyle.None;
        }

        if (style.HorizontalAlignment != def.HorizontalAlignment)
            xlStyle.Alignment.Horizontal = style.HorizontalAlignment switch
            {
                HorizontalAlignment.Left => XLAlignmentHorizontalValues.Left,
                HorizontalAlignment.Center => XLAlignmentHorizontalValues.Center,
                HorizontalAlignment.Right => XLAlignmentHorizontalValues.Right,
                HorizontalAlignment.Justify => XLAlignmentHorizontalValues.Justify,
                HorizontalAlignment.Distributed => XLAlignmentHorizontalValues.Distributed,
                HorizontalAlignment.Fill => XLAlignmentHorizontalValues.Fill,
                _ => XLAlignmentHorizontalValues.General,
            };

        if (style.VerticalAlignment != def.VerticalAlignment)
            xlStyle.Alignment.Vertical = style.VerticalAlignment switch
            {
                VerticalAlignment.Top => XLAlignmentVerticalValues.Top,
                VerticalAlignment.Center => XLAlignmentVerticalValues.Center,
                VerticalAlignment.Justify => XLAlignmentVerticalValues.Justify,
                VerticalAlignment.Distributed => XLAlignmentVerticalValues.Distributed,
                _ => XLAlignmentVerticalValues.Bottom,
            };

        if (style.ReadingOrder != def.ReadingOrder)
            xlStyle.Alignment.ReadingOrder = style.ReadingOrder switch
            {
                CellReadingOrder.LeftToRight => XLAlignmentReadingOrderValues.LeftToRight,
                CellReadingOrder.RightToLeft => XLAlignmentReadingOrderValues.RightToLeft,
                _ => XLAlignmentReadingOrderValues.ContextDependent,
            };

        if (style.WrapText != def.WrapText)
            xlStyle.Alignment.WrapText = style.WrapText;

        if (style.ShrinkToFit != def.ShrinkToFit)
            xlStyle.Alignment.ShrinkToFit = style.ShrinkToFit;

        if (style.IndentLevel != def.IndentLevel)
            xlStyle.Alignment.Indent = Math.Clamp(style.IndentLevel, 0, 15);

        if (style.TextRotation != def.TextRotation && IsSupportedTextRotation(style.TextRotation))
            xlStyle.Alignment.TextRotation = style.TextRotation;

        if (style.NumberFormat != def.NumberFormat)
        {
            if (BuiltInNumberFormatCatalog.TryResolveNumberFormatIdForCode(style.NumberFormat, out var builtInNumberFormatId) &&
                builtInNumberFormatId is { } resolvedNumberFormatId)
            {
                // Prefer the implicit builtin numFmtId (matching real Excel/ClosedXML behavior) over an
                // explicit <numFmt> entry so common ribbon actions (Comma Style, Accounting, Percentage,
                // Fraction, Scientific, Text) round-trip as their native Format Cells category instead of
                // "Custom".
                xlStyle.NumberFormat.NumberFormatId = resolvedNumberFormatId;
            }
            else
            {
                xlStyle.NumberFormat.Format = style.NumberFormat;
            }
        }

        if (style.Locked != def.Locked)
            xlStyle.Protection.Locked = style.Locked;

        if (style.Hidden != def.Hidden)
            xlStyle.Protection.Hidden = style.Hidden;
    }

    private static bool TryGetUnifiedNumber(XLCellValue value, out double number)
    {
        number = 0;
        if (!value.IsUnifiedNumber || XlCellValueNumberField is null)
            return false;

        if (XlCellValueNumberField.GetValue(value) is double raw)
        {
            number = raw;
            return true;
        }

        return false;
    }

    private static string MapNumberFormat(IXLNumberFormat numberFormat)
    {
        if (!string.IsNullOrEmpty(numberFormat.Format))
            return numberFormat.Format;

        return BuiltInNumberFormatCatalog.TryResolveFormatCode(numberFormat.NumberFormatId, out var builtInFormat) &&
               !string.IsNullOrEmpty(builtInFormat)
            ? builtInFormat
            : CellStyle.Default.NumberFormat;
    }

    private static ErrorValue MapErrorValue(XLError error) => error switch
    {
        XLError.NullValue => ErrorValue.Null,
        XLError.DivisionByZero => ErrorValue.DivByZero,
        XLError.IncompatibleValue => ErrorValue.Value,
        XLError.CellReference => ErrorValue.Ref,
        XLError.NameNotRecognized => ErrorValue.Name,
        XLError.NumberInvalid => ErrorValue.Num,
        XLError.NoValueAvailable => ErrorValue.NA,
        _ => new ErrorValue(error.ToString())
    };

    private static XLError MapErrorValueInverse(ErrorValue error) => error.Code.ToUpperInvariant() switch
    {
        "#NULL!" => XLError.NullValue,
        "#DIV/0!" => XLError.DivisionByZero,
        "#VALUE!" => XLError.IncompatibleValue,
        "#REF!" => XLError.CellReference,
        "#NAME?" => XLError.NameNotRecognized,
        "#NUM!" => XLError.NumberInvalid,
        "#N/A" => XLError.NoValueAvailable,
        _ => XLError.NoValueAvailable
    };

    // Default Excel indexed-color palette (no workbook-authored overrides), used by call sites that
    // don't have a resolved WorkbookIndexedColorPalette in scope.
    private static readonly WorkbookIndexedColorPalette DefaultIndexedColors = new();

    public static CellColor MapColor(XLColor xlColor, WorkbookTheme theme) =>
        MapColor(xlColor, theme, DefaultIndexedColors);

    public static CellColor MapColor(XLColor xlColor, WorkbookTheme theme, WorkbookIndexedColorPalette indexedColors)
    {
        if (xlColor.ColorType == XLColorType.Theme)
            return theme.ResolveColor(ToWorkbookThemeColorSlot(xlColor.ThemeColor), xlColor.ThemeTint);

        if (xlColor.ColorType == XLColorType.Indexed && indexedColors.TryResolveColor(xlColor.Indexed + 1, out var indexedColor))
            return indexedColor;

        if (!TryMapConcreteColor(xlColor, out var color))
            return CellColor.Black;

        return color;
    }

    private static bool TryMapConcreteColor(XLColor xlColor, out CellColor color)
    {
        color = default;
        if (xlColor.ColorType != XLColorType.Color || !xlColor.HasValue)
            return false;

        // Avoid XLColor.Color here; it exposes drawing types in portable core code.
        var rgb = xlColor.ToString().Trim().TrimStart('#');
        if (rgb.Length == 8)
            rgb = rgb[2..];

        if (rgb.Length != 6 ||
            !byte.TryParse(rgb[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(rgb[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(rgb[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = new CellColor(r, g, b);
        return true;
    }

    // Preserves a cell/font/fill color's theme link (slot + tint) alongside the baked RGB fallback in
    // MapColor, so ApplyStyle can re-emit <color theme="…" tint="…"/> instead of a literal rgb on save
    // (see R19-theme-extlst-1: without this a theme-linked cell color loses its theme link on round-trip
    // and never re-colors when the workbook theme changes).
    internal static WorkbookThemeColorReference? MapThemeColorReference(XLColor xlColor) =>
        xlColor.ColorType == XLColorType.Theme
            ? new WorkbookThemeColorReference(ToWorkbookThemeColorSlot(xlColor.ThemeColor), xlColor.ThemeTint)
            : null;

    // Inverse of ToXLColor: converts a resolved theme-color reference back into a ClosedXML XLColor
    // that serializes as <color theme="…" tint="…"/>, mirroring the tint-omission convention already
    // used by MapRunColorToXLColor (XlsxFileAdapter.Save.cs) for rich-text run colors.
    internal static XLColor ToXLColor(WorkbookThemeColorReference themeColor) =>
        Math.Abs(themeColor.Tint) > 0.000001
            ? XLColor.FromTheme(ToXLThemeColor(themeColor.Slot), themeColor.Tint)
            : XLColor.FromTheme(ToXLThemeColor(themeColor.Slot));

    // Preserves a border edge's theme link on save, mirroring the FontThemeColor/FillThemeColor
    // handling above (see R80-border-theme-color-1: without this every themed cell border was
    // flattened to a literal <color rgb="…"/> on save, even when untouched from an Excel-authored
    // theme-colored border, unlike font/fill colors which already re-emit <color theme="…"/>).
    private static XLColor ToXLBorderColor(CellBorder border) =>
        border.ThemeColor is { } borderThemeColor
            ? ToXLColor(borderThemeColor)
            : XLColor.FromArgb(255, border.Color.R, border.Color.G, border.Color.B);

    private static XLThemeColor ToXLThemeColor(WorkbookThemeColorSlot slot) => slot switch
    {
        WorkbookThemeColorSlot.Dark1 => XLThemeColor.Text1,
        WorkbookThemeColorSlot.Light1 => XLThemeColor.Background1,
        WorkbookThemeColorSlot.Dark2 => XLThemeColor.Text2,
        WorkbookThemeColorSlot.Light2 => XLThemeColor.Background2,
        WorkbookThemeColorSlot.Accent1 => XLThemeColor.Accent1,
        WorkbookThemeColorSlot.Accent2 => XLThemeColor.Accent2,
        WorkbookThemeColorSlot.Accent3 => XLThemeColor.Accent3,
        WorkbookThemeColorSlot.Accent4 => XLThemeColor.Accent4,
        WorkbookThemeColorSlot.Accent5 => XLThemeColor.Accent5,
        WorkbookThemeColorSlot.Accent6 => XLThemeColor.Accent6,
        WorkbookThemeColorSlot.Hyperlink => XLThemeColor.Hyperlink,
        WorkbookThemeColorSlot.FollowedHyperlink => XLThemeColor.FollowedHyperlink,
        _ => XLThemeColor.Text1,
    };

    private static WorkbookThemeColorSlot ToWorkbookThemeColorSlot(XLThemeColor themeColor) => themeColor switch
    {
        XLThemeColor.Text1 => WorkbookThemeColorSlot.Dark1,
        XLThemeColor.Background1 => WorkbookThemeColorSlot.Light1,
        XLThemeColor.Text2 => WorkbookThemeColorSlot.Dark2,
        XLThemeColor.Background2 => WorkbookThemeColorSlot.Light2,
        XLThemeColor.Accent1 => WorkbookThemeColorSlot.Accent1,
        XLThemeColor.Accent2 => WorkbookThemeColorSlot.Accent2,
        XLThemeColor.Accent3 => WorkbookThemeColorSlot.Accent3,
        XLThemeColor.Accent4 => WorkbookThemeColorSlot.Accent4,
        XLThemeColor.Accent5 => WorkbookThemeColorSlot.Accent5,
        XLThemeColor.Accent6 => WorkbookThemeColorSlot.Accent6,
        XLThemeColor.Hyperlink => WorkbookThemeColorSlot.Hyperlink,
        XLThemeColor.FollowedHyperlink => WorkbookThemeColorSlot.FollowedHyperlink,
        _ => WorkbookThemeColorSlot.Dark1
    };

    // Derives the solid placeholder colour ApplyStyle stamps for a gradient-filled cell (see the
    // GradientFill branch above). The base colour is the gradient's first stop, but its low-order
    // bits are overwritten with a hash of the gradient's FULL content (type, degree, insets, and
    // every stop) so that two structurally-DIFFERENT gradients which merely share a first stop
    // colour (e.g. white→blue and white→red) still stamp distinguishable placeholders. Without this,
    // ClosedXML's style cache would dedup the two byte-identical placeholder fills into a single
    // rebuilt <fill>, leaving XlsxStylesheetMetadataPreserver.MergeStylesheetGradientFills unable to
    // tell which cellXf should get which gradient back. The perturbation is at most +/-7 per channel
    // (imperceptible) so the placeholder still reads as the intended colour if it is ever left
    // un-restored (e.g. the genuine-solid-collision guard in the preserver). Both this method and the
    // preserver call it with the SAME CellGradientFill content, so the perturbation always agrees
    // between the write side and the restore side.
    internal static CellColor ComputeGradientPlaceholderColor(CellGradientFill gradientFill)
    {
        var baseColor = gradientFill.Stops.Count > 0 ? gradientFill.Stops[0].Color : CellColor.White;
        var hash = unchecked((uint)gradientFill.GetHashCode());
        var r = (byte)((baseColor.R & ~0b111) | (int)(hash & 0b111));
        var g = (byte)((baseColor.G & ~0b111) | (int)((hash >> 3) & 0b111));
        var b = (byte)((baseColor.B & ~0b111) | (int)((hash >> 6) & 0b111));
        return new CellColor(r, g, b);
    }

    private static CellBorder MapBorder(XLBorderStyleValues style, XLColor color, WorkbookTheme theme, WorkbookIndexedColorPalette indexedColors)
    {
        var mapped = style switch
        {
            XLBorderStyleValues.None => BorderStyle.None,
            XLBorderStyleValues.Thin => BorderStyle.Thin,
            XLBorderStyleValues.Medium => BorderStyle.Medium,
            XLBorderStyleValues.Thick => BorderStyle.Thick,
            XLBorderStyleValues.Dashed => BorderStyle.Dashed,
            XLBorderStyleValues.Dotted => BorderStyle.Dotted,
            XLBorderStyleValues.Double => BorderStyle.Double,
            XLBorderStyleValues.Hair => BorderStyle.Hair,
            XLBorderStyleValues.SlantDashDot => BorderStyle.SlantDashDot,
            XLBorderStyleValues.MediumDashed => BorderStyle.MediumDashed,
            XLBorderStyleValues.DashDot => BorderStyle.DashDot,
            XLBorderStyleValues.MediumDashDot => BorderStyle.MediumDashDot,
            XLBorderStyleValues.DashDotDot => BorderStyle.DashDotDot,
            XLBorderStyleValues.MediumDashDotDot => BorderStyle.MediumDashDotDot,
            _ => BorderStyle.None,
        };
        return new CellBorder(mapped, MapColor(color, theme, indexedColors), MapThemeColorReference(color));
    }

    private static XLBorderStyleValues MapBorderStyleInverse(BorderStyle style) => style switch
    {
        BorderStyle.Thin => XLBorderStyleValues.Thin,
        BorderStyle.Medium => XLBorderStyleValues.Medium,
        BorderStyle.Thick => XLBorderStyleValues.Thick,
        BorderStyle.Dashed => XLBorderStyleValues.Dashed,
        BorderStyle.Dotted => XLBorderStyleValues.Dotted,
        BorderStyle.Double => XLBorderStyleValues.Double,
        BorderStyle.Hair => XLBorderStyleValues.Hair,
        BorderStyle.SlantDashDot => XLBorderStyleValues.SlantDashDot,
        BorderStyle.MediumDashed => XLBorderStyleValues.MediumDashed,
        BorderStyle.DashDot => XLBorderStyleValues.DashDot,
        BorderStyle.MediumDashDot => XLBorderStyleValues.MediumDashDot,
        BorderStyle.DashDotDot => XLBorderStyleValues.DashDotDot,
        BorderStyle.MediumDashDotDot => XLBorderStyleValues.MediumDashDotDot,
        _ => XLBorderStyleValues.None,
    };

    private static CellFillPatternStyle MapFillPatternStyle(XLFillPatternValues pattern) => pattern switch
    {
        XLFillPatternValues.Solid => CellFillPatternStyle.Solid,
        XLFillPatternValues.Gray0625 => CellFillPatternStyle.Gray0625,
        XLFillPatternValues.Gray125 => CellFillPatternStyle.Gray125,
        XLFillPatternValues.LightGray => CellFillPatternStyle.LightGray,
        XLFillPatternValues.MediumGray => CellFillPatternStyle.MediumGray,
        XLFillPatternValues.DarkGray => CellFillPatternStyle.DarkGray,
        XLFillPatternValues.LightHorizontal => CellFillPatternStyle.LightHorizontal,
        XLFillPatternValues.LightVertical => CellFillPatternStyle.LightVertical,
        XLFillPatternValues.LightDown => CellFillPatternStyle.LightDown,
        XLFillPatternValues.LightUp => CellFillPatternStyle.LightUp,
        XLFillPatternValues.LightGrid => CellFillPatternStyle.LightGrid,
        XLFillPatternValues.LightTrellis => CellFillPatternStyle.LightTrellis,
        XLFillPatternValues.DarkHorizontal => CellFillPatternStyle.DarkHorizontal,
        XLFillPatternValues.DarkVertical => CellFillPatternStyle.DarkVertical,
        XLFillPatternValues.DarkDown => CellFillPatternStyle.DarkDown,
        XLFillPatternValues.DarkUp => CellFillPatternStyle.DarkUp,
        XLFillPatternValues.DarkGrid => CellFillPatternStyle.DarkGrid,
        XLFillPatternValues.DarkTrellis => CellFillPatternStyle.DarkTrellis,
        _ => CellFillPatternStyle.None,
    };

    private static XLFillPatternValues MapFillPatternStyleInverse(CellFillPatternStyle pattern) => pattern switch
    {
        CellFillPatternStyle.Solid => XLFillPatternValues.Solid,
        CellFillPatternStyle.Gray0625 => XLFillPatternValues.Gray0625,
        CellFillPatternStyle.Gray125 => XLFillPatternValues.Gray125,
        CellFillPatternStyle.LightGray => XLFillPatternValues.LightGray,
        CellFillPatternStyle.MediumGray => XLFillPatternValues.MediumGray,
        CellFillPatternStyle.DarkGray => XLFillPatternValues.DarkGray,
        CellFillPatternStyle.LightHorizontal => XLFillPatternValues.LightHorizontal,
        CellFillPatternStyle.LightVertical => XLFillPatternValues.LightVertical,
        CellFillPatternStyle.LightDown => XLFillPatternValues.LightDown,
        CellFillPatternStyle.LightUp => XLFillPatternValues.LightUp,
        CellFillPatternStyle.LightGrid => XLFillPatternValues.LightGrid,
        CellFillPatternStyle.LightTrellis => XLFillPatternValues.LightTrellis,
        CellFillPatternStyle.DarkHorizontal => XLFillPatternValues.DarkHorizontal,
        CellFillPatternStyle.DarkVertical => XLFillPatternValues.DarkVertical,
        CellFillPatternStyle.DarkDown => XLFillPatternValues.DarkDown,
        CellFillPatternStyle.DarkUp => XLFillPatternValues.DarkUp,
        CellFillPatternStyle.DarkGrid => XLFillPatternValues.DarkGrid,
        CellFillPatternStyle.DarkTrellis => XLFillPatternValues.DarkTrellis,
        _ => XLFillPatternValues.None,
    };

    private static bool IsSupportedTextRotation(int rotation) =>
        (rotation >= -90 && rotation <= 90) || rotation == 255;

    private static bool IsSupportedFontSize(double fontSize) =>
        double.IsFinite(fontSize) && fontSize > 0 && fontSize <= 409;

    /// <summary>
    /// Decodes OOXML <c>_xHHHH_</c> hex escapes that ClosedXML leaves unresolved when it reads a cached
    /// formula-string value (the <c>&lt;v&gt;</c> of a <c>t="str"</c> cell). ClosedXML's own writer escapes
    /// astral characters (emoji, etc.) as a pair of surrogate-half escapes such as
    /// <c>_xD83C__xDF89_</c> for U+1F389, but its reader only un-escapes the shared-string / inline-string
    /// path, so a full ClosedXML re-save round-trips emoji in formula results into literal escape text.
    ///
    /// The decode is scoped narrowly: it only runs when the text contains a UTF-16 surrogate-half escape
    /// (<c>_xD800_</c>–<c>_xDFFF_</c>). A lone surrogate half is never valid in a real .NET string, so this
    /// pattern is unambiguously the ClosedXML artifact and can never collide with legitimate user text that
    /// happens to contain a BMP-looking <c>_x0041_</c> literal (Excel re-escapes those on every save, so they
    /// never reach the model as literal text either way).
    /// </summary>
    internal static string DecodeUnresolvedXmlHexEscapes(string text)
    {
        if (string.IsNullOrEmpty(text) || !ContainsSurrogateHalfEscape(text))
            return text;

        var builder = new System.Text.StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (TryReadHexEscape(text, i, out var code, out var consumed) &&
                code is >= 0xD800 and <= 0xDFFF)
            {
                // High surrogate immediately followed by a low-surrogate escape -> recombine to the astral char.
                if (code <= 0xDBFF &&
                    TryReadHexEscape(text, i + consumed, out var low, out var lowConsumed) &&
                    low is >= 0xDC00 and <= 0xDFFF)
                {
                    builder.Append((char)code);
                    builder.Append((char)low);
                    i += consumed + lowConsumed;
                    continue;
                }

                // A lone surrogate-half escape: emit the surrogate code unit (matches what Excel renders).
                builder.Append((char)code);
                i += consumed;
                continue;
            }

            builder.Append(text[i]);
            i++;
        }

        return builder.ToString();
    }

    private static bool ContainsSurrogateHalfEscape(string text)
    {
        int idx = text.IndexOf("_xD", StringComparison.Ordinal);
        while (idx >= 0)
        {
            if (TryReadHexEscape(text, idx, out var code, out _) && code is >= 0xD800 and <= 0xDFFF)
                return true;
            idx = text.IndexOf("_xD", idx + 1, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>Parses a <c>_xHHHH_</c> escape at <paramref name="start"/>; HHHH is exactly 4 hex digits.</summary>
    private static bool TryReadHexEscape(string text, int start, out int code, out int consumed)
    {
        code = 0;
        consumed = 0;
        const int length = 7; // "_xHHHH_"
        if (start < 0 || start + length > text.Length)
            return false;
        if (text[start] != '_' || (text[start + 1] != 'x' && text[start + 1] != 'X') || text[start + 6] != '_')
            return false;

        int value = 0;
        for (int j = start + 2; j < start + 6; j++)
        {
            int digit = HexDigit(text[j]);
            if (digit < 0)
                return false;
            value = (value << 4) | digit;
        }

        code = value;
        consumed = length;
        return true;
    }

    private static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };
}
