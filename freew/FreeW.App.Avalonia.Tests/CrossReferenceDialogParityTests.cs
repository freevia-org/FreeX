using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class CrossReferenceDialogParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Uses_Wpf_list_geometry_actions_and_modal_validation_contract()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new CrossReferenceDialog(TextDocument.CreateEmpty());
            try
            {
                var lists = dialog.GetLogicalDescendants().OfType<ListBox>().ToArray();
                lists.Should().HaveCount(3);
                lists.Select(list => (list.MinWidth, list.Height)).Should().Equal(
                    (150, 170),
                    (180, 170),
                    (300, 200));
                dialog.GetLogicalDescendants().OfType<ComboBox>().Should().BeEmpty();
                dialog.GetLogicalDescendants().OfType<TextBlock>()
                    .Should().NotContain(text => text.Text == CrossReferenceDialogPlanner.MissingTargetMessage);

                var hyperlink = dialog.GetLogicalDescendants().OfType<CheckBox>().Single();
                hyperlink.IsChecked.Should().BeTrue();
                hyperlink.Height.Should().Be(18);

                var buttons = dialog.GetLogicalDescendants().OfType<Button>()
                    .Where(button => button.GetType() == typeof(Button))
                    .ToArray();
                buttons.Select(button => button.Content?.ToString()).Should().Equal("OK", "Cancel");
                buttons.Should().OnlyContain(button => button.MinWidth == 80);
                buttons.Single(button => button.IsDefault).Content.Should().Be("OK");
                buttons.Single(button => button.IsCancel).Content.Should().Be("Cancel");
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Populates_heading_and_note_targets_through_the_shared_planner()
    {
        await Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph("First") { StyleId = "Heading1" });
            var noteParagraph = new Paragraph();
            noteParagraph.Runs.Add(Run.FootnoteReference(1));
            document.Blocks.Add(noteParagraph);
            document.Footnotes[1] = new Footnote(1, "Note");

            var dialog = new CrossReferenceDialog(document);
            try
            {
                var typeList = dialog.GetLogicalDescendants().OfType<ListBox>().First();
                var targetList = dialog.GetLogicalDescendants().OfType<ListBox>().Last();

                targetList.ItemCount.Should().Be(
                    CrossReferenceDialogPlanner.BuildTargetChoices(document, CrossRefType.Heading).Count);
                targetList.SelectedIndex.Should().Be(0);

                typeList.SelectedIndex = CrossReferenceDialogPlanner.BuildTypeChoices()
                    .Select((choice, index) => (choice, index))
                    .Single(pair => pair.choice.Type == CrossRefType.Footnote)
                    .index;

                targetList.ItemCount.Should().Be(
                    CrossReferenceDialogPlanner.BuildTargetChoices(document, CrossRefType.Footnote).Count);
                targetList.SelectedIndex.Should().Be(0);
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void Wpf_authority_and_Avalonia_consumer_keep_the_same_surface_contract()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "CrossReferenceDialog.cs"));
        var avaloniaSource = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "ReferencesDialogs.cs"));
        var avalonia = avaloniaSource[..avaloniaSource.IndexOf("internal sealed class SourceConflictResolutionDialog", StringComparison.Ordinal)];

        wpf.Should().Contain("CrossReferenceDialogSession")
            .And.Contain("MinWidth = 150")
            .And.Contain("MinWidth = 180")
            .And.Contain("MinWidth = 300")
            .And.Contain("DialogMessageHelper.ShowWarning(");
        avalonia.Should().Contain("private readonly ListBox _typeList")
            .And.Contain("private readonly ListBox _insertAsList")
            .And.Contain("private readonly ListBox _targetList")
            .And.Contain("CrossReferenceDialogSession")
            .And.Contain("AvaloniaUserMessageDialog.ShowWarningAsync(")
            .And.NotContain("private readonly ComboBox _typeBox")
            .And.NotContain("private readonly TextBlock _status");
    }
}
