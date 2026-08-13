using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum ZoomTargetDialogKind
{
    Slide,
    Section,
    Summary,
    SummaryCoverImage,
}

public enum ZoomTargetDialogField
{
    Target,
}

public enum ZoomTargetDialogAction
{
    MoveUp,
    MoveDown,
    Accept,
    Cancel,
}

public static class ZoomTargetDialogSurfaceCatalog
{
    public static PresentationDialogSurfacePlan<ZoomTargetDialogField, ZoomTargetDialogAction> Build(
        ZoomTargetDialogKind kind,
        string? title = null)
    {
        var prefix = $"FreeP.ZoomTarget.{kind}";
        return new(
            title ?? DefaultTitle(kind),
            AccessibleName(kind),
            $"{prefix}.Dialog",
            [
                new PresentationDialogFieldPlan<ZoomTargetDialogField>(
                    ZoomTargetDialogField.Target,
                    kind == ZoomTargetDialogKind.Summary
                        ? PresentationDialogControlKind.List
                        : PresentationDialogControlKind.Choice,
                    TargetLabel(kind),
                    TargetAccessibleName(kind),
                    $"{prefix}.Target",
                    TargetHelpText(kind)),
            ],
            Actions(kind, prefix));
    }

    private static IReadOnlyList<PresentationDialogActionPlan<ZoomTargetDialogAction>> Actions(
        ZoomTargetDialogKind kind,
        string prefix)
    {
        var actions = new List<PresentationDialogActionPlan<ZoomTargetDialogAction>>();
        if (kind == ZoomTargetDialogKind.Summary)
        {
            actions.Add(Action(ZoomTargetDialogAction.MoveUp, "Move Up",
                "Move selected section up", prefix));
            actions.Add(Action(ZoomTargetDialogAction.MoveDown, "Move Down",
                "Move selected section down", prefix));
        }

        actions.Add(Action(ZoomTargetDialogAction.Accept, "OK",
            "Accept zoom target selection", prefix, isDefault: true));
        actions.Add(Action(ZoomTargetDialogAction.Cancel, "Cancel",
            "Cancel zoom target selection", prefix, isCancel: true));
        return actions;
    }

    private static PresentationDialogActionPlan<ZoomTargetDialogAction> Action(
        ZoomTargetDialogAction id,
        string label,
        string accessibleName,
        string prefix,
        bool isDefault = false,
        bool isCancel = false) =>
        new(id, label, accessibleName, $"{prefix}.{id}", isDefault, isCancel);

    private static string DefaultTitle(ZoomTargetDialogKind kind) => kind switch
    {
        ZoomTargetDialogKind.Section => SectionZoomInsertionPlanner.DialogTitle,
        ZoomTargetDialogKind.Summary => SummaryZoomInsertionPlanner.DialogTitle,
        ZoomTargetDialogKind.SummaryCoverImage => ZoomCoverImagePlanner.DialogTitle,
        _ => SlideZoomInsertionPlanner.DialogTitle,
    };

    private static string AccessibleName(ZoomTargetDialogKind kind) => kind switch
    {
        ZoomTargetDialogKind.Section => "Insert Section Zoom",
        ZoomTargetDialogKind.Summary => "Insert Summary Zoom",
        ZoomTargetDialogKind.SummaryCoverImage => "Set Summary Zoom cover image",
        _ => "Insert Slide Zoom",
    };

    private static string TargetLabel(ZoomTargetDialogKind kind) => kind switch
    {
        ZoomTargetDialogKind.Section => "Target section:",
        ZoomTargetDialogKind.Summary => "Target sections (select at least two):",
        ZoomTargetDialogKind.SummaryCoverImage => "Summary Zoom tile:",
        _ => "Target slide:",
    };

    private static string TargetAccessibleName(ZoomTargetDialogKind kind) => kind switch
    {
        ZoomTargetDialogKind.Section => "Target section",
        ZoomTargetDialogKind.Summary => "Target sections",
        ZoomTargetDialogKind.SummaryCoverImage => "Summary Zoom tile",
        _ => "Target slide",
    };

