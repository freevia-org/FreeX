using Freexcel.Core.Model;
using Freexcel.Core.Formula;

namespace Freexcel.Core.Commands;

/// <summary>
/// Command to edit the value or formula of one or more cells.
/// Captures previous cell state for undo.
/// </summary>
public sealed class EditCellsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly IReadOnlyList<(CellAddress Address, Cell NewCell)> _edits;
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;

    public string Label => _edits.Count == 1 ? "Edit Cell" : $"Edit {_edits.Count} Cells";

    public EditCellsCommand(SheetId sheetId, IReadOnlyList<(CellAddress Address, Cell NewCell)> edits)
    {
        _sheetId = sheetId;
        _edits = edits;
    }

    /// <summary>Convenience constructor for editing a single cell value.</summary>
    public EditCellsCommand(SheetId sheetId, CellAddress address, ScalarValue value)
        : this(sheetId, [(address, Cell.FromValue(value))])
    {
    }

    /// <summary>Convenience factory for editing a single cell value.</summary>
    public static EditCellsCommand ForValue(SheetId sheetId, CellAddress address, ScalarValue value)
        => new(sheetId, address, value);

    /// <summary>Convenience constructor for setting a single cell formula.</summary>
    public static EditCellsCommand ForFormula(SheetId sheetId, CellAddress address, string formulaText)
    {
        return new EditCellsCommand(sheetId, [(address, Cell.FromFormula(formulaText))]);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (sheet.IsProtected)
        {
            foreach (var (addr, _) in _edits)
            {
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, addr))
                    return new CommandOutcome(false, "The sheet is protected.");
            }
        }

        _snapshot = [];

        var affected = new List<CellAddress>();

        foreach (var (addr, newCell) in _edits)
        {
            // Save old state for undo
            var oldCell = sheet.GetCell(addr)?.Clone();
            _snapshot.Add((addr, oldCell));

            // Apply new state while preserving the cell's existing formatting.
            var appliedCell = newCell.Clone();
            if (oldCell is not null)
                appliedCell.StyleId = oldCell.StyleId;
            sheet.SetCell(addr, appliedCell);
            affected.Add(addr);
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;

        var sheet = ctx.GetSheet(_sheetId);

        foreach (var (addr, oldCell) in _snapshot)
        {
            if (oldCell is null)
                sheet.ClearCell(addr);
            else
                sheet.SetCell(addr, oldCell.Clone());
        }
    }
}

/// <summary>Command to add a new sheet to the workbook.</summary>
public sealed class AddSheetCommand : IWorkbookCommand
{
    private readonly string _name;
    private SheetId? _addedSheetId;

    public string Label => $"Add Sheet '{_name}'";

    public AddSheetCommand(string name) => _name = name;

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var validationError = ctx.Workbook.ValidateSheetName(_name);
        if (validationError is not null)
            return new CommandOutcome(false, validationError);

        var sheet = ctx.Workbook.AddSheet(_name);
        _addedSheetId = sheet.Id;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_addedSheetId.HasValue)
            ctx.Workbook.RemoveSheet(_addedSheetId.Value);
    }
}

/// <summary>Command to rename a sheet.</summary>
public sealed class RenameSheetCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _newName;
    private string? _oldName;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];

    public string Label => $"Rename Sheet to '{_newName}'";

    public RenameSheetCommand(SheetId sheetId, string newName)
    {
        _sheetId = sheetId;
        _newName = newName;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var sheet = ctx.GetSheet(_sheetId);
        var validationError = ctx.Workbook.ValidateSheetName(_newName, _sheetId);
        if (validationError is not null)
            return new CommandOutcome(false, validationError);

        _oldName = sheet.Name;
        sheet.Name = _newName;
        _formulaSnapshot.Clear();
        InsertRowsCommand.RewriteAllFormulas(
            ctx.Workbook, new RenameSheetOp(_oldName, _newName), _formulaSnapshot);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_oldName is not null)
        {
            var sheet = ctx.GetSheet(_sheetId);
            sheet.Name = _oldName;
            InsertRowsCommand.RestoreFormulas(ctx.Workbook, _formulaSnapshot);
        }
    }
}

