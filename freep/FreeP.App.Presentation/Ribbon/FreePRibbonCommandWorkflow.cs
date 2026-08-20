using System.Globalization;
using Free.Shared.Drawing;
using Free.Shared.Ribbon;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum FreePRibbonCommandGroup
{
    Slide,
    Clipboard,
    Text,
    Table,
    Insert,
    Picture,
    Shape,
    SmartArt,
    Chart,
    Link,
    Arrange,
    Design,
    Transition,
    Animation,
    View,
    Review,
    SlideShow,
}

public enum FreePRibbonHostActionKind
{
    Copy,
    Cut,
    Paste,
    InsertPicture,
    InsertVideo,
    InsertAudio,
    OpenTablePicker,
    ExecuteTableStructureAction,
    MergeTableCells,
    SplitTableCell,
    PickPictureBullet,
    InsertSlideZoom,
    InsertSectionZoom,
    InsertSummaryZoom,
    EditZoomTarget,
    EditSummaryZoomTargets,
    FormatZoom,
    SetZoomCoverImage,
    ResetZoomCoverImage,
    OpenHeaderFooter,
    DesignRequest,
    ApplySmartArtColor,
    ApplySmartArtLayout,
    ApplySmartArtQuickStyle,
    ConvertSmartArtToShapes,
    OpenSmartArtTextPane,
    OpenChartData,
    OpenChartDisplayOptions,
    OpenChartAxisOptions,
    OpenChartSeriesOptions,
    OpenChartPointOptions,
    OpenChartLayoutOptions,
    OpenChartExSeriesLayout,
    OpenChartDataTableOptions,
    OpenChartBubbleOptions,
    OpenChartPieOptions,
    OpenChartPlotStyleOptions,
    OpenChart3DViewOptions,
    OpenChartTextOptions,
    OpenChartAreaOptions,
    OpenChartProtectionOptions,
    OpenHyperlink,
    OpenRotationOptions,
    SetEditPointsEnabled,
    OpenFind,
    OpenReplace,
    ShowCommentsPane,
    ShowAccessibilityPane,
    ShowAltTextPane,
    ShowReadingOrderPane,
    ShowSelectionPane,
    ShowProofingPane,
    AddComment,
    EditComment,
    ReplyComment,
    DeleteComment,
    PreviousComment,
    NextComment,
    ResolveComment,
    ReopenComment,
    ApplyViewShowState,
    ApplyViewZoomState,
    PickTransitionSound,
    ToggleAnimationPane,
    StartSlideShowFromBeginning,
    StartSlideShowFromCurrent,
    RehearseTimings,
    RecordTimings,
    OpenCustomShows,
    OpenSlideShowSettings,
}

public enum FreePRibbonHostQueryKind
{
    BeginFormatPainter,
    EditPointsEnabled,
    AnimationPaneVisible,
    ViewShowState,
    ViewZoomState,
}

public enum FreePRibbonTextActionKind
{
    ToggleFormat,
    SetParagraphAlignment,
    ApplyListPreset,
    ToggleBullets,
    ToggleNumbering,
    Indent,
    Outdent,
    SetFontFamily,
    SetFontSize,
    SetColor,
    SetTextVerticalType,
    SetTableCellFill,
    SetTableCellAnchor,
    SetTableCellBorder,
    SetTableCellInset,
    SetTableRowHeight,
    RemoveHyperlink,
}

public sealed record FreePRibbonHostAction(
    FreePRibbonHostActionKind Kind,
    object? Argument = null);

public sealed record FreePRibbonHostQuery(
    FreePRibbonHostQueryKind Kind,
    object? Argument = null);

public sealed record FreePRibbonTextAction(
    FreePRibbonTextActionKind Kind,
    object? Argument = null,
    object? SecondaryArgument = null);

/// <summary>
/// Renderer boundary for native editors, pickers, dialogs, clipboard, and pane/window ownership.
/// Command ids and portable fallback behavior stay in <see cref="FreePRibbonCommandWorkflow"/>.
/// </summary>
public sealed class FreePRibbonCommandHostAdapter
{
    public Action<FreePRibbonHostAction>? ExecuteAction { get; init; }

    public Func<FreePRibbonHostAction, bool>? TryExecuteAction { get; init; }

    public Func<FreePRibbonHostQuery, object?>? QueryState { get; init; }

    public Func<FreePRibbonTextAction, bool>? TryHandleTextAction { get; init; }

    internal bool Execute(FreePRibbonHostActionKind kind, object? argument = null)
    {
        var action = new FreePRibbonHostAction(kind, argument);
        if (TryExecuteAction is not null)
            return TryExecuteAction(action);
        if (ExecuteAction is null)
            return false;

        ExecuteAction(action);
        return true;
    }

    internal bool TryQuery<T>(FreePRibbonHostQueryKind kind, out T value, object? argument = null)
    {
        if (QueryState?.Invoke(new FreePRibbonHostQuery(kind, argument)) is T result)
        {
            value = result;
            return true;
        }

        value = default!;
        return false;
    }

    internal bool TryHandle(FreePRibbonTextActionKind kind, object? argument = null, object? secondary = null) =>
        TryHandleTextAction?.Invoke(new FreePRibbonTextAction(kind, argument, secondary)) == true;
}

public sealed record FreePRibbonCommandBuildResult(
    RibbonCommandRegistry Registry,
    IReadOnlyDictionary<FreePRibbonCommandGroup, IReadOnlyList<RibbonCommandId>> CommandGroups)
{
    public IReadOnlyList<RibbonCommandId> CommonCommandIds =>
        CommandGroups.Values.SelectMany(static commands => commands).ToArray();
}

/// <summary>
/// Owns FreeP's renderer-neutral ribbon command registry, grouping, state policy, and application routing.
/// </summary>
public static class FreePRibbonCommandWorkflow
{
    public static FreePRibbonCommandBuildResult Build(
        EditingSession editor,
        RibbonStateStore stateStore,
        FreePRibbonCommandHostAdapter? host = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(stateStore);

        host ??= new FreePRibbonCommandHostAdapter();
        var commands = new Registrar();

        RegisterSlideCommands(commands, editor, host);
        RegisterClipboardCommands(commands, editor, host);
        RegisterTextCommands(commands, editor, stateStore, host);
        RegisterTableCommands(commands, editor, host);
        RegisterInsertCommands(commands, editor, host);
        RegisterPictureAndShapeCommands(commands, editor);
        RegisterSmartArtCommands(commands, host);
        RegisterChartCommands(commands, editor, host);
        RegisterLinkCommands(commands, editor, host);
        RegisterArrangeCommands(commands, editor, stateStore, host);
        RegisterDesignCommands(commands, editor, host);
        RegisterTransitionCommands(commands, editor, stateStore, host);
        RegisterAnimationCommands(commands, editor, stateStore, host);
        RegisterViewCommands(commands, stateStore, host);
        RegisterReviewCommands(commands, host);
        RegisterSlideShowCommands(commands, host);

        return commands.Build();
    }

    public static FreePRibbonCommandBuildResult BindInto(
        RibbonCommandRegistry target,
        EditingSession editor,
        RibbonStateStore stateStore,
        FreePRibbonCommandHostAdapter? host = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        var result = Build(editor, stateStore, host);
        foreach (var commandId in result.CommonCommandIds)
        {
            if (result.Registry.TryGet(commandId, out var command) && command is not null)
                target.Register(commandId, command);
        }

        return new FreePRibbonCommandBuildResult(target, result.CommandGroups);
    }

    private static void RegisterSlideCommands(
        Registrar commands,
        EditingSession editor,
        FreePRibbonCommandHostAdapter host)
    {
        commands.Action(FreePRibbonCommandGroup.Slide, "freep.undo", editor.Undo);
        commands.Action(FreePRibbonCommandGroup.Slide, "freep.redo", editor.Redo);
        commands.Action(FreePRibbonCommandGroup.Slide, "freep.new-slide", () => editor.InsertSlide());
        commands.Action(FreePRibbonCommandGroup.Slide, "freep.duplicate-slide", editor.DuplicateCurrentSlide);
        commands.Action(FreePRibbonCommandGroup.Slide, "freep.delete-slide", editor.DeleteCurrentSlide);
        commands.HostAction(FreePRibbonCommandGroup.Slide, SlideZoomInsertionPlanner.CommandId, host, FreePRibbonHostActionKind.InsertSlideZoom);
        commands.HostAction(FreePRibbonCommandGroup.Slide, SectionZoomInsertionPlanner.CommandId, host, FreePRibbonHostActionKind.InsertSectionZoom);
        commands.HostAction(FreePRibbonCommandGroup.Slide, SummaryZoomInsertionPlanner.CommandId, host, FreePRibbonHostActionKind.InsertSummaryZoom);
        commands.HostAction(FreePRibbonCommandGroup.Slide, ZoomTargetPlanner.CommandId, host, FreePRibbonHostActionKind.EditZoomTarget);
        commands.HostAction(FreePRibbonCommandGroup.Slide, SummaryZoomTargetPlanner.CommandId, host, FreePRibbonHostActionKind.EditSummaryZoomTargets);
        commands.HostAction(FreePRibbonCommandGroup.Slide, ZoomObjectPropertiesPlanner.CommandId, host, FreePRibbonHostActionKind.FormatZoom);
        commands.HostAction(FreePRibbonCommandGroup.Slide, ZoomCoverImagePlanner.CommandId, host, FreePRibbonHostActionKind.SetZoomCoverImage);
        commands.HostAction(FreePRibbonCommandGroup.Slide, ZoomCoverImagePlanner.ResetCommandId, host, FreePRibbonHostActionKind.ResetZoomCoverImage);
    }

