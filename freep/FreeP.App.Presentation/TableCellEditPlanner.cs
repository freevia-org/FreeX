using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum TableCellEditStartStatus
{
    Ready,
    MissingSlide,
    ShapeNotFound,
    NotTable,
    CellOutOfRange,
    MissingCellBounds,
}

public enum TableCellTextFormatKind
{
    Bold,
    Italic,
    Underline,
    Superscript,
    Subscript,
}

public enum TableCellTextValueFormatKind
{
    FontFamily,
    FontSize,
    Color,
}

public enum TableCellParagraphFormatKind
{
    Alignment,
    BulletToggle,
    NumberingToggle,
    ListPreset,
    PictureBullet,
    Indent,
    Outdent,
}

public enum TableCellTextFormatStatus
{
    Ready,
    MissingSlide,
    ShapeNotFound,
    NotTable,
    MissingActiveCell,
    CellOutOfRange,
    MissingTextBody,
    NoTextRuns,
}

public enum TableCellNavigationDirection
{
    Next,
    Previous,
}

public enum TableCellEditKeyboardKey
{
    Other,
    Escape,
    Tab,
    B,
    I,
    U,
}

[Flags]
public enum TableCellEditKeyboardModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Platform = 8,
}

public enum TableCellEditKeyboardAction
{
    None,
    Cancel,
    Navigate,
    ToggleTextFormat,
}

public readonly record struct TableCellEditKeyboardPlan(
    TableCellEditKeyboardAction Action,
    TableCellNavigationDirection? NavigationDirection = null,
    TableCellTextFormatKind? TextFormatKind = null);

public enum TableCellNavigationStatus
{
    Ready,
    MissingSlide,
    ShapeNotFound,
    NotTable,
    MissingActiveCell,
    CellOutOfRange,
    NoTargetCell,
}

public sealed record TableCellEditState(
    uint? ShapeId,
    int? Row,
    int? Col,
    bool HasSelectedTable,
    bool HasActiveCell,
    bool CanEditText,
    bool CanFormatText,
    bool CanInsertRow,
    bool CanInsertColumn,
    bool CanDeleteRow,
    bool CanDeleteColumn,
    bool CanMergeWithRight,
    bool CanMergeWithBelow,
    bool CanSplitCell)
{
    public static readonly TableCellEditState None = new(
        null,
        null,
        null,
        HasSelectedTable: false,
        HasActiveCell: false,
        CanEditText: false,
        CanFormatText: false,
        CanInsertRow: false,
        CanInsertColumn: false,
        CanDeleteRow: false,
        CanDeleteColumn: false,
        CanMergeWithRight: false,
        CanMergeWithBelow: false,
        CanSplitCell: false);
}

public sealed record TableCellNavigationPlan(
    TableCellNavigationStatus Status,
    uint? ShapeId,
    int? Row,
    int? Col,
    TableCellNavigationDirection Direction)
{
    public bool IsReady => Status == TableCellNavigationStatus.Ready;
}

public sealed record TableCellEditStartPlan(
    TableCellEditStartStatus Status,
    uint ShapeId,
    int Row,
    int Col,
    TableCell? Cell,
    CellRectDip? CellRect,
    InCanvasEditorPlacement? Placement,
    InCanvasEditorTextSelection InitialSelection,
    InCanvasTableCellRichTextEditPlan? RichTextPlan,
    TextBody? OriginalBody,
    InCanvasTableCellTextEditPlanner? EditPlanner)
{
    public bool IsReady => Status == TableCellEditStartStatus.Ready;
}

public sealed record InCanvasEditorRunStyle(
    int ParagraphIndex,
    int RunIndex,
    int Start,
    int End,
    string Text,
    string? FontFamily,
    double? FontSizePt,
    bool Bold,
    bool Italic,
    bool Underline,
    bool Strikethrough,
    ThemeAwareColor? Color);

public sealed record InCanvasEditorSelectedRunRange(
    int ParagraphIndex,
    int RunIndex,
    int RunStart,
    int RunEnd,
    int SelectionStart,
    int SelectionEnd,
    string Text);

public sealed record InCanvasEditorParagraphStyle(
    int ParagraphIndex,
    int Start,
    int End,
    string Text,
    TextAlign? Align,
    BulletKind BulletKind,
    string? BulletChar,
    AutoNumType? AutoNumType,
    int? AutoNumStartAt,
    bool BulletSuppressed,
    ImagePart? BulletImage,
    int Level,
    long? MarginLeftEmu,
    long? IndentEmu,
    bool AutoNumStartAtSpecified = false)
{
    public bool HasListFormatting =>
        !BulletSuppressed && BulletKind != BulletKind.None;
}

public sealed record InCanvasEditorSelectedListState(
    bool HasSelectedParagraphs,
    bool HasListFormatting,
    bool HasMixedListFormatting,
    string? PresetId,
    string? DisplayName,
    string? PreviewText,
    PresentationListGalleryItemKind? GalleryItemKind,
    BulletKind? BulletKind,
    string? BulletChar,
    AutoNumType? AutoNumType,
    int? AutoNumStartAt,
    bool IsPictureBullet,
    bool AutoNumStartAtSpecified = false)
{
    public bool HasResolvedPreset => !string.IsNullOrWhiteSpace(PresetId);

    public static readonly InCanvasEditorSelectedListState None = new(
        HasSelectedParagraphs: false,
        HasListFormatting: false,
        HasMixedListFormatting: false,
        PresetId: null,
        DisplayName: null,
        PreviewText: null,
        GalleryItemKind: null,
        BulletKind: null,
        BulletChar: null,
        AutoNumType: null,
        AutoNumStartAt: null,
        IsPictureBullet: false);
}

public sealed record InCanvasEditorTextStyleState(
    string? FontFamily,
    double? FontSizePt,
    bool? Bold,
    bool? Italic,
    bool? Underline,
    bool? Strikethrough,
    ThemeAwareColor? Color)
{
    public bool IsMixed =>
        FontFamily is null ||
        FontSizePt is null ||
        Bold is null ||
        Italic is null ||
        Underline is null ||
        Strikethrough is null ||
        Color is null;
}

public sealed record InCanvasTableCellRichTextEditPlan(
    string PlainText,
    IReadOnlyList<InCanvasEditorRunStyle> Runs,
    InCanvasEditorTextStyleState SuggestedEditorStyle,
    InCanvasEditorTextStyleState InitialSelectionStyle,
    bool HasMixedFormatting,
    InCanvasEditorTextSelection Selection,
    IReadOnlyList<InCanvasEditorSelectedRunRange> SelectedRunRanges,
    IReadOnlyList<InCanvasEditorParagraphStyle> Paragraphs,
    IReadOnlyList<InCanvasEditorParagraphStyle> SelectedParagraphs,
    InCanvasEditorSelectedListState SelectedListState,
    bool HasMixedParagraphFormatting)
{
    public bool HasRichFormatting => Runs.Count > 1 || HasMixedFormatting;
    public bool HasListFormatting => Paragraphs.Any(paragraph => paragraph.HasListFormatting);
}

public sealed record TableCellTextFormatPlan(
    TableCellTextFormatStatus Status,
    uint? ShapeId,
    int? Row,
    int? Col,
    TableCellTextFormatKind Kind,
    bool? TargetValue,
    IPresentationCommand? Command,
    InCanvasEditorTextSelection? EffectiveSelection = null,
    InCanvasTableCellRichTextEditPlan? ResultRichTextPlan = null)
{
    public bool IsReady => Status == TableCellTextFormatStatus.Ready && Command is not null;
}

public sealed record TableCellTextValueFormatPlan(
    TableCellTextFormatStatus Status,
    uint? ShapeId,
    int? Row,
    int? Col,
    TableCellTextValueFormatKind Kind,
    object? Value,
    IPresentationCommand? Command,
    InCanvasEditorTextSelection? EffectiveSelection = null,
    InCanvasTableCellRichTextEditPlan? ResultRichTextPlan = null)
{
    public bool IsReady => Status == TableCellTextFormatStatus.Ready && Command is not null;
}

public sealed record TableCellParagraphFormatPlan(
    TableCellTextFormatStatus Status,
    uint? ShapeId,
    int? Row,
    int? Col,
    TableCellParagraphFormatKind Kind,
    TextAlign? Value,
    IPresentationCommand? Command,
    InCanvasEditorTextSelection? EffectiveSelection = null,
    InCanvasTableCellRichTextEditPlan? ResultRichTextPlan = null,
    bool? BulletEnabled = null,
    int LevelDelta = 0,
    TableCellListPresetDescriptor? ListPreset = null,
    ImagePart? BulletImage = null)
{
    public bool IsReady => Status == TableCellTextFormatStatus.Ready && Command is not null;
}

public sealed record TableCellListPresetDescriptor(
    string Id,
    string DisplayName,
    BulletKind BulletKind,
    string? BulletChar = null,
    AutoNumType? AutoNumType = null,
    int StartAt = 1);

