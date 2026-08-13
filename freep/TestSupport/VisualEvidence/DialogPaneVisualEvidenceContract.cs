namespace FreeP.App.Compositor;

public enum DialogPaneVisualEvidenceSurfaceKind
{
    Dialog,
    Pane,
    ChoiceOverlay,
}

public sealed record DialogPaneVisualEvidenceScenario(
    string Id,
    string RouteId,
    string StateId,
    DialogPaneVisualEvidenceSurfaceKind SurfaceKind,
    bool CompareFocus = true,
    bool CompareButtons = true,
    bool CompareEnabledState = true);

public sealed record DialogPaneVisualEvidencePixelTarget(double Width, double Height);

public static class DialogPaneVisualEvidenceCatalog
{
    public const int LogicalShellWidth = 1280;
    public const int LogicalShellHeight = 760;
    public const double TargetDpi = 96d;

    public static IReadOnlyList<DialogPaneVisualEvidenceScenario> All { get; } =
    [
        Dialog("design.slide-size", "initial"),
        Dialog("design.slide-size", "invalid"),
        Dialog("insert.header-footer", "date-time"),
        Dialog("insert.header-footer", "apply-to-all"),
        Dialog("home.find-replace", "find"),
        Dialog("home.find-replace", "replace"),

        Dialog("insert.hyperlink", "initial"),
        Dialog("insert.hyperlink", "validation"),
        Dialog("insert.hyperlink", "populated"),
        Dialog("chart.edit-data", "initial"),
        Dialog("chart.edit-data", "validation"),
        Dialog("chart.edit-data", "populated"),
        Dialog("slideshow.custom-shows", "initial"),
        Dialog("slideshow.custom-shows", "validation"),
        Dialog("slideshow.custom-shows", "populated"),

        Pane("startup.slide-pane"),
        Pane("startup.notes-pane"),
        Pane("review.comments-pane"),
        Pane("review.accessibility-pane"),
        Pane("review.alt-text-pane"),
        Pane("review.reading-order-pane"),
        Pane("review.proofing-pane"),
        Pane("accessibility.media-caption-pane"),
        Pane("context.smartart-text-pane"),
        Pane("animations.animation-pane"),
        Pane("file.print-options"),

        Overlay("insert.table-picker"),
        Overlay("design.layout-picker"),
    ];

    public static DialogPaneVisualEvidenceScenario Get(string id) =>
        All.Single(scenario => StringComparer.Ordinal.Equals(scenario.Id, id));

    public static DialogPaneVisualEvidencePixelTarget? PixelTargetFor(DialogPaneVisualEvidenceScenario scenario) =>
        scenario.RouteId switch
        {
            "startup.slide-pane" => new(180, 578),
            "startup.notes-pane" => new(1100, 60),
            "review.comments-pane" => new(1100, 100),
            "review.accessibility-pane" => new(320, 578),
            "review.alt-text-pane" => new(292, 578),
            "review.reading-order-pane" => new(320, 578),
            "review.proofing-pane" => new(320, 578),
            "accessibility.media-caption-pane" => new(320, 578),
            "context.smartart-text-pane" => new(320, 578),
            "animations.animation-pane" => new(240, 578),
            "file.print-options" => new(1010, 578),
            "insert.table-picker" => new(1100, 192),
            "design.layout-picker" => new(1100, 181),
            _ => null,
        };

    private static DialogPaneVisualEvidenceScenario Dialog(string routeId, string stateId) =>
        new($"{routeId}.{stateId}", routeId, stateId, DialogPaneVisualEvidenceSurfaceKind.Dialog);

    private static DialogPaneVisualEvidenceScenario Pane(string routeId) =>
        new($"{routeId}.seeded", routeId, "seeded", DialogPaneVisualEvidenceSurfaceKind.Pane, CompareFocus: false);

    private static DialogPaneVisualEvidenceScenario Overlay(string routeId) =>
        new($"{routeId}.open", routeId, "open", DialogPaneVisualEvidenceSurfaceKind.ChoiceOverlay, CompareFocus: false);
}

public sealed record DialogPaneVisualEvidenceButton(
    string ActionId,
    string Label,
    bool IsEnabled,
    bool IsDefault,
    bool IsCancel);

public sealed record DialogPaneVisualEvidenceControlState(
    string Role,
    string Label,
    bool IsEnabled,
    bool? IsChecked = null,
    bool? IsSelected = null);

public sealed record DialogPaneVisualEvidenceAssertion(
    string Id,
    bool Passed,
    string Detail);

