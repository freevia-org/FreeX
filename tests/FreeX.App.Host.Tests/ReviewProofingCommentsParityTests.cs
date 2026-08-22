using System.Linq;
using FluentAssertions;
using Free.Shared.Ribbon;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Host.Tests;

public sealed class ReviewProofingCommentsParityTests
{
    // After the declarative-ribbon cutover (commit a02d5c06d) the Review-tab proofing/comment/note
    // commands live in the single-source declarative ribbon model (FreeXRibbon.Build()) rather than in
    // hand-authored MainWindow.xaml <Button> markup. The old assertions on per-button AutomationId,
    // HelpText and Click handlers no longer have a declarative meaning, so parity is now asserted
    // against the declarative catalog: each command must appear in its expected Review-tab group
    // (by group header) with the expected access-key (KeyTip).
    [Theory]
    [InlineData("Proofing", "Spelling", "SP")]
    [InlineData("Proofing", "Check Performance", "CP")]
    [InlineData("Proofing", "Workbook Statistics", "W")]
    [InlineData("Accessibility", "Check Accessibility", "CA")]
    [InlineData("Comments", "New Comment", "CM")]
    [InlineData("Comments", "Delete Comment", "XC")]
    [InlineData("Comments", "Previous Comment", "PC")]
    [InlineData("Comments", "Next Comment", "JC")]
    [InlineData("Comments", "Show Comments", "SC")]
    [InlineData("Notes", "New Note", "O")]
    [InlineData("Notes", "Edit Note", "E")]
    [InlineData("Notes", "Delete Note", "D")]
    [InlineData("Notes", "Previous Note", "PN")]
    [InlineData("Notes", "Next Note", "N")]
    [InlineData("Notes", "Show Notes", "H")]
    public void ReviewProofingCommentAndNoteCommands_ArePresentWithKeyTipInExpectedGroup(
        string groupName,
        string commandName,
        string keyTip)
    {
        var reviewTab = FreeXRibbon.Build().Tabs
            .SingleOrDefault(tab => string.Equals(tab.Header, "Review", StringComparison.Ordinal));
        reviewTab.Should().NotBeNull("the Review ribbon tab should be present");

        var group = reviewTab!.Groups
            .SingleOrDefault(g => string.Equals(g.Header, groupName, StringComparison.Ordinal));
        group.Should().NotBeNull($"the Review/{groupName} ribbon group should be present");

        var command = group!.Controls
            .SingleOrDefault(control => string.Equals(control.Label, commandName, StringComparison.Ordinal));
        command.Should().NotBeNull($"the Review/{groupName}/{commandName} command should be present");
        command!.KeyTip.Should().Be(keyTip);
    }

    [Fact]
    public void AccessibilityCheckerDialog_CleanOkClosesWithoutNavigationResult()
    {
        var source = DialogSourceTestSupport.ReadHostSources("AccessibilityCheckerDialog.cs");
        var cleanStart = source.IndexOf("if (_plan.State == AccessibilityCheckerDialogState.Clean)", StringComparison.Ordinal);
        var issueStart = source.IndexOf("var body = new Grid();", cleanStart, StringComparison.Ordinal);
        var cleanContent = source[
            cleanStart..
            issueStart];

        cleanContent.Should().Contain("_messageBox.Text = _plan.CleanMessage");
        cleanContent.Should().Contain("_goToButton.Visibility = Visibility.Collapsed");
        cleanContent.Should().NotContain("DialogResult = true");
    }
}
