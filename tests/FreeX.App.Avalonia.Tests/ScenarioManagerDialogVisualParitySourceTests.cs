using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class ScenarioManagerDialogVisualParitySourceTests
{
    [Fact]
    public void ScenarioManagerDialog_UsesWpfBodyCompositionAndSharedCompactChrome()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyWindow(dialog, dialogChrome);");
        source.Should().Contain("scenarioList.ItemTemplate = new FuncDataTemplate<ScenarioManagerDialogScenarioItem>");
        source.Should().Contain("Text = item.Choice.Name,");
        source.Should().Contain("public override string ToString() => Choice.Name;");
        source.Should().Contain("ScenarioManagerDialogLayout.FieldBottomMargin");
        source.Should().Contain("ScenarioManagerDialogLayout.ScenarioListHeaderBottomMargin");
        source.Should().Contain("ScenarioManagerDialogLayout.LockedCheckBoxBottomMargin");
        source.Should().Contain("ScenarioManagerDialogLayout.HiddenCheckBoxBottomMargin");
        source.Should().Contain("ScenarioManagerDialogChromeStyle");
        source.Should().Contain("ControlHeight = 22");
        source.Should().Contain("TextBoxHeight = 22");
        source.Should().Contain("ButtonHeight = 22");
        source.Should().Contain("ButtonPadding = new Thickness(8, 1)");
        source.Should().Contain("ScenarioManagerDialogLayout.CloseRowTopMargin");
        source.Should().Contain("RowDefinitions = new RowDefinitions(\"Auto,Auto,Auto,Auto,Auto,Auto\")");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions($\"{ScenarioManagerDialogLayout.FieldLabelColumnWidth},*\")");
        source.Should().Contain("control.MinWidth = 0;");
        source.Should().Contain("control.HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch;");
        source.Should().Contain("AddScenarioManagerField(fields, 2");
        source.Should().Contain("AddScenarioManagerCheckBox(fields, 4, preventChangesBox);");
        source.Should().Contain("Margin = new Thickness(10, 20, 0, 0)");
        source.Should().Contain("RowDefinitions = new RowDefinitions(\"*,Auto,Auto\")");
        source.Should().NotContain("dialog.Content = new ScrollViewer");
    }

    [Fact]
    public void ScenarioManagerRangePickers_WrapGridFieldsAndRemainSharedSessionBacked()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ScenarioManagerRangePickers.cs"));

        source.Should().Contain("target?.Parent is not Panel field");
        source.Should().Contain("if (field is Grid parentGrid)");
        source.Should().Contain("ScenarioManagerChangingCellsPickerButton");
        source.Should().Contain("ScenarioManagerResultCellsPickerButton");
        source.Should().Contain("owner.AttachDialogRangePicker(dialog, picker, target, targetId);");
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
