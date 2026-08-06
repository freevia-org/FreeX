using System.Collections.ObjectModel;

namespace FreeP.App.Compositor;

public enum ChartOptionsDialogFieldId
{
    RotationX,
    RotationY,
    Perspective,
    HeightPercent,
    DepthPercent,
    BarGapDepthPercent,
    RightAngleAxes,
    Wireframe,
    BubbleScale,
    BubbleSizeRepresents,
    ShowNegativeBubbles,
    ScatterStyle,
    RadarStyle,
    ProtectedChartObject,
    ProtectedData,
    ProtectedFormatting,
    ProtectedSelection,
    FontFamily,
    FontSize,
    Bold,
    Italic,
    TextColor,
    AreaTarget,
    FillColor,
    FillTransparency,
    NoFill,
    OutlineColor,
    NoOutline,
    OutlineWidth,
    ShowDataTable,
    HorizontalBorder,
    VerticalBorder,
    OutlineBorder,
    LegendKeys,
    BackgroundColor,
    BorderColor,
    BorderWidth,
    FirstSliceAngle,
    DoughnutHole,
    OfPieType,
    OfPieSplitType,
    OfPieSplitPosition,
    OfPieSecondPieSize,
    OfPieCustomPointIndices,
    OfPieGapWidth,
    OfPieSeriesLines,
    LayoutTargetObject,
    LayoutTarget,
    XMode,
    YMode,
    WidthMode,
    HeightMode,
    X,
    Y,
    Width,
    Height,
    ChartExSeries,
    ChartExLayout,
}

public enum ChartOptionsDialogControlKind
{
    Text,
    Choice,
    Toggle,
}

public sealed record ChartOptionsDialogFieldPlan(
    ChartOptionsDialogFieldId Id,
    ChartOptionsDialogControlKind ControlKind,
    string Label,
    string AccessibleName,
    string AutomationId,
    string Text = "",
    int SelectedIndex = -1,
    bool IsChecked = false,
    IReadOnlyList<string>? Choices = null,
    bool IsVisible = true,
    bool IsEnabled = true,
    double LabelWidth = 180,
    double MinimumControlWidth = 150,
    bool IsStandalone = false)
{
    public IReadOnlyList<string> ChoiceLabels { get; } =
        Choices ?? Array.Empty<string>();
}

public sealed record ChartOptionsDialogGroupPlan(
    string Id,
    string? Header,
    string AccessibleName,
    IReadOnlyList<ChartOptionsDialogFieldPlan> Fields);

public sealed class ChartOptionsDialogPlan
{
    private readonly IReadOnlyDictionary<ChartOptionsDialogFieldId, ChartOptionsDialogFieldPlan> _fields;

    public ChartOptionsDialogPlan(
        string commandId,
        string title,
        double width,
        double height,
        double minimumWidth,
        double minimumHeight,
        bool isResizable,
        bool isScrollable,
        string? hint,
        string acceptLabel,
        string cancelLabel,
        IReadOnlyList<ChartOptionsDialogGroupPlan> groups)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(acceptLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(cancelLabel);
        ArgumentNullException.ThrowIfNull(groups);

        var fields = groups.SelectMany(group => group.Fields).ToArray();
        var duplicate = fields
            .GroupBy(field => field.Id)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate chart dialog field: {duplicate.Key}.", nameof(groups));

        CommandId = commandId;
        Title = title;
        Width = width;
        Height = height;
        MinimumWidth = minimumWidth;
        MinimumHeight = minimumHeight;
        IsResizable = isResizable;
        IsScrollable = isScrollable;
        Hint = hint;
        AcceptLabel = acceptLabel;
        CancelLabel = cancelLabel;
        Groups = new ReadOnlyCollection<ChartOptionsDialogGroupPlan>(groups.ToArray());
        _fields = new ReadOnlyDictionary<ChartOptionsDialogFieldId, ChartOptionsDialogFieldPlan>(
            fields.ToDictionary(field => field.Id));
    }

    public string CommandId { get; }
    public string Title { get; }
    public double Width { get; }
    public double Height { get; }
    public double MinimumWidth { get; }
    public double MinimumHeight { get; }
    public bool IsResizable { get; }
    public bool IsScrollable { get; }
    public string? Hint { get; }
    public string AcceptLabel { get; }
    public string CancelLabel { get; }
    public IReadOnlyList<ChartOptionsDialogGroupPlan> Groups { get; }
    public IReadOnlyDictionary<ChartOptionsDialogFieldId, ChartOptionsDialogFieldPlan> Fields => _fields;

