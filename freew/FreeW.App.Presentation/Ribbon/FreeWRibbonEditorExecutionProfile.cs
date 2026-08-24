using Free.Shared.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>
/// Typed renderer boundary for editor-owned ribbon behavior. Presentation owns command construction,
/// state gating, command-value parsing, and planner/catalog expansion; renderers only adapt their
/// editor surface and native dialogs to these ports.
/// </summary>
public sealed record FreeWRibbonEditorCommandFamilyPorts(
    IReadOnlyDictionary<FreeWRibbonCommandAction, IRibbonCommand> Commands,
    IReadOnlyDictionary<RibbonCommandId, IRibbonCommand>? AdapterCommands = null);

/// <summary>
/// Collects renderer-native commands without mutating the application registry. The editor profile
/// remains the sole owner of canonical command-id registration for these families.
/// </summary>
public sealed class FreeWRibbonEditorCommandFamilyBuilder
{
    private readonly FreeWRibbonCommandBindingPorts _bindings = new();

    public IRibbonCommand Bind(FreeWRibbonCommandAction action, IRibbonCommand command) =>
        _bindings.Bind(action, command);

    public IRibbonCommand BindAction(
        FreeWRibbonCommandAction action,
        Action execute,
        Func<bool>? isEnabled = null,
        Action? prepareExecution = null) =>
        _bindings.BindAction(action, execute, isEnabled, prepareExecution);

    public IRibbonStatefulCommand BindToggle(
        FreeWRibbonCommandAction action,
        Action toggle,
        Func<bool> isChecked,
        Func<bool>? isEnabled = null,
        Action? prepareExecution = null) =>
        _bindings.BindToggle(action, toggle, isChecked, isEnabled, prepareExecution);

    public void Register(RibbonCommandId commandId, IRibbonCommand command) =>
        _bindings.Register(commandId, command);

    public FreeWRibbonEditorCommandFamilyPorts Build() =>
        new(
            _bindings.CanonicalBindings.ToDictionary(static pair => pair.Key, static pair => pair.Value),
            _bindings.AdapterBindings.ToDictionary(static pair => pair.Key, static pair => pair.Value));
}

public sealed record FreeWRibbonFloatingFeedback(string Title, string Message);

public interface IFreeWRibbonFloatingPositionPreset
{
    string Suffix { get; }
    double HorizontalOffsetPt { get; }
    double VerticalOffsetPt { get; }
    HorizontalAnchor HorizontalAnchor { get; }
    VerticalAnchor VerticalAnchor { get; }
}

public static class FreeWRibbonFloatingFeedbackCatalog
{
    public static readonly FreeWRibbonFloatingFeedback EditShape = new(
        "Edit Shape",
        "Choose 'Convert to Freeform' or 'Edit Points' from the menu.");

    public static readonly FreeWRibbonFloatingFeedback TextDirection = new(
        "Text Direction",
        "Choose a text direction from the dropdown.");

    public static readonly FreeWRibbonFloatingFeedback ShapeEffects = new(
        "Shape Effects",
        "Choose an effect from the dropdown.");

    public static readonly FreeWRibbonFloatingFeedback ShapeStyles = new(
        "Shape Styles",
        "Choose a shape style from the gallery.");

    public static readonly FreeWRibbonFloatingFeedback GroupSelectionRequired = new(
        "Group",
        "Select two or more floating objects first (Shift-click or Ctrl-click).");

    public static readonly FreeWRibbonFloatingFeedback UngroupSelectionRequired = new(
        "Ungroup",
        "Select a group first.");
}

public sealed record FreeWRibbonFloatingExecutionPorts(
    Action PrepareExecution,
    Func<ObjectFormatTarget, bool> HasSelection,
    Func<bool> HasTransformSelection,
    Action<ObjectFormatTarget, ImageWrapping> ApplyWrap,
    Func<ObjectFormatTarget, ObjectFormatTransformCommand, bool> ApplyTransform,
    Func<ObjectFormatTarget, ZOrderOperation, bool> ApplyZOrder,
    Action<ObjectFormatTarget, ObjectFormatSizeDimension, double> ApplySize,
    Action<ObjectFormatTarget, TextAlignment> ApplyParagraphAlignment,
    Func<FloatingObjectArrangeKind, bool> CanArrange,
    Action<FloatingObjectArrangeKind> Arrange,
    Func<Shape?> SelectedShape,
    Action<ShapeKind> SetShapeKind,
    Action ConvertShapeToFreeform,
    Action BeginShapeEditPoints,
    Action<ShapeTextDirection> SetShapeTextDirection,
    Action<ShapeFill?> SetShapeExtendedFill,
    Action<string?> SetShapeFill,
    Action<string?, double, string?> SetShapeOutline,
    Action<ShapeEffectLst?> SetShapeEffects,
    Action<ShapeStylePreset> ApplyShapeStyle,
    Func<bool> CanGroup,
    Action Group,
    Func<bool> CanUngroup,
    Action Ungroup,
    Action<FreeWRibbonFloatingFeedback>? ShowFeedback = null,
    Action<ObjectFormatTarget, FreeWRibbonObjectPositionInput>? ApplyPosition = null);

public sealed record FreeWRibbonChartSmartArtExecutionPorts(
    Action PrepareExecution,
    Action CompleteExecution,
    Func<Chart?> SelectedChart,
    Action<ChartKind> SetChartKind,
    Action<ChartStyle> ApplyChartStyle,
    Action<ChartColorScheme> ApplyChartColorScheme,
    Action<ChartQuickLayout> ApplyChartQuickLayout,
    Action ToggleChartLegend,
    Func<Chart, ValueTask<ChartTitleDialogResult?>>? ShowChartTitleDialogAsync,
    Action<ChartTitleDialogResult> ApplyChartTitleOutcome,
    Action? ToggleChartTitleFallback,
    Func<Chart, ValueTask<ChartAxisTitlesDialogResult?>>? ShowChartAxisTitlesDialogAsync,
    Action<ChartAxisTitlesDialogResult> ApplyChartAxisTitlesOutcome,
    Action? ToggleChartAxisTitlesFallback,
    Func<Chart, ValueTask<Chart?>>? ShowChartDataDialogAsync,
    Action<Chart> ApplyChartDataOutcome,
    Func<Chart, ValueTask<ChartSizeDialogResult?>>? ShowChartSizeDialogAsync,
    Action<ChartSizeDialogResult> ApplyChartSizeOutcome,
    Func<SmartArt?> SelectedSmartArt,
    Action<SmartArtStructureOperation> MutateSmartArt,
    Action<SmartArtLayoutPreset> ApplySmartArtLayout,
    Action<SmartArtColorScheme> ApplySmartArtColorScheme,
    Action<SmartArtStyle> ApplySmartArtStyle,
    Func<SmartArt, ValueTask<SmartArt?>>? ShowSmartArtEditDialogAsync,
    Action<SmartArt> ApplySmartArtEditOutcome,
    Action<ChartStyle>? PreviewChartStyle = null,
    Action<ChartColorScheme>? PreviewChartColorScheme = null,
    Action<ChartQuickLayout>? PreviewChartQuickLayout = null,
    Action? CancelChartDesignPreview = null,
    Action<ChartStyle>? CommitChartStyle = null,
    Action<ChartColorScheme>? CommitChartColorScheme = null,
    Action<ChartQuickLayout>? CommitChartQuickLayout = null,
    Action<SmartArtLayoutPreset>? PreviewSmartArtLayout = null,
    Action<SmartArtColorScheme>? PreviewSmartArtColorScheme = null,
    Action<SmartArtStyle>? PreviewSmartArtStyle = null,
    Action? CancelSmartArtDesignPreview = null,
    Action<SmartArtLayoutPreset>? CommitSmartArtLayout = null,
    Action<SmartArtColorScheme>? CommitSmartArtColorScheme = null,
    Action<SmartArtStyle>? CommitSmartArtStyle = null);

