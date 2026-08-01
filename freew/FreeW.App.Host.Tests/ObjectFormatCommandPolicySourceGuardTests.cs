using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class ObjectFormatCommandPolicySourceGuardTests
{
    [Fact]
    public void WpfRibbonCommands_RoutePictureAndShapeObjectFormatPolicyThroughPresentationPlanner()
    {
        var source = ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");

        source.Should().Contain("using FreeW.App.Presentation.Ribbon;");
        source.Should().Contain("ObjectFormatCommandPlanner.WrapCommands(ObjectFormatTarget.Picture)");
        source.Should().Contain("ObjectFormatCommandPlanner.TransformCommands(ObjectFormatTarget.Picture)");
        source.Should().Contain("ObjectFormatCommandPlanner.ZOrderCommands(ObjectFormatTarget.Picture)");
        source.Should().Contain("ObjectFormatCommandPlanner.ZOrderCommands(ObjectFormatTarget.Shape)");
        source.Should().Contain("ObjectFormatCommandPlanner.WrapCommands(ObjectFormatTarget.Shape)");
        source.Should().Contain("ObjectFormatCommandPlanner.TransformCommands(ObjectFormatTarget.Shape)");
        source.Should().NotContain("new ImageWrapCommand(editor, ImageWrapping.");
        source.Should().NotContain("new ShapeWrapCommand(editor, ImageWrapping.");
        source.Should().NotContain("new ImageZOrderCommand(editor, ZOrderOperation.");
        source.Should().NotContain("new ImageRotateStepCommand(editor, +90)");
        source.Should().NotContain("new ShapeRotateStepCommand(editor, +90)");
    }

    [Fact]
    public void AvaloniaRibbonCommands_RouteFloatingObjectFormatPolicyThroughPresentationPlanner()
    {
        var source = ReadSource("freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");

        source.Should().Contain("using FreeW.App.Presentation.Ribbon;");
        source.Should().Contain("foreach (var target in ObjectFormatCommandPlanner.Targets)");
        source.Should().Contain("ObjectFormatCommandPlanner.WrapDropdownCommandId(target)");
        source.Should().Contain("ObjectFormatCommandPlanner.TransformDropdownCommandId(target)");
        source.Should().Contain("ObjectFormatCommandPlanner.WrapCommands(target)");
        source.Should().Contain("ObjectFormatCommandPlanner.TransformCommands(target)");
        source.Should().Contain("ObjectFormatCommandPlanner.ZOrderCommands(target)");
        source.Should().Contain("ObjectFormatCommandPlanner.SizeCommands(target)");
        source.Should().Contain("ObjectFormatCommandPlanner.TryParseSizePoints(value, out var pt)");
        source.Should().NotContain("foreach (var prefix in new[] { \"image\", \"shape\" })");
        source.Should().NotContain("SetFloatingWrap(ImageWrapping.");
        source.Should().NotContain("double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var pt) && pt > 0");
    }

    [Fact]
    public void WpfAndAvaloniaTransformRoutes_KeepGroupedChildTransformsOnTheSharedCommand()
    {
        var wpfEditor = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var wpfRibbon = ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avaloniaEditor = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var avaloniaRibbon = ReadSource("freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");

        wpfEditor.Should().Contain("public bool RotateSelectedFloating(double angleDeg)");
        wpfEditor.Should().Contain("new SetDrawingGroupChildRotationCommand(");
        wpfEditor.Should().Contain("new ChangeDrawingGroupChildZOrderCommand(");
        wpfEditor.Should().Contain("RestoreSelectedFloatingGroupChildPath(selectedChild)");
        wpfEditor.Should().Contain("SelectedFloatingGroupChildTransform()");
        wpfRibbon.Should().Contain("new FloatingTransformCommand(editor, command)");
        avaloniaEditor.Should().Contain("new SetDrawingGroupChildRotationCommand(");
        avaloniaEditor.Should().Contain("new ChangeDrawingGroupChildZOrderCommand(");
        avaloniaRibbon.Should().Contain("editor.ChangeSelectedFloatingZOrder(operation, requiredKind)");
        avaloniaRibbon.Should().Contain("editor.RotateSelectedFloating(command.RotationDeltaDegrees)");
        avaloniaRibbon.Should().Contain("editor.FlipSelectedFloating(horizontal: true)");
    }

    [Fact]
    public void WpfAndAvaloniaNestedShapeTypeAndAltTextRoutes_PreserveTheChildPath()
    {
        var wpfEditor = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avaloniaEditor = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var avaloniaMain = ReadSource("freew", "FreeW.App.Avalonia", "MainWindow.cs");

        wpfEditor.Should().Contain("new SetShapeKindCommand(");
        wpfEditor.Should().Contain("new SetShapeAltTextCommand(");
        wpfEditor.Should().Contain("nested.BlockIndex, nested.RunIndex, kind, nested.ChildPath");
        wpfEditor.Should().Contain("nested.BlockIndex, nested.RunIndex, normalized, nested.ChildPath");
        avaloniaEditor.Should().Contain("nested.BlockIndex, nested.RunIndex, kind, nested.ChildPath");
        avaloniaEditor.Should().Contain("nested.BlockIndex, nested.RunIndex, normalized, nested.ChildPath");
        avaloniaMain.Should().Contain("selectedShape is null && selectedWordArt is null");
        avaloniaMain.Should().NotContain("SelectedFloatingInfo?.Kind is not (\"Shape\" or \"WordArt\")");
        wpfEditor.Should().Contain("nested.BlockIndex, nested.RunIndex, nested.ChildPath, widthPt, heightPt");
        avaloniaEditor.Should().Contain("public void SetSelectedShapeSize(double widthPt, double heightPt)");
        avaloniaMain.Should().Contain("_editor.SetSelectedShapeSize(result.Width, result.Height)");
        wpfEditor.Should().Contain("public void SetSelectedShapePosition(double horizontalOffsetPt, double verticalOffsetPt,");
        wpfEditor.Should().Contain("nested.BlockIndex, nested.RunIndex, nested.ChildPath,");
        avaloniaEditor.Should().Contain("public void SetSelectedShapePosition(double hOffsetPt, double vOffsetPt,");
        avaloniaMain.Should().Contain("_editor.GetSelectedShapePosition()");
        avaloniaMain.Should().Contain("_editor.SetSelectedShapePosition(");
        avaloniaMain.Should().Contain("position.IsGroupLocal");
    }

    private static string ReadSource(params string[] relativePath)
    {
        var path = relativePath.Aggregate(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), Path.Combine);
        return File.ReadAllText(path);
    }

}
