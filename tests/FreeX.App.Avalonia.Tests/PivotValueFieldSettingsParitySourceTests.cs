namespace FreeX.App.Avalonia.Tests;

public sealed class PivotValueFieldSettingsParitySourceTests
{
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