    private static void RegisterClipboardCommands(
        Registrar commands,
        EditingSession editor,
        FreePRibbonCommandHostAdapter host)
    {
        commands.HostAction(FreePRibbonCommandGroup.Clipboard, "freep.copy", host, FreePRibbonHostActionKind.Copy);
        commands.HostAction(FreePRibbonCommandGroup.Clipboard, "freep.cut", host, FreePRibbonHostActionKind.Cut);
        commands.HostAction(FreePRibbonCommandGroup.Clipboard, "freep.paste", host, FreePRibbonHostActionKind.Paste);
        commands.Action(FreePRibbonCommandGroup.Clipboard, "freep.format-painter", () =>
        {
            if (editor.SelectedShapeIds.Count == 1 &&
                host.TryQuery<bool>(FreePRibbonHostQueryKind.BeginFormatPainter, out var began) &&
                began)
            {
                return;
            }

            editor.CopyFormatting();
            editor.ApplyFormattingToSelection();
        });
    }

    private static void RegisterTextCommands(
        Registrar commands,
        EditingSession editor,
        RibbonStateStore stateStore,
        FreePRibbonCommandHostAdapter host)
    {
        RegisterTextToggle(commands, editor, stateStore, host, "freep.bold", TableCellTextFormatKind.Bold);
        RegisterTextToggle(commands, editor, stateStore, host, "freep.italic", TableCellTextFormatKind.Italic);
        RegisterTextToggle(commands, editor, stateStore, host, "freep.underline", TableCellTextFormatKind.Underline);
        RegisterTextToggle(commands, editor, stateStore, host, "freep.strikethrough", TableCellTextFormatKind.Strikethrough);
        RegisterTextToggle(commands, editor, stateStore, host, "freep.superscript", TableCellTextFormatKind.Superscript);
        RegisterTextToggle(commands, editor, stateStore, host, "freep.subscript", TableCellTextFormatKind.Subscript);

        RegisterParagraphAlignment(commands, editor, host, "freep.paragraph.align-left", TextAlign.Left);
        RegisterParagraphAlignment(commands, editor, host, "freep.paragraph.align-center", TextAlign.Center);
        RegisterParagraphAlignment(commands, editor, host, "freep.paragraph.align-right", TextAlign.Right);
        RegisterParagraphAlignment(commands, editor, host, "freep.paragraph.align-justify", TextAlign.Justify);

        commands.Context(FreePRibbonCommandGroup.Text, "freep.bullets", context =>
            ApplyParagraphList(editor, host, context.SelectedValue, numbering: false));
        commands.Context(FreePRibbonCommandGroup.Text, "freep.numbering", context =>
            ApplyParagraphList(editor, host, context.SelectedValue, numbering: true));

        foreach (var item in PresentationListGalleryPlanner.BuildPlans().SelectMany(static plan => plan.Items))
        {
            if (!item.IsEnabled || item.ListPreset is null)
                continue;

            var preset = item.ListPreset;
            commands.Action(FreePRibbonCommandGroup.Text, item.CommandId, () =>
            {
                if (!host.TryHandle(FreePRibbonTextActionKind.ApplyListPreset, preset))
                    editor.TryApplyActiveTableCellParagraphListPreset(preset);
            });
        }

        commands.HostAction(
            FreePRibbonCommandGroup.Text,
            PresentationListGalleryPlanner.ImageBulletCommandId,
            host,
            FreePRibbonHostActionKind.PickPictureBullet);

        RegisterIndent(commands, editor, host, "freep.indent-increase", increase: true);
        RegisterIndent(commands, editor, host, "freep.indent-decrease", increase: false);
        RegisterIndent(commands, editor, host, "freep.increase-indent", increase: true);
        RegisterIndent(commands, editor, host, "freep.decrease-indent", increase: false);

        commands.Context(FreePRibbonCommandGroup.Text, "freep.font-family", context =>
        {
            var family = context.SelectedValue;
            if (string.IsNullOrWhiteSpace(family) || host.TryHandle(FreePRibbonTextActionKind.SetFontFamily, family))
                return;

            if (!editor.TryApplyActiveTableCellFontFamily(family))
                editor.SetFontFamilyOnSelection(family);
        });
        commands.Context(FreePRibbonCommandGroup.Text, "freep.font-size", context =>
        {
            if (!TryGetFontSize(context, out var sizePt) ||
                host.TryHandle(FreePRibbonTextActionKind.SetFontSize, sizePt))
                return;

            if (!editor.TryApplyActiveTableCellFontSize(sizePt))
                editor.SetFontSizeOnSelection(sizePt);
        });
        commands.Context(FreePRibbonCommandGroup.Text, "freep.font-color", context =>
        {
            if (!TryGetColor(context, out var color) ||
                host.TryHandle(FreePRibbonTextActionKind.SetColor, color))
                return;

            if (!editor.TryApplyActiveTableCellColor(color))
                editor.SetColorOnSelection(color);
        });
        commands.Context(FreePRibbonCommandGroup.Text, "freep.text-autofit", context =>
        {
            if (TextAutoFitOptionParser.TryParse(GetSelectedValue(context), out var kind))
                editor.SetTextAutoFitOnSelection(kind);
        });
        commands.Context(FreePRibbonCommandGroup.Text, "freep.text-direction", context =>
        {
            if (!TextVerticalTypeOptionParser.TryParse(GetSelectedValue(context), out var verticalType) ||
                host.TryHandle(FreePRibbonTextActionKind.SetTextVerticalType, verticalType))
                return;

            if (!editor.TryApplyActiveTableCellTextVerticalType(verticalType))
                editor.SetTextVerticalTypeOnSelection(verticalType);
        });
        commands.Context(FreePRibbonCommandGroup.Text, "freep.text-columns", context =>
        {
            if (TextColumnCountOptionParser.TryParse(GetSelectedValue(context), out var count))
                editor.SetTextColumnCountOnSelection(count);
        });
        commands.Context(FreePRibbonCommandGroup.Text, "freep.text-column-spacing", context =>
        {
            if (TextColumnSpacingOptionParser.TryParse(GetSelectedValue(context), out var spacingEmu))
                editor.SetTextColumnSpacingOnSelection(spacingEmu);
        });
    }

    private static void RegisterTableCommands(
        Registrar commands,
        EditingSession editor,
        FreePRibbonCommandHostAdapter host)
    {
        commands.Context(FreePRibbonCommandGroup.Table, "freep.table-cell-fill", context =>
        {
            if (TryGetColor(context, out var color) &&
                !host.TryHandle(FreePRibbonTextActionKind.SetTableCellFill, color))
                editor.TryApplyActiveTableCellFill(color);
        });
        commands.Context(FreePRibbonCommandGroup.Table, "freep.table-cell-anchor", context =>
        {
            if (TryGetTableCellAnchor(context, out var anchor) &&
                !host.TryHandle(FreePRibbonTextActionKind.SetTableCellAnchor, anchor))
                editor.TryApplyActiveTableCellAnchor(anchor);
        });
        commands.Context(FreePRibbonCommandGroup.Table, "freep.table-cell-border", context =>
        {
            if (TableCellBorderOptionParser.TryParse(GetSelectedValue(context), out var side, out var outline) &&
                !host.TryHandle(FreePRibbonTextActionKind.SetTableCellBorder, side, outline))
                editor.TryApplyActiveTableCellBorder(side, outline);
        });
        commands.Context(FreePRibbonCommandGroup.Table, "freep.table-cell-inset", context =>
        {
            if (TableCellInsetOptionParser.TryParse(GetSelectedValue(context), out var side, out var insetPt) &&
                !host.TryHandle(FreePRibbonTextActionKind.SetTableCellInset, side, insetPt))
                editor.TryApplyActiveTableCellInset(side, insetPt);
        });
        commands.Context(FreePRibbonCommandGroup.Table, "freep.table-row-height", context =>
        {
            if (TableRowHeightOptionParser.TryParse(GetSelectedValue(context), out var heightEmu) &&
                !host.TryHandle(FreePRibbonTextActionKind.SetTableRowHeight, heightEmu))
                editor.TryApplyActiveTableRowHeight(heightEmu);
        });

        commands.Register(
            FreePRibbonCommandGroup.Table,
            TableCellEditPlanner.MergeCellsCommandId,
            new HostStatefulActionCommand(
                () => host.Execute(FreePRibbonHostActionKind.MergeTableCells),
                () =>
                {
                    var state = TableCellEditPlanner.PlanSelectedCell(
                        editor.CurrentSlide,
                        editor.SelectedShapeIds,
                        editor.ActiveTableCell);
                    return state.CanMergeWithRight || state.CanMergeWithBelow;
                },
                fallbackExecute: editor.TryMergeActiveTableCell));
        commands.Register(
            FreePRibbonCommandGroup.Table,
            TableCellEditPlanner.SplitCellCommandId,
            new HostStatefulActionCommand(
                () => host.Execute(FreePRibbonHostActionKind.SplitTableCell),
                () => TableCellEditPlanner.PlanSelectedCell(
                    editor.CurrentSlide,
                    editor.SelectedShapeIds,
                    editor.ActiveTableCell).CanSplitCell,
                fallbackExecute: editor.TrySplitActiveTableCell));

        RegisterTableEdit(commands, editor, host, TableCellEditPlanner.DistributeRowsCommandId,
            PresentationDomainContextActionKind.DistributeTableRows, editor.TryDistributeActiveTableRows);
        RegisterTableEdit(commands, editor, host, TableCellEditPlanner.DistributeColumnsCommandId,
            PresentationDomainContextActionKind.DistributeTableColumns, editor.TryDistributeActiveTableColumns);
        RegisterTableEdit(commands, editor, host, TableCellEditPlanner.InsertRowAboveCommandId,
            PresentationDomainContextActionKind.InsertTableRowAbove, editor.TryInsertActiveTableRowAbove);
        RegisterTableEdit(commands, editor, host, TableCellEditPlanner.InsertRowBelowCommandId,
            PresentationDomainContextActionKind.InsertTableRowBelow, editor.TryInsertActiveTableRowBelow);
        RegisterTableEdit(commands, editor, host, TableCellEditPlanner.InsertColumnLeftCommandId,
            PresentationDomainContextActionKind.InsertTableColumnLeft, editor.TryInsertActiveTableColumnLeft);
        RegisterTableEdit(commands, editor, host, TableCellEditPlanner.InsertColumnRightCommandId,
            PresentationDomainContextActionKind.InsertTableColumnRight, editor.TryInsertActiveTableColumnRight);
        RegisterTableEdit(commands, editor, host, TableCellEditPlanner.DeleteRowCommandId,
            PresentationDomainContextActionKind.DeleteTableRow, editor.TryDeleteActiveTableRow);
        RegisterTableEdit(commands, editor, host, TableCellEditPlanner.DeleteColumnCommandId,
            PresentationDomainContextActionKind.DeleteTableColumn, editor.TryDeleteActiveTableColumn);

        RegisterTableStyleFlag(commands, editor, TableCellEditPlanner.TableFirstRowCommandId, TableStyleFlagKind.FirstRow);
        RegisterTableStyleFlag(commands, editor, TableCellEditPlanner.TableLastRowCommandId, TableStyleFlagKind.LastRow);
        RegisterTableStyleFlag(commands, editor, TableCellEditPlanner.TableFirstColCommandId, TableStyleFlagKind.FirstCol);
        RegisterTableStyleFlag(commands, editor, TableCellEditPlanner.TableLastColCommandId, TableStyleFlagKind.LastCol);
        RegisterTableStyleFlag(commands, editor, TableCellEditPlanner.TableBandRowCommandId, TableStyleFlagKind.BandRow);
        RegisterTableStyleFlag(commands, editor, TableCellEditPlanner.TableBandColCommandId, TableStyleFlagKind.BandCol);
    }

