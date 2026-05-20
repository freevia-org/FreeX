using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record TableStyleGalleryOption(
    string Label,
    string StyleName,
    StructuredTableStyleBanding Banding);

public static class TableStyleGalleryPlanner
{
    public static IReadOnlyList<TableStyleGalleryOption> GetOptions() =>
    [
        ..CreateLightStyles(),
        ..CreateMediumStyles(),
        ..CreateDarkStyles()
    ];

    public static TableStyleGalleryOption GetOption(int index)
    {
        var options = GetOptions();
        return options[Math.Clamp(index, 0, options.Count - 1)];
    }

    private static IEnumerable<TableStyleGalleryOption> CreateLightStyles()
    {
        var accents = new[]
        {
            (new CellColor(217, 217, 217), new CellColor(242, 242, 242), CellColor.Black),
            (new CellColor(91, 155, 213), new CellColor(221, 235, 247), CellColor.White),
            (new CellColor(237, 125, 49), new CellColor(252, 228, 214), CellColor.White),
            (new CellColor(165, 165, 165), new CellColor(237, 237, 237), CellColor.White),
            (new CellColor(255, 192, 0), new CellColor(255, 242, 204), CellColor.Black),
            (new CellColor(68, 114, 196), new CellColor(217, 225, 242), CellColor.White),
            (new CellColor(112, 173, 71), new CellColor(226, 239, 218), CellColor.White)
        };

        return CreateStyleGroup("Light", 21, accents, useDarkRows: false);
    }

    private static IEnumerable<TableStyleGalleryOption> CreateMediumStyles()
    {
        var accents = new[]
        {
            (new CellColor(31, 78, 121), new CellColor(222, 235, 247), CellColor.White),
            (new CellColor(31, 115, 70), new CellColor(226, 239, 218), CellColor.White),
            (new CellColor(91, 155, 213), new CellColor(221, 235, 247), CellColor.White),
            (new CellColor(112, 48, 160), new CellColor(229, 224, 236), CellColor.White),
            (new CellColor(192, 80, 77), new CellColor(242, 220, 219), CellColor.White),
            (new CellColor(128, 100, 162), new CellColor(235, 229, 241), CellColor.White),
            (new CellColor(75, 172, 198), new CellColor(218, 238, 243), CellColor.White)
        };

        return CreateStyleGroup("Medium", 28, accents, useDarkRows: false);
    }

    private static IEnumerable<TableStyleGalleryOption> CreateDarkStyles()
    {
        var accents = new[]
        {
            (new CellColor(54, 54, 54), new CellColor(68, 68, 68), CellColor.White),
            (new CellColor(31, 78, 121), new CellColor(41, 92, 135), CellColor.White),
            (new CellColor(0, 97, 0), new CellColor(0, 125, 0), CellColor.White),
            (new CellColor(91, 44, 111), new CellColor(112, 48, 160), CellColor.White),
            (new CellColor(128, 55, 52), new CellColor(160, 64, 61), CellColor.White),
            (new CellColor(68, 84, 106), new CellColor(84, 105, 132), CellColor.White)
        };

        return CreateStyleGroup("Dark", 11, accents, useDarkRows: true);
    }

    private static IEnumerable<TableStyleGalleryOption> CreateStyleGroup(
        string family,
        int count,
        IReadOnlyList<(CellColor Header, CellColor Band, CellColor Font)> accents,
        bool useDarkRows)
    {
        for (var index = 1; index <= count; index++)
        {
            var accent = accents[(index - 1) % accents.Count];
            var evenFill = useDarkRows
                ? Darken(accent.Band, 18)
                : CellColor.White;
            var oddFill = useDarkRows
                ? accent.Band
                : Lighten(accent.Band, ((index - 1) / accents.Count) * 8);

            yield return new TableStyleGalleryOption(
                $"{family} {index}",
                $"TableStyle{family}{index}",
                new StructuredTableStyleBanding(accent.Header, oddFill, evenFill, accent.Font));
        }
    }

    private static CellColor Lighten(CellColor color, int amount) =>
        new(
            ClampColor(color.R + amount),
            ClampColor(color.G + amount),
            ClampColor(color.B + amount));

    private static CellColor Darken(CellColor color, int amount) =>
        new(
            ClampColor(color.R - amount),
            ClampColor(color.G - amount),
            ClampColor(color.B - amount));

    private static byte ClampColor(int value) => (byte)Math.Clamp(value, 0, 255);
}