    private static string TargetHelpText(ZoomTargetDialogKind kind) => kind switch
    {
        ZoomTargetDialogKind.Summary =>
            "Select at least two sections. Their list order becomes the Summary Zoom order.",
        ZoomTargetDialogKind.SummaryCoverImage =>
            "Choose the Summary Zoom tile whose preview image will be used.",
        _ => "Choose the destination for the Zoom object.",
    };
}

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
        : this(ZoomTargetDialogKind.Slide, options, selectedTargetId)
    {
    }

    public ZoomSingleTargetDialogSession(
        ZoomTargetDialogKind kind,
        IReadOnlyList<(string Id, string DisplayName)> options,
        string? selectedTargetId = null,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        Kind = kind;
        Surface = ZoomTargetDialogSurfaceCatalog.Build(kind, title);
        _options = options
            .Select(option => new ZoomTargetOption(option.Id, option.DisplayName))
            .ToArray();
        InitialSelectedIndex = FindSelectedIndex(_options, selectedTargetId);
    }

    public IReadOnlyList<ZoomTargetOption> Options => _options;

    public ZoomTargetDialogKind Kind { get; }

    public PresentationDialogSurfacePlan<ZoomTargetDialogField, ZoomTargetDialogAction> Surface { get; }

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
        IReadOnlyCollection<string>? selectedTargetIds = null,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options
            .Select(option => new ZoomTargetOption(option.Id, option.DisplayName))
            .ToList();
        Surface = ZoomTargetDialogSurfaceCatalog.Build(ZoomTargetDialogKind.Summary, title);
        InitialSelectedTargetIds = SummaryZoomTargetPlanner.SelectOrderedTargets(
            _options.Select(option => option.Id),
            selectedTargetIds ?? Array.Empty<string>());
    }

    public IReadOnlyList<ZoomTargetOption> Options => _options;

    public PresentationDialogSurfacePlan<ZoomTargetDialogField, ZoomTargetDialogAction> Surface { get; }

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
    ReturnToParent,
    ShowBackground,
    ImageType,
    TransitionEnabled,
    TransitionDuration,
    FrameBorderEnabled,
    FrameBorderColor,
    FrameBorderThemeColor,
    FrameBorderThemeEnabled,
    FrameBorderWidth,
    FrameBorderDash,
    FrameBorderGradientEnabled,
    FrameBorderGradientStart,
    FrameBorderGradientEnd,
    FrameBorderGradientAngle,
    FrameBorderPatternEnabled,
    FrameBorderPatternPreset,
    FrameBorderPatternForeground,
    FrameBorderPatternBackground,
    FrameBorderNoFillEnabled,
    FrameBorderShadowEnabled,
    FrameBorderShadowColor,
    FrameBorderShadowAlpha,
    FrameBorderShadowBlur,
    FrameBorderShadowDistance,
    FrameBorderShadowDirection,
    FrameBorderGlowEnabled,
    FrameBorderGlowColor,
    FrameBorderGlowAlpha,
    FrameBorderGlowRadius,
    FrameBorderSoftEdgeEnabled,
    FrameBorderSoftEdgeRadius,
    FrameBorderReflectionEnabled,
    FrameBorderReflectionAlpha,
    FrameBorderReflectionBlur,
    FrameBorderReflectionDistance,
    FrameBorderReflectionDirection,
    FrameBorderReflectionScale,
    FrameBorderReflectionEndPosition,
    FrameGeometry,
    CropEdges,
    SummaryTile,
    SummaryOffset,
    SummaryScale,
    ApplySummaryPropertiesToAllTiles,
}

public enum ZoomObjectPropertiesDialogControlKind
{
    Toggle,
    Text,
    Choice,
}

public sealed record ZoomObjectPropertiesDialogControlPlan(
    ZoomObjectPropertiesDialogField Field,
    ZoomObjectPropertiesDialogControlKind Kind,
    string Label,
    IReadOnlyList<object> Options,
    string? PlaceholderText = null,
    string? ToolTipText = null,
    bool SummaryOnly = false)
{
    public string AccessibleName => Label.TrimEnd(':');

    public string AutomationId => $"FreeP.ZoomFormat.{Field}";

    public string? HelpText => ToolTipText ?? PlaceholderText;
}

public sealed record ZoomObjectPropertiesDialogFieldState(
    ZoomObjectPropertiesDialogField Field,
    object? Value,
    bool IsEnabled)
{
    public string TextValue => Value as string ?? Value?.ToString() ?? string.Empty;
}

public sealed record ZoomObjectPropertiesDialogState(
    int SelectedSummaryTileIndex,
    IReadOnlyList<ZoomObjectPropertiesDialogFieldState> Fields)
{
    public ZoomObjectPropertiesDialogFieldState this[ZoomObjectPropertiesDialogField field] =>
        Fields.First(state => state.Field == field);
}

public sealed record ZoomObjectPropertiesDialogAction(
    ZoomObjectPropertiesDialogField Field,
    object? Value);