public static class TableCellListPresetCatalog
{
    public const string BulletDiscId = "bullet.disc";
    public const string BulletHollowCircleId = "bullet.hollow-circle";
    public const string BulletSquareId = "bullet.square";
    public const string BulletDashId = "bullet.dash";
    public const string BulletCheckId = "bullet.check";
    public const string NumberArabicPeriodId = "number.arabic-period";
    public const string NumberRomanUpperPeriodId = "number.roman-upper-period";
    public const string NumberRomanLowerPeriodId = "number.roman-lower-period";
    public const string NumberAlphaUpperPeriodId = "number.alpha-upper-period";
    public const string NumberAlphaLowerPeriodId = "number.alpha-lower-period";

    public static readonly TableCellListPresetDescriptor BulletDisc = new(
        BulletDiscId,
        "Disc Bullet",
        BulletKind.Char,
        BulletChar: "\u2022");

    public static readonly TableCellListPresetDescriptor BulletHollowCircle = new(
        BulletHollowCircleId,
        "Hollow Circle Bullet",
        BulletKind.Char,
        BulletChar: "\u25E6");

    public static readonly TableCellListPresetDescriptor BulletSquare = new(
        BulletSquareId,
        "Square Bullet",
        BulletKind.Char,
        BulletChar: "\u25AA");

    public static readonly TableCellListPresetDescriptor BulletDash = new(
        BulletDashId,
        "Dash Bullet",
        BulletKind.Char,
        BulletChar: "\u2013");

    public static readonly TableCellListPresetDescriptor BulletCheck = new(
        BulletCheckId,
        "Check Bullet",
        BulletKind.Char,
        BulletChar: "\u2713");

    public static readonly TableCellListPresetDescriptor NumberArabicPeriod = new(
        NumberArabicPeriodId,
        "Arabic 1.",
        BulletKind.Auto,
        AutoNumType: FreeP.Core.Model.AutoNumType.ArabicPeriod);

    public static readonly TableCellListPresetDescriptor NumberRomanUpperPeriod = new(
        NumberRomanUpperPeriodId,
        "Roman I.",
        BulletKind.Auto,
        AutoNumType: FreeP.Core.Model.AutoNumType.RomanUcPeriod);

    public static readonly TableCellListPresetDescriptor NumberRomanLowerPeriod = new(
        NumberRomanLowerPeriodId,
        "Roman i.",
        BulletKind.Auto,
        AutoNumType: FreeP.Core.Model.AutoNumType.RomanLcPeriod);

    public static readonly TableCellListPresetDescriptor NumberAlphaUpperPeriod = new(
        NumberAlphaUpperPeriodId,
        "Alpha A.",
        BulletKind.Auto,
        AutoNumType: FreeP.Core.Model.AutoNumType.AlphaUcPeriod);

    public static readonly TableCellListPresetDescriptor NumberAlphaLowerPeriod = new(
        NumberAlphaLowerPeriodId,
        "Alpha a.",
        BulletKind.Auto,
        AutoNumType: FreeP.Core.Model.AutoNumType.AlphaLcPeriod);

    public static IReadOnlyList<TableCellListPresetDescriptor> BuiltIn { get; } =
    [
        BulletDisc,
        BulletHollowCircle,
        BulletSquare,
        BulletDash,
        BulletCheck,
        NumberArabicPeriod,
        NumberRomanUpperPeriod,
        NumberRomanLowerPeriod,
        NumberAlphaUpperPeriod,
        NumberAlphaLowerPeriod,
    ];

    public static bool TryGet(string? id, out TableCellListPresetDescriptor? preset)
    {
        preset = BuiltIn.FirstOrDefault(candidate =>
            StringComparer.OrdinalIgnoreCase.Equals(candidate.Id, id));
        return preset is not null;
    }

    public static bool TryMatch(
        BulletKind bulletKind,
        string? bulletChar,
        AutoNumType? autoNumType,
        out TableCellListPresetDescriptor? preset)
    {
        preset = BuiltIn.FirstOrDefault(candidate =>
            candidate.BulletKind == bulletKind &&
            (bulletKind != BulletKind.Char || StringComparer.Ordinal.Equals(candidate.BulletChar, bulletChar)) &&
            (bulletKind != BulletKind.Auto || candidate.AutoNumType == autoNumType));

        return preset is not null;
    }
}

public static class TableCellEditPlanner
{
    public const string MergeCellsCommandId = "freep.table.merge-cells";
    public const string SplitCellCommandId = "freep.table.split-cell";
    public const string InsertRowAboveCommandId = "freep.table.insert-row-above";
    public const string InsertRowBelowCommandId = "freep.table.insert-row-below";
    public const string InsertColumnLeftCommandId = "freep.table.insert-column-left";
    public const string InsertColumnRightCommandId = "freep.table.insert-column-right";
    public const string DeleteRowCommandId = "freep.table.delete-row";
    public const string DeleteColumnCommandId = "freep.table.delete-column";
    public const string DistributeRowsCommandId = "freep.table.distribute-rows";
    public const string DistributeColumnsCommandId = "freep.table.distribute-columns";
    public const string TableFirstRowCommandId = "freep.table.first-row";
    public const string TableLastRowCommandId = "freep.table.last-row";
    public const string TableFirstColCommandId = "freep.table.first-column";
    public const string TableLastColCommandId = "freep.table.last-column";
    public const string TableBandRowCommandId = "freep.table.banded-rows";
    public const string TableBandColCommandId = "freep.table.banded-columns";

    private const int MaxParagraphLevel = 8;
    private const long ParagraphIndentStepEmu = DrawingMlCoordinateUnits.EmuPerInch / 2;
    private const long ParagraphHangingIndentEmu = -DrawingMlCoordinateUnits.EmuPerInch / 4;
    private const string DefaultBulletChar = "\u2022";

    public static InCanvasEditorPlacement PlanCellEditorPlacement(
        SlideShape tableShape,
        CellRectDip cellRect,
        SlideTransformCore transform,
        double minimumWidth,
        double minimumHeight)
    {
        ArgumentNullException.ThrowIfNull(tableShape);
        ArgumentNullException.ThrowIfNull(transform);

        var tableBounds = new ShapeBoundsDip(
            tableShape.OffsetXEmu / DrawingMlCoordinateUnits.EmuPerPixel,
            tableShape.OffsetYEmu / DrawingMlCoordinateUnits.EmuPerPixel,
            tableShape.ExtentCxEmu / DrawingMlCoordinateUnits.EmuPerPixel,
            tableShape.ExtentCyEmu / DrawingMlCoordinateUnits.EmuPerPixel);
        return SlideCanvasGeometryPlanner.PlanTableCellEditorPlacement(
            cellRect,
            tableBounds,
            transform,
            minimumWidth,
            minimumHeight,
            tableShape.RotationDeg,
            tableShape.FlipH,
            tableShape.FlipV);
    }

    public static TableCellEditState PlanSelectedCell(
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell)
    {
        ArgumentNullException.ThrowIfNull(selectedShapeIds);

        if (slide is null || selectedShapeIds.Count == 0)
            return TableCellEditState.None;

        var shape = ShapeHitTester.FindShape(slide, selectedShapeIds[0]);
        if (shape?.Kind != SlideShapeKind.Table || shape.Table is null)
            return TableCellEditState.None;

        if (activeCell is not { } requested)
        {
            return TableCellEditState.None with
            {
                ShapeId = shape.Id,
                HasSelectedTable = true,
                CanInsertRow = shape.Table.Rows.Count > 0,
                CanInsertColumn = shape.Table.ColumnWidthsEmu.Count > 0,
                CanDeleteRow = shape.Table.Rows.Count > 1,
                CanDeleteColumn = shape.Table.ColumnWidthsEmu.Count > 1,
            };
        }

        var normalized = NormalizeCell(shape.Table, requested.Row, requested.Col);
        if (normalized is null)
        {
            return TableCellEditState.None with
            {
                ShapeId = shape.Id,
                HasSelectedTable = true,
            };
        }

        var cell = normalized.Value.Cell;
        int row = normalized.Value.Row;
        int col = normalized.Value.Col;
        int colSpan = Math.Max(1, cell.GridSpan);
        int rowSpan = Math.Max(1, cell.RowSpan);

        return new TableCellEditState(
            shape.Id,
            row,
            col,
            HasSelectedTable: true,
            HasActiveCell: true,
            CanEditText: true,
            CanFormatText: true,
            CanInsertRow: true,
            CanInsertColumn: shape.Table.ColumnWidthsEmu.Count > 0,
            CanDeleteRow: shape.Table.Rows.Count > 1,
            CanDeleteColumn: shape.Table.ColumnWidthsEmu.Count > 1,
            CanMergeWithRight: col + colSpan < shape.Table.ColumnWidthsEmu.Count,
            CanMergeWithBelow: row + rowSpan < shape.Table.Rows.Count,
            CanSplitCell: colSpan > 1 || rowSpan > 1);
    }

