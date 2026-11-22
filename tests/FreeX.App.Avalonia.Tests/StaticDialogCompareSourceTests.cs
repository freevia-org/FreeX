using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Presentation.PivotUI;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Source contracts from the static WPF-versus-Avalonia review of Create Names from Selection, the colour
/// picker, the workbook theme editor and the PivotTable Value/Label filter and calculated dialogs. Each pin
/// names the WPF fact the Avalonia dialog has to keep matching.
/// </summary>
public sealed class StaticDialogCompareSourceTests
{
    [Fact]
    public void CreateNamesFromSelection_ClientWidthIsTheWpfOuterWidthMinusTheNoResizeFrame()
    {
        var source = ReadAppSource("MainWindow.DefinedNames.cs").Replace("\r\n", "\n");
        var dialogSource = Between(
            source,
            "private async Task ShowCreateNamesFromSelectionDialogAsync()",
            "// ── Helpers");

        // WPF CreateNamesFromSelectionDialog: Width = 280 is the OUTER width of a NoResize window.
        dialogSource.Should().Contain("Width = 280 - WpfNoResizeFrameWidth,");
        dialogSource.Should().Contain("MinWidth = 280 - WpfNoResizeFrameWidth,");
        dialogSource.Should().Contain("MaxWidth = 280 - WpfNoResizeFrameWidth,");
        dialogSource.Should().NotContain("Width = 280,");
    }

    [Fact]
    public void PivotFilterDialogs_ListLocalizedKindLabelsLikeTheWpfDialogs()
    {
        var source = ReadAppSource("MainWindow.PivotFilters.cs");

        source.Should().Contain("foreach (var option in PivotFieldFilterPlanner.LabelFilterKinds)");
        source.Should().Contain("foreach (var option in PivotFieldFilterPlanner.ValueFilterKinds)");
        source.Should().Contain("kindBox.Items.Add(UiText.Get(option.ResourceKey));");
        source.Should().NotContain("foreach (var (label, _) in PivotFieldFilterPlanner.LabelFilterKinds)");
        source.Should().NotContain("foreach (var (label, _) in PivotFieldFilterPlanner.ValueFilterKinds)");
    }

    [Fact]
    public void PivotFilterKindResources_MatchThePlannerEnglishLabelsSoWpfAndAvaloniaShowTheSameText()
    {
        var resx = XDocument.Load(RepoFile("src", "FreeX.App.Localization", "Resources", "Strings.resx"));
        var values = resx.Root!.Elements("data").ToDictionary(
            data => (string)data.Attribute("name")!,
            data => (string)data.Element("value")!);

        foreach (var option in PivotFieldFilterPlanner.LabelFilterKinds)
            values[option.ResourceKey].Should().Be(option.FallbackLabel, option.ResourceKey);
        foreach (var option in PivotFieldFilterPlanner.ValueFilterKinds)
            values[option.ResourceKey].Should().Be(option.FallbackLabel, option.ResourceKey);
    }

    [Fact]
    public void PivotFilterForms_EditorsStretchToTheWpfLabelRowHeight()
    {
        var source = ReadAppSource("MainWindow.PivotFilters.cs");

        // WPF PivotLabelFilterDialog/PivotValueFilterDialog: Labels are 25.96px (15.96 text + 5px padding top
        // and bottom) and the editors have no vertical alignment, so they stretch to that row.
        source.Should().Contain("private const double PivotFilterFormRowEditorHeight = 26;");
        source.Should().Contain("editor.Height = PivotFilterFormRowEditorHeight;");
        source.Should().Contain("editor.MinHeight = PivotFilterFormRowEditorHeight;");
        source.Should().Contain("editor.MaxHeight = PivotFilterFormRowEditorHeight;");
        source.Should().Contain("Margin = new Thickness(5, 5 + top, 13, 5 + bottom),");
    }

    [Fact]
    public void WorkbookThemeEditor_InvalidColourWarningUsesTheCustomizeThemeTitleInEveryMode()
    {
        var source = ReadAppSource("MainWindow.ThemeCustomize.cs");

        // WPF ShowInvalidThemeColor always titles the warning WorkbookTheme_CustomizeThemeTitle.
        source.Should().Contain("UiText.Get(\"WorkbookTheme_CustomizeThemeTitle\")");
    }

    [Fact]
    public void ColorPickerInvalidRgbMessage_IsOneLocalizedKeySharedByWpfAndAvalonia()
    {
        var avalonia = ReadAppSource("MainWindow.MoreColors.cs");
        var wpf = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "ColorPickerDialog.xaml.cs"));

        avalonia.Should().Contain("UiText.Get(\"ColorPicker_InvalidRgbMessage\")");
        wpf.Should().Contain("UiText.Get(\"ColorPicker_InvalidRgbMessage\")");
        wpf.Should().NotContain("\"Enter RGB values from 0 to 255.\"");
    }

    private static string Between(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0, start);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThan(startIndex, end);
        return source[startIndex..endIndex];
    }

    private static string ReadAppSource(string fileName) =>
        File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", fileName));

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeX.slnx", parts);
}
