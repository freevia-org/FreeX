using Free.Shared.Drawing;
using Free.Shared.Theme;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationDesignCommandIntentKind
{
    SetTheme,
    SetSlideSize,
    SetSlideBackground,
    RequestCustomSlideSize,
    RequestLayoutPicker,
}

public sealed record PresentationDesignCommandPlan(
    string CommandId,
    PresentationDesignCommandIntentKind Intent,
    string? ThemeId = null,
    long? SlideSizeCxEmu = null,
    long? SlideSizeCyEmu = null,
    int? BackgroundRgb = null);

public enum PresentationLayoutChoiceChromeState
{
    Available,
    Current,
    Disabled
}

public sealed record PresentationLayoutChoiceChrome(
    PresentationLayoutChoiceChromeState State,
    bool IsCurrent,
    bool IsEnabled,
    string BorderBrushHex,
    string BackgroundBrushHex,
    double BorderThicknessDip,
    string BadgeText);

public enum PresentationLayoutPlaceholderCategory
{
    Title,
    Content,
}

public sealed record PresentationLayoutPlaceholderVisualSpec(
    PresentationLayoutPlaceholderCategory Category,
    string FillBrushHex,
    string StrokeBrushHex,
    double StrokeThicknessDip,
    double CornerRadiusDip);

public sealed record PresentationLayoutPickerVisualSpec(
    string ThumbnailBackgroundBrushHex,
    string ThumbnailBorderBrushHex,
    double ThumbnailBorderThicknessDip,
    string BadgeForegroundBrushHex,
    PresentationLayoutPlaceholderVisualSpec TitlePlaceholder,
    PresentationLayoutPlaceholderVisualSpec ContentPlaceholder)
{
    public PresentationLayoutPlaceholderVisualSpec ResolvePlaceholder(PlaceholderType type) =>
        type is PlaceholderType.Title or PlaceholderType.CenteredTitle or PlaceholderType.SubTitle
            ? TitlePlaceholder
            : ContentPlaceholder;
}

public sealed record PresentationLayoutThumbnailPlaceholder(
    PlaceholderType PlaceholderType,
    string RoleLabel,
    LayoutRect Bounds)
{
    public PresentationLayoutPlaceholderVisualSpec Visual =>
        PresentationDesignCommandPlanner.LayoutPickerVisuals.ResolvePlaceholder(PlaceholderType);
}

public sealed record PresentationLayoutChoice(
    string LayoutId,
    string DisplayName,
    SlideLayoutType LayoutType,
    bool IsCurrent,
    string? MasterId,
    string MasterDisplayName,
    int PlaceholderCount,
    int DisplayOrder)
{
    public string GroupKey { get; init; } = string.Empty;
    public string GroupHeading { get; init; } = MasterDisplayName;
    public IReadOnlyList<PresentationLayoutThumbnailPlaceholder> ThumbnailPlaceholders { get; init; } =
        Array.Empty<PresentationLayoutThumbnailPlaceholder>();
    public PresentationLayoutChoiceChrome Chrome { get; init; } =
        new(
            PresentationLayoutChoiceChromeState.Available,
            false,
            true,
            "#D0D0D0",
            "#FFFFFF",
            1,
            string.Empty);

    public string AutomationId => $"layout-{LayoutId}";

    public string DisplayLabel
    {
        get
        {
            var currentPrefix = IsCurrent ? "Current - " : string.Empty;
            var placeholders = PlaceholderCount == 1
                ? "1 placeholder"
                : $"{PlaceholderCount} placeholders";
            return $"{currentPrefix}{DisplayName}\n{MasterDisplayName} - {placeholders}";
        }
    }
}

public sealed record PresentationLayoutGroup(
    string GroupKey,
    string Heading,
    IReadOnlyList<PresentationLayoutChoice> Choices);

public sealed record PresentationLayoutPickerPlan(
    string CommandId,
    string? CurrentLayoutId,
    bool HasCurrentSlide,
    IReadOnlyList<PresentationLayoutChoice> Choices,
    IReadOnlyList<PresentationLayoutGroup> Groups)
{
    public bool CanApply => HasCurrentSlide && Choices.Count > 0;
}