    public ChartOptionsDialogFieldPlan Field(ChartOptionsDialogFieldId fieldId) =>
        _fields.TryGetValue(fieldId, out var field)
            ? field
            : throw new KeyNotFoundException($"The dialog plan does not define {fieldId}.");
}

public sealed record ChartOptionsDialogFieldValue(
    string Text = "",
    int SelectedIndex = -1,
    bool IsChecked = false);

public sealed class ChartOptionsDialogValues
{
    private readonly IReadOnlyDictionary<ChartOptionsDialogFieldId, ChartOptionsDialogFieldValue> _values;

    public ChartOptionsDialogValues(
        IReadOnlyDictionary<ChartOptionsDialogFieldId, ChartOptionsDialogFieldValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = new ReadOnlyDictionary<ChartOptionsDialogFieldId, ChartOptionsDialogFieldValue>(
            new Dictionary<ChartOptionsDialogFieldId, ChartOptionsDialogFieldValue>(values));
    }

    public string Text(ChartOptionsDialogFieldId fieldId) => Value(fieldId).Text;
    public int SelectedIndex(ChartOptionsDialogFieldId fieldId) => Value(fieldId).SelectedIndex;
    public bool IsChecked(ChartOptionsDialogFieldId fieldId) => Value(fieldId).IsChecked;

    private ChartOptionsDialogFieldValue Value(ChartOptionsDialogFieldId fieldId) =>
        _values.TryGetValue(fieldId, out var value)
            ? value
            : throw new KeyNotFoundException($"The dialog values do not contain {fieldId}.");
}

public static class ChartOptionsDialogPlanCatalog
{
    public static ChartOptionsDialogPlan BuildDialogPlan(
        this Chart3DViewOptionsDialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = session.State;
        var surface = session.Surface;
        var choices = Labels(session.BooleanOptions, option => option.Label);
        return Plan(
            surface.CommandId,
            surface.Title,
            Chart3DViewOptionsPlanner.DefaultDialogWidth,
            Chart3DViewOptionsPlanner.DefaultDialogHeight,
            380,
            320,
            surface.AutoHint,
            surface.OkLabel,
            surface.CancelLabel,
            Group("view", "3-D view", [
                Text(ChartOptionsDialogFieldId.RotationX, surface.RotationXLabel, state.RotationXText, 170),
                Text(ChartOptionsDialogFieldId.RotationY, surface.RotationYLabel, state.RotationYText, 170),
                Text(ChartOptionsDialogFieldId.Perspective, surface.PerspectiveLabel, state.PerspectiveText, 170),
                Text(ChartOptionsDialogFieldId.HeightPercent, surface.HeightPercentLabel, state.HeightPercentText, 170),
                Text(ChartOptionsDialogFieldId.DepthPercent, surface.DepthPercentLabel, state.DepthPercentText, 170),
                Text(ChartOptionsDialogFieldId.BarGapDepthPercent, surface.BarGapDepthPercentLabel, state.BarGapDepthPercentText, 170, isEnabled: state.SupportsBarGapDepth),
                Choice(ChartOptionsDialogFieldId.RightAngleAxes, surface.RightAngleAxesLabel, state.RightAngleAxesIndex, choices, 170),
                Choice(ChartOptionsDialogFieldId.Wireframe, surface.WireframeLabel, state.WireframeIndex, choices, 170),
            ]));
    }

    public static ChartOptionsDialogPlan BuildDialogPlan(
        this ChartBubbleOptionsDialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = session.State;
        var surface = session.Surface;
        return Plan(
            surface.CommandId,
            surface.Title,
            ChartBubbleOptionsPlanner.DefaultDialogWidth,
            ChartBubbleOptionsPlanner.DefaultDialogHeight,
            360,
            240,
            surface.Hint,
            surface.OkLabel,
            surface.CancelLabel,
            Group("bubble", null, [
                Text(ChartOptionsDialogFieldId.BubbleScale, surface.BubbleScaleLabel, state.BubbleScaleText, 190),
                Choice(
                    ChartOptionsDialogFieldId.BubbleSizeRepresents,
                    surface.SizeRepresentsLabel,
                    state.SizeRepresentsIndex,
                    Labels(session.SizeRepresentsOptions, option => option.Label),
                    190),
                Toggle(ChartOptionsDialogFieldId.ShowNegativeBubbles, surface.ShowNegativeBubblesLabel, state.ShowNegativeBubbles),
            ]));
    }

