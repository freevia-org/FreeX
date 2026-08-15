using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record WordArtRibbonPreset<T>(
    RibbonCommandId CommandId,
    string Label,
    string? KeyTip,
    T Value) where T : struct, Enum;

public sealed record WordArtRibbonPorts(
    Func<bool> HasSelection,
    Action<WordArtStyle> ApplyStyle,
    Action<WordArtWarp> ApplyWarp,
    Action PrepareExecution);

/// <summary>
/// Owns the WordArt style/warp command catalog and command-state policy. Native editors only resolve
/// their current WordArt selection to a model target and repaint after the shared object edit.
/// </summary>
public static class WordArtRibbonWorkflow
{
    public static RibbonCommandId StyleMenuCommandId { get; } = new("freew.wordart-style");

    public static RibbonCommandId WarpMenuCommandId { get; } = new("freew.wordart-transform");

    public static IReadOnlyList<WordArtRibbonPreset<WordArtStyle>> StylePresets { get; } =
    [
        Style("freew.wordart-style-fill-blue", "Fill: Blue", "B", WordArtStyle.FillBlue),
        Style("freew.wordart-style-gradient", "Gradient Fill", "G", WordArtStyle.GradientFill),
        Style("freew.wordart-style-outline", "Outline", "O", WordArtStyle.Outline),
        Style("freew.wordart-style-shadow", "Shadow", "S", WordArtStyle.Shadow),
        Style("freew.wordart-style-fill-gold", "Fill: Gold", "D", WordArtStyle.FillGold),
        Style("freew.wordart-style-fill-white", "Fill: White", "W", WordArtStyle.FillWhite),
        Style("freew.wordart-style-grad-multi", "Gradient: Multicolour", "M", WordArtStyle.GradFillMulti),
        Style("freew.wordart-style-chrome-one", "Outline Only", "L", WordArtStyle.ChromeOne),
        Style("freew.wordart-style-chrome-two", "White + Outline", "H", WordArtStyle.ChromeTwo),
        Style("freew.wordart-style-shadow-orange", "Shadow: Orange", "A", WordArtStyle.ShadowOrange),
        Style("freew.wordart-style-glow-blue", "Glow: Blue", "U", WordArtStyle.GlowBlue),
        Style("freew.wordart-style-glow-gold", "Glow: Gold", "I", WordArtStyle.GlowGold),
        Style("freew.wordart-style-reflection", "Reflection", "F", WordArtStyle.Reflection),
        Style("freew.wordart-style-bevel", "Bevel", "V", WordArtStyle.Bevel),
        Style("freew.wordart-style-pattern", "Pattern Fill", "P", WordArtStyle.PatternFill),
    ];

    public static IReadOnlyList<WordArtRibbonPreset<WordArtWarp>> WarpPresets { get; } =
    [
        Warp("freew.wordart-warp-none", "No Transform", "N", WordArtWarp.None),
        Warp("freew.wordart-warp-arch-up", "Arch Up", "A", WordArtWarp.ArchUp),
        Warp("freew.wordart-warp-arch-down", "Arch Down", "D", WordArtWarp.ArchDown),
        Warp("freew.wordart-warp-circle", "Circle", "C", WordArtWarp.Circle),
        Warp("freew.wordart-warp-wave1", "Wave 1", "W", WordArtWarp.Wave1),
        Warp("freew.wordart-warp-wave2", "Wave 2", "V", WordArtWarp.Wave2),
        Warp("freew.wordart-warp-inflate", "Inflate", "I", WordArtWarp.Inflate),
        Warp("freew.wordart-warp-deflate", "Deflate", "E", WordArtWarp.Deflate),
        Warp("freew.wordart-warp-chevron-up", "Chevron Up", "U", WordArtWarp.ChevronUp),
        Warp("freew.wordart-warp-chevron-down", "Chevron Down", "H", WordArtWarp.ChevronDown),
        Warp("freew.wordart-warp-fade-right", "Fade Right", "F", WordArtWarp.FadeRight),
        Warp("freew.wordart-warp-fade-left", "Fade Left", "L", WordArtWarp.FadeLeft),
        Warp("freew.wordart-warp-slant-up", "Slant Up", "S", WordArtWarp.SlantUp),
        Warp("freew.wordart-warp-slant-down", "Slant Down", "T", WordArtWarp.SlantDown),
    ];

    public static RibbonCommandId StyleCommandId(WordArtStyle style) =>
        StylePresets.Single(preset => preset.Value == style).CommandId;

    public static RibbonCommandId WarpCommandId(WordArtWarp warp) => warp switch
    {
        WordArtWarp.Button => new("freew.wordart-warp-button"),
        WordArtWarp.InflateBottom => new("freew.wordart-warp-inflate-bottom"),
        _ => WarpPresets.Single(preset => preset.Value == warp).CommandId,
    };

    public static void Register(IRibbonCommandRegistry registry, WordArtRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(ports);

        registry.Register(StyleMenuCommandId, MenuCommand(ports));
        registry.Register(WarpMenuCommandId, MenuCommand(ports));

        foreach (var style in Enum.GetValues<WordArtStyle>())
        {
            var captured = style;
            registry.Register(
                StyleCommandId(captured),
                Command(() => ports.ApplyStyle(captured), ports));
        }

        foreach (var warp in Enum.GetValues<WordArtWarp>())
        {
            var captured = warp;
            registry.Register(
                WarpCommandId(captured),
                Command(() => ports.ApplyWarp(captured), ports));
        }
    }

    private static IRibbonStatefulCommand Command(Action execute, WordArtRibbonPorts ports) =>
        new FreeWRibbonStatefulPortCommand(
            _ => execute(),
            () => new RibbonCommandState(IsEnabled: ports.HasSelection()),
            ports.PrepareExecution);

    private static IRibbonStatefulCommand MenuCommand(WordArtRibbonPorts ports) =>
        new FreeWRibbonStatefulPortCommand(
            _ => { },
            () => new RibbonCommandState(IsEnabled: ports.HasSelection()));

    private static WordArtRibbonPreset<WordArtStyle> Style(
        string commandId,
        string label,
        string keyTip,
        WordArtStyle style) =>
        new(new RibbonCommandId(commandId), label, keyTip, style);

    private static WordArtRibbonPreset<WordArtWarp> Warp(
        string commandId,
        string label,
        string keyTip,
        WordArtWarp warp) =>
        new(new RibbonCommandId(commandId), label, keyTip, warp);
}