    private static void RegisterInsertCommands(
        Registrar commands,
        EditingSession editor,
        FreePRibbonCommandHostAdapter host)
    {
        foreach (var plan in SlideObjectInsertionPlanner.BuiltInPlans)
        {
            if (plan.CommandId == SlideObjectInsertionPlanner.Table3x3CommandId)
            {
                commands.HostAction(FreePRibbonCommandGroup.Insert, plan.CommandId, host, FreePRibbonHostActionKind.OpenTablePicker);
            }
            else if (plan.RequiresPicturePayload)
            {
                commands.HostAction(FreePRibbonCommandGroup.Insert, plan.CommandId, host, FreePRibbonHostActionKind.InsertPicture);
            }
            else if (plan.RequiresMediaPayload)
            {
                commands.HostAction(
                    FreePRibbonCommandGroup.Insert,
                    plan.CommandId,
                    host,
                    plan.CommandId == SlideObjectInsertionPlanner.VideoCommandId
                        ? FreePRibbonHostActionKind.InsertVideo
                        : FreePRibbonHostActionKind.InsertAudio);
            }
            else
            {
                commands.Action(FreePRibbonCommandGroup.Insert, plan.CommandId, () =>
                    SlideObjectInsertionPlanner.Apply(editor, plan));
            }
        }

        commands.HostAction(FreePRibbonCommandGroup.Insert, HeaderFooterCommandPlanner.HeaderFooterCommandId, host, FreePRibbonHostActionKind.OpenHeaderFooter, HeaderFooterCommandFocus.HeaderFooter);
        commands.HostAction(FreePRibbonCommandGroup.Insert, HeaderFooterCommandPlanner.DateTimeCommandId, host, FreePRibbonHostActionKind.OpenHeaderFooter, HeaderFooterCommandFocus.DateTime);
        commands.HostAction(FreePRibbonCommandGroup.Insert, HeaderFooterCommandPlanner.SlideNumberCommandId, host, FreePRibbonHostActionKind.OpenHeaderFooter, HeaderFooterCommandFocus.SlideNumber);
    }

    private static void RegisterPictureAndShapeCommands(Registrar commands, EditingSession editor)
    {
        commands.Action(FreePRibbonCommandGroup.Picture, PictureCropAuthoringPlanner.InsetCommandId, () => editor.SetSelectedPictureCrop(PictureCropAuthoringPlanner.Inset()));
        commands.Action(FreePRibbonCommandGroup.Picture, PictureCropAuthoringPlanner.ResetCommandId, () => editor.SetSelectedPictureCrop(PictureCropAuthoringPlanner.Reset()));
        commands.Action(FreePRibbonCommandGroup.Picture, PictureColorEffectAuthoringPlanner.GrayscaleCommandId, () => editor.SetSelectedPictureColorEffects(PictureColorEffectAuthoringPlanner.Grayscale()));
        commands.Action(FreePRibbonCommandGroup.Picture, PictureColorEffectAuthoringPlanner.ResetCommandId, () => editor.SetSelectedPictureColorEffects(PictureColorEffectAuthoringPlanner.Reset()));

        RegisterShapeEffect(commands, ShapeEffectAuthoringPlanner.NoneCommandId, () => editor.SetSelectedShapeShadow(ShapeEffectAuthoringPlanner.None()));
        RegisterShapeEffect(commands, ShapeEffectAuthoringPlanner.SubtleCommandId, () => editor.SetSelectedShapeShadow(ShapeEffectAuthoringPlanner.Subtle()));
        RegisterShapeEffect(commands, ShapeEffectAuthoringPlanner.OffsetCommandId, () => editor.SetSelectedShapeShadow(ShapeEffectAuthoringPlanner.Offset()));
        RegisterShapeEffect(commands, ShapeEffectAuthoringPlanner.GlowNoneCommandId, () => editor.SetSelectedShapeGlow(ShapeEffectAuthoringPlanner.GlowNone()));
        RegisterShapeEffect(commands, ShapeEffectAuthoringPlanner.GlowSubtleCommandId, () => editor.SetSelectedShapeGlow(ShapeEffectAuthoringPlanner.GlowSubtle()));
        RegisterShapeEffect(commands, ShapeEffectAuthoringPlanner.GlowStrongCommandId, () => editor.SetSelectedShapeGlow(ShapeEffectAuthoringPlanner.GlowStrong()));
        RegisterShapeEffect(commands, ShapeEffectAuthoringPlanner.SoftEdgeNoneCommandId, () => editor.SetSelectedShapeSoftEdge(ShapeEffectAuthoringPlanner.SoftEdgeNone()));
        RegisterShapeEffect(commands, ShapeEffectAuthoringPlanner.SoftEdgeSubtleCommandId, () => editor.SetSelectedShapeSoftEdge(ShapeEffectAuthoringPlanner.SoftEdgeSubtle()));
        RegisterShapeEffect(commands, ShapeEffectAuthoringPlanner.SoftEdgeStrongCommandId, () => editor.SetSelectedShapeSoftEdge(ShapeEffectAuthoringPlanner.SoftEdgeStrong()));
        RegisterShapeEffect(commands, ShapeEffectAuthoringPlanner.BevelNoneCommandId, () => editor.SetSelectedShapeBevel(ShapeEffectAuthoringPlanner.BevelNone()));
        RegisterShapeEffect(commands, ShapeEffectAuthoringPlanner.BevelSubtleCommandId, () => editor.SetSelectedShapeBevel(ShapeEffectAuthoringPlanner.BevelSubtle()));
        RegisterShapeEffect(commands, ShapeEffectAuthoringPlanner.BevelStrongCommandId, () => editor.SetSelectedShapeBevel(ShapeEffectAuthoringPlanner.BevelStrong()));
        RegisterShapeEffect(commands, ShapeEffectAuthoringPlanner.Shape3dNoneCommandId, () => editor.SetSelectedShape3d(ShapeEffectAuthoringPlanner.Shape3dNone()));
        RegisterShapeEffect(commands, ShapeEffectAuthoringPlanner.Shape3dSubtleCommandId, () => editor.SetSelectedShape3d(ShapeEffectAuthoringPlanner.Shape3dSubtle()));
        RegisterShapeEffect(commands, ShapeEffectAuthoringPlanner.Shape3dStrongCommandId, () => editor.SetSelectedShape3d(ShapeEffectAuthoringPlanner.Shape3dStrong()));

        commands.Action(FreePRibbonCommandGroup.Shape, ShapeTransparencyPlanner.FillCommandId, () => editor.SetSelectedFillTransparency(0));
        commands.Action(FreePRibbonCommandGroup.Shape, ShapeTransparencyPlanner.OutlineCommandId, () => editor.SetSelectedOutlineTransparency(0));
        foreach (var option in ShapeTransparencyPlanner.Options)
        {
            var percent = option.Percent;
            commands.Action(FreePRibbonCommandGroup.Shape, ShapeTransparencyPlanner.OptionCommandId(ShapeTransparencyTarget.Fill, percent), () => editor.SetSelectedFillTransparency(percent));
            commands.Action(FreePRibbonCommandGroup.Shape, ShapeTransparencyPlanner.OptionCommandId(ShapeTransparencyTarget.Outline, percent), () => editor.SetSelectedOutlineTransparency(percent));
        }
    }

    private static void RegisterSmartArtCommands(Registrar commands, FreePRibbonCommandHostAdapter host)
    {
        var colors = new (string CommandId, SmartArtColorPreset Preset)[]
        {
            (SmartArtAuthoringPlanner.ThemeAccentsCommandId, SmartArtColorPreset.ThemeAccents),
            (SmartArtAuthoringPlanner.SingleAccentCommandId, SmartArtColorPreset.SingleAccent),
            (SmartArtAuthoringPlanner.MonochromaticAccent2CommandId, SmartArtColorPreset.MonochromaticAccent2),
            (SmartArtAuthoringPlanner.MonochromaticAccent3CommandId, SmartArtColorPreset.MonochromaticAccent3),
            (SmartArtAuthoringPlanner.MonochromaticAccent4CommandId, SmartArtColorPreset.MonochromaticAccent4),
            (SmartArtAuthoringPlanner.MonochromaticAccent5CommandId, SmartArtColorPreset.MonochromaticAccent5),
            (SmartArtAuthoringPlanner.MonochromaticAccent6CommandId, SmartArtColorPreset.MonochromaticAccent6),
            (SmartArtAuthoringPlanner.GrayscaleCommandId, SmartArtColorPreset.Grayscale),
        };
        foreach (var (commandId, preset) in colors)
            commands.HostAction(FreePRibbonCommandGroup.SmartArt, commandId, host, FreePRibbonHostActionKind.ApplySmartArtColor, preset);
        foreach (var entry in SmartArtAuthoringPlanner.ColorGallery)
            commands.HostAction(FreePRibbonCommandGroup.SmartArt, entry.CommandId, host, FreePRibbonHostActionKind.ApplySmartArtColor, entry.Preset);

        foreach (var (commandId, preset) in SmartArtLayouts)
            commands.HostAction(FreePRibbonCommandGroup.SmartArt, commandId, host, FreePRibbonHostActionKind.ApplySmartArtLayout, preset);
        foreach (var (commandId, preset) in SmartArtStyles)
            commands.HostAction(FreePRibbonCommandGroup.SmartArt, commandId, host, FreePRibbonHostActionKind.ApplySmartArtQuickStyle, preset);

        commands.HostAction(FreePRibbonCommandGroup.SmartArt, SmartArtAuthoringPlanner.ConvertToShapesCommandId, host, FreePRibbonHostActionKind.ConvertSmartArtToShapes);
        commands.HostAction(FreePRibbonCommandGroup.SmartArt, SmartArtEditingPlanner.OpenTextPaneCommandId, host, FreePRibbonHostActionKind.OpenSmartArtTextPane);
    }

