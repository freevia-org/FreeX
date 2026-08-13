using System.Windows.Automation;
using System.Windows.Controls;
using System.Xml.Linq;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for J33: the Find/Replace status TextBlock must be a polite UIA live
/// region, and every StatusLabel.Text update must additionally raise the automation
/// notifications a live region needs (AutomationProperties.Name change + LiveRegionChanged),
/// since WPF live regions are not announced by a plain Text mutation alone. Before the fix,
/// StatusLabel had no AutomationProperties.LiveSetting and every "StatusLabel.Text = ..." was a
/// bare property set with no accompanying automation notification.
/// </summary>
public sealed class TA11yFindReplaceStatusLiveRegionTests
{
    [Fact]
    public void StatusLabel_IsDeclaredAsPoliteLiveRegionInXaml()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("FindReplaceDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var statusLabel = document.Descendants(presentation + "TextBlock")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "StatusLabel");

        // AutomationProperties.LiveSetting is declared as a plain (default-namespace) attribute in
        // this project's XAML -- see MainWindow.xaml's status bar TextBlocks for the same convention.
        statusLabel.Attribute("AutomationProperties.LiveSetting")?.Value.Should().Be("Polite");
    }

    [Fact]
    public void FindNext_NoMatches_UpdatesStatusLabelAutomationNameForLiveRegionAnnouncement()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
            var dialog = new FindReplaceDialog(
                () => workbook,
                command => commandBus.Execute(workbook.Id, command),
                _ => { });
            dialog.Show();
            try
            {
                var findBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "FindBox");
                var statusLabel = DialogSourceTestSupport.GetPrivateField<TextBlock>(dialog, "StatusLabel");

                findBox.Text = "no-such-value-anywhere";
                DialogSourceTestSupport.InvokePrivateHandler(dialog, "FindNext_Click");

                statusLabel.Text.Should().Be(UiText.Get("FindReplace_NoMatchesFound"));

                // The accessible Name must be kept in sync with the visible status text -- this is
                // what a screen reader actually reads when the live region change is announced.
                AutomationProperties.GetName(statusLabel).Should().Be(statusLabel.Text);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ReplaceAll_UpdatesStatusLabelAutomationNameToMatchReplacedCountMessage()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(a1, new TextValue("foo"));
            var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
            var dialog = new FindReplaceDialog(
                () => workbook,
                command => commandBus.Execute(workbook.Id, command),
                _ => { },
                replaceMode: true,
                getCurrentSheetId: () => sheet.Id);
            dialog.Show();
            try
            {
                var statusLabel = DialogSourceTestSupport.GetPrivateField<TextBlock>(dialog, "StatusLabel");
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ReplaceFindBox").Text = "foo";
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ReplaceBox").Text = "bar";

                DialogSourceTestSupport.InvokePrivateHandler(dialog, "ReplaceAll_Click");

                statusLabel.Text.Should().Be(UiText.Format("FindReplace_ReplacedCellsStatus", 1));
                AutomationProperties.GetName(statusLabel).Should().Be(statusLabel.Text);
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
