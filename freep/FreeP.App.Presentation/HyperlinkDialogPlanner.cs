using Free.Shared.AppServices;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum HyperlinkDialogTargetKind
{
    Url,
    Slide
}

public enum HyperlinkDialogField
{
    None,
    Url,
    Slide
}

public sealed record HyperlinkDialogInitialState(
    HyperlinkDialogTargetKind TargetKind,
    string UrlText,
    string? TargetSlideId,
    string TooltipText);

public sealed record HyperlinkDialogSlideOption(
    string Id,
    string DisplayText)
{
    public override string ToString() => DisplayText;
}

public sealed record HyperlinkDialogRequest(
    IReadOnlyList<HyperlinkDialogSlideOption> SlideOptions,
    HyperlinkDialogInitialState InitialState,
    int SelectedSlideIndex);

public sealed record HyperlinkDialogValidationMessage(
    string Caption,
    string Message,
    HyperlinkDialogField FocusField);

public sealed record HyperlinkDialogResultPlan(
    bool ShouldApply,
    Hyperlink? Result,
    HyperlinkDialogValidationMessage? Validation);

public sealed record HyperlinkDialogApplyPlan(
    bool ShouldApply,
    string? Url,
    string? TargetSlideId,
    string? Tooltip);

public sealed record HyperlinkDialogViewState(
    HyperlinkDialogTargetKind TargetKind,
    string UrlText,
    int SelectedSlideIndex,
    string TooltipText,
    bool IsUrlInputEnabled,
    bool IsSlideInputEnabled,
    string ValidationText);

/// <summary>
/// Renderer-neutral state and acceptance workflow for the hyperlink dialog.
/// Hosts retain native controls, event/focus wiring, validation presentation, and window closing.
/// </summary>
public sealed class HyperlinkDialogSession
{
    public HyperlinkDialogSession(HyperlinkDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SlideOptions);
        ArgumentNullException.ThrowIfNull(request.InitialState);

        SlideOptions = request.SlideOptions.ToArray();
        var initial = request.InitialState;
        var (isUrlInputEnabled, isSlideInputEnabled) = InputEnablement(initial.TargetKind);
        State = new HyperlinkDialogViewState(
            initial.TargetKind,
            initial.UrlText ?? string.Empty,
            request.SelectedSlideIndex,
            initial.TooltipText ?? string.Empty,
            isUrlInputEnabled,
            isSlideInputEnabled,
            string.Empty);
    }

    public IReadOnlyList<HyperlinkDialogSlideOption> SlideOptions { get; }

    public HyperlinkDialogViewState State { get; private set; }

    public Hyperlink? Result { get; private set; }

    public HyperlinkDialogResultPlan? LastResultPlan { get; private set; }

    public HyperlinkDialogViewState SelectTarget(HyperlinkDialogTargetKind targetKind)
    {
        var (isUrlInputEnabled, isSlideInputEnabled) = InputEnablement(targetKind);
        State = State with
        {
            TargetKind = targetKind,
            IsUrlInputEnabled = isUrlInputEnabled,
            IsSlideInputEnabled = isSlideInputEnabled,
        };
        return State;
    }

    public void SetUrlText(string? urlText)
        => State = State with { UrlText = urlText ?? string.Empty };

    public void SelectSlide(int selectedSlideIndex)
        => State = State with { SelectedSlideIndex = selectedSlideIndex };

    public void SetTooltipText(string? tooltipText)
        => State = State with { TooltipText = tooltipText ?? string.Empty };

    public HyperlinkDialogViewState SetInput(
        HyperlinkDialogTargetKind targetKind,
        string? urlText,
        int selectedSlideIndex,
        string? tooltipText)
    {
        var (isUrlInputEnabled, isSlideInputEnabled) = InputEnablement(targetKind);
        State = State with
        {
            TargetKind = targetKind,
            UrlText = urlText ?? string.Empty,
            SelectedSlideIndex = selectedSlideIndex,
            TooltipText = tooltipText ?? string.Empty,
            IsUrlInputEnabled = isUrlInputEnabled,
            IsSlideInputEnabled = isSlideInputEnabled,
        };
        return State;
    }

    public HyperlinkDialogResultPlan TryAccept()
    {
        var selectedSlideId = State.SelectedSlideIndex >= 0
            && State.SelectedSlideIndex < SlideOptions.Count
                ? SlideOptions[State.SelectedSlideIndex].Id
                : null;
        var plan = HyperlinkDialogPlanner.BuildResult(
            State.TargetKind,
            State.UrlText,
            selectedSlideId,
            State.TooltipText);

        LastResultPlan = plan;
        Result = plan.ShouldApply ? plan.Result : null;
        State = State with { ValidationText = plan.Validation?.Message ?? string.Empty };
        return plan;
    }

    private static (bool IsUrlInputEnabled, bool IsSlideInputEnabled) InputEnablement(
        HyperlinkDialogTargetKind targetKind)
        => targetKind switch
        {
            HyperlinkDialogTargetKind.Url => (true, false),
            HyperlinkDialogTargetKind.Slide => (false, true),
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, null),
        };
}