public sealed record FreeWRibbonChartSmartArtCommands(
    IRibbonStatefulCommand ChartLegend);

public sealed record FreeWRibbonImageExecutionPorts(
    Action PrepareExecution,
    Action CompleteExecution,
    Func<InlineImage?> SelectedImage,
    Func<InlineImage, ValueTask<ImageCropDialogResult?>>? ShowCropDialogAsync,
    Action<ImageCropDialogResult> ApplyCropOutcome,
    Action ResetImage);

public sealed record FreeWRibbonTableCellSelection(Table Table, int RowIndex, int ColumnIndex);

public sealed record FreeWRibbonTableExecutionPorts(
    Action PrepareExecution,
    Action CompleteExecution,
    Func<FreeWRibbonTableCellSelection?> SelectedCell,
    Func<ModelTableContext?> SelectedContext,
    Func<bool> CanConvertToText,
    Func<TableFormulaDialogInitialState, ValueTask<TableFormulaField?>>? ShowFormulaDialogAsync,
    Action<TableFormulaField> ApplyFormulaOutcome,
    Func<ModelTableContext, ValueTask<TablePropertiesValues?>>? ShowPropertiesDialogAsync,
    Action<TablePropertiesValues> ApplyPropertiesOutcome,
    Func<ValueTask<char?>>? ShowTableToTextDialogAsync,
    Action<char> ApplyTableToTextOutcome);

public static class FreeWRibbonEditorExecutionProfile
{
    public static IReadOnlyList<FreeWRibbonCommandAction> TableActions { get; } =
    [
        FreeWRibbonCommandAction.Table,
        FreeWRibbonCommandAction.TableHeaderRow,
        FreeWRibbonCommandAction.TableBandedRows,
        FreeWRibbonCommandAction.TableLastRow,
        FreeWRibbonCommandAction.TableFirstColumn,
        FreeWRibbonCommandAction.TableLastColumn,
        FreeWRibbonCommandAction.TableBandedCols,
        FreeWRibbonCommandAction.DrawTable,
        FreeWRibbonCommandAction.Eraser,
        FreeWRibbonCommandAction.TableViewGridlines,
        FreeWRibbonCommandAction.TableProperties,
        FreeWRibbonCommandAction.TableSelectTable,
        FreeWRibbonCommandAction.TableSelectRow,
        FreeWRibbonCommandAction.TableSelectCol,
        FreeWRibbonCommandAction.TableSelectCell,
        FreeWRibbonCommandAction.TableInsertAbove,
        FreeWRibbonCommandAction.TableInsertBelow,
        FreeWRibbonCommandAction.TableInsertColLeft,
        FreeWRibbonCommandAction.TableInsertColRight,
        FreeWRibbonCommandAction.TableMergeCells,
        FreeWRibbonCommandAction.TableSplitCell,
        FreeWRibbonCommandAction.TableShading,
        FreeWRibbonCommandAction.TableBorders,
        FreeWRibbonCommandAction.TableDeleteRow,
        FreeWRibbonCommandAction.TableDeleteCol,
        FreeWRibbonCommandAction.TableDelete,
        FreeWRibbonCommandAction.SplitTable,
        FreeWRibbonCommandAction.TableRowHeight,
        FreeWRibbonCommandAction.TableColWidth,
        FreeWRibbonCommandAction.TableDistributeRows,
        FreeWRibbonCommandAction.TableDistributeCols,
        FreeWRibbonCommandAction.TableAutofitContents,
        FreeWRibbonCommandAction.TableAutofitWindow,
        FreeWRibbonCommandAction.TableAutofitFixed,
        FreeWRibbonCommandAction.CellAlignTopLeft,
        FreeWRibbonCommandAction.CellAlignTopCenter,
        FreeWRibbonCommandAction.CellAlignTopRight,
        FreeWRibbonCommandAction.CellAlignMiddleLeft,
        FreeWRibbonCommandAction.CellAlignMiddleCenter,
        FreeWRibbonCommandAction.CellAlignMiddleRight,
        FreeWRibbonCommandAction.CellAlignBottomLeft,
        FreeWRibbonCommandAction.CellAlignBottomCenter,
        FreeWRibbonCommandAction.CellAlignBottomRight,
        FreeWRibbonCommandAction.TableCellMargins,
        FreeWRibbonCommandAction.CellTextDirectionHorizontal,
        FreeWRibbonCommandAction.CellTextDirectionRotate90,
        FreeWRibbonCommandAction.CellTextDirectionRotate270,
        FreeWRibbonCommandAction.TableRepeatHeader,
        FreeWRibbonCommandAction.TableFormula,
        FreeWRibbonCommandAction.TableToText,
    ];

    public static IReadOnlyList<FreeWRibbonCommandAction> ReferenceActions { get; } =
    [
        FreeWRibbonCommandAction.Footnote,
        FreeWRibbonCommandAction.Endnote,
        FreeWRibbonCommandAction.NextFootnote,
        FreeWRibbonCommandAction.PreviousFootnote,
        FreeWRibbonCommandAction.NextEndnote,
        FreeWRibbonCommandAction.PreviousEndnote,
        FreeWRibbonCommandAction.ShowNotes,
        FreeWRibbonCommandAction.FootnoteEndnoteOptions,
        FreeWRibbonCommandAction.Toc,
        FreeWRibbonCommandAction.TocRefresh,
        FreeWRibbonCommandAction.Caption,
        FreeWRibbonCommandAction.InsertCaption_Figure,
        FreeWRibbonCommandAction.InsertCaption_Table,
        FreeWRibbonCommandAction.InsertCaption_Equation,
        FreeWRibbonCommandAction.CrossReference,
        FreeWRibbonCommandAction.Citation,
        FreeWRibbonCommandAction.ManageSources,
        FreeWRibbonCommandAction.CitationStyle,
        FreeWRibbonCommandAction.Bibliography,
        FreeWRibbonCommandAction.Tof,
        FreeWRibbonCommandAction.Tof_Figure,
        FreeWRibbonCommandAction.Tof_Table,
        FreeWRibbonCommandAction.Tof_Equation,
        FreeWRibbonCommandAction.TofRefresh,
        FreeWRibbonCommandAction.TofRefresh_Figure,
        FreeWRibbonCommandAction.TofRefresh_Table,
        FreeWRibbonCommandAction.TofRefresh_Equation,
        FreeWRibbonCommandAction.IndexMark,
        FreeWRibbonCommandAction.IndexInsert,
        FreeWRibbonCommandAction.IndexRefresh,
        FreeWRibbonCommandAction.MarkCitation,
        FreeWRibbonCommandAction.TableOfAuthorities,
        FreeWRibbonCommandAction.TableOfAuthoritiesRefresh,
    ];

