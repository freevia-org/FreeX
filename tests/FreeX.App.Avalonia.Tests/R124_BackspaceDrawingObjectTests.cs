using Avalonia.Headless;
using Avalonia.Input;
using Free.Shared.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R124-model-drawing-backspace-avalonia-1: round 123 fixed the WPF host's Backspace-with-a-
/// selected-drawing-object bug (R123_BackspaceDrawingObjectTests / MainWindow.KeyboardCommands.cs's
/// HasSelectedDrawingObject guard) but the identical bug in this shell was left unfixed --
/// MainWindow.KeyboardParity.cs's ClearSelectionAndEdit (routed here by Key.Back, the same
/// AvaloniaHostShortcut.ClearSelectionAndEdit table entries R75_BackspaceActiveCellOnlyClearTests
/// exercises) never checked _selectedDrawingObjectKind/_selectedDrawingObjectId before clearing the
/// active cell and opening it for edit. In real Excel, Backspace with a picture/shape/text box/chart
/// selected is a total no-op: no object deletion (that's Delete-only, see
/// R121_AvaloniaDeleteDrawingObjectTests), no cell mutation, no edit mode, and the object stays
/// selected.
///
/// These tests drive the REAL Avalonia entry point (RaiseKeyDownForTest ->
/// MainWindow_KeyDownAsync -> TryHandleAvaloniaHostShortcutAsync -> ClearSelectionAndEdit), the same
/// route a real Backspace keypress takes, mirroring R121_AvaloniaDeleteDrawingObjectTests'
/// SelectDrawingObjectForTest convention for driving object selection without a platform clipboard.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R124_BackspaceDrawingObjectTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(SelectionPaneObjectKind.Chart)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    public async Task BackspaceKey_WithDrawingObjectSelected_IsTotalNoOp(SelectionPaneObjectKind kind)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var anchor = new CellAddress(sheet.Id, 5, 5);
                sheet.SetCell(anchor, new NumberValue(42));

                var objectId = AddObject(sheet, kind, anchor);
                window.SelectDrawingObjectForTest(kind, objectId, anchor);

                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Back });

                sheet.GetCell(anchor)!.Value.Should().Be(new NumberValue(42),
                    "Backspace with a drawing object selected must not touch the cell underneath it");
                window.Session.FormulaEditAddress.Should().BeNull(
                    "Backspace with a drawing object selected must not open the in-cell editor");
                ObjectExists(sheet, kind, objectId).Should().BeTrue(
                    "Backspace must never delete a selected object -- only Delete does that");
                window.SelectedDrawingObjectKindForTest.Should().Be(kind,
                    "the object must remain selected after a no-op Backspace");
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
    public async Task BackspaceKey_WithNoDrawingObjectSelected_StillClearsActiveCellAndEntersEditMode()
    {
        // Sibling no-regression (shares R75_BackspaceActiveCellOnlyClearTests' territory): the new
        // guard must only fire when a drawing object is genuinely selected -- plain cell Backspace
        // must be completely unaffected.
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var activeCell = new CellAddress(sheet.Id, 2, 2);
                sheet.SetCell(activeCell, new NumberValue(7));
                window.SelectCellForTest(activeCell);

                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Back });

                sheet.GetCell(activeCell)?.Value.Should().Be(BlankValue.Instance,
                    "with no drawing object selected, Backspace must still clear the active cell as before");
                window.Session.FormulaEditAddress.Should().Be(activeCell,
                    "with no drawing object selected, Backspace must still enter edit mode on the active cell");
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
                    DataRange = new GridRange(anchor, new CellAddress(sheet.Id, 4, 3)),
                    Left = 123,
                    Top = 234,
                };
                sheet.Charts.Add(chart);
                return chart.Id;
            case SelectionPaneObjectKind.Shape:
                var shape = new DrawingShapeModel
                {
                    Name = "SalesShape",
                    Anchor = anchor,
                };
                sheet.DrawingShapes.Add(shape);
                return shape.Id;
            case SelectionPaneObjectKind.Picture:
                var picture = new PictureModel
                {
                    Name = "SalesPicture",
                    Anchor = anchor,
                };
                sheet.Pictures.Add(picture);
                return picture.Id;
            case SelectionPaneObjectKind.TextBox:
                var textBox = new TextBoxModel
                {
                    Name = "SalesTextBox",
                    Anchor = anchor,
                    Text = "Sales",
                };
                sheet.TextBoxes.Add(textBox);
                return textBox.Id;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    // Checks the SPECIFIC object created for this test case is still present, rather than asserting a
    // total sheet count -- the shared headless MainWindow session can carry residual objects across
    // test cases on the same ActiveSheet, so an exact-count assertion is not reliable test isolation.
    private static bool ObjectExists(Sheet sheet, SelectionPaneObjectKind kind, Guid objectId) => kind switch
    {
        SelectionPaneObjectKind.Chart => sheet.Charts.Any(o => o.Id == objectId),
        SelectionPaneObjectKind.Shape => sheet.DrawingShapes.Any(o => o.Id == objectId),
        SelectionPaneObjectKind.Picture => sheet.Pictures.Any(o => o.Id == objectId),
        SelectionPaneObjectKind.TextBox => sheet.TextBoxes.Any(o => o.Id == objectId),
        _ => false,
    };
}
