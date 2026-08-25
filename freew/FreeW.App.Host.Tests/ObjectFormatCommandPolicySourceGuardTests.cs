using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class ObjectFormatCommandPolicySourceGuardTests
{
    [Fact]
    public void WpfRibbonCommands_RoutePictureAndShapeObjectFormatPolicyThroughPresentationPlanner()
    {
        var source = ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var profile = ReadSource("freew", "FreeW.App.Presentation", "Ribbon", "FreeWRibbonEditorExecutionProfile.cs");

        source.Should().Contain("using FreeW.App.Presentation.Ribbon;");
        source.Should().Contain("FreeWRibbonEditorExecutionProfile.RegisterFloating(");
        source.Should().Contain("CreateFloatingExecutionPorts(editor,");
        profile.Should().Contain("foreach (var target in ObjectFormatCommandPlanner.Targets)");
        profile.Should().Contain("ObjectFormatCommandPlanner.WrapCommands(target)");
        profile.Should().Contain("ObjectFormatCommandPlanner.TransformCommands(target)");
        profile.Should().Contain("ObjectFormatCommandPlanner.ZOrderCommands(target)");
        profile.Should().Contain("ObjectFormatCommandPlanner.SizeCommands(target)");
        source.Should().NotContain("ObjectFormatCommandPlanner.WrapCommands(");
        source.Should().NotContain("ObjectFormatCommandPlanner.TransformCommands(");
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
        var profile = ReadSource("freew", "FreeW.App.Presentation", "Ribbon", "FreeWRibbonEditorExecutionProfile.cs");

        source.Should().Contain("using FreeW.App.Presentation.Ribbon;");
        source.Should().Contain("FreeWRibbonEditorExecutionProfile.RegisterFloating(");
        source.Should().Contain("CreateFloatingExecutionPorts(editor,");
        profile.Should().Contain("ObjectFormatCommandPlanner.WrapDropdownCommandId(target)");
        profile.Should().Contain("ObjectFormatCommandPlanner.TransformDropdownCommandId(target)");
        profile.Should().Contain("ObjectFormatCommandPlanner.WrapCommands(target)");
        profile.Should().Contain("ObjectFormatCommandPlanner.TransformCommands(target)");
        profile.Should().Contain("ObjectFormatCommandPlanner.ZOrderCommands(target)");
        profile.Should().Contain("ObjectFormatCommandPlanner.SizeCommands(target)");
        profile.Should().Contain("ObjectFormatCommandPlanner.TryParseSizePoints(context.SelectedValue, out var points)");
        source.Should().NotContain("ObjectFormatCommandPlanner.WrapCommands(");
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
        var profile = ReadSource("freew", "FreeW.App.Presentation", "Ribbon", "FreeWRibbonEditorExecutionProfile.cs");

        wpfEditor.Should().Contain("public bool RotateSelectedFloating(double angleDeg)");
        wpfEditor.Should().Contain("ObjectEdits.RotateBy(target, angleDeg)");
        wpfEditor.Should().Contain("ObjectEdits.Flip(target, horizontal)");
        wpfEditor.Should().Contain("ObjectEdits.ChangeZOrder(");
        wpfEditor.Should().NotContain("new SetDrawingGroupChildRotationCommand(");
        wpfEditor.Should().NotContain("new ChangeDrawingGroupChildZOrderCommand(");
        wpfRibbon.Should().Contain("editor.RotateSelectedFloating(command.RotationDeltaDegrees)");
        wpfRibbon.Should().Contain("editor.FlipSelectedFloating(horizontal: true)");
        wpfRibbon.Should().Contain("editor.ChangeSelectedFloatingZOrder(operation)");
        avaloniaEditor.Should().Contain("ObjectEdits.RotateBy(target, angleDeg)");
        avaloniaEditor.Should().Contain("ObjectEdits.Flip(target, horizontal)");
        avaloniaEditor.Should().Contain("ObjectEdits.ChangeZOrder(");
        avaloniaEditor.Should().NotContain("new SetDrawingGroupChildRotationCommand(");
        avaloniaEditor.Should().NotContain("new ChangeDrawingGroupChildZOrderCommand(");
        avaloniaRibbon.Should().Contain("editor.ChangeSelectedFloatingZOrder(");
        avaloniaRibbon.Should().Contain("target == ObjectFormatTarget.Picture ? \"Image\" : \"Shape\"");
        avaloniaRibbon.Should().Contain("editor.RotateSelectedFloating(command.RotationDeltaDegrees)");
        avaloniaRibbon.Should().Contain("editor.FlipSelectedFloating(horizontal: true)");
        profile.Should().Contain("ports.ApplyTransform(target, captured)");
        profile.Should().Contain("ports.ApplyZOrder(target, captured.Operation)");
    }

    [Fact]
    public void WpfAndAvaloniaNestedShapeTypeAndAltTextRoutes_PreserveTheChildPath()
    {
        var wpfEditor = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avaloniaEditor = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var avaloniaMain = ReadSource("freew", "FreeW.App.Avalonia", "MainWindow.cs");

        wpfEditor.Should().Contain("ObjectEdits.SetShapeKind(");
        wpfEditor.Should().Contain("ObjectEdits.SetShapeAltText(");
        wpfEditor.Should().Contain("ObjectTarget(nested.BlockIndex, nested.RunIndex, nested.ChildPath)");
        avaloniaEditor.Should().Contain("ObjectEdits.SetShapeKind(");
        avaloniaEditor.Should().Contain("ObjectEdits.SetAltText(");
        avaloniaEditor.Should().Contain("ObjectTarget(nested.BlockIndex, nested.RunIndex, nested.ChildPath)");
        avaloniaMain.Should().Contain("selectedShape is null && selectedWordArt is null");
        avaloniaMain.Should().NotContain("SelectedFloatingInfo?.Kind is not (\"Shape\" or \"WordArt\")");
        wpfEditor.Should().Contain("ObjectEdits.SetShapeSize(");
        avaloniaEditor.Should().Contain("public void SetSelectedShapeSize(double widthPt, double heightPt)");
        avaloniaMain.Should().Contain("_editor.SetSelectedShapeSize(result.Width, result.Height)");
        wpfEditor.Should().Contain("public void SetSelectedShapePosition(double horizontalOffsetPt, double verticalOffsetPt,");
        wpfEditor.Should().Contain("ObjectEdits.SetShapePosition(");
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
