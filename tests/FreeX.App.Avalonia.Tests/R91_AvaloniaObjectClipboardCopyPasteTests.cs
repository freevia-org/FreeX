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
                ClearSampleDrawingObjects(sheet);
                var anchor = new CellAddress(sheet.Id, 2, 2);
                var destination = new CellAddress(sheet.Id, 12, 12);
                sheet.SetCell(anchor, new NumberValue(99));

                var objectId = AddObject(sheet, kind, anchor);
                var sourcePosition = ObjectPosition(sheet, kind, objectId);
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
                var pastedPosition = ObjectPosition(
                    sheet,
                    kind,
                    window.SelectedDrawingObjectIdForTest!.Value);
                pastedPosition.X.Should().BeApproximately(sourcePosition.X + 12, 0.001);
                pastedPosition.Y.Should().BeApproximately(sourcePosition.Y + 12, 0.001);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
            return true;
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
                ClearSampleDrawingObjects(sourceSheet);
                var sourceAnchor = new CellAddress(sourceSheet.Id, 2, 2);
                var destinationAnchor = new CellAddress(sourceSheet.Id, 12, 12);
                sourceSheet.SetCell(sourceAnchor, new NumberValue(99));
                var objectId = AddObject(sourceSheet, kind, sourceAnchor);
                var sourcePosition = ObjectPosition(sourceSheet, kind, objectId);
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
                var movedPosition = ObjectPosition(
                    sourceSheet,
                    kind,
                    window.SelectedDrawingObjectIdForTest!.Value);
                movedPosition.X.Should().BeApproximately(sourcePosition.X, 0.001);
                movedPosition.Y.Should().BeApproximately(sourcePosition.Y, 0.001);
                window.Session.CanUndo.Should().BeTrue();
                window.Session.UndoLastEdit().Success.Should().BeTrue();
                ObjectIds(sourceSheet, kind).Should().Equal([objectId], "Undo must restore the original object");
                CountObjects(sourceSheet, kind).Should().Be(1);
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
    public async Task CutThenPaste_CanMoveObjectAcrossSheets_AndKeepsPendingCutOnStaleSource()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sourceSheet = window.Session.ActiveSheet;
                ClearSampleDrawingObjects(sourceSheet);
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
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
            return true;
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
                ClearSampleDrawingObjects(sheet);
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
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
            return true;
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
                ClearSampleDrawingObjects(sourceSheet);
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
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(SelectionPaneObjectKind.Chart)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    public async Task CopyToProtectedDestination_RejectsCreationForEveryObjectKind(
        SelectionPaneObjectKind kind)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sourceSheet = window.Session.ActiveSheet;
                ClearSampleDrawingObjects(sourceSheet);
                var destinationSheet = window.Session.Workbook.AddSheet("ProtectedCopyDestination");
                var sourceAnchor = new CellAddress(sourceSheet.Id, 2, 2);
                var objectId = AddObject(sourceSheet, kind, sourceAnchor);
                window.Session.ExecuteReviewCommand(new ProtectSheetCommand(
                    destinationSheet.Id,
                    password: null,
                    permissions: [SheetProtectionPermission.SelectLockedCells]));

                var result = window.Session.ExecuteReviewCommand(
                    new DuplicateDrawingObjectCommand(
                        sourceSheet.Id,
                        destinationSheet.Id,
                        kind,
                        objectId));

                result.Success.Should().BeFalse();
                CountObjects(sourceSheet, kind).Should().Be(1);
                CountObjects(destinationSheet, kind).Should().Be(0);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(SelectionPaneObjectKind.Chart)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    public async Task SameSheetCut_UsesSourceObjectProtection(
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
                var objectId = AddObject(sheet, kind, anchor);
                SetObjectLocked(sheet, kind, objectId, locked: false);
                var sourcePosition = ObjectPosition(sheet, kind, objectId);
                window.Session.ExecuteReviewCommand(new ProtectSheetCommand(
                    sheet.Id,
                    password: null,
                    permissions: [SheetProtectionPermission.SelectLockedCells]));

                var result = window.Session.ExecuteReviewCommand(
                    new DuplicateDrawingObjectCommand(
                        sheet.Id,
                        sheet.Id,
                        kind,
                        objectId,
                        removeSource: true));

                result.Success.Should().BeTrue();
                CountObjects(sheet, kind).Should().Be(1);
                var movedId = ObjectIds(sheet, kind).Single();
                movedId.Should().NotBe(objectId);
                var movedPosition = ObjectPosition(sheet, kind, movedId);
                movedPosition.X.Should().BeApproximately(sourcePosition.X, 0.001);
                movedPosition.Y.Should().BeApproximately(sourcePosition.Y, 0.001);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(SelectionPaneObjectKind.Chart, true, false, false)]
    [InlineData(SelectionPaneObjectKind.Shape, true, false, false)]
    [InlineData(SelectionPaneObjectKind.Picture, true, false, false)]
    [InlineData(SelectionPaneObjectKind.TextBox, true, false, false)]
    [InlineData(SelectionPaneObjectKind.Chart, false, true, true)]
    [InlineData(SelectionPaneObjectKind.Shape, false, true, true)]
    [InlineData(SelectionPaneObjectKind.Picture, false, true, true)]
    [InlineData(SelectionPaneObjectKind.TextBox, false, true, true)]
    public async Task CrossSheetCut_RequiresSourceRemovalAndDestinationCreationPermission(
        SelectionPaneObjectKind kind,
        bool protectSource,
        bool protectDestination,
        bool sourceObjectUnlocked)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sourceSheet = window.Session.ActiveSheet;
                ClearSampleDrawingObjects(sourceSheet);
                var destinationSheet = window.Session.Workbook.AddSheet("GuardDestination");
                var sourceAnchor = new CellAddress(sourceSheet.Id, 2, 2);
                var objectId = AddObject(sourceSheet, kind, sourceAnchor);
                SetObjectLocked(sourceSheet, kind, objectId, !sourceObjectUnlocked);

                if (protectSource)
                    window.Session.ExecuteReviewCommand(new ProtectSheetCommand(
                        sourceSheet.Id,
                        password: null,
                        permissions: [SheetProtectionPermission.SelectLockedCells]));
                if (protectDestination)
                    window.Session.ExecuteReviewCommand(new ProtectSheetCommand(
                        destinationSheet.Id,
                        password: null,
                        permissions: [SheetProtectionPermission.SelectLockedCells]));

                var result = window.Session.ExecuteReviewCommand(
                    new DuplicateDrawingObjectCommand(
                        sourceSheet.Id,
                        destinationSheet.Id,
                        kind,
                        objectId,
                        removeSource: true));

                result.Success.Should().BeFalse();
                CountObjects(sourceSheet, kind).Should().Be(1);
                CountObjects(destinationSheet, kind).Should().Be(0);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(SelectionPaneObjectKind.Chart)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    public async Task CrossSheetCut_AllowsUnlockedObjectOnProtectedSource(
        SelectionPaneObjectKind kind)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sourceSheet = window.Session.ActiveSheet;
                ClearSampleDrawingObjects(sourceSheet);
                var destinationSheet = window.Session.Workbook.AddSheet("UnlockedSourceDestination");
                var sourceAnchor = new CellAddress(sourceSheet.Id, 2, 2);
                var objectId = AddObject(sourceSheet, kind, sourceAnchor);
                SetObjectLocked(sourceSheet, kind, objectId, locked: false);
                window.Session.ExecuteReviewCommand(new ProtectSheetCommand(
                    sourceSheet.Id,
                    password: null,
                    permissions: [SheetProtectionPermission.SelectLockedCells]));

                var result = window.Session.ExecuteReviewCommand(
                    new DuplicateDrawingObjectCommand(
                        sourceSheet.Id,
                        destinationSheet.Id,
                        kind,
                        objectId,
                        removeSource: true));

                result.Success.Should().BeTrue();
                CountObjects(sourceSheet, kind).Should().Be(0);
                CountObjects(destinationSheet, kind).Should().Be(1);
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
    public async Task DrawingObjectContextMenu_CutAndPaste_UsesObjectClipboardRoute()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                ClearSampleDrawingObjects(sheet);
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
                    AnchorOffsetX = 17,
                    AnchorOffsetY = 29,
                };
                sheet.DrawingShapes.Add(shape);
                return shape.Id;
            case SelectionPaneObjectKind.Picture:
                var picture = new PictureModel
                {
                    Name = "SalesPicture",
                    Anchor = anchor,
                    AnchorOffsetX = 31,
                    AnchorOffsetY = 43,
                };
                sheet.Pictures.Add(picture);
                return picture.Id;
            case SelectionPaneObjectKind.TextBox:
                var textBox = new TextBoxModel
                {
                    Name = "SalesTextBox",
                    Anchor = anchor,
                    Text = "Sales",
                    AnchorOffsetX = 47,
                    AnchorOffsetY = 59,
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
    /// two pictures. These tests assert ABSOLUTE object counts and exact id sets, so the sample objects
    /// are dropped here to establish the empty precondition the assertions describe. (Chart is the only
    /// kind the sample has none of, which is why the Chart theory case was the only one that ever
    /// passed.) Freshly added destination sheets start empty and need no such reset.
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

    private static (double X, double Y) ObjectPosition(
        Sheet sheet,
        SelectionPaneObjectKind kind,
        Guid objectId) => kind switch
    {
        SelectionPaneObjectKind.Chart when sheet.Charts.Find(item => item.Id == objectId) is { } chart =>
            (chart.Left, chart.Top),
        SelectionPaneObjectKind.Shape when sheet.DrawingShapes.Find(item => item.Id == objectId) is { } shape =>
            (shape.AnchorOffsetX, shape.AnchorOffsetY),
        SelectionPaneObjectKind.Picture when sheet.Pictures.Find(item => item.Id == objectId) is { } picture =>
            (picture.AnchorOffsetX, picture.AnchorOffsetY),
        SelectionPaneObjectKind.TextBox when sheet.TextBoxes.Find(item => item.Id == objectId) is { } textBox =>
            (textBox.AnchorOffsetX, textBox.AnchorOffsetY),
        _ => throw new InvalidOperationException($"Object {objectId} was not found."),
    };

    private static void SetObjectLocked(
        Sheet sheet,
        SelectionPaneObjectKind kind,
        Guid objectId,
        bool locked)
    {
        switch (kind)
        {
            case SelectionPaneObjectKind.Chart:
                sheet.Charts.Find(item => item.Id == objectId)!.Locked = locked;
                break;
            case SelectionPaneObjectKind.Shape:
                sheet.DrawingShapes.Find(item => item.Id == objectId)!.Locked = locked;
                break;
            case SelectionPaneObjectKind.Picture:
                sheet.Pictures.Find(item => item.Id == objectId)!.Locked = locked;
                break;
            case SelectionPaneObjectKind.TextBox:
                sheet.TextBoxes.Find(item => item.Id == objectId)!.Locked = locked;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

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
