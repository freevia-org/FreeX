using Avalonia.Headless;
using Avalonia.Input;
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
}