    public static TableCellEditStartPlan BeginEdit(
        int slideIndex,
        Slide? slide,
        uint shapeId,
        int row,
        int col,
        SlideTransformCore transform,
        double minimumWidth,
        double minimumHeight)
    {
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumHeight);

        if (slide is null)
            return NotReady(TableCellEditStartStatus.MissingSlide, shapeId, row, col);

        var shape = ShapeHitTester.FindShape(slide, shapeId);
        if (shape is null)
            return NotReady(TableCellEditStartStatus.ShapeNotFound, shapeId, row, col);
        if (shape.Kind != SlideShapeKind.Table || shape.Table is null)
            return NotReady(TableCellEditStartStatus.NotTable, shapeId, row, col);

        var normalized = NormalizeCell(shape.Table, row, col);
        if (normalized is null)
            return NotReady(TableCellEditStartStatus.CellOutOfRange, shapeId, row, col);

        var cellRect = TableCellHitTester.GetCellRect(shape, normalized.Value.Row, normalized.Value.Col);
        if (cellRect is null)
            return NotReady(TableCellEditStartStatus.MissingCellBounds, shapeId, normalized.Value.Row, normalized.Value.Col);

        var placement = PlanCellEditorPlacement(
            shape,
            cellRect.Value,
            transform,
            minimumWidth,
            minimumHeight);
        var originalBody = TextBodyModelCloner.CloneTextBody(normalized.Value.Cell.TextBody);