    public static IReadOnlyList<FreeWRibbonCommandAction> HeaderFooterActions { get; } =
    [
        FreeWRibbonCommandAction.Header,
        FreeWRibbonCommandAction.Footer,
        FreeWRibbonCommandAction.PageNumber,
        FreeWRibbonCommandAction.PageNumberTop,
        FreeWRibbonCommandAction.PageNumberBottom,
        FreeWRibbonCommandAction.PageNumberCurrent,
        FreeWRibbonCommandAction.PageNumberFormat,
        FreeWRibbonCommandAction.Datetime,
        FreeWRibbonCommandAction.HfEditHeader,
        FreeWRibbonCommandAction.HfEditFooter,
        FreeWRibbonCommandAction.HfEditFirstHeader,
        FreeWRibbonCommandAction.HfEditFirstFooter,
        FreeWRibbonCommandAction.HfEditEvenHeader,
        FreeWRibbonCommandAction.HfEditEvenFooter,
        FreeWRibbonCommandAction.HfGoToHeader,
        FreeWRibbonCommandAction.HfGoToFooter,
        FreeWRibbonCommandAction.HfClose,
        FreeWRibbonCommandAction.HfDifferentFirstPage,
        FreeWRibbonCommandAction.HfDifferentOddEven,
        FreeWRibbonCommandAction.HfHeaderFromTop,
        FreeWRibbonCommandAction.HfFooterFromBottom,
        FreeWRibbonCommandAction.HfInsertPageNumber,
        FreeWRibbonCommandAction.HfInsertPageNumberFooter,
        FreeWRibbonCommandAction.HfInsertDatetime,
        FreeWRibbonCommandAction.HfInsertField,
    ];

