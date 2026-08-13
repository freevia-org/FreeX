using System.Windows.Media;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal static class FreePBrushes
{
    private static readonly ProductThemeResourceProfile ThemeResources = ProductThemeResourceProfiles.FreeP;
    private static readonly FreePVisualPalette Palette = FreePVisualPalettes.Default;

    internal static Brush Accent => ResolveTheme("Accent", BrandThemes.FreeP.Colors.Accent);
    internal static Brush AccentDark => ResolveTheme("AccentDark", BrandThemes.FreeP.Colors.AccentDark);
    internal static Brush SheetSurface => ResolveTheme("SheetSurface", BrandThemes.FreeP.Colors.SheetSurface);
    internal static Brush White => ResolveTheme("White", BrandThemes.FreeP.Colors.White);

    internal static Color AccentColor => ResolveThemeColor("Accent", BrandThemes.FreeP.Colors.Accent);
    internal static Color AccentDarkColor => ResolveThemeColor("AccentDark", BrandThemes.FreeP.Colors.AccentDark);

    internal static Brush PaneHeadingText => Create(Palette.PaneHeadingText);
    internal static Brush PaneText => Create(Palette.PaneText);
    internal static Brush PaneSecondaryText => Create(Palette.PaneSecondaryText);
    internal static Brush PaneMutedText => Create(Palette.PaneMutedText);
    internal static Brush PaneSurface => Create(Palette.PaneSurface);
    internal static Brush PaneBorder => Create(Palette.PaneBorder);
    internal static Brush CardBorder => Create(Palette.CardBorder);
    internal static Brush DisabledBorder => Create(Palette.DisabledBorder);
    internal static Brush DisabledSurface => Create(Palette.DisabledSurface);
    internal static Brush PlaceholderSurface => Create(Palette.PlaceholderSurface);
    internal static Brush NotesHintSurface => Create(Palette.NotesHintSurface);
    internal static Brush NotesSurface => Create(Palette.NotesSurface);
    internal static Brush GridBorder => Create(Palette.GridBorder);
    internal static Brush AnimationText => Create(Palette.AnimationText);
    internal static Brush AnimationSelectedSurface => Create(Palette.AnimationSelectedSurface);
    internal static Brush AnimationDanger => Create(Palette.AnimationDanger);
    internal static Brush SelectedCommentSurface => Create(Palette.SelectedCommentSurface);
    internal static Brush SelectedCardSurface => Create(Palette.SelectedCardSurface);
    internal static Brush SelectedSwatchSurface => Create(Palette.SelectedSwatchSurface);
    internal static Brush SelectedRowSurface => Create(Palette.SelectedRowSurface);
    internal static Brush SubtlePaneSurface => Create(Palette.SubtlePaneSurface);
    internal static Brush SubtlePaneBorder => Create(Palette.SubtlePaneBorder);
    internal static Brush PresenterSurface => Create(Palette.PresenterSurface);
    internal static Brush PresenterPanelSurface => Create(Palette.PresenterPanelSurface);
    internal static Brush PresenterSecondarySurface => Create(Palette.PresenterSecondarySurface);
    internal static Brush PresenterBorder => Create(Palette.PresenterBorder);
    internal static Brush PresenterMutedText => Create(Palette.PresenterMutedText);

    private static Brush ResolveTheme(string role, ThemeColor fallback) =>
        WpfThemeResourceResolver.Find<Brush>(ThemeResources.Brush(role)) ?? Create(fallback);

    private static Color ResolveThemeColor(string role, ThemeColor fallback) =>
        WpfThemeResourceResolver.ResolveProjectedOr<SolidColorBrush, Color>(
            ThemeResources.Brush(role),
            brush => brush.Color,
            WpfThemeApplier.ToColor(fallback));

    private static SolidColorBrush Create(ThemeColor color) =>
        new(WpfThemeApplier.ToColor(color));
}
