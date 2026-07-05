using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum HeaderFooterCommandFocus
{
    HeaderFooter,
    DateTime,
    Footer,
    SlideNumber
}

public enum HeaderFooterApplyScope
{
    CurrentSlide,
    AllSlides
}

public sealed record HeaderFooterState(
    bool ShowDateTime,
    bool ShowFooter,
    bool ShowSlideNumber,
    string FooterText,
    bool HasDateTimePlaceholder,
    bool HasFooterPlaceholder,
    bool HasSlideNumberPlaceholder);

public sealed record HeaderFooterApplyOptions(
    bool ShowDateTime,
    bool ShowFooter,
    bool ShowSlideNumber,
    string FooterText,
    HeaderFooterApplyScope Scope);

public sealed record HeaderFooterApplyPlan(
    bool ShouldApply,
    HeaderFooterApplyOptions Options,
    IReadOnlyList<int> TargetSlideIndexes,
    string? Limitation);

public static class HeaderFooterCommandPlanner
{
    public const string HeaderFooterCommandId = "freep.header-footer";
    public const string DateTimeCommandId = "freep.date-time";
    public const string SlideNumberCommandId = "freep.slide-number";

    public const string PlaceholderCreationEvidence =
        "Creates missing slide footer/date/slide-number placeholders with shared fallback geometry; PowerPoint-authoritative visual/layout baselines remain deferred.";

    public static HeaderFooterState BuildState(EditingSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        return BuildState(editor.Presentation, editor.CurrentSlideIndex);
    }

    public static HeaderFooterState BuildState(Presentation presentation, int slideIndex)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        if (slideIndex < 0 || slideIndex >= presentation.Slides.Count)
        {
            return new(false, false, false, string.Empty, false, false, false);
        }

        var slide = presentation.Slides[slideIndex];
        var hasDate = HasHeaderFooterShape(slide, HeaderFooterFieldKind.DateTime);
        var hasFooter = HasHeaderFooterShape(slide, HeaderFooterFieldKind.Footer);
        var hasSlideNumber = HasHeaderFooterShape(slide, HeaderFooterFieldKind.SlideNumber);
        var flags = slide.HfVisibility;

