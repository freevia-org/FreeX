using System.Printing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// The Print Preview dialog shows print-job settings twice: the Excel-style settings rail on the left
/// and a toolbar whose values the Print button actually sends. These tests pin that the rail drives
/// the toolbar (so what the user set is what prints) and that the rail gets the same selection and
/// Page Setup hookups as the File &gt; Print pane.
/// </summary>
public sealed class PrintPreviewDialogRailWiringTests
{
    [Fact]
    public void RailCopiesSidesAndCollation_UpdateTheToolbarControlsThePrintButtonSends()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = CreateDialog(hasSelection: false, showPageSetup: null);
            var rail = Rail(dialog);
            var toolbar = Toolbar(dialog);

            var railCopies = rail.Children.OfType<TextBox>().First();
            var railCombos = rail.Children.OfType<ComboBox>().ToList();
            railCopies.Text = "3";
            railCombos[2].SelectedIndex = 1; // sides: two-sided, long edge
            railCombos[3].SelectedIndex = 1; // collation: uncollated

            ToolbarControl<TextBox>(toolbar, "PrintPreview_CopiesAutomationName").Text.Should().Be("3");
            ToolbarControl<ComboBox>(toolbar, "PrintPreview_SidesAutomationName").SelectedIndex
                .Should().Be(PrintPreviewToolbarStatePlanner.SidesModeToIndex(PrintPreviewSidesMode.TwoSidedLongEdge));
            ToolbarControl<CheckBox>(toolbar, "PrintPreview_CollatedAutomationName").IsChecked.Should().BeFalse();
        });
    }

    [Fact]
    public void RailPrintSelection_IsEnabledOnlyWhenTheWorkbookHasASelection()
    {
        StaTestRunner.Run(() =>
        {
            PrintWhatItems(CreateDialog(hasSelection: true, showPageSetup: null))
                .Should().OnlyContain(item => item.IsEnabled);
            PrintWhatItems(CreateDialog(hasSelection: false, showPageSetup: null))
                .Should().Contain(item => !item.IsEnabled);
        });
    }

    [Fact]
    public void RailPageSetupLink_OpensPageSetup()
    {
        StaTestRunner.Run(() =>
        {
            var opened = 0;
            var dialog = CreateDialog(hasSelection: false, showPageSetup: () => opened++);
            var link = Rail(dialog).Children.OfType<Button>()
                .Single(button => AutomationProperties.GetAutomationId(button) == "BackstagePageSetupLink");

            link.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            opened.Should().Be(1);
        });
    }

    [Fact]
    public void ApplyPageGeometry_SendsLandscapeAndThePaperSizeToThePrinter()
    {
        var ticket = new PrintTicket();

        // Letter landscape in device-independent pixels (11in x 8.5in at 96 DPI).
        NativePrinterDeviceSettings.ApplyPageGeometry(ticket, new Size(1056, 816));

        ticket.PageOrientation.Should().Be(PageOrientation.Landscape);
        ticket.PageMediaSize!.Width.Should().Be(816);
        ticket.PageMediaSize.Height.Should().Be(1056);
    }

    [Fact]
    public void ApplyPageGeometry_KeepsPortraitForAPortraitPage_AndIgnoresAnUnusableSize()
    {
        var ticket = new PrintTicket();
        NativePrinterDeviceSettings.ApplyPageGeometry(ticket, new Size(816, 1056));
        ticket.PageOrientation.Should().Be(PageOrientation.Portrait);

        var untouched = new PrintTicket();
        NativePrinterDeviceSettings.ApplyPageGeometry(untouched, new Size(0, double.NaN));
        untouched.PageOrientation.Should().BeNull();
        untouched.PageMediaSize.Should().BeNull();
    }

    private static PrintPreviewDialog CreateDialog(bool hasSelection, Action? showPageSetup)
    {
        var sheet = new Workbook("Book1").AddSheet("Sheet1");
        var document = new FixedDocument();
        var plan = new PrintSettingsPlan(["Active sheet"]);
        return new PrintPreviewDialog(
            "Book1",
            document,
            plan,
            showPageSetup: showPageSetup,
            refreshPreviewWithSettings: _ => (document, plan),
            sheetId: sheet.Id,
            sheet: sheet,
            executeCommand: _ => { },
            fixturePrinterName: "Fixture Printer",
            hasSelection: hasSelection);
    }

    private static StackPanel Rail(PrintPreviewDialog dialog) =>
        (StackPanel)((Grid)dialog.Content).Children.OfType<ScrollViewer>().Single().Content;

    private static ToolBar Toolbar(PrintPreviewDialog dialog) =>
        ((Grid)dialog.Content).Children.OfType<ToolBar>().Single();

    private static T ToolbarControl<T>(ToolBar toolbar, string automationNameKey)
        where T : DependencyObject =>
        toolbar.Items.OfType<T>()
            .Single(control => AutomationProperties.GetName(control) == UiText.Get(automationNameKey));

    private static IReadOnlyList<ComboBoxItem> PrintWhatItems(PrintPreviewDialog dialog) =>
        Rail(dialog).Children.OfType<ComboBox>().ElementAt(1).Items.OfType<ComboBoxItem>().ToList();
}