    private static void RegisterChartCommands(
        Registrar commands,
        EditingSession editor,
        FreePRibbonCommandHostAdapter host)
    {
        commands.HostAction(FreePRibbonCommandGroup.Chart, ChartDataDialogPlanner.EditDataCommandId, host, FreePRibbonHostActionKind.OpenChartData);
        commands.HostAction(FreePRibbonCommandGroup.Chart, ChartDataDialogPlanner.ChangeChartTypeCommandId, host, FreePRibbonHostActionKind.OpenChartData);
        foreach (var option in ChartDataDialogPlanner.ChartTypeOptions)
        {
            var chartType = option.Value;
            commands.Action(FreePRibbonCommandGroup.Chart, ChartDataDialogPlanner.ChangeChartTypeOptionCommandId(chartType), () => editor.ChangeSelectedChartType(chartType));
        }

        commands.HostAction(FreePRibbonCommandGroup.Chart, ChartDisplayOptionsPlanner.CommandId, host, FreePRibbonHostActionKind.OpenChartDisplayOptions);
        commands.HostAction(FreePRibbonCommandGroup.Chart, ChartAxisOptionsPlanner.CommandId, host, FreePRibbonHostActionKind.OpenChartAxisOptions);
        commands.HostAction(FreePRibbonCommandGroup.Chart, ChartSeriesOptionsPlanner.CommandId, host, FreePRibbonHostActionKind.OpenChartSeriesOptions);
        commands.HostAction(FreePRibbonCommandGroup.Chart, ChartPointOptionsPlanner.CommandId, host, FreePRibbonHostActionKind.OpenChartPointOptions);
        commands.HostAction(FreePRibbonCommandGroup.Chart, ChartLayoutOptionsPlanner.CommandId, host, FreePRibbonHostActionKind.OpenChartLayoutOptions);
        commands.HostAction(FreePRibbonCommandGroup.Chart, ChartExSeriesLayoutPlanner.CommandId, host, FreePRibbonHostActionKind.OpenChartExSeriesLayout);
        commands.HostAction(FreePRibbonCommandGroup.Chart, ChartDataTableOptionsPlanner.CommandId, host, FreePRibbonHostActionKind.OpenChartDataTableOptions);
        commands.HostAction(FreePRibbonCommandGroup.Chart, ChartBubbleOptionsPlanner.CommandId, host, FreePRibbonHostActionKind.OpenChartBubbleOptions);
        commands.HostAction(FreePRibbonCommandGroup.Chart, ChartPieOptionsPlanner.CommandId, host, FreePRibbonHostActionKind.OpenChartPieOptions);
        commands.HostAction(FreePRibbonCommandGroup.Chart, ChartPlotStyleOptionsPlanner.CommandId, host, FreePRibbonHostActionKind.OpenChartPlotStyleOptions);
        commands.HostAction(FreePRibbonCommandGroup.Chart, Chart3DViewOptionsPlanner.CommandId, host, FreePRibbonHostActionKind.OpenChart3DViewOptions);
        commands.HostAction(FreePRibbonCommandGroup.Chart, ChartTextOptionsPlanner.CommandId, host, FreePRibbonHostActionKind.OpenChartTextOptions);
        commands.HostAction(FreePRibbonCommandGroup.Chart, ChartAreaOptionsPlanner.CommandId, host, FreePRibbonHostActionKind.OpenChartAreaOptions);
        commands.HostAction(FreePRibbonCommandGroup.Chart, ChartProtectionOptionsPlanner.CommandId, host, FreePRibbonHostActionKind.OpenChartProtectionOptions);
    }

    private static void RegisterLinkCommands(
        Registrar commands,
        EditingSession editor,
        FreePRibbonCommandHostAdapter host)
    {
        commands.HostAction(FreePRibbonCommandGroup.Link, "freep.insert-link", host, FreePRibbonHostActionKind.OpenHyperlink);
        commands.Action(FreePRibbonCommandGroup.Link, "freep.remove-link", () =>
        {
            if (!host.TryHandle(FreePRibbonTextActionKind.RemoveHyperlink))
                editor.RemoveShapeHyperlink();
        });
    }

    private static void RegisterArrangeCommands(
        Registrar commands,
        EditingSession editor,
        RibbonStateStore stateStore,
        FreePRibbonCommandHostAdapter host)
    {
        commands.Action(FreePRibbonCommandGroup.Arrange, "freep.arrange.group", editor.GroupSelectedShapes);
        commands.Action(FreePRibbonCommandGroup.Arrange, "freep.arrange.ungroup", editor.UngroupSelected);
        foreach (var (commandId, kind) in ShapeChangePlanner.Presets)
        {
            var shapeKind = kind;
            commands.Action(FreePRibbonCommandGroup.Arrange, commandId, () => editor.ChangeSelectedAutoShapeKind(shapeKind));
        }

        commands.HostAction(FreePRibbonCommandGroup.Arrange, RotationOptionsPlanner.CommandId, host, FreePRibbonHostActionKind.OpenRotationOptions);
        commands.Register(
            FreePRibbonCommandGroup.Arrange,
            PresentationEditPointsModePlanner.CommandId,
            new EditPointsToggleCommand(stateStore, host));

        var routes = new (string CommandId, Action Execute)[]
        {
            ("freep.arrange.bring-to-front", editor.BringToFront),
            ("freep.arrange.bring-forward", editor.BringForward),
            ("freep.arrange.send-backward", editor.SendBackward),
            ("freep.arrange.send-to-back", editor.SendToBack),
            ("freep.arrange.flip-horizontal", editor.FlipSelectedHorizontal),
            ("freep.arrange.flip-vertical", editor.FlipSelectedVertical),
            ("freep.arrange.rotate-left-90", editor.RotateSelectedLeft90),
            ("freep.arrange.rotate-right-90", editor.RotateSelectedRight90),
            ("freep.arrange.align-left", editor.AlignLeft),
            ("freep.arrange.align-center-h", editor.AlignCenterH),
            ("freep.arrange.align-right", editor.AlignRight),
            ("freep.arrange.align-top", editor.AlignTop),
            ("freep.arrange.align-middle", editor.AlignMiddle),
            ("freep.arrange.align-bottom", editor.AlignBottom),
            ("freep.arrange.align-left-to-slide", editor.AlignLeftToSlide),
            ("freep.arrange.align-center-h-to-slide", editor.AlignCenterHToSlide),
            ("freep.arrange.align-right-to-slide", editor.AlignRightToSlide),
            ("freep.arrange.align-top-to-slide", editor.AlignTopToSlide),
            ("freep.arrange.align-middle-to-slide", editor.AlignMiddleToSlide),
            ("freep.arrange.align-bottom-to-slide", editor.AlignBottomToSlide),
            ("freep.arrange.distribute-h", editor.DistributeHorizontally),
            ("freep.arrange.distribute-v", editor.DistributeVertically),
        };
        foreach (var (commandId, execute) in routes)
            commands.Action(FreePRibbonCommandGroup.Arrange, commandId, execute);
    }

    private static void RegisterDesignCommands(
        Registrar commands,
        EditingSession editor,
        FreePRibbonCommandHostAdapter host)
    {
        foreach (var plan in PresentationDesignCommandPlanner.BuiltInPlans.Prepend(PresentationDesignCommandPlanner.LayoutPlan))
        {
            commands.Action(FreePRibbonCommandGroup.Design, plan.CommandId, () =>
                PresentationDesignCommandPlanner.TryApply(
                    editor,
                    plan,
                    request => host.Execute(FreePRibbonHostActionKind.DesignRequest, request)));
        }
    }

    private static void RegisterTransitionCommands(
        Registrar commands,
        EditingSession editor,
        RibbonStateStore stateStore,
        FreePRibbonCommandHostAdapter host)
    {
        foreach (var plan in PresentationTransitionCommandPlanner.BuiltInPlans)
        {
            commands.Register(
                FreePRibbonCommandGroup.Transition,
                plan.CommandId,
                plan.Intent is PresentationTransitionCommandIntentKind.ToggleAdvanceOnClick
                    or PresentationTransitionCommandIntentKind.ToggleSoundLoop
                    ? new TransitionToggleCommand(stateStore, editor, plan)
                    : new ContextRibbonCommand(context =>
                        PresentationTransitionCommandPlanner.TryApply(
                            editor,
                            plan,
                            context.SelectedValue,
                            () => host.Execute(FreePRibbonHostActionKind.PickTransitionSound))));
        }
    }