    public static ChartOptionsDialogPlan BuildDialogPlan(
        this ChartPlotStyleOptionsDialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = session.State;
        var surface = session.Surface;
        return Plan(
            surface.CommandId,
            surface.Title,
            ChartPlotStyleOptionsPlanner.DefaultDialogWidth,
            ChartPlotStyleOptionsPlanner.DefaultDialogHeight,
            360,
            220,
            surface.Hint,
            surface.OkLabel,
            surface.CancelLabel,
            Group("plot-style", null, [
                Choice(ChartOptionsDialogFieldId.ScatterStyle, surface.ScatterStyleLabel, state.ScatterStyleIndex, Labels(session.ScatterStyleOptions, option => option.Label), 190, isEnabled: state.IsScatterEnabled),
                Choice(ChartOptionsDialogFieldId.RadarStyle, surface.RadarStyleLabel, state.RadarStyleIndex, Labels(session.RadarStyleOptions, option => option.Label), 190, isEnabled: state.IsRadarEnabled),
            ]));
    }

    public static ChartOptionsDialogPlan BuildDialogPlan(
        this ChartProtectionOptionsDialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = session.State;
        var surface = session.Surface;
        var choices = Labels(session.BooleanOptions, option => option.Label);
        return Plan(
            surface.CommandId,
            surface.Title,
            ChartProtectionOptionsPlanner.DefaultDialogWidth,
            ChartProtectionOptionsPlanner.DefaultDialogHeight,
            400,
            260,
            surface.Hint,
            surface.OkLabel,
            surface.CancelLabel,
            Group("protection", "Protection", [
                Choice(ChartOptionsDialogFieldId.ProtectedChartObject, surface.ChartObjectLabel, state.ChartObjectIndex, choices),
                Choice(ChartOptionsDialogFieldId.ProtectedData, surface.DataLabel, state.DataIndex, choices),
                Choice(ChartOptionsDialogFieldId.ProtectedFormatting, surface.FormattingLabel, state.FormattingIndex, choices),
                Choice(ChartOptionsDialogFieldId.ProtectedSelection, surface.SelectionLabel, state.SelectionIndex, choices),
            ]));
    }

    public static ChartOptionsDialogPlan BuildDialogPlan(
        this ChartTextOptionsDialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = session.State;
        var surface = session.Surface;
        var choices = Labels(session.BooleanOptions, option => option.Label);
        return Plan(
            surface.CommandId,
            surface.Title,
            ChartTextOptionsPlanner.DefaultDialogWidth,
            ChartTextOptionsPlanner.DefaultDialogHeight,
            380,
            280,
            surface.AutoHint,
            surface.OkLabel,
            surface.CancelLabel,
            Group("text", "Text", [
                Text(ChartOptionsDialogFieldId.FontFamily, surface.FontFamilyLabel, state.FontFamilyText),
                Text(ChartOptionsDialogFieldId.FontSize, surface.FontSizeLabel, state.FontSizeText),
                Choice(ChartOptionsDialogFieldId.Bold, surface.BoldLabel, state.BoldIndex, choices),
                Choice(ChartOptionsDialogFieldId.Italic, surface.ItalicLabel, state.ItalicIndex, choices),
                Text(ChartOptionsDialogFieldId.TextColor, surface.ColorLabel, state.ColorText),
            ]));
    }

    public static Chart3DViewOptionsDialogInput BuildInput(
        this Chart3DViewOptionsDialogSession session,
        ChartOptionsDialogValues values) => new(
            values.Text(ChartOptionsDialogFieldId.RotationX),
            values.Text(ChartOptionsDialogFieldId.RotationY),
            values.Text(ChartOptionsDialogFieldId.Perspective),
            values.Text(ChartOptionsDialogFieldId.HeightPercent),
            values.Text(ChartOptionsDialogFieldId.DepthPercent),
            values.Text(ChartOptionsDialogFieldId.BarGapDepthPercent),
            values.SelectedIndex(ChartOptionsDialogFieldId.RightAngleAxes),
            values.SelectedIndex(ChartOptionsDialogFieldId.Wireframe));

    public static ChartBubbleOptionsDialogInput BuildInput(
        this ChartBubbleOptionsDialogSession session,
        ChartOptionsDialogValues values) => new(
            values.Text(ChartOptionsDialogFieldId.BubbleScale),
            values.SelectedIndex(ChartOptionsDialogFieldId.BubbleSizeRepresents),
            values.IsChecked(ChartOptionsDialogFieldId.ShowNegativeBubbles));

