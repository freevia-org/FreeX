using System.Reflection;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using FluentAssertions;

using FreeX.App.Presentation.DrawingInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Headless proof that the Avalonia Picture Format route enters live crop mode, renders the crop
/// adorner, and commits through the same undoable crop command used by WPF.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaPictureCropRuntimeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task PictureCropRibbonCommand_EntersCropModeRendersAdornerAndUsesUndoableCommand()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("PictureCropFixture");
            window.Session.SelectSheet(sheet.Id);

            var picture = new PictureModel
            {
                Anchor = new CellAddress(sheet.Id, 1, 1),
                Kind = PictureKind.Image,
                Width = 200,
                Height = 100,
                ImageBytes = [1, 2, 3],
                ContentType = "image/png",
            };
            sheet.Pictures.Add(picture);

            var bounds = new DrawingObjectBounds(
                SelectionPaneObjectKind.Picture,
                picture.Id,
                "Picture",
                1,
                1,
                0,
                0,
                picture.Width,
                picture.Height,
                PictureKind: PictureKind.Image,
                ImageBytes: picture.ImageBytes,
                ImageContentType: picture.ContentType,
                PictureCells: []);

            InvokePrivate(window, "SelectDrawingObject", bounds);
            var commands = (IReadOnlyDictionary<string, Action>)InvokePrivate(
                window, "BuildContextualTabCommands")!;

            commands.Should().ContainKey("Crop Picture");
            commands["Crop Picture"]();

            GetPrivateField<bool>(window, "_isPictureCropMode").Should().BeTrue();

            var renderPlan = new DrawingObjectRenderPlan(
                bounds,
                DrawingObjectRenderPrimitiveKind.Image);
            var container = (Grid)InvokePrivate(
                window,
                "CreateSelectableDrawingObjectVisual",
                renderPlan,
                picture.Width,
                picture.Height)!;
            var cropAdorner = container.Children[1].Should().BeOfType<Canvas>().Subject;
            cropAdorner.Children.Should().HaveCount(9, "the crop border and eight shared planner handles must be rendered");

            var crop = new PictureCropRatios(0.1, 0.05, 0.2, 0.15);
            ((bool)InvokePrivate(window, "ApplyPictureCrop", picture.Id, crop)!).Should().BeTrue();
            picture.CropLeft.Should().Be(crop.Left);
            picture.CropTop.Should().Be(crop.Top);
            picture.CropRight.Should().Be(crop.Right);
            picture.CropBottom.Should().Be(crop.Bottom);
            window.Session.CanUndo.Should().BeTrue();

            window.Session.UndoLastEdit().Success.Should().BeTrue();
            picture.CropLeft.Should().Be(0);
            picture.CropTop.Should().Be(0);
            picture.CropRight.Should().Be(0);
            picture.CropBottom.Should().Be(0);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PictureContextMenuCropCommand_EntersLiveCropMode()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("PictureContextMenuFixture");
            window.Session.SelectSheet(sheet.Id);

            var picture = new PictureModel
            {
                Anchor = new CellAddress(sheet.Id, 1, 1),
                Kind = PictureKind.Image,
                Width = 200,
                Height = 100,
                ImageBytes = [1, 2, 3],
                ContentType = "image/png",
            };
            sheet.Pictures.Add(picture);

            var bounds = new DrawingObjectBounds(
                SelectionPaneObjectKind.Picture,
                picture.Id,
                "Picture",
                1,
                1,
                0,
                0,
                picture.Width,
                picture.Height,
                PictureKind: PictureKind.Image,
                ImageBytes: picture.ImageBytes,
                ImageContentType: picture.ContentType,
                PictureCells: []);

            InvokePrivate(window, "SelectDrawingObject", bounds);
            InvokePrivate(
                window,
                "DispatchDrawingObjectContextMenuCommand",
                new Free.Shared.Ribbon.RibbonCommandId("CropPicture"));

            GetPrivateField<bool>(window, "_isPictureCropMode").Should().BeTrue();
            window.Session.CanUndo.Should().BeFalse("entering crop mode must not mutate the picture until a drag is committed");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    private static object? InvokePrivate(MainWindow window, string methodName, params object[] args) =>
        typeof(MainWindow)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(window, args);

    private static T GetPrivateField<T>(MainWindow window, string fieldName) =>
        (T)typeof(MainWindow)
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;
}
