using Free.Shared.Theme;

namespace FreeP.App.Compositor;

/// <summary>
/// Presentation-specific visual roles shared by the WPF and Avalonia renderers.
/// Product-wide chrome colors remain owned by <see cref="BrandThemes.FreeP"/>.
/// </summary>
public sealed record FreePVisualPalette(
    ThemeColor PaneHeadingText,
    ThemeColor PaneText,
    ThemeColor PaneSecondaryText,
    ThemeColor PaneMutedText,
    ThemeColor PaneSurface,
    ThemeColor PaneBorder,
    ThemeColor CardBorder,
    ThemeColor DisabledBorder,
    ThemeColor DisabledSurface,
    ThemeColor PlaceholderSurface,
    ThemeColor NotesHintSurface,
    ThemeColor NotesSurface,
    ThemeColor GridBorder,
    ThemeColor AnimationText,
    ThemeColor AnimationSelectedSurface,
    ThemeColor AnimationDanger,
    ThemeColor SelectedCommentSurface,
    ThemeColor SelectedCardSurface,
    ThemeColor SelectedSwatchSurface,
    ThemeColor SelectedRowSurface,
    ThemeColor SubtlePaneSurface,
    ThemeColor SubtlePaneBorder,
    ThemeColor PresenterSurface,
    ThemeColor PresenterPanelSurface,
    ThemeColor PresenterSecondarySurface,
    ThemeColor PresenterBorder,
    ThemeColor PresenterMutedText);

public static class FreePVisualPalettes
{
    /// <summary>Byte-identical to the pre-dedup FreeP WPF and Avalonia literals.</summary>
    public static FreePVisualPalette Default { get; } = new(
        PaneHeadingText: ThemeColor.FromHex("#333333"),
        PaneText: ThemeColor.FromHex("#444444"),
        PaneSecondaryText: ThemeColor.FromHex("#555555"),
        PaneMutedText: ThemeColor.FromHex("#666666"),
        PaneSurface: ThemeColor.FromHex("#FAFAFA"),
        PaneBorder: ThemeColor.FromHex("#C0C0C0"),
        CardBorder: ThemeColor.FromHex("#E0E0E0"),
        DisabledBorder: ThemeColor.FromHex("#C8C8C8"),
        DisabledSurface: ThemeColor.FromHex("#F0F0F0"),
        PlaceholderSurface: ThemeColor.FromHex("#E6E6E6"),
        NotesHintSurface: ThemeColor.FromHex("#FFFFF0"),
        NotesSurface: ThemeColor.FromHex("#FFFFE8"),
        GridBorder: ThemeColor.FromHex("#DDDDDD"),
        AnimationText: ThemeColor.FromHex("#222222"),
        AnimationSelectedSurface: ThemeColor.FromHex("#FFE0D6"),
        AnimationDanger: ThemeColor.FromHex("#C02020"),
        SelectedCommentSurface: ThemeColor.FromHex("#F4ECE8"),
        SelectedCardSurface: ThemeColor.FromHex("#FFF6F2"),
        SelectedSwatchSurface: ThemeColor.FromHex("#FEF2EC"),
        SelectedRowSurface: ThemeColor.FromHex("#E8F1FF"),
        SubtlePaneSurface: ThemeColor.FromHex("#F7F7F7"),
        SubtlePaneBorder: ThemeColor.FromHex("#E2E2E2"),
        PresenterSurface: ThemeColor.FromHex("#1E222A"),
        PresenterPanelSurface: ThemeColor.FromHex("#2D323D"),
        PresenterSecondarySurface: ThemeColor.FromHex("#262B35"),
        PresenterBorder: ThemeColor.FromHex("#505766"),
        PresenterMutedText: ThemeColor.FromHex("#AAB2C2"));
}

public interface IFreePVisualBrushAdapter<TBrush>
{
    static abstract TBrush ResolveTheme(ThemeResourceDescriptor resource, ThemeColor fallback);

    static abstract TBrush Create(ThemeColor color);
}

/// <summary>
/// Projects the portable FreeP palette into a renderer-native brush type.
/// </summary>
public abstract class FreePVisualBrushCatalog<TBrush, TAdapter>
    where TAdapter : IFreePVisualBrushAdapter<TBrush>
{
    private static readonly ProductThemeResourceProfile ThemeResources = ProductThemeResourceProfiles.FreeP;
    private static readonly FreePVisualPalette Palette = FreePVisualPalettes.Default;

    public static TBrush Accent => ResolveTheme("Accent", BrandThemes.FreeP.Colors.Accent);
    public static TBrush AccentDark => ResolveTheme("AccentDark", BrandThemes.FreeP.Colors.AccentDark);
    public static TBrush SheetSurface => ResolveTheme("SheetSurface", BrandThemes.FreeP.Colors.SheetSurface);
    public static TBrush White => ResolveTheme("White", BrandThemes.FreeP.Colors.White);

    public static TBrush PaneHeadingText => TAdapter.Create(Palette.PaneHeadingText);
    public static TBrush PaneText => TAdapter.Create(Palette.PaneText);
    public static TBrush PaneSecondaryText => TAdapter.Create(Palette.PaneSecondaryText);
    public static TBrush PaneMutedText => TAdapter.Create(Palette.PaneMutedText);
    public static TBrush PaneSurface => TAdapter.Create(Palette.PaneSurface);
    public static TBrush PaneBorder => TAdapter.Create(Palette.PaneBorder);
    public static TBrush CardBorder => TAdapter.Create(Palette.CardBorder);
    public static TBrush DisabledBorder => TAdapter.Create(Palette.DisabledBorder);
    public static TBrush DisabledSurface => TAdapter.Create(Palette.DisabledSurface);
    public static TBrush PlaceholderSurface => TAdapter.Create(Palette.PlaceholderSurface);
    public static TBrush NotesHintSurface => TAdapter.Create(Palette.NotesHintSurface);
    public static TBrush NotesSurface => TAdapter.Create(Palette.NotesSurface);
    public static TBrush GridBorder => TAdapter.Create(Palette.GridBorder);
    public static TBrush AnimationText => TAdapter.Create(Palette.AnimationText);
    public static TBrush AnimationSelectedSurface => TAdapter.Create(Palette.AnimationSelectedSurface);
    public static TBrush AnimationDanger => TAdapter.Create(Palette.AnimationDanger);
    public static TBrush SelectedCommentSurface => TAdapter.Create(Palette.SelectedCommentSurface);
    public static TBrush SelectedCardSurface => TAdapter.Create(Palette.SelectedCardSurface);
    public static TBrush SelectedSwatchSurface => TAdapter.Create(Palette.SelectedSwatchSurface);
    public static TBrush SelectedRowSurface => TAdapter.Create(Palette.SelectedRowSurface);
    public static TBrush SubtlePaneSurface => TAdapter.Create(Palette.SubtlePaneSurface);
    public static TBrush SubtlePaneBorder => TAdapter.Create(Palette.SubtlePaneBorder);
    public static TBrush PresenterSurface => TAdapter.Create(Palette.PresenterSurface);
    public static TBrush PresenterPanelSurface => TAdapter.Create(Palette.PresenterPanelSurface);
    public static TBrush PresenterSecondarySurface => TAdapter.Create(Palette.PresenterSecondarySurface);
    public static TBrush PresenterBorder => TAdapter.Create(Palette.PresenterBorder);
    public static TBrush PresenterMutedText => TAdapter.Create(Palette.PresenterMutedText);

    private static TBrush ResolveTheme(string role, ThemeColor fallback) =>
        TAdapter.ResolveTheme(ThemeResources.Brush(role), fallback);
}
