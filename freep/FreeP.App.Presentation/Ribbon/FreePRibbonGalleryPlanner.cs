using Free.Shared.Ribbon;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Shared WPF-authority geometry and colour contract for the Design / Animations / Transitions ribbon
/// preview galleries. The hosts keep native widget construction; every number and colour lives here.
/// </summary>
public static class FreePRibbonGalleryMetrics
{
    public static readonly SrgbColor HoverBackground = new(0xEA, 0xF1, 0xFB);
    public static readonly SrgbColor HoverBorder = new(0x2B, 0x57, 0x9A);
    public static readonly SrgbColor ThumbnailBorder = new(0x9C, 0x9C, 0x9C);
    public static readonly SrgbColor AnimationPreviewBorder = new(0xA6, 0xA6, 0xA6);
    public static readonly SrgbColor AnimationGlyphColor = new(0x8C, 0x8C, 0x8C);
    public static readonly SrgbColor TransitionIncomingColor = new(0xB7, 0xB7, 0xB7);
    public static readonly SrgbColor TransitionOutgoingColor = new(0xE9, 0xE9, 0xE9);

    public const double StripMarginSide = 2;
    public const double StripMarginTop = 1;
    public const double CaptionFontSize = 9;
    public const double CaptionMarginSide = 1;
    public const double CaptionMarginTop = 1;
    public const double ButtonPadding = 1;
    public const double ButtonSideMargin = 1;

    // Transition thumbnail.
    public const double TransitionButtonWidth = 54;
    public const double TransitionButtonHeight = 51;
    public const double TransitionPreviewWidth = 50;
    public const double TransitionPreviewHeight = 30;

    // Animation favourite thumbnail and More button.
    public const double AnimationButtonWidth = 55;
    public const double AnimationButtonHeight = 51;
    public const double AnimationPreviewWidth = 50;
    public const double AnimationPreviewHeight = 30;
    public const double AnimationGlyphFontSize = 25;
    public const double AnimationNoneGlyphFontSize = 24;
    public const string AnimationGlyphFontFamily = "Segoe UI Symbol";
    public const double AnimationMoreWidth = 62;
    public const double AnimationMoreHeight = 50;
    public const double AnimationMoreFontSize = 10;
    public const double AnimationMoreLeftMargin = 2;
    public const double AnimationMoreRightMargin = 1;

    // Theme thumbnail and More button.
    public const double ThemePreviewWidth = 82;
    public const double ThemePreviewHeight = 50;
    public const double ThemePreviewSideMargin = 3;
    public const double ThemePagePaddingSide = 7;
    public const double ThemePagePaddingTop = 3;
    public const double ThemePagePaddingBottom = 4;
    public const double ThemeSampleFontSize = 26;
    public const string ThemeSampleText = "Aa";
    public const double ThemeSwatchWidth = 7;
    public const double ThemeSwatchHeight = 4;
    public const double ThemeSwatchGap = 1;
    public const double ThemeMoreMinWidth = 28;
    public const double ThemeMoreHeight = 48;
    public const double ThemeMorePaddingSide = 4;
    // The WPF More button inherits the ribbon's ambient font size (plain string content); the Avalonia button
    // wraps the text in an explicit 10pt TextBlock. Kept per host so neither rendering changes.
    public const double AvaloniaThemeMoreFontSize = 10;
    public const string ThemeMoreText = "More…";
    public const string ThemeMoreLabel = "More Themes";

    public static double AnimationGlyphSizeFor(string commandId) =>
        PresentationAnimationPreviewCatalog.IsNone(commandId) ? AnimationNoneGlyphFontSize : AnimationGlyphFontSize;
}

public enum RibbonGalleryPreviewAlignment
{
    Stretch,
    Start,
    Center,
    End,
}

public enum TransitionPreviewKind
{
    None,
    Fade,
    Push,
    Wipe,
    Split,
    Reveal,
    Cut,
    RandomBars,
}

public sealed record TransitionGalleryEntry(string CommandId, string LabelKey, TransitionPreviewKind Preview);

/// <summary>One bar of the Random Bars preview: fixed size, vertically centred, offset from the left.</summary>
public sealed record TransitionPreviewBar(double Width, double Height, double LeftMargin);

/// <summary>
/// Geometry of the incoming-slide block inside a transition thumbnail. A null width or height means the
/// block stretches on that axis; margins are uniform-per-axis (horizontal, vertical).
/// </summary>
public sealed record TransitionPreviewPlan(
    double? Width,
    double? Height,
    RibbonGalleryPreviewAlignment Horizontal,
    RibbonGalleryPreviewAlignment Vertical,
    double HorizontalMargin,
    double VerticalMargin,
    double Opacity,
    bool IncomingVisible,
    IReadOnlyList<TransitionPreviewBar> Bars);

