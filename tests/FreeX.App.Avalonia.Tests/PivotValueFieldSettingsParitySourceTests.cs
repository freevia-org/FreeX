namespace FreeX.App.Avalonia.Tests;

public sealed class PivotValueFieldSettingsParitySourceTests
{
    [Fact]
    public void AvaloniaValueFieldSettings_MatchesWpfClientGeometryAndControlMetrics()
    {
        var avalonia = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotFieldSettings.cs"));
        var visual = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "PivotValueFieldSettingsVisual.cs"));
        var wpf = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "PivotValueFieldSettingsDialog.xaml"));

        wpf.Should().Contain("Width=\"430\"");
        wpf.Should().Contain("Height=\"430\"");
        wpf.Should().Contain("<DockPanel Margin=\"14\">");
        wpf.Should().Contain("<ColumnDefinition Width=\"118\"/>");
        wpf.Should().Contain("Width=\"78\"");
        wpf.Should().Contain("Height=\"24\"");

        avalonia.Should().Contain("Background = Brushes.Transparent,");
        avalonia.Should().Contain("Width = PivotValueFieldSettingsVisual.ClientWidth,");
        avalonia.Should().Contain("Height = PivotValueFieldSettingsVisual.ClientHeight,");
        avalonia.Should().Contain("Child = new Grid { Margin = new Thickness(PivotValueFieldSettingsVisual.OuterMargin), Children = { bodyGrid } }");
        avalonia.Should().Contain("SetWpfValueFieldTextBoxHeight(nameBox);");
        avalonia.Should().Contain("PivotValueFieldSettingsVisual.ApplyTextBox(baseItemBox, PivotValueFieldSettingsVisual.ControlHeight);");
        avalonia.Should().Contain("SetWpfValueFieldButtonHeight(ok);");
        avalonia.Should().Contain("SetWpfValueFieldButtonHeight(cancel);");
        avalonia.Should().Contain("SetWpfValueFieldButtonHeight(numberFormatButton);");
        visual.Should().Contain("public const double TabContentMargin = 10;");
        visual.Should().Contain("public const double ControlHeight = 24;");
        visual.Should().Contain("public const double TextBoxHeight = 18;");
        visual.Should().Contain("public const double ButtonHeight = 20;");
        visual.Should().Contain("public const double ButtonWidth = 78;");
        visual.Should().Contain("public const double NumberFormatButtonWidth = 128;");
        avalonia.Should().Contain("PivotDialogChromeStyle with { ControlHeight = 20 }");
        CountOccurrences(avalonia, "Margin = new Thickness(PivotValueFieldSettingsVisual.TabContentMargin)").Should().Be(3);
        CountOccurrences(avalonia, "Margin = new Thickness(0, 0, 0, PivotValueFieldSettingsVisual.LabelControlSpacing)").Should().Be(3);
        avalonia.Should().NotContain("Spacing = 6, Margin = new Thickness(0)");
        avalonia.Should().Contain("HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch");
        avalonia.Should().Contain("new Thickness(0, 10, 0, 6)");
        avalonia.Should().Contain("ApplyPivotButtonChrome(ok, PivotValueFieldSettingsVisual.ButtonWidth, isDefault: true);");
        avalonia.Should().Contain("ApplyPivotButtonChrome(cancel, PivotValueFieldSettingsVisual.ButtonWidth);");
        avalonia.Should().Contain("ApplyPivotButtonChrome(numberFormatButton, PivotValueFieldSettingsVisual.NumberFormatButtonWidth);");
        avalonia.Should().Contain("AvaloniaCompactDialogChrome.ApplyClassicTabChrome(");
    }

    [Fact]
    public void AvaloniaValueFieldSettings_PreservesProductionAutomationIds()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotFieldSettings.cs"));

        foreach (var automationId in new[]
                 {
                     "PivotValueFieldSettingsDialog",
                     "PivotValueFieldSettingsNameBox",
                     "PivotValueFieldSettingsSummaryBox",
                     "PivotValueFieldSettingsShowValuesAsBox",
                     "PivotValueFieldSettingsBaseFieldBox",
                     "PivotValueFieldSettingsBaseItemBox",
                     "PivotValueNumberFormatPresetBox",
                     "PivotValueNumberFormatButton",
                     "PivotValueFieldSettingsOkButton",
                     "PivotValueFieldSettingsCancelButton",
                     "PivotValueFieldSettingsTabs",
                 })
        {
            source.Should().Contain($"\"{automationId}\"");
        }
    }

    [Fact]
    public void AvaloniaValueFieldSettings_UsesTheSharedLocalizedNumberFormatCatalog()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotFieldSettings.cs"));

        source.Should().Contain("PivotValueFieldPlanner.NumberFormatPresets");
        source.Should().Contain("UiText.Get(preset.ResourceKey)");
        source.Should().Contain("PivotValueFieldPlanner.FindNumberFormatPresetIndex(field.NumberFormatId)");
        source.Should().Contain("PivotValueNumberFormatPresetBox");
        source.Should().Contain("numberFormatPanel.Children.Add(numberFormatPresetBox);");
        source.Should().Contain("numberFormatId = preset.NumberFormatId;");
        source.Should().Contain("numberFormatCode = null;");
        source.Should().Contain("numberFormatButton.Click += async");
        source.Should().Contain("ShowPivotNumberFormatInputDialogAsync(CurrentNumberFormatCode())");
        source.Should().Contain("SetNumberFormatState(acceptedFormat)");
        source.Should().Contain("numberFormatPresetBox.Items.Add(formatCode)");
    }

    [Fact]
    public void AvaloniaValueFieldSettings_UsesLocalizedShowValuesAsValidationMessages()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotFieldSettings.cs"));

        source.Should().Contain("PivotValueFieldPlanner.ValidateShowValuesAs(showValuesAs, baseFieldIndex, baseItem)");
        source.Should().Contain("PivotValueFieldPlanner.DescribeValidationError(validationError)");
        source.Should().Contain("ShowEditIssue(UiText.Get(errorPlan.ResourceKey));");
        source.Should().Contain("FocusInvalidShowValuesAsInput(valueFieldTabs!, baseFieldBox, baseItemBox, baseFieldIndex);");
        source.Should().NotContain("PivotValueFieldPlanner.TryValidateShowValuesAs(showValuesAs, baseFieldIndex, baseItem");
    }

    [Fact]
    public void SharedValueFieldResultPlanner_RoundTripsNumberFormatSelection()
    {
        var field = new FreeX.Core.Model.PivotDataFieldModel(
            SourceFieldIndex: 0,
            Name: "Sum of Sales",
            SummaryFunction: "sum",
            NumberFormatId: 7,
            NumberFormatCode: null);

        var result = FreeX.App.Presentation.PivotUI.PivotValueFieldPlanner.CreateResult(
            field,
            ["Sales"],
            field.Name,
            summaryFunctionIndex: 0,
            showValuesAsIndex: 0,
            baseFieldSelectedIndex: 0,
            baseItemText: null,
            numberFormatId: 14,
            numberFormatCode: null);

        result.NumberFormatId.Should().Be(14);
        result.NumberFormatCode.Should().BeNull();
    }

    [Theory]
    [InlineData("General", null, null)]
    [InlineData("$#,##0.00", 7, null)]
    [InlineData("$#,##0.00;[Red]($#,##0.00)", 8, null)]
    [InlineData("0.0000", 164, "0.0000")]
    public void SharedNumberFormatStatePlanner_MapsAcceptedFormatCode(
        string code,
        int? expectedId,
        string? expectedCustomCode)
    {
        var state = FreeX.App.Presentation.PivotUI.PivotValueFieldPlanner.ResolveNumberFormatState(code);

        state.NumberFormatId.Should().Be(expectedId);
        state.NumberFormatCode.Should().Be(expectedCustomCode);
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;
}
