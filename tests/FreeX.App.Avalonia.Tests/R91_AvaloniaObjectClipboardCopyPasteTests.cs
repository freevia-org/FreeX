using Avalonia.Headless;
using Avalonia.Input;
using Free.Shared.Ribbon;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R91 object-clipboard parity: WPF already duplicates the selected drawing object on Ctrl+C/Ctrl+V.
/// These tests drive Avalonia's real keyboard route and keep the cell underneath the object as a
/// guard against silently falling back to range-text copy.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R91_AvaloniaObjectClipboardCopyPasteTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(SelectionPaneObjectKind.Chart)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    public async Task CopyThenPaste_WithDrawingObjectSelected_DuplicatesObjectNotUnderlyingCell(
        SelectionPaneObjectKind kind)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var anchor = new CellAddress(sheet.Id, 2, 2);
                var destination = new CellAddress(sheet.Id, 12, 12);
                sheet.SetCell(anchor, new NumberValue(99));

                var objectId = AddObject(sheet, kind, anchor);
                window.SelectDrawingObjectForTest(kind, objectId, anchor);

                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.C,
                    KeyModifiers = KeyModifiers.Control,
                });

                // A normal cell selection is the real post-copy destination transition. The
                // internal object clipboard must survive that selection change.
                window.SelectCellForTest(destination);
                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.V,
                    KeyModifiers = KeyModifiers.Control,
                });

                CountObjects(sheet, kind).Should().Be(2);
                sheet.GetCell(anchor)!.Value.Should().Be(new NumberValue(99));
                window.SelectedDrawingObjectKindForTest.Should().Be(kind);
                window.SelectedDrawingObjectIdForTest.Should().NotBe(objectId);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(SelectionPaneObjectKind.Chart)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    public async Task CutThenPaste_WithDrawingObjectSelected_MovesOnlyAfterSuccessfulPaste(
        SelectionPaneObjectKind kind)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sourceSheet = window.Session.ActiveSheet;
                var sourceAnchor = new CellAddress(sourceSheet.Id, 2, 2);
                var destinationAnchor = new CellAddress(sourceSheet.Id, 12, 12);
                sourceSheet.SetCell(sourceAnchor, new NumberValue(99));
                var objectId = AddObject(sourceSheet, kind, sourceAnchor);
                window.SelectDrawingObjectForTest(kind, objectId, sourceAnchor);

                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.X,
                    KeyModifiers = KeyModifiers.Control,
                });

                CountObjects(sourceSheet, kind).Should().Be(1, "Cut must not mutate the source before Paste");
                window.SelectCellForTest(destinationAnchor);
                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.V,
                    KeyModifiers = KeyModifiers.Control,
                });

                CountObjects(sourceSheet, kind).Should().Be(1);
                ObjectIds(sourceSheet, kind).Should().NotContain(objectId);
                window.Session.CanUndo.Should().BeTrue();
                window.Session.UndoLastEdit().Success.Should().BeTrue();
                ObjectIds(sourceSheet, kind).Should().Equal([objectId], "Undo must restore the original object");
                CountObjects(sourceSheet, kind).Should().Be(1);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CutThenPaste_CanMoveObjectAcrossSheets_AndKeepsPendingCutOnStaleSource()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sourceSheet = window.Session.ActiveSheet;
                var destinationSheet = window.Session.Workbook.AddSheet("CutDestination");
                var sourceAnchor = new CellAddress(sourceSheet.Id, 2, 2);
                var destinationAnchor = new CellAddress(destinationSheet.Id, 4, 4);
                var shape = new DrawingShapeModel { Name = "MoveMe", Anchor = sourceAnchor };
                sourceSheet.DrawingShapes.Add(shape);
                window.SelectDrawingObjectForTest(SelectionPaneObjectKind.Shape, shape.Id, sourceAnchor);

                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.X,
                    KeyModifiers = KeyModifiers.Control,
                });
                sourceSheet.DrawingShapes.Remove(shape);
                window.Session.SelectSheet(destinationSheet.Id);
                window.SelectCellForTest(destinationAnchor);

                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.V,
                    KeyModifiers = KeyModifiers.Control,
                });
                destinationSheet.DrawingShapes.Should().BeEmpty("a stale source must not paste");

                // Restoring the same source id proves the failed paste did not consume the cut
                // clipboard; the next paste can still complete the move.
                sourceSheet.DrawingShapes.Add(shape);
                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.V,
                    KeyModifiers = KeyModifiers.Control,
                });
                sourceSheet.DrawingShapes.Should().BeEmpty();
                destinationSheet.DrawingShapes.Should().ContainSingle();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EscapeAfterObjectCut_CancelsMoveAndLeavesSourceAvailable()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var anchor = new CellAddress(sheet.Id, 2, 2);
                var destination = new CellAddress(sheet.Id, 12, 12);
                var objectId = AddObject(sheet, SelectionPaneObjectKind.Picture, anchor);
                window.SelectDrawingObjectForTest(SelectionPaneObjectKind.Picture, objectId, anchor);

                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.X,
                    KeyModifiers = KeyModifiers.Control,
                });
                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Escape });
                window.SelectCellForTest(destination);
                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.V,
                    KeyModifiers = KeyModifiers.Control,
                });

                ObjectIds(sheet, SelectionPaneObjectKind.Picture).Should().Equal(objectId);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CutThenPaste_OnProtectedDestination_LeavesSourceAndPendingCutUntouched()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sourceSheet = window.Session.ActiveSheet;
                var destinationSheet = window.Session.Workbook.AddSheet("ProtectedDestination");
                var sourceAnchor = new CellAddress(sourceSheet.Id, 2, 2);
                var destinationAnchor = new CellAddress(destinationSheet.Id, 4, 4);
                var objectId = AddObject(sourceSheet, SelectionPaneObjectKind.TextBox, sourceAnchor);
                window.SelectDrawingObjectForTest(SelectionPaneObjectKind.TextBox, objectId, sourceAnchor);

                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.X,
                    KeyModifiers = KeyModifiers.Control,
                });
                window.Session.ExecuteReviewCommand(new ProtectSheetCommand(
                    destinationSheet.Id,
                    password: null,
                    permissions: [SheetProtectionPermission.SelectLockedCells]));
                window.Session.SelectSheet(destinationSheet.Id);
                window.SelectCellForTest(destinationAnchor);

                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.V,
                    KeyModifiers = KeyModifiers.Control,
                });

                ObjectIds(sourceSheet, SelectionPaneObjectKind.TextBox).Should().Equal(objectId);
                destinationSheet.TextBoxes.Should().BeEmpty();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DrawingObjectContextMenu_CutAndPaste_UsesObjectClipboardRoute()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var source = new CellAddress(sheet.Id, 2, 2);
                var destination = new CellAddress(sheet.Id, 12, 12);
                var objectId = AddObject(sheet, SelectionPaneObjectKind.Chart, source);
                window.SelectDrawingObjectForTest(SelectionPaneObjectKind.Chart, objectId, source);

                InvokePrivate(window, "DispatchDrawingObjectContextMenuCommand", new RibbonCommandId("Cut"));
                CountObjects(sheet, SelectionPaneObjectKind.Chart).Should().Be(1);
                window.SelectCellForTest(destination);
                InvokePrivate(window, "DispatchDrawingObjectContextMenuCommand", new RibbonCommandId("Paste"));

                CountObjects(sheet, SelectionPaneObjectKind.Chart).Should().Be(1);
                ObjectIds(sheet, SelectionPaneObjectKind.Chart).Should().NotContain(objectId);
            }
            finally
            {
                window.Close();
            }
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

    private static int CountObjects(Sheet sheet, SelectionPaneObjectKind kind) => kind switch
    {
        SelectionPaneObjectKind.Chart => sheet.Charts.Count,
        SelectionPaneObjectKind.Shape => sheet.DrawingShapes.Count,
        SelectionPaneObjectKind.Picture => sheet.Pictures.Count,
        SelectionPaneObjectKind.TextBox => sheet.TextBoxes.Count,
        _ => 0,
    };

    private static IEnumerable<Guid> ObjectIds(Sheet sheet, SelectionPaneObjectKind kind) => kind switch
    {
        SelectionPaneObjectKind.Chart => sheet.Charts.Select(item => item.Id),
        SelectionPaneObjectKind.Shape => sheet.DrawingShapes.Select(item => item.Id),
        SelectionPaneObjectKind.Picture => sheet.Pictures.Select(item => item.Id),
        SelectionPaneObjectKind.TextBox => sheet.TextBoxes.Select(item => item.Id),
        _ => [],
    };

    private static void InvokePrivate(MainWindow window, string methodName, params object[] args) =>
        typeof(MainWindow).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .Invoke(window, args);
}
