using System.IO;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Source contracts for the WPF features carried into the Avalonia colour picker, the Format Cells
/// font tab and the workbook theme editor.
/// </summary>
public sealed class ColorPickerFontThemeDialogFeatureSourceTests
{
    [Fact]
    public void ColorPicker_ExposesTheNoColorContractAndKeepsTheLegacyEntryPoint()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.MoreColors.cs"));
        var normalized = source.Replace("\r\n", "\n");

        source.Should().Contain("private readonly record struct ColorPickerChoice(CellColor? Color);");
        source.Should().Contain("private async System.Threading.Tasks.Task<CellColor?> ShowMoreColorsDialogAsync(string title, CellColor initial)");
        source.Should().Contain("var choice = await ShowColorPickerDialogAsync(title, initial, allowNoColor: false);");
        normalized.Should().Contain(
            "private System.Threading.Tasks.Task<ColorPickerChoice?> ShowColorPickerDialogAsync(\n" +
            "        string title,\n" +
            "        CellColor? initial,\n" +
            "        bool allowNoColor)");
        source.Should().Contain("if (allowNoColor)");
        source.Should().Contain("UiText.Get(\"ColorPicker_NoColor\")");
        source.Should().Contain("\"NoColorButton\"");
        source.Should().Contain("dialog.Close((ColorPickerChoice?)new ColorPickerChoice(null));");
        source.Should().Contain("buttons.Children.Insert(0, noColorButton);");
    }

    [Fact]
    public void ColorPicker_CustomPageHasSpectrumLuminosityAndComponentBoxesFromSharedPlanners()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.MoreColors.cs"));

        source.Should().Contain("CellColorPalettePlanner.BuildCustomSpectrumSwatches()");
        source.Should().Contain("CellColorPalettePlanner.ScaleColor(baseColor, luminositySlider.Value / 100d)");
        source.Should().Contain("FreeX.App.Presentation.ColorInputParser.TryParseColorText(");
        source.Should().Contain("FreeX.App.Presentation.ColorInputParser.TryParseRgbComponents(");
        source.Should().Contain("RowDefinitions = new RowDefinitions(\"Auto,Auto,Auto,Auto,Auto,Auto,Auto\")");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions(\"220,16,80,*\")");
        foreach (var automationId in new[]
        {
            "CustomSpectrumPanel", "CustomLuminositySlider", "CustomRedTextBox", "CustomGreenTextBox",
            "CustomBlueTextBox", "MoreColorsHexBox",
        })
        {
            source.Should().Contain("\"" + automationId + "\"");
        }

        foreach (var key in new[]
        {
            "ColorPicker_Luminosity", "ColorPicker_Red", "ColorPicker_Green", "ColorPicker_Blue",
            "ColorPicker_RRGGBBOrRGBValuesFrom0To255", "ColorPicker_InvalidColorMessage",
            "ColorPicker_CustomSpectrumColorGroup", "ColorPicker_CustomColorLuminosity",
        })
        {
            source.Should().Contain("\"" + key + "\"");
        }
    }

    [Fact]
    public void FormatCellsFontTab_OffersEveryUnderlineStyleAndMapsThemThroughTheSharedPlanner()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var helperSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.FormatCellsFont.cs"));

        source.Should().Contain("var fontLabels = CreateFormatCellsFontLabels();");
        source.Should().Contain("fontLabels.UnderlineDouble,");
        source.Should().Contain("fontLabels.UnderlineSingleAccounting,");
        source.Should().Contain("fontLabels.UnderlineDoubleAccounting,");
        source.Should().Contain("underlineBox.IsChecked = FormatCellsDialogPlanner.IsSingleUnderlineSelected(underlineLabel, fontLabels);");
        source.Should().Contain("doubleUnderlineBox.IsChecked = FormatCellsDialogPlanner.IsDoubleUnderlineSelected(underlineLabel, fontLabels);");
        source.Should().Contain("|| FormatCellsDialogPlanner.IsDoubleUnderlineSelected(underlineStyleBox.SelectedItem as string, fontLabels),");

        helperSource.Should().Contain("UiText.Get(\"FormatCells_UnderlineDouble\")");
        helperSource.Should().Contain("UiText.Get(\"FormatCells_UnderlineSingleAccounting\")");
        helperSource.Should().Contain("UiText.Get(\"FormatCells_UnderlineDoubleAccounting\")");
    }

    [Fact]
    public void FormatCellsFontTab_PreviewsATypedColourAndListsInstalledFonts()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var helperSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.FormatCellsFont.cs"));

        source.Should().Contain("fontColorTextBox.TextChanged += (_, _) =>");
        source.Should().Contain("if (TryParseRgb(fontColorTextBox.Text, out var typedPreviewColor))");
        source.Should().Contain("fontPreview.Foreground = Brush(typedPreviewColor);");
        source.Should().Contain("var fontNameChoices = BuildFormatCellsFontNameChoices(");

        helperSource.Should().Contain("FontManager.Current.SystemFonts");
        helperSource.Should().Contain(".Distinct(StringComparer.CurrentCultureIgnoreCase)");
        helperSource.Should().Contain(".OrderBy(font => font, StringComparer.CurrentCultureIgnoreCase)");
        helperSource.Should().Contain("fonts.Insert(0, current);");
    }

    [Fact]
    public void ThemesCommand_OpensTheWorkbookThemeEditorBuiltOnTheSharedPlanner()
    {
        var gallerySource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Themes.cs"));
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ThemeCustomize.cs"));

        gallerySource.Should().Contain("var picked = await ShowWorkbookThemeDialogAsync(_session.Workbook.Theme);");
        gallerySource.Should().Contain("WorkbookThemeCommandPlanner.PlanApply(chosen)");

        source.Should().Contain("private async Task<WorkbookTheme?> ShowWorkbookThemeDialogAsync(WorkbookTheme initialTheme)");
        source.Should().Contain("Title = UiText.Get(\"WorkbookTheme_CustomizeTheme\")");
        source.Should().Contain("Width = 760 - WpfNoResizeFrameWidth,");
        source.Should().Contain("Height = 600 - WpfNoResizeClientFrameHeight,");
        source.Should().Contain("WorkbookThemeDialogPlanner.TryCreateTheme(");
        source.Should().Contain("WorkbookThemeDialogPlanner.PreviewColorOrBlack(");
        source.Should().Contain("WorkbookThemeDialogColorCodec.FormatColor(theme.GetColor(field.Slot))");
        source.Should().Contain("await ShowColorPickerDialogAsync(");
        source.Should().Contain("UiText.Get(\"WorkbookTheme_Save\")");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions(\"94,*,94,*\")");
        source.Should().Contain("new UniformGrid { Columns = 3 }");
        foreach (var slot in new[]
        {
            "Dark1", "Light1", "Dark2", "Light2", "Accent1", "Accent2", "Accent3", "Accent4", "Accent5",
            "Accent6", "Hyperlink", "FollowedHyperlink",
        })
        {
            source.Should().Contain("\"WorkbookTheme_" + slot + "\"");
            source.Should().Contain("\"WorkbookTheme_Pick" + slot + "Color\"");
        }
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(parts);
}
