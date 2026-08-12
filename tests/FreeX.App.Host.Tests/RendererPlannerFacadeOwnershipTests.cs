using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class RendererPlannerFacadeOwnershipTests
{
    [Fact]
    public void WpfDialogRenderer_DoesNotOwnSharedPlannerFacades()
    {
        var root = WorkspaceFileLocator.FindWorkspaceRoot();
        var host = Path.Combine(root, "src", "FreeX.App.Host");

        File.Exists(Path.Combine(host, "AutoFilterDialog.Criteria.cs")).Should().BeFalse();
        File.Exists(Path.Combine(host, "SymbolPickerDialog.Catalog.cs")).Should().BeFalse();
        File.Exists(Path.Combine(host, "GoalSeekInputParser.cs")).Should().BeFalse();

        var dataTable = File.ReadAllText(Path.Combine(host, "DataTableDialog.cs"));
        var cellShift = File.ReadAllText(Path.Combine(host, "CellShiftDialog.cs"));
        var colorPicker = File.ReadAllText(Path.Combine(host, "ColorPickerDialog.xaml.cs"));
        var goalSeek = File.ReadAllText(Path.Combine(host, "GoalSeekDialog.xaml.cs"));

        dataTable.Should().Contain("DataTableInputParser.TryParse(");
        dataTable.Should().NotContain("public static bool TryParse(");
        cellShift.Should().Contain("CellShiftDialogPlanner.GetAvailableChoices");
        cellShift.Should().NotContain("public static IReadOnlyList<CellShiftDialogOption>");
        colorPicker.Should().Contain("ColorInputParser.TryParseColorText");
        colorPicker.Should().Contain("CellColorPalettePlanner.BuildThemePalette");
        colorPicker.Should().NotContain("public static IReadOnlyList<CellColorSwatch>");
        goalSeek.Should().Contain("GoalSeekRequestParser.TryParse(");
        goalSeek.Should().Contain("GoalSeekStatusDialogPlanner.DescribeValidationError(");
    }
}
