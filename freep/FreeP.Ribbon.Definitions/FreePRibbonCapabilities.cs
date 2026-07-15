using Free.Shared.Ribbon;

namespace FreeP.Ribbon.Definitions;

/// <summary>
/// Describes the FreeP ribbon surface a host can safely expose.
/// </summary>
public sealed record FreePRibbonCapabilities(
    string Name,
    bool UseAvaloniaBackedSurface)
{
    public static FreePRibbonCapabilities Wpf { get; } = new(
        "WPF",
        UseAvaloniaBackedSurface: false)
    {
        Profile = FreePRibbonProfile.Wpf
    };

    public static FreePRibbonCapabilities Avalonia { get; } = new(
        "Avalonia",
        UseAvaloniaBackedSurface: true)
    {
        Profile = FreePRibbonProfile.Avalonia
    };

    internal FreePRibbonProfile Profile { get; init; } = FreePRibbonProfile.Wpf;
}

internal enum FreePRibbonHomeGroupId
{
    File,
    Slides,
    Clipboard,
    Font,
    Paragraph,
    Arrange,
    Edit,
    Editing,
    SlideShow,
}

internal sealed record FreePRibbonProfile(
    IReadOnlyList<FreePRibbonHomeGroupId> HomeGroups,
    IReadOnlyDictionary<FreePRibbonHomeGroupId, int> HomeGroupPriorities,
    string NewSlideKeyTip,
    string SlideShowGroupKeyTip,
    RibbonCommandIconKind SlideShowFromCurrentSlideIcon,
    bool SlideShowOnHome,
    bool IncludeAnimationSeparators,
    int AnimationTriggerWidth)
{
    internal static FreePRibbonProfile Wpf { get; } = new(
        HomeGroups:
        [
            FreePRibbonHomeGroupId.Slides,
            FreePRibbonHomeGroupId.Clipboard,
            FreePRibbonHomeGroupId.Font,
            FreePRibbonHomeGroupId.Paragraph,
            FreePRibbonHomeGroupId.Arrange,
            FreePRibbonHomeGroupId.Editing,
        ],
        HomeGroupPriorities: new Dictionary<FreePRibbonHomeGroupId, int>
        {
            [FreePRibbonHomeGroupId.Slides] = 100,
            [FreePRibbonHomeGroupId.Clipboard] = 90,
            [FreePRibbonHomeGroupId.Font] = 80,
            [FreePRibbonHomeGroupId.Paragraph] = 78,
            [FreePRibbonHomeGroupId.Arrange] = 70,
            [FreePRibbonHomeGroupId.Editing] = 70,
        },
        NewSlideKeyTip: FreePRibbonText.NewSlideKeyTip,
        SlideShowGroupKeyTip: FreePRibbonText.SlideShowGroupWpfKeyTip,
        SlideShowFromCurrentSlideIcon: RibbonCommandIconKind.Previous,
        SlideShowOnHome: false,
        IncludeAnimationSeparators: true,
        AnimationTriggerWidth: 130);

    internal static FreePRibbonProfile Avalonia { get; } = new(
        HomeGroups:
        [
            FreePRibbonHomeGroupId.File,
            FreePRibbonHomeGroupId.Slides,
            FreePRibbonHomeGroupId.Clipboard,
            FreePRibbonHomeGroupId.Font,
            FreePRibbonHomeGroupId.Paragraph,
            FreePRibbonHomeGroupId.Arrange,
            FreePRibbonHomeGroupId.Edit,
            FreePRibbonHomeGroupId.Editing,
            FreePRibbonHomeGroupId.SlideShow,
        ],
        HomeGroupPriorities: new Dictionary<FreePRibbonHomeGroupId, int>
        {
            [FreePRibbonHomeGroupId.File] = 100,
            [FreePRibbonHomeGroupId.Slides] = 90,
            [FreePRibbonHomeGroupId.Clipboard] = 88,
            [FreePRibbonHomeGroupId.Font] = 86,
            [FreePRibbonHomeGroupId.Paragraph] = 84,
            [FreePRibbonHomeGroupId.Arrange] = 85,
            [FreePRibbonHomeGroupId.Edit] = 80,
            [FreePRibbonHomeGroupId.Editing] = 75,
            [FreePRibbonHomeGroupId.SlideShow] = 70,
        },
        NewSlideKeyTip: FreePRibbonText.NewSlideAvaloniaKeyTip,
        SlideShowGroupKeyTip: FreePRibbonText.SlideShowGroupAvaloniaKeyTip,
        SlideShowFromCurrentSlideIcon: RibbonCommandIconKind.Next,
        SlideShowOnHome: true,
        IncludeAnimationSeparators: false,
        AnimationTriggerWidth: 120);
}
