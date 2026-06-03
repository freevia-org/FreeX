using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class WorkbookThemeDialogXamlTests
{
    [Fact]
    public void Dialog_ExposesThemePresetButtonsBackedByWorkflow()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("WorkbookThemeDialog.xaml");
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WorkbookThemeDialog.xaml.cs"));

        xaml.Should().Contain("x:Name=\"OfficePresetButton\"");
        xaml.Should().Contain("x:Name=\"ColorfulPresetButton\"");
        xaml.Should().Contain("x:Name=\"GrayscalePresetButton\"");
        xaml.Should().Contain("Click=\"OfficePresetButton_Click\"");
        xaml.Should().Contain("Click=\"ColorfulPresetButton_Click\"");
        xaml.Should().Contain("Click=\"GrayscalePresetButton_Click\"");

        source.Should().Contain("OfficePresetButton_Click");
        source.Should().Contain("LoadTheme(WorkbookTheme.Office)");
        source.Should().Contain("ColorfulPresetButton_Click");
        source.Should().Contain("LoadTheme(WorkbookThemeWorkflow.CreateColorfulTheme())");
        source.Should().Contain("GrayscalePresetButton_Click");
        source.Should().Contain("LoadTheme(WorkbookThemeWorkflow.CreateGrayscaleTheme())");
    }
}
