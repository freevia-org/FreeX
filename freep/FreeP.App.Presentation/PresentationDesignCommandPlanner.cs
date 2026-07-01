using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationDesignCommandIntentKind
{
    SetTheme,
    SetSlideSize,
    RequestCustomSlideSize,
    RequestLayoutPicker,
}

public sealed record PresentationDesignCommandPlan(
    string CommandId,
    PresentationDesignCommandIntentKind Intent,
    string? ThemeId = null,
    long? SlideSizeCxEmu = null,
    long? SlideSizeCyEmu = null);

public sealed record PresentationLayoutChoice(
    string LayoutId,
    string DisplayName,
    SlideLayoutType LayoutType,
    bool IsCurrent);

public sealed record PresentationLayoutPickerPlan(
    string CommandId,
    string? CurrentLayoutId,
    bool HasCurrentSlide,
    IReadOnlyList<PresentationLayoutChoice> Choices)
{
    public bool CanApply => HasCurrentSlide && Choices.Count > 0;
}

public static class PresentationDesignCommandPlanner
{
    public const string LayoutCommandId = "freep.layout";
    public const long SlideSizeWidescreen16x9CxEmu = DrawingMlCoordinateUnits.EmuPerInch * 40 / 3;
    public const long SlideSizeStandard4x3CxEmu = DrawingMlCoordinateUnits.EmuPerInch * 10;
    public const long SlideSizeStandardCyEmu = DrawingMlCoordinateUnits.EmuPerInch * 15 / 2;

    public static readonly PresentationDesignCommandPlan LayoutPlan =
        new(LayoutCommandId, PresentationDesignCommandIntentKind.RequestLayoutPicker);

    public static readonly IReadOnlyList<PresentationDesignCommandPlan> BuiltInPlans =
        new[]
        {
            new PresentationDesignCommandPlan(
                "freep.theme.office",
                PresentationDesignCommandIntentKind.SetTheme,
                ThemeId: BuiltInThemes.Id.Office),
            new PresentationDesignCommandPlan(
                "freep.theme.berlin",
                PresentationDesignCommandIntentKind.SetTheme,
                ThemeId: BuiltInThemes.Id.Berlin),
            new PresentationDesignCommandPlan(
                "freep.theme.facet",
                PresentationDesignCommandIntentKind.SetTheme,
                ThemeId: BuiltInThemes.Id.Facet),
            new PresentationDesignCommandPlan(
                "freep.theme.ion",
                PresentationDesignCommandIntentKind.SetTheme,
                ThemeId: BuiltInThemes.Id.Ion),
            new PresentationDesignCommandPlan(
                "freep.theme.slice",
                PresentationDesignCommandIntentKind.SetTheme,
                ThemeId: BuiltInThemes.Id.Slice),
            new PresentationDesignCommandPlan(
                "freep.slide-size-16x9",
                PresentationDesignCommandIntentKind.SetSlideSize,
                SlideSizeCxEmu: SlideSizeWidescreen16x9CxEmu,
                SlideSizeCyEmu: SlideSizeStandardCyEmu),
            new PresentationDesignCommandPlan(
                "freep.slide-size-4x3",
                PresentationDesignCommandIntentKind.SetSlideSize,
                SlideSizeCxEmu: SlideSizeStandard4x3CxEmu,
                SlideSizeCyEmu: SlideSizeStandardCyEmu),
            new PresentationDesignCommandPlan(
                "freep.slide-size-custom",
                PresentationDesignCommandIntentKind.RequestCustomSlideSize),
        };

    public static bool TryPlan(string commandId, out PresentationDesignCommandPlan plan)
    {
        if (StringComparer.Ordinal.Equals(LayoutPlan.CommandId, commandId))
        {
            plan = LayoutPlan;
            return true;
        }

        foreach (var candidate in BuiltInPlans)
        {
            if (StringComparer.Ordinal.Equals(candidate.CommandId, commandId))
            {
                plan = candidate;
                return true;
            }
        }

        plan = default!;
        return false;
    }

    public static PresentationLayoutPickerPlan BuildLayoutPickerPlan(
        Presentation presentation,
        int currentSlideIndex)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var currentSlide = currentSlideIndex >= 0 && currentSlideIndex < presentation.Slides.Count
            ? presentation.Slides[currentSlideIndex]
            : null;
        var currentLayoutId = currentSlide?.LayoutId;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var choices = new List<PresentationLayoutChoice>();

        foreach (var layout in presentation.Layouts)
        {
            if (string.IsNullOrWhiteSpace(layout.Id) || !seen.Add(layout.Id))
            {
                continue;
            }

            choices.Add(new PresentationLayoutChoice(
                layout.Id,
                BuildLayoutDisplayName(layout),
                layout.LayoutType,
                StringComparer.Ordinal.Equals(layout.Id, currentLayoutId)));
        }

        return new PresentationLayoutPickerPlan(
            LayoutCommandId,
            currentLayoutId,
            currentSlide is not null,
            choices);
    }

    public static bool TryApplyLayoutChoice(
        EditingSession editor,
        string layoutId,
        out PresentationLayoutChoice? choice)
    {
        ArgumentNullException.ThrowIfNull(editor);

        choice = null;
        if (string.IsNullOrWhiteSpace(layoutId))
        {
            return false;
        }

        var pickerPlan = BuildLayoutPickerPlan(editor.Presentation, editor.CurrentSlideIndex);
        choice = pickerPlan.Choices.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.LayoutId, layoutId));

        return choice is not null && editor.SetCurrentSlideLayout(choice.LayoutId);
    }

    public static bool TryApply(
        EditingSession editor,
        PresentationDesignCommandPlan plan,
        Action<PresentationDesignCommandPlan>? onHostRequest = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(plan);

        switch (plan.Intent)
        {
            case PresentationDesignCommandIntentKind.SetTheme:
                if (string.IsNullOrWhiteSpace(plan.ThemeId))
                {
                    return false;
                }

                editor.SetTheme(plan.ThemeId);
                return true;

            case PresentationDesignCommandIntentKind.SetSlideSize:
                if (plan.SlideSizeCxEmu is not { } cxEmu ||
                    plan.SlideSizeCyEmu is not { } cyEmu ||
                    cxEmu <= 0 ||
                    cyEmu <= 0)
                {
                    return false;
                }

                editor.SetSlideSize(cxEmu, cyEmu);
                return true;

            case PresentationDesignCommandIntentKind.RequestCustomSlideSize:
            case PresentationDesignCommandIntentKind.RequestLayoutPicker:
                if (onHostRequest is null)
                {
                    return false;
                }

                onHostRequest(plan);
                return true;

            default:
                return false;
        }
    }

    private static string BuildLayoutDisplayName(SlideLayout layout)
    {
        if (!string.IsNullOrWhiteSpace(layout.Name))
        {
            return layout.Name;
        }

        return layout.LayoutType switch
        {
            SlideLayoutType.Title => "Title Slide",
            SlideLayoutType.TitleContent => "Title and Content",
            SlideLayoutType.TitleOnly => "Title Only",
            SlideLayoutType.Blank => "Blank",
            SlideLayoutType.TwoContent => "Two Content",
            SlideLayoutType.Comparison => "Comparison",
            SlideLayoutType.ContentCaption => "Content with Caption",
            SlideLayoutType.PictureCaption => "Picture with Caption",
            _ => "Custom Layout",
        };
    }
}