        return new(
            flags?.ShowDate ?? hasDate,
            flags?.ShowFooter ?? hasFooter,
            flags?.ShowSlideNum ?? hasSlideNumber,
            FindFooterText(slide),
            hasDate,
            hasFooter,
            hasSlideNumber);
    }

    public static HeaderFooterApplyOptions BuildDefaultOptions(
        HeaderFooterState state,
        HeaderFooterCommandFocus focus)
    {
        return focus switch
        {
            HeaderFooterCommandFocus.DateTime => new(
                ShowDateTime: true,
                state.ShowFooter,
                state.ShowSlideNumber,
                state.FooterText,
                HeaderFooterApplyScope.CurrentSlide),
            HeaderFooterCommandFocus.SlideNumber => new(
                state.ShowDateTime,
                state.ShowFooter,
                ShowSlideNumber: true,
                state.FooterText,
                HeaderFooterApplyScope.CurrentSlide),
            HeaderFooterCommandFocus.Footer => new(
                state.ShowDateTime,
                ShowFooter: true,
                state.ShowSlideNumber,
                state.FooterText,
                HeaderFooterApplyScope.CurrentSlide),
            _ => new(
                state.ShowDateTime,
                state.ShowFooter,
                state.ShowSlideNumber,
                state.FooterText,
                HeaderFooterApplyScope.CurrentSlide)
        };
    }

    public static HeaderFooterApplyPlan BuildApplyPlan(
        Presentation presentation,
        int currentSlideIndex,
        HeaderFooterApplyOptions options)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        var targets = ResolveTargets(presentation, currentSlideIndex, options.Scope);
        return new(
            targets.Count > 0,
            options with { FooterText = options.FooterText ?? string.Empty },
            targets,
            null);
    }

    public static bool TryApply(
        EditingSession editor,
        HeaderFooterApplyOptions options,
        out HeaderFooterApplyPlan plan)
    {
        ArgumentNullException.ThrowIfNull(editor);
        plan = BuildApplyPlan(editor.Presentation, editor.CurrentSlideIndex, options);
        if (!plan.ShouldApply)
        {
            return false;
        }

        editor.Bus.Execute(new ApplyHeaderFooterCommand(plan));
        return true;
    }

    public static bool IsVisibleByHeaderFooterFlags(SlideShape shape, Slide slide)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(slide);

        var kind = GetHeaderFooterKind(shape);
        if (kind is HeaderFooterFieldKind.None || slide.HfVisibility is not { } flags)
        {
            return true;
        }

        return kind switch
        {
            HeaderFooterFieldKind.DateTime => flags.ShowDate,
            HeaderFooterFieldKind.Footer => flags.ShowFooter,
            HeaderFooterFieldKind.SlideNumber => flags.ShowSlideNum,
            _ => true
        };
    }

    private static IReadOnlyList<int> ResolveTargets(
        Presentation presentation,
        int currentSlideIndex,
        HeaderFooterApplyScope scope)
    {
        if (presentation.Slides.Count == 0)
        {
            return Array.Empty<int>();
        }

        if (scope == HeaderFooterApplyScope.AllSlides)
        {
            return Enumerable.Range(0, presentation.Slides.Count).ToArray();
        }

        return currentSlideIndex >= 0 && currentSlideIndex < presentation.Slides.Count
            ? [currentSlideIndex]
            : Array.Empty<int>();
    }

    private static void ApplyToSlide(Presentation presentation, Slide slide, HeaderFooterApplyOptions options)
    {
        slide.HfVisibility = new HfFlags
        {
            ShowDate = options.ShowDateTime,
            ShowFooter = options.ShowFooter,
            ShowSlideNum = options.ShowSlideNumber,
            ShowHeader = slide.HfVisibility?.ShowHeader ?? false
        };

        var nextShapeId = NextShapeId(slide);
        if (options.ShowDateTime)
        {
            var dateShape = EnsureHeaderFooterShape(
                presentation,
                slide,
                HeaderFooterFieldKind.DateTime,
                ref nextShapeId);
            EnsureSingleFieldRun(dateShape, "datetime1", string.Empty);
        }

        if (options.ShowFooter)
        {
            var footerShape = EnsureHeaderFooterShape(
                presentation,
                slide,
                HeaderFooterFieldKind.Footer,
                ref nextShapeId);
            ApplyFooterText(footerShape, options.FooterText);
        }

        if (options.ShowSlideNumber)
        {
            var slideNumberShape = EnsureHeaderFooterShape(
                presentation,
                slide,
                HeaderFooterFieldKind.SlideNumber,
                ref nextShapeId);
            EnsureSingleFieldRun(slideNumberShape, "slidenum", string.Empty);
        }

        foreach (var shape in Flatten(slide.Shapes))
        {
            switch (GetHeaderFooterKind(shape))
            {
                case HeaderFooterFieldKind.DateTime when options.ShowDateTime:
                    EnsureSingleFieldRun(shape, "datetime1", string.Empty);
                    break;
                case HeaderFooterFieldKind.Footer:
                    ApplyFooterText(shape, options.FooterText);
                    break;
                case HeaderFooterFieldKind.SlideNumber when options.ShowSlideNumber:
                    EnsureSingleFieldRun(shape, "slidenum", string.Empty);
                    break;
            }
        }
    }

    private static SlideShape EnsureHeaderFooterShape(
        Presentation presentation,
        Slide slide,
        HeaderFooterFieldKind kind,
        ref uint nextShapeId)
    {
        var existing = FindHeaderFooterShape(slide, kind);
        if (existing is not null)
        {
            return existing;
        }

        var shape = CreateHeaderFooterShape(
            presentation,
            slide,
            kind,
            nextShapeId);
        slide.Shapes.Add(shape);
        nextShapeId = nextShapeId >= uint.MaxValue ? uint.MaxValue : nextShapeId + 1;
        return shape;
    }

    private static SlideShape CreateHeaderFooterShape(
        Presentation presentation,
        Slide slide,
        HeaderFooterFieldKind kind,
        uint shapeId)
    {
        var shape = new SlideShape
        {
            Id = shapeId,
            Name = HeaderFooterShapeName(kind, shapeId),
            Kind = SlideShapeKind.AutoShape,
            Placeholder = new Placeholder
            {
                Type = ToPlaceholderType(kind),
                Idx = HeaderFooterPlaceholderIndex(kind)
            },
            TextBody = new TextBody()
        };

        if (!HasInheritedHeaderFooterGeometry(presentation, slide, shape.Placeholder))
        {
            ApplyFallbackGeometry(presentation, kind, shape);
        }

        return shape;
    }

    private static bool HasInheritedHeaderFooterGeometry(
        Presentation presentation,
        Slide slide,
        Placeholder placeholder)
    {
        var layoutShape = PlaceholderResolver.FindLayoutPlaceholder(placeholder, slide, presentation);
        if (layoutShape is not null && (layoutShape.ExtentCxEmu > 0 || layoutShape.ExtentCyEmu > 0))
        {
            return true;
        }

        var masterShape = PlaceholderResolver.FindMasterPlaceholder(placeholder, slide, presentation);
        return masterShape is not null && (masterShape.ExtentCxEmu > 0 || masterShape.ExtentCyEmu > 0);
    }

    private static void ApplyFallbackGeometry(
        Presentation presentation,
        HeaderFooterFieldKind kind,
        SlideShape shape)
    {
        var slideWidth = Math.Max(1, presentation.SlideSizeCxEmu);
        var slideHeight = Math.Max(1, presentation.SlideSizeCyEmu);
        var marginX = Math.Max(1, slideWidth / 20);
        var marginBottom = Math.Max(1, slideHeight / 18);
        var height = Math.Max(1, slideHeight / 18);
        var footerTop = Math.Max(0, slideHeight - marginBottom - height);
        var columnGap = Math.Max(1, slideWidth / 80);
        var sideWidth = Math.Max(1, slideWidth / 4);
        var centerWidth = Math.Max(1, slideWidth - (marginX * 2) - (sideWidth * 2) - (columnGap * 2));

        shape.OffsetYEmu = footerTop;
        shape.ExtentCyEmu = height;

        switch (kind)
        {
            case HeaderFooterFieldKind.DateTime:
                shape.OffsetXEmu = marginX;
                shape.ExtentCxEmu = sideWidth;
                break;
            case HeaderFooterFieldKind.SlideNumber:
                shape.OffsetXEmu = Math.Max(0, slideWidth - marginX - sideWidth);
                shape.ExtentCxEmu = sideWidth;
                break;
            case HeaderFooterFieldKind.Footer:
                shape.OffsetXEmu = marginX + sideWidth + columnGap;
                shape.ExtentCxEmu = centerWidth;
                break;
        }
    }

    private static uint NextShapeId(Slide slide)
    {
        var max = MaxShapeId(slide.Shapes);
        return max >= uint.MaxValue ? uint.MaxValue : max + 1;
    }

    private static uint MaxShapeId(IEnumerable<SlideShape> shapes)
    {
        uint max = 0;
        foreach (var shape in shapes)
        {
            if (shape.Id > max)
            {
                max = shape.Id;
            }

            var childMax = MaxShapeId(shape.Children);
            if (childMax > max)
            {
                max = childMax;
            }
        }

        return max;
    }

    private static void ApplyFooterText(SlideShape shape, string footerText)
    {
        if (shape.TextBody is null || !ContainsFieldKind(shape.TextBody, HeaderFooterFieldKind.Footer))
        {
            EnsureSingleFieldRun(shape, "footer", footerText);
            return;
        }

        foreach (var run in shape.TextBody.Paragraphs.SelectMany(paragraph => paragraph.Runs))
        {
            if (run.Field is { } field && ClassifyFieldType(field.FieldType) == HeaderFooterFieldKind.Footer)
            {
                field.CachedText = footerText;
                run.Text = footerText;
            }
        }
    }

    private static void EnsureSingleFieldRun(SlideShape shape, string fieldType, string cachedText)
    {
        shape.TextBody ??= new TextBody();
        if (shape.TextBody.Paragraphs.Count == 0)
        {
            shape.TextBody.Paragraphs.Add(new Paragraph());
        }

        var paragraph = shape.TextBody.Paragraphs[0];
        paragraph.Runs.Clear();
        paragraph.Runs.Add(new Run
        {
            Text = cachedText,
            Field = new FieldRun
            {
                FieldType = fieldType,
                CachedText = cachedText
            }
        });
    }

    private static string FindFooterText(Slide slide)
    {
        foreach (var shape in Flatten(slide.Shapes))
        {
            if (GetHeaderFooterKind(shape) != HeaderFooterFieldKind.Footer)
            {
                continue;
            }

            var fieldText = shape.TextBody?.Paragraphs
                .SelectMany(paragraph => paragraph.Runs)
                .Where(run => run.Field is not null &&
                    ClassifyFieldType(run.Field.FieldType) == HeaderFooterFieldKind.Footer)
                .Select(run => !string.IsNullOrEmpty(run.Field!.CachedText) ? run.Field.CachedText : run.Text)
                .FirstOrDefault(text => !string.IsNullOrEmpty(text));
            if (fieldText is not null)
            {
                return fieldText;
            }

            if (!string.IsNullOrEmpty(shape.PlainText))
            {
                return shape.PlainText;
            }
        }

        return string.Empty;
    }

    private static bool HasHeaderFooterShape(Slide slide, HeaderFooterFieldKind kind) =>
        Flatten(slide.Shapes).Any(shape => GetHeaderFooterKind(shape) == kind);

    private static SlideShape? FindHeaderFooterShape(Slide slide, HeaderFooterFieldKind kind) =>
        Flatten(slide.Shapes).FirstOrDefault(shape => GetHeaderFooterKind(shape) == kind);

    private static PlaceholderType ToPlaceholderType(HeaderFooterFieldKind kind) => kind switch
    {
        HeaderFooterFieldKind.DateTime => PlaceholderType.DateTime,
        HeaderFooterFieldKind.Footer => PlaceholderType.Footer,
        HeaderFooterFieldKind.SlideNumber => PlaceholderType.SlideNumber,
        _ => PlaceholderType.Body
    };

    private static int HeaderFooterPlaceholderIndex(HeaderFooterFieldKind kind) => kind switch
    {
        HeaderFooterFieldKind.DateTime => 10,
        HeaderFooterFieldKind.Footer => 11,
        HeaderFooterFieldKind.SlideNumber => 12,
        _ => 0
    };

    private static string HeaderFooterShapeName(HeaderFooterFieldKind kind, uint shapeId) => kind switch
    {
        HeaderFooterFieldKind.DateTime => $"Date Placeholder {shapeId}",
        HeaderFooterFieldKind.Footer => $"Footer Placeholder {shapeId}",
        HeaderFooterFieldKind.SlideNumber => $"Slide Number Placeholder {shapeId}",
        _ => $"Placeholder {shapeId}"
    };

    private static HeaderFooterFieldKind GetHeaderFooterKind(SlideShape shape)
    {
        var placeholderKind = shape.Placeholder?.Type switch
        {
            PlaceholderType.DateTime => HeaderFooterFieldKind.DateTime,
            PlaceholderType.Footer => HeaderFooterFieldKind.Footer,
            PlaceholderType.SlideNumber => HeaderFooterFieldKind.SlideNumber,
            _ => HeaderFooterFieldKind.None
        };
        if (placeholderKind != HeaderFooterFieldKind.None)
        {
            return placeholderKind;
        }

        return shape.TextBody is null
            ? HeaderFooterFieldKind.None
            : FirstFieldKind(shape.TextBody);
    }

    private static HeaderFooterFieldKind FirstFieldKind(TextBody textBody) =>
        textBody.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Field is null
                ? HeaderFooterFieldKind.None
                : ClassifyFieldType(run.Field.FieldType))
            .FirstOrDefault(kind => kind != HeaderFooterFieldKind.None);

    private static bool ContainsFieldKind(TextBody textBody, HeaderFooterFieldKind kind) =>
        textBody.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Any(run => run.Field is not null && ClassifyFieldType(run.Field.FieldType) == kind);

    private static HeaderFooterFieldKind ClassifyFieldType(string? fieldType)
    {
        var text = (fieldType ?? string.Empty).Trim().ToLowerInvariant();
        if (text.Contains("slidenum", StringComparison.Ordinal) ||
            text == "\\slidenum" ||
            text == "ppslidenum")
        {
            return HeaderFooterFieldKind.SlideNumber;
        }

        if (text.StartsWith("datetime", StringComparison.Ordinal) ||
            text == "date" ||
            text == "time")
        {
            return HeaderFooterFieldKind.DateTime;
        }

        if (text == "footer" || text == "ftr")
        {
            return HeaderFooterFieldKind.Footer;
        }

        return HeaderFooterFieldKind.None;
    }

    private static IEnumerable<SlideShape> Flatten(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            yield return shape;
            foreach (var child in Flatten(shape.Children))
            {
                yield return child;
            }
        }
    }

    private sealed class ApplyHeaderFooterCommand : IPresentationCommand
    {
        private readonly HeaderFooterApplyPlan _plan;
        private readonly Dictionary<int, Slide> _before = new();
        private readonly Dictionary<int, Slide> _after = new();

        public ApplyHeaderFooterCommand(HeaderFooterApplyPlan plan)
        {
            _plan = plan;
        }

        public string Label => "Apply Header and Footer";

        public bool HasEffect(Presentation presentation) =>
            _plan.TargetSlideIndexes.Any(index => index >= 0 && index < presentation.Slides.Count);

        public void Apply(Presentation presentation)
        {
            foreach (var index in _plan.TargetSlideIndexes)
            {
                if (index < 0 || index >= presentation.Slides.Count)
                {
                    continue;
                }

                if (!_before.ContainsKey(index))
                {
                    _before[index] = SlideCloner.CloneSlide(presentation.Slides[index]);
                    var updated = SlideCloner.CloneSlide(presentation.Slides[index]);
                    ApplyToSlide(presentation, updated, _plan.Options);
                    _after[index] = updated;
                }

                presentation.Slides[index] = SlideCloner.CloneSlide(_after[index]);
            }
        }

        public void Revert(Presentation presentation)
        {
            foreach (var (index, slide) in _before)
            {
                if (index >= 0 && index < presentation.Slides.Count)
                {
                    presentation.Slides[index] = SlideCloner.CloneSlide(slide);
                }
            }
        }
    }

    private enum HeaderFooterFieldKind
    {
        None,
        DateTime,
        Footer,
        SlideNumber
    }
}
