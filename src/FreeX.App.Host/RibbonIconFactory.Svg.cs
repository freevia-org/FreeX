using System.Windows.Media;
using Free.Shared.Ribbon.Icons;
using Free.Shared.Ribbon.Wpf;

namespace FreeX.App.Host;

public static partial class RibbonIconFactory
{
    // Key the cache by the EXACT rendered size, not a coarse small/large bucket. The vector is re-wrapped
    // per size (the shared loader scales stroke widths to the target), so sharing one drawing across e.g.
    // 18/20/22px left strokes mis-scaled and the glyph looked soft/blurry.
    private static readonly SvgCommandIconLoader CommandIconLoader = new(
        resourceFolder: "CommandIconsSvg",
        slugFromCommandName: name => RibbonCommandIconPolicy.ToCommandIconSlug(
            RibbonCommandIconPolicy.NormalizeCommandIconName(name)),
        slugCandidates: RibbonCommandIconPolicy.GetCommandIconSlugCandidates,
        sizeKeySelector: size => ((int)Math.Round(size)).ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static ImageSource? TryLoadCommandIcon(string commandName, Brush glyphBrush, double size) =>
        CommandIconLoader.TryLoad(commandName, glyphBrush, size);

}
