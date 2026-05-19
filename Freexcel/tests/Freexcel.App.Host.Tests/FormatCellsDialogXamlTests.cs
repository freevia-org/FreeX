using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class FormatCellsDialogXamlTests
{
    [Fact]
    public void FormatCellsDialog_ExposesShrinkToFitAndMapsItIntoStyleDiff()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml.cs"));

        xaml.Should().Contain("x:Name=\"DlgShrinkToFitCheck\"");
        xaml.Should().Contain("Content=\"Shrink to fit\"");
        xaml.Should().Contain("x:Name=\"DlgDoubleUnderlineCheck\"");
        xaml.Should().Contain("Content=\"Double underline\"");
        source.Should().Contain("DlgShrinkToFitCheck.IsChecked = s.ShrinkToFit;");
        source.Should().Contain("DlgDoubleUnderlineCheck.IsChecked = s.DoubleUnderline;");
        source.Should().Contain("DoubleUnderline: DlgDoubleUnderlineCheck.IsChecked");
        source.Should().Contain("ShrinkToFit:   DlgShrinkToFitCheck.IsChecked");
        source.Should().Contain("Enum.GetNames(typeof(CellHAlign))");
        source.Should().Contain("Enum.GetNames(typeof(CellVAlign))");
    }
}