    private static void RegisterAnimationCommands(
        Registrar commands,
        EditingSession editor,
        RibbonStateStore stateStore,
        FreePRibbonCommandHostAdapter host)
    {
        foreach (var plan in PresentationAnimationCommandPlanner.BuiltInPlans)
        {
            commands.Register(
                FreePRibbonCommandGroup.Animation,
                plan.CommandId,
                plan.Intent == PresentationAnimationCommandIntentKind.TogglePane
                    ? new AnimationPaneToggleCommand(stateStore, editor, plan, host)
                    : new ContextRibbonCommand(context =>
                        PresentationAnimationCommandPlanner.TryApply(
                            editor,
                            plan,
                            context.SelectedValue,
                            request => host.Execute(FreePRibbonHostActionKind.ToggleAnimationPane, request))));
        }
    }

    private static void RegisterViewCommands(
        Registrar commands,
        RibbonStateStore stateStore,
        FreePRibbonCommandHostAdapter host)
    {
        var initialShowState = host.TryQuery<PresentationViewShowState>(FreePRibbonHostQueryKind.ViewShowState, out var showState)
            ? showState
            : PresentationViewShowState.Default;
        foreach (var plan in PresentationViewShowPlanner.BuildPlans(initialShowState))
        {
            commands.Register(
                FreePRibbonCommandGroup.View,
                plan.CommandId,
                new ViewShowToggleCommand(stateStore, plan, host));
        }

        foreach (var plan in PresentationViewZoomPlanner.BuiltInPlans)
        {
            commands.Register(
                FreePRibbonCommandGroup.View,
                plan.CommandId,
                new ContextRibbonCommand(context =>
                {
                    var state = host.TryQuery<PresentationViewZoomState>(FreePRibbonHostQueryKind.ViewZoomState, out var current)
                        ? current
                        : PresentationViewZoomState.FitToWindow;
                    var result = PresentationViewZoomPlanner.Execute(state, plan, context.SelectedValue);
                    host.Execute(FreePRibbonHostActionKind.ApplyViewZoomState, result.State);
                }));
        }
    }

    private static void RegisterReviewCommands(Registrar commands, FreePRibbonCommandHostAdapter host)
    {
        commands.HostAction(FreePRibbonCommandGroup.Review, PresentationReviewWorkflowPlanner.CommentsPaneCommandId, host, FreePRibbonHostActionKind.ShowCommentsPane);
        commands.HostAction(FreePRibbonCommandGroup.Review, PresentationReviewWorkflowPlanner.AccessibilityCommandId, host, FreePRibbonHostActionKind.ShowAccessibilityPane);
        commands.HostAction(FreePRibbonCommandGroup.Review, PresentationReviewWorkflowPlanner.AltTextCommandId, host, FreePRibbonHostActionKind.ShowAltTextPane);
        commands.HostAction(FreePRibbonCommandGroup.Review, PresentationReviewWorkflowPlanner.ReadingOrderPaneCommandId, host, FreePRibbonHostActionKind.ShowReadingOrderPane);
        commands.HostAction(FreePRibbonCommandGroup.Review, PresentationSelectionPanePlanner.SelectionPaneCommandId, host, FreePRibbonHostActionKind.ShowSelectionPane);
        commands.HostAction(FreePRibbonCommandGroup.Review, PresentationReviewWorkflowPlanner.ProofingCommandId, host, FreePRibbonHostActionKind.ShowProofingPane);
        commands.HostAction(FreePRibbonCommandGroup.Review, PresentationReviewWorkflowPlanner.AddCommentCommandId, host, FreePRibbonHostActionKind.AddComment);
        commands.HostAction(FreePRibbonCommandGroup.Review, PresentationReviewWorkflowPlanner.EditCommentCommandId, host, FreePRibbonHostActionKind.EditComment);
        commands.HostAction(FreePRibbonCommandGroup.Review, PresentationReviewWorkflowPlanner.ReplyCommentCommandId, host, FreePRibbonHostActionKind.ReplyComment);
        commands.HostAction(FreePRibbonCommandGroup.Review, PresentationReviewWorkflowPlanner.DeleteCommentCommandId, host, FreePRibbonHostActionKind.DeleteComment);
        commands.HostAction(FreePRibbonCommandGroup.Review, PresentationReviewWorkflowPlanner.PreviousCommentCommandId, host, FreePRibbonHostActionKind.PreviousComment);
        commands.HostAction(FreePRibbonCommandGroup.Review, PresentationReviewWorkflowPlanner.NextCommentCommandId, host, FreePRibbonHostActionKind.NextComment);
        commands.HostAction(FreePRibbonCommandGroup.Review, PresentationReviewWorkflowPlanner.ResolveCommentCommandId, host, FreePRibbonHostActionKind.ResolveComment);
        commands.HostAction(FreePRibbonCommandGroup.Review, PresentationReviewWorkflowPlanner.ReopenCommentCommandId, host, FreePRibbonHostActionKind.ReopenComment);
        commands.HostAction(FreePRibbonCommandGroup.Review, "freep.find", host, FreePRibbonHostActionKind.OpenFind);
        commands.HostAction(FreePRibbonCommandGroup.Review, "freep.replace", host, FreePRibbonHostActionKind.OpenReplace);
    }

    private static void RegisterSlideShowCommands(Registrar commands, FreePRibbonCommandHostAdapter host)
    {
        commands.HostAction(FreePRibbonCommandGroup.SlideShow, "freep.slideshow.from-beginning", host, FreePRibbonHostActionKind.StartSlideShowFromBeginning);
        commands.HostAction(FreePRibbonCommandGroup.SlideShow, "freep.slideshow.from-current-slide", host, FreePRibbonHostActionKind.StartSlideShowFromCurrent);
        commands.HostAction(FreePRibbonCommandGroup.SlideShow, "freep.slideshow.rehearse-timings", host, FreePRibbonHostActionKind.RehearseTimings);
        commands.HostAction(FreePRibbonCommandGroup.SlideShow, "freep.slideshow.record-timings", host, FreePRibbonHostActionKind.RecordTimings);
        commands.HostAction(FreePRibbonCommandGroup.SlideShow, "freep.slideshow.custom-shows", host, FreePRibbonHostActionKind.OpenCustomShows);
        commands.HostAction(FreePRibbonCommandGroup.SlideShow, SlideShowSettingsPlanner.CommandId, host, FreePRibbonHostActionKind.OpenSlideShowSettings);
    }

    private static void RegisterTextToggle(
        Registrar commands,
        EditingSession editor,
        RibbonStateStore stateStore,
        FreePRibbonCommandHostAdapter host,
        string commandId,
        TableCellTextFormatKind kind)
    {
        commands.Register(
            FreePRibbonCommandGroup.Text,
            commandId,
            new LocalToggleCommand(
                stateStore,
                commandId,
                () => QuerySelectedTextFormatState(editor, kind),
                () =>
            {
                // A true return means a native in-canvas text editor applied the toggle to its own
                // live (uncommitted) buffer -- the selected shape/cell keeps its stale pre-edit
                // TextBody for the rest of the edit session, so QuerySelectedTextFormatState cannot
                // see the change. LocalToggleCommand falls back to click-parity tracking whenever
                // this returns true; see its remarks.
                if (host.TryHandle(FreePRibbonTextActionKind.ToggleFormat, kind))
                    return true;

                var applied = kind switch
                {
                    TableCellTextFormatKind.Bold => editor.ToggleBoldOnActiveTableCell(),
                    TableCellTextFormatKind.Italic => editor.ToggleItalicOnActiveTableCell(),
                    TableCellTextFormatKind.Underline => editor.ToggleUnderlineOnActiveTableCell(),
                    TableCellTextFormatKind.Strikethrough => editor.ToggleStrikethroughOnActiveTableCell(),
                    TableCellTextFormatKind.Superscript => editor.ToggleSuperscriptOnActiveTableCell(),
                    TableCellTextFormatKind.Subscript => editor.ToggleSubscriptOnActiveTableCell(),
                    _ => false,
                };
                if (applied)
                    return false;

                switch (kind)
                {
                    case TableCellTextFormatKind.Bold: editor.ToggleBoldOnSelection(); break;
                    case TableCellTextFormatKind.Italic: editor.ToggleItalicOnSelection(); break;
                    case TableCellTextFormatKind.Underline: editor.ToggleUnderlineOnSelection(); break;
                    case TableCellTextFormatKind.Strikethrough: editor.ToggleStrikethroughOnSelection(); break;
                    case TableCellTextFormatKind.Superscript: editor.ToggleSuperscriptOnSelection(); break;
                    case TableCellTextFormatKind.Subscript: editor.ToggleSubscriptOnSelection(); break;
                }
                return false;
            },
                () => editor.SelectedShapeIds));
    }

    /// <summary>
    /// Reports whether <paramref name="kind"/> is already applied across the current selection --
    /// the active table cell when one is set and has text, otherwise the selected shapes' text
    /// runs -- using the same majority-rule ground truth <see
    /// cref="EditingSession.TryApplyActiveTableCellTextFormat"/> and <see
    /// cref="EditingSession.ToggleBoldOnSelection"/> (and its siblings) already compute to decide
    /// which way a toggle click goes. Returns null when nothing selected carries this text
    /// property, so the caller can fall back to its own click-parity tracking.
    /// </summary>
    private static bool? QuerySelectedTextFormatState(EditingSession editor, TableCellTextFormatKind kind)
    {
        var cellPlan = editor.PlanActiveTableCellTextFormat(kind);
        if (cellPlan.IsReady && cellPlan.TargetValue is { } targetValue)
            return !targetValue;

        if (editor.CurrentSlide is null || editor.SelectedShapeIds.Count == 0)
            return null;

        var allRuns = new List<Run>();
        foreach (var id in editor.SelectedShapeIds)
        {
            var shape = ShapeHitTester.FindShape(editor.CurrentSlide, id);
            if (shape?.TextBody is null) continue;
            foreach (var paragraph in shape.TextBody.Paragraphs)
                allRuns.AddRange(paragraph.Runs);
        }

        return allRuns.Count == 0 ? null : allRuns.All(run => GetRunFormatState(run, kind));
    }