/// <summary>Command to delete a sheet from the workbook.</summary>
public sealed class RemoveSheetCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private Sheet? _removedSheet;
    private int _removedIndex;
    private Dictionary<string, GridRange>? _namedRangeSnapshot;

    public string Label => "Delete Sheet";

    public RemoveSheetCommand(SheetId sheetId) => _sheetId = sheetId;

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        if (ctx.Workbook.Sheets.Count <= 1)
            return new CommandOutcome(false, "Cannot delete the only sheet.");

        var sheet = ctx.GetSheet(_sheetId);
        _removedSheet = sheet;
        var sheets = ctx.Workbook.Sheets;
        for (int i = 0; i < sheets.Count; i++)
            if (sheets[i].Id == _sheetId) { _removedIndex = i; break; }
        _namedRangeSnapshot = InsertRowsCommand.CaptureNamedRanges(ctx.Workbook);
        foreach (var (name, range) in ctx.Workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet == _sheetId)
                ctx.Workbook.RemoveNamedRange(name);
        }
        ctx.Workbook.RemoveSheet(_sheetId);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_removedSheet is not null)
        {
            ctx.Workbook.InsertSheet(_removedIndex, _removedSheet);
            InsertRowsCommand.RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
        }
    }
}

/// <summary>Command to move a sheet tab from one workbook position to another.</summary>
public sealed class MoveSheetCommand : IWorkbookCommand
{
    private readonly int _fromIndex;
    private readonly int _toIndex;
    private bool _applied;

    public string Label => "Move Sheet";

    public MoveSheetCommand(int fromIndex, int toIndex)
    {
        _fromIndex = fromIndex;
        _toIndex = toIndex;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        if (!IsValidIndex(ctx.Workbook, _fromIndex) || !IsValidIndex(ctx.Workbook, _toIndex))
            return new CommandOutcome(false, "Sheet index is out of range.");

        if (_fromIndex == _toIndex)
            return new CommandOutcome(true);

        ctx.Workbook.MoveSheet(_fromIndex, _toIndex);
        _applied = true;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied || _fromIndex == _toIndex)
            return;

        ctx.Workbook.MoveSheet(_toIndex, _fromIndex);
        _applied = false;
    }

    private static bool IsValidIndex(Workbook workbook, int index) =>
        index >= 0 && index < workbook.Sheets.Count;
}

/// <summary>Command to duplicate a worksheet immediately after the source sheet.</summary>
public sealed class DuplicateSheetCommand : IWorkbookCommand
{
    private readonly SheetId _sourceSheetId;
    private readonly string? _requestedName;
    private SheetId? _copySheetId;
    private int _insertIndex;

    public string Label => "Duplicate Sheet";

