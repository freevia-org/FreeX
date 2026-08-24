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
    Slides,
    Clipboard,
    Font,
    Paragraph,
    Arrange,
    Edit,
    Editing,
}

internal sealed record FreePRibbonProfile(
    IReadOnlyList<FreePRibbonHomeGroupId> HomeGroups,
    IReadOnlyDictionary<FreePRibbonHomeGroupId, int> HomeGroupPriorities,
    Func<string> SlideShowGroupKeyTip,
    RibbonCommandIconKind SlideShowFromCurrentSlideIcon,
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
            FreePRibbonHomeGroupId.Edit,
            FreePRibbonHomeGroupId.Editing,
        ],
        HomeGroupPriorities: new Dictionary<FreePRibbonHomeGroupId, int>
        {
            [FreePRibbonHomeGroupId.Slides] = 100,
            [FreePRibbonHomeGroupId.Clipboard] = 90,
            [FreePRibbonHomeGroupId.Font] = 80,
            [FreePRibbonHomeGroupId.Paragraph] = 78,
            [FreePRibbonHomeGroupId.Arrange] = 70,
            [FreePRibbonHomeGroupId.Edit] = 75,
            [FreePRibbonHomeGroupId.Editing] = 70,
        },
        SlideShowGroupKeyTip: () => FreePRibbonText.SlideShowGroupWpfKeyTip,
        SlideShowFromCurrentSlideIcon: RibbonCommandIconKind.Previous,
        IncludeAnimationSeparators: true,
        AnimationTriggerWidth: 130);

    internal static FreePRibbonProfile Avalonia { get; } = new(
        HomeGroups:
        [
            FreePRibbonHomeGroupId.Slides,
            FreePRibbonHomeGroupId.Clipboard,
            FreePRibbonHomeGroupId.Font,
            FreePRibbonHomeGroupId.Paragraph,
            FreePRibbonHomeGroupId.Arrange,
            FreePRibbonHomeGroupId.Edit,
            FreePRibbonHomeGroupId.Editing,
        ],
        HomeGroupPriorities: new Dictionary<FreePRibbonHomeGroupId, int>
        {
            [FreePRibbonHomeGroupId.Slides] = 90,
            [FreePRibbonHomeGroupId.Clipboard] = 88,
            [FreePRibbonHomeGroupId.Font] = 86,
            [FreePRibbonHomeGroupId.Paragraph] = 84,
            [FreePRibbonHomeGroupId.Arrange] = 85,
            [FreePRibbonHomeGroupId.Edit] = 80,
            [FreePRibbonHomeGroupId.Editing] = 75,
        },
        SlideShowGroupKeyTip: () => FreePRibbonText.SlideShowGroupWpfKeyTip,
        SlideShowFromCurrentSlideIcon: RibbonCommandIconKind.Previous,
        IncludeAnimationSeparators: false,
        AnimationTriggerWidth: 120);
}
