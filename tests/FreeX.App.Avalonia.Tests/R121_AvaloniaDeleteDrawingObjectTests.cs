using Avalonia.Headless;
using Avalonia.Input;
using Free.Shared.Ribbon;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R121 (round 111 backlog): FreeX had no way to delete a picture/text box/shape/chart -- the Delete
/// key always routed to ClearContentsCommand over the cell range, never touching a selected drawing
/// object. These tests drive Avalonia's REAL Delete-key route
/// (<c>MainWindow.MainWindow_KeyDownAsync</c> -&gt; <c>TryDeleteSelectedDrawingObject</c>) the same way
/// R91_AvaloniaObjectClipboardCopyPasteTests drives Ctrl+C/Ctrl+V.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R121_AvaloniaDeleteDrawingObjectTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(SelectionPaneObjectKind.Chart)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    public async Task DeleteKey_WithDrawingObjectSelected_RemovesObjectNotCellContents(
        SelectionPaneObjectKind kind)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                ClearSampleDrawingObjects(sheet);
                var anchor = new CellAddress(sheet.Id, 2, 2);
                sheet.SetCell(anchor, new NumberValue(99));

                var objectId = AddObject(sheet, kind, anchor);
                window.SelectDrawingObjectForTest(kind, objectId, anchor);

                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Delete });

                CountObjects(sheet, kind).Should().Be(0, "Delete on a selected drawing object must remove it");
                sheet.GetCell(anchor)!.Value.Should().Be(new NumberValue(99),
                    "Delete on a selected object must not also clear the cell underneath it");
                window.SelectedDrawingObjectKindForTest.Should().BeNull("selection must clear after delete");
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
    public async Task DeleteKey_WithNoDrawingObjectSelected_StillClearsCellContents()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                ClearSampleDrawingObjects(sheet);
                var anchor = new CellAddress(sheet.Id, 3, 3);
                sheet.SetCell(anchor, new NumberValue(7));
                window.SelectCellForTest(anchor);

                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Delete });

                // Clear Contents blanks the cell in place (preserving its formatting) rather than
                // dropping the cell entirely, so the cleared value is BlankValue -- never null. This
                // matches the sibling R124_BackspaceDrawingObjectTests assertion.
                sheet.GetCell(anchor)?.Value.Should().Be(BlankValue.Instance,
                    "with no drawing object selected, Delete must fall through to ordinary Clear Contents");
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
    public async Task DeleteKey_OnLockedObjectUnderEditObjectsProtection_IsRejected()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                ClearSampleDrawingObjects(sheet);
                var anchor = new CellAddress(sheet.Id, 2, 2);
                var objectId = AddObject(sheet, SelectionPaneObjectKind.Picture, anchor);
                window.Session.ExecuteReviewCommand(new ProtectSheetCommand(
                    sheet.Id,
                    password: null,
                    permissions: [SheetProtectionPermission.SelectLockedCells]));
                window.SelectDrawingObjectForTest(SelectionPaneObjectKind.Picture, objectId, anchor);

                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Delete });

                CountObjects(sheet, SelectionPaneObjectKind.Picture).Should().Be(1,
                    "a locked picture must not be deleted while the sheet blocks Edit Objects");
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

    /// <summary>
    /// The Avalonia shell's no-argument startup deliberately loads <c>PortPreviewWorkbookFactory</c>'s
    /// SAMPLE workbook ("Showing sample workbook."), which already carries one shape, one text box and
    /// two pictures. These tests assert ABSOLUTE object counts, so the sample objects are dropped here
    /// to establish the empty precondition the assertions describe. (Chart is the only kind the sample
    /// has none of, which is why the Chart theory case was the only one that ever passed.)
    /// </summary>
    private static void ClearSampleDrawingObjects(Sheet sheet)
    {
        sheet.Pictures.Clear();
        sheet.TextBoxes.Clear();
        sheet.DrawingShapes.Clear();
        sheet.Charts.Clear();
    }

    private static int CountObjects(Sheet sheet, SelectionPaneObjectKind kind) => kind switch
    {
        SelectionPaneObjectKind.Chart => sheet.Charts.Count,
        SelectionPaneObjectKind.Shape => sheet.DrawingShapes.Count,
        SelectionPaneObjectKind.Picture => sheet.Pictures.Count,
        SelectionPaneObjectKind.TextBox => sheet.TextBoxes.Count,
        _ => 0,
    };
}
