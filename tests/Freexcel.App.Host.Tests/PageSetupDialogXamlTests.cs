using System.IO;
using System.Xml.Linq;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class PageSetupDialogXamlTests
{
    [Fact]
    public void PageSetupDialog_ExposesKeyboardAccessKeysForTabsOptionsAndButtons()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml"));

        foreach (var header in new[]
        {
            "_Page",
            "_Margins",
            "_Header/Footer",
            "_Sheet"
        })
            xaml.Should().Contain($"Header=\"{header}\"");

        foreach (var content in new[]
        {
            "_Orientation:",
            "_Paper size:",
            "First _page number:",
            "Print _quality:",
            "_Left:",
            "_Right:",
            "_Top:",
            "_Bottom:",
            "_Header:",
            "_Footer:",
            "_Header preset:",
            "_Footer preset:",
            "Custom _Header...",
            "Custom _Footer...",
            "_Different first page",
            "Different _odd and even pages",
            "_Scale with document",
            "_Align with page margins",
            "Print _area:",
            "_Rows to repeat at top:",
            "_Columns to repeat at left:",
            "_Center horizontally",
            "Center _vertically",
            "_Print gridlines",
            "Print row and column _headings",
            "Pa_ge order:",
            "_Black and white",
            "_Draft quality",
            "Cell _errors as:",
            "Co_mments:",
            "_OK",
            "_Cancel"
        })
            xaml.Should().Contain(content);
    }

    [Fact]
    public void PageSetupDialogOpenedFromKeyboard_FocusesOrientationBox()
    {
        var source = ReadPageSetupDialogSource();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("OrientationBox.Focus();");
        source.Should().Contain("Keyboard.Focus(OrientationBox);");
    }

    [Fact]
    public void PageTab_UsesExcelLikeScalingChoices()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        document.Descendants(presentation + "GroupBox")
            .Single(element => element.Attribute("Header")?.Value == "Scaling")
            .Descendants(presentation + "RadioButton")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_Adjust to:", "_Fit to:"]);

        foreach (var name in new[] { "ScalePercentBox", "FitPagesWideBox", "FitPagesTallBox" })
        {
            document.Descendants()
                .Any(element => element.Attribute(x + "Name")?.Value == name)
                .Should().BeTrue($"{name} should exist for Excel-style scaling input");
        }
    }

    [Fact]
    public void PageTab_DisablesInactiveScalingInputsByMode()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml"));
        var source = ReadPageSetupDialogSource();

        xaml.Should().Contain("Checked=\"ScalingMode_Changed\"");
        source.Should().Contain("UpdateScalingInputState");
        source.Should().Contain("ScalePercentBox.IsEnabled = adjustTo");
        source.Should().Contain("FitPagesWideBox.IsEnabled = fitTo");
        source.Should().Contain("FitPagesTallBox.IsEnabled = fitTo");
    }

    [Fact]
    public void HeaderFooterTab_ReusesSupportedPresetAndCustomDialogConcepts()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var tab = document.Descendants(presentation + "TabItem")
            .Single(element => element.Attribute("Header")?.Value == "_Header/Footer");

        foreach (var name in new[]
        {
            "HeaderPresetBox",
            "FooterPresetBox",
            "CustomHeaderButton",
            "CustomFooterButton",
            "DifferentFirstPageBox",
            "DifferentOddEvenBox",
            "ScaleWithDocumentBox",
            "AlignWithMarginsBox"
        })
        {
            tab.Descendants()
                .Any(element => element.Attribute(x + "Name")?.Value == name)
                .Should().BeTrue($"{name} should exist on the Page Setup Header/Footer tab");
        }

        var headerPresets = tab
            .Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "HeaderPresetBox")
            .Elements(presentation + "ComboBoxItem")
            .Select(element => element.Attribute("Content")?.Value);
        var footerPresets = tab
            .Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "FooterPresetBox")
            .Elements(presentation + "ComboBoxItem")
            .Select(element => element.Attribute("Content")?.Value);

        headerPresets.Should().Contain(["Book1.xlsx, Sheet1", "Confidential, Page 1", "Date, Page 1", "File path"]);
        footerPresets.Should().Contain(["Book1.xlsx, Sheet1", "Time", "Date, Page 1", "File name"]);
    }

    [Fact]
    public void SheetTab_ExposesCurrentSelectionRangePickerButtonsForPrintRanges()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml"));
        var source = ReadPageSetupDialogSource();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var (buttonName, targetName, automationName) in new[]
        {
            ("PrintAreaPickerButton", "PrintAreaBox", "Select print area"),
            ("RowsRepeatPickerButton", "RowsRepeatBox", "Select rows to repeat"),
            ("ColumnsRepeatPickerButton", "ColumnsRepeatBox", "Select columns to repeat")
        })
        {
            var button = document.Descendants(presentation + "Button")
                .SingleOrDefault(element => element.Attribute(x + "Name")?.Value == buttonName);

            button.Should().NotBeNull($"{buttonName} should expose Excel-like picker affordance");
            button!.Attribute("Content")?.Value.Should().Be("...");
            button.Attribute("Click")?.Value.Should().Be("RangePickerButton_Click");
            button.Attribute("ToolTip")?.Value.Should().Contain("Collapse dialog");
            button.Attribute("Tag")?.Value.Should().Be(targetName);
            button.Attribute(x + "Name")?.Value.Should().Be(buttonName);
            button.Attribute("AutomationProperties.Name")?.Value.Should().Be(automationName);
        }

        source.Should().Contain("RangePickerButton_Click");
        source.Should().Contain("private readonly GridRange? _currentSelection");
        source.Should().Contain("PageSetupRangeSelectionRequest");
        source.Should().Contain("RangeSelectionRequest = CreateRangeSelectionRequest");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
        source.Should().Contain("target.Text = targetName switch");
        source.Should().Contain("selection.ToString()");
        source.Should().Contain("CellAddress.NumberToColumnName(selection.Start.Col)");
        var pickerHandlerSource = source[
            source.IndexOf("private void RangePickerButton_Click", StringComparison.Ordinal)..
            source.IndexOf("public static PageSetupRangeSelectionRequest", StringComparison.Ordinal)];
        pickerHandlerSource.Should().Contain("target.Focus()");
        pickerHandlerSource.Should().Contain("target.SelectAll()");
        pickerHandlerSource.Should().Contain("Keyboard.Focus(target)");
    }

    [Fact]
    public void PageSetupRangeSelectionRequest_UsesExcelCollapseIntent()
    {
        PageSetupDialog.CreateRangeSelectionRequest(PageSetupRangeSelectionTarget.PrintArea, " A1:C10 ")
            .Should()
            .Be(new PageSetupRangeSelectionRequest(PageSetupRangeSelectionTarget.PrintArea, "A1:C10", CollapseDialog: true));
    }

    [Fact]
    public void PageSetupDialogInvalidPrintArea_SelectsSheetTabPrintAreaBox()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml"));
        var source = ReadPageSetupDialogSource();

        xaml.Should().Contain("x:Name=\"PageSetupTabs\"");
        xaml.Should().Contain("x:Name=\"SheetTab\"");
        source.Should().Contain("FocusInvalidPrintArea();");
        source.Should().Contain("private void FocusInvalidPrintArea()");
        source.Should().Contain("PageSetupTabs.SelectedItem = SheetTab;");
        source.Should().Contain("PrintAreaBox.Focus();");
        source.Should().Contain("PrintAreaBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(PrintAreaBox);");
    }

    [Fact]
    public void PageSetupDialogInvalidPrintTitles_SelectsSheetTabInvalidTitleBox()
    {
        var source = ReadPageSetupDialogSource();

        source.Should().Contain("FocusInvalidPrintTitles();");
        source.Should().Contain("private void FocusInvalidPrintTitles()");
        source.Should().Contain("PageSetupTabs.SelectedItem = SheetTab;");
        source.Should().Contain("target.Focus();");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void PageSetupDialogInvalidPageTabNumber_SelectsPageTabInvalidBox()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml"));
        var source = ReadPageSetupDialogSource();

        xaml.Should().Contain("x:Name=\"PageTab\"");
        source.Should().Contain("FocusInvalidPageTabNumber(FirstPageNumberBox);");
        source.Should().Contain("FocusInvalidPageTabNumber(PrintQualityBox);");
        source.Should().Contain("private void FocusInvalidPageTabNumber(TextBox target)");
        source.Should().Contain("PageSetupTabs.SelectedItem = PageTab;");
        source.Should().Contain("target.Focus();");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void PageSetupDialogInvalidMargin_SelectsMarginsTabInvalidBox()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml"));
        var source = ReadPageSetupDialogSource();

        xaml.Should().Contain("x:Name=\"MarginsTab\"");
        source.Should().Contain("FocusInvalidMarginInput();");
        source.Should().Contain("FocusInvalidHeaderFooterMargin();");
        source.Should().Contain("private void FocusInvalidMarginInput()");
        source.Should().Contain("private void FocusMarginsTabTextBox(TextBox target)");
        source.Should().Contain("PageSetupTabs.SelectedItem = MarginsTab;");
        source.Should().Contain("target.Focus();");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void PageSetupDialogInvalidScaling_SelectsPageTabActiveScalingBox()
    {
        var source = ReadPageSetupDialogSource();

        source.Should().Contain("FocusInvalidScalingInput();");
        source.Should().Contain("private void FocusInvalidScalingInput()");
        source.Should().Contain("PageSetupTabs.SelectedItem = PageTab;");
        source.Should().Contain("ScalePercentBox");
        source.Should().Contain("FitPagesWideBox");
        source.Should().Contain("FitPagesTallBox");
        source.Should().Contain("target.Focus();");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void Footer_ExposesExcelPrintActionsAndPrinterOptionsAction()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml"));
        var source = ReadPageSetupDialogSource();
        var handlerSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.PageLayout.cs"));

        foreach (var content in new[] { "Print Pre_view", "_Print...", "_Options..." })
            xaml.Should().Contain($"Content=\"{content}\"");

        xaml.Should().Contain("Click=\"OptionsButton_Click\"");
        xaml.Should().NotContain("IsEnabled=\"False\"");
        xaml.Should().NotContain("not available yet");
        source.Should().Contain("PageSetupDialogAction.Options");
        source.Should().Contain("PageSetupDialogAction.PrintPreview");
        source.Should().Contain("PageSetupDialogAction.Print");
        handlerSource.Should().Contain("PageSetupDialogAction.Options");
        handlerSource.Should().Contain("ShowPageSetupPrinterOptions()");
        handlerSource.Should().Contain("PrintButton_Click(this, new RoutedEventArgs())");
    }

    [Fact]
    public void PageSetupHandler_AppliesHeaderFooterValuesReturnedByDialog()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.PageLayout.cs"));

        source.Should().Contain("new CompositeWorkbookCommand(");
        source.Should().Contain("new PageSetupDialog(sheet, SheetGrid.SelectedRange)");
        source.Should().Contain("new SetHeaderFooterCommand(");
        source.Should().Contain("dialog.FirstPageHeader");
        source.Should().Contain("dialog.EvenPageFooter");
        source.Should().Contain("dialog.ScaleHeaderFooterWithDocument");
        source.Should().Contain("dialog.AlignHeaderFooterWithMargins");
    }

    private static string ReadPageSetupDialogSource() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.RangeSelection.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.ValidationFocus.cs")));
}
