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
