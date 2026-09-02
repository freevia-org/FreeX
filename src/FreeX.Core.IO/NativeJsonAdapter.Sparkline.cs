using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static SparklineModel? TryLoadSparkline(SparklineDto? sparklineDto, SheetId sheetId)
    {
        if (sparklineDto?.DataRange is null || sparklineDto.Location is null)
            return null;

        try
        {
            var dataRange = GridRange.Parse(sparklineDto.DataRange, sheetId);
            var location  = CellAddress.Parse(sparklineDto.Location, sheetId);
            if (dataRange.Start.Sheet != sheetId || dataRange.End.Sheet != sheetId || location.Sheet != sheetId)
                return null;
            if (!Enum.IsDefined(sparklineDto.Kind))
                return null;

            // r198: the date axis is a per-group setting; drop it rather than the whole sparkline
            // if it names a range on another sheet.
            var dateAxisRange = sparklineDto.DateAxisRange is null
                ? (GridRange?)null
                : GridRange.Parse(sparklineDto.DateAxisRange, sheetId) is var parsedDateAxis
                  && parsedDateAxis.Start.Sheet == sheetId && parsedDateAxis.End.Sheet == sheetId
                    ? parsedDateAxis
                    : null;

            return new SparklineModel
            {
                DataRange           = dataRange,
                Location            = location,
                Kind                = sparklineDto.Kind,
                GroupId             = sparklineDto.GroupId,
                ShowMarkers         = sparklineDto.ShowMarkers,
                ShowHighPoint       = sparklineDto.ShowHighPoint,
                ShowLowPoint        = sparklineDto.ShowLowPoint,
                ShowFirstPoint      = sparklineDto.ShowFirstPoint,
                ShowLastPoint       = sparklineDto.ShowLastPoint,
                ShowNegativePoints  = sparklineDto.ShowNegativePoints,
                ShowAxis            = sparklineDto.ShowAxis,
                DisplayHidden       = sparklineDto.DisplayHidden,
                RightToLeft         = sparklineDto.RightToLeft,
                SeriesColor         = ParseDtoColor(sparklineDto.SeriesColor),
                NegativeColor       = ParseDtoColor(sparklineDto.NegativeColor),
                AxisColor           = ParseDtoColor(sparklineDto.AxisColor),
                MarkersColor        = ParseDtoColor(sparklineDto.MarkersColor),
                HighPointColor      = ParseDtoColor(sparklineDto.HighPointColor),
                LowPointColor       = ParseDtoColor(sparklineDto.LowPointColor),
                FirstPointColor     = ParseDtoColor(sparklineDto.FirstPointColor),
                LastPointColor      = ParseDtoColor(sparklineDto.LastPointColor),
                LineWeight          = sparklineDto.LineWeight,
                MinAxisType         = sparklineDto.MinAxisType,
                MaxAxisType         = sparklineDto.MaxAxisType,
                ManualMin           = sparklineDto.ManualMin,
                ManualMax           = sparklineDto.ManualMax,
                DisplayEmptyCellsAs = sparklineDto.DisplayEmptyCellsAs,
                DateAxisRange       = dateAxisRange,
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool IsSparklineOnSheet(SparklineModel sparkline, SheetId sheetId) =>
        sparkline.DataRange.Start.Sheet == sheetId &&
        sparkline.DataRange.End.Sheet   == sheetId &&
        sparkline.Location.Sheet        == sheetId;

    private static SparklineDto ToSparklineDto(SparklineModel sparkline) => new()
    {
        DataRange           = sparkline.DataRange.ToString(),
        Location            = sparkline.Location.ToA1(),
        Kind                = sparkline.Kind,
        GroupId             = sparkline.GroupId,
        ShowMarkers         = sparkline.ShowMarkers,
        ShowHighPoint       = sparkline.ShowHighPoint,
        ShowLowPoint        = sparkline.ShowLowPoint,
        ShowFirstPoint      = sparkline.ShowFirstPoint,
        ShowLastPoint       = sparkline.ShowLastPoint,
        ShowNegativePoints  = sparkline.ShowNegativePoints,
        ShowAxis            = sparkline.ShowAxis,
        DisplayHidden       = sparkline.DisplayHidden,
        RightToLeft         = sparkline.RightToLeft,
        SeriesColor         = FormatDtoColor(sparkline.SeriesColor),
        NegativeColor       = FormatDtoColor(sparkline.NegativeColor),
        AxisColor           = FormatDtoColor(sparkline.AxisColor),
        MarkersColor        = FormatDtoColor(sparkline.MarkersColor),
        HighPointColor      = FormatDtoColor(sparkline.HighPointColor),
        LowPointColor       = FormatDtoColor(sparkline.LowPointColor),
        FirstPointColor     = FormatDtoColor(sparkline.FirstPointColor),
        LastPointColor      = FormatDtoColor(sparkline.LastPointColor),
        LineWeight          = sparkline.LineWeight,
        MinAxisType         = sparkline.MinAxisType,
        MaxAxisType         = sparkline.MaxAxisType,
        ManualMin           = sparkline.ManualMin,
        ManualMax           = sparkline.ManualMax,
        DisplayEmptyCellsAs = sparkline.DisplayEmptyCellsAs,
        DateAxisRange       = sparkline.DateAxisRange?.ToString(),
    };

    // ── Color serialization helpers ────────────────────────────────────────────

    /// <summary>Serialise a CellColor to a 6-char uppercase hex string (RRGGBB), or null.</summary>
    private static string? FormatDtoColor(CellColor? color) =>
        color.HasValue
            ? $"{color.Value.R:X2}{color.Value.G:X2}{color.Value.B:X2}"
            : null;

    /// <summary>Parse a 6-char RRGGBB hex string back to a CellColor, returning null on failure.</summary>
    private static CellColor? ParseDtoColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Length < 6)
            return null;
        hex = hex.TrimStart('#');
        if (hex.Length < 6)
            return null;
        // Accept RRGGBB or AARRGGBB
        if (hex.Length == 8)
            hex = hex[2..];
        if (hex.Length != 6)
            return null;
        if (byte.TryParse(hex[0..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return new CellColor(r, g, b);
        }
        return null;
    }
}
