using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaSheetDialogSizeSourceTests
{
    [Fact]
    public void RenameAndUnhideSheetDialogs_UseWpfLogicalCaptureSizes()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        var renameDialog = ExtractMethodSource(
            source,
            "private async Task<string?> ShowRenameSheetDialogAsync(string currentName)",
            "private async Task ShowFindDialogAsync()");
        renameDialog.Should().Contain("Width = 340,");
        renameDialog.Should().Contain("Height = 150,");
        renameDialog.Should().Contain("MinWidth = 340,");
        renameDialog.Should().Contain("MinHeight = 150,");
        renameDialog.Should().Contain("MaxWidth = 340,");
        renameDialog.Should().Contain("MaxHeight = 150,");
        renameDialog.Should().Contain("CanResize = false,");
        renameDialog.Should().Contain("MinWidth = 72,");

        var unhideDialog = ExtractMethodSource(
            source,
            "private async Task<WorkbookHiddenSheet?> ShowUnhideSheetDialogAsync(IReadOnlyList<WorkbookHiddenSheet> hiddenSheets)",
            "private async Task<string?> ShowRenameSheetDialogAsync(string currentName)");
        unhideDialog.Should().Contain("Width = 340,");
        unhideDialog.Should().Contain("Height = 160,");
        unhideDialog.Should().Contain("MinWidth = 340,");
        unhideDialog.Should().Contain("MinHeight = 160,");
        unhideDialog.Should().Contain("MaxWidth = 340,");
        unhideDialog.Should().Contain("MaxHeight = 160,");
        unhideDialog.Should().Contain("CanResize = false,");
        unhideDialog.Should().Contain("ApplyDialogButtonChrome(okButton, width: 72, isDefault: true);");
        unhideDialog.Should().Contain("ApplyDialogButtonChrome(cancelButton, width: 72);");
    }

    [Fact]
    public void RemainingDataDialogs_UseStableLiveRendererSizes()
    {
        var rendererSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.RendererAccess.cs"));
        rendererSource.Should().Contain("private const int ForecastSheetDialogWidth = 320;");
        rendererSource.Should().Contain("private const int ForecastSheetDialogHeight = 150;");
        rendererSource.Should().Contain("private const int SubtotalDialogWidth = 380;");
        rendererSource.Should().Contain("private const int SubtotalDialogHeight = 390;");
        rendererSource.Should().Contain(
            "private const int TextToColumnsDialogWidth = (int)TextToColumnsDialogMetrics.WindowWidth;");
        rendererSource.Should().Contain(
            "private const int TextToColumnsDialogHeight = (int)TextToColumnsDialogMetrics.WindowHeight;");
        rendererSource.Should().NotContain("ParityDialogWidth");

        var mainWindowSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var forecastDialog = ExtractMethodSource(
            mainWindowSource,
            "private async Task<ForecastSheetPlan?> ShowForecastSheetInputDialogAsync()",
            "private static string FormatForecastSheetPlanError(ForecastSheetPlan plan)");
        forecastDialog.Should().Contain("Width = ForecastSheetDialogWidth,");
        forecastDialog.Should().Contain("Height = ForecastSheetDialogHeight,");
        forecastDialog.Should().Contain("MinWidth = ForecastSheetDialogWidth,");
        forecastDialog.Should().Contain("MinHeight = ForecastSheetDialogHeight,");
        forecastDialog.Should().Contain("MaxWidth = ForecastSheetDialogWidth,");
        forecastDialog.Should().Contain("MaxHeight = ForecastSheetDialogHeight,");

        var subtotalDialog = ExtractMethodSource(
            mainWindowSource,
            "private async Task<SubtotalDialogPlanResult?> ShowSubtotalInputDialogAsync(",
            "private static StackPanel CreateSubtotalField(string label, Control control, double topMargin = 0)");
        subtotalDialog.Should().Contain("Width = SubtotalDialogWidth,");
        subtotalDialog.Should().Contain("Height = SubtotalDialogHeight,");
        subtotalDialog.Should().Contain("MinWidth = SubtotalDialogWidth,");
        subtotalDialog.Should().Contain("MinHeight = SubtotalDialogHeight,");

        var textToColumnsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.TextToColumns.cs"));
        var textToColumnsDialog = ExtractMethodSource(
            textToColumnsSource,
            "private async Task ShowTextToColumnsDialogAsync()",
            "private static IReadOnlyList<string> ReadTextToColumnsSources(Sheet sheet, GridRange range)");
        textToColumnsDialog.Should().Contain("Width = TextToColumnsDialogWidth,");
        textToColumnsDialog.Should().Contain("Height = TextToColumnsDialogHeight,");
        textToColumnsDialog.Should().Contain("MinWidth = TextToColumnsDialogMetrics.MinimumWindowWidth,");
        textToColumnsDialog.Should().Contain("MinHeight = TextToColumnsDialogMetrics.MinimumWindowHeight,");
    }

    [Fact]
    public void SubtotalDialog_UsesSharedWindowsChromeAfterGenericDialogNormalization()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var subtotalDialog = ExtractMethodSource(
            source,
            "private async Task<SubtotalDialogPlanResult?> ShowSubtotalInputDialogAsync(",
            "private static StackPanel CreateSubtotalField(string label, Control control, double topMargin = 0)");

        subtotalDialog.Should().Contain("AvaloniaCompactDialogChrome.ApplyWindow(dialog, SubtotalDialogChromeStyle);");
        subtotalDialog.Should().Contain("ApplySubtotalComboBoxChrome(groupColumnBox);");
        subtotalDialog.Should().Contain("ApplySubtotalComboBoxChrome(functionBox);");
        subtotalDialog.Should().Contain("AvaloniaCompactDialogChrome.ApplyGroupBox(columnsGroup, SubtotalDialogChromeStyle);");
        subtotalDialog.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(okButton, SubtotalDialogChromeStyle, 72, isDefault: true);");
        subtotalDialog.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(removeAllButton, SubtotalDialogChromeStyle, 92);");
        subtotalDialog.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(cancelButton, SubtotalDialogChromeStyle, 72);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyCompactCheckBox(checkBox, SubtotalDialogChromeStyle);");
        subtotalDialog.Should().NotContain("ApplyDialogComboBoxChrome(groupColumnBox);");
        subtotalDialog.Should().NotContain("ApplyDialogButtonChrome(okButton, 72");
    }

    private static string ExtractMethodSource(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"source should contain {startMarker}");

        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"source should contain {endMarker} after {startMarker}");

        return source[start..end];
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(parts);
}