    private static bool GetRunFormatState(Run run, TableCellTextFormatKind kind) => kind switch
    {
        TableCellTextFormatKind.Bold => run.Bold,
        TableCellTextFormatKind.Italic => run.Italic,
        TableCellTextFormatKind.Underline => run.Underline,
        TableCellTextFormatKind.Strikethrough => run.Strikethrough,
        TableCellTextFormatKind.Superscript => run.BaselineOffset > 0,
        TableCellTextFormatKind.Subscript => run.BaselineOffset < 0,
        _ => false,
    };

    private static void RegisterParagraphAlignment(
        Registrar commands,
        EditingSession editor,
        FreePRibbonCommandHostAdapter host,
        string commandId,
        TextAlign alignment) =>
        commands.Action(FreePRibbonCommandGroup.Text, commandId, () =>
        {
            if (!host.TryHandle(FreePRibbonTextActionKind.SetParagraphAlignment, alignment))
                editor.TryApplyActiveTableCellParagraphAlignment(alignment);
        });

    private static void RegisterIndent(
        Registrar commands,
        EditingSession editor,
        FreePRibbonCommandHostAdapter host,
        string commandId,
        bool increase) =>
        commands.Action(FreePRibbonCommandGroup.Text, commandId, () =>
        {
            var kind = increase ? FreePRibbonTextActionKind.Indent : FreePRibbonTextActionKind.Outdent;
            if (host.TryHandle(kind))
                return;

            if (increase)
                editor.TryApplyActiveTableCellParagraphIndent();
            else
                editor.TryApplyActiveTableCellParagraphOutdent();
        });

    private static void ApplyParagraphList(
        EditingSession editor,
        FreePRibbonCommandHostAdapter host,
        string? selectedValue,
        bool numbering)
    {
        if ((TableCellListPresetCatalog.TryGet(selectedValue, out var preset) ||
             PresentationListGalleryPlanner.TryGetPresetCommand(selectedValue, out preset)) &&
            preset is not null)
        {
            if (!host.TryHandle(FreePRibbonTextActionKind.ApplyListPreset, preset))
                editor.TryApplyActiveTableCellParagraphListPreset(preset);
            return;
        }

        var kind = numbering ? FreePRibbonTextActionKind.ToggleNumbering : FreePRibbonTextActionKind.ToggleBullets;
        if (host.TryHandle(kind))
            return;

        if (numbering)
            editor.TryApplyActiveTableCellParagraphNumberingToggle();
        else
            editor.TryApplyActiveTableCellParagraphBulletToggle();
    }

    private static void RegisterTableStyleFlag(
        Registrar commands,
        EditingSession editor,
        string commandId,
        TableStyleFlagKind kind) =>
        commands.Register(
            FreePRibbonCommandGroup.Table,
            commandId,
            new TableStyleFlagToggleCommand(editor, kind));

    private static void RegisterTableEdit(
        Registrar commands,
        EditingSession editor,
        FreePRibbonCommandHostAdapter host,
        string commandId,
        PresentationDomainContextActionKind actionKind,
        Func<bool> execute) =>
        commands.Register(
            FreePRibbonCommandGroup.Table,
            commandId,
            new HostStatefulActionCommand(
                () => host.Execute(FreePRibbonHostActionKind.ExecuteTableStructureAction, actionKind),
                () => editor.ActiveTableCell is not null,
                execute));

    private static void RegisterShapeEffect(Registrar commands, string commandId, Action execute) =>
        commands.Action(FreePRibbonCommandGroup.Shape, commandId, execute);

