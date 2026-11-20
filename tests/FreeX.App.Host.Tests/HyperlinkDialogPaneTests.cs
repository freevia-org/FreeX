using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Excel's Insert Hyperlink panes: the Link to bar follows Excel's order and each pane composes
/// its own kind of target (a sheet location, a new document's full path, a mailto with subject).
/// </summary>
public sealed class HyperlinkDialogPaneTests
{
    [Fact]
    public void LinkTo_ListsExcelsFourPanesInOrder()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new HyperlinkDialog();
            try
            {
                var linkTypes = DialogSourceTestSupport.GetPrivateField<ListBox>(dialog, "_linkTypes");
                linkTypes.Items.Cast<string>().Should().Equal(
                    UiText.Get("Hyperlink_LinkTypeExistingFileOrWebPage"),
                    UiText.Get("Hyperlink_LinkTypePlaceInThisDocument"),
                    UiText.Get("Hyperlink_LinkTypeCreateNewDocument"),
                    UiText.Get("Hyperlink_LinkTypeEmailAddress"));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void PlaceInThisDocument_EditsAnExistingLinkOnItsSheetAndCell()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            workbook.AddSheet("Sheet1");
            workbook.AddSheet("My Data");
            var dialog = new HyperlinkDialog("'My Data'!C3", "Go");
            try
            {
                dialog.ApplyPrefill(
                    new HyperlinkDialogPrefill(HyperlinkTargetKind.PlaceInThisDocument, "'My Data'!C3", "Go", "", ""),
                    workbook);
                var target = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "_targetBox");
                target.Text.Should().Be("C3");

                target.Text = "D9";
                var result = dialog.ComposeResultForTest();

                result!.LinkType.Should().Be(HyperlinkTargetKind.PlaceInThisDocument);
                result.Target.Should().Be("'My Data'!D9");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void EmailAddress_ComposesAMailtoWithItsSubject()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new HyperlinkDialog();
            try
            {
                dialog.ApplyPrefill(
                    new HyperlinkDialogPrefill(HyperlinkTargetKind.EmailAddress, "mailto:a@b.com?subject=Q3%20report", "", "", ""),
                    workbook: null);
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "_subjectBox").Text.Should().Be("Q3 report");

                var result = dialog.ComposeResultForTest();

                result!.Target.Should().Be("mailto:a@b.com?subject=Q3%20report");
                result.DisplayText.Should().Be("a@b.com");
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
