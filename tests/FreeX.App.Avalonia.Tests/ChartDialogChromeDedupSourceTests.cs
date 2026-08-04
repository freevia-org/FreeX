using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class ChartDialogChromeDedupSourceTests
{
    [Fact]
    public void ChartDialogs_DelegateCompactControlChromeThroughChartFactories()
    {
        var formatSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartFormatDialogs.cs"));
        var remainingSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartRemainingDialogs.cs"));
        var tabsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartTabs.cs"));
        var typeFormatSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartTypeFormatDialogs.cs"));

        formatSource.Should().Contain("using Free.Shared.Shell.Avalonia;");
        formatSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle ChartDialogChromeStyle => new(FormulaBarFontFamily);");
        formatSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, ChartDialogChromeStyle, width, isDefault);");
        formatSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(textBox, ChartDialogChromeStyle);");
        formatSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, ChartDialogChromeStyle);");
        formatSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, ChartDialogChromeStyle);");
        formatSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyRadioButton(radioButton, ChartDialogChromeStyle);");
        formatSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow(controls, margin)");
        formatSource.Should().Contain("private static Button CreateChartButton(");
        formatSource.Should().Contain("private static TextBox CreateChartTextBox(");
        formatSource.Should().Contain("private static ComboBox CreateChartComboBox(");
        formatSource.Should().Contain("private static CheckBox CreateChartCheckBox(");
        formatSource.Should().Contain("private static RadioButton CreateChartRadioButton(");

        remainingSource.Should().Contain("CreateChartRadioButton(");
        remainingSource.Should().Contain("CreateChartTextBox(");
        remainingSource.Should().Contain("CreateChartButton(");
        tabsSource.Should().Contain("CreateChartButton(UiText.Get(addSeriesAction.LabelResourceKey), 92)");
        tabsSource.Should().Contain("CreateChartTextBox(initialRange, 540, UiText.Get(\"ChartLoc_RangePlaceholder\"))");
        tabsSource.Should().Contain("CreateChartComboBox(260, positionChoices)");
        typeFormatSource.Should().Contain("CreateChartTextBox(current.BarGapWidth.ToString(CultureInfo.InvariantCulture), 260)");
        typeFormatSource.Should().Contain("CreateChartComboBox(260, sizeChoices)");
        typeFormatSource.Should().Contain("CreateChartButton(DescribeColor(label, color), 260)");

        foreach (var source in new[] { formatSource, remainingSource, tabsSource, typeFormatSource })
        {
            AssertNoLocalButtonChrome(source);
            AssertNoLocalTextBoxChrome(source, "textBox");
            AssertNoLocalTextBoxChrome(source, "box");
            AssertNoLocalComboBoxChrome(source, "comboBox");
            AssertNoLocalComboBoxChrome(source, "combo");
        }
    }

    private static void AssertNoLocalButtonChrome(string source)
    {
        source.Should().NotContain("button.Height = 24;");
        source.Should().NotContain("button.MinHeight = 24;");
        source.Should().NotContain("button.MaxHeight = 24;");
        source.Should().NotContain("button.Padding = new Thickness(4, 1);");
        source.Should().NotContain("button.BorderBrush = isDefault ? Brush(0, 120, 215) : Brush(112, 112, 112);");
    }

    private static void AssertNoLocalTextBoxChrome(string source, string variableName)
    {
        source.Should().NotContain($"{variableName}.Height = 24;");
        source.Should().NotContain($"{variableName}.MinHeight = 24;");
        source.Should().NotContain($"{variableName}.MaxHeight = 24;");
        source.Should().NotContain($"{variableName}.Padding = new Thickness(4, 1);");
        source.Should().NotContain($"{variableName}.BorderBrush = Brush(130, 130, 130);");
    }

    private static void AssertNoLocalComboBoxChrome(string source, string variableName)
    {
        source.Should().NotContain($"{variableName}.Height = 24;");
        source.Should().NotContain($"{variableName}.MinHeight = 24;");
        source.Should().NotContain($"{variableName}.MaxHeight = 24;");
        source.Should().NotContain($"{variableName}.Padding = new Thickness(5, 0, 4, 0);");
        source.Should().NotContain($"{variableName}.BorderBrush = Brush(130, 130, 130);");
    }

    private static string RepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }
}
