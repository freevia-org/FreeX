using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class OptionsDialogSourceTests
{
    [Fact]
    public void OptionsDialog_ExposesPersistedViewOptions()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "OptionsDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "OptionsDialog.xaml.cs"));

        xaml.Should().Contain("<ListBoxItem Content=\"View\"/>");
        xaml.Should().Contain("x:Name=\"PanelView\"");
        xaml.Should().Contain("x:Name=\"OptShowFormulaBar\"");
        xaml.Should().Contain("x:Name=\"OptFormulaBarExpanded\"");
        source.Should().Contain("OptShowFormulaBar.IsChecked = _opts.ShowFormulaBar");
        source.Should().Contain("OptFormulaBarExpanded.IsChecked = _opts.FormulaBarExpanded");
        source.Should().Contain("ShowFormulaBar     = OptShowFormulaBar.IsChecked == true");
        source.Should().Contain("FormulaBarExpanded = OptFormulaBarExpanded.IsChecked == true");
    }
}