public static class TransitionGalleryPlanner
{
    public const double SmallLabelsNarrowRibbonWidthThreshold = 800;
    public const int FullVisibleCount = 6;
    public const int SmallLabelsNarrowVisibleCount = 3;
    public const int SmallLabelsWideVisibleCount = 4;
    public const int IconOnlyVisibleCount = 3;
    public const int RandomBarCount = 5;

    public static IReadOnlyList<TransitionGalleryEntry> Entries { get; } =
    [
        new("freep.transition.none", "Ribbon_Command_TransitionNone_Label", TransitionPreviewKind.None),
        new("freep.transition.fade", "Ribbon_Command_TransitionFade_Label", TransitionPreviewKind.Fade),
        new("freep.transition.push", "Ribbon_Command_TransitionPush_Label", TransitionPreviewKind.Push),
        new("freep.transition.wipe", "Ribbon_Command_TransitionWipe_Label", TransitionPreviewKind.Wipe),
        new("freep.transition.split", "Ribbon_Command_TransitionSplit_Label", TransitionPreviewKind.Split),
        new("freep.transition.reveal", "Ribbon_Command_TransitionReveal_Label", TransitionPreviewKind.Reveal),
        new("freep.transition.cut", "Ribbon_Command_TransitionCut_Label", TransitionPreviewKind.Cut),
        new("freep.transition.random-bars", "Ribbon_Command_TransitionRandomBars_Label", TransitionPreviewKind.RandomBars),
    ];

    /// <summary>
    /// PowerPoint reveals a progressively wider first row as the Transitions tab gains room; the adjacent
    /// More group retains the complete catalog.
    /// </summary>
    public static int VisibleCount(RibbonAdaptiveGroupState state, double availableRibbonWidth) => state switch
    {
        RibbonAdaptiveGroupState.Full => FullVisibleCount,
        RibbonAdaptiveGroupState.SmallWithLabels =>
            availableRibbonWidth <= SmallLabelsNarrowRibbonWidthThreshold
                ? SmallLabelsNarrowVisibleCount
                : SmallLabelsWideVisibleCount,
        RibbonAdaptiveGroupState.IconOnly => IconOnlyVisibleCount,
        _ => IconOnlyVisibleCount,
    };

    public static IReadOnlyList<TransitionGalleryEntry> VisibleEntries(
        RibbonAdaptiveGroupState state,
        double availableRibbonWidth) =>
        Entries.Take(VisibleCount(state, availableRibbonWidth)).ToArray();

    public static TransitionPreviewPlan PreviewFor(TransitionPreviewKind kind)
    {
        const RibbonGalleryPreviewAlignment stretch = RibbonGalleryPreviewAlignment.Stretch;
        return kind switch
        {
            TransitionPreviewKind.None => Block(horizontalMargin: 5, verticalMargin: 5),
            TransitionPreviewKind.Fade => Block(horizontalMargin: 8, verticalMargin: 6, opacity: 0.62),
            TransitionPreviewKind.Push => Block(width: 31, horizontal: RibbonGalleryPreviewAlignment.End),
            TransitionPreviewKind.Wipe => Block(width: 25, horizontal: RibbonGalleryPreviewAlignment.Start),
            TransitionPreviewKind.Split => Block(width: 18, horizontal: RibbonGalleryPreviewAlignment.Center),
            TransitionPreviewKind.Reveal => Block(height: 18, vertical: RibbonGalleryPreviewAlignment.End),
            TransitionPreviewKind.Cut => Block(horizontalMargin: 3, verticalMargin: 3),
            TransitionPreviewKind.RandomBars => new TransitionPreviewPlan(
                null, null, stretch, stretch, 0, 0, 1, false, BuildBars()),
            _ => Block(),
        };
    }

    private static TransitionPreviewPlan Block(
        double? width = null,
        double? height = null,
        RibbonGalleryPreviewAlignment horizontal = RibbonGalleryPreviewAlignment.Stretch,
        RibbonGalleryPreviewAlignment vertical = RibbonGalleryPreviewAlignment.Stretch,
        double horizontalMargin = 0,
        double verticalMargin = 0,
        double opacity = 1) =>
        new(width, height, horizontal, vertical, horizontalMargin, verticalMargin, opacity, true, []);

