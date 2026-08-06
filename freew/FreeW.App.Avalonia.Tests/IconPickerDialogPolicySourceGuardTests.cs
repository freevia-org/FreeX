using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class IconPickerDialogPolicySourceGuardTests
{
    [Fact]
    public void AvaloniaIconPickerDelegatesPortableWorkflowAndKeepsDrawingAndCloseBehaviorLocal()
    {
        var source = ReadAvaloniaSource("IconPickerDialog.cs");

        source.Should().Contain("new IconPickerDialogSession(");
        source.Should().Contain("IconPickerCatalog.LoadFromBaseDirectory(");
        source.Should().Contain("_session.ApplyFilter(");
        source.Should().Contain("_session.Select(");
        source.Should().Contain("_session.PlanAccept(");
        source.Should().Contain("SvgIconRasterizer.LoadFileToPaintedBounds(");
        source.Should().Contain("AvaloniaUserMessageDialog.ShowWarningAsync(");
        source.Should().Contain("Close(plan.Selection)");
        source.Should().Contain("private static readonly IconPickerSurfaceSpec Surface = IconPickerDialogPlanner.Surface;");
        source.Should().Contain("AvaloniaCompactDialogChrome.CreateOkCancelRow(");
        source.Should().Contain("IconPickerDialogPlanner.ToolTipFor(entry)");
        source.Should().Contain("AutomationProperties.SetAutomationId(");
        source.Should().NotContain("LoadEntries(");
        source.Should().NotContain("Directory.Enumerate");
        source.Should().NotContain("TitleCase(");
        source.Should().NotContain("IconPickerDialogPlanner.Filter(");
        source.Should().NotContain("private const int ThumbSize");
        source.Should().NotContain("Title = \"Insert Icon\"");
    }

    private static string ReadAvaloniaSource(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", fileName));
    }
}
