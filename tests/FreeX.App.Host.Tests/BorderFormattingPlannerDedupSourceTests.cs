using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class BorderFormattingPlannerDedupSourceTests
{
    [Theory]
    [InlineData("BorderPickerPlanner.cs")]
    [InlineData("BorderDrawPlanner.cs")]
    [InlineData("FormatCellsMergePlanner.cs")]
    [InlineData("HomeNumberFormatDropdownPlanner.cs")]
    [InlineData("SelectionStyleCommandPlanner.cs")]
    public void WpfHost_DoesNotReintroducePureBorderFormattingPlannerFacades(string fileName)
    {
        var hostPlannerPath = DialogSourceTestSupport.FindHostSourceFile("MainWindow.xaml");
        var hostSourceDirectory = Path.GetDirectoryName(hostPlannerPath)
            ?? throw new DirectoryNotFoundException("Could not locate FreeX.App.Host source directory.");

        File.Exists(Path.Combine(hostSourceDirectory, fileName))
            .Should()
            .BeFalse("WPF should consume shared border/formatting planners directly");
    }

    [Theory]
    [InlineData("BorderDrawPlanner.cs")]
    [InlineData("CellMergePlanner.cs")]
    [InlineData("HomeNumberFormatDropdownPlanner.cs")]
    [InlineData("SelectionStyleCommandPlanner.cs")]
    public void SharedBorderFormattingPlanners_StayFreeOfWpfHostDependencies(string fileName)
    {
        var source = DialogSourceTestSupport.ReadAppServicesSource(fileName);

        source.Should().NotContain("System.Windows");
        source.Should().NotContain("FreeX.App.Host");
        source.Should().NotContain("UiText.Get(");
    }
}
