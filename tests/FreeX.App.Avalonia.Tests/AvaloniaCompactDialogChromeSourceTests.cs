using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaCompactDialogChromeSourceTests
{
    [Fact]
    public void PivotOptions_DelegatesCompactControlChromeToSharedHelper()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotOptions.cs"));

        source.Should().Contain("using Free.Shared.Shell.Avalonia;");
        source.Should().Contain("private static AvaloniaCompactDialogChromeStyle PivotDialogChromeStyle => new(FormulaBarFontFamily)");
        source.Should().Contain("ControlHeight = 22,");
        source.Should().Contain("ButtonHeight = 20,");
        source.Should().Contain("ButtonPadding = new Thickness(12, 1),");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, PivotDialogChromeStyle, minWidth, isDefault);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(textBox, PivotDialogChromeStyle, fixedHeight);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, PivotDialogChromeStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 0, 0, 37));");
        source.Should().Contain("Width = PivotOptionsPlanner.DialogWidth,");
        source.Should().Contain("MinHeight = PivotOptionsPlanner.DialogMinHeight,");
        source.Should().Contain("new Border { Height = PivotOptionsPlanner.LayoutAndFormatAvaloniaSpacerHeight }");

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
    public void InsertFunctionDialogs_DelegateCompactControlChromeToSharedHelper()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.InsertFunction.cs"));

        source.Should().Contain("using Free.Shared.Shell.Avalonia;");
        source.Should().Contain("private static AvaloniaCompactDialogChromeStyle InsertFunctionDialogChromeStyle => new(FormulaBarFontFamily);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, InsertFunctionDialogChromeStyle, minWidth, isDefault);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(textBox, InsertFunctionDialogChromeStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, InsertFunctionDialogChromeStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyListBox(listBox, InsertFunctionDialogChromeStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow(");

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
        source.Should().Contain("public double ControlHeight { get; init; } = CompactDialogVisualTokens.ControlHeight;");
        source.Should().Contain("public double? TextBoxHeight { get; init; }");
        source.Should().Contain("public double? ComboBoxHeight { get; init; }");
        source.Should().Contain("public double? TabHeight { get; init; }");
        source.Should().Contain("public double ButtonHeight { get; init; } = CompactDialogVisualTokens.ButtonHeight;");
        source.Should().Contain("public double ButtonMinWidth { get; init; } = CompactDialogVisualTokens.ButtonMinWidth;");
        source.Should().Contain("public double FontSize { get; init; } = CompactDialogVisualTokens.FontSize;");
        source.Should().Contain("public IBrush? FocusedInputBorderBrush { get; init; }");
        source.Should().Contain("public IBrush? ButtonBorderBrush { get; init; }");
        source.Should().Contain("public IBrush? DialogTabPaneBorderBrush { get; init; }");
        source.Should().Contain("public bool RemoveFocusAdorner { get; init; }");
        source.Should().Contain("CompactDialogVisualTokens.ButtonPaddingHorizontal");
        source.Should().Contain("CompactDialogVisualTokens.ButtonPaddingVertical");
        source.Should().Contain("CompactDialogVisualTokens.TextBoxPaddingHorizontal");
        source.Should().Contain("CompactDialogVisualTokens.TextBoxPaddingVertical");
        source.Should().Contain("CompactDialogVisualTokens.ComboBoxPaddingHorizontal");
        source.Should().Contain("CompactDialogVisualTokens.ComboBoxPaddingVertical");
        source.Should().Contain("CompactDialogVisualTokens.TogglePaddingLeft");
        source.Should().Contain("CompactDialogVisualTokens.LabelPadding");
        source.Should().Contain("CompactDialogVisualTokens.GroupBoxMarginVertical");
        source.Should().Contain("CompactDialogVisualTokens.GroupBoxPaddingHorizontal");
        source.Should().Contain("CompactDialogVisualTokens.GroupBoxPaddingVertical");
        source.Should().Contain("public Thickness ListBoxItemPadding { get; init; } = new(4, 1);");
        source.Should().Contain("public double ListBoxItemMinHeight { get; init; } = CompactDialogVisualTokens.ControlHeight;");
        source.Should().Contain("public CornerRadius ButtonCornerRadius { get; init; } = new(CompactDialogVisualTokens.ButtonCornerRadius);");
        source.Should().Contain("public IBrush? ButtonHoverBackgroundBrush { get; init; }");
        source.Should().Contain("public IBrush? ButtonPressedBackgroundBrush { get; init; }");
        source.Should().Contain("public IBrush? ButtonAccentBrush { get; init; }");
        source.Should().Contain("ThemeBrush(\"ThemeAccentBrush\", ButtonAccentBrush)");
        source.Should().Contain("Color.FromRgb(200, 200, 200)");
        source.Should().Contain("Color.FromRgb(183, 188, 194)");
        source.Should().Contain("button.Height = style.ButtonHeight;");
        source.Should().Contain("button.IsSet(Layoutable.MinWidthProperty) ? button.MinWidth : style.ButtonMinWidth");
        source.Should().Contain("button.MinHeight = style.ButtonHeight;");
        source.Should().Contain("button.MaxHeight = style.ButtonHeight;");
        source.Should().Contain("button.CornerRadius = style.ButtonCornerRadius;");
        source.Should().Contain("var restingBackground = style.ButtonBackgroundBrush ?? ThemeWhiteBrush();");
        source.Should().Contain("new Setter(Button.BackgroundProperty, restingBackground)");
        source.Should().Contain("Class(\":pointerover\")");
        source.Should().Contain("Class(\":pressed\")");
        source.Should().Contain("? style.DefaultButtonBorderBrush ?? accentBrush");
        source.Should().Contain(": style.ButtonBorderBrush ?? ButtonBorderBrush;");
        source.Should().Contain("button.IsDefault = true;");
        source.Should().Contain("if (fixedHeight)");
        source.Should().Contain("var height = style.TextBoxHeight ?? style.ControlHeight;");
        source.Should().Contain("textBox.Padding = style.TextBoxPadding;");
        source.Should().Contain("textBox.FocusAdorner = null;");
        source.Should().Contain("style.FocusedInputBorderBrush ?? ThemeAccentBrush(style)");
        source.Should().Contain("var height = style.ComboBoxHeight ?? style.ControlHeight;");
        source.Should().Contain("comboBox.Padding = style.ComboBoxPadding;");
        source.Should().Contain("public static void ApplyCheckBox(");
        source.Should().Contain("public static void ApplyRadioButton(");
        source.Should().Contain("checkBox.Padding = style.TogglePadding;");
        source.Should().Contain("radioButton.Padding = style.TogglePadding;");
        source.Should().Contain("checkBox.VerticalContentAlignment = VerticalAlignment.Center;");
        source.Should().Contain("radioButton.VerticalContentAlignment = VerticalAlignment.Center;");
        source.Should().Contain("public static void ApplyListBox(");
        source.Should().Contain("case GroupBox groupBox:");
        source.Should().Contain("case Label label:");
        source.Should().Contain("groupBox.Margin = style.GroupBoxMargin;");
        source.Should().Contain("groupBox.Padding = style.GroupBoxPadding;");
        source.Should().Contain("label.Padding = style.LabelPadding;");
        source.Should().Contain("new Setter(Layoutable.MinHeightProperty, style.ListBoxItemMinHeight)");
        source.Should().Contain("public static StackPanel CreateActionRow(");
        source.Should().Contain("public static void ApplyClassicTabChrome(");
        source.Should().Contain("var tabHeight = style.TabHeight ?? style.ControlHeight;");
        source.Should().Contain("new Setter(Layoutable.HeightProperty, explicitTabHeight)");
        source.Should().Contain("new Setter(Layoutable.MaxHeightProperty, explicitTabHeight)");
        source.Should().Contain("Name(\"PART_ItemsPresenter\")");
        source.Should().Contain("Name(\"PART_SelectedContentHost\")");
        source.Should().Contain("style.DialogTabPaneBorderBrush ?? DialogTabPaneBorderBrush");
        source.Should().Contain("style.TabHeight ?? style.ControlHeight");
        source.Should().Contain("DialogTabChromeMetrics.AdjacentTabOverlap");
        source.Should().Contain("DialogTabChromeMetrics.SelectedTabContentOverlap");
        source.Should().Contain("FuncControlTemplate<TabItem>");
    }

    [Fact]
    public void EveryDialogTabControl_UsesSharedClassicChrome()
    {
        var paths = new[]
        {
            new[] { "src", "FreeX.App.Avalonia", "MainWindow.cs" },
            new[] { "src", "FreeX.App.Avalonia", "MainWindow.PageLayout.cs" },
            new[] { "src", "FreeX.App.Avalonia", "MainWindow.PivotFilters.cs" },
            new[] { "src", "FreeX.App.Avalonia", "MainWindow.PivotFieldSettings.cs" },
            new[] { "src", "FreeX.App.Avalonia", "MainWindow.PivotOptions.cs" },
            new[] { "src", "FreeX.App.Avalonia", "MainWindow.Symbol.cs" },
            new[] { "freew", "FreeW.App.Avalonia", "OptionsDialog.cs" },
            new[] { "shared", "Free.Shared.Shell.Avalonia", "AvaloniaLegalNoticesDialog.cs" },
        };
        var source = string.Join(Environment.NewLine, paths.Select(path => File.ReadAllText(RepoFile(path))));

        CountOccurrences(source, "new TabControl").Should().Be(10);
        source.Should().Contain("private readonly TabControl _tabControl = new();");
        CountOccurrences(source, "AvaloniaCompactDialogChrome.ApplyClassicTabChrome(").Should().Be(11);
        source.Should().NotContain("private static void ApplyClassicTabChrome");
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

    [Fact]
    public void DataOpsDialogs_DelegateCompactControlChromeToSharedHelper()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Consolidate.cs"));

        source.Should().Contain("using Free.Shared.Shell.Avalonia;");
        source.Should().Contain("private static AvaloniaCompactDialogChromeStyle DataOpsDialogChromeStyle => new(FormulaBarFontFamily);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, DataOpsDialogChromeStyle, button.MinWidth, isDefault);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(textBox, DataOpsDialogChromeStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, DataOpsDialogChromeStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, DataOpsDialogChromeStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyRadioButton(radioButton, DataOpsDialogChromeStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([applyButton, cancelButton], new Thickness(0, 12, 0, 0));");

        AssertNoLocalButtonChrome(source);
        AssertNoLocalTextBoxChrome(source, "textBox");
        AssertNoLocalComboBoxChrome(source, "comboBox");
    }

    [Fact]
    public void ConsolidateDialog_MatchesWpfFunctionAndRangePickerLayout()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Consolidate.cs"));

        source.Should().Contain("HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,");
        source.Should().Contain("Width = ConsolidateDialogPlanner.CaptureContentWidth,");
        source.Should().Contain("Height = ConsolidateDialogPlanner.CaptureContentHeight,");
        source.Should().Contain("ControlHeight = 20,");
        source.Should().Contain("ButtonHeight = 20,");
        source.Should().Contain("ConsolidateDialogChromeStyle with { ControlHeight = 22 }");
        source.Should().Contain("ApplyDataOpsRangePickerButtonChrome(browseButton);");
        source.Should().Contain("ApplyDataOpsRangePickerButtonChrome(destinationBrowseButton);");
        source.Should().Contain("button.Padding = new Thickness(0, 1);");
        source.Should().Contain("browseButton.Margin = new Thickness(0, 0, 6, 0);");
        source.Should().Contain("destinationBrowseButton.Margin = new Thickness(0, 0, 6, 0);");
        source.Should().Contain("Spacing = 0,");
        source.Should().Contain("functionBox.Margin = new Thickness(0, 0, 0, 8);");
        source.Should().Contain("new Thickness(0, 6, 0, 13)");
        source.Should().Contain("Margin = new Thickness(0, 0, 0, 1),");
        source.Should().Contain("topRowBox.Margin = new Thickness(0, 0, 16, 0);");
        source.Should().NotContain("new Thickness(0, 0, 0, 0)");
        source.Should().NotContain("DockPanel.SetDock(buttonRow, Dock.Bottom);");
        source.Should().NotContain("FontWeight = FontWeight.SemiBold");
    }

    [Fact]
    public void ResidualClusterCDialogs_DelegateRemainingChromeToSharedHelper()
    {
        var allowEditRangeSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.AllowEditRange.cs"));
        var definedNamesSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.DefinedNames.cs"));
        var consolidateSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Consolidate.cs"));
        var protectionSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Protection.cs"));
        var selectionPaneSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.SelectionPane.cs"));
        var tableResizeSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.TableResize.cs"));

        allowEditRangeSource.Should().Contain("using Free.Shared.Shell.Avalonia;");
        allowEditRangeSource.Should().Contain(
            "AvaloniaCompactDialogChrome.ApplyListBox(rangesList, AllowEditRangeDialogChromeStyle);");
        allowEditRangeSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow(");
        allowEditRangeSource.Should().Contain(
            "Children = { newButton, modifyButton, deleteButton, permissionsButton }");
        allowEditRangeSource.Should().Contain(
            "AvaloniaCompactDialogChrome.CreateActionRow([okButton, closeButton], style: AvaloniaCompactDialogChrome.WindowsStyle);");

        consolidateSource.Should().Contain("ApplyDataOpsListBoxChrome(referencesList);");
        consolidateSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyListBox(listBox, DataOpsDialogChromeStyle);");
        consolidateSource.Should().Contain("[addButton, removeButton]");

        definedNamesSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([closeButton], new Thickness(0, 10, 0, 0));");
        definedNamesSource.Should().Contain("ApplyNamesCheckBoxChrome(topRowBox);");
        definedNamesSource.Should().Contain("ApplyNamesCheckBoxChrome(leftColumnBox);");
        definedNamesSource.Should().Contain("ApplyNamesCheckBoxChrome(bottomRowBox);");
        definedNamesSource.Should().Contain("ApplyNamesCheckBoxChrome(rightColumnBox);");
        definedNamesSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, NamesDialogChromeStyle);");
        definedNamesSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow(");
        definedNamesSource.Should().Contain("[cancelButton, okButton]");

        protectionSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton], new Thickness(0, 10, 0, 0));");
        selectionPaneSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel]);");
        selectionPaneSource.Should().NotContain("Children = { ok, cancel }");
        tableResizeSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0))");

        foreach (var source in new[] { allowEditRangeSource, definedNamesSource, consolidateSource, protectionSource, selectionPaneSource, tableResizeSource })
        {
            AssertNoLocalButtonChrome(source);
            AssertNoLocalTextBoxChrome(source, "textBox");
            AssertNoLocalComboBoxChrome(source, "comboBox");
        }
    }

    [Fact]
    public void ConditionalFormatDialogs_DelegateCompactControlChromeToSharedHelper()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ConditionalFormat.cs"));

        source.Should().Contain("using Free.Shared.Shell.Avalonia;");
        source.Should().Contain("private static AvaloniaCompactDialogChromeStyle ConditionalFormatDialogChromeStyle => new(FormulaBarFontFamily);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, style ?? ConditionalFormatDialogChromeStyle, width, isDefault);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(tb, ConditionalFormatDialogChromeStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(cb, ConditionalFormatDialogChromeStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyCheckBox(cb, ConditionalFormatDialogChromeStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyListBox(");
        source.Should().Contain("var manageDialogChrome = ConditionalFormatDialogChromeStyle with { ButtonHeight = 22 };");
        source.Should().Contain("manageDialogChrome with { ListBoxItemPadding = new Thickness(2, 0) }");
        source.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton], new Thickness(0, 10, 0, 0));");

        AssertNoLocalButtonChrome(source);
        AssertNoLocalTextBoxChrome(source, "tb");
        AssertNoLocalComboBoxChrome(source, "cb");
        AssertNoLocalListBoxChrome(source);
    }

    [Fact]
    public void OptionsProtectionAndSheetDialogs_DelegateCompactControlChromeToSharedHelper()
    {
        var optionsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));
        var protectionSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Protection.cs"));
        var moveCopySource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.MoveCopySheet.cs"));
        var sheetOptionsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.SheetOptionsNotes.cs"));

        optionsSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle OptionsDialogChromeStyle => new(FormulaBarFontFamily);");
        optionsSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, OptionsDialogChromeStyle, minWidth, isDefault);");
        optionsSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(textBox, OptionsDialogChromeStyle);");
        optionsSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, OptionsDialogChromeStyle);");
        optionsSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, OptionsDialogChromeStyle);");
        optionsSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyRadioButton(radioButton, OptionsDialogChromeStyle);");

        protectionSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle ProtectionDialogChromeStyle => new(FormulaBarFontFamily);");
        protectionSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, ProtectionDialogChromeStyle, minWidth, isDefault);");
        protectionSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(textBox, ProtectionDialogChromeStyle);");
        protectionSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, ProtectionDialogChromeStyle);");
        protectionSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton], new Thickness(0, 10, 0, 0));");

        moveCopySource.Should().Contain("private static AvaloniaCompactDialogChromeStyle MoveCopySheetDialogChromeStyle => new(FormulaBarFontFamily);");
        moveCopySource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, MoveCopySheetDialogChromeStyle, minWidth, isDefault);");
        moveCopySource.Should().Contain("AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, MoveCopySheetDialogChromeStyle);");
        moveCopySource.Should().Contain("AvaloniaCompactDialogChrome.ApplyListBox(listBox, MoveCopySheetDialogChromeStyle);");
        moveCopySource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton], new Thickness(0, 12, 0, 0));");

        sheetOptionsSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle SheetOptionsDialogChromeStyle => new(FormulaBarFontFamily);");
        sheetOptionsSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, SheetOptionsDialogChromeStyle, minWidth, isDefault);");
        sheetOptionsSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, SheetOptionsDialogChromeStyle);");
        sheetOptionsSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyListBox(listBox, SheetOptionsDialogChromeStyle);");
        sheetOptionsSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 14, 0, 0));");
        sheetOptionsSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([goToButton, closeButton], new Thickness(0, 10, 0, 0));");

        foreach (var source in new[] { optionsSource, protectionSource, moveCopySource, sheetOptionsSource })
        {
            source.Should().Contain("using Free.Shared.Shell.Avalonia;");
            AssertNoLocalButtonChrome(source);
            AssertNoLocalTextBoxChrome(source, "textBox");
            AssertNoLocalListBoxChrome(source);
        }
    }

    [Fact]
    public void SelectionPaneDialog_DelegatesCompactControlChromeToSharedHelper()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.SelectionPane.cs"));

        source.Should().Contain("using Free.Shared.Shell.Avalonia;");
        source.Should().Contain("private static AvaloniaCompactDialogChromeStyle SelectionPaneDialogChromeStyle => new(FormulaBarFontFamily);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, SelectionPaneDialogChromeStyle, width, isDefault);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(textBox, SelectionPaneDialogChromeStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, SelectionPaneDialogChromeStyle);");

        AssertNoLocalButtonChrome(source);
        AssertNoLocalTextBoxChrome(source, "textBox");
        AssertNoLocalComboBoxChrome(source, "comboBox");
    }

    [Fact]
    public void ResidualRibbonMenuSymbolAndInsertDialogs_DelegateCompactControlChromeToSharedHelper()
    {
        var ribbonSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.RibbonMenuDialogs.cs"));
        var symbolSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Symbol.cs"));
        var insertSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.InsertObjects.cs"));
        var moreColorsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.MoreColors.cs"));

        ribbonSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle RibbonMenuDialogChromeStyle => new(FormulaBarFontFamily);");
        ribbonSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, RibbonMenuDialogChromeStyle, minWidth, isDefault);");
        ribbonSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(textBox, RibbonMenuDialogChromeStyle);");
        ribbonSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow(");

        symbolSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle SymbolDialogChromeStyle => new(FormulaBarFontFamily);");
        symbolSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, SymbolDialogChromeStyle, minWidth, isDefault);");
        symbolSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(textBox, SymbolDialogChromeStyle);");
        symbolSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(");
        symbolSource.Should().Contain("SymbolDialogChromeStyle with { ComboBoxPadding = new Thickness(6, 1) }");
        symbolSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([insert, cancel], new Thickness(0, 12, 0, 0));");

        insertSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle InsertObjectDialogChromeStyle => new(FormulaBarFontFamily);");
        insertSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, InsertObjectDialogChromeStyle, width, isDefault);");
        insertSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(textBox, InsertObjectDialogChromeStyle);");
        insertSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, InsertObjectDialogChromeStyle);");
        insertSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow(");
        insertSource.Should().Contain("CreateTableDialogPlanner.ActionRowTopMargin");

        moreColorsSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle MoreColorsDialogChromeStyle => new(FormulaBarFontFamily);");
        moreColorsSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, MoreColorsDialogChromeStyle, width, isDefault);");
        moreColorsSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(textBox, MoreColorsDialogChromeStyle);");
        moreColorsSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton]);");

        foreach (var source in new[] { symbolSource, insertSource, moreColorsSource })
        {
            source.Should().NotContain("Height = 24,");
            source.Should().NotContain("BorderBrush = Brush(130, 130, 130),");
            source.Should().NotContain("BorderBrush = Brush(0, 120, 215),");
            source.Should().NotContain("BorderBrush = Brush(112, 112, 112),");
        }
    }

    [Fact]
    public void ResidualDataOutlinePrintAndFilterDialogs_DelegateCompactControlChromeToSharedHelper()
    {
        var getDataSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.GetData.cs"));
        var outlineSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Outline.cs"));
        var printSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Print.cs"));
        var autoFilterSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.AutoFilter.cs"));
        var errorCheckingSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ErrorChecking.cs"));
        var parityCaptureSource = File.ReadAllText(RepoFile(
            "tools", "FreeX.ParityCapture.Avalonia", "Capture", "MainWindow.ParityCapture.cs"));

        getDataSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle GetDataDialogChromeStyle => new(FormulaBarFontFamily);");
        getDataSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, GetDataDialogChromeStyle, minWidth, isDefault);");
        getDataSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(tb, GetDataDialogChromeStyle);");
        getDataSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(cb, GetDataDialogChromeStyle);");
        getDataSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyCheckBox(cb, GetDataDialogChromeStyle);");
        getDataSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyRadioButton(rb, GetDataDialogChromeStyle);");
        getDataSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([loadButton, cancelButton], new Thickness(0, 8, 0, 0));");

        outlineSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle OutlineDialogChromeStyle => new(FormulaBarFontFamily);");
        outlineSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, OutlineDialogChromeStyle, minWidth, isDefault);");
        outlineSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyCheckBox(cb, OutlineDialogChromeStyle);");
        outlineSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton], new Thickness(0, 12, 0, 0))");

        printSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle PrintDialogChromeStyle => new(FormulaBarFontFamily);");
        printSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, PrintDialogChromeStyle, minWidth, isDefault);");
        printSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(tb, PrintDialogChromeStyle);");
        printSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(cb, PrintDialogChromeStyle);");
        printSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyRadioButton(rb, PrintDialogChromeStyle);");
        printSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyCheckBox(cb, PrintDialogChromeStyle);");
        printSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([cancelButton, printButton]);");

        autoFilterSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle AutoFilterDialogChromeStyle => new(FormulaBarFontFamily);");
        autoFilterSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(okButton, AutoFilterDialogChromeStyle, 72, isDefault: true);");
        autoFilterSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([okButton], new Thickness(0, 6, 0, 0))");

        errorCheckingSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle ErrorCheckingDialogChromeStyle => new(FormulaBarFontFamily);");
        errorCheckingSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyWindow(dialog, ErrorCheckingDialogChromeStyle);");
        errorCheckingSource.Should().NotContain("ErrorCheckingParityFixture.CreateIssues(sheetId)");
        parityCaptureSource.Should().Contain("ErrorCheckingParityFixture.CreateIssues(sheetId)");
        errorCheckingSource.Should().Contain("Width = ErrorCheckingDialogPlanner.AvaloniaClientWidth");
        errorCheckingSource.Should().Contain("Height = ErrorCheckingDialogPlanner.AvaloniaClientHeight");
        errorCheckingSource.Should().Contain("HorizontalAlignment = AvaloniaHorizontalAlignment.Left");
        errorCheckingSource.Should().Contain("VerticalAlignment = AvaloniaVerticalAlignment.Top");
        errorCheckingSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(");
        errorCheckingSource.Should().Contain("ErrorCheckingDialogChromeStyle,");
        errorCheckingSource.Should().NotContain("Height=24, Padding=(4,1), white background");

        foreach (var source in new[] { getDataSource, outlineSource, printSource, autoFilterSource })
        {
            AssertNoLocalButtonChrome(source);
            AssertNoLocalTextBoxChrome(source, "tb");
            AssertNoLocalComboBoxChrome(source, "cb");
        }
    }

    [Fact]
    public void DrawingFormatDialogs_DelegateResidualCompactControlChromeToSharedHelper()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.DrawingFormatDialogs.cs"));

        source.Should().Contain("using Free.Shared.Shell.Avalonia;");
        source.Should().Contain("private static AvaloniaCompactDialogChromeStyle DrawingDialogChromeStyle => new(FormulaBarFontFamily);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, DrawingDialogChromeStyle, width, isDefault);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(tb, DrawingDialogChromeStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(cb, DrawingDialogChromeStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyCheckBox(cb, DrawingDialogChromeStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([resetButton, ok, cancel], new Thickness(0, 8, 0, 0))");
        source.Should().Contain("DrawingDialogChromeStyle with { ButtonHeight = 22, ButtonPadding = new Thickness(8, 1) }");

        AssertNoLocalButtonChrome(source);
        AssertNoLocalTextBoxChrome(source, "tb");
        AssertNoLocalComboBoxChrome(source, "cb");
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

    private static void AssertNoLocalListBoxChrome(string source)
    {
        source.Should().NotContain("new Setter(TemplatedControl.PaddingProperty, new Thickness(4, 1))");
        source.Should().NotContain("new Setter(Layoutable.MinHeightProperty, 24.0)");
        source.Should().NotContain("new Setter(global::Avalonia.Controls.Control.MinHeightProperty, 24.0)");
        source.Should().NotContain("new Setter(TemplatedControl.FontSizeProperty, 12.0)");
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(parts);
}
