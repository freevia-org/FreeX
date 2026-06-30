using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaCompactDialogChromeSourceTests
{
    [Fact]
    public void PivotOptions_DelegatesCompactControlChromeToSharedHelper()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotOptions.cs"));

        source.Should().Contain("using Free.Shared.Shell.Avalonia;");
        source.Should().Contain("private static AvaloniaCompactDialogChromeStyle PivotDialogChromeStyle => new(FormulaBarFontFamily);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, PivotDialogChromeStyle, minWidth, isDefault);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(textBox, PivotDialogChromeStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, PivotDialogChromeStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel]);");

        source.Should().NotContain("button.Height = 24;");
        source.Should().NotContain("textBox.Height = 24;");
        source.Should().NotContain("comboBox.Height = 24;");
        source.Should().NotContain("button.BorderBrush = isDefault ? Brush(0, 120, 215) : Brush(112, 112, 112);");
        source.Should().NotContain("textBox.BorderBrush = Brush(130, 130, 130);");
        source.Should().NotContain("comboBox.BorderBrush = Brush(130, 130, 130);");
    }

    [Fact]
    public void TableDesignDialogs_DelegateCompactControlChromeToSharedHelper()
    {
        var tableNameSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.TableName.cs"));
        var tableResizeSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.TableResize.cs"));

        tableNameSource.Should().Contain("using Free.Shared.Shell.Avalonia;");
        tableNameSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle TableNameDialogChromeStyle => new(FormulaBarFontFamily);");
        tableNameSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, TableNameDialogChromeStyle, minWidth, isDefault);");
        tableNameSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(textBox, TableNameDialogChromeStyle);");
        tableNameSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0))");

        tableResizeSource.Should().Contain("using Free.Shared.Shell.Avalonia;");
        tableResizeSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle TableResizeDialogChromeStyle => new(FormulaBarFontFamily);");
        tableResizeSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, TableResizeDialogChromeStyle, minWidth, isDefault);");
        tableResizeSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(textBox, TableResizeDialogChromeStyle);");
        tableResizeSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0))");

        foreach (var source in new[] { tableNameSource, tableResizeSource })
        {
            source.Should().NotContain("button.Height = 24;");
            source.Should().NotContain("textBox.Height = 24;");
            source.Should().NotContain("button.BorderBrush = isDefault ? Brush(0, 120, 215) : Brush(112, 112, 112);");
            source.Should().NotContain("textBox.BorderBrush = Brush(130, 130, 130);");
            source.Should().NotContain("HorizontalAlignment = AvaloniaHorizontalAlignment.Right");
            source.Should().NotContain("Spacing = 8,");
        }
    }

    [Fact]
    public void DefinedNamesDialogs_DelegateCompactControlChromeToSharedHelper()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.DefinedNames.cs"));

        source.Should().Contain("using Free.Shared.Shell.Avalonia;");
        source.Should().Contain("private static AvaloniaCompactDialogChromeStyle NamesDialogChromeStyle => new(FormulaBarFontFamily);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, NamesDialogChromeStyle, minWidth, isDefault);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(textBox, NamesDialogChromeStyle, fixedHeight);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, NamesDialogChromeStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyListBox(listBox, NamesDialogChromeStyle);");

        source.Should().NotContain("button.Height = 24;");
        source.Should().NotContain("textBox.Height = 24;");
        source.Should().NotContain("comboBox.Height = 24;");
        source.Should().NotContain("button.BorderBrush = isDefault ? Brush(0, 120, 215) : Brush(112, 112, 112);");
        source.Should().NotContain("textBox.BorderBrush = Brush(130, 130, 130);");
        source.Should().NotContain("comboBox.BorderBrush = Brush(130, 130, 130);");
        source.Should().NotContain("new Style(x => x.OfType<ListBoxItem>())");
        source.Should().NotContain("new Setter(Layoutable.MinHeightProperty, 24.0)");
    }

    [Fact]
    public void SharedCompactChrome_CarriesTheExistingDialogMetrics()
    {
        var source = File.ReadAllText(RepoFile(
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaCompactDialogChrome.cs"));

        source.Should().Contain("public sealed record AvaloniaCompactDialogChromeStyle(FontFamily FontFamily)");
        source.Should().Contain("public double ControlHeight { get; init; } = 24;");
        source.Should().Contain("public double FontSize { get; init; } = 12;");
        source.Should().Contain("public Thickness ButtonPadding { get; init; } = new(4, 1);");
        source.Should().Contain("public Thickness TextBoxPadding { get; init; } = new(4, 1);");
        source.Should().Contain("public Thickness ComboBoxPadding { get; init; } = new(5, 0, 4, 0);");
        source.Should().Contain("public Thickness ListBoxItemPadding { get; init; } = new(4, 1);");
        source.Should().Contain("public double ListBoxItemMinHeight { get; init; } = 24;");
        source.Should().Contain("Color.FromRgb(0, 120, 215)");
        source.Should().Contain("Color.FromRgb(112, 112, 112)");
        source.Should().Contain("Color.FromRgb(130, 130, 130)");
        source.Should().Contain("button.Height = style.ControlHeight;");
        source.Should().Contain("button.MinHeight = style.ControlHeight;");
        source.Should().Contain("button.MaxHeight = style.ControlHeight;");
        source.Should().Contain("button.Background = Brushes.White;");
        source.Should().Contain("button.BorderBrush = isDefault ? DefaultButtonBorderBrush : ButtonBorderBrush;");
        source.Should().Contain("if (fixedHeight)");
        source.Should().Contain("textBox.Padding = style.TextBoxPadding;");
        source.Should().Contain("comboBox.Padding = style.ComboBoxPadding;");
        source.Should().Contain("public static void ApplyCheckBox(");
        source.Should().Contain("public static void ApplyRadioButton(");
        source.Should().Contain("public static void ApplyListBox(");
        source.Should().Contain("new Setter(Layoutable.MinHeightProperty, style.ListBoxItemMinHeight)");
        source.Should().Contain("public static StackPanel CreateActionRow(");
    }

    [Fact]
    public void PageLayoutAndPageBreakDialogs_DelegateCompactControlChromeToSharedHelper()
    {
        var pageLayoutSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PageLayout.cs"));
        var pageBreakSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PageBreakActions.cs"));

        pageLayoutSource.Should().Contain("using Free.Shared.Shell.Avalonia;");
        pageLayoutSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle PageLayoutDialogChromeStyle => new(FormulaBarFontFamily);");
        pageLayoutSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, PageLayoutDialogChromeStyle, minWidth, isDefault);");
        pageLayoutSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(textBox, PageLayoutDialogChromeStyle);");
        pageLayoutSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, PageLayoutDialogChromeStyle);");
        pageLayoutSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, PageLayoutDialogChromeStyle);");
        pageLayoutSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyRadioButton(radioButton, PageLayoutDialogChromeStyle);");

        pageBreakSource.Should().Contain("using Free.Shared.Shell.Avalonia;");
        pageBreakSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, PageLayoutDialogChromeStyle, minWidth, isDefault);");

        pageLayoutSource.Should().NotContain("button.Height = 24;");
        pageLayoutSource.Should().NotContain("textBox.Height = 24;");
        pageLayoutSource.Should().NotContain("comboBox.Height = 24;");
        pageBreakSource.Should().NotContain("button.Height = 24;");
        pageBreakSource.Should().NotContain("button.BorderBrush = isDefault ? Brush(0, 120, 215) : Brush(112, 112, 112);");
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