    public DuplicateSheetCommand(SheetId sourceSheetId, string? name = null)
    {
        _sourceSheetId = sourceSheetId;
        _requestedName = name;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var source = ctx.GetSheet(_sourceSheetId);
        var sourceIndex = ctx.Workbook.Sheets.ToList().FindIndex(s => s.Id == _sourceSheetId);
        if (sourceIndex < 0)
            return new CommandOutcome(false, "Source sheet was not found.");

        var name = _requestedName ?? GenerateCopyName(ctx.Workbook, source.Name);
        var validationError = ctx.Workbook.ValidateSheetName(name);
        if (validationError is not null)
            return new CommandOutcome(false, validationError);

        var copyId = SheetId.New();
        var copy = CloneSheet(source, copyId, name);
        _insertIndex = sourceIndex + 1;
        _copySheetId = copyId;
        ctx.Workbook.InsertSheet(_insertIndex, copy);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_copySheetId.HasValue)
            ctx.Workbook.RemoveSheet(_copySheetId.Value);
    }

    private static string GenerateCopyName(Workbook workbook, string sourceName)
    {
        for (int n = 2; n < 10_000; n++)
        {
            var suffix = $" ({n})";
            var baseName = sourceName.Length + suffix.Length <= 31
                ? sourceName
                : sourceName[..(31 - suffix.Length)];
            var candidate = baseName + suffix;
            if (workbook.ValidateSheetName(candidate) is null)
                return candidate;
        }

        return $"Sheet{Guid.NewGuid():N}"[..31];
    }

    private static Sheet CloneSheet(Sheet source, SheetId copyId, string name)
    {
        var copy = new Sheet(copyId, name)
        {
            DefaultColumnWidth = source.DefaultColumnWidth,
            DefaultRowHeight = source.DefaultRowHeight,
            FrozenRows = source.FrozenRows,
            FrozenCols = source.FrozenCols,
            SplitRow = source.SplitRow,
            SplitColumn = source.SplitColumn,
            PrintArea = RemapRange(source.PrintArea, copyId),
            PageOrientation = source.PageOrientation,
            PaperSize = source.PaperSize,
            PageMargins = source.PageMargins,
            HeaderMargin = source.HeaderMargin,
            FooterMargin = source.FooterMargin,
            PrintGridlines = source.PrintGridlines,
            PrintHeadings = source.PrintHeadings,
            ScaleToFit = source.ScaleToFit,
            PrintTitleRows = source.PrintTitleRows,
            PrintTitleColumns = source.PrintTitleColumns,
            PageHeader = source.PageHeader,
            PageFooter = source.PageFooter,
            FirstPageHeader = source.FirstPageHeader,
            FirstPageFooter = source.FirstPageFooter,
            EvenPageHeader = source.EvenPageHeader,
            EvenPageFooter = source.EvenPageFooter,
            DifferentFirstPageHeaderFooter = source.DifferentFirstPageHeaderFooter,
            DifferentOddEvenHeaderFooter = source.DifferentOddEvenHeaderFooter,
            HeaderFooterScaleWithDocument = source.HeaderFooterScaleWithDocument,
            HeaderFooterAlignWithMargins = source.HeaderFooterAlignWithMargins,
            CenterHorizontallyOnPage = source.CenterHorizontallyOnPage,
            CenterVerticallyOnPage = source.CenterVerticallyOnPage,
            PageOrder = source.PageOrder,
            FirstPageNumber = source.FirstPageNumber,
            PrintBlackAndWhite = source.PrintBlackAndWhite,
            PrintDraftQuality = source.PrintDraftQuality,
            PrintQualityDpi = source.PrintQualityDpi,
            PrintErrorValue = source.PrintErrorValue,
            PrintComments = source.PrintComments,
            ViewMode = source.ViewMode,
            IsHidden = false,
            TabColor = source.TabColor,
            IsProtected = source.IsProtected,
            ProtectionPassword = source.ProtectionPassword
        };

        foreach (var (col, width) in source.ColumnWidths)
            copy.ColumnWidths[col] = width;
        foreach (var (row, height) in source.RowHeights)
            copy.RowHeights[row] = height;
        foreach (var row in source.HiddenRows)
            copy.HiddenRows.Add(row);
        foreach (var row in source.FilterHiddenRows)
            copy.FilterHiddenRows.Add(row);
        foreach (var col in source.HiddenCols)
            copy.HiddenCols.Add(col);
        foreach (var rowBreak in source.RowPageBreaks)
            copy.RowPageBreaks.Add(rowBreak);
        foreach (var columnBreak in source.ColumnPageBreaks)
            copy.ColumnPageBreaks.Add(columnBreak);
        foreach (var (address, cell) in source.EnumerateCells())
            copy.SetCell(RemapAddress(address, copyId), cell.Clone());
        foreach (var range in source.MergedRegions)
            copy.MergedRegions.Add(RemapRange(range, copyId));
        foreach (var (address, comment) in source.Comments)
            copy.Comments[RemapAddress(address, copyId)] = comment;
        foreach (var (address, hyperlink) in source.Hyperlinks)
            copy.Hyperlinks[RemapAddress(address, copyId)] = hyperlink;
        foreach (var range in source.AllowEditRanges)
            copy.AllowEditRanges.Add(RemapRange(range, copyId));
        foreach (var chart in source.Charts)
            copy.Charts.Add(new ChartModel
            {
                Type = chart.Type,
                DataRange = RemapRange(chart.DataRange, copyId),
                FirstRowIsHeader = chart.FirstRowIsHeader,
                FirstColIsCategories = chart.FirstColIsCategories,
                Title = chart.Title,
                XAxisTitle = chart.XAxisTitle,
                YAxisTitle = chart.YAxisTitle,
                LegendPosition = chart.LegendPosition,
                ShowLegend = chart.ShowLegend,
                Left = chart.Left,
                Top = chart.Top,
                Width = chart.Width,
                Height = chart.Height
            });
        foreach (var textBox in source.TextBoxes)
            copy.TextBoxes.Add(new TextBoxModel
            {
                Anchor = RemapAddress(textBox.Anchor, copyId),
                Text = textBox.Text,
                Width = textBox.Width,
                Height = textBox.Height,
                RotationDegrees = textBox.RotationDegrees,
                FillColor = textBox.FillColor,
                OutlineColor = textBox.OutlineColor
            });
        foreach (var shape in source.DrawingShapes)
            copy.DrawingShapes.Add(new DrawingShapeModel
            {
                Anchor = RemapAddress(shape.Anchor, copyId),
                Kind = shape.Kind,
                Width = shape.Width,
                Height = shape.Height,
                RotationDegrees = shape.RotationDegrees,
                FillColor = shape.FillColor,
                OutlineColor = shape.OutlineColor
            });
        foreach (var picture in source.Pictures)
        {
            var copiedPicture = new PictureModel
            {
                Anchor = RemapAddress(picture.Anchor, copyId),
                Kind = picture.Kind,
                SourceRowCount = picture.SourceRowCount,
                SourceColumnCount = picture.SourceColumnCount,
                ImageBytes = picture.ImageBytes?.ToArray(),
                ContentType = picture.ContentType,
                Width = picture.Width,
                Height = picture.Height,
                RotationDegrees = picture.RotationDegrees
            };
            foreach (var cell in picture.Cells)
                copiedPicture.Cells.Add(cell);
            copy.Pictures.Add(copiedPicture);
        }
        foreach (var sparkline in source.Sparklines)
            copy.Sparklines.Add(new SparklineModel
            {
                DataRange = RemapRange(sparkline.DataRange, copyId),
                Location = RemapAddress(sparkline.Location, copyId),
                Kind = sparkline.Kind
            });
        foreach (var cf in source.ConditionalFormats)
            copy.ConditionalFormats.Add(new ConditionalFormat
            {
                AppliesTo = RemapRange(cf.AppliesTo, copyId),
                Priority = cf.Priority,
                RuleType = cf.RuleType,
                Operator = cf.Operator,
                Value1 = cf.Value1,
                Value2 = cf.Value2,
                FormatIfTrue = cf.FormatIfTrue?.Clone(),
                MinColor = cf.MinColor,
                MidColor = cf.MidColor,
                MaxColor = cf.MaxColor,
                UseThreeColorScale = cf.UseThreeColorScale,
                DataBarColor = cf.DataBarColor,
                AboveAverage = cf.AboveAverage
            });
        foreach (var dv in source.DataValidations)
            copy.DataValidations.Add(new DataValidation
            {
                AppliesTo = RemapRange(dv.AppliesTo, copyId),
                Type = dv.Type,
                Operator = dv.Operator,
                Formula1 = dv.Formula1,
                Formula2 = dv.Formula2,
                AllowBlank = dv.AllowBlank,
                ShowDropdown = dv.ShowDropdown,
                ErrorTitle = dv.ErrorTitle,
                ErrorMessage = dv.ErrorMessage,
                PromptTitle = dv.PromptTitle,
                PromptMessage = dv.PromptMessage
            });

        return copy;
    }

    private static CellAddress RemapAddress(CellAddress address, SheetId sheetId) =>
        new(sheetId, address.Row, address.Col);

    private static GridRange RemapRange(GridRange range, SheetId sheetId) =>
        new(RemapAddress(range.Start, sheetId), RemapAddress(range.End, sheetId));

    private static GridRange? RemapRange(GridRange? range, SheetId sheetId) =>
        range.HasValue ? RemapRange(range.Value, sheetId) : null;
}