public sealed record DialogPaneVisualEvidenceCapture(
    string ScenarioId,
    string RouteId,
    string StateId,
    string Host,
    string CaptureStatus,
    string ImagePath,
    double LogicalWidth,
    double LogicalHeight,
    int PixelWidth,
    int PixelHeight,
    double DpiX,
    double DpiY,
    long NonBackgroundPixelCount,
    string FocusedRole,
    string FocusedLabel,
    IReadOnlyList<DialogPaneVisualEvidenceButton> Buttons,
    IReadOnlyList<DialogPaneVisualEvidenceControlState> Controls,
    IReadOnlyList<DialogPaneVisualEvidenceAssertion> Assertions,
    IReadOnlyList<string> Limitations,
    double SourceDpiX = DialogPaneVisualEvidenceCatalog.TargetDpi,
    double SourceDpiY = DialogPaneVisualEvidenceCatalog.TargetDpi,
    string RasterNormalization = "logical-96-dpi",
    string PixelComparisonImagePath = "",
    double PixelComparisonLogicalWidth = 0,
    double PixelComparisonLogicalHeight = 0);

public sealed record DialogPaneVisualEvidenceHostManifest(
    int SchemaVersion,
    string Host,
    string CaptureMode,
    double TargetDpi,
    int LogicalShellWidth,
    int LogicalShellHeight,
    string GeneratedAtUtc,
    IReadOnlyList<DialogPaneVisualEvidenceCapture> Captures,
    IReadOnlyList<string> Limitations);

public enum DialogPaneVisualEvidenceClassification
{
    Pass,
    Mismatch,
    Limitation,
}

public sealed record DialogPaneVisualEvidencePixelMetrics(
    int WpfPixelWidth,
    int WpfPixelHeight,
    int AvaloniaPixelWidth,
    int AvaloniaPixelHeight,
    int NormalizedWidth,
    int NormalizedHeight,
    long ComparedPixelCount,
    long ForegroundUnionPixelCount,
    long ChangedPixelCount,
    long ForegroundChangedPixelCount,
    double ChangedPixelRatio,
    double ForegroundChangedPixelRatio,
    double MeanChannelDelta,
    int MaxChannelDelta,
    int ChangedChannelThreshold,
    double MaximumChangedPixelRatio,
    double MaximumForegroundChangedPixelRatio,
    double MaximumMeanChannelDelta,
    bool PixelDimensionsMatch,
    bool ThresholdPassed,
    string BackgroundHandling,
    string HeatmapPath,
    string WpfImageSha256,
    string AvaloniaImageSha256,
    string HeatmapSha256);

public sealed record DialogPaneVisualEvidenceComparison(
    string ScenarioId,
    string RouteId,
    string StateId,
    DialogPaneVisualEvidenceClassification Classification,
    string WpfImagePath,
    string AvaloniaImagePath,
    bool DimensionsMatch,
    bool FocusMatches,
    bool ButtonOrderMatches,
    bool EnabledStateMatches,
    bool WpfNonblank,
    bool AvaloniaNonblank,
    IReadOnlyList<string> Details,
    DialogPaneVisualEvidencePixelMetrics? PixelMetrics = null,
    DialogPaneVisualEvidencePixelMetrics? ShellContextPixelMetrics = null);

