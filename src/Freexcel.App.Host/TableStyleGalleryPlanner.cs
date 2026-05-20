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
        new("Light 1", "TableStyleLight1",
            new StructuredTableStyleBanding(new CellColor(217, 217, 217), new CellColor(242, 242, 242), CellColor.White, CellColor.Black)),
        new("Light 9", "TableStyleLight9",
            new StructuredTableStyleBanding(new CellColor(31, 115, 70), new CellColor(226, 239, 218), CellColor.White, CellColor.White)),
        new("Light 11", "TableStyleLight11",
            new StructuredTableStyleBanding(new CellColor(91, 155, 213), new CellColor(221, 235, 247), CellColor.White, CellColor.White)),
        new("Medium 2", "TableStyleMedium2",
            new StructuredTableStyleBanding(new CellColor(31, 78, 121), new CellColor(222, 235, 247), CellColor.White, CellColor.White)),
        new("Medium 4", "TableStyleMedium4",
            new StructuredTableStyleBanding(new CellColor(112, 48, 160), new CellColor(229, 224, 236), CellColor.White, CellColor.White)),
        new("Medium 7", "TableStyleMedium7",
            new StructuredTableStyleBanding(new CellColor(192, 80, 77), new CellColor(242, 220, 219), CellColor.White, CellColor.White)),
        new("Dark 1", "TableStyleDark1",
            new StructuredTableStyleBanding(new CellColor(54, 54, 54), new CellColor(68, 68, 68), new CellColor(80, 80, 80), CellColor.White)),
        new("Dark 4", "TableStyleDark4",
            new StructuredTableStyleBanding(new CellColor(0, 97, 0), new CellColor(0, 125, 0), new CellColor(0, 150, 0), CellColor.White))
    ];

    public static TableStyleGalleryOption GetOption(int index)
    {
        var options = GetOptions();
        return options[Math.Clamp(index, 0, options.Count - 1)];
    }
}