/// <summary>Command to hide or unhide a worksheet.</summary>
public sealed class SetSheetHiddenCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly bool _hidden;
    private bool? _previousHidden;

    public string Label => _hidden ? "Hide Sheet" : "Unhide Sheet";

    public SetSheetHiddenCommand(SheetId sheetId, bool hidden)
    {
        _sheetId = sheetId;
        _hidden = hidden;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var sheet = ctx.GetSheet(_sheetId);
        if (_hidden && !ctx.Workbook.Sheets.Any(s => s.Id != _sheetId && !s.IsHidden))
            return new CommandOutcome(false, "Cannot hide the only visible sheet.");

        _previousHidden = sheet.IsHidden;
        sheet.IsHidden = _hidden;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHidden is null)
            return;

        ctx.GetSheet(_sheetId).IsHidden = _previousHidden.Value;
    }
}

/// <summary>Command to set or clear a worksheet tab color.</summary>
public sealed class SetSheetTabColorCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellColor? _color;
    private CellColor? _previousColor;
    private bool _hadPreviousColor;

    public string Label => "Set Sheet Tab Color";

    public SetSheetTabColorCommand(SheetId sheetId, CellColor? color)
    {
        _sheetId = sheetId;
        _color = color;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var sheet = ctx.GetSheet(_sheetId);
        _previousColor = sheet.TabColor;
        _hadPreviousColor = sheet.TabColor.HasValue;
        sheet.TabColor = _color;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.GetSheet(_sheetId).TabColor = _hadPreviousColor ? _previousColor : null;
    }
}