public static class PresentationDesignCommandPlanner
{
    public const string LayoutCommandId = "freep.layout";
    public const long SlideSizeWidescreen16x9CxEmu = DrawingMlCoordinateUnits.EmuPerInch * 40 / 3;
    public const long SlideSizeStandard4x3CxEmu = DrawingMlCoordinateUnits.EmuPerInch * 10;
    public const long SlideSizeStandardCyEmu = DrawingMlCoordinateUnits.EmuPerInch * 15 / 2;
    public const double LayoutThumbnailWidthDip = 96;
    public const double LayoutThumbnailHeightDip = 54;

    public static readonly PresentationLayoutPickerVisualSpec LayoutPickerVisuals = new(
        ThumbnailBackgroundBrushHex: "#FFFFFF",
        ThumbnailBorderBrushHex: "#D9D9D9",
        ThumbnailBorderThicknessDip: 1,
        BadgeForegroundBrushHex: BrandThemes.FreeP.Colors.Accent.ToHex(),
        TitlePlaceholder: new PresentationLayoutPlaceholderVisualSpec(
            PresentationLayoutPlaceholderCategory.Title,
            FillBrushHex: "#F8DDD1",
            StrokeBrushHex: "#999999",
            StrokeThicknessDip: 1,
            CornerRadiusDip: 1),
        ContentPlaceholder: new PresentationLayoutPlaceholderVisualSpec(
            PresentationLayoutPlaceholderCategory.Content,
            FillBrushHex: "#EAF1F6",
            StrokeBrushHex: "#999999",
            StrokeThicknessDip: 1,
            CornerRadiusDip: 1));

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
            new PresentationDesignCommandPlan(
                "freep.background-white",
                PresentationDesignCommandIntentKind.SetSlideBackground,
                BackgroundRgb: 0xFFFFFF),
            new PresentationDesignCommandPlan(
                "freep.background-black",
                PresentationDesignCommandIntentKind.SetSlideBackground,
                BackgroundRgb: 0x000000),
            new PresentationDesignCommandPlan(
                "freep.background-blue",
                PresentationDesignCommandIntentKind.SetSlideBackground,
                BackgroundRgb: 0xD9EAF7),
            new PresentationDesignCommandPlan(
                "freep.background-reset",
                PresentationDesignCommandIntentKind.SetSlideBackground),
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

