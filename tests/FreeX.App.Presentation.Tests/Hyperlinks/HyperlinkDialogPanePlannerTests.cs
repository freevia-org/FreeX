using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Hyperlinks;

public sealed class HyperlinkDialogPanePlannerTests
{
    [Fact]
    public void LinkTypeOrder_FollowsExcel() =>
        HyperlinkDialogPanePlanner.LinkTypeOrder.Should().Equal(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            HyperlinkTargetKind.PlaceInThisDocument,
            HyperlinkTargetKind.CreateNewDocument,
            HyperlinkTargetKind.EmailAddress);

    [Theory]
    [InlineData("Sheet1", false, "B2", "Sheet1!B2")]
    [InlineData("My Data", false, "", "'My Data'!A1")]
    [InlineData("Totals", true, "B2", "Totals")]
    public void ComposeDocumentLocation_QuotesSheetsAndUsesNamesAsIs(string name, bool isName, string reference, string expected) =>
        HyperlinkDialogPanePlanner.ComposeDocumentLocation(new HyperlinkDocumentPlace(name, isName), reference)
            .Should().Be(expected);

    [Theory]
    [InlineData("'My Data'!C3", "My Data", "C3")]
    [InlineData("#Sheet1!A5", "Sheet1", "A5")]
    [InlineData("Totals", "Totals", "A1")]
    public void SplitDocumentLocation_RecoversSheetAndReference(string target, string place, string reference) =>
        HyperlinkDialogPanePlanner.SplitDocumentLocation(target).Should().Be((place, reference));

    [Fact]
    public void EmailTargets_RoundTripTheSubject()
    {
        var target = HyperlinkDialogPanePlanner.ComposeEmailTarget("a@b.com", "Q3 & Q4");
        target.Should().Be("mailto:a@b.com?subject=Q3%20%26%20Q4");
        HyperlinkDialogPanePlanner.SplitEmailTarget(target).Should().Be(("a@b.com", "Q3 & Q4"));
        HyperlinkDialogPanePlanner.ComposeEmailTarget("mailto:a@b.com", "").Should().Be("mailto:a@b.com");
    }

    [Fact]
    public void ListFolder_PutsFoldersFirstAndSkipsHiddenFiles()
    {
        var root = Directory.CreateTempSubdirectory("freex-hyperlink-pane-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "zeta"));
            File.WriteAllText(Path.Combine(root.FullName, "alpha.txt"), "");
            // Dot-files are hidden on Linux/macOS; the attribute hides it on Windows.
            var hidden = Path.Combine(root.FullName, ".hidden.txt");
            File.WriteAllText(hidden, "");
            File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);

            var entries = HyperlinkDialogPanePlanner.ListFolder(root.FullName);

            entries.Select(entry => (entry.Name, entry.IsFolder)).Should().Equal(("zeta", true), ("alpha.txt", false));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ComposeNewDocumentPath_JoinsTheFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Docs");
        HyperlinkDialogPanePlanner.ComposeNewDocumentPath(folder, "Plan.xlsx").Should().Be(Path.Combine(folder, "Plan.xlsx"));
        HyperlinkDialogPanePlanner.ComposeNewDocumentPath(folder, " ").Should().BeEmpty();
    }
}
