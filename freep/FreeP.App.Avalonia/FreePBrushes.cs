using Avalonia.Media;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal static class FreePBrushes
{
    private static readonly ProductThemeResourceProfile ThemeResources = ProductThemeResourceProfiles.FreeP;
    private static readonly FreePVisualPalette Palette = FreePVisualPalettes.Default;

    internal static IBrush Accent => ResolveTheme("Accent", BrandThemes.FreeP.Colors.Accent);
    internal static IBrush AccentDark => ResolveTheme("AccentDark", BrandThemes.FreeP.Colors.AccentDark);
    internal static IBrush SheetSurface => ResolveTheme("SheetSurface", BrandThemes.FreeP.Colors.SheetSurface);
    internal static IBrush White => ResolveTheme("White", BrandThemes.FreeP.Colors.White);

    internal static IBrush PaneHeadingText => Create(Palette.PaneHeadingText);
    internal static IBrush PaneText => Create(Palette.PaneText);
    internal static IBrush PaneSecondaryText => Create(Palette.PaneSecondaryText);
    internal static IBrush PaneMutedText => Create(Palette.PaneMutedText);
    internal static IBrush PaneSurface => Create(Palette.PaneSurface);
    internal static IBrush PaneBorder => Create(Palette.PaneBorder);
    internal static IBrush CardBorder => Create(Palette.CardBorder);
    internal static IBrush DisabledBorder => Create(Palette.DisabledBorder);
    internal static IBrush DisabledSurface => Create(Palette.DisabledSurface);
    internal static IBrush PlaceholderSurface => Create(Palette.PlaceholderSurface);
    internal static IBrush NotesHintSurface => Create(Palette.NotesHintSurface);
    internal static IBrush NotesSurface => Create(Palette.NotesSurface);
    internal static IBrush GridBorder => Create(Palette.GridBorder);
    internal static IBrush AnimationText => Create(Palette.AnimationText);
    internal static IBrush AnimationSelectedSurface => Create(Palette.AnimationSelectedSurface);
    internal static IBrush AnimationDanger => Create(Palette.AnimationDanger);
    internal static IBrush SelectedCommentSurface => Create(Palette.SelectedCommentSurface);
    internal static IBrush SelectedCardSurface => Create(Palette.SelectedCardSurface);
    internal static IBrush SelectedSwatchSurface => Create(Palette.SelectedSwatchSurface);
    internal static IBrush SelectedRowSurface => Create(Palette.SelectedRowSurface);
    internal static IBrush SubtlePaneSurface => Create(Palette.SubtlePaneSurface);
    internal static IBrush SubtlePaneBorder => Create(Palette.SubtlePaneBorder);
    internal static IBrush PresenterSurface => Create(Palette.PresenterSurface);
    internal static IBrush PresenterPanelSurface => Create(Palette.PresenterPanelSurface);
    internal static IBrush PresenterSecondarySurface => Create(Palette.PresenterSecondarySurface);
    internal static IBrush PresenterBorder => Create(Palette.PresenterBorder);
    internal static IBrush PresenterMutedText => Create(Palette.PresenterMutedText);

    private static IBrush ResolveTheme(string role, ThemeColor fallback) =>
        AvaloniaThemeResourceResolver.Find<IBrush>(ThemeResources.Brush(role)) ?? Create(fallback);

    private static SolidColorBrush Create(ThemeColor color) =>
        new(AvaloniaThemeApplier.ToColor(color));
}
