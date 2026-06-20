using Free.Shared.Ribbon.Wpf;

namespace FreeP.App.Host;

/// <summary>
/// Maps FreeP's <c>freep.*</c> ribbon command ids to shared <see cref="RibbonCommandIconKind"/> glyphs, so
/// the shared WPF renderer (ribbon, BackstageFrame rail, QAT) draws a meaningful vector icon per control.
/// Ids without a dedicated mapping fall back to the generic glyph. Mirrors FreeWRibbonIcons, kept minimal for
/// the scaffold's small ribbon.
/// </summary>
internal static class FreePRibbonIcons
{
    /// <summary>Installs the FreeP command-id → glyph resolver on the shared icon factory.</summary>
    public static void Install() => RibbonIconFactory.CommandIconKindResolver = Resolve;

    public static RibbonCommandIconKind? Resolve(string commandId) =>
        Map.TryGetValue(commandId, out var kind) ? kind : null;

    private static readonly IReadOnlyDictionary<string, RibbonCommandIconKind> Map =
        new Dictionary<string, RibbonCommandIconKind>(StringComparer.OrdinalIgnoreCase)
        {
            // Slides
            ["freep.new-slide"] = RibbonCommandIconKind.Insert,
            ["freep.duplicate-slide"] = RibbonCommandIconKind.Copy,
            ["freep.delete-slide"] = RibbonCommandIconKind.Delete,
            ["freep.layout"] = RibbonCommandIconKind.Grid,

            // Clipboard
            ["freep.paste"] = RibbonCommandIconKind.Paste,
            ["freep.cut"] = RibbonCommandIconKind.Cut,
            ["freep.copy"] = RibbonCommandIconKind.Copy,

            // Font
            ["freep.font-family"] = RibbonCommandIconKind.Font,
            ["freep.bold"] = RibbonCommandIconKind.Bold,
            ["freep.italic"] = RibbonCommandIconKind.Italic,
            ["freep.underline"] = RibbonCommandIconKind.Underline,

            // Insert
            ["freep.text-box"] = RibbonCommandIconKind.TextBox,
            ["freep.picture"] = RibbonCommandIconKind.Picture,
            ["freep.shape-rectangle"] = RibbonCommandIconKind.Rectangle,
            ["freep.shape-ellipse"] = RibbonCommandIconKind.Ellipse,
        };
}