            var isCurrent = StringComparer.Ordinal.Equals(layout.Id, currentLayoutId);
            var masterDisplayName = BuildMasterDisplayName(presentation, layout.MasterId);
            choices.Add(new PresentationLayoutChoice(
                layout.Id,
                BuildLayoutDisplayName(layout),
                layout.LayoutType,
                isCurrent,
                layout.MasterId,
                masterDisplayName,
                layout.Placeholders.Count,
                choices.Count)
            {
                GroupKey = BuildLayoutGroupKey(layout.MasterId, masterDisplayName),
                GroupHeading = masterDisplayName,
                ThumbnailPlaceholders = BuildLayoutThumbnailPlaceholders(presentation, layout),
                Chrome = BuildLayoutChoiceChrome(currentSlide is not null, isCurrent),
            });
        }

        return new PresentationLayoutPickerPlan(
            LayoutCommandId,
            currentLayoutId,
            currentSlide is not null,
            choices,
            BuildLayoutGroups(choices));
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

            case PresentationDesignCommandIntentKind.SetSlideBackground:
                editor.SetCurrentSlideBackground(plan.BackgroundRgb is { } rgb
                    ? new ShapeFill.Solid(SrgbColor.FromRgb(rgb))
                    : null);
                return editor.CurrentSlide is not null;

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

    private static string BuildMasterDisplayName(Presentation presentation, string? masterId)
    {
        if (string.IsNullOrWhiteSpace(masterId))
        {
            return "Unknown Master";
        }

        for (var i = 0; i < presentation.Masters.Count; i++)
        {
            if (StringComparer.Ordinal.Equals(presentation.Masters[i].Id, masterId))
            {
                var name = presentation.Masters[i].Name;
                return string.IsNullOrWhiteSpace(name) ? $"Master {i + 1}" : name;
            }
        }

        return "Unknown Master";
    }

    private static string BuildLayoutGroupKey(string? masterId, string masterDisplayName) =>
        string.IsNullOrWhiteSpace(masterId) ? masterDisplayName : masterId;

    private static IReadOnlyList<PresentationLayoutGroup> BuildLayoutGroups(
        IReadOnlyList<PresentationLayoutChoice> choices)
    {
        var groups = new List<PresentationLayoutGroup>();
        foreach (var choice in choices)
        {
            var existingIndex = groups.FindIndex(group =>
                StringComparer.Ordinal.Equals(group.GroupKey, choice.GroupKey));
            if (existingIndex >= 0)
            {
                var existing = groups[existingIndex];
                groups[existingIndex] = existing with
                {
                    Choices = existing.Choices.Concat(new[] { choice }).ToArray(),
                };
                continue;
            }

            groups.Add(new PresentationLayoutGroup(
                choice.GroupKey,
                choice.GroupHeading,
                new[] { choice }));
        }

        return groups;
    }

    private static PresentationLayoutChoiceChrome BuildLayoutChoiceChrome(
        bool hasCurrentSlide,
        bool isCurrent)
    {
        if (!hasCurrentSlide)
        {
            return new PresentationLayoutChoiceChrome(
                PresentationLayoutChoiceChromeState.Disabled,
                false,
                false,
                "#A6A6A6",
                "#F3F3F3",
                1,
                "Unavailable");
        }

        if (isCurrent)
        {
            return new PresentationLayoutChoiceChrome(
                PresentationLayoutChoiceChromeState.Current,
                true,
                false,
                BrandThemes.FreeP.Colors.Accent.ToHex(),
                BrandThemes.FreeP.Colors.AccentSoft.ToHex(),
                2,
                "Current");
        }

        return new PresentationLayoutChoiceChrome(
            PresentationLayoutChoiceChromeState.Available,
            false,
            true,
            "#D0D0D0",
            "#FFFFFF",
            1,
            string.Empty);
    }

    private static IReadOnlyList<PresentationLayoutThumbnailPlaceholder> BuildLayoutThumbnailPlaceholders(
        Presentation presentation,
        SlideLayout layout)
    {
        var fromGeometry = layout.Placeholders
            .Where(shape => shape.Placeholder is not null &&
                            shape.ExtentCxEmu > 0 &&
                            shape.ExtentCyEmu > 0 &&
                            presentation.SlideSizeCxEmu > 0 &&
                            presentation.SlideSizeCyEmu > 0)
            .Select(shape => new PresentationLayoutThumbnailPlaceholder(
                shape.Placeholder!.Type,
                BuildPlaceholderRoleLabel(shape.Placeholder.Type),
                ScalePlaceholderBounds(
                    shape.OffsetXEmu,
                    shape.OffsetYEmu,
                    shape.ExtentCxEmu,
                    shape.ExtentCyEmu,
                    presentation.SlideSizeCxEmu,
                    presentation.SlideSizeCyEmu)))
            .ToArray();

        if (fromGeometry.Length > 0)
            return fromGeometry;

        if (layout.Placeholders.Count > 0)
            return BuildMetadataThumbnailPlaceholders(layout.Placeholders);

        return BuildDefaultThumbnailPlaceholders(layout.LayoutType);
    }

    private static IReadOnlyList<PresentationLayoutThumbnailPlaceholder> BuildMetadataThumbnailPlaceholders(
        IReadOnlyList<SlideShape> placeholders)
    {
        var slots = new List<PresentationLayoutThumbnailPlaceholder>();
        var contentIndex = 0;
        foreach (var shape in placeholders)
        {
            if (shape.Placeholder is not { } placeholder)
                continue;

            var isTitle = placeholder.Type is PlaceholderType.Title or
                PlaceholderType.CenteredTitle or
                PlaceholderType.SubTitle;
            var bounds = isTitle
                ? new LayoutRect(10, 8 + (slots.Count * 9), 76, 7)
                : new LayoutRect(
                    contentIndex++ % 2 == 0 ? 10 : 52,
                    22 + ((contentIndex - 1) / 2 * 13),
                    34,
                    10);
            slots.Add(new PresentationLayoutThumbnailPlaceholder(
                placeholder.Type,
                BuildPlaceholderRoleLabel(placeholder.Type),
                bounds));
        }

        return slots;
    }

    private static IReadOnlyList<PresentationLayoutThumbnailPlaceholder> BuildDefaultThumbnailPlaceholders(
        SlideLayoutType layoutType) =>
        layoutType switch
        {
            SlideLayoutType.Title => new[]
            {
                Thumb(PlaceholderType.CenteredTitle, "Title", 16, 16, 64, 8),
                Thumb(PlaceholderType.SubTitle, "Subtitle", 24, 30, 48, 6),
            },
            SlideLayoutType.TitleOnly => new[]
            {
                Thumb(PlaceholderType.Title, "Title", 10, 10, 76, 9),
            },
            SlideLayoutType.Blank => Array.Empty<PresentationLayoutThumbnailPlaceholder>(),
            SlideLayoutType.TwoContent => new[]
            {
                Thumb(PlaceholderType.Title, "Title", 10, 8, 76, 7),
                Thumb(PlaceholderType.Body, "Content", 10, 21, 34, 24),
                Thumb(PlaceholderType.Body, "Content", 52, 21, 34, 24),
            },
            SlideLayoutType.Comparison => new[]
            {
                Thumb(PlaceholderType.Title, "Title", 10, 7, 76, 7),
                Thumb(PlaceholderType.Title, "Heading", 10, 19, 34, 5),
                Thumb(PlaceholderType.Title, "Heading", 52, 19, 34, 5),
                Thumb(PlaceholderType.Body, "Content", 10, 28, 34, 17),
                Thumb(PlaceholderType.Body, "Content", 52, 28, 34, 17),
            },
            SlideLayoutType.ContentCaption => new[]
            {
                Thumb(PlaceholderType.Title, "Title", 10, 9, 30, 7),
                Thumb(PlaceholderType.Body, "Caption", 10, 22, 30, 20),
                Thumb(PlaceholderType.Body, "Content", 48, 12, 38, 30),
            },
            SlideLayoutType.PictureCaption => new[]
            {
                Thumb(PlaceholderType.Picture, "Picture", 10, 10, 76, 26),
                Thumb(PlaceholderType.Body, "Caption", 18, 41, 60, 5),
            },
            _ => new[]
            {
                Thumb(PlaceholderType.Title, "Title", 10, 8, 76, 7),
                Thumb(PlaceholderType.Body, "Content", 12, 22, 72, 22),
            },
        };

    private static PresentationLayoutThumbnailPlaceholder Thumb(
        PlaceholderType type,
        string roleLabel,
        double x,
        double y,
        double width,
        double height) =>
        new(type, roleLabel, new LayoutRect(x, y, width, height));

    private static LayoutRect ScalePlaceholderBounds(
        long offsetXEmu,
        long offsetYEmu,
        long extentCxEmu,
        long extentCyEmu,
        long slideCxEmu,
        long slideCyEmu)
    {
        var x = Math.Clamp(offsetXEmu / (double)slideCxEmu * LayoutThumbnailWidthDip, 2, LayoutThumbnailWidthDip - 4);
        var y = Math.Clamp(offsetYEmu / (double)slideCyEmu * LayoutThumbnailHeightDip, 2, LayoutThumbnailHeightDip - 4);
        var width = Math.Clamp(extentCxEmu / (double)slideCxEmu * LayoutThumbnailWidthDip, 4, Math.Max(4, LayoutThumbnailWidthDip - x - 2));
        var height = Math.Clamp(extentCyEmu / (double)slideCyEmu * LayoutThumbnailHeightDip, 4, Math.Max(4, LayoutThumbnailHeightDip - y - 2));
        return new LayoutRect(x, y, width, height);
    }

    private static string BuildPlaceholderRoleLabel(PlaceholderType type) =>
        type switch
        {
            PlaceholderType.CenteredTitle or PlaceholderType.Title => "Title",
            PlaceholderType.SubTitle => "Subtitle",
            PlaceholderType.Picture => "Picture",
            PlaceholderType.Chart => "Chart",
            PlaceholderType.Table => "Table",
            PlaceholderType.Media => "Media",
            PlaceholderType.DateTime => "Date",
            PlaceholderType.Footer => "Footer",
            PlaceholderType.SlideNumber => "Number",
            _ => "Content",
        };
}
