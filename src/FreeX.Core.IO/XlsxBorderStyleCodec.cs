using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxBorderStyleCodec
{
    public static BorderStyle Decode(string? token) => token switch
    {
        "thin" => BorderStyle.Thin,
        "medium" => BorderStyle.Medium,
        "thick" => BorderStyle.Thick,
        "dashed" => BorderStyle.Dashed,
        "dotted" => BorderStyle.Dotted,
        "double" => BorderStyle.Double,
        "hair" => BorderStyle.Hair,
        "slantDashDot" => BorderStyle.SlantDashDot,
        "mediumDashed" => BorderStyle.MediumDashed,
        "dashDot" => BorderStyle.DashDot,
        "mediumDashDot" => BorderStyle.MediumDashDot,
        "dashDotDot" => BorderStyle.DashDotDot,
        "mediumDashDotDot" => BorderStyle.MediumDashDotDot,
        _ => BorderStyle.None
    };
}