        return new TableCellEditStartPlan(
            TableCellEditStartStatus.Ready,
            shapeId,
            normalized.Value.Row,
            normalized.Value.Col,
            normalized.Value.Cell,
            cellRect.Value,
            placement,
            PlanInitialSelection(originalBody),
            PlanRichTextEdit(originalBody, PlanInitialSelection(originalBody)),
            originalBody,
            InCanvasTableCellTextEditPlanner.BeginRichText(
                slideIndex,
                shapeId,
                normalized.Value.Row,
                normalized.Value.Col,
                normalized.Value.Cell.TextBody));
    }

    public static InCanvasTextEditDecision CommitRichText(
        InCanvasTableCellTextEditPlanner? editPlanner,
        TextBody editedBody)
    {
        ArgumentNullException.ThrowIfNull(editedBody);

        return editPlanner?.CommitRichText(editedBody)
            ?? new InCanvasTextEditDecision(InCanvasTextEditOutcome.Unchanged, null);
    }

    public static InCanvasTextEditDecision Cancel(InCanvasTableCellTextEditPlanner? editPlanner) =>
        editPlanner?.Cancel()
        ?? new InCanvasTextEditDecision(InCanvasTextEditOutcome.Canceled, null);

    public static TableCellNavigationPlan PlanNavigation(
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        TableCellNavigationDirection direction)
    {
        ArgumentNullException.ThrowIfNull(selectedShapeIds);

        if (slide is null)
            return DisabledNavigation(TableCellNavigationStatus.MissingSlide, direction);
        if (selectedShapeIds.Count == 0)
            return DisabledNavigation(TableCellNavigationStatus.ShapeNotFound, direction);

        var shape = ShapeHitTester.FindShape(slide, selectedShapeIds[0]);
        if (shape is null)
            return DisabledNavigation(TableCellNavigationStatus.ShapeNotFound, direction);
        if (shape.Kind != SlideShapeKind.Table || shape.Table is null)
            return DisabledNavigation(TableCellNavigationStatus.NotTable, direction, shape.Id);
        if (activeCell is not { } requested)
            return DisabledNavigation(TableCellNavigationStatus.MissingActiveCell, direction, shape.Id);

        var normalized = NormalizeCell(shape.Table, requested.Row, requested.Col);
        if (normalized is null)
            return DisabledNavigation(TableCellNavigationStatus.CellOutOfRange, direction, shape.Id);

        var anchors = GetEditableCellAnchors(shape.Table);
        int currentIndex = anchors.FindIndex(cell => cell.Row == normalized.Value.Row && cell.Col == normalized.Value.Col);
        if (currentIndex < 0)
            return DisabledNavigation(TableCellNavigationStatus.CellOutOfRange, direction, shape.Id);

        int targetIndex = direction == TableCellNavigationDirection.Next
            ? currentIndex + 1
            : currentIndex - 1;
        if (targetIndex < 0 || targetIndex >= anchors.Count)
        {
            return DisabledNavigation(
                TableCellNavigationStatus.NoTargetCell,
                direction,
                shape.Id,
                normalized.Value.Row,
                normalized.Value.Col);
        }

        var target = anchors[targetIndex];
        return new TableCellNavigationPlan(
            TableCellNavigationStatus.Ready,
            shape.Id,
            target.Row,
            target.Col,
            direction);
    }

    public static TableCellEditKeyboardPlan PlanKeyboard(
        TableCellEditKeyboardKey key,
        TableCellEditKeyboardModifiers modifiers)
    {
        if (key == TableCellEditKeyboardKey.Escape)
            return new(TableCellEditKeyboardAction.Cancel);

        if (key == TableCellEditKeyboardKey.Tab &&
            (modifiers & (TableCellEditKeyboardModifiers.Control |
                          TableCellEditKeyboardModifiers.Alt |
                          TableCellEditKeyboardModifiers.Platform)) == 0)
        {
            return new(
                TableCellEditKeyboardAction.Navigate,
                (modifiers & TableCellEditKeyboardModifiers.Shift) != 0
                    ? TableCellNavigationDirection.Previous
                    : TableCellNavigationDirection.Next);
        }

        if ((modifiers & TableCellEditKeyboardModifiers.Control) != 0)
        {
            var formatKind = key switch
            {
                TableCellEditKeyboardKey.B => TableCellTextFormatKind.Bold,
                TableCellEditKeyboardKey.I => TableCellTextFormatKind.Italic,
                TableCellEditKeyboardKey.U => TableCellTextFormatKind.Underline,
                _ => (TableCellTextFormatKind?)null,
            };
            if (formatKind is { } resolvedKind)
                return new(TableCellEditKeyboardAction.ToggleTextFormat, TextFormatKind: resolvedKind);
        }

        return new(TableCellEditKeyboardAction.None);
    }

    public static InCanvasEditorTextSelection PlanInitialSelection(TextBody? body)
    {
        int textLength = InCanvasTextEditPlanner.ExtractPlainText(body).Length;
        return textLength > 0
            ? new InCanvasEditorTextSelection(0, textLength)
            : new InCanvasEditorTextSelection(0, 0);
    }

    public static InCanvasTableCellRichTextEditPlan PlanRichTextEdit(
        TextBody? body,
        InCanvasEditorTextSelection initialSelection)
    {
        var runs = BuildRunStyles(body);
        string plainText = InCanvasTextEditPlanner.ExtractPlainText(body);
        var effectiveSelection = PlanPreservedSelection(initialSelection, plainText.Length);
        var suggestedStyle = BuildStyleState(runs.Count > 0 ? [runs[0]] : []);
        var selectionStyleRuns = ResolveInitialSelectionStyleRuns(
            runs,
            effectiveSelection,
            plainText.Length);
        var selectionStyle = BuildStyleState(selectionStyleRuns);
        var selectedRunRanges = BuildSelectedRunRanges(runs, effectiveSelection);
        var paragraphs = BuildParagraphStyles(body);
        var selectedParagraphs = BuildSelectedParagraphStyles(
            paragraphs,
            effectiveSelection,
            plainText.Length);

        return new InCanvasTableCellRichTextEditPlan(
            plainText,
            runs,
            suggestedStyle,
            selectionStyle,
            HasMixedFormatting(runs),
            effectiveSelection,
            selectedRunRanges,
            paragraphs,
            selectedParagraphs,
            BuildSelectedListState(selectedParagraphs),
            HasMixedParagraphFormatting(paragraphs));
    }

    public static InCanvasEditorTextSelection PlanPreservedSelection(
        InCanvasEditorTextSelection selection,
        int textLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(textLength);

        return new InCanvasEditorTextSelection(
            Math.Clamp(selection.Start, 0, textLength),
            Math.Clamp(selection.End, 0, textLength));
    }

    internal static TextBody ApplyParagraphAlignmentToBody(
        TextBody source,
        TextAlign alignment,
        (int Start, int End)? selection) =>
        ApplyParagraphAlignment(source, alignment, selection);

    internal static TextBody ApplyParagraphBulletToggleToBody(
        TextBody source,
        (int Start, int End)? selection) =>
        ApplyParagraphBulletToggle(source, selection, out _);

    internal static TextBody ApplyParagraphNumberingToggleToBody(
        TextBody source,
        (int Start, int End)? selection) =>
        ApplyParagraphNumberingToggle(source, selection, out _);

    internal static TextBody ApplyParagraphListPresetToBody(
        TextBody source,
        (int Start, int End)? selection,
        TableCellListPresetDescriptor preset) =>
        ApplyParagraphListPreset(source, selection, preset);

    internal static TextBody ApplyParagraphPictureBulletToBody(
        TextBody source,
        (int Start, int End)? selection,
        ImagePart image) =>
        ApplyParagraphPictureBullet(source, selection, image);

    internal static TextBody ApplyParagraphIndentToBody(
        TextBody source,
        bool increase,
        (int Start, int End)? selection) =>
        ApplyParagraphIndent(source, increase, selection);

    public static TableCellTextFormatPlan PlanTextFormat(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        TableCellTextFormatKind kind,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(selectedShapeIds);

        if (slide is null)
            return DisabledFormat(TableCellTextFormatStatus.MissingSlide, kind);
        if (selectedShapeIds.Count == 0)
            return DisabledFormat(TableCellTextFormatStatus.ShapeNotFound, kind);

        var shape = ShapeHitTester.FindShape(slide, selectedShapeIds[0]);
        if (shape is null)
            return DisabledFormat(TableCellTextFormatStatus.ShapeNotFound, kind);
        if (shape.Kind != SlideShapeKind.Table || shape.Table is null)
            return DisabledFormat(TableCellTextFormatStatus.NotTable, kind, shape.Id);
        if (activeCell is not { } requested)
            return DisabledFormat(TableCellTextFormatStatus.MissingActiveCell, kind, shape.Id);

        var normalized = NormalizeCell(shape.Table, requested.Row, requested.Col);
        if (normalized is null)
            return DisabledFormat(TableCellTextFormatStatus.CellOutOfRange, kind, shape.Id);

        var (row, col, cell) = normalized.Value;
        if (cell.TextBody is null)
            return DisabledFormat(TableCellTextFormatStatus.MissingTextBody, kind, shape.Id, row, col);

        var runs = cell.TextBody.Paragraphs.SelectMany(p => p.Runs).ToList();
        if (runs.Count == 0)
            return DisabledFormat(TableCellTextFormatStatus.NoTextRuns, kind, shape.Id, row, col);

        var editedBody = TextBodyRunMutationPlanner.ToggleTextFormat(
            cell.TextBody,
            kind,
            selection,
            out var targetValue);
        var effectiveSelection = PlanFormatResultSelection(editedBody, selection);
        var richTextPlan = PlanRichTextEdit(editedBody, effectiveSelection);

        return new TableCellTextFormatPlan(
            TableCellTextFormatStatus.Ready,
            shape.Id,
            row,
            col,
            kind,
            targetValue,
            new SetTableCellTextCommand(slideIndex, shape.Id, row, col, editedBody),
            effectiveSelection,
            richTextPlan);
    }

    public static TableCellTextValueFormatPlan PlanFontFamily(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        string? fontFamily,
        (int Start, int End)? selection = null) =>
        PlanTextValueFormat(
            slideIndex,
            slide,
            selectedShapeIds,
            activeCell,
            TableCellTextValueFormatKind.FontFamily,
            fontFamily,
            selection);

    public static TableCellTextValueFormatPlan PlanFontSize(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        double? sizePt,
        (int Start, int End)? selection = null) =>
        PlanTextValueFormat(
            slideIndex,
            slide,
            selectedShapeIds,
            activeCell,
            TableCellTextValueFormatKind.FontSize,
            sizePt,
            selection);

    public static TableCellTextValueFormatPlan PlanColor(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        ThemeAwareColor? color,
        (int Start, int End)? selection = null) =>
        PlanTextValueFormat(
            slideIndex,
            slide,
            selectedShapeIds,
            activeCell,
            TableCellTextValueFormatKind.Color,
            color,
            selection);

    public static TableCellParagraphFormatPlan PlanParagraphAlignment(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        TextAlign alignment,
        (int Start, int End)? selection = null)
    {
        return PlanParagraphFormat(
            slideIndex,
            slide,
            selectedShapeIds,
            activeCell,
            TableCellParagraphFormatKind.Alignment,
            alignment,
            selection,
            body => ApplyParagraphAlignment(body, alignment, selection));
    }

    public static TableCellParagraphFormatPlan PlanParagraphBulletToggle(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        (int Start, int End)? selection = null)
    {
        return PlanParagraphFormat(
            slideIndex,
            slide,
            selectedShapeIds,
            activeCell,
            TableCellParagraphFormatKind.BulletToggle,
            null,
            selection,
            body => ApplyParagraphBulletToggle(body, selection, out bool enabled),
            bulletEnabledFactory: body =>
            {
                int textLength = InCanvasTextEditPlanner.ExtractPlainText(body).Length;
                var range = NormalizeSelection(selection, textLength);
                var indexes = ResolveParagraphIndexes(body, range);
                return indexes.Count > 0 && !indexes.All(index => IsBulletEnabled(body.Paragraphs[index]));
            });
    }

    public static TableCellParagraphFormatPlan PlanParagraphNumberingToggle(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        (int Start, int End)? selection = null)
    {
        return PlanParagraphFormat(
            slideIndex,
            slide,
            selectedShapeIds,
            activeCell,
            TableCellParagraphFormatKind.NumberingToggle,
            null,
            selection,
            body => ApplyParagraphNumberingToggle(body, selection, out bool enabled),
            bulletEnabledFactory: body =>
            {
                int textLength = InCanvasTextEditPlanner.ExtractPlainText(body).Length;
                var range = NormalizeSelection(selection, textLength);
                var indexes = ResolveParagraphIndexes(body, range);
                return indexes.Count > 0 && !indexes.All(index => IsAutoNumberingEnabled(body.Paragraphs[index]));
            });
    }

    public static TableCellParagraphFormatPlan PlanParagraphListPreset(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        TableCellListPresetDescriptor preset,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(preset);

        return PlanParagraphFormat(
            slideIndex,
            slide,
            selectedShapeIds,
            activeCell,
            TableCellParagraphFormatKind.ListPreset,
            null,
            selection,
            body => ApplyParagraphListPreset(body, selection, preset),
            listPreset: preset);
    }

    public static TableCellParagraphFormatPlan PlanParagraphListPreset(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        string presetId,
        (int Start, int End)? selection = null)
    {
        if (!TableCellListPresetCatalog.TryGet(presetId, out var preset) || preset is null)
        {
            return DisabledParagraphFormat(
                TableCellTextFormatStatus.NoTextRuns,
                TableCellParagraphFormatKind.ListPreset,
                null);
        }

        return PlanParagraphListPreset(
            slideIndex,
            slide,
            selectedShapeIds,
            activeCell,
            preset,
            selection);
    }

    public static TableCellParagraphFormatPlan PlanParagraphPictureBullet(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        PresentationPictureBulletPayload payload,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!payload.IsValid)
        {
            return DisabledParagraphFormat(
                TableCellTextFormatStatus.NoTextRuns,
                TableCellParagraphFormatKind.PictureBullet,
                null);
        }

        var image = PresentationPictureBulletAuthoringPlanner.CreateImagePart(payload);
        return PlanParagraphFormat(
            slideIndex,
            slide,
            selectedShapeIds,
            activeCell,
            TableCellParagraphFormatKind.PictureBullet,
            null,
            selection,
            body => ApplyParagraphPictureBullet(body, selection, image),
            bulletImage: image);
    }

    public static TableCellParagraphFormatPlan PlanParagraphIndent(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        (int Start, int End)? selection = null)
    {
        return PlanParagraphFormat(
            slideIndex,
            slide,
            selectedShapeIds,
            activeCell,
            TableCellParagraphFormatKind.Indent,
            null,
            selection,
            body => ApplyParagraphIndent(body, increase: true, selection),
            levelDelta: 1);
    }

    public static TableCellParagraphFormatPlan PlanParagraphOutdent(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        (int Start, int End)? selection = null)
    {
        return PlanParagraphFormat(
            slideIndex,
            slide,
            selectedShapeIds,
            activeCell,
            TableCellParagraphFormatKind.Outdent,
            null,
            selection,
            body => ApplyParagraphIndent(body, increase: false, selection),
            levelDelta: -1);
    }

    private static TableCellParagraphFormatPlan PlanParagraphFormat(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        TableCellParagraphFormatKind kind,
        TextAlign? value,
        (int Start, int End)? selection,
        Func<TextBody, TextBody> mutate,
        Func<TextBody, bool?>? bulletEnabledFactory = null,
        int levelDelta = 0,
        TableCellListPresetDescriptor? listPreset = null,
        ImagePart? bulletImage = null)
    {
        ArgumentNullException.ThrowIfNull(selectedShapeIds);
        ArgumentNullException.ThrowIfNull(mutate);

        if (slide is null)
            return DisabledParagraphFormat(TableCellTextFormatStatus.MissingSlide, kind, value);
        if (selectedShapeIds.Count == 0)
            return DisabledParagraphFormat(TableCellTextFormatStatus.ShapeNotFound, kind, value);

        var shape = ShapeHitTester.FindShape(slide, selectedShapeIds[0]);
        if (shape is null)
            return DisabledParagraphFormat(TableCellTextFormatStatus.ShapeNotFound, kind, value);
        if (shape.Kind != SlideShapeKind.Table || shape.Table is null)
            return DisabledParagraphFormat(TableCellTextFormatStatus.NotTable, kind, value, shape.Id);
        if (activeCell is not { } requested)
            return DisabledParagraphFormat(TableCellTextFormatStatus.MissingActiveCell, kind, value, shape.Id);

        var normalized = NormalizeCell(shape.Table, requested.Row, requested.Col);
        if (normalized is null)
            return DisabledParagraphFormat(TableCellTextFormatStatus.CellOutOfRange, kind, value, shape.Id);

        var (row, col, cell) = normalized.Value;
        if (cell.TextBody is null)
            return DisabledParagraphFormat(TableCellTextFormatStatus.MissingTextBody, kind, value, shape.Id, row, col);
        if (cell.TextBody.Paragraphs.Count == 0)
            return DisabledParagraphFormat(TableCellTextFormatStatus.NoTextRuns, kind, value, shape.Id, row, col);

        bool? bulletEnabled = bulletEnabledFactory?.Invoke(cell.TextBody);
        var editedBody = mutate(cell.TextBody);
        var effectiveSelection = PlanFormatResultSelection(editedBody, selection);
        var richTextPlan = PlanRichTextEdit(editedBody, effectiveSelection);

        return new TableCellParagraphFormatPlan(
            TableCellTextFormatStatus.Ready,
            shape.Id,
            row,
            col,
            kind,
            value,
            new SetTableCellTextCommand(slideIndex, shape.Id, row, col, editedBody),
            effectiveSelection,
            richTextPlan,
            bulletEnabled,
            levelDelta,
            listPreset,
            bulletImage);
    }

    private static TableCellTextValueFormatPlan PlanTextValueFormat(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        TableCellTextValueFormatKind kind,
        object? value,
        (int Start, int End)? selection)
    {
        ArgumentNullException.ThrowIfNull(selectedShapeIds);

        if (slide is null)
            return DisabledValueFormat(TableCellTextFormatStatus.MissingSlide, kind, value);
        if (selectedShapeIds.Count == 0)
            return DisabledValueFormat(TableCellTextFormatStatus.ShapeNotFound, kind, value);

        var shape = ShapeHitTester.FindShape(slide, selectedShapeIds[0]);
        if (shape is null)
            return DisabledValueFormat(TableCellTextFormatStatus.ShapeNotFound, kind, value);
        if (shape.Kind != SlideShapeKind.Table || shape.Table is null)
            return DisabledValueFormat(TableCellTextFormatStatus.NotTable, kind, value, shape.Id);
        if (activeCell is not { } requested)
            return DisabledValueFormat(TableCellTextFormatStatus.MissingActiveCell, kind, value, shape.Id);

        var normalized = NormalizeCell(shape.Table, requested.Row, requested.Col);
        if (normalized is null)
            return DisabledValueFormat(TableCellTextFormatStatus.CellOutOfRange, kind, value, shape.Id);

        var (row, col, cell) = normalized.Value;
        if (cell.TextBody is null)
            return DisabledValueFormat(TableCellTextFormatStatus.MissingTextBody, kind, value, shape.Id, row, col);

        var runs = cell.TextBody.Paragraphs.SelectMany(p => p.Runs).ToList();
        if (runs.Count == 0)
            return DisabledValueFormat(TableCellTextFormatStatus.NoTextRuns, kind, value, shape.Id, row, col);

        var editedBody = TextBodyRunMutationPlanner.ApplyValueFormat(
            cell.TextBody,
            kind,
            value,
            selection);
        var effectiveSelection = PlanFormatResultSelection(editedBody, selection);
        var richTextPlan = PlanRichTextEdit(editedBody, effectiveSelection);

        return new TableCellTextValueFormatPlan(
            TableCellTextFormatStatus.Ready,
            shape.Id,
            row,
            col,
            kind,
            value,
            new SetTableCellTextCommand(slideIndex, shape.Id, row, col, editedBody),
            effectiveSelection,
            richTextPlan);
    }

    private static InCanvasEditorTextSelection PlanFormatResultSelection(
        TextBody body,
        (int Start, int End)? selection)
    {
        int textLength = InCanvasTextEditPlanner.ExtractPlainText(body).Length;
        var normalized = NormalizeSelection(selection, textLength);
        return normalized is { } range
            ? new InCanvasEditorTextSelection(range.Start, range.End)
            : PlanInitialSelection(body);
    }

    private static (int Start, int End)? NormalizeSelection((int Start, int End)? selection, int textLength)
    {
        if (selection is not { } s)
            return null;

        int start = Math.Min(s.Start, s.End);
        int end = Math.Max(s.Start, s.End);
        start = Math.Clamp(start, 0, textLength);
        end = Math.Clamp(end, 0, textLength);
        return end > start ? (start, end) : null;
    }

    /// <summary>
    /// Splits the runs of <paramref name="body"/> (in place) at the [start, end) character
    /// boundaries of its concatenated plain text (paragraphs joined with '\n', matching
    /// <see cref="InCanvasTextEditPlanner.ExtractPlainText"/>), and returns the list of runs
    /// that fall entirely within the selection so callers can apply formatting to just them.
    /// Runs entirely outside the range are left untouched; runs straddling a boundary are
    /// split into an in-range and an out-of-range run (cloned formatting, sliced text).
    /// </summary>
    private static List<Run> SplitRunsAtSelection(TextBody body, int start, int end)
    {
        var selected = new List<Run>();
        int cursor = 0;

        for (int pi = 0; pi < body.Paragraphs.Count; pi++)
        {
            if (pi > 0)
                cursor += 1; // '\n' joining separator, matches ExtractPlainText

            var paragraph = body.Paragraphs[pi];
            var newRuns = new List<Run>();

            foreach (var run in paragraph.Runs)
            {
                int runStart = cursor;
                int runLen = run.Text.Length;
                int runEnd = runStart + runLen;
                cursor = runEnd;

                int overlapStart = Math.Max(runStart, start);
                int overlapEnd = Math.Min(runEnd, end);

                if (overlapEnd <= overlapStart)
                {
                    // No overlap with the selection at all.
                    newRuns.Add(run);
                    continue;
                }

                // Slice into up to three pieces: before (unselected), middle (selected), after (unselected).
                int beforeLen = overlapStart - runStart;
                int selectedLen = overlapEnd - overlapStart;
                int afterLen = runEnd - overlapEnd;

                if (beforeLen > 0)
                    newRuns.Add(TextBodyModelCloner.CloneRunWithText(run, run.Text.Substring(0, beforeLen)));

                var middle = TextBodyModelCloner.CloneRunWithText(
                    run,
                    run.Text.Substring(beforeLen, selectedLen));
                newRuns.Add(middle);
                selected.Add(middle);

                if (afterLen > 0)
                    newRuns.Add(TextBodyModelCloner.CloneRunWithText(
                        run,
                        run.Text.Substring(beforeLen + selectedLen, afterLen)));
            }

            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(newRuns);
        }

        return selected;
    }

    private static bool RunFormatEquals(Run a, Run b) =>
        a.Language == b.Language
        && a.AlternateLanguage == b.AlternateLanguage
        && a.Kumimoji == b.Kumimoji
        && a.SmartTagClean == b.SmartTagClean
        && a.NormalizeHeight == b.NormalizeHeight
        && a.CharacterSpacingHundredthsPt == b.CharacterSpacingHundredthsPt
        && a.KerningThresholdHundredthsPt == b.KerningThresholdHundredthsPt
        && a.UnderlineStyleToken == b.UnderlineStyleToken
        && a.StrikeStyleToken == b.StrikeStyleToken
        && a.Dirty == b.Dirty
        && a.NoProof == b.NoProof
        && a.Error == b.Error
        && a.FontFamily == b.FontFamily
        && a.FontSizePt == b.FontSizePt
        && a.BaselineOffset == b.BaselineOffset
        && a.Bold == b.Bold
        && a.Italic == b.Italic
        && a.BoldSet == b.BoldSet
        && a.ItalicSet == b.ItalicSet
        && a.Underline == b.Underline
        && a.Strikethrough == b.Strikethrough
        && a.Caps == b.Caps
        && TextBodyModelCloner.ColorsEqual(a.Color, b.Color)
        && a.Hyperlink == b.Hyperlink
        && a.Field == b.Field
        && a.TextFill == b.TextFill
        && a.TextOutline == b.TextOutline
        && a.TextShadow == b.TextShadow
        && a.TextReflection == b.TextReflection
        && a.Math == b.Math;

    /// <summary>Merges adjacent runs within each paragraph that share identical formatting, to avoid run proliferation after a selection split.</summary>
    private static void MergeAdjacentRunsWithSameFormat(TextBody body)
    {
        foreach (var paragraph in body.Paragraphs)
        {
            var merged = new List<Run>();
            foreach (var run in paragraph.Runs)
            {
                if (merged.Count > 0 && RunFormatEquals(merged[^1], run))
                    merged[^1].Text += run.Text;
                else
                    merged.Add(run);
            }

            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(merged);
        }
    }

    private static TableCellEditStartPlan NotReady(
        TableCellEditStartStatus status,
        uint shapeId,
        int row,
        int col) =>
        new(status, shapeId, row, col, null, null, null, default, null, null, null);

    private static List<InCanvasEditorRunStyle> BuildRunStyles(TextBody? body)
    {
        var runs = new List<InCanvasEditorRunStyle>();
        if (body is null)
            return runs;

        int cursor = 0;
        for (int pi = 0; pi < body.Paragraphs.Count; pi++)
        {
            if (pi > 0)
                cursor += 1;

            var paragraph = body.Paragraphs[pi];
            for (int ri = 0; ri < paragraph.Runs.Count; ri++)
            {
                var run = paragraph.Runs[ri];
                int start = cursor;
                int end = start + run.Text.Length;
                runs.Add(new InCanvasEditorRunStyle(
                    pi,
                    ri,
                    start,
                    end,
                    run.Text,
                    run.FontFamily,
                    run.FontSizePt,
                    run.Bold,
                    run.Italic,
                    run.Underline,
                    run.Strikethrough,
                    run.Color));
                cursor = end;
            }
        }

        return runs;
    }

    private static bool OverlapsSelection(
        InCanvasEditorRunStyle run,
        InCanvasEditorTextSelection selection)
    {
        if (selection.IsCollapsed)
            return false;

        int start = Math.Min(selection.Start, selection.End);
        int end = Math.Max(selection.Start, selection.End);
        return run.End > start && run.Start < end;
    }

    private static IReadOnlyList<InCanvasEditorSelectedRunRange> BuildSelectedRunRanges(
        IReadOnlyList<InCanvasEditorRunStyle> runs,
        InCanvasEditorTextSelection selection)
    {
        if (selection.IsCollapsed)
            return [];

        int selectionStart = Math.Min(selection.Start, selection.End);
        int selectionEnd = Math.Max(selection.Start, selection.End);
        var selected = new List<InCanvasEditorSelectedRunRange>();

        foreach (var run in runs)
        {
            int overlapStart = Math.Max(run.Start, selectionStart);
            int overlapEnd = Math.Min(run.End, selectionEnd);
            if (overlapEnd <= overlapStart)
                continue;

            selected.Add(new InCanvasEditorSelectedRunRange(
                run.ParagraphIndex,
                run.RunIndex,
                run.Start,
                run.End,
                overlapStart,
                overlapEnd,
                run.Text.Substring(overlapStart - run.Start, overlapEnd - overlapStart)));
        }

        return selected;
    }

    private static IReadOnlyList<InCanvasEditorParagraphStyle> BuildParagraphStyles(TextBody? body)
    {
        var paragraphs = new List<InCanvasEditorParagraphStyle>();
        if (body is null)
            return paragraphs;

        int cursor = 0;
        for (int pi = 0; pi < body.Paragraphs.Count; pi++)
        {
            var paragraph = body.Paragraphs[pi];
            string text = string.Concat(paragraph.Runs.Select(run => run.Text));
            int start = cursor;
            int end = start + text.Length;
            paragraphs.Add(new InCanvasEditorParagraphStyle(
                pi,
                start,
                end,
                text,
                paragraph.Align,
                paragraph.BulletKind,
                paragraph.BulletKind == BulletKind.Char ? paragraph.BulletChar : null,
                paragraph.BulletKind == BulletKind.Auto ? paragraph.AutoNumType : null,
                paragraph.BulletKind == BulletKind.Auto ? paragraph.AutoNumStartAt : null,
                paragraph.BulletSuppressed,
                paragraph.BulletKind == BulletKind.Image ? paragraph.BulletImage : null,
                paragraph.Level,
                paragraph.MarginLeftEmu,
                paragraph.IndentEmu,
                paragraph.AutoNumStartAtSpecified));
            cursor = end + (pi < body.Paragraphs.Count - 1 ? 1 : 0);
        }

        return paragraphs;
    }

    private static IReadOnlyList<InCanvasEditorParagraphStyle> BuildSelectedParagraphStyles(
        IReadOnlyList<InCanvasEditorParagraphStyle> paragraphs,
        InCanvasEditorTextSelection selection,
        int plainTextLength)
    {
        if (paragraphs.Count == 0)
            return [];

        if (selection.IsCollapsed)
        {
            int caret = Math.Clamp(selection.Start, 0, plainTextLength);
            var paragraph = paragraphs.LastOrDefault(candidate =>
                candidate.Start <= caret && caret <= candidate.End);
            return paragraph is null ? [paragraphs[0]] : [paragraph];
        }

        int selectionStart = Math.Min(selection.Start, selection.End);
        int selectionEnd = Math.Max(selection.Start, selection.End);
        var selected = new List<InCanvasEditorParagraphStyle>();

        for (int i = 0; i < paragraphs.Count; i++)
        {
            var paragraph = paragraphs[i];
            bool overlapsText = paragraph.End > selectionStart && paragraph.Start < selectionEnd;
            bool overlapsEmptyParagraph = paragraph.Start == paragraph.End &&
                selectionStart <= paragraph.Start &&
                paragraph.Start < selectionEnd;
            bool overlapsSeparator = i < paragraphs.Count - 1 &&
                paragraph.End < selectionEnd &&
                paragraph.End + 1 > selectionStart;

            if (overlapsText || overlapsEmptyParagraph || overlapsSeparator)
                selected.Add(paragraph);
        }

        return selected;
    }

    private static InCanvasEditorSelectedListState BuildSelectedListState(
        IReadOnlyList<InCanvasEditorParagraphStyle> selectedParagraphs)
    {
        if (selectedParagraphs.Count == 0)
            return InCanvasEditorSelectedListState.None;

        var listParagraphs = selectedParagraphs
            .Where(paragraph => paragraph.HasListFormatting)
            .ToArray();

        if (listParagraphs.Length == 0)
        {
            return InCanvasEditorSelectedListState.None with
            {
                HasSelectedParagraphs = true,
            };
        }

        bool hasMixedListFormatting = selectedParagraphs.Count != listParagraphs.Length ||
            listParagraphs
                .Skip(1)
                .Any(paragraph => !ListStateEquals(listParagraphs[0], paragraph));

        var first = listParagraphs[0];
        if (hasMixedListFormatting)
        {
            return new InCanvasEditorSelectedListState(
                HasSelectedParagraphs: true,
                HasListFormatting: true,
                HasMixedListFormatting: true,
                PresetId: null,
                DisplayName: null,
                PreviewText: null,
                GalleryItemKind: null,
                BulletKind: null,
                BulletChar: null,
                AutoNumType: null,
                AutoNumStartAt: null,
                IsPictureBullet: false);
        }

        if (first.BulletKind == BulletKind.Image)
        {
            return new InCanvasEditorSelectedListState(
                HasSelectedParagraphs: true,
                HasListFormatting: true,
                HasMixedListFormatting: false,
                PresetId: null,
                DisplayName: "Picture Bullet",
                PreviewText: "[image]",
                GalleryItemKind: PresentationListGalleryItemKind.ImageBullet,
                BulletKind: BulletKind.Image,
                BulletChar: null,
                AutoNumType: null,
                AutoNumStartAt: null,
                IsPictureBullet: true);
        }

        if (TableCellListPresetCatalog.TryMatch(
                first.BulletKind,
                first.BulletChar,
                first.AutoNumType,
                out var preset) &&
            preset is not null)
        {
            var itemKind = preset.BulletKind == BulletKind.Auto
                ? PresentationListGalleryItemKind.Numbering
                : PresentationListGalleryItemKind.CharacterBullet;

            return new InCanvasEditorSelectedListState(
                HasSelectedParagraphs: true,
                HasListFormatting: true,
                HasMixedListFormatting: false,
                preset.Id,
                preset.DisplayName,
                PresentationListGalleryPlanner.GetPresetPreviewText(preset),
                itemKind,
                preset.BulletKind,
                preset.BulletChar,
                preset.AutoNumType,
                first.AutoNumStartAt,
                IsPictureBullet: false,
                AutoNumStartAtSpecified: first.AutoNumStartAtSpecified);
        }

        return new InCanvasEditorSelectedListState(
            HasSelectedParagraphs: true,
            HasListFormatting: true,
            HasMixedListFormatting: false,
            PresetId: null,
            DisplayName: first.BulletKind == BulletKind.Auto ? "Custom Numbering" : "Custom Bullet",
            PreviewText: first.BulletKind == BulletKind.Auto
                ? "1.  Custom Numbering"
                : $"{(string.IsNullOrEmpty(first.BulletChar) ? DefaultBulletChar : first.BulletChar)}  Custom Bullet",
            GalleryItemKind: first.BulletKind == BulletKind.Auto
                ? PresentationListGalleryItemKind.Numbering
                : PresentationListGalleryItemKind.CharacterBullet,
            BulletKind: first.BulletKind,
            BulletChar: first.BulletChar,
            AutoNumType: first.AutoNumType,
            AutoNumStartAt: first.AutoNumStartAt,
            IsPictureBullet: false,
            AutoNumStartAtSpecified: first.AutoNumStartAtSpecified);
    }

    private static bool ListStateEquals(
        InCanvasEditorParagraphStyle left,
        InCanvasEditorParagraphStyle right) =>
        left.BulletKind == right.BulletKind &&
        left.BulletChar == right.BulletChar &&
        left.AutoNumType == right.AutoNumType &&
        left.AutoNumStartAt == right.AutoNumStartAt &&
        left.AutoNumStartAtSpecified == right.AutoNumStartAtSpecified &&
        left.BulletSuppressed == right.BulletSuppressed &&
        ImagePartsEqual(left.BulletImage, right.BulletImage);

    private static IReadOnlyList<InCanvasEditorRunStyle> ResolveInitialSelectionStyleRuns(
        IReadOnlyList<InCanvasEditorRunStyle> runs,
        InCanvasEditorTextSelection selection,
        int plainTextLength)
    {
        if (runs.Count == 0)
            return [];

        if (!selection.IsCollapsed)
        {
            var selectedRuns = runs
                .Where(run => OverlapsSelection(run, selection))
                .ToList();
            return selectedRuns.Count > 0 ? selectedRuns : runs;
        }

        int caret = Math.Clamp(selection.Start, 0, plainTextLength);

        var boundaryRun = runs.LastOrDefault(run => run.Start < caret && run.End == caret);
        if (boundaryRun is not null)
            return [boundaryRun];

        var containingRun = runs.FirstOrDefault(run => run.Start <= caret && caret < run.End);
        if (containingRun is not null)
            return [containingRun];

        var precedingRun = runs.LastOrDefault(run => run.End <= caret);
        if (precedingRun is not null)
            return [precedingRun];

        return [runs[0]];
    }

    private static InCanvasEditorTextStyleState BuildStyleState(
        IReadOnlyList<InCanvasEditorRunStyle> runs)
    {
        if (runs.Count == 0)
            return new InCanvasEditorTextStyleState(null, null, null, null, null, null, null);

        var first = runs[0];
        return new InCanvasEditorTextStyleState(
            AllEqual(runs, first.FontFamily, static (run, value) => run.FontFamily == value) ? first.FontFamily : null,
            AllEqual(runs, first.FontSizePt, static (run, value) => run.FontSizePt == value) ? first.FontSizePt : null,
            AllEqual(runs, first.Bold, static (run, value) => run.Bold == value) ? first.Bold : null,
            AllEqual(runs, first.Italic, static (run, value) => run.Italic == value) ? first.Italic : null,
            AllEqual(runs, first.Underline, static (run, value) => run.Underline == value) ? first.Underline : null,
            AllEqual(runs, first.Strikethrough, static (run, value) => run.Strikethrough == value) ? first.Strikethrough : null,
            AllEqual(runs, first.Color, static (run, value) => TextBodyModelCloner.ColorsEqual(run.Color, value)) ? first.Color : null);
    }

    private static bool AllEqual<T>(
        IReadOnlyList<InCanvasEditorRunStyle> runs,
        T value,
        Func<InCanvasEditorRunStyle, T, bool> comparer)
    {
        foreach (var run in runs)
        {
            if (!comparer(run, value))
                return false;
        }

        return true;
    }

    private static bool HasMixedFormatting(IReadOnlyList<InCanvasEditorRunStyle> runs)
    {
        if (runs.Count <= 1)
            return false;

        var first = runs[0];
        return runs.Any(run =>
            run.FontFamily != first.FontFamily ||
            run.FontSizePt != first.FontSizePt ||
            run.Bold != first.Bold ||
            run.Italic != first.Italic ||
            run.Underline != first.Underline ||
            run.Strikethrough != first.Strikethrough ||
            !TextBodyModelCloner.ColorsEqual(run.Color, first.Color));
    }

    private static bool HasMixedParagraphFormatting(IReadOnlyList<InCanvasEditorParagraphStyle> paragraphs)
    {
        if (paragraphs.Count <= 1)
            return false;

        var first = paragraphs[0];
        return paragraphs.Any(paragraph =>
            paragraph.Align != first.Align ||
            paragraph.BulletKind != first.BulletKind ||
            paragraph.BulletChar != first.BulletChar ||
            paragraph.AutoNumType != first.AutoNumType ||
            paragraph.AutoNumStartAt != first.AutoNumStartAt ||
            paragraph.BulletSuppressed != first.BulletSuppressed ||
            !ImagePartsEqual(paragraph.BulletImage, first.BulletImage) ||
            paragraph.Level != first.Level ||
            paragraph.MarginLeftEmu != first.MarginLeftEmu ||
            paragraph.IndentEmu != first.IndentEmu);
    }

    private static bool ImagePartsEqual(ImagePart? left, ImagePart? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        if (!StringComparer.Ordinal.Equals(left.ContentType, right.ContentType))
            return false;

        return left.Bytes.SequenceEqual(right.Bytes);
    }

    private static TableCellTextFormatPlan DisabledFormat(
        TableCellTextFormatStatus status,
        TableCellTextFormatKind kind,
        uint? shapeId = null,
        int? row = null,
        int? col = null) =>
        new(status, shapeId, row, col, kind, null, null);

    private static TableCellTextValueFormatPlan DisabledValueFormat(
        TableCellTextFormatStatus status,
        TableCellTextValueFormatKind kind,
        object? value,
        uint? shapeId = null,
        int? row = null,
        int? col = null) =>
        new(status, shapeId, row, col, kind, value, null);

    private static TableCellParagraphFormatPlan DisabledParagraphFormat(
        TableCellTextFormatStatus status,
        TableCellParagraphFormatKind kind,
        TextAlign? value,
        uint? shapeId = null,
        int? row = null,
        int? col = null) =>
        new(status, shapeId, row, col, kind, value, null);

    private static TableCellNavigationPlan DisabledNavigation(
        TableCellNavigationStatus status,
        TableCellNavigationDirection direction,
        uint? shapeId = null,
        int? row = null,
        int? col = null) =>
        new(status, shapeId, row, col, direction);

    private static List<(int Row, int Col)> GetEditableCellAnchors(TableShape table)
    {
        var anchors = new List<(int Row, int Col)>();
        for (int row = 0; row < table.Rows.Count; row++)
        {
            for (int col = 0; col < table.ColumnWidthsEmu.Count; col++)
            {
                var normalized = NormalizeCell(table, row, col);
                if (normalized is null)
                    continue;
                if (normalized.Value.Row != row || normalized.Value.Col != col)
                    continue;
                if (!anchors.Contains((row, col)))
                    anchors.Add((row, col));
            }
        }

        return anchors;
    }

    private static TextBody ApplyParagraphAlignment(
        TextBody source,
        TextAlign alignment,
        (int Start, int End)? selection)
    {
        var editedBody = TextBodyModelCloner.CloneTextBody(source)!;
        int textLength = InCanvasTextEditPlanner.ExtractPlainText(source).Length;
        var range = NormalizeSelection(selection, textLength);

        foreach (int paragraphIndex in ResolveParagraphIndexes(editedBody, range))
            editedBody.Paragraphs[paragraphIndex].Align = alignment;

        return editedBody;
    }

    private static TextBody ApplyParagraphBulletToggle(
        TextBody source,
        (int Start, int End)? selection,
        out bool enabled)
    {
        var editedBody = TextBodyModelCloner.CloneTextBody(source)!;
        int textLength = InCanvasTextEditPlanner.ExtractPlainText(source).Length;
        var range = NormalizeSelection(selection, textLength);
        var paragraphIndexes = ResolveParagraphIndexes(editedBody, range);
        enabled = paragraphIndexes.Count > 0 &&
            !paragraphIndexes.All(index => IsBulletEnabled(editedBody.Paragraphs[index]));

        foreach (int paragraphIndex in paragraphIndexes)
        {
            var paragraph = editedBody.Paragraphs[paragraphIndex];
            if (enabled)
            {
                paragraph.BulletKind = BulletKind.Char;
                paragraph.BulletChar = string.IsNullOrEmpty(paragraph.BulletChar)
                    ? DefaultBulletChar
                    : paragraph.BulletChar;
                paragraph.BulletImage = null;
                paragraph.BulletSuppressed = false;
            }
            else
            {
                paragraph.BulletKind = BulletKind.None;
                paragraph.BulletChar = null;
                paragraph.BulletImage = null;
                paragraph.BulletSuppressed = true;
            }
        }

        return editedBody;
    }

    private static TextBody ApplyParagraphNumberingToggle(
        TextBody source,
        (int Start, int End)? selection,
        out bool enabled)
    {
        var editedBody = TextBodyModelCloner.CloneTextBody(source)!;
        int textLength = InCanvasTextEditPlanner.ExtractPlainText(source).Length;
        var range = NormalizeSelection(selection, textLength);
        var paragraphIndexes = ResolveParagraphIndexes(editedBody, range);
        enabled = paragraphIndexes.Count > 0 &&
            !paragraphIndexes.All(index => IsAutoNumberingEnabled(editedBody.Paragraphs[index]));

        foreach (int paragraphIndex in paragraphIndexes)
        {
            var paragraph = editedBody.Paragraphs[paragraphIndex];
            if (enabled)
            {
                paragraph.BulletKind = BulletKind.Auto;
                paragraph.BulletChar = null;
                paragraph.BulletImage = null;
                paragraph.AutoNumType = AutoNumType.ArabicPeriod;
                paragraph.AutoNumStartAt = 1;
                paragraph.AutoNumStartAtSpecified = false;
                paragraph.BulletSuppressed = false;
            }
            else
            {
                paragraph.BulletKind = BulletKind.None;
                paragraph.BulletChar = null;
                paragraph.BulletImage = null;
                paragraph.BulletSuppressed = true;
            }
        }

        return editedBody;
    }

    private static TextBody ApplyParagraphListPreset(
        TextBody source,
        (int Start, int End)? selection,
        TableCellListPresetDescriptor preset)
    {
        var editedBody = TextBodyModelCloner.CloneTextBody(source)!;
        int textLength = InCanvasTextEditPlanner.ExtractPlainText(source).Length;
        var range = NormalizeSelection(selection, textLength);

        foreach (int paragraphIndex in ResolveParagraphIndexes(editedBody, range))
            ApplyListPreset(editedBody.Paragraphs[paragraphIndex], preset);

        return editedBody;
    }

    private static void ApplyListPreset(
        Paragraph paragraph,
        TableCellListPresetDescriptor preset)
    {
        paragraph.BulletKind = preset.BulletKind;
        paragraph.BulletSuppressed = false;

        if (preset.BulletKind == BulletKind.Auto)
        {
            paragraph.BulletChar = null;
            paragraph.BulletImage = null;
            paragraph.AutoNumType = preset.AutoNumType ?? AutoNumType.ArabicPeriod;
            paragraph.AutoNumStartAt = Math.Max(1, preset.StartAt);
            paragraph.AutoNumStartAtSpecified = preset.StartAt != 1;
            return;
        }

        if (preset.BulletKind == BulletKind.Char)
        {
            paragraph.BulletChar = string.IsNullOrEmpty(preset.BulletChar)
                ? DefaultBulletChar
                : preset.BulletChar;
            paragraph.BulletImage = null;
            return;
        }

        paragraph.BulletChar = null;
        paragraph.BulletImage = null;
        paragraph.BulletSuppressed = true;
    }

    private static TextBody ApplyParagraphPictureBullet(
        TextBody source,
        (int Start, int End)? selection,
        ImagePart image)
    {
        var editedBody = TextBodyModelCloner.CloneTextBody(source)!;
        int textLength = InCanvasTextEditPlanner.ExtractPlainText(source).Length;
        var range = NormalizeSelection(selection, textLength);

        foreach (int paragraphIndex in ResolveParagraphIndexes(editedBody, range))
            PresentationPictureBulletAuthoringPlanner.ApplyToParagraph(editedBody.Paragraphs[paragraphIndex], image);

        return editedBody;
    }

    private static TextBody ApplyParagraphIndent(
        TextBody source,
        bool increase,
        (int Start, int End)? selection)
    {
        var editedBody = TextBodyModelCloner.CloneTextBody(source)!;
        int textLength = InCanvasTextEditPlanner.ExtractPlainText(source).Length;
        var range = NormalizeSelection(selection, textLength);

        foreach (int paragraphIndex in ResolveParagraphIndexes(editedBody, range))
        {
            var paragraph = editedBody.Paragraphs[paragraphIndex];
            int oldLevel = Math.Clamp(paragraph.Level, 0, MaxParagraphLevel);
            int newLevel = increase
                ? Math.Min(MaxParagraphLevel, oldLevel + 1)
                : Math.Max(0, oldLevel - 1);
            long currentMargin = Math.Max(0, paragraph.MarginLeftEmu ?? oldLevel * ParagraphIndentStepEmu);
            long nextMargin = increase
                ? Math.Max(currentMargin + ParagraphIndentStepEmu, newLevel * ParagraphIndentStepEmu)
                : Math.Max(0, currentMargin - ParagraphIndentStepEmu);

            paragraph.Level = newLevel;
            paragraph.MarginLeftEmu = nextMargin > 0 ? nextMargin : null;

            if (nextMargin > 0 && IsBulletEnabled(paragraph) && paragraph.IndentEmu is null)
                paragraph.IndentEmu = ParagraphHangingIndentEmu;
            else if (nextMargin == 0 && paragraph.IndentEmu == ParagraphHangingIndentEmu)
                paragraph.IndentEmu = null;
        }

        return editedBody;
    }

    private static bool IsBulletEnabled(Paragraph paragraph) =>
        !paragraph.BulletSuppressed && paragraph.BulletKind != BulletKind.None;

    private static bool IsAutoNumberingEnabled(Paragraph paragraph) =>
        !paragraph.BulletSuppressed && paragraph.BulletKind == BulletKind.Auto;

    private static IReadOnlyList<int> ResolveParagraphIndexes(
        TextBody body,
        (int Start, int End)? selection)
    {
        if (selection is null)
            return Enumerable.Range(0, body.Paragraphs.Count).ToArray();

        var selected = new List<int>();
        int cursor = 0;
        for (int pi = 0; pi < body.Paragraphs.Count; pi++)
        {
            int paragraphStart = cursor;
            int paragraphEnd = paragraphStart + body.Paragraphs[pi].Runs.Sum(run => run.Text.Length);
            bool overlapsText = paragraphEnd > selection.Value.Start && paragraphStart < selection.Value.End;
            bool overlapsEmptyParagraph = paragraphStart == paragraphEnd &&
                selection.Value.Start <= paragraphStart &&
                paragraphStart < selection.Value.End;
            bool overlapsSeparator = pi < body.Paragraphs.Count - 1 &&
                paragraphEnd < selection.Value.End &&
                paragraphEnd + 1 > selection.Value.Start;

            if (overlapsText || overlapsEmptyParagraph || overlapsSeparator)
                selected.Add(pi);

            cursor = paragraphEnd + (pi < body.Paragraphs.Count - 1 ? 1 : 0);
        }

        return selected.Count > 0
            ? selected
            : Enumerable.Range(0, body.Paragraphs.Count).ToArray();
    }

    private static bool GetRunFormat(Run run, TableCellTextFormatKind kind) => kind switch
    {
        TableCellTextFormatKind.Bold => run.Bold,
        TableCellTextFormatKind.Italic => run.Italic,
        TableCellTextFormatKind.Underline => run.Underline,
        TableCellTextFormatKind.Superscript => run.BaselineOffset > 0,
        TableCellTextFormatKind.Subscript => run.BaselineOffset < 0,
        _ => false,
    };

    private static void SetRunFormat(Run run, TableCellTextFormatKind kind, bool value)
    {
        switch (kind)
        {
            case TableCellTextFormatKind.Bold:
                run.Bold = value;
                run.BoldSet = true;
                break;
            case TableCellTextFormatKind.Italic:
                run.Italic = value;
                run.ItalicSet = true;
                break;
            case TableCellTextFormatKind.Underline:
                run.Underline = value;
                break;
            case TableCellTextFormatKind.Superscript:
                run.BaselineOffset = value ? 10000 : null;
                break;
            case TableCellTextFormatKind.Subscript:
                run.BaselineOffset = value ? -10000 : null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private static void SetRunValueFormat(Run run, TableCellTextValueFormatKind kind, object? value)
    {
        switch (kind)
        {
            case TableCellTextValueFormatKind.FontFamily:
                run.FontFamily = (string?)value;
                break;
            case TableCellTextValueFormatKind.FontSize:
                run.FontSizePt = (double?)value;
                break;
            case TableCellTextValueFormatKind.Color:
                run.Color = (ThemeAwareColor?)value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private static (int Row, int Col, TableCell Cell)? NormalizeCell(
        TableShape table,
        int row,
        int col)
    {
        if (row < 0 || row >= table.Rows.Count)
            return null;
        if (col < 0 || col >= table.ColumnWidthsEmu.Count)
            return null;
        if (col >= table.Rows[row].Cells.Count)
            return null;

        var requestedCell = table.Rows[row].Cells[col];
        if (!requestedCell.HMerge && !requestedCell.VMerge)
            return (row, col, requestedCell);

        for (int r = 0; r < table.Rows.Count; r++)
        {
            var tableRow = table.Rows[r];
            for (int c = 0; c < tableRow.Cells.Count; c++)
            {
                var candidate = tableRow.Cells[c];
                if (candidate.HMerge || candidate.VMerge)
                    continue;

                int colSpan = Math.Max(1, candidate.GridSpan);
                int rowSpan = Math.Max(1, candidate.RowSpan);
                if (r <= row && row < r + rowSpan && c <= col && col < c + colSpan)
                    return (r, c, candidate);
            }
        }

        return null;
    }
}