    public static ChartPlotStyleOptionsDialogInput BuildInput(
        this ChartPlotStyleOptionsDialogSession session,
        ChartOptionsDialogValues values) => new(
            values.SelectedIndex(ChartOptionsDialogFieldId.ScatterStyle),
            values.SelectedIndex(ChartOptionsDialogFieldId.RadarStyle));

    public static ChartProtectionOptionsDialogInput BuildInput(
        this ChartProtectionOptionsDialogSession session,
        ChartOptionsDialogValues values) => new(
            values.SelectedIndex(ChartOptionsDialogFieldId.ProtectedChartObject),
            values.SelectedIndex(ChartOptionsDialogFieldId.ProtectedData),
            values.SelectedIndex(ChartOptionsDialogFieldId.ProtectedFormatting),
            values.SelectedIndex(ChartOptionsDialogFieldId.ProtectedSelection));

    public static ChartTextOptionsDialogInput BuildInput(
        this ChartTextOptionsDialogSession session,
        ChartOptionsDialogValues values) => new(
            values.Text(ChartOptionsDialogFieldId.FontFamily),
            values.Text(ChartOptionsDialogFieldId.FontSize),
            values.SelectedIndex(ChartOptionsDialogFieldId.Bold),
            values.SelectedIndex(ChartOptionsDialogFieldId.Italic),
            values.Text(ChartOptionsDialogFieldId.TextColor));

    private static ChartOptionsDialogPlan Plan(
        string commandId,
        string title,
        double width,
        double height,
        double minimumWidth,
        double minimumHeight,
        string? hint,
        string acceptLabel,
        string cancelLabel,
        params ChartOptionsDialogGroupPlan[] groups) => new(
            commandId,
            title,
            width,
            height,
            minimumWidth,
            minimumHeight,
            isResizable: false,
            isScrollable: false,
            hint,
            acceptLabel,
            cancelLabel,
            groups);

    private static ChartOptionsDialogGroupPlan Group(
        string id,
        string? header,
        IReadOnlyList<ChartOptionsDialogFieldPlan> fields) =>
        new(id, header, header ?? "Chart options", fields);

    private static ChartOptionsDialogFieldPlan Text(
        ChartOptionsDialogFieldId id,
        string label,
        string? value,
        double labelWidth = 180,
        bool isVisible = true,
        bool isEnabled = true) =>
        Field(id, ChartOptionsDialogControlKind.Text, label, value ?? string.Empty, labelWidth: labelWidth, isVisible: isVisible, isEnabled: isEnabled);

    private static ChartOptionsDialogFieldPlan Choice(
        ChartOptionsDialogFieldId id,
        string label,
        int selectedIndex,
        IReadOnlyList<string> choices,
        double labelWidth = 180,
        bool isVisible = true,
        bool isEnabled = true) =>
        Field(id, ChartOptionsDialogControlKind.Choice, label, selectedIndex: selectedIndex, choices: choices, labelWidth: labelWidth, isVisible: isVisible, isEnabled: isEnabled);

    private static ChartOptionsDialogFieldPlan Toggle(
        ChartOptionsDialogFieldId id,
        string label,
        bool isChecked,
        bool isVisible = true,
        bool isEnabled = true) =>
        Field(id, ChartOptionsDialogControlKind.Toggle, label, isChecked: isChecked, isVisible: isVisible, isEnabled: isEnabled, isStandalone: true);

    private static ChartOptionsDialogFieldPlan Field(
        ChartOptionsDialogFieldId id,
        ChartOptionsDialogControlKind kind,
        string label,
        string text = "",
        int selectedIndex = -1,
        bool isChecked = false,
        IReadOnlyList<string>? choices = null,
        double labelWidth = 180,
        bool isVisible = true,
        bool isEnabled = true,
        bool isStandalone = false) => new(
            id,
            kind,
            label,
            label,
            $"FreeP.ChartOptions.{id}",
            text,
            selectedIndex,
            isChecked,
            choices,
            isVisible,
            isEnabled,
            labelWidth,
            MinimumControlWidth: 150,
            IsStandalone: isStandalone);

    private static IReadOnlyList<string> Labels<T>(
        IReadOnlyList<T> options,
        Func<T, string> label) =>
        options.Select(label).ToArray();
}