    private static TransitionPreviewBar[] BuildBars()
    {
        var bars = new TransitionPreviewBar[RandomBarCount];
        for (var index = 0; index < bars.Length; index++)
            bars[index] = new TransitionPreviewBar(5, index % 2 == 0 ? 24 : 17, 5 + index * 9);
        return bars;
    }
}

public sealed record AnimationGalleryCategory(string HeaderKey, string CommandMarker, IReadOnlyList<RibbonControl> Controls);

public sealed record AnimationGalleryPlan(
    IReadOnlyList<RibbonControl> Controls,
    IReadOnlyList<RibbonControl> Favorites,
    IReadOnlyList<AnimationGalleryCategory> Categories);

public static class AnimationGalleryPlanner
{
    public const string EffectsGroupId = "animation-effects";
    public const string MoreEffectsLabelKey = "Ribbon_Command_AnimationMoreEffects_Label";
    public const int SmallLabelsFavoriteCount = 3;

    public static IReadOnlyList<string> FavoriteCommandIds { get; } =
    [
        "freep.anim.none",
        "freep.anim.entrance.appear",
        "freep.anim.entrance.fade",
        "freep.anim.entrance.fly-in",
        "freep.anim.entrance.wipe",
        "freep.anim.entrance.zoom",
    ];

    /// <summary>Ordered (header resource key, command-id segment) pairs for the More Effects menu.</summary>
    public static IReadOnlyList<(string HeaderKey, string CommandMarker)> CategoryDefinitions { get; } =
    [
        ("Ribbon_Category_AnimationEntrance_Label", ".entrance."),
        ("Ribbon_Category_AnimationEmphasis_Label", ".emphasis."),
        ("Ribbon_Category_AnimationExit_Label", ".exit."),
    ];

    public static IReadOnlyList<string> FavoriteIdsFor(RibbonAdaptiveGroupState state) =>
        state == RibbonAdaptiveGroupState.SmallWithLabels
            ? FavoriteCommandIds.Take(SmallLabelsFavoriteCount).ToArray()
            : FavoriteCommandIds;

    public static AnimationGalleryPlan Plan(RibbonTab tab, RibbonAdaptiveGroupState state)
    {
        var controls = tab.FindGroup(EffectsGroupId)?.Controls
            .Where(control => !string.IsNullOrEmpty(control.CommandId.Value))
            .ToArray()
            ?? [];

        var favorites = new List<RibbonControl>();
        foreach (var commandId in FavoriteIdsFor(state))
        {
            var control = controls.FirstOrDefault(candidate => candidate.CommandId.Value == commandId);
            if (control is not null)
                favorites.Add(control);
        }

        var categories = CategoryDefinitions
            .Select(definition => new AnimationGalleryCategory(
                definition.HeaderKey,
                definition.CommandMarker,
                controls
                    .Where(control => control.CommandId.Value.Contains(definition.CommandMarker, StringComparison.Ordinal))
                    .ToArray()))
            .ToArray();

        return new AnimationGalleryPlan(controls, favorites, categories);
    }
}

public sealed record ThemeGalleryPlan(IReadOnlyList<BuiltInThemeEntry> Thumbnails, bool ShowMore);

public static class ThemeGalleryPlanner
{
    public const string CommandIdPrefix = "freep.theme.";

    public static IReadOnlyList<ThemeColorSlot> AccentSlots { get; } =
    [
        ThemeColorSlot.Accent1,
        ThemeColorSlot.Accent2,
        ThemeColorSlot.Accent3,
        ThemeColorSlot.Accent4,
        ThemeColorSlot.Accent5,
        ThemeColorSlot.Accent6,
    ];

    /// <summary>Full shows every theme; any narrower state shows the active theme (else the first) plus More.</summary>
    public static ThemeGalleryPlan Plan(
        IReadOnlyList<BuiltInThemeEntry> entries,
        RibbonAdaptiveGroupState state,
        string? activeThemeName)
    {
        if (state == RibbonAdaptiveGroupState.Full)
            return new ThemeGalleryPlan(entries, false);

        var active = entries.FirstOrDefault(entry =>
                string.Equals(entry.DisplayName, activeThemeName, StringComparison.OrdinalIgnoreCase))
            ?? entries[0];
        return new ThemeGalleryPlan([active], true);
    }

    public static RibbonCommandId CommandIdFor(BuiltInThemeEntry entry) =>
        new(CommandIdPrefix + entry.Id.ToLowerInvariant());
}

public static class FreePRibbonGalleryCommands
{
    public static void Execute(IRibbonCommandRegistry registry, RibbonCommandId commandId)
    {
        if (registry.TryGet(commandId, out var command) && command is not null)
            command.Execute(RibbonCommandContext.Empty);
    }
}