    public static void RegisterFamilies(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonEditorCommandFamilyPorts tables,
        FreeWRibbonEditorCommandFamilyPorts references,
        FreeWRibbonEditorCommandFamilyPorts headerFooter)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        RegisterFamily(bindings, tables);
        RegisterFamily(bindings, references);
        RegisterFamily(bindings, headerFooter);
    }

    public static void RegisterFamily(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonEditorCommandFamilyPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(ports);

        foreach (var (action, command) in ports.Commands)
            bindings.Bind(action, command);

        if (ports.AdapterCommands is null)
            return;

        foreach (var (commandId, command) in ports.AdapterCommands)
            bindings.Register(commandId, command);
    }

    public static void RegisterFloatingPositionCommands(
        IRibbonCommandRegistry registry,
        string prefix,
        FreeWRibbonFloatingObjectCommandPorts ports,
        IEnumerable<IFreeWRibbonFloatingPositionPreset> presets)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(presets);

        registry.Register(
            $"freew.{prefix}-position",
            FreeWRibbonFloatingObjectCommandFactory.CreatePosition(ports));
        foreach (var preset in presets)
        {
            var captured = preset;
            registry.Register(
                $"freew.{prefix}-position-{captured.Suffix}",
                FreeWRibbonFloatingObjectCommandFactory.CreatePositionPreset(
                    ports,
                    new FreeWRibbonObjectPositionInput(
                        captured.HorizontalOffsetPt,
                        captured.VerticalOffsetPt,
                        captured.HorizontalAnchor,
                        captured.VerticalAnchor)));
        }
    }

    public static void RegisterFloating(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonFloatingExecutionPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(ports);

        foreach (var target in ObjectFormatCommandPlanner.Targets)
        {
            bindings.Register(
                ObjectFormatCommandPlanner.WrapDropdownCommandId(target),
                EmptyRibbonCommand.Instance);
            foreach (var command in ObjectFormatCommandPlanner.WrapCommands(target))
            {
                var captured = command;
                bindings.Register(captured.CommandId, Stateful(
                    () => ports.ApplyWrap(target, captured.Wrapping),
                    () => ports.HasSelection(target),
                    ports.PrepareExecution));
            }

            bindings.Register(
                ObjectFormatCommandPlanner.TransformDropdownCommandId(target),
                EmptyRibbonCommand.Instance);
            foreach (var command in ObjectFormatCommandPlanner.TransformCommands(target))
            {
                var captured = command;
                bindings.Register(captured.CommandId, Stateful(
                    () => ports.ApplyTransform(target, captured),
                    ports.HasTransformSelection,
                    ports.PrepareExecution));
            }

            foreach (var command in ObjectFormatCommandPlanner.ZOrderCommands(target))
            {
                var captured = command;
                bindings.Register(captured.CommandId, Stateful(
                    () => ports.ApplyZOrder(target, captured.Operation),
                    () => ports.HasSelection(target),
                    ports.PrepareExecution));
            }

            foreach (var command in ObjectFormatCommandPlanner.SizeCommands(target))
            {
                var captured = command;
                bindings.Register(captured.CommandId, new FreeWRibbonStatefulPortCommand(
                    context =>
                    {
                        if (ObjectFormatCommandPlanner.TryParseSizePoints(context.SelectedValue, out var points))
                            ports.ApplySize(target, captured.Dimension, points);
                    },
                    () => new RibbonCommandState(IsEnabled: ports.HasSelection(target)),
                    ports.PrepareExecution));
            }
        }

        BindAlignmentCommands(bindings, ports, ObjectFormatTarget.Picture);
        BindAlignmentCommands(bindings, ports, ObjectFormatTarget.Shape);
        BindArrangeCommands(bindings, ports, ObjectFormatTarget.Picture);
        BindArrangeCommands(bindings, ports, ObjectFormatTarget.Shape);
        RegisterLayoutArrangeCommands(bindings, ports);

        bindings.Bind(FreeWRibbonCommandAction.ShapeEditShape, Stateful(
            () => ports.ShowFeedback?.Invoke(FreeWRibbonFloatingFeedbackCatalog.EditShape),
            () => ports.ShowFeedback is not null || ports.SelectedShape() is not null,
            ports.PrepareExecution));
        bindings.Bind(FreeWRibbonCommandAction.ShapeConvertFreeform, Stateful(
            ports.ConvertShapeToFreeform,
            () => ports.SelectedShape() is not null,
            ports.PrepareExecution));
        bindings.Bind(FreeWRibbonCommandAction.ShapeEditPoints, Stateful(
            () =>
            {
                if (ports.SelectedShape() is { HasCustomGeometry: false })
                    ports.ConvertShapeToFreeform();
                ports.BeginShapeEditPoints();
            },
            () => ports.SelectedShape() is not null,
            ports.PrepareExecution));

        BindShapeKind(bindings, ports, FreeWRibbonCommandAction.ShapeChangeRectangle, ShapeKind.Rectangle);
        BindShapeKind(bindings, ports, FreeWRibbonCommandAction.ShapeChangeRounded, ShapeKind.RoundedRectangle);
        BindShapeKind(bindings, ports, FreeWRibbonCommandAction.ShapeChangeEllipse, ShapeKind.Ellipse);
        bindings.Register("freew.shape-change", Stateful(
            static () => { },
            () => ports.SelectedShape() is not null,
            ports.PrepareExecution));

        bindings.Bind(FreeWRibbonCommandAction.ShapeTextDirection, Stateful(
            () => ports.ShowFeedback?.Invoke(FreeWRibbonFloatingFeedbackCatalog.TextDirection),
            () => ports.ShowFeedback is not null || ports.SelectedShape() is not null,
            ports.PrepareExecution));
        BindShapeTextDirection(bindings, ports, FreeWRibbonCommandAction.ShapeTextHorizontal, ShapeTextDirection.Horizontal);
        BindShapeTextDirection(bindings, ports, FreeWRibbonCommandAction.ShapeTextRotate90, ShapeTextDirection.Rotate90);
        BindShapeTextDirection(bindings, ports, FreeWRibbonCommandAction.ShapeTextRotate270, ShapeTextDirection.Rotate270);

        RegisterShapeFillOutline(bindings, ports);
        BindShapeEffects(bindings, ports);

        bindings.Bind(FreeWRibbonCommandAction.ShapeStylesGallery, new FreeWRibbonStatefulPortCommand(
            context =>
            {
                if (ports.ShowFeedback is not null)
                {
                    ports.ShowFeedback(FreeWRibbonFloatingFeedbackCatalog.ShapeStyles);
                    return;
                }

                var preset = ShapeStylePreset.Catalog.FirstOrDefault(item =>
                    string.Equals(item.Id, context.SelectedValue, StringComparison.OrdinalIgnoreCase));
                if (preset is not null)
                    ports.ApplyShapeStyle(preset);
            },
            () => new RibbonCommandState(IsEnabled: ports.ShowFeedback is not null || CanFormatShape(ports)),
            ports.PrepareExecution));
        foreach (var preset in ShapeStylePreset.Catalog)
        {
            var captured = preset;
            bindings.Register($"freew.{captured.Id}", Stateful(
                () => ports.ApplyShapeStyle(captured),
                () => CanFormatShape(ports),
                ports.PrepareExecution));
        }

        bindings.Bind(FreeWRibbonCommandAction.ObjectGroup, Stateful(
            () => ExecuteOrShowFeedback(
                ports.CanGroup,
                ports.Group,
                ports.ShowFeedback,
                FreeWRibbonFloatingFeedbackCatalog.GroupSelectionRequired),
            () => ports.ShowFeedback is not null || ports.CanGroup(),
            ports.PrepareExecution));
        bindings.Bind(FreeWRibbonCommandAction.ObjectUngroup, Stateful(
            () => ExecuteOrShowFeedback(
                ports.CanUngroup,
                ports.Ungroup,
                ports.ShowFeedback,
                FreeWRibbonFloatingFeedbackCatalog.UngroupSelectionRequired),
            () => ports.ShowFeedback is not null || ports.CanUngroup(),
            ports.PrepareExecution));

    }

    public static FreeWRibbonChartSmartArtCommands RegisterChartSmartArt(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonChartSmartArtExecutionPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(ports);

        bindings.Register("freew.chart-type", EmptyRibbonCommand.Instance);
        foreach (var kind in Enum.GetValues<ChartKind>())
        {
            var captured = kind;
            bindings.Register($"freew.chart-type-{captured.ToString().ToLowerInvariant()}", Stateful(
                () => ports.SetChartKind(captured),
                () => ports.SelectedChart() is not null,
                ports.PrepareExecution));
        }

        bindings.Register("freew.chart-style", EmptyRibbonCommand.Instance);
        foreach (var style in ChartStyle.Catalog)
        {
            var captured = style;
            bindings.Register(
                $"freew.chart-style-{captured.Id}",
                ChartGalleryCommand(
                    captured,
                    ports.ApplyChartStyle,
                    ports.PreviewChartStyle,
                    ports.CancelChartDesignPreview,
                    ports.CommitChartStyle,
                    ports));
        }

        foreach (var layout in ChartQuickLayout.Catalog)
        {
            var captured = layout;
            bindings.Register(
                $"freew.chart-quick-layout-{captured.Id}",
                ChartGalleryCommand(
                    captured,
                    ports.ApplyChartQuickLayout,
                    ports.PreviewChartQuickLayout,
                    ports.CancelChartDesignPreview,
                    ports.CommitChartQuickLayout,
                    ports));
        }

        bindings.Register(ChartColorRibbonCommandCatalog.ParentCommandId, EmptyRibbonCommand.Instance);
        foreach (var scheme in ChartColorScheme.Catalog)
        {
            var captured = scheme;
            bindings.Register(
                ChartColorRibbonCommandCatalog.CommandId(captured),
                ChartGalleryCommand(
                    captured,
                    ports.ApplyChartColorScheme,
                    ports.PreviewChartColorScheme,
                    ports.CancelChartDesignPreview,
                    ports.CommitChartColorScheme,
                    ports));
        }

        var chartLegend = new FreeWRibbonStatefulPortCommand(
            _ => ports.ToggleChartLegend(),
            () => BuildChartLegendState(ports.SelectedChart()),
            ports.PrepareExecution);
        bindings.Bind(FreeWRibbonCommandAction.ChartToggleLegend, chartLegend);
        bindings.Bind(FreeWRibbonCommandAction.ChartTitle, AsyncStateful(
            _ => ExecuteSelectedDialogAsync(
                ports.SelectedChart,
                ports.ShowChartTitleDialogAsync,
                ports.ApplyChartTitleOutcome,
                ports.CompleteExecution,
                ports.ToggleChartTitleFallback),
            () => ports.SelectedChart() is not null
                && (ports.ShowChartTitleDialogAsync is not null || ports.ToggleChartTitleFallback is not null),
            ports.PrepareExecution));
        bindings.Bind(FreeWRibbonCommandAction.ChartAxisTitles, AsyncStateful(
            _ => ExecuteSelectedDialogAsync(
                ports.SelectedChart,
                ports.ShowChartAxisTitlesDialogAsync,
                ports.ApplyChartAxisTitlesOutcome,
                ports.CompleteExecution,
                ports.ToggleChartAxisTitlesFallback),
            () => ports.SelectedChart() is not null
                && (ports.ShowChartAxisTitlesDialogAsync is not null || ports.ToggleChartAxisTitlesFallback is not null),
            ports.PrepareExecution));
        bindings.Bind(FreeWRibbonCommandAction.ChartEditData, AsyncStateful(
            context => ExecuteChartDataAsync(ports, context.SelectedValue),
            () => ports.SelectedChart() is not null,
            ports.PrepareExecution));
        var chartSizeCommand = AsyncStateful(
            context => ExecuteChartSizeAsync(ports, context.SelectedValue),
            () => ports.SelectedChart() is not null,
            ports.PrepareExecution);
        bindings.Bind(FreeWRibbonCommandAction.ChartSize, chartSizeCommand);
        bindings.Bind(FreeWRibbonCommandAction.ChartSizeDialog, chartSizeCommand);

        bindings.Register("freew.smartart-layout", EmptyRibbonCommand.Instance);
        foreach (var preset in SmartArtLayoutPreset.Catalog)
        {
            var captured = preset;
            bindings.Register(
                $"freew.smartart-layout-{captured.Id}",
                CatalogGalleryCommand(
                    captured,
                    ports.ApplySmartArtLayout,
                    ports.PreviewSmartArtLayout,
                    ports.CancelSmartArtDesignPreview,
                    ports.CommitSmartArtLayout,
                    () => SmartArtCommandPlanner.CanEdit(ports.SelectedSmartArt()),
                    ports.PrepareExecution));
        }
        RegisterSmartArtLayoutAlias(bindings, ports, "freew.smartart-layout-list", SmartArtKind.List);
        RegisterSmartArtLayoutAlias(bindings, ports, "freew.smartart-layout-process", SmartArtKind.Process);
        RegisterSmartArtLayoutAlias(bindings, ports, "freew.smartart-layout-cycle", SmartArtKind.Process);
        RegisterSmartArtLayoutAlias(bindings, ports, "freew.smartart-layout-hierarchy", SmartArtKind.Hierarchy);

        bindings.Register("freew.smartart-colors", EmptyRibbonCommand.Instance);
        foreach (var scheme in SmartArtColorScheme.Catalog)
        {
            var captured = scheme;
            bindings.Register(
                $"freew.smartart-colors-{captured.Id}",
                CatalogGalleryCommand(
                    captured,
                    ports.ApplySmartArtColorScheme,
                    ports.PreviewSmartArtColorScheme,
                    ports.CancelSmartArtDesignPreview,
                    ports.CommitSmartArtColorScheme,
                    () => SmartArtCommandPlanner.CanEdit(ports.SelectedSmartArt()),
                    ports.PrepareExecution));
        }

        foreach (var style in SmartArtStyle.Catalog)
        {
            var captured = style;
            bindings.Register(
                SmartArtCommandPlanner.StyleCommandId(captured),
                CatalogGalleryCommand(
                    captured,
                    ports.ApplySmartArtStyle,
                    ports.PreviewSmartArtStyle,
                    ports.CancelSmartArtDesignPreview,
                    ports.CommitSmartArtStyle,
                    () => SmartArtCommandPlanner.CanEdit(ports.SelectedSmartArt()),
                    ports.PrepareExecution));
        }

        BindSmartArtStructure(bindings, ports, FreeWRibbonCommandAction.SmartartAddShape, SmartArtStructureOperation.AddShape);
        BindSmartArtStructure(bindings, ports, FreeWRibbonCommandAction.SmartartRemoveShape, SmartArtStructureOperation.RemoveShape);
        BindSmartArtStructure(bindings, ports, FreeWRibbonCommandAction.SmartartPromote, SmartArtStructureOperation.Promote);
        BindSmartArtStructure(bindings, ports, FreeWRibbonCommandAction.SmartartDemote, SmartArtStructureOperation.Demote);
        BindSmartArtStructure(bindings, ports, FreeWRibbonCommandAction.SmartartMoveUp, SmartArtStructureOperation.MoveUp);
        BindSmartArtStructure(bindings, ports, FreeWRibbonCommandAction.SmartartMoveDown, SmartArtStructureOperation.MoveDown);
        bindings.Bind(FreeWRibbonCommandAction.SmartartEditText, AsyncStateful(
            context => ExecuteSmartArtEditAsync(ports, context.SelectedValue),
            () => SmartArtCommandPlanner.CanEdit(ports.SelectedSmartArt()),
            ports.PrepareExecution));
        bindings.Bind(FreeWRibbonCommandAction.SmartartChangeStyle, new FreeWRibbonStatefulPortCommand(
            context =>
            {
                if (SmartArtCommandPlanner.ResolveStyle(context.SelectedValue) is { } style)
                    (ports.CommitSmartArtStyle ?? ports.ApplySmartArtStyle)(style);
            },
            () => new RibbonCommandState(IsEnabled: SmartArtCommandPlanner.CanEdit(ports.SelectedSmartArt())),
            ports.PrepareExecution));

        return new FreeWRibbonChartSmartArtCommands(chartLegend);
    }

    public static RibbonCommandState BuildChartLegendState(Chart? chart)
    {
        if (chart is null)
            return new RibbonCommandState(IsEnabled: false, IsChecked: false);

        var state = ChartSmartArtVisualPlanner.BuildChartElementCommandState(chart);
        return new RibbonCommandState(
            IsEnabled: state.CanToggleLegend,
            IsChecked: state.IsLegendVisible);
    }

    public static void RegisterImageTableWorkflows(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonImageExecutionPorts imagePorts,
        FreeWRibbonTableExecutionPorts tablePorts)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(imagePorts);
        ArgumentNullException.ThrowIfNull(tablePorts);

        bindings.Bind(FreeWRibbonCommandAction.ImageCrop, AsyncStateful(
            _ => ExecuteSelectedDialogAsync(
                imagePorts.SelectedImage,
                imagePorts.ShowCropDialogAsync,
                imagePorts.ApplyCropOutcome,
                imagePorts.CompleteExecution),
            () => imagePorts.SelectedImage() is not null && imagePorts.ShowCropDialogAsync is not null,
            imagePorts.PrepareExecution));
        bindings.Bind(FreeWRibbonCommandAction.ImageReset, Stateful(
            imagePorts.ResetImage,
            () => imagePorts.SelectedImage() is not null,
            imagePorts.PrepareExecution));

        bindings.Bind(FreeWRibbonCommandAction.TableFormula, AsyncStateful(
            _ => ExecuteTableFormulaAsync(tablePorts),
            () => tablePorts.SelectedCell() is not null && tablePorts.ShowFormulaDialogAsync is not null,
            tablePorts.PrepareExecution));
        var propertiesCommand = AsyncStateful(
            _ => ExecuteSelectedDialogAsync(
                tablePorts.SelectedContext,
                tablePorts.ShowPropertiesDialogAsync,
                tablePorts.ApplyPropertiesOutcome,
                tablePorts.CompleteExecution),
            () => tablePorts.SelectedContext() is not null && tablePorts.ShowPropertiesDialogAsync is not null,
            tablePorts.PrepareExecution);
        bindings.Bind(FreeWRibbonCommandAction.TableProperties, propertiesCommand);
        bindings.Bind(FreeWRibbonCommandAction.TableRowHeight, propertiesCommand);
        bindings.Bind(FreeWRibbonCommandAction.TableColWidth, propertiesCommand);
        bindings.Bind(FreeWRibbonCommandAction.TableCellMargins, propertiesCommand);
        bindings.Bind(FreeWRibbonCommandAction.TableToText, AsyncStateful(
            _ => ExecuteTableToTextAsync(tablePorts),
            () => tablePorts.CanConvertToText() && tablePorts.ShowTableToTextDialogAsync is not null,
            tablePorts.PrepareExecution));
    }

    private static ValueTask ExecuteChartDataAsync(
        FreeWRibbonChartSmartArtExecutionPorts ports,
        string? selectedValue)
    {
        if (ports.SelectedChart() is not { } chart)
            return Complete(ports.CompleteExecution);
        if (ChartDataPresetCatalog.TryCreateNamedReplacement(selectedValue, out var preset))
        {
            ports.ApplyChartDataOutcome(preset);
            return Complete(ports.CompleteExecution);
        }
        return !string.IsNullOrWhiteSpace(selectedValue) || ports.ShowChartDataDialogAsync is null
            ? Complete(ports.CompleteExecution)
            : ApplyDialogOutcomeAsync(
                ports.ShowChartDataDialogAsync(chart),
                ports.ApplyChartDataOutcome,
                ports.CompleteExecution);
    }

    private static ValueTask ExecuteChartSizeAsync(
        FreeWRibbonChartSmartArtExecutionPorts ports,
        string? selectedValue)
    {
        if (ports.SelectedChart() is not { } chart)
            return Complete(ports.CompleteExecution);
        if (FreeWRibbonNumericValueParser.TryParseChartSize(
                selectedValue,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed))
        {
            ports.ApplyChartSizeOutcome(new ChartSizeDialogResult(parsed.WidthPt, parsed.HeightPt));
            return Complete(ports.CompleteExecution);
        }
        return !string.IsNullOrWhiteSpace(selectedValue) || ports.ShowChartSizeDialogAsync is null
            ? Complete(ports.CompleteExecution)
            : ApplyDialogOutcomeAsync(
                ports.ShowChartSizeDialogAsync(chart),
                ports.ApplyChartSizeOutcome,
                ports.CompleteExecution);
    }

    private static ValueTask ExecuteSmartArtEditAsync(
        FreeWRibbonChartSmartArtExecutionPorts ports,
        string? selectedValue)
    {
        if (ports.SelectedSmartArt() is not { } smartArt)
            return Complete(ports.CompleteExecution);
        if (selectedValue is not null)
        {
            if (SmartArtCommandPlanner.BuildEditedContent(smartArt.Kind, selectedValue) is { } replacement)
                ports.ApplySmartArtEditOutcome(replacement);
            return Complete(ports.CompleteExecution);
        }
        return ports.ShowSmartArtEditDialogAsync is null
            ? Complete(ports.CompleteExecution)
            : ApplyDialogOutcomeAsync(
                ports.ShowSmartArtEditDialogAsync(smartArt),
                ports.ApplySmartArtEditOutcome,
                ports.CompleteExecution);
    }

    private static ValueTask ExecuteTableFormulaAsync(FreeWRibbonTableExecutionPorts ports)
    {
        if (ports.SelectedCell() is not { } cell || ports.ShowFormulaDialogAsync is null)
            return Complete(ports.CompleteExecution);
        var initialState = TableFormulaDialogPlanner.BuildInitialState(
            cell.Table,
            cell.RowIndex,
            cell.ColumnIndex);
        return ApplyDialogOutcomeAsync(
            ports.ShowFormulaDialogAsync(initialState),
            ports.ApplyFormulaOutcome,
            ports.CompleteExecution);
    }

    private static async ValueTask ExecuteTableToTextAsync(FreeWRibbonTableExecutionPorts ports)
    {
        try
        {
            if (ports.ShowTableToTextDialogAsync is null)
                return;
            if (await ports.ShowTableToTextDialogAsync() is { } outcome)
                ports.ApplyTableToTextOutcome(outcome);
        }
        finally
        {
            ports.CompleteExecution();
        }
    }

    private static ValueTask ExecuteSelectedDialogAsync<TSelection, TOutcome>(
        Func<TSelection?> selected,
        Func<TSelection, ValueTask<TOutcome?>>? showDialogAsync,
        Action<TOutcome> applyOutcome,
        Action completeExecution,
        Action? fallback = null)
        where TSelection : class
        where TOutcome : class
    {
        var selection = selected();
        if (selection is not null && showDialogAsync is not null)
            return ApplyDialogOutcomeAsync(showDialogAsync(selection), applyOutcome, completeExecution);

        if (selection is not null)
            fallback?.Invoke();
        completeExecution();
        return ValueTask.CompletedTask;
    }

    private static async ValueTask ApplyDialogOutcomeAsync<TOutcome>(
        ValueTask<TOutcome?> pendingOutcome,
        Action<TOutcome> applyOutcome,
        Action completeExecution)
        where TOutcome : class
    {
        try
        {
            if (await pendingOutcome is { } outcome)
                applyOutcome(outcome);
        }
        finally
        {
            completeExecution();
        }
    }

    private static ValueTask Complete(Action completeExecution)
    {
        completeExecution();
        return ValueTask.CompletedTask;
    }

    private static void RegisterShapeFillOutline(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonFloatingExecutionPorts ports)
    {
        bindings.Register(ObjectFormatCommandPlanner.ShapeFillCommandId, Stateful(
            static () => { },
            () => CanFormatShape(ports),
            ports.PrepareExecution));
        foreach (var command in ObjectFormatCommandPlanner.ShapeFillCommands())
        {
            var captured = command;
            bindings.Register(captured.CommandId, Stateful(
                () =>
                {
                    if (captured.Kind == ObjectFormatShapeFillKind.NoFill)
                    {
                        ports.SetShapeExtendedFill(null);
                        ports.SetShapeFill(null);
                    }
                    else if (ObjectFormatCommandPlanner.UsesExtendedShapeFill(captured.Kind))
                    {
                        ports.SetShapeExtendedFill(ObjectFormatCommandPlanner.BuildShapeExtendedFill(captured.Kind));
                    }
                },
                () => CanFormatShape(ports),
                ports.PrepareExecution));
        }

        bindings.Register(ObjectFormatCommandPlanner.ShapeOutlineCommandId, Stateful(
            static () => { },
            () => CanFormatShape(ports),
            ports.PrepareExecution));
        foreach (var command in ObjectFormatCommandPlanner.ShapeOutlineCommands())
        {
            var captured = command;
            bindings.Register(captured.CommandId, Stateful(
                () =>
                {
                    var shape = ports.SelectedShape();
                    if (shape is null)
                        return;
                    var plan = ObjectFormatCommandPlanner.PlanShapeOutline(
                        captured.Kind,
                        shape.OutlineColorHex,
                        shape.OutlineWidthPt);
                    ports.SetShapeOutline(plan.ColorHex, plan.WidthPt, plan.Dash);
                },
                () => CanFormatShape(ports),
                ports.PrepareExecution));
        }
    }

    private static void BindShapeEffects(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonFloatingExecutionPorts ports)
    {
        bindings.Bind(FreeWRibbonCommandAction.ShapeEffects, Stateful(
            () =>
            {
                if (ports.ShowFeedback is not null)
                    ports.ShowFeedback(FreeWRibbonFloatingFeedbackCatalog.ShapeEffects);
                else
                    ports.SetShapeEffects(null);
            },
            () => ports.ShowFeedback is not null || ports.SelectedShape() is not null,
            ports.PrepareExecution));
        BindShapeEffect(bindings, ports, FreeWRibbonCommandAction.ShapeEffectsNone, null);
        BindShapeEffect(bindings, ports, FreeWRibbonCommandAction.ShapeEffectShadow, new ShapeEffectLst { HasShadow = true });
        BindShapeEffect(bindings, ports, FreeWRibbonCommandAction.ShapeEffectGlow, new ShapeEffectLst { HasGlow = true });
        BindShapeEffect(bindings, ports, FreeWRibbonCommandAction.ShapeEffectSoftEdge, new ShapeEffectLst { HasSoftEdge = true });
        BindShapeEffect(bindings, ports, FreeWRibbonCommandAction.ShapeEffectReflection, new ShapeEffectLst { HasReflection = true });
        BindShapeEffect(bindings, ports, FreeWRibbonCommandAction.ShapeEffectBevel, new ShapeEffectLst { HasBevel = true });
    }

    private static void BindShapeEffect(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonFloatingExecutionPorts ports,
        FreeWRibbonCommandAction action,
        ShapeEffectLst? effect) =>
        bindings.Bind(action, Stateful(
            () => ports.SetShapeEffects(effect?.Clone()),
            () => ports.SelectedShape() is not null,
            ports.PrepareExecution));

    private static void BindAlignmentCommands(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonFloatingExecutionPorts ports,
        ObjectFormatTarget target)
    {
        var left = target == ObjectFormatTarget.Picture
            ? FreeWRibbonCommandAction.ImageAlignLeft
            : FreeWRibbonCommandAction.ShapeAlignLeft;
        var center = target == ObjectFormatTarget.Picture
            ? FreeWRibbonCommandAction.ImageAlignCenter
            : FreeWRibbonCommandAction.ShapeAlignCenter;
        var right = target == ObjectFormatTarget.Picture
            ? FreeWRibbonCommandAction.ImageAlignRight
            : FreeWRibbonCommandAction.ShapeAlignRight;

        BindAlignment(bindings, ports, target, left, TextAlignment.Left);
        BindAlignment(bindings, ports, target, center, TextAlignment.Center);
        BindAlignment(bindings, ports, target, right, TextAlignment.Right);
    }

    private static void BindAlignment(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonFloatingExecutionPorts ports,
        ObjectFormatTarget target,
        FreeWRibbonCommandAction action,
        TextAlignment alignment) =>
        bindings.Bind(action, Stateful(
            () => ports.ApplyParagraphAlignment(target, alignment),
            () => ports.HasSelection(target),
            ports.PrepareExecution));

    private static void BindArrangeCommands(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonFloatingExecutionPorts ports,
        ObjectFormatTarget target)
    {
        var alignPage = target == ObjectFormatTarget.Picture
            ? FreeWRibbonCommandAction.ImageAlignToPage
            : FreeWRibbonCommandAction.ShapeAlignToPage;
        var alignMargin = target == ObjectFormatTarget.Picture
            ? FreeWRibbonCommandAction.ImageAlignToMargin
            : FreeWRibbonCommandAction.ShapeAlignToMargin;
        var distributeH = target == ObjectFormatTarget.Picture
            ? FreeWRibbonCommandAction.ImageDistributeH
            : FreeWRibbonCommandAction.ShapeDistributeH;
        var distributeV = target == ObjectFormatTarget.Picture
            ? FreeWRibbonCommandAction.ImageDistributeV
            : FreeWRibbonCommandAction.ShapeDistributeV;

        BindArrange(bindings, ports, alignPage, FloatingObjectArrangeKind.AlignToPage);
        BindArrange(bindings, ports, alignMargin, FloatingObjectArrangeKind.AlignToMargin);
        BindArrange(bindings, ports, distributeH, FloatingObjectArrangeKind.DistributeHorizontal);
        BindArrange(bindings, ports, distributeV, FloatingObjectArrangeKind.DistributeVertical);
    }

    private static void BindArrange(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonFloatingExecutionPorts ports,
        FreeWRibbonCommandAction action,
        FloatingObjectArrangeKind kind) =>
        bindings.Bind(action, Stateful(
            () => ports.Arrange(kind),
            () => ports.CanArrange(kind),
            ports.PrepareExecution));

    private static void RegisterLayoutArrangeCommands(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonFloatingExecutionPorts ports)
    {
        bindings.Register("freew.layout-wrap", EmptyRibbonCommand.Instance);
        bindings.Register("freew.layout-rotate", EmptyRibbonCommand.Instance);
        bindings.Register("freew.layout-position", EmptyRibbonCommand.Instance);

        foreach (var preset in LayoutPositionPresets)
        {
            var captured = preset;
            bindings.Register(
                $"freew.layout-position-{captured.Suffix}",
                Stateful(
                    () => TryWithSelectedLayoutTarget(ports, target => ports.ApplyPosition?.Invoke(target, captured.Input)),
                    () => ports.ApplyPosition is not null && HasLayoutSelection(ports),
                    ports.PrepareExecution));
        }

        foreach (var command in ObjectFormatCommandPlanner.WrapCommands(ObjectFormatTarget.Picture))
        {
            var captured = command;
            bindings.Register(
                LayoutCommandId(captured.CommandId),
                Stateful(
                    () => TryWithSelectedLayoutTarget(ports, target => ports.ApplyWrap(target, captured.Wrapping)),
                    () => HasLayoutSelection(ports),
                    ports.PrepareExecution));
        }

        foreach (var command in ObjectFormatCommandPlanner.ZOrderCommands(ObjectFormatTarget.Picture))
        {
            var captured = command;
            bindings.Register(
                LayoutCommandId(captured.CommandId),
                Stateful(
                    () => TryWithSelectedLayoutTarget(ports, target => ports.ApplyZOrder(target, captured.Operation)),
                    () => HasLayoutSelection(ports),
                    ports.PrepareExecution));
        }

        foreach (var command in ObjectFormatCommandPlanner.TransformCommands(ObjectFormatTarget.Picture))
        {
            var captured = command;
            bindings.Register(
                LayoutCommandId(captured.CommandId),
                Stateful(
                    () => TryWithSelectedLayoutTarget(ports, target => ports.ApplyTransform(target, captured)),
                    () => HasLayoutSelection(ports),
                    ports.PrepareExecution));
        }
    }

    private static bool HasLayoutSelection(FreeWRibbonFloatingExecutionPorts ports) =>
        ports.HasSelection(ObjectFormatTarget.Picture) || ports.HasSelection(ObjectFormatTarget.Shape);

    private static void TryWithSelectedLayoutTarget(
        FreeWRibbonFloatingExecutionPorts ports,
        Action<ObjectFormatTarget> apply)
    {
        if (ports.HasSelection(ObjectFormatTarget.Picture))
            apply(ObjectFormatTarget.Picture);
        else if (ports.HasSelection(ObjectFormatTarget.Shape))
            apply(ObjectFormatTarget.Shape);
    }

    private static string LayoutCommandId(string targetCommandId) =>
        targetCommandId.Replace("freew.image-", "freew.layout-", StringComparison.Ordinal);

    private static IReadOnlyList<(string Suffix, FreeWRibbonObjectPositionInput Input)> LayoutPositionPresets { get; } =
    [
        ("column-paragraph", new(0, 0, HorizontalAnchor.Column, VerticalAnchor.Paragraph)),
        ("margin-paragraph", new(0, 0, HorizontalAnchor.Margin, VerticalAnchor.Paragraph)),
        ("page-paragraph", new(0, 0, HorizontalAnchor.Page, VerticalAnchor.Paragraph)),
        ("page-top", new(0, 0, HorizontalAnchor.Page, VerticalAnchor.Page)),
    ];

    private static void BindShapeKind(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonFloatingExecutionPorts ports,
        FreeWRibbonCommandAction action,
        ShapeKind kind) =>
        bindings.Bind(action, Stateful(
            () => ports.SetShapeKind(kind),
            () => ports.SelectedShape() is not null,
            ports.PrepareExecution));

    private static void BindShapeTextDirection(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonFloatingExecutionPorts ports,
        FreeWRibbonCommandAction action,
        ShapeTextDirection direction) =>
        bindings.Bind(action, Stateful(
            () => ports.SetShapeTextDirection(direction),
            () => ports.SelectedShape() is not null,
            ports.PrepareExecution));

    private static void BindSmartArtStructure(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonChartSmartArtExecutionPorts ports,
        FreeWRibbonCommandAction action,
        SmartArtStructureOperation operation) =>
        bindings.Bind(action, Stateful(
            () => ports.MutateSmartArt(operation),
            () => SmartArtCommandPlanner.IsEnabled(ports.SelectedSmartArt(), operation),
            ports.PrepareExecution));

    private static void RegisterSmartArtLayoutAlias(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonChartSmartArtExecutionPorts ports,
        RibbonCommandId commandId,
        SmartArtKind kind)
    {
        var preset = SmartArtLayoutPreset.Catalog.First(item => item.Kind == kind);
        bindings.Register(commandId, Stateful(
            () => ports.ApplySmartArtLayout(preset),
            () => SmartArtCommandPlanner.CanEdit(ports.SelectedSmartArt()),
            ports.PrepareExecution));
    }

    private static bool CanFormatShape(FreeWRibbonFloatingExecutionPorts ports) =>
        ObjectFormatCommandPlanner.CanFormatShapeFillOutline(ports.SelectedShape()?.Kind);

    private static IRibbonStatefulCommand Stateful(
        Action execute,
        Func<bool> isEnabled,
        Action? prepareExecution = null) =>
        new FreeWRibbonStatefulPortCommand(
            _ => execute(),
            () => new RibbonCommandState(IsEnabled: isEnabled()),
            prepareExecution);

    private static IRibbonStatefulCommand ChartGalleryCommand<T>(
        T value,
        Action<T> apply,
        Action<T>? preview,
        Action? cancelPreview,
        Action<T>? commit,
        FreeWRibbonChartSmartArtExecutionPorts ports)
        where T : class =>
        CatalogGalleryCommand(
            value,
            apply,
            preview,
            cancelPreview,
            commit,
            () => ports.SelectedChart() is not null,
            ports.PrepareExecution);

    private static IRibbonStatefulCommand CatalogGalleryCommand<T>(
        T value,
        Action<T> apply,
        Action<T>? preview,
        Action? cancelPreview,
        Action<T>? commit,
        Func<bool> isEnabled,
        Action prepareExecution)
        where T : class =>
        preview is not null && cancelPreview is not null && commit is not null
            ? new PreviewableCatalogCommand<T>(
                value,
                preview,
                cancelPreview,
                commit,
                isEnabled,
                prepareExecution)
            : Stateful(
                () => apply(value),
                isEnabled,
                prepareExecution);

    private sealed class PreviewableCatalogCommand<T>(
        T value,
        Action<T> preview,
        Action cancelPreview,
        Action<T> commit,
        Func<bool> isEnabled,
        Action prepareExecution) : IRibbonPreviewCommand, IRibbonStatefulCommand
        where T : class
    {
        public void BeginPreview(RibbonCommandContext context) => preview(value);

        public void CancelPreview() => cancelPreview();

        public void Execute(RibbonCommandContext context)
        {
            prepareExecution();
            commit(value);
        }

        public RibbonCommandState GetState() => new(IsEnabled: isEnabled());
    }

    private static void ExecuteOrShowFeedback(
        Func<bool> canExecute,
        Action execute,
        Action<FreeWRibbonFloatingFeedback>? showFeedback,
        FreeWRibbonFloatingFeedback feedback)
    {
        if (canExecute())
            execute();
        else
            showFeedback?.Invoke(feedback);
    }

    private static IRibbonStatefulCommand AsyncStateful(
        Func<RibbonCommandContext, ValueTask> executeAsync,
        Func<bool> isEnabled,
        Action? prepareExecution = null) =>
        new FreeWRibbonAsyncStatefulPortCommand(
            executeAsync,
            () => new RibbonCommandState(IsEnabled: isEnabled()),
            prepareExecution);
}
