using Free.Shared.Ribbon;
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

public sealed record FreeWRibbonFloatingExecutionPorts(
    Action PrepareExecution,
    Func<ObjectFormatTarget, bool> HasSelection,
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
    IReadOnlyDictionary<FreeWRibbonCommandAction, IRibbonCommand>? NativeCanonicalCommands = null);

public sealed record FreeWRibbonChartSmartArtExecutionPorts(
    Action PrepareExecution,
    Func<Chart?> SelectedChart,
    Action<ChartKind> SetChartKind,
    Action<ChartStyle> ApplyChartStyle,
    Action<ChartColorScheme> ApplyChartColorScheme,
    Action<ChartQuickLayout> ApplyChartQuickLayout,
    Action ToggleChartLegend,
    IRibbonCommand ChartTitleCommand,
    IRibbonCommand ChartAxisTitlesCommand,
    IRibbonCommand ChartEditDataCommand,
    IRibbonCommand ChartSizeCommand,
    Func<SmartArt?> SelectedSmartArt,
    Action<SmartArtStructureOperation> MutateSmartArt,
    Action<SmartArtLayoutPreset> ApplySmartArtLayout,
    Action<SmartArtColorScheme> ApplySmartArtColorScheme,
    Action<SmartArtStyle> ApplySmartArtStyle,
    IRibbonCommand SmartArtEditTextCommand,
    string ChartColorCommandPrefix = "freew.chart-colors");

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
        FreeWRibbonCommandAction.TableInsertColLeft,
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
                    () => ports.HasSelection(target),
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

        bindings.Bind(FreeWRibbonCommandAction.ShapeEditShape, Stateful(
            static () => { },
            () => ports.SelectedShape() is not null,
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
            static () => { },
            () => ports.SelectedShape() is not null,
            ports.PrepareExecution));
        BindShapeTextDirection(bindings, ports, FreeWRibbonCommandAction.ShapeTextHorizontal, ShapeTextDirection.Horizontal);
        BindShapeTextDirection(bindings, ports, FreeWRibbonCommandAction.ShapeTextRotate90, ShapeTextDirection.Rotate90);
        BindShapeTextDirection(bindings, ports, FreeWRibbonCommandAction.ShapeTextRotate270, ShapeTextDirection.Rotate270);

        RegisterShapeFillOutline(bindings, ports);
        BindShapeEffects(bindings, ports);

        bindings.Bind(FreeWRibbonCommandAction.ShapeStylesGallery, new FreeWRibbonStatefulPortCommand(
            context =>
            {
                var preset = ShapeStylePreset.Catalog.FirstOrDefault(item =>
                    string.Equals(item.Id, context.SelectedValue, StringComparison.OrdinalIgnoreCase));
                if (preset is not null)
                    ports.ApplyShapeStyle(preset);
            },
            () => new RibbonCommandState(IsEnabled: CanFormatShape(ports)),
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
            ports.Group,
            ports.CanGroup,
            ports.PrepareExecution));
        bindings.Bind(FreeWRibbonCommandAction.ObjectUngroup, Stateful(
            ports.Ungroup,
            ports.CanUngroup,
            ports.PrepareExecution));

        if (ports.NativeCanonicalCommands is not null)
        {
            foreach (var (action, command) in ports.NativeCanonicalCommands)
                bindings.Bind(action, command);
        }

    }

    public static void RegisterChartSmartArt(
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
            bindings.Register($"freew.chart-style-{captured.Id}", Stateful(
                () => ports.ApplyChartStyle(captured),
                () => ports.SelectedChart() is not null,
                ports.PrepareExecution));
        }

        foreach (var layout in ChartQuickLayout.Catalog)
        {
            var captured = layout;
            bindings.Register($"freew.chart-quick-layout-{captured.Id}", Stateful(
                () => ports.ApplyChartQuickLayout(captured),
                () => ports.SelectedChart() is not null,
                ports.PrepareExecution));
        }

        bindings.Register(ports.ChartColorCommandPrefix, EmptyRibbonCommand.Instance);
        foreach (var scheme in ChartColorScheme.Catalog)
        {
            var captured = scheme;
            bindings.Register($"{ports.ChartColorCommandPrefix}-{captured.Id}", Stateful(
                () => ports.ApplyChartColorScheme(captured),
                () => ports.SelectedChart() is not null,
                ports.PrepareExecution));
        }

        bindings.Bind(FreeWRibbonCommandAction.ChartToggleLegend, Stateful(
            ports.ToggleChartLegend,
            () => ports.SelectedChart() is not null,
            ports.PrepareExecution));
        bindings.Bind(FreeWRibbonCommandAction.ChartTitle, ports.ChartTitleCommand);
        bindings.Bind(FreeWRibbonCommandAction.ChartAxisTitles, ports.ChartAxisTitlesCommand);
        bindings.Bind(FreeWRibbonCommandAction.ChartEditData, ports.ChartEditDataCommand);
        bindings.Bind(FreeWRibbonCommandAction.ChartSize, ports.ChartSizeCommand);
        bindings.Bind(FreeWRibbonCommandAction.ChartSizeDialog, ports.ChartSizeCommand);

        bindings.Register("freew.smartart-layout", EmptyRibbonCommand.Instance);
        foreach (var preset in SmartArtLayoutPreset.Catalog)
        {
            var captured = preset;
            bindings.Register($"freew.smartart-layout-{captured.Id}", Stateful(
                () => ports.ApplySmartArtLayout(captured),
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
            bindings.Register($"freew.smartart-colors-{captured.Id}", Stateful(
                () => ports.ApplySmartArtColorScheme(captured),
                () => SmartArtCommandPlanner.CanEdit(ports.SelectedSmartArt()),
                ports.PrepareExecution));
        }

        BindSmartArtStructure(bindings, ports, FreeWRibbonCommandAction.SmartartAddShape, SmartArtStructureOperation.AddShape);
        BindSmartArtStructure(bindings, ports, FreeWRibbonCommandAction.SmartartRemoveShape, SmartArtStructureOperation.RemoveShape);
        BindSmartArtStructure(bindings, ports, FreeWRibbonCommandAction.SmartartPromote, SmartArtStructureOperation.Promote);
        BindSmartArtStructure(bindings, ports, FreeWRibbonCommandAction.SmartartDemote, SmartArtStructureOperation.Demote);
        BindSmartArtStructure(bindings, ports, FreeWRibbonCommandAction.SmartartMoveUp, SmartArtStructureOperation.MoveUp);
        BindSmartArtStructure(bindings, ports, FreeWRibbonCommandAction.SmartartMoveDown, SmartArtStructureOperation.MoveDown);
        bindings.Bind(FreeWRibbonCommandAction.SmartartEditText, ports.SmartArtEditTextCommand);
        bindings.Bind(FreeWRibbonCommandAction.SmartartChangeStyle, new FreeWRibbonStatefulPortCommand(
            context =>
            {
                if (SmartArtCommandPlanner.ResolveStyle(context.SelectedValue) is { } style)
                    ports.ApplySmartArtStyle(style);
            },
            () => new RibbonCommandState(IsEnabled: SmartArtCommandPlanner.CanEdit(ports.SelectedSmartArt())),
            ports.PrepareExecution));
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
        BindShapeEffect(bindings, ports, FreeWRibbonCommandAction.ShapeEffects, null);
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
}
