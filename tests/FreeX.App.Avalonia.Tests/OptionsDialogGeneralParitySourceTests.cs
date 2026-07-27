using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class OptionsDialogGeneralParitySourceTests
{
    [Fact]
    public void GeneralOptions_UsesWpfMetricsAndExactContentStates()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));
        var wpf = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "OptionsDialog.xaml"));

        source.Should().Contain("generalPanel.Spacing = 0;");
        source.Should().Contain("OptionsDialogPlanner.GeneralContentWidth");
        source.Should().Contain("OptionsDialogPlanner.GeneralLabelWidth");
        source.Should().Contain("OptionsDialogPlanner.GeneralSmallFieldWidth");
        source.Should().Contain("OptionsDialogPlanner.GeneralFieldSpacing");
        source.Should().Contain("IsEditable = true");
        source.Should().Contain("OptionsText(\"Options_ShowFeatureDescriptionsInScreenTips\")");
        source.Should().Contain("IsChecked = current.CollapseRibbonAutomatically");
        source.Should().Contain("collapseRibbonAutomatically: collapseRibbonBox.IsChecked == true");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions($\"{OptionsDialogPlanner.CategoryColumnWidth},*\")");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions($\"{labelWidth},*\")");

        wpf.Should().Contain("Options_ShowFeatureDescriptionsInScreenTips");
        wpf.Should().Contain("ColumnDefinition Width=\"230\"");
        wpf.Should().Contain("Height=\"24\"");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");

        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}