    private static bool TryGetFontSize(RibbonCommandContext context, out double sizePt)
    {
        sizePt = context.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value)
            ? value switch
            {
                double d => d,
                float f => f,
                int i => i,
                decimal m => (double)m,
                string s when TryParseFontSize(s, out var parsed) => parsed,
                _ => 0,
            }
            : 0;
        return sizePt > 0 && !double.IsNaN(sizePt) && !double.IsInfinity(sizePt);
    }

    private static bool TryParseFontSize(string value, out double sizePt)
    {
        var text = value.Trim();
        if (text.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
            text = text[..^2].Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out sizePt);
    }

    private static bool TryGetColor(RibbonCommandContext context, out ThemeAwareColor? color)
    {
        color = null;
        if (!context.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value))
            return false;

        if (FreePRibbonChoiceCatalog.TryResolve(
                value,
                FreePRibbonChoiceCatalog.ColorChoices,
                out FreePRibbonColorChoiceDescriptor descriptor))
        {
            color = descriptor.Color;
            return true;
        }

        switch (value)
        {
            case ThemeAwareColor themeColor:
                color = themeColor;
                return true;
            case SrgbColor srgb:
                color = new ThemeAwareColor(srgb);
                return true;
            case string text:
                return TryParseColor(text, out color);
            default:
                return false;
        }
    }

    private static bool TryParseColor(string? value, out ThemeAwareColor? color)
    {
        color = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (RgbColorTextCodec.TryParse(
                text,
                RgbColorTextProfile.TrimmedHashOrBare,
                out var rgb))
        {
            color = new ThemeAwareColor(new SrgbColor(rgb.R, rgb.G, rgb.B));
            return true;
        }

        return false;
    }

    private static bool TryGetTableCellAnchor(RibbonCommandContext context, out TableCellAnchor? anchor)
    {
        anchor = null;
        if (!context.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value))
            return false;

        if (FreePRibbonChoiceCatalog.TryResolve(
                value,
                FreePRibbonChoiceCatalog.TableCellAnchorChoices,
                out FreePRibbonTableCellAnchorChoiceDescriptor descriptor))
        {
            anchor = descriptor.Anchor;
            return true;
        }

        if (value is TableCellAnchor cellAnchor)
        {
            anchor = cellAnchor;
            return true;
        }

        return false;
    }

    private static object? GetSelectedValue(RibbonCommandContext context) =>
        context.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value)
            ? value
            : null;

    private static readonly (string CommandId, SmartArtLayoutPreset Preset)[] SmartArtLayouts =
    [
        (SmartArtAuthoringPlanner.BasicProcessLayoutCommandId, SmartArtLayoutPreset.BasicProcess),
        (SmartArtAuthoringPlanner.AccentProcessLayoutCommandId, SmartArtLayoutPreset.AccentProcess),
        (SmartArtAuthoringPlanner.AscendingProcessLayoutCommandId, SmartArtLayoutPreset.AscendingProcess),
        (SmartArtAuthoringPlanner.DescendingProcessLayoutCommandId, SmartArtLayoutPreset.DescendingProcess),
        (SmartArtAuthoringPlanner.BasicTimelineLayoutCommandId, SmartArtLayoutPreset.BasicTimeline),
        (SmartArtAuthoringPlanner.CircleAccentTimelineLayoutCommandId, SmartArtLayoutPreset.CircleAccentTimeline),
        (SmartArtAuthoringPlanner.PhasedProcessLayoutCommandId, SmartArtLayoutPreset.PhasedProcess),
        (SmartArtAuthoringPlanner.StepDownProcessLayoutCommandId, SmartArtLayoutPreset.StepDownProcess),
        (SmartArtAuthoringPlanner.ContinuousBlockProcessLayoutCommandId, SmartArtLayoutPreset.ContinuousBlockProcess),
        (SmartArtAuthoringPlanner.SegmentedProcessLayoutCommandId, SmartArtLayoutPreset.SegmentedProcess),
        (SmartArtAuthoringPlanner.ChevronProcessLayoutCommandId, SmartArtLayoutPreset.ChevronProcess),
        (SmartArtAuthoringPlanner.BasicChevronProcessLayoutCommandId, SmartArtLayoutPreset.BasicChevronProcess),
        (SmartArtAuthoringPlanner.ClosedChevronProcessLayoutCommandId, SmartArtLayoutPreset.ClosedChevronProcess),
        (SmartArtAuthoringPlanner.BendingProcessLayoutCommandId, SmartArtLayoutPreset.BendingProcess),
        (SmartArtAuthoringPlanner.AlternatingProcessLayoutCommandId, SmartArtLayoutPreset.AlternatingProcess),
        (SmartArtAuthoringPlanner.ArrowRibbonLayoutCommandId, SmartArtLayoutPreset.ArrowRibbon),
        (SmartArtAuthoringPlanner.CircleProcessLayoutCommandId, SmartArtLayoutPreset.CircleProcess),
        (SmartArtAuthoringPlanner.CircleArrowProcessLayoutCommandId, SmartArtLayoutPreset.CircleArrowProcess),
        (SmartArtAuthoringPlanner.IncreasingCircleProcessLayoutCommandId, SmartArtLayoutPreset.IncreasingCircleProcess),
        (SmartArtAuthoringPlanner.FunnelProcessLayoutCommandId, SmartArtLayoutPreset.FunnelProcess),
        (SmartArtAuthoringPlanner.VerticalProcessLayoutCommandId, SmartArtLayoutPreset.VerticalProcess),
        (SmartArtAuthoringPlanner.VerticalBoxListLayoutCommandId, SmartArtLayoutPreset.VerticalBoxList),
        (SmartArtAuthoringPlanner.VerticalBlockListLayoutCommandId, SmartArtLayoutPreset.VerticalBlockList),
        (SmartArtAuthoringPlanner.VerticalChevronListLayoutCommandId, SmartArtLayoutPreset.VerticalChevronList),
        (SmartArtAuthoringPlanner.VerticalArrowListLayoutCommandId, SmartArtLayoutPreset.VerticalArrowList),
        (SmartArtAuthoringPlanner.VerticalBulletListLayoutCommandId, SmartArtLayoutPreset.VerticalBulletList),
        (SmartArtAuthoringPlanner.VerticalPictureListLayoutCommandId, SmartArtLayoutPreset.VerticalPictureList),
        (SmartArtAuthoringPlanner.HorizontalBulletListLayoutCommandId, SmartArtLayoutPreset.HorizontalBulletList),
        (SmartArtAuthoringPlanner.HorizontalBlockListLayoutCommandId, SmartArtLayoutPreset.HorizontalBlockList),
        (SmartArtAuthoringPlanner.TrapezoidListLayoutCommandId, SmartArtLayoutPreset.TrapezoidList),
        (SmartArtAuthoringPlanner.GroupedListLayoutCommandId, SmartArtLayoutPreset.GroupedList),
        (SmartArtAuthoringPlanner.BasicCycleLayoutCommandId, SmartArtLayoutPreset.BasicCycle),
        (SmartArtAuthoringPlanner.MultidirectionalCycleLayoutCommandId, SmartArtLayoutPreset.MultidirectionalCycle),
        (SmartArtAuthoringPlanner.Cycle2LayoutCommandId, SmartArtLayoutPreset.Cycle2),
        (SmartArtAuthoringPlanner.ContinuousCycleLayoutCommandId, SmartArtLayoutPreset.ContinuousCycle),
        (SmartArtAuthoringPlanner.GearCycleLayoutCommandId, SmartArtLayoutPreset.GearCycle),
        (SmartArtAuthoringPlanner.TextCycleLayoutCommandId, SmartArtLayoutPreset.TextCycle),
        (SmartArtAuthoringPlanner.BlockCycleLayoutCommandId, SmartArtLayoutPreset.BlockCycle),
        (SmartArtAuthoringPlanner.NonDirectionalCycleLayoutCommandId, SmartArtLayoutPreset.NonDirectionalCycle),
        (SmartArtAuthoringPlanner.BasicBlockListLayoutCommandId, SmartArtLayoutPreset.BasicBlockList),
        (SmartArtAuthoringPlanner.BasicListLayoutCommandId, SmartArtLayoutPreset.BasicList),
        (SmartArtAuthoringPlanner.List2LayoutCommandId, SmartArtLayoutPreset.List2),
        (SmartArtAuthoringPlanner.StackedListLayoutCommandId, SmartArtLayoutPreset.StackedList),
        (SmartArtAuthoringPlanner.DescendingBlockListLayoutCommandId, SmartArtLayoutPreset.DescendingBlockList),
        (SmartArtAuthoringPlanner.BasicPyramidLayoutCommandId, SmartArtLayoutPreset.BasicPyramid),
        (SmartArtAuthoringPlanner.PyramidListLayoutCommandId, SmartArtLayoutPreset.PyramidList),
        (SmartArtAuthoringPlanner.InvertedPyramidLayoutCommandId, SmartArtLayoutPreset.InvertedPyramid),
        (SmartArtAuthoringPlanner.RadialCycleLayoutCommandId, SmartArtLayoutPreset.RadialCycle),
        (SmartArtAuthoringPlanner.BasicRadialLayoutCommandId, SmartArtLayoutPreset.BasicRadial),
        (SmartArtAuthoringPlanner.RadialClusterLayoutCommandId, SmartArtLayoutPreset.RadialCluster),
        (SmartArtAuthoringPlanner.RadialListLayoutCommandId, SmartArtLayoutPreset.RadialList),
        (SmartArtAuthoringPlanner.BasicMatrixLayoutCommandId, SmartArtLayoutPreset.BasicMatrix),
        (SmartArtAuthoringPlanner.TitledMatrixLayoutCommandId, SmartArtLayoutPreset.TitledMatrix),
        (SmartArtAuthoringPlanner.GridMatrixLayoutCommandId, SmartArtLayoutPreset.GridMatrix),
        (SmartArtAuthoringPlanner.BasicRelationshipLayoutCommandId, SmartArtLayoutPreset.BasicRelationship),
        (SmartArtAuthoringPlanner.OpposingIdeasLayoutCommandId, SmartArtLayoutPreset.OpposingIdeas),
        (SmartArtAuthoringPlanner.ConvergingRadialLayoutCommandId, SmartArtLayoutPreset.ConvergingRadial),
        (SmartArtAuthoringPlanner.DivergingRadialLayoutCommandId, SmartArtLayoutPreset.DivergingRadial),
        (SmartArtAuthoringPlanner.BasicVennLayoutCommandId, SmartArtLayoutPreset.BasicVenn),
        (SmartArtAuthoringPlanner.RadialVennLayoutCommandId, SmartArtLayoutPreset.RadialVenn),
        (SmartArtAuthoringPlanner.TargetListLayoutCommandId, SmartArtLayoutPreset.TargetList),
        (SmartArtAuthoringPlanner.StackedVennLayoutCommandId, SmartArtLayoutPreset.StackedVenn),
        (SmartArtAuthoringPlanner.InterlockingRingsLayoutCommandId, SmartArtLayoutPreset.InterlockingRings),
        (SmartArtAuthoringPlanner.BasicHierarchyLayoutCommandId, SmartArtLayoutPreset.BasicHierarchy),
        (SmartArtAuthoringPlanner.Hierarchy3LayoutCommandId, SmartArtLayoutPreset.Hierarchy3),
        (SmartArtAuthoringPlanner.HorizontalHierarchyLayoutCommandId, SmartArtLayoutPreset.HorizontalHierarchy),
        (SmartArtAuthoringPlanner.OrgChartLayoutCommandId, SmartArtLayoutPreset.OrgChart),
        (SmartArtAuthoringPlanner.NameAndTitleOrgChartLayoutCommandId, SmartArtLayoutPreset.NameAndTitleOrgChart),
        (SmartArtAuthoringPlanner.PictureCaptionListLayoutCommandId, SmartArtLayoutPreset.PictureCaptionList),
        (SmartArtAuthoringPlanner.PictureAccentListLayoutCommandId, SmartArtLayoutPreset.PictureAccentList),
        (SmartArtAuthoringPlanner.PictureStackLayoutCommandId, SmartArtLayoutPreset.PictureStack),
        (SmartArtAuthoringPlanner.PictureLineupLayoutCommandId, SmartArtLayoutPreset.PictureLineup),
        (SmartArtAuthoringPlanner.PictureStripsLayoutCommandId, SmartArtLayoutPreset.PictureStrips),
        (SmartArtAuthoringPlanner.ContinuousPictureListLayoutCommandId, SmartArtLayoutPreset.ContinuousPictureList),
        (SmartArtAuthoringPlanner.PictureGridLayoutCommandId, SmartArtLayoutPreset.PictureGrid),
        (SmartArtAuthoringPlanner.PictureAccentProcessLayoutCommandId, SmartArtLayoutPreset.PictureAccentProcess),
        (SmartArtAuthoringPlanner.LabeledHierarchyLayoutCommandId, SmartArtLayoutPreset.LabeledHierarchy),
        (SmartArtAuthoringPlanner.TableHierarchyLayoutCommandId, SmartArtLayoutPreset.TableHierarchy),
    ];

    private static readonly (string CommandId, SmartArtQuickStylePreset Preset)[] SmartArtStyles =
    [
        (SmartArtAuthoringPlanner.SimpleQuickStyleCommandId, SmartArtQuickStylePreset.SimpleFill),
        (SmartArtAuthoringPlanner.ModerateQuickStyleCommandId, SmartArtQuickStylePreset.ModerateEffect),
        (SmartArtAuthoringPlanner.IntenseQuickStyleCommandId, SmartArtQuickStylePreset.IntenseEffect),
        (SmartArtAuthoringPlanner.SubtleQuickStyleCommandId, SmartArtQuickStylePreset.Subtle),
        (SmartArtAuthoringPlanner.SoftEdgeQuickStyleCommandId, SmartArtQuickStylePreset.SoftEdge),
        (SmartArtAuthoringPlanner.InsertQuickStyleCommandId, SmartArtQuickStylePreset.Insert),
        (SmartArtAuthoringPlanner.CartoonQuickStyleCommandId, SmartArtQuickStylePreset.Cartoon),
        (SmartArtAuthoringPlanner.PowderQuickStyleCommandId, SmartArtQuickStylePreset.Powder),
        (SmartArtAuthoringPlanner.PolishedQuickStyleCommandId, SmartArtQuickStylePreset.Polished),
        (SmartArtAuthoringPlanner.BrickSceneQuickStyleCommandId, SmartArtQuickStylePreset.BrickScene),
        (SmartArtAuthoringPlanner.FlatSceneQuickStyleCommandId, SmartArtQuickStylePreset.FlatScene),
        (SmartArtAuthoringPlanner.MetallicSceneQuickStyleCommandId, SmartArtQuickStylePreset.MetallicScene),
        (SmartArtAuthoringPlanner.SunsetSceneQuickStyleCommandId, SmartArtQuickStylePreset.SunsetScene),
        (SmartArtAuthoringPlanner.BirdsEyeSceneQuickStyleCommandId, SmartArtQuickStylePreset.BirdsEyeScene),
    ];

    private sealed class Registrar
    {
        private readonly RibbonCommandRegistry _registry = new();
        private readonly Dictionary<FreePRibbonCommandGroup, List<RibbonCommandId>> _groups = new();
        private readonly HashSet<RibbonCommandId> _registered = [];

        public void Register(FreePRibbonCommandGroup group, RibbonCommandId commandId, IRibbonCommand command)
        {
            if (!_registered.Add(commandId))
                throw new InvalidOperationException($"Duplicate FreeP ribbon command registration: {commandId}");

            _registry.Register(commandId, command);
            if (!_groups.TryGetValue(group, out var commands))
                _groups[group] = commands = [];
            commands.Add(commandId);
        }

        public void Action(FreePRibbonCommandGroup group, RibbonCommandId commandId, Action execute) =>
            Register(group, commandId, new ActionRibbonCommand(execute));

        public void Context(FreePRibbonCommandGroup group, RibbonCommandId commandId, Action<RibbonCommandContext> execute) =>
            Register(group, commandId, new ContextRibbonCommand(execute));

        public void HostAction(
            FreePRibbonCommandGroup group,
            RibbonCommandId commandId,
            FreePRibbonCommandHostAdapter host,
            FreePRibbonHostActionKind kind,
            object? argument = null) =>
            Action(group, commandId, () => host.Execute(kind, argument));

        public FreePRibbonCommandBuildResult Build() =>
            new(
                _registry,
                _groups.ToDictionary(
                    static pair => pair.Key,
                    static pair => (IReadOnlyList<RibbonCommandId>)pair.Value.ToArray()));
    }

    /// <summary>
    /// Backs a text-format ribbon toggle (freep.bold/italic/underline/strikethrough/superscript/
    /// subscript). <paramref name="queryChecked"/> is the real ground truth -- e.g.
    /// <see cref="QuerySelectedTextFormatState"/> -- consulted on every <see cref="GetState"/> call
    /// so the button reflects the selection's actual formatting rather than a click-parity guess.
    /// <paramref name="execute"/> applies the toggle and reports whether a native in-canvas text
    /// editor handled it live: when it did, the selected shape/cell keeps its stale pre-edit
    /// <c>TextBody</c> for the rest of that edit session (nothing commits until editing ends), so
    /// <paramref name="queryChecked"/> would read the same frozen answer on every call and the
    /// button would never move no matter how many times it is clicked. For that case this class
    /// stops trusting the query for the duration of the edit session and instead tracks whether
    /// each click flipped the property on or off, exactly like it did before that query existed;
    /// it re-seeds from the query the moment a native-handled click starts a new edit session, and
    /// resumes trusting the query the moment a click is <em>not</em> reported as native-handled
    /// (selection change, non-native table-cell/selection path, etc.). The same fallback also
    /// covers the indeterminate case (nothing selected carries the property).
    ///
    /// A native-handled edit session also ends without any further click at all -- the user
    /// presses Escape or clicks away, then selects a different shape, and never touches this
    /// button again. <paramref name="selectedShapeIds"/> lets <see cref="GetState"/> notice that:
    /// it snapshots the selection the moment a live session starts, and if the current selection no
    /// longer matches on a later <see cref="GetState"/> call, the session is treated as over and the
    /// query resumes. This only catches session-end-by-selection-change; a session that ends by the
    /// native editor simply deactivating on the SAME still-selected shape (no selection change)
    /// leaves no signal in this class today -- that would need a new host query (e.g. an "is a
    /// native text edit session currently active" entry on <c>FreePRibbonHostQueryEndpoints</c>,
    /// wired from the WPF/Avalonia in-canvas editors' own deactivation events) rather than a
    /// heuristic bolted on here.
    /// </summary>
    private sealed class LocalToggleCommand : IRibbonStatefulCommand
    {
        private readonly RibbonStateStore _stateStore;
        private readonly RibbonCommandId _commandId;
        private readonly Func<bool?> _queryChecked;
        private readonly Func<bool> _execute;
        private readonly Func<IReadOnlyList<uint>> _selectedShapeIds;
        private bool _isChecked;
        private bool _liveEditSessionActive;
        private IReadOnlyList<uint> _liveEditSessionSelection = [];

        public LocalToggleCommand(
            RibbonStateStore stateStore,
            RibbonCommandId commandId,
            Func<bool?> queryChecked,
            Func<bool> execute,
            Func<IReadOnlyList<uint>> selectedShapeIds)
        {
            _stateStore = stateStore;
            _commandId = commandId;
            _queryChecked = queryChecked;
            _execute = execute;
            _selectedShapeIds = selectedShapeIds;
        }

        public void Execute(RibbonCommandContext context)
        {
            var nativeEditorHandledIt = _execute();
            if (nativeEditorHandledIt)
            {
                if (!_liveEditSessionActive)
                    _isChecked = _queryChecked() ?? _isChecked;
                _isChecked = !_isChecked;
                // Snapshot defensively: EditingSession.SelectedShapeIds exposes its live, mutable
                // backing list directly (no copy), so capturing the reference itself would compare
                // as equal to every later selection -- the list object never changes identity, only
                // its contents do.
                _liveEditSessionSelection = _selectedShapeIds().ToArray();
            }
            else
            {
                _isChecked = _queryChecked() ?? !_isChecked;
            }

            _liveEditSessionActive = nativeEditorHandledIt;
            _stateStore.SetChecked(_commandId, _isChecked);
        }

        public RibbonCommandState GetState()
        {
            if (_liveEditSessionActive && !_liveEditSessionSelection.SequenceEqual(_selectedShapeIds()))
                _liveEditSessionActive = false;

            return new(IsChecked: _liveEditSessionActive ? _isChecked : (_queryChecked() ?? _isChecked));
        }
    }

    private sealed class HostStatefulActionCommand(
        Func<bool> hostExecute,
        Func<bool> canExecute,
        Func<bool> fallbackExecute) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (canExecute())
            {
                if (!hostExecute())
                    fallbackExecute();
            }
        }

        public RibbonCommandState GetState() => new(IsEnabled: canExecute());
    }

    private sealed class TableStyleFlagToggleCommand(
        EditingSession editor,
        TableStyleFlagKind kind) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ToggleSelectedTableStyleFlag(kind);

        public RibbonCommandState GetState()
        {
            var isAvailable = editor.TryGetSelectedTableStyleFlag(kind, out var isChecked);
            return new RibbonCommandState(
                IsEnabled: isAvailable,
                IsChecked: isAvailable && isChecked);
        }
    }

    private sealed class EditPointsToggleCommand : IRibbonStatefulCommand
    {
        private readonly RibbonStateStore _stateStore;
        private readonly FreePRibbonCommandHostAdapter _host;
        private bool _localEnabled = true;

        public EditPointsToggleCommand(RibbonStateStore stateStore, FreePRibbonCommandHostAdapter host)
        {
            _stateStore = stateStore;
            _host = host;
            Sync();
        }

        public void Execute(RibbonCommandContext context)
        {
            var plan = PresentationEditPointsModePlanner.BuildTogglePlan(IsEnabled());
            _localEnabled = plan.NextIsEnabled;
            _host.Execute(FreePRibbonHostActionKind.SetEditPointsEnabled, plan.NextIsEnabled);
            Sync();
        }

        public RibbonCommandState GetState() => new(IsChecked: IsEnabled());

        private bool IsEnabled() =>
            _host.TryQuery<bool>(FreePRibbonHostQueryKind.EditPointsEnabled, out var enabled)
                ? enabled
                : _localEnabled;

        private void Sync() => _stateStore.SetChecked(PresentationEditPointsModePlanner.CommandId, IsEnabled());
    }

    private sealed class TransitionToggleCommand : IRibbonStatefulCommand
    {
        private readonly RibbonStateStore _stateStore;
        private readonly EditingSession _editor;
        private readonly PresentationTransitionCommandPlan _plan;

        public TransitionToggleCommand(
            RibbonStateStore stateStore,
            EditingSession editor,
            PresentationTransitionCommandPlan plan)
        {
            _stateStore = stateStore;
            _editor = editor;
            _plan = plan;
            Sync();
            _editor.Changed += Sync;
            _editor.CurrentSlideChanged += OnCurrentSlideChanged;
        }

        public void Execute(RibbonCommandContext context)
        {
            if (PresentationTransitionCommandPlanner.TryApply(_editor, _plan, context.SelectedValue))
                Sync();
        }

        public RibbonCommandState GetState()
        {
            var state = PresentationTransitionCommandPlanner.GetToggleState(
                _editor.CurrentSlideTransition,
                _plan.Intent);
            return new RibbonCommandState(
                IsEnabled: state.IsEnabled,
                IsChecked: state.IsChecked);
        }

        private void OnCurrentSlideChanged(object? sender, EventArgs args) => Sync();

        private void Sync()
        {
            var state = GetState();
            _stateStore.SetEnabled(_plan.CommandId, state.IsEnabled);
            _stateStore.SetChecked(_plan.CommandId, state.IsChecked);
        }
    }

    private sealed class AnimationPaneToggleCommand : IRibbonStatefulCommand
    {
        private readonly RibbonStateStore _stateStore;
        private readonly EditingSession _editor;
        private readonly PresentationAnimationCommandPlan _plan;
        private readonly FreePRibbonCommandHostAdapter _host;
        private bool _localVisible;

        public AnimationPaneToggleCommand(
            RibbonStateStore stateStore,
            EditingSession editor,
            PresentationAnimationCommandPlan plan,
            FreePRibbonCommandHostAdapter host)
        {
            _stateStore = stateStore;
            _editor = editor;
            _plan = plan;
            _host = host;
        }

        public void Execute(RibbonCommandContext context)
        {
            if (!PresentationAnimationCommandPlanner.TryApply(
                    _editor,
                    _plan,
                    context.SelectedValue,
                    request => _host.Execute(FreePRibbonHostActionKind.ToggleAnimationPane, request)))
                return;

            _localVisible = !_localVisible;
            _stateStore.SetChecked(_plan.CommandId, GetState().IsChecked);
        }

        public RibbonCommandState GetState() => new(IsChecked:
            _host.TryQuery<bool>(FreePRibbonHostQueryKind.AnimationPaneVisible, out var visible)
                ? visible
                : _localVisible);
    }

    private sealed class ViewShowToggleCommand : IRibbonStatefulCommand
    {
        private readonly RibbonStateStore _stateStore;
        private readonly PresentationViewShowCommandPlan _plan;
        private readonly FreePRibbonCommandHostAdapter _host;
        private PresentationViewShowState _localState = PresentationViewShowState.Default;

        public ViewShowToggleCommand(
            RibbonStateStore stateStore,
            PresentationViewShowCommandPlan plan,
            FreePRibbonCommandHostAdapter host)
        {
            _stateStore = stateStore;
            _plan = plan;
            _host = host;
            Sync();
        }

        public void Execute(RibbonCommandContext context)
        {
            var result = PresentationViewShowPlanner.Toggle(CurrentState(), _plan);
            _localState = result.State;
            _host.Execute(FreePRibbonHostActionKind.ApplyViewShowState, result.State);
            _stateStore.SetChecked(_plan.CommandId, result.IsChecked);
        }

        public RibbonCommandState GetState() => new(
            IsChecked: PresentationViewShowPlanner.IsChecked(CurrentState(), _plan.Kind));

        private PresentationViewShowState CurrentState() =>
            _host.TryQuery<PresentationViewShowState>(FreePRibbonHostQueryKind.ViewShowState, out var state)
                ? state
                : _localState;

        private void Sync() => _stateStore.SetChecked(_plan.CommandId, GetState().IsChecked);
    }
}
