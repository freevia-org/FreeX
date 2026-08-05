using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ZoomTargetOption(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record ZoomTargetMovePlan(int FromIndex, int ToIndex, string TargetId);

public sealed class ZoomSingleTargetDialogSession
{
    private readonly IReadOnlyList<ZoomTargetOption> _options;

    public ZoomSingleTargetDialogSession(
        IReadOnlyList<(string Id, string DisplayName)> options,
        string? selectedTargetId = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options
            .Select(option => new ZoomTargetOption(option.Id, option.DisplayName))
            .ToArray();
        InitialSelectedIndex = FindSelectedIndex(_options, selectedTargetId);
    }

    public IReadOnlyList<ZoomTargetOption> Options => _options;

    public int InitialSelectedIndex { get; }

    public bool CanAccept => _options.Count > 0;

    public string? SelectedTargetId { get; private set; }

    public bool TryAccept(int selectedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= _options.Count
            || string.IsNullOrWhiteSpace(_options[selectedIndex].Id))
            return false;

        SelectedTargetId = _options[selectedIndex].Id;
        return true;
    }

    private static int FindSelectedIndex(
        IReadOnlyList<ZoomTargetOption> options,
        string? selectedTargetId)
    {
        if (options.Count == 0)
            return -1;

        for (var index = 0; index < options.Count; index++)
        {
            if (string.Equals(
                    options[index].Id,
                    selectedTargetId,
                    StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return 0;
    }
}

public sealed class SummaryZoomDialogSession
{
    private readonly List<ZoomTargetOption> _options;

    public SummaryZoomDialogSession(
        IReadOnlyList<(string Id, string DisplayName)> options,
        IReadOnlyCollection<string>? selectedTargetIds = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options
            .Select(option => new ZoomTargetOption(option.Id, option.DisplayName))
            .ToList();
        InitialSelectedTargetIds = SummaryZoomTargetPlanner.SelectOrderedTargets(
            _options.Select(option => option.Id),
            selectedTargetIds ?? Array.Empty<string>());
    }

    public IReadOnlyList<ZoomTargetOption> Options => _options;

    public IReadOnlyList<string> InitialSelectedTargetIds { get; }

    public IReadOnlyList<string> SelectedTargetIds { get; private set; } = Array.Empty<string>();

    public bool CanAccept => _options.Count >= 2;

    public bool TryMoveSelected(
        IReadOnlyCollection<string>? selectedTargetIds,
        int delta,
        out ZoomTargetMovePlan? plan)
    {
        plan = null;
        if (selectedTargetIds is not { Count: 1 } || delta == 0)
            return false;

        var selectedTargetId = selectedTargetIds.First();
        var index = _options.FindIndex(option =>
            string.Equals(option.Id, selectedTargetId, StringComparison.OrdinalIgnoreCase));
        var targetIndex = index + delta;
        if (index < 0 || targetIndex < 0 || targetIndex >= _options.Count)
            return false;

        var selected = _options[index];
        _options.RemoveAt(index);
        _options.Insert(targetIndex, selected);
        plan = new ZoomTargetMovePlan(index, targetIndex, selected.Id);
        return true;
    }

    public bool TryAccept(IEnumerable<string>? selectedTargetIds)
    {
        var selected = SummaryZoomTargetPlanner.SelectOrderedTargets(
            _options.Select(option => option.Id),
            selectedTargetIds ?? Array.Empty<string>());
        if (selected.Count < 2)
            return false;

        SelectedTargetIds = selected;
        return true;
    }
}

public enum ZoomObjectPropertiesDialogField
{
    TransitionDuration,
    FrameBorderColor,
    FrameBorderWidth,
    FrameBorderDash,
    FrameBorderGradient,
    FrameBorderPattern,
    FrameBorderShadow,
    FrameGeometry,
    CropEdges,
    SummaryTileLayout,
}

public enum ZoomObjectPropertiesBorderMode
{
    Gradient,
    Pattern,
    NoFill,
    Theme,
}

public sealed record ZoomObjectPropertiesDialogValidation(
    ZoomObjectPropertiesDialogField Field,
    string Message);

public sealed record ZoomObjectPropertiesBorderModePlan(
    bool GradientEnabled,
    bool PatternEnabled,
    bool NoFillEnabled,
    bool ThemeEnabled);

public sealed record ZoomObjectPropertiesDialogEnablement(
    bool TransitionDuration,
    bool FrameBorderColor,
    bool FrameBorderWidth,
    bool FrameBorderDash,
    bool FrameBorderGradientToggle,
    bool FrameBorderGradientFields,
    bool FrameBorderPatternToggle,
    bool FrameBorderPatternFields,
    bool FrameBorderNoFillToggle,
    bool FrameBorderThemeToggle,
    bool FrameBorderThemeColor,
    bool FrameBorderShadowToggle,
    bool FrameBorderShadowFields);

public sealed record ZoomObjectPropertiesDialogFields(
    bool ReturnToParent,
    bool ShowBackground,
    string ImageType,
    bool TransitionEnabled,
    string TransitionDuration,
    bool FrameBorderEnabled,
    string FrameBorderColor,
    ThemeColorSlot? FrameBorderThemeColor,
    bool FrameBorderThemeEnabled,
    string FrameBorderWidth,
    OutlineDash FrameBorderDash,
    bool FrameBorderGradientEnabled,
    string FrameBorderGradientStart,
    string FrameBorderGradientEnd,
    string FrameBorderGradientAngle,
    bool FrameBorderPatternEnabled,
    string FrameBorderPatternPreset,
    string FrameBorderPatternForeground,
    string FrameBorderPatternBackground,
    bool FrameBorderNoFillEnabled,
    bool FrameBorderShadowEnabled,
    string FrameBorderShadowColor,
    string FrameBorderShadowAlpha,
    string FrameBorderShadowBlur,
    string FrameBorderShadowDistance,
    string FrameBorderShadowDirection,
    string FrameGeometry,
    string CropEdges,
    string SummaryOffset,
    string SummaryScale);

public sealed record ZoomObjectPropertiesDialogInput(
    bool ReturnToParent,
    bool ShowBackground,
    string? ImageType,
    bool TransitionEnabled,
    string? TransitionDuration,
    bool FrameBorderEnabled,
    string? FrameBorderColor,
    ThemeColorSlot? FrameBorderThemeColor,
    bool FrameBorderThemeEnabled,
    string? FrameBorderWidth,
    string? FrameBorderDash,
    bool FrameBorderGradientEnabled,
    string? FrameBorderGradientStart,
    string? FrameBorderGradientEnd,
    string? FrameBorderGradientAngle,
    bool FrameBorderPatternEnabled,
    string? FrameBorderPatternPreset,
    string? FrameBorderPatternForeground,
    string? FrameBorderPatternBackground,
    bool FrameBorderNoFillEnabled,
    bool FrameBorderShadowEnabled,
    string? FrameBorderShadowColor,
    string? FrameBorderShadowAlpha,
    string? FrameBorderShadowBlur,
    string? FrameBorderShadowDistance,
    string? FrameBorderShadowDirection,
    string? FrameGeometry,
    string? CropEdges,
    int SummaryTileIndex,
    string? SummaryOffset,
    string? SummaryScale,
    bool ApplySummaryPropertiesToAllTiles);

public sealed record ZoomObjectPropertiesDialogResult(
    ZoomObjectProperties Properties,
    ZoomObjectPropertiesPlanner.SummaryZoomTileLayoutEdit? SummaryTileLayout,
    ZoomObjectPropertiesPlanner.SummaryZoomTilePropertiesEdit? SummaryTileProperties,
    bool ApplySummaryPropertiesToAllTiles);

public sealed class ZoomObjectPropertiesDialogSession
{
    private readonly ZoomObjectProperties _current;
    private readonly IReadOnlyList<SummaryZoomTarget> _summaryTargets;
    private readonly IReadOnlyList<ZoomObjectProperties> _summaryTileProperties;

    public ZoomObjectPropertiesDialogSession(
        ZoomObjectProperties current,
        IReadOnlyList<SummaryZoomTarget>? summaryTargets = null,
        IReadOnlyList<ZoomObjectProperties>? summaryTileProperties = null)
    {
        ArgumentNullException.ThrowIfNull(current);

        _current = current;
        _summaryTargets = summaryTargets ?? Array.Empty<SummaryZoomTarget>();
        _summaryTileProperties = summaryTileProperties is { Count: var count }
            && count == _summaryTargets.Count
            ? summaryTileProperties
            : Enumerable.Repeat(current, _summaryTargets.Count).ToArray();
        SummaryTargetOptions = _summaryTargets
            .Select(target => new ZoomTargetOption(
                target.SectionId,
                string.IsNullOrWhiteSpace(target.Title) ? target.SectionId : target.Title))
            .ToArray();
        Result = new ZoomObjectPropertiesDialogResult(current, null, null, false);
    }

    public IReadOnlyList<ZoomTargetOption> SummaryTargetOptions { get; }

    public bool HasSummaryTargets => _summaryTargets.Count > 0;

    public ZoomObjectPropertiesDialogResult Result { get; private set; }

    public ZoomObjectPropertiesDialogFields InitialFields => BuildFields(0);

    public bool TryBuildSummaryTileFields(
        int summaryTileIndex,
        out ZoomObjectPropertiesDialogFields? fields)
    {
        if (summaryTileIndex < 0 || summaryTileIndex >= _summaryTargets.Count)
        {
            fields = null;
            return false;
        }

        fields = BuildFields(summaryTileIndex);
        return true;
    }

    public bool TryAccept(
        ZoomObjectPropertiesDialogInput input,
        out ZoomObjectPropertiesDialogValidation? validation)
    {
        validation = null;
        if (!ZoomObjectPropertiesPlanner.TryParseTransitionDuration(
                input.TransitionDuration,
                input.TransitionEnabled,
                out var transitionDuration))
            return Invalid(
                ZoomObjectPropertiesDialogField.TransitionDuration,
                ZoomObjectPropertiesPlanner.InvalidTransitionDurationMessage,
                out validation);

        var noFillEnabled = input.FrameBorderEnabled && input.FrameBorderNoFillEnabled;
        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderColor(
                input.FrameBorderColor,
                input.FrameBorderEnabled
                && !input.FrameBorderGradientEnabled
                && !input.FrameBorderPatternEnabled
                && !input.FrameBorderThemeEnabled
                && !noFillEnabled,
                out var frameBorderColor))
            return Invalid(
                ZoomObjectPropertiesDialogField.FrameBorderColor,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderColorMessage,
                out validation);

        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderWidth(
                input.FrameBorderWidth,
                input.FrameBorderEnabled,
                out var frameBorderWidth))
            return Invalid(
                ZoomObjectPropertiesDialogField.FrameBorderWidth,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderWidthMessage,
                out validation);

        var frameBorderDashText = input.FrameBorderEnabled ? input.FrameBorderDash : null;
        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderDash(
                frameBorderDashText,
                out var frameBorderDash))
            return Invalid(
                ZoomObjectPropertiesDialogField.FrameBorderDash,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderDashMessage,
                out validation);

        var gradientEnabled = input.FrameBorderEnabled
            && input.FrameBorderGradientEnabled
            && !noFillEnabled;
        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderGradient(
                input.FrameBorderGradientStart,
                input.FrameBorderGradientEnd,
                input.FrameBorderGradientAngle,
                gradientEnabled,
                out var frameBorderGradient))
            return Invalid(
                ZoomObjectPropertiesDialogField.FrameBorderGradient,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderGradientMessage,
                out validation);
        if (gradientEnabled)
            frameBorderColor = null;

        var patternEnabled = input.FrameBorderEnabled
            && input.FrameBorderPatternEnabled
            && !noFillEnabled;
        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderPattern(
                input.FrameBorderPatternPreset,
                input.FrameBorderPatternForeground,
                input.FrameBorderPatternBackground,
                patternEnabled,
                out var frameBorderPattern))
            return Invalid(
                ZoomObjectPropertiesDialogField.FrameBorderPattern,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderPatternMessage,
                out validation);
        if (patternEnabled)
        {
            frameBorderColor = null;
            frameBorderGradient = null;
        }

        if (noFillEnabled)
        {
            frameBorderColor = null;
            frameBorderGradient = null;
            frameBorderPattern = null;
        }

        var themeColor = input.FrameBorderEnabled
            && input.FrameBorderThemeEnabled
            && !noFillEnabled
            ? input.FrameBorderThemeColor
            : null;
        if (themeColor is not null)
        {
            frameBorderColor = null;
            frameBorderGradient = null;
            frameBorderPattern = null;
        }

        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderShadow(
                input.FrameBorderShadowColor,
                input.FrameBorderShadowAlpha,
                input.FrameBorderShadowBlur,
                input.FrameBorderShadowDistance,
                input.FrameBorderShadowDirection,
                input.FrameBorderShadowEnabled,
                out var frameBorderShadow))
            return Invalid(
                ZoomObjectPropertiesDialogField.FrameBorderShadow,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderShadowMessage,
                out validation);

        if (!ZoomObjectPropertiesPlanner.TryParseFrameGeometry(
                input.FrameGeometry,
                out var frameGeometry))
            return Invalid(
                ZoomObjectPropertiesDialogField.FrameGeometry,
                ZoomObjectPropertiesPlanner.InvalidFrameGeometryMessage,
                out validation);

        if (!ZoomObjectPropertiesPlanner.TryParseCropEdges(
                input.CropEdges,
                out var cropLeft,
                out var cropTop,
                out var cropRight,
                out var cropBottom))
            return Invalid(
                ZoomObjectPropertiesDialogField.CropEdges,
                ZoomObjectPropertiesPlanner.InvalidCropEdgesMessage,
                out validation);

        ZoomObjectPropertiesPlanner.SummaryZoomTileLayoutEdit? summaryTileLayout = null;
        if (_summaryTargets.Count > 0)
        {
            if (input.SummaryTileIndex < 0 || input.SummaryTileIndex >= _summaryTargets.Count
                || !ZoomObjectPropertiesPlanner.TryParseFactorPair(
                    input.SummaryOffset,
                    allowNegative: true,
                    out var offsetX,
                    out var offsetY)
                || !ZoomObjectPropertiesPlanner.TryParseFactorPair(
                    input.SummaryScale,
                    allowNegative: false,
                    out var scaleX,
                    out var scaleY))
                return Invalid(
                    ZoomObjectPropertiesDialogField.SummaryTileLayout,
                    ZoomObjectPropertiesPlanner.InvalidSummaryTileLayoutMessage,
                    out validation);

            summaryTileLayout = new ZoomObjectPropertiesPlanner.SummaryZoomTileLayoutEdit(
                _summaryTargets[input.SummaryTileIndex].SectionId,
                offsetX,
                offsetY,
                scaleX,
                scaleY);
        }

        var properties = new ZoomObjectProperties(
            input.ReturnToParent,
            NormalizeImageType(input.ImageType),
            transitionDuration,
            input.ShowBackground,
            cropLeft,
            cropTop,
            cropRight,
            cropBottom,
            frameBorderColor,
            frameBorderWidth,
            frameBorderDash,
            frameGeometry,
            frameBorderGradient,
            frameBorderPattern,
            noFillEnabled ? true : null,
            themeColor,
            frameBorderShadow,
            input.FrameBorderShadowEnabled ? true : false);

        ZoomObjectPropertiesPlanner.SummaryZoomTilePropertiesEdit? summaryTileProperties = null;
        var applyToAll = _summaryTargets.Count > 0 && input.ApplySummaryPropertiesToAllTiles;
        if (_summaryTargets.Count > 0 && !applyToAll)
        {
            summaryTileProperties = new ZoomObjectPropertiesPlanner.SummaryZoomTilePropertiesEdit(
                _summaryTargets[input.SummaryTileIndex].SectionId,
                properties);
        }

        Result = new ZoomObjectPropertiesDialogResult(
            properties,
            summaryTileLayout,
            summaryTileProperties,
            applyToAll);
        return true;
    }

    public static ZoomObjectPropertiesBorderModePlan SelectExclusiveBorderMode(
        ZoomObjectPropertiesBorderMode mode) =>
        new(
            GradientEnabled: mode == ZoomObjectPropertiesBorderMode.Gradient,
            PatternEnabled: mode == ZoomObjectPropertiesBorderMode.Pattern,
            NoFillEnabled: mode == ZoomObjectPropertiesBorderMode.NoFill,
            ThemeEnabled: mode == ZoomObjectPropertiesBorderMode.Theme);

    public static ZoomObjectPropertiesDialogEnablement BuildEnablement(
        bool transitionEnabled,
        bool frameBorderEnabled,
        bool gradientEnabled,
        bool patternEnabled,
        bool noFillEnabled,
        bool themeEnabled,
        bool shadowEnabled)
    {
        var noFill = frameBorderEnabled && noFillEnabled;
        var gradient = frameBorderEnabled && gradientEnabled && !noFill;
        var pattern = frameBorderEnabled && patternEnabled && !noFill;
        var theme = frameBorderEnabled && themeEnabled && !noFill;
        return new ZoomObjectPropertiesDialogEnablement(
            TransitionDuration: transitionEnabled,
            FrameBorderColor: frameBorderEnabled && !gradient && !pattern && !noFill && !theme,
            FrameBorderWidth: frameBorderEnabled,
            FrameBorderDash: frameBorderEnabled,
            FrameBorderGradientToggle: frameBorderEnabled,
            FrameBorderGradientFields: gradient,
            FrameBorderPatternToggle: frameBorderEnabled,
            FrameBorderPatternFields: pattern,
            FrameBorderNoFillToggle: frameBorderEnabled,
            FrameBorderThemeToggle: frameBorderEnabled,
            FrameBorderThemeColor: theme,
            FrameBorderShadowToggle: frameBorderEnabled,
            FrameBorderShadowFields: frameBorderEnabled && shadowEnabled);
    }

    private ZoomObjectPropertiesDialogFields BuildFields(int summaryTileIndex)
    {
        var hasSummaryTile = summaryTileIndex >= 0 && summaryTileIndex < _summaryTargets.Count;
        var properties = hasSummaryTile ? _summaryTileProperties[summaryTileIndex] : _current;
        var target = hasSummaryTile ? _summaryTargets[summaryTileIndex] : null;

        return new ZoomObjectPropertiesDialogFields(
            ReturnToParent: properties.ReturnToParent ?? true,
            ShowBackground: properties.ShowBackground ?? true,
            ImageType: NormalizeImageType(properties.ImageType),
            TransitionEnabled: ZoomObjectPropertiesPlanner.IsTransitionEnabled(properties),
            TransitionDuration: properties.TransitionDuration ?? string.Empty,
            FrameBorderEnabled: ZoomObjectPropertiesPlanner.IsFrameBorderEnabled(properties),
            FrameBorderColor: properties.FrameBorderColor ?? string.Empty,
            FrameBorderThemeColor: properties.FrameBorderThemeColor,
            FrameBorderThemeEnabled: ZoomObjectPropertiesPlanner.IsFrameBorderThemeColorEnabled(properties),
            FrameBorderWidth: ZoomObjectPropertiesPlanner.FormatFrameBorderWidth(properties),
            FrameBorderDash: properties.FrameBorderDash ?? OutlineDash.Solid,
            FrameBorderGradientEnabled: ZoomObjectPropertiesPlanner.IsFrameBorderGradientEnabled(properties),
            FrameBorderGradientStart: ZoomObjectPropertiesPlanner.FormatFrameBorderGradientStart(properties),
            FrameBorderGradientEnd: ZoomObjectPropertiesPlanner.FormatFrameBorderGradientEnd(properties),
            FrameBorderGradientAngle: ZoomObjectPropertiesPlanner.FormatFrameBorderGradientAngle(properties),
            FrameBorderPatternEnabled: ZoomObjectPropertiesPlanner.IsFrameBorderPatternEnabled(properties),
            FrameBorderPatternPreset: ZoomObjectPropertiesPlanner.FormatFrameBorderPatternPreset(properties),
            FrameBorderPatternForeground: ZoomObjectPropertiesPlanner.FormatFrameBorderPatternForeground(properties),
            FrameBorderPatternBackground: ZoomObjectPropertiesPlanner.FormatFrameBorderPatternBackground(properties),
            FrameBorderNoFillEnabled: ZoomObjectPropertiesPlanner.IsFrameBorderNoFillEnabled(properties),
            FrameBorderShadowEnabled: ZoomObjectPropertiesPlanner.IsFrameBorderShadowEnabled(properties),
            FrameBorderShadowColor: ZoomObjectPropertiesPlanner.FormatFrameBorderShadowColor(properties),
            FrameBorderShadowAlpha: ZoomObjectPropertiesPlanner.FormatFrameBorderShadowAlpha(properties),
            FrameBorderShadowBlur: ZoomObjectPropertiesPlanner.FormatFrameBorderShadowBlur(properties),
            FrameBorderShadowDistance: ZoomObjectPropertiesPlanner.FormatFrameBorderShadowDistance(properties),
            FrameBorderShadowDirection: ZoomObjectPropertiesPlanner.FormatFrameBorderShadowDirection(properties),
            FrameGeometry: ZoomObjectPropertiesPlanner.FrameGeometryOptions.FirstOrDefault(
                geometry => string.Equals(geometry, properties.FrameGeometry, StringComparison.OrdinalIgnoreCase))
                ?? "rect",
            CropEdges: ZoomObjectPropertiesPlanner.FormatCropEdges(properties),
            SummaryOffset: target is null
                ? string.Empty
                : ZoomObjectPropertiesPlanner.FormatFactorPair(target.OffsetFactorX, target.OffsetFactorY),
            SummaryScale: target is null
                ? string.Empty
                : ZoomObjectPropertiesPlanner.FormatFactorPair(target.ScaleFactorX, target.ScaleFactorY));
    }

    private static string NormalizeImageType(string? imageType) =>
        ZoomObjectPropertiesPlanner.IsSupportedImageType(imageType)
            ? imageType!.ToLowerInvariant()
            : "preview";

    private static bool Invalid(
        ZoomObjectPropertiesDialogField field,
        string message,
        out ZoomObjectPropertiesDialogValidation? validation)
    {
        validation = new ZoomObjectPropertiesDialogValidation(field, message);
        return false;
    }
}
