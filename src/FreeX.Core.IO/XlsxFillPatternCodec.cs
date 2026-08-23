using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxFillPatternCodec
{
    public static CellFillPatternStyle FromToken(string? patternType) =>
        patternType switch
        {
            "solid" => CellFillPatternStyle.Solid,
            "gray0625" => CellFillPatternStyle.Gray0625,
            "gray125" => CellFillPatternStyle.Gray125,
            "lightGray" => CellFillPatternStyle.LightGray,
            "mediumGray" => CellFillPatternStyle.MediumGray,
            "darkGray" => CellFillPatternStyle.DarkGray,
            "lightHorizontal" => CellFillPatternStyle.LightHorizontal,
            "lightVertical" => CellFillPatternStyle.LightVertical,
            "lightDown" => CellFillPatternStyle.LightDown,
            "lightUp" => CellFillPatternStyle.LightUp,
            "lightGrid" => CellFillPatternStyle.LightGrid,
            "lightTrellis" => CellFillPatternStyle.LightTrellis,
            "darkHorizontal" => CellFillPatternStyle.DarkHorizontal,
            "darkVertical" => CellFillPatternStyle.DarkVertical,
            "darkDown" => CellFillPatternStyle.DarkDown,
            "darkUp" => CellFillPatternStyle.DarkUp,
            "darkGrid" => CellFillPatternStyle.DarkGrid,
            "darkTrellis" => CellFillPatternStyle.DarkTrellis,
            _ => CellFillPatternStyle.None
        };
}
