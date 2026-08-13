using Avalonia.Headless;
using Avalonia.Input;
using Free.Shared.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R130-model-drawing-keyboard-avalonia-1: round 129 fixed a cross-shell keyboard matrix for a
/// genuinely selected picture/shape/text box/chart -- arrow-key nudge, Escape-deselect, F2 no-op,
/// and Ctrl+D/Ctrl+R no-op -- in BOTH shells (WPF: R129_DrawingObjectKeyboardFamilyTests; shared
/// model: DrawingObjectCommandPlannerTests). The Avalonia half of that fix was never exercised by a
/// test because the Avalonia test project was locked by another agent at merge time -- it has shipped
/// on "logic mirrored line-for-line from the proven WPF path" alone. That justification has failed
/// twice before in this codebase (r124: three r123 fixes were WPF-only; r128: a correctly-widened
/// Avalonia guard sat behind a narrower gate so the widened code never executed), so this class
/// actually drives the real Avalonia entry point (RaiseKeyDownForTest -> MainWindow_KeyDownAsync),
/// mirroring R124_BackspaceDrawingObjectTests' SelectDrawingObjectForTest convention for driving
/// object selection without a platform clipboard.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R130_DrawingObjectNudgeEscapeF2FillTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(SelectionPaneObjectKind.Chart)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    public async Task ArrowKey_WithDrawingObjectSelected_NudgesObjectInstead_OfMovingCellCursor(SelectionPaneObjectKind kind)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                // R129/R130 note: new MainWindow([]) falls back to a sample workbook seeding row 1
                // (B1="Windows", C1="macOS"). Anchor well away from row/col 0-2 so this test never
                // collides with that sample content.
                var anchor = new CellAddress(sheet.Id, 8, 8);
                var objectId = AddObject(sheet, kind, anchor);
                window.SelectDrawingObjectForTest(kind, objectId, anchor);

                // SelectDrawingObjectForTest relocates the session's active cell to the object's
                // anchor (matching the real selection-driven path) -- capture the active cell AFTER
                // selecting the object as the baseline the nudge must not move away from.
                var activeCell = window.Session.ActiveCell;
                var (offsetXBefore, leftBefore) = ReadPosition(sheet, kind, objectId);

                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Right });

                var (offsetXAfter, leftAfter) = ReadPosition(sheet, kind, objectId);
                if (kind == SelectionPaneObjectKind.Chart)
                {
                    leftAfter.Should().BeGreaterThan(leftBefore,
                        "the arrow key must nudge the selected chart's Left, not its anchor offset");
                }
                else
                {
                    offsetXAfter.Should().BeGreaterThan(offsetXBefore,
                        "the arrow key must nudge the selected object's pixel offset");
                }

                window.Session.ActiveCell.Should().Be(activeCell,
                    "the cell cursor underneath the selected object must not move");
                window.SelectedDrawingObjectIdForTest.Should().Be(objectId,
                    "the object must remain selected after nudging");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ArrowKey_WithNoDrawingObjectSelected_StillNavigatesCellAsBefore()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var start = new CellAddress(sheet.Id, 8, 8);
                window.SelectCellForTest(start);

                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Right });

                window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 8, 9),
                    "with no drawing object selected, arrow keys must still move the cell cursor as before");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlArrowKey_WithShapeSelected_MovesBySmallerIncrementThanPlainArrow()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var anchor = new CellAddress(sheet.Id, 8, 8);
                var shape = new DrawingShapeModel { Name = "SalesShape", Anchor = anchor };
                sheet.DrawingShapes.Add(shape);
                window.SelectDrawingObjectForTest(SelectionPaneObjectKind.Shape, shape.Id, anchor);

                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Right });
                var coarseOffset = shape.AnchorOffsetX;

                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Right, KeyModifiers = KeyModifiers.Control });
                var fineDelta = shape.AnchorOffsetX - coarseOffset;

                fineDelta.Should().BeGreaterThan(0).And.BeLessThan(coarseOffset,
                    "Ctrl+arrow must nudge by a smaller increment than a plain arrow, matching Excel");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public void EscapeKey_WithPictureSelected_DeselectsObject()
    {
        var source = File.ReadAllText(TestWorkspaceFileLocator.FindFileFromBaseDirectory(
            "src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var escapeRoute = source.IndexOf(
            "if (e.Key == Key.Escape && HasSelectedDrawingObject())",
            StringComparison.Ordinal);
        var nudgeRoute = source.IndexOf(
            "if (TryPlanSelectedDrawingObjectNudge(e.Key, e.KeyModifiers, out var nudgePlan))",
            StringComparison.Ordinal);

        escapeRoute.Should().BeGreaterThanOrEqualTo(0);
        nudgeRoute.Should().BeGreaterThan(escapeRoute);
        source[escapeRoute..nudgeRoute].Should().Contain("ClearSelectedDrawingObject();");
        source[escapeRoute..nudgeRoute].Should().NotContain("TryDeleteSelectedDrawingObject()");
    }

    [Fact]
    public async Task F2Key_WithShapeSelected_DoesNotEnterEditMode()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var activeCell = new CellAddress(sheet.Id, 10, 10);
                sheet.SetCell(activeCell, new NumberValue(42));
                window.SelectCellForTest(activeCell);

                var anchor = new CellAddress(sheet.Id, 8, 8);
                var shape = new DrawingShapeModel { Name = "SalesShape", Anchor = anchor };
                sheet.DrawingShapes.Add(shape);
                window.SelectDrawingObjectForTest(SelectionPaneObjectKind.Shape, shape.Id, anchor);

                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.F2 });

                window.Session.FormulaEditAddress.Should().BeNull(
                    "F2 with a drawing object selected must not open the underlying active cell for edit");
                window.SelectedDrawingObjectKindForTest.Should().Be(SelectionPaneObjectKind.Shape,
                    "the object must remain selected after a no-op F2");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task F2Key_WithNoDrawingObjectSelected_StillEntersEditModeAsBefore()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var activeCell = new CellAddress(sheet.Id, 10, 10);
                sheet.SetCell(activeCell, new NumberValue(7));
                window.SelectCellForTest(activeCell);

                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.F2 });

                window.Session.FormulaEditAddress.Should().Be(activeCell,
                    "with no drawing object selected, F2 must still enter edit mode on the active cell");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlD_WithShapeSelected_DoesNotFill()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var sourceCell = new CellAddress(sheet.Id, 10, 10);
                var targetCell = new CellAddress(sheet.Id, 11, 10);
                sheet.SetCell(sourceCell, new NumberValue(99));
                window.Session.SelectRange(new GridRange(sourceCell, targetCell));

                var anchor = new CellAddress(sheet.Id, 8, 8);
                var shape = new DrawingShapeModel { Name = "SalesShape", Anchor = anchor };
                sheet.DrawingShapes.Add(shape);
                window.SelectDrawingObjectForTest(SelectionPaneObjectKind.Shape, shape.Id, anchor);
                // SelectDrawingObjectForTest collapses the session's selected range down to the
                // object's anchor cell as a side effect of re-anchoring the active cell -- restore the
                // real fill range afterwards (SelectRange does not touch MainWindow's
                // _selectedDrawingObjectKind/-Id fields) so the guard is exercised against a genuine
                // pending fill instead of a degenerate single-cell selection that would no-op anyway.
                window.Session.SelectRange(new GridRange(sourceCell, targetCell));

                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.D, KeyModifiers = KeyModifiers.Control });

                (sheet.GetCell(targetCell)?.Value ?? BlankValue.Instance).Should().Be(BlankValue.Instance,
                    "Ctrl+D with a drawing object selected must not fill the underlying selection");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlD_WithNoDrawingObjectSelected_StillFillsAsBefore()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var sourceCell = new CellAddress(sheet.Id, 10, 10);
                var targetCell = new CellAddress(sheet.Id, 11, 10);
                sheet.SetCell(sourceCell, new NumberValue(99));
                window.Session.SelectRange(new GridRange(sourceCell, targetCell));

                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.D, KeyModifiers = KeyModifiers.Control });

                sheet.GetCell(targetCell)?.Value.Should().Be(new NumberValue(99),
                    "with no drawing object selected, Ctrl+D must still fill down as before");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlR_WithShapeSelected_DoesNotFill()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var sourceCell = new CellAddress(sheet.Id, 10, 10);
                var targetCell = new CellAddress(sheet.Id, 10, 11);
                sheet.SetCell(sourceCell, new NumberValue(99));
                window.Session.SelectRange(new GridRange(sourceCell, targetCell));

                var anchor = new CellAddress(sheet.Id, 8, 8);
                var shape = new DrawingShapeModel { Name = "SalesShape", Anchor = anchor };
                sheet.DrawingShapes.Add(shape);
                window.SelectDrawingObjectForTest(SelectionPaneObjectKind.Shape, shape.Id, anchor);
                // See CtrlD_WithShapeSelected_DoesNotFill for why the range must be restored here.
                window.Session.SelectRange(new GridRange(sourceCell, targetCell));

                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.R, KeyModifiers = KeyModifiers.Control });

                (sheet.GetCell(targetCell)?.Value ?? BlankValue.Instance).Should().Be(BlankValue.Instance,
                    "Ctrl+R with a drawing object selected must not fill the underlying selection");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlR_WithNoDrawingObjectSelected_StillFillsAsBefore()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var sourceCell = new CellAddress(sheet.Id, 10, 10);
                var targetCell = new CellAddress(sheet.Id, 10, 11);
                sheet.SetCell(sourceCell, new NumberValue(99));
                window.Session.SelectRange(new GridRange(sourceCell, targetCell));

                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.R, KeyModifiers = KeyModifiers.Control });

                sheet.GetCell(targetCell)?.Value.Should().Be(new NumberValue(99),
                    "with no drawing object selected, Ctrl+R must still fill right as before");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static Guid AddObject(Sheet sheet, SelectionPaneObjectKind kind, CellAddress anchor)
    {
        switch (kind)
        {
            case SelectionPaneObjectKind.Chart:
                var chart = new ChartModel
                {
                    Title = "Sales",
                    Type = ChartType.Column,
                    DataRange = new GridRange(anchor, new CellAddress(sheet.Id, 12, 12)),
                    Left = 123,
                    Top = 234,
                };
                sheet.Charts.Add(chart);
                return chart.Id;
            case SelectionPaneObjectKind.Shape:
                var shape = new DrawingShapeModel { Name = "SalesShape", Anchor = anchor };
                sheet.DrawingShapes.Add(shape);
                return shape.Id;
            case SelectionPaneObjectKind.Picture:
                var picture = new PictureModel { Name = "SalesPicture", Anchor = anchor };
                sheet.Pictures.Add(picture);
                return picture.Id;
            case SelectionPaneObjectKind.TextBox:
                var textBox = new TextBoxModel { Name = "SalesTextBox", Anchor = anchor, Text = "Sales" };
                sheet.TextBoxes.Add(textBox);
                return textBox.Id;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private static (double offsetX, double left) ReadPosition(Sheet sheet, SelectionPaneObjectKind kind, Guid objectId) => kind switch
    {
        SelectionPaneObjectKind.Chart => (0.0, sheet.Charts.First(c => c.Id == objectId).Left),
        SelectionPaneObjectKind.Shape => (sheet.DrawingShapes.First(s => s.Id == objectId).AnchorOffsetX, 0.0),
        SelectionPaneObjectKind.Picture => (sheet.Pictures.First(p => p.Id == objectId).AnchorOffsetX, 0.0),
        SelectionPaneObjectKind.TextBox => (sheet.TextBoxes.First(t => t.Id == objectId).AnchorOffsetX, 0.0),
        _ => (0.0, 0.0),
    };
}