public static class HyperlinkDialogPlanner
{
    public const string Caption = "Insert Hyperlink";
    public const string MissingUrlMessage =
        "Please enter a URL (e.g. https://example.com).";
    public const string UnsupportedUrlMessage =
        "Only http, https, mailto, and local file URLs are supported.";
    public const string MissingSlideMessage =
        "Please select a target slide.";

    public static HyperlinkDialogRequest BuildDialogRequest(
        IReadOnlyList<Slide> slides,
        Hyperlink? current)
    {
        var options = BuildSlideOptions(slides);
        var initial = BuildInitialState(current);
        return new HyperlinkDialogRequest(
            options,
            initial,
            SelectedSlideIndex(options, initial.TargetSlideId));
    }

    public static IReadOnlyList<HyperlinkDialogSlideOption> BuildSlideOptions(
        IReadOnlyList<Slide> slides)
    {
        ArgumentNullException.ThrowIfNull(slides);

        var options = new List<HyperlinkDialogSlideOption>(slides.Count);
        for (int i = 0; i < slides.Count; i++)
        {
            var slide = slides[i];
            var title = NullIfWhiteSpace(slide.Title) ?? $"Slide {i + 1}";
            options.Add(new HyperlinkDialogSlideOption(
                slide.Id,
                $"{i + 1}. {title}"));
        }

        return options;
    }

    public static HyperlinkDialogInitialState BuildInitialState(Hyperlink? current)
    {
        if (current is null)
        {
            return new HyperlinkDialogInitialState(
                HyperlinkDialogTargetKind.Url,
                string.Empty,
                null,
                string.Empty);
        }

        return current.IsExternal
            ? new HyperlinkDialogInitialState(
                HyperlinkDialogTargetKind.Url,
                current.Url ?? string.Empty,
                null,
                current.Tooltip ?? string.Empty)
            : new HyperlinkDialogInitialState(
                HyperlinkDialogTargetKind.Slide,
                string.Empty,
                current.TargetSlideId,
                current.Tooltip ?? string.Empty);
    }

    public static HyperlinkDialogResultPlan BuildResult(
        HyperlinkDialogTargetKind targetKind,
        string? urlText,
        string? selectedSlideId,
        string? tooltipText)
    {
        return targetKind switch
        {
            HyperlinkDialogTargetKind.Url => BuildUrlResult(urlText, tooltipText),
            HyperlinkDialogTargetKind.Slide => BuildSlideResult(selectedSlideId, tooltipText),
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, null)
        };
    }

    public static HyperlinkDialogApplyPlan BuildApplyPlan(Hyperlink? result)
    {
        return result is null
            ? new HyperlinkDialogApplyPlan(false, null, null, null)
            : new HyperlinkDialogApplyPlan(
                true,
                result.Url,
                result.TargetSlideId,
                result.Tooltip);
    }

    public static string? NullIfWhiteSpace(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    public static bool IsSupportedExternalUrl(string url)
    {
        return ExternalUriLauncher.TryCreateAllowedUri(url, out var uri)
            && uri.Scheme is "http" or "https" or "mailto" or "file";
    }

    public static int SelectedSlideIndex(
        IReadOnlyList<HyperlinkDialogSlideOption> options,
        string? targetSlideId)
    {
        if (options.Count == 0)
        {
            return -1;
        }

        if (!string.IsNullOrWhiteSpace(targetSlideId))
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Id == targetSlideId)
                {
                    return i;
                }
            }
        }

        return 0;
    }

    private static HyperlinkDialogResultPlan BuildUrlResult(
        string? urlText,
        string? tooltipText)
    {
        var url = urlText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            return Invalid(MissingUrlMessage, HyperlinkDialogField.Url);
        }

        if (!IsSupportedExternalUrl(url))
        {
            return Invalid(UnsupportedUrlMessage, HyperlinkDialogField.Url);
        }

        return new HyperlinkDialogResultPlan(
            true,
            new Hyperlink
            {
                Url = url,
                Tooltip = NullIfWhiteSpace(tooltipText)
            },
            null);
    }

    private static HyperlinkDialogResultPlan BuildSlideResult(
        string? selectedSlideId,
        string? tooltipText)
    {
        var slideId = NullIfWhiteSpace(selectedSlideId);
        if (slideId is null)
        {
            return Invalid(MissingSlideMessage, HyperlinkDialogField.Slide);
        }

        return new HyperlinkDialogResultPlan(
            true,
            new Hyperlink
            {
                TargetSlideId = slideId,
                Tooltip = NullIfWhiteSpace(tooltipText)
            },
            null);
    }

    private static HyperlinkDialogResultPlan Invalid(
        string message,
        HyperlinkDialogField focusField)
        => new(
            false,
            null,
            new HyperlinkDialogValidationMessage(Caption, message, focusField));
}