public static class DialogPaneVisualEvidenceComparer
{
    public static DialogPaneVisualEvidenceComparison Compare(
        DialogPaneVisualEvidenceScenario scenario,
        DialogPaneVisualEvidenceCapture? wpf,
        DialogPaneVisualEvidenceCapture? avalonia,
        double logicalDimensionTolerance = 2d)
    {
        if (wpf is null || avalonia is null)
        {
            return new DialogPaneVisualEvidenceComparison(
                scenario.Id,
                scenario.RouteId,
                scenario.StateId,
                DialogPaneVisualEvidenceClassification.Limitation,
                wpf?.ImagePath ?? string.Empty,
                avalonia?.ImagePath ?? string.Empty,
                false,
                false,
                false,
                false,
                wpf?.NonBackgroundPixelCount > 0,
                avalonia?.NonBackgroundPixelCount > 0,
                [wpf is null ? "WPF capture is missing." : "Avalonia capture is missing."]);
        }

        var details = new List<string>();
        var dimensionsMatch =
            Math.Abs(wpf.LogicalWidth - avalonia.LogicalWidth) <= logicalDimensionTolerance &&
            Math.Abs(wpf.LogicalHeight - avalonia.LogicalHeight) <= logicalDimensionTolerance;
        var wpfFocusAvailable = !string.IsNullOrWhiteSpace(wpf.FocusedRole);
        var avaloniaFocusAvailable = !string.IsNullOrWhiteSpace(avalonia.FocusedRole);
        var focusUnavailable = scenario.CompareFocus && (!wpfFocusAvailable || !avaloniaFocusAvailable);
        var focusMatches = !scenario.CompareFocus || focusUnavailable ||
            StringComparer.OrdinalIgnoreCase.Equals(wpf.FocusedRole, avalonia.FocusedRole) &&
            StringComparer.OrdinalIgnoreCase.Equals(wpf.FocusedLabel, avalonia.FocusedLabel);
        var buttonOrderMatches = !scenario.CompareButtons ||
            wpf.Buttons.Select(button => button.ActionId)
                .SequenceEqual(avalonia.Buttons.Select(button => button.ActionId), StringComparer.Ordinal);
        var enabledStateMatches = !scenario.CompareEnabledState ||
            ComparableEnabledStates(wpf).SequenceEqual(ComparableEnabledStates(avalonia), StringComparer.Ordinal);
        var wpfNonblank = wpf.NonBackgroundPixelCount > 0;
        var avaloniaNonblank = avalonia.NonBackgroundPixelCount > 0;
        var assertionsPass = wpf.Assertions.All(assertion => assertion.Passed) &&
            avalonia.Assertions.All(assertion => assertion.Passed);

        if (!dimensionsMatch)
            details.Add($"Logical dimensions differ: WPF {wpf.LogicalWidth:0.##}x{wpf.LogicalHeight:0.##}, Avalonia {avalonia.LogicalWidth:0.##}x{avalonia.LogicalHeight:0.##}.");
        if (!focusMatches)
            details.Add($"Focus differs: WPF {wpf.FocusedRole}/{wpf.FocusedLabel}, Avalonia {avalonia.FocusedRole}/{avalonia.FocusedLabel}.");
        if (focusUnavailable)
            details.Add($"Initial focus was unavailable: WPF {FocusDescription(wpf)}, Avalonia {FocusDescription(avalonia)}.");
        if (!buttonOrderMatches)
            details.Add($"Action-button order differs: WPF [{string.Join(", ", wpf.Buttons.Select(button => button.ActionId))}], Avalonia [{string.Join(", ", avalonia.Buttons.Select(button => button.ActionId))}].");
        if (!enabledStateMatches)
            details.Add("Enabled/checked/selected control state differs.");
        if (!wpfNonblank || !avaloniaNonblank)
            details.Add("One or both images failed the non-background pixel check.");
        if (!assertionsPass)
            details.Add("One or more scenario behavior assertions failed.");

        var limitations = wpf.Limitations.Concat(avalonia.Limitations).Distinct(StringComparer.Ordinal).ToArray();
        var captureLimited = focusUnavailable ||
            !StringComparer.Ordinal.Equals(wpf.CaptureStatus, "complete") ||
            !StringComparer.Ordinal.Equals(avalonia.CaptureStatus, "complete") ||
            limitations.Length > 0;
        if (captureLimited)
            details.AddRange(limitations);

        var mismatch = !dimensionsMatch || !focusMatches || !buttonOrderMatches ||
            !enabledStateMatches || !wpfNonblank || !avaloniaNonblank || !assertionsPass;
        var classification = mismatch
            ? DialogPaneVisualEvidenceClassification.Mismatch
            : captureLimited
                ? DialogPaneVisualEvidenceClassification.Limitation
                : DialogPaneVisualEvidenceClassification.Pass;

        return new DialogPaneVisualEvidenceComparison(
            scenario.Id,
            scenario.RouteId,
            scenario.StateId,
            classification,
            wpf.ImagePath,
            avalonia.ImagePath,
            dimensionsMatch,
            focusMatches,
            buttonOrderMatches,
            enabledStateMatches,
            wpfNonblank,
            avaloniaNonblank,
            details);
    }

    private static IEnumerable<string> ComparableEnabledStates(DialogPaneVisualEvidenceCapture capture) =>
        capture.Controls.Select(control =>
            $"{control.Role}|{control.Label}|{control.IsEnabled}|{control.IsChecked}|{control.IsSelected}")
            .OrderBy(state => state, StringComparer.Ordinal);

    private static string FocusDescription(DialogPaneVisualEvidenceCapture capture) =>
        string.IsNullOrWhiteSpace(capture.FocusedRole)
            ? "unavailable"
            : $"{capture.FocusedRole}/{capture.FocusedLabel}";
}