/// <summary>
/// Owns renderer-neutral zoom-properties field registration, re-entrant state application,
/// dispatch, and validation focus policy. Native hosts map field values to native controls.
/// </summary>
public sealed class ZoomObjectPropertiesDialogFormSession<TControl>
    where TControl : class
{
    private readonly Dictionary<ZoomObjectPropertiesDialogField, TControl> _controls = [];
    private readonly Dictionary<ZoomObjectPropertiesDialogField, bool> _selectAllOnFocus = [];
    private readonly Func<ZoomObjectPropertiesDialogAction, ZoomObjectPropertiesDialogState> _dispatch;
    private readonly Action<TControl, ZoomObjectPropertiesDialogFieldState> _applyFieldState;
    private readonly Action<TControl, bool> _focus;

    public ZoomObjectPropertiesDialogFormSession(
        Func<ZoomObjectPropertiesDialogAction, ZoomObjectPropertiesDialogState> dispatch,
        Action<TControl, ZoomObjectPropertiesDialogFieldState> applyFieldState,
        Action<TControl, bool> focus)
    {
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _applyFieldState = applyFieldState ?? throw new ArgumentNullException(nameof(applyFieldState));
        _focus = focus ?? throw new ArgumentNullException(nameof(focus));
    }

    public bool IsApplyingState { get; private set; }

    public void Register(
        ZoomObjectPropertiesDialogField field,
        TControl control,
        bool selectAllOnFocus)
    {
        ArgumentNullException.ThrowIfNull(control);
        _controls.Add(field, control);
        _selectAllOnFocus.Add(field, selectAllOnFocus);
    }

    public TControl Control(ZoomObjectPropertiesDialogField field) =>
        _controls.TryGetValue(field, out var control)
            ? control
            : throw new KeyNotFoundException($"The zoom properties form does not define {field}.");

    public void Dispatch(ZoomObjectPropertiesDialogField field, object? value)
    {
        if (!IsApplyingState)
            ApplyState(_dispatch(new ZoomObjectPropertiesDialogAction(field, value)));
    }

    public void ApplyState(ZoomObjectPropertiesDialogState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        IsApplyingState = true;
        try
        {
            foreach (var fieldState in state.Fields)
            {
                if (_controls.TryGetValue(fieldState.Field, out var control))
                    _applyFieldState(control, fieldState);
            }
        }
        finally
        {
            IsApplyingState = false;
        }
    }

    public bool Focus(ZoomObjectPropertiesDialogField field)
    {
        if (!_controls.TryGetValue(field, out var control))
            return false;

        _focus(control, _selectAllOnFocus[field]);
        return true;
    }
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
    bool FrameBorderShadowFields,
    bool FrameBorderGlowToggle,
    bool FrameBorderGlowFields,
    bool FrameBorderSoftEdgeToggle,
    bool FrameBorderSoftEdgeFields,
    bool FrameBorderReflectionToggle,
    bool FrameBorderReflectionFields);

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
    bool FrameBorderGlowEnabled,
    string FrameBorderGlowColor,
    string FrameBorderGlowAlpha,
    string FrameBorderGlowRadius,
    bool FrameBorderSoftEdgeEnabled,
    string FrameBorderSoftEdgeRadius,
    bool FrameBorderReflectionEnabled,
    string FrameBorderReflectionAlpha,
    string FrameBorderReflectionBlur,
    string FrameBorderReflectionDistance,
    string FrameBorderReflectionDirection,
    string FrameBorderReflectionScale,
    string FrameBorderReflectionEndPosition,
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
    bool FrameBorderGlowEnabled,
    string? FrameBorderGlowColor,
    string? FrameBorderGlowAlpha,
    string? FrameBorderGlowRadius,
    bool FrameBorderSoftEdgeEnabled,
    string? FrameBorderSoftEdgeRadius,
    bool FrameBorderReflectionEnabled,
    string? FrameBorderReflectionAlpha,
    string? FrameBorderReflectionBlur,
    string? FrameBorderReflectionDistance,
    string? FrameBorderReflectionDirection,
    string? FrameBorderReflectionScale,
    string? FrameBorderReflectionEndPosition,
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
    private readonly HashSet<ZoomObjectPropertiesDialogField> _dirtyFields = [];
    private ZoomObjectProperties _activeProperties;
    private ZoomObjectPropertiesDialogFields _fields;
    private int _selectedSummaryTileIndex;
    private bool _applySummaryPropertiesToAllTiles;

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
        Surface = ZoomObjectPropertiesDialogSurfacePlanner.BuildSurfacePlan();
        FieldCatalog = Surface.FieldCatalog
            .Where(control => !control.SummaryOnly || HasSummaryTargets)
            .Select(control => control.Field == ZoomObjectPropertiesDialogField.SummaryTile
                ? control with { Options = SummaryTargetOptions.Cast<object>().ToArray() }
                : control)
            .ToArray();
        _selectedSummaryTileIndex = HasSummaryTargets ? 0 : -1;
        _activeProperties = ResolveProperties(_selectedSummaryTileIndex);
        _fields = BuildFields(_selectedSummaryTileIndex);
        InitialFields = _fields;
        CommitPlan = new ZoomObjectPropertiesDialogResult(current, null, null, false);
        State = BuildState();
    }

    public IReadOnlyList<ZoomTargetOption> SummaryTargetOptions { get; }

    public bool HasSummaryTargets => _summaryTargets.Count > 0;

    public ZoomObjectPropertiesDialogSurfacePlan Surface { get; }

    public IReadOnlyList<ZoomObjectPropertiesDialogControlPlan> FieldCatalog { get; }

    public ZoomObjectPropertiesDialogState State { get; private set; }

    public ZoomObjectPropertiesDialogResult CommitPlan { get; private set; }

    public ZoomObjectPropertiesDialogResult Result => CommitPlan;

    public ZoomObjectPropertiesDialogFields InitialFields { get; }

    public ZoomObjectPropertiesDialogState Dispatch(ZoomObjectPropertiesDialogAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (action.Field == ZoomObjectPropertiesDialogField.SummaryTile)
        {
            SelectSummaryTile(action.Value);
        }
        else if (action.Field == ZoomObjectPropertiesDialogField.ApplySummaryPropertiesToAllTiles)
        {
            if (action.Value is bool applyToAll)
                _applySummaryPropertiesToAllTiles = applyToAll;
        }
        else if (TryApplyFieldAction(action))
        {
            _dirtyFields.Add(action.Field);
        }

        State = BuildState();
        return State;
    }

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

    public bool TryAccept(out ZoomObjectPropertiesDialogValidation? validation) =>
        TryAcceptCore(BuildInput(), _activeProperties, out validation);

    public bool TryAccept(
        ZoomObjectPropertiesDialogInput input,
        out ZoomObjectPropertiesDialogValidation? validation) =>
        TryAcceptCore(input, preserveUnknownsFrom: null, out validation);

    private bool TryAcceptCore(
        ZoomObjectPropertiesDialogInput input,
        ZoomObjectProperties? preserveUnknownsFrom,
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
                ZoomObjectPropertiesDialogField.FrameBorderGradientStart,
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
                ZoomObjectPropertiesDialogField.FrameBorderPatternPreset,
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
                ZoomObjectPropertiesDialogField.FrameBorderShadowColor,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderShadowMessage,
                out validation);

        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderGlow(
                input.FrameBorderGlowColor,
                input.FrameBorderGlowAlpha,
                input.FrameBorderGlowRadius,
                input.FrameBorderGlowEnabled,
                out var frameBorderGlow))
            return Invalid(
                ZoomObjectPropertiesDialogField.FrameBorderGlowColor,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderGlowMessage,
                out validation);

        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderSoftEdge(
                input.FrameBorderSoftEdgeRadius,
                input.FrameBorderSoftEdgeEnabled,
                out var frameBorderSoftEdge))
            return Invalid(
                ZoomObjectPropertiesDialogField.FrameBorderSoftEdgeRadius,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderSoftEdgeMessage,
                out validation);

        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderReflection(
                input.FrameBorderReflectionAlpha,
                input.FrameBorderReflectionDistance,
                input.FrameBorderReflectionDirection,
                input.FrameBorderReflectionScale,
                input.FrameBorderReflectionBlur,
                input.FrameBorderReflectionEndPosition,
                input.FrameBorderReflectionEnabled,
                out var frameBorderReflection))
            return Invalid(
                ZoomObjectPropertiesDialogField.FrameBorderReflectionAlpha,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderReflectionMessage,
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
                    ZoomObjectPropertiesDialogField.SummaryScale,
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
            input.FrameBorderShadowEnabled ? true : false,
            frameBorderGlow,
            input.FrameBorderGlowEnabled ? true : false,
            frameBorderSoftEdge,
            input.FrameBorderSoftEdgeEnabled ? true : false,
            frameBorderReflection,
            input.FrameBorderReflectionEnabled ? true : false);
        if (preserveUnknownsFrom is not null)
            properties = PreserveUntouchedUnknowns(preserveUnknownsFrom, properties);

        ZoomObjectPropertiesPlanner.SummaryZoomTilePropertiesEdit? summaryTileProperties = null;
        var applyToAll = _summaryTargets.Count > 0 && input.ApplySummaryPropertiesToAllTiles;
        if (_summaryTargets.Count > 0 && !applyToAll)
        {
            summaryTileProperties = new ZoomObjectPropertiesPlanner.SummaryZoomTilePropertiesEdit(
                _summaryTargets[input.SummaryTileIndex].SectionId,
                properties);
        }

        CommitPlan = new ZoomObjectPropertiesDialogResult(
            properties,
            summaryTileLayout,
            summaryTileProperties,
            applyToAll);
        return true;
    }

    private bool TryApplyFieldAction(ZoomObjectPropertiesDialogAction action)
    {
        if (action.Value is bool toggle)
        {
            switch (action.Field)
            {
                case ZoomObjectPropertiesDialogField.ReturnToParent:
                    _fields = _fields with { ReturnToParent = toggle };
                    return true;
                case ZoomObjectPropertiesDialogField.ShowBackground:
                    _fields = _fields with { ShowBackground = toggle };
                    return true;
                case ZoomObjectPropertiesDialogField.TransitionEnabled:
                    _fields = _fields with { TransitionEnabled = toggle };
                    return true;
                case ZoomObjectPropertiesDialogField.FrameBorderEnabled:
                    _fields = _fields with { FrameBorderEnabled = toggle };
                    return true;
                case ZoomObjectPropertiesDialogField.FrameBorderThemeEnabled:
                case ZoomObjectPropertiesDialogField.FrameBorderGradientEnabled:
                case ZoomObjectPropertiesDialogField.FrameBorderPatternEnabled:
                case ZoomObjectPropertiesDialogField.FrameBorderNoFillEnabled:
                    ApplyExclusiveBorderMode(action.Field, toggle);
                    return true;
                case ZoomObjectPropertiesDialogField.FrameBorderShadowEnabled:
                    _fields = _fields with { FrameBorderShadowEnabled = toggle };
                    return true;
                case ZoomObjectPropertiesDialogField.FrameBorderGlowEnabled:
                    _fields = _fields with { FrameBorderGlowEnabled = toggle };
                    return true;
                case ZoomObjectPropertiesDialogField.FrameBorderSoftEdgeEnabled:
                    _fields = _fields with { FrameBorderSoftEdgeEnabled = toggle };
                    return true;
                case ZoomObjectPropertiesDialogField.FrameBorderReflectionEnabled:
                    _fields = _fields with { FrameBorderReflectionEnabled = toggle };
                    return true;
            }
        }

        if (action.Field == ZoomObjectPropertiesDialogField.FrameBorderThemeColor)
        {
            if (action.Value is not null and not ThemeColorSlot)
                return false;

            _fields = _fields with
            {
                FrameBorderThemeColor = action.Value is ThemeColorSlot slot ? slot : null,
            };
            return true;
        }

        if (action.Field == ZoomObjectPropertiesDialogField.FrameBorderDash)
        {
            if (action.Value is not OutlineDash dash)
                return false;

            _fields = _fields with { FrameBorderDash = dash };
            return true;
        }

        var text = action.Value?.ToString() ?? string.Empty;
        switch (action.Field)
        {
            case ZoomObjectPropertiesDialogField.ImageType:
                _fields = _fields with { ImageType = text };
                return true;
            case ZoomObjectPropertiesDialogField.TransitionDuration:
                _fields = _fields with { TransitionDuration = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderColor:
                _fields = _fields with { FrameBorderColor = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderWidth:
                _fields = _fields with { FrameBorderWidth = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderGradientStart:
                _fields = _fields with { FrameBorderGradientStart = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderGradientEnd:
                _fields = _fields with { FrameBorderGradientEnd = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderGradientAngle:
                _fields = _fields with { FrameBorderGradientAngle = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderPatternPreset:
                _fields = _fields with { FrameBorderPatternPreset = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderPatternForeground:
                _fields = _fields with { FrameBorderPatternForeground = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderPatternBackground:
                _fields = _fields with { FrameBorderPatternBackground = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderShadowColor:
                _fields = _fields with { FrameBorderShadowColor = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderShadowAlpha:
                _fields = _fields with { FrameBorderShadowAlpha = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderShadowBlur:
                _fields = _fields with { FrameBorderShadowBlur = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderShadowDistance:
                _fields = _fields with { FrameBorderShadowDistance = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderShadowDirection:
                _fields = _fields with { FrameBorderShadowDirection = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderGlowColor:
                _fields = _fields with { FrameBorderGlowColor = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderGlowAlpha:
                _fields = _fields with { FrameBorderGlowAlpha = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderGlowRadius:
                _fields = _fields with { FrameBorderGlowRadius = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderSoftEdgeRadius:
                _fields = _fields with { FrameBorderSoftEdgeRadius = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderReflectionAlpha:
                _fields = _fields with { FrameBorderReflectionAlpha = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderReflectionBlur:
                _fields = _fields with { FrameBorderReflectionBlur = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderReflectionDistance:
                _fields = _fields with { FrameBorderReflectionDistance = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderReflectionDirection:
                _fields = _fields with { FrameBorderReflectionDirection = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderReflectionScale:
                _fields = _fields with { FrameBorderReflectionScale = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameBorderReflectionEndPosition:
                _fields = _fields with { FrameBorderReflectionEndPosition = text };
                return true;
            case ZoomObjectPropertiesDialogField.FrameGeometry:
                _fields = _fields with { FrameGeometry = text };
                return true;
            case ZoomObjectPropertiesDialogField.CropEdges:
                _fields = _fields with { CropEdges = text };
                return true;
            case ZoomObjectPropertiesDialogField.SummaryOffset:
                _fields = _fields with { SummaryOffset = text };
                return true;
            case ZoomObjectPropertiesDialogField.SummaryScale:
                _fields = _fields with { SummaryScale = text };
                return true;
            default:
                return false;
        }
    }

    private void ApplyExclusiveBorderMode(ZoomObjectPropertiesDialogField field, bool enabled)
    {
        if (!enabled)
        {
            _fields = field switch
            {
                ZoomObjectPropertiesDialogField.FrameBorderGradientEnabled =>
                    _fields with { FrameBorderGradientEnabled = false },
                ZoomObjectPropertiesDialogField.FrameBorderPatternEnabled =>
                    _fields with { FrameBorderPatternEnabled = false },
                ZoomObjectPropertiesDialogField.FrameBorderNoFillEnabled =>
                    _fields with { FrameBorderNoFillEnabled = false },
                ZoomObjectPropertiesDialogField.FrameBorderThemeEnabled =>
                    _fields with { FrameBorderThemeEnabled = false },
                _ => _fields,
            };
            return;
        }

        var mode = field switch
        {
            ZoomObjectPropertiesDialogField.FrameBorderGradientEnabled =>
                ZoomObjectPropertiesBorderMode.Gradient,
            ZoomObjectPropertiesDialogField.FrameBorderPatternEnabled =>
                ZoomObjectPropertiesBorderMode.Pattern,
            ZoomObjectPropertiesDialogField.FrameBorderNoFillEnabled =>
                ZoomObjectPropertiesBorderMode.NoFill,
            _ => ZoomObjectPropertiesBorderMode.Theme,
        };
        var plan = SelectExclusiveBorderMode(mode);
        _fields = _fields with
        {
            FrameBorderGradientEnabled = plan.GradientEnabled,
            FrameBorderPatternEnabled = plan.PatternEnabled,
            FrameBorderNoFillEnabled = plan.NoFillEnabled,
            FrameBorderThemeEnabled = plan.ThemeEnabled,
        };
    }

    private void SelectSummaryTile(object? value)
    {
        var index = value switch
        {
            int selectedIndex => selectedIndex,
            ZoomTargetOption option => FindSummaryTargetIndex(option.Id),
            string sectionId => FindSummaryTargetIndex(sectionId),
            _ => -1,
        };
        if (index < 0 || index >= _summaryTargets.Count || index == _selectedSummaryTileIndex)
            return;

        _selectedSummaryTileIndex = index;
        _activeProperties = ResolveProperties(index);
        _fields = BuildFields(index);
        _dirtyFields.Clear();
    }

    private int FindSummaryTargetIndex(string sectionId)
    {
        for (var index = 0; index < SummaryTargetOptions.Count; index++)
        {
            if (string.Equals(
                    SummaryTargetOptions[index].Id,
                    sectionId,
                    StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    private ZoomObjectPropertiesDialogInput BuildInput() =>
        new(
            ReturnToParent: _fields.ReturnToParent,
            ShowBackground: _fields.ShowBackground,
            ImageType: _fields.ImageType,
            TransitionEnabled: _fields.TransitionEnabled,
            TransitionDuration: _fields.TransitionDuration,
            FrameBorderEnabled: _fields.FrameBorderEnabled,
            FrameBorderColor: _fields.FrameBorderColor,
            FrameBorderThemeColor: _fields.FrameBorderThemeColor,
            FrameBorderThemeEnabled: _fields.FrameBorderThemeEnabled,
            FrameBorderWidth: _fields.FrameBorderWidth,
            FrameBorderDash: _fields.FrameBorderDash.ToString(),
            FrameBorderGradientEnabled: _fields.FrameBorderGradientEnabled,
            FrameBorderGradientStart: _fields.FrameBorderGradientStart,
            FrameBorderGradientEnd: _fields.FrameBorderGradientEnd,
            FrameBorderGradientAngle: _fields.FrameBorderGradientAngle,
            FrameBorderPatternEnabled: _fields.FrameBorderPatternEnabled,
            FrameBorderPatternPreset: _fields.FrameBorderPatternPreset,
            FrameBorderPatternForeground: _fields.FrameBorderPatternForeground,
            FrameBorderPatternBackground: _fields.FrameBorderPatternBackground,
            FrameBorderNoFillEnabled: _fields.FrameBorderNoFillEnabled,
            FrameBorderShadowEnabled: _fields.FrameBorderShadowEnabled,
            FrameBorderShadowColor: _fields.FrameBorderShadowColor,
            FrameBorderShadowAlpha: _fields.FrameBorderShadowAlpha,
            FrameBorderShadowBlur: _fields.FrameBorderShadowBlur,
            FrameBorderShadowDistance: _fields.FrameBorderShadowDistance,
            FrameBorderShadowDirection: _fields.FrameBorderShadowDirection,
            FrameBorderGlowEnabled: _fields.FrameBorderGlowEnabled,
            FrameBorderGlowColor: _fields.FrameBorderGlowColor,
            FrameBorderGlowAlpha: _fields.FrameBorderGlowAlpha,
            FrameBorderGlowRadius: _fields.FrameBorderGlowRadius,
            FrameBorderSoftEdgeEnabled: _fields.FrameBorderSoftEdgeEnabled,
            FrameBorderSoftEdgeRadius: _fields.FrameBorderSoftEdgeRadius,
            FrameBorderReflectionEnabled: _fields.FrameBorderReflectionEnabled,
            FrameBorderReflectionAlpha: _fields.FrameBorderReflectionAlpha,
            FrameBorderReflectionBlur: _fields.FrameBorderReflectionBlur,
            FrameBorderReflectionDistance: _fields.FrameBorderReflectionDistance,
            FrameBorderReflectionDirection: _fields.FrameBorderReflectionDirection,
            FrameBorderReflectionScale: _fields.FrameBorderReflectionScale,
            FrameBorderReflectionEndPosition: _fields.FrameBorderReflectionEndPosition,
            FrameGeometry: _fields.FrameGeometry,
            CropEdges: _fields.CropEdges,
            SummaryTileIndex: _selectedSummaryTileIndex,
            SummaryOffset: _fields.SummaryOffset,
            SummaryScale: _fields.SummaryScale,
            ApplySummaryPropertiesToAllTiles: _applySummaryPropertiesToAllTiles);

    private ZoomObjectPropertiesDialogState BuildState()
    {
        var enablement = BuildEnablement(
            _fields.TransitionEnabled,
            _fields.FrameBorderEnabled,
            _fields.FrameBorderGradientEnabled,
            _fields.FrameBorderPatternEnabled,
            _fields.FrameBorderNoFillEnabled,
            _fields.FrameBorderThemeEnabled,
            _fields.FrameBorderShadowEnabled,
            _fields.FrameBorderGlowEnabled,
            _fields.FrameBorderSoftEdgeEnabled,
            _fields.FrameBorderReflectionEnabled);
        var fields = FieldCatalog
            .Select(control => new ZoomObjectPropertiesDialogFieldState(
                control.Field,
                GetFieldValue(control.Field),
                IsFieldEnabled(control.Field, enablement)))
            .ToArray();
        return new ZoomObjectPropertiesDialogState(_selectedSummaryTileIndex, fields);
    }

    private object? GetFieldValue(ZoomObjectPropertiesDialogField field) => field switch
    {
        ZoomObjectPropertiesDialogField.ReturnToParent => _fields.ReturnToParent,
        ZoomObjectPropertiesDialogField.ShowBackground => _fields.ShowBackground,
        ZoomObjectPropertiesDialogField.ImageType => _fields.ImageType,
        ZoomObjectPropertiesDialogField.TransitionEnabled => _fields.TransitionEnabled,
        ZoomObjectPropertiesDialogField.TransitionDuration => _fields.TransitionDuration,
        ZoomObjectPropertiesDialogField.FrameBorderEnabled => _fields.FrameBorderEnabled,
        ZoomObjectPropertiesDialogField.FrameBorderColor => _fields.FrameBorderColor,
        ZoomObjectPropertiesDialogField.FrameBorderThemeColor => _fields.FrameBorderThemeColor,
        ZoomObjectPropertiesDialogField.FrameBorderThemeEnabled => _fields.FrameBorderThemeEnabled,
        ZoomObjectPropertiesDialogField.FrameBorderWidth => _fields.FrameBorderWidth,
        ZoomObjectPropertiesDialogField.FrameBorderDash => _fields.FrameBorderDash,
        ZoomObjectPropertiesDialogField.FrameBorderGradientEnabled => _fields.FrameBorderGradientEnabled,
        ZoomObjectPropertiesDialogField.FrameBorderGradientStart => _fields.FrameBorderGradientStart,
        ZoomObjectPropertiesDialogField.FrameBorderGradientEnd => _fields.FrameBorderGradientEnd,
        ZoomObjectPropertiesDialogField.FrameBorderGradientAngle => _fields.FrameBorderGradientAngle,
        ZoomObjectPropertiesDialogField.FrameBorderPatternEnabled => _fields.FrameBorderPatternEnabled,
        ZoomObjectPropertiesDialogField.FrameBorderPatternPreset => _fields.FrameBorderPatternPreset,
        ZoomObjectPropertiesDialogField.FrameBorderPatternForeground => _fields.FrameBorderPatternForeground,
        ZoomObjectPropertiesDialogField.FrameBorderPatternBackground => _fields.FrameBorderPatternBackground,
        ZoomObjectPropertiesDialogField.FrameBorderNoFillEnabled => _fields.FrameBorderNoFillEnabled,
        ZoomObjectPropertiesDialogField.FrameBorderShadowEnabled => _fields.FrameBorderShadowEnabled,
        ZoomObjectPropertiesDialogField.FrameBorderShadowColor => _fields.FrameBorderShadowColor,
        ZoomObjectPropertiesDialogField.FrameBorderShadowAlpha => _fields.FrameBorderShadowAlpha,
        ZoomObjectPropertiesDialogField.FrameBorderShadowBlur => _fields.FrameBorderShadowBlur,
        ZoomObjectPropertiesDialogField.FrameBorderShadowDistance => _fields.FrameBorderShadowDistance,
        ZoomObjectPropertiesDialogField.FrameBorderShadowDirection => _fields.FrameBorderShadowDirection,
        ZoomObjectPropertiesDialogField.FrameBorderGlowEnabled => _fields.FrameBorderGlowEnabled,
        ZoomObjectPropertiesDialogField.FrameBorderGlowColor => _fields.FrameBorderGlowColor,
        ZoomObjectPropertiesDialogField.FrameBorderGlowAlpha => _fields.FrameBorderGlowAlpha,
        ZoomObjectPropertiesDialogField.FrameBorderGlowRadius => _fields.FrameBorderGlowRadius,
        ZoomObjectPropertiesDialogField.FrameBorderSoftEdgeEnabled => _fields.FrameBorderSoftEdgeEnabled,
        ZoomObjectPropertiesDialogField.FrameBorderSoftEdgeRadius => _fields.FrameBorderSoftEdgeRadius,
        ZoomObjectPropertiesDialogField.FrameBorderReflectionEnabled => _fields.FrameBorderReflectionEnabled,
        ZoomObjectPropertiesDialogField.FrameBorderReflectionAlpha => _fields.FrameBorderReflectionAlpha,
        ZoomObjectPropertiesDialogField.FrameBorderReflectionBlur => _fields.FrameBorderReflectionBlur,
        ZoomObjectPropertiesDialogField.FrameBorderReflectionDistance => _fields.FrameBorderReflectionDistance,
        ZoomObjectPropertiesDialogField.FrameBorderReflectionDirection => _fields.FrameBorderReflectionDirection,
        ZoomObjectPropertiesDialogField.FrameBorderReflectionScale => _fields.FrameBorderReflectionScale,
        ZoomObjectPropertiesDialogField.FrameBorderReflectionEndPosition => _fields.FrameBorderReflectionEndPosition,
        ZoomObjectPropertiesDialogField.FrameGeometry => _fields.FrameGeometry,
        ZoomObjectPropertiesDialogField.CropEdges => _fields.CropEdges,
        ZoomObjectPropertiesDialogField.SummaryTile =>
            _selectedSummaryTileIndex >= 0 && _selectedSummaryTileIndex < SummaryTargetOptions.Count
                ? SummaryTargetOptions[_selectedSummaryTileIndex]
                : null,
        ZoomObjectPropertiesDialogField.SummaryOffset => _fields.SummaryOffset,
        ZoomObjectPropertiesDialogField.SummaryScale => _fields.SummaryScale,
        ZoomObjectPropertiesDialogField.ApplySummaryPropertiesToAllTiles =>
            _applySummaryPropertiesToAllTiles,
        _ => null,
    };

    private static bool IsFieldEnabled(
        ZoomObjectPropertiesDialogField field,
        ZoomObjectPropertiesDialogEnablement enablement) => field switch
    {
        ZoomObjectPropertiesDialogField.TransitionDuration => enablement.TransitionDuration,
        ZoomObjectPropertiesDialogField.FrameBorderColor => enablement.FrameBorderColor,
        ZoomObjectPropertiesDialogField.FrameBorderWidth => enablement.FrameBorderWidth,
        ZoomObjectPropertiesDialogField.FrameBorderDash => enablement.FrameBorderDash,
        ZoomObjectPropertiesDialogField.FrameBorderGradientEnabled =>
            enablement.FrameBorderGradientToggle,
        ZoomObjectPropertiesDialogField.FrameBorderGradientStart or
            ZoomObjectPropertiesDialogField.FrameBorderGradientEnd or
            ZoomObjectPropertiesDialogField.FrameBorderGradientAngle =>
            enablement.FrameBorderGradientFields,
        ZoomObjectPropertiesDialogField.FrameBorderPatternEnabled =>
            enablement.FrameBorderPatternToggle,
        ZoomObjectPropertiesDialogField.FrameBorderPatternPreset or
            ZoomObjectPropertiesDialogField.FrameBorderPatternForeground or
            ZoomObjectPropertiesDialogField.FrameBorderPatternBackground =>
            enablement.FrameBorderPatternFields,
        ZoomObjectPropertiesDialogField.FrameBorderNoFillEnabled =>
            enablement.FrameBorderNoFillToggle,
        ZoomObjectPropertiesDialogField.FrameBorderThemeEnabled =>
            enablement.FrameBorderThemeToggle,
        ZoomObjectPropertiesDialogField.FrameBorderThemeColor => enablement.FrameBorderThemeColor,
        ZoomObjectPropertiesDialogField.FrameBorderShadowEnabled =>
            enablement.FrameBorderShadowToggle,
        ZoomObjectPropertiesDialogField.FrameBorderShadowColor or
            ZoomObjectPropertiesDialogField.FrameBorderShadowAlpha or
            ZoomObjectPropertiesDialogField.FrameBorderShadowBlur or
            ZoomObjectPropertiesDialogField.FrameBorderShadowDistance or
            ZoomObjectPropertiesDialogField.FrameBorderShadowDirection =>
            enablement.FrameBorderShadowFields,
        ZoomObjectPropertiesDialogField.FrameBorderGlowEnabled =>
            enablement.FrameBorderGlowToggle,
        ZoomObjectPropertiesDialogField.FrameBorderGlowColor or
            ZoomObjectPropertiesDialogField.FrameBorderGlowAlpha or
            ZoomObjectPropertiesDialogField.FrameBorderGlowRadius =>
            enablement.FrameBorderGlowFields,
        ZoomObjectPropertiesDialogField.FrameBorderSoftEdgeEnabled =>
            enablement.FrameBorderSoftEdgeToggle,
        ZoomObjectPropertiesDialogField.FrameBorderSoftEdgeRadius =>
            enablement.FrameBorderSoftEdgeFields,
        ZoomObjectPropertiesDialogField.FrameBorderReflectionEnabled =>
            enablement.FrameBorderReflectionToggle,
        ZoomObjectPropertiesDialogField.FrameBorderReflectionAlpha or
            ZoomObjectPropertiesDialogField.FrameBorderReflectionBlur or
            ZoomObjectPropertiesDialogField.FrameBorderReflectionDistance or
            ZoomObjectPropertiesDialogField.FrameBorderReflectionDirection or
            ZoomObjectPropertiesDialogField.FrameBorderReflectionScale or
            ZoomObjectPropertiesDialogField.FrameBorderReflectionEndPosition =>
            enablement.FrameBorderReflectionFields,
        _ => true,
    };

    private ZoomObjectProperties PreserveUntouchedUnknowns(
        ZoomObjectProperties source,
        ZoomObjectProperties normalized)
    {
        if (!_dirtyFields.Contains(ZoomObjectPropertiesDialogField.ImageType)
            && !string.IsNullOrWhiteSpace(source.ImageType)
            && !ZoomObjectPropertiesPlanner.IsSupportedImageType(source.ImageType))
            normalized = normalized with { ImageType = source.ImageType };

        if (!_dirtyFields.Contains(ZoomObjectPropertiesDialogField.FrameGeometry)
            && !string.IsNullOrWhiteSpace(source.FrameGeometry)
            && !ZoomObjectPropertiesPlanner.FrameGeometryOptions.Any(option =>
                string.Equals(option, source.FrameGeometry, StringComparison.OrdinalIgnoreCase)))
            normalized = normalized with { FrameGeometry = source.FrameGeometry };

        return normalized;
    }

    private ZoomObjectProperties ResolveProperties(int summaryTileIndex) =>
        summaryTileIndex >= 0 && summaryTileIndex < _summaryTileProperties.Count
            ? _summaryTileProperties[summaryTileIndex]
            : _current;

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
        bool shadowEnabled,
        bool glowEnabled,
        bool softEdgeEnabled,
        bool reflectionEnabled)
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
            FrameBorderShadowFields: frameBorderEnabled && shadowEnabled,
            FrameBorderGlowToggle: frameBorderEnabled,
            FrameBorderGlowFields: frameBorderEnabled && glowEnabled,
            FrameBorderSoftEdgeToggle: frameBorderEnabled,
            FrameBorderSoftEdgeFields: frameBorderEnabled && softEdgeEnabled,
            FrameBorderReflectionToggle: frameBorderEnabled,
            FrameBorderReflectionFields: frameBorderEnabled && reflectionEnabled);
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
            FrameBorderGlowEnabled: ZoomObjectPropertiesPlanner.IsFrameBorderGlowEnabled(properties),
            FrameBorderGlowColor: ZoomObjectPropertiesPlanner.FormatFrameBorderGlowColor(properties),
            FrameBorderGlowAlpha: ZoomObjectPropertiesPlanner.FormatFrameBorderGlowAlpha(properties),
            FrameBorderGlowRadius: ZoomObjectPropertiesPlanner.FormatFrameBorderGlowRadius(properties),
            FrameBorderSoftEdgeEnabled: ZoomObjectPropertiesPlanner.IsFrameBorderSoftEdgeEnabled(properties),
            FrameBorderSoftEdgeRadius: ZoomObjectPropertiesPlanner.FormatFrameBorderSoftEdgeRadius(properties),
            FrameBorderReflectionEnabled: ZoomObjectPropertiesPlanner.IsFrameBorderReflectionEnabled(properties),
            FrameBorderReflectionAlpha: ZoomObjectPropertiesPlanner.FormatFrameBorderReflectionAlpha(properties),
            FrameBorderReflectionBlur: ZoomObjectPropertiesPlanner.FormatFrameBorderReflectionBlur(properties),
            FrameBorderReflectionDistance: ZoomObjectPropertiesPlanner.FormatFrameBorderReflectionDistance(properties),
            FrameBorderReflectionDirection: ZoomObjectPropertiesPlanner.FormatFrameBorderReflectionDirection(properties),
            FrameBorderReflectionScale: ZoomObjectPropertiesPlanner.FormatFrameBorderReflectionScale(properties),
            FrameBorderReflectionEndPosition: ZoomObjectPropertiesPlanner.FormatFrameBorderReflectionEndPosition(properties),
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
