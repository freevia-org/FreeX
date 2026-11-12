using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// End-to-end coverage of Word's Create New Style / Modify Style results in the WPF editor: a
/// paragraph, character or linked style is created, applied, rendered, listed in the Styles gallery
/// and undone through the shared style-catalog path.
/// </summary>
public sealed class StyleCatalogAuthoringHostTests
{
    private static DocumentView CreateView(params string[] paragraphs)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        var view = new DocumentView();
        view.LoadModel(document);
        view.MoveCaretToBlockForTest(0, 0);
        return view;
    }

    private static StyleDefinition Definition(
        string name,
        StyleDefinitionKind kind,
        RunFormatting run,
        ParagraphFormatting? paragraph = null,
        bool addToGallery = true) =>
        new(name, kind, kind == StyleDefinitionKind.Character ? null : "Normal", null, run, paragraph ?? ParagraphFormatting.Default, addToGallery);

    private static void Select(DocumentView view, int startOffset, int endOffset)
    {
        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        var run = paragraph.Inlines.OfType<System.Windows.Documents.Run>().First();
        view.Selection.Select(
            run.ContentStart.GetPositionAtOffset(startOffset)!,
            run.ContentStart.GetPositionAtOffset(endOffset)!);
    }

    private static string[] SwatchNames(FrameworkElement gallery) =>
        Descendants(gallery)
            .OfType<Button>()
            .Select(button => button.ToolTip as string)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToArray();

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    [StaFact]
    public void CreateStyleAndApply_ParagraphStyle_AppliesToCaretParagraph_AndUndoesInOneStep()
    {
        var view = CreateView("First", "Second");

        var created = view.CreateStyleAndApply(Definition(
            "Callout",
            StyleDefinitionKind.Paragraph,
            new RunFormatting { Bold = true, FontFamily = "Georgia" },
            new ParagraphFormatting { Alignment = FreeW.Core.Model.TextAlignment.Center }));

        created!.Type.Should().Be(StyleType.Paragraph);
        ((Paragraph)view.Model.Blocks[0]).StyleId.Should().Be(created.Id);
        ((Paragraph)view.Model.Blocks[1]).StyleId.Should().BeNull();
        var rendered = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        rendered.TextAlignment.Should().Be(System.Windows.TextAlignment.Center);
        rendered.Inlines.OfType<System.Windows.Documents.Run>().First().FontWeight.Should().Be(FontWeights.Bold);

        view.Commands.Undo().Should().BeTrue();
        view.Model.Styles.Should().NotContainKey(created.Id);
        ((Paragraph)view.Model.Blocks[0]).StyleId.Should().BeNull();
    }

    [StaFact]
    public void CreateStyleAndApply_CharacterStyle_FormatsOnlyTheSelectedText()
    {
        var view = CreateView("Styled text");
        Select(view, 0, 6);

        var created = view.CreateStyleAndApply(Definition(
            "Code",
            StyleDefinitionKind.Character,
            new RunFormatting { Italic = true, HighlightColorHex = "#FFFF00" }));

        created!.Type.Should().Be(StyleType.Character);
        var paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.StyleId.Should().BeNull("a character style never becomes the paragraph's style");
        var styled = paragraph.Runs.Where(run => run.StyleId == created.Id).ToArray();
        string.Concat(styled.Select(run => run.Text)).Should().Be("Styled");
        styled.Should().OnlyContain(run => run.Formatting.Italic && run.Formatting.HighlightColorHex == "#FFFF00");
        paragraph.Runs.Where(run => run.StyleId != created.Id).Should().OnlyContain(run => !run.Formatting.Italic);
    }

    [StaFact]
    public void CreateStyleAndApply_CharacterStyle_WithCaretOnly_OnlyAddsItToTheCatalog()
    {
        var view = CreateView("Plain text");

        var created = view.CreateStyleAndApply(Definition(
            "Code", StyleDefinitionKind.Character, new RunFormatting { Italic = true }));

        view.Model.Styles.Should().ContainKey(created!.Id);
        ((Paragraph)view.Model.Blocks[0]).Runs.Should().OnlyContain(run => run.StyleId == null && !run.Formatting.Italic);
    }

    [StaFact]
    public void LinkedStyle_AppliesAsParagraphWithCaret_AndAsCharacterWithSelection()
    {
        var view = CreateView("Linked text", "Other");
        var created = view.CreateStyleAndApply(Definition(
            "Callout", StyleDefinitionKind.Linked, new RunFormatting { Bold = true }))!;
        var characterId = created.LinkedStyleId!;
        ((Paragraph)view.Model.Blocks[0]).StyleId.Should().Be(created.Id, "with a caret the paragraph half applies");

        view.MoveCaretToBlockForTest(1, 0);
        var second = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ElementAt(1);
        var run = second.Inlines.OfType<System.Windows.Documents.Run>().First();
        view.Selection.Select(run.ContentStart, run.ContentStart.GetPositionAtOffset(3)!);
        view.ApplyNamedStyle(created.Id);

        var other = (Paragraph)view.Model.Blocks[1];
        other.StyleId.Should().BeNull("with selected text the character half applies");
        string.Concat(other.Runs.Where(r => r.StyleId == characterId).Select(r => r.Text)).Should().Be("Oth");
    }

    [StaFact]
    public void ModifyStyle_RepaintsStyledParagraphs_AndRenamesCustomStyle()
    {
        var view = CreateView("Body");
        var created = view.CreateStyleAndApply(Definition(
            "Callout", StyleDefinitionKind.Paragraph, RunFormatting.Default))!;

        var updated = view.ModifyStyle(created.Id, Definition(
            "Aside", StyleDefinitionKind.Paragraph, new RunFormatting { Italic = true }));

        updated!.Name.Should().Be("Aside");
        updated.Id.Should().Be(created.Id);
        view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First()
            .Inlines.OfType<System.Windows.Documents.Run>().First().FontStyle.Should().Be(FontStyles.Italic);

        view.Commands.Undo().Should().BeTrue();
        view.Model.Styles[created.Id].Name.Should().Be("Callout");
    }

    [StaFact]
    public void UpdateStyleToMatchSelection_TakesTheSelectionFormatting()
    {
        var view = CreateView("Body");
        var created = view.CreateStyleAndApply(Definition(
            "Callout", StyleDefinitionKind.Paragraph, RunFormatting.Default))!;
        Select(view, 0, 4);
        view.Selection.ApplyPropertyValue(System.Windows.Documents.TextElement.FontWeightProperty, FontWeights.Bold);
        Select(view, 0, 4);

        var updated = view.UpdateStyleToMatchSelection(created.Id);

        updated!.Run.Bold.Should().BeTrue();
        updated.Paragraph.ListKind.Should().Be(ListKind.None);
        view.Commands.Undo().Should().BeTrue();
        view.Model.Styles[created.Id].Run.Bold.Should().BeFalse();
    }

    [StaFact]
    public void DeleteStyle_ReturnsParagraphsToDefault_AndUndoRestoresThem()
    {
        var view = CreateView("Body");
        var created = view.CreateStyleAndApply(Definition(
            "Callout", StyleDefinitionKind.Linked, new RunFormatting { Bold = true }))!;

        view.DeleteParagraphStyle(created.Id).Should().BeTrue();

        view.Model.Styles.Should().NotContainKey(created.Id).And.NotContainKey(created.LinkedStyleId!);
        ((Paragraph)view.Model.Blocks[0]).StyleId.Should().BeNull();

        view.Commands.Undo().Should().BeTrue();
        ((Paragraph)view.Model.Blocks[0]).StyleId.Should().Be(created.Id);
        view.Model.Styles.Should().ContainKey(created.LinkedStyleId!);
    }

    [StaFact]
    public void StylesGallery_ListsCustomParagraphAndCharacterStyles_ButNotLinkedHalvesOrHiddenStyles()
    {
        var view = CreateView("Body");
        view.CreateStyleAndApply(Definition("Callout", StyleDefinitionKind.Linked, RunFormatting.Default));
        view.CreateStyleAndApply(Definition("Code", StyleDefinitionKind.Character, RunFormatting.Default));
        view.CreateStyleAndApply(Definition("Hidden", StyleDefinitionKind.Paragraph, RunFormatting.Default, addToGallery: false));

        var names = SwatchNames(StylesGallery.Build(view));

        names.Should().Contain(["Normal", "Callout", "Code"]);
        names.Should().NotContain(["Callout Char", "Hidden"]);
    }

    [StaFact]
    public void StylesGallery_FollowsTheCatalog_AfterCreateRemoveFromGalleryAndUndo()
    {
        var view = CreateView("Body");
        var gallery = StylesGallery.Build(view);
        gallery.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        SwatchNames(gallery).Should().NotContain("Callout");

        var created = view.CreateStyleAndApply(Definition(
            "Callout", StyleDefinitionKind.Paragraph, RunFormatting.Default))!;
        SwatchNames(gallery).Should().Contain("Callout");

        view.SetStyleGalleryVisibility(created.Id, showInGallery: false)!.QuickFormat.Should().BeFalse();
        SwatchNames(gallery).Should().NotContain("Callout");
        view.Model.Styles.Should().ContainKey(created.Id, "removing from the gallery keeps the style");

        view.Commands.Undo().Should().BeTrue();
        SwatchNames(gallery).Should().Contain("Callout");
    }

    [StaFact]
    public void StylesGallery_SwatchContextMenu_IsPlannedBySharedPlanner()
    {
        var view = CreateView("Body");
        var created = view.CreateStyleAndApply(Definition(
            "Callout", StyleDefinitionKind.Paragraph, RunFormatting.Default))!;

        var custom = StyleGalleryPlanner.BuildContextMenu(view.Model, created.Id);
        var builtIn = StyleGalleryPlanner.BuildContextMenu(view.Model, "Normal");

        custom.Select(item => item.Command).Should().Equal(Enum.GetValues<StyleGalleryContextCommand>());
        custom.Should().OnlyContain(item => item.IsEnabled);
        custom[0].Label.Should().Contain("Callout");
        builtIn.Single(item => item.Command == StyleGalleryContextCommand.Delete).IsEnabled.Should().BeFalse();
        builtIn.Single(item => item.Command == StyleGalleryContextCommand.RemoveFromGallery).IsEnabled.Should().BeFalse();
        builtIn.Single(item => item.Command == StyleGalleryContextCommand.Modify).IsEnabled.Should().BeTrue();

        var swatch = Descendants(StylesGallery.Build(view)).OfType<Button>()
            .First(button => button.ToolTip as string == "Callout");
        swatch.ContextMenu.Should().NotBeNull();
    }

    private static T Find<T>(DependencyObject root, string automationId) where T : DependencyObject =>
        Descendants(root).OfType<T>().Single(element =>
            System.Windows.Automation.AutomationProperties.GetAutomationId(element) == automationId);

    [StaFact]
    public void StyleDefinitionDialog_ProjectsSession_AndControlsDriveTheResult()
    {
        var document = TextDocument.CreateEmpty();
        StyleManager.CreateStyle(
            document, "Code", null, new RunFormatting { FontFamily = "Consolas" },
            ParagraphFormatting.Default, null, StyleType.Character);
        var session = StyleDefinitionDialogPlanner.CreateNewSession(document, "Normal");
        var view = StyleDefinitionDialog.Create(owner: null, document, session);
        var window = view.Window;

        window.Title.Should().Be(StyleDefinitionDialogText.NewTitle);
        var name = Find<TextBox>(window, StyleDefinitionDialogSession.NameAutomationId);
        var kind = Find<ComboBox>(window, StyleDefinitionDialogSession.KindAutomationId);
        var basedOn = Find<ComboBox>(window, StyleDefinitionDialogSession.BasedOnAutomationId);
        var nextStyle = Find<ComboBox>(window, StyleDefinitionDialogSession.NextStyleAutomationId);
        var alignment = Find<ComboBox>(window, StyleDefinitionDialogSession.AlignmentAutomationId);
        var highlight = Find<ComboBox>(window, StyleDefinitionDialogSession.HighlightAutomationId);
        var bold = Find<System.Windows.Controls.Primitives.ToggleButton>(window, StyleDefinitionDialogSession.BoldAutomationId);
        var gallery = Find<CheckBox>(window, StyleDefinitionDialogSession.AddToGalleryAutomationId);
        var description = Find<TextBlock>(window, StyleDefinitionDialogSession.DescriptionAutomationId);
        var preview = (TextBlock)Find<Border>(window, StyleDefinitionDialogSession.PreviewAutomationId).Child;

        kind.Items.Count.Should().Be(3);
        kind.SelectedIndex.Should().Be((int)StyleDefinitionKind.Paragraph);
        ((StyleDialogStyleChoice)basedOn.SelectedItem).Value.Should().Be("Normal");
        nextStyle.IsEnabled.Should().BeTrue();
        gallery.IsChecked.Should().BeTrue();
        preview.Text.Should().Be(StyleDefinitionDialogText.PreviewSample);

        name.Text = "Keyword";
        kind.SelectedIndex = (int)StyleDefinitionKind.Character;

        nextStyle.IsEnabled.Should().BeFalse("a character style has no following-paragraph style");
        alignment.IsEnabled.Should().BeFalse();
        basedOn.Items.Cast<StyleDialogStyleChoice>().Select(choice => choice.Key)
            .Should().Equal(StyleDefinitionDialogText.NoBasedOn, "Code");

        basedOn.SelectedIndex = 1;
        preview.FontFamily.Source.Should().Be("Consolas", "the preview resolves through the based-on style");
        bold.IsChecked = true;
        bold.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        preview.FontWeight.Should().Be(FontWeights.Bold);
        highlight.SelectedIndex = session.HighlightColors.ToList().FindIndex(choice => choice.Hex == "#FFFF00");
        preview.Background.Should().BeOfType<System.Windows.Media.SolidColorBrush>();
        description.Text.Should().Contain("Bold").And.Contain("Based on: Code");

        view.TryAccept().Should().BeTrue();
        view.Result!.Name.Should().Be("Keyword");
        view.Result.Kind.Should().Be(StyleDefinitionKind.Character);
        view.Result.BasedOnId.Should().Be("Code");
        view.Result.Run.Bold.Should().BeTrue();
        view.Result.Run.HighlightColorHex.Should().Be("#FFFF00");
        window.Close();
    }

    [StaFact]
    public void StyleDefinitionDialog_ModifyBuiltIn_LocksNameAndType()
    {
        var document = TextDocument.CreateEmpty();
        BuiltInStyles.EnsureSeeded(document, "Heading1");
        var view = StyleDefinitionDialog.Create(
            owner: null,
            document,
            StyleDefinitionDialogPlanner.CreateModifySession(document, document.Styles["Heading1"]));

        view.Window.Title.Should().Be(StyleDefinitionDialogText.ModifyTitle);
        Find<TextBox>(view.Window, StyleDefinitionDialogSession.NameAutomationId).IsReadOnly.Should().BeTrue();
        Find<ComboBox>(view.Window, StyleDefinitionDialogSession.KindAutomationId).IsEnabled.Should().BeFalse();
        view.TryAccept().Should().BeTrue();
        view.Result!.Name.Should().Be(document.Styles["Heading1"].Name);
        view.Window.Close();
    }

    [Fact]
    public void RibbonCommands_OpenTheWordStyleDefinitionDialog_AndRouteThroughTheSharedCoordinator()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var host = Path.Combine(root, "freew", "FreeW.App.Host");
        var commands = File.ReadAllText(Path.Combine(host, "Ribbon", "FreeWRibbonCommands.cs"));
        var dialog = File.ReadAllText(Path.Combine(host, "StyleDefinitionDialog.cs"));
        var partial = File.ReadAllText(Path.Combine(host, "Editing", "DocumentView.StyleCatalog.cs"));

        commands.Should().Contain("StyleDefinitionDialog.AskNew(owner, editor.Model, editor.CurrentParagraphStyleId)")
            .And.Contain("editor.CreateStyleAndApply(def)")
            .And.Contain("StyleDefinitionDialog.AskModify(owner, editor.Model, existing)")
            .And.Contain("editor.ModifyStyle(mod.StyleId, def)");
        dialog.Should().Contain("StyleDefinitionDialogPlanner.CreateNewSession(")
            .And.Contain("StyleDefinitionDialogPlanner.CreateModifySession(")
            .And.Contain("session.PlanAcceptance()")
            .And.Contain("FontDialog.Prompt(owner, session.Run)")
            .And.Contain("ParagraphBreaksDialog.Prompt(owner, session.Paragraph)")
            .And.Contain("TabsDialog.Prompt(owner, session.Paragraph.TabStops")
            .And.Contain("BordersAndShadingDialog.Prompt(owner, session.Paragraph")
            .And.NotContain("StyleManager.");
        partial.Should().Contain("_editingSession.Styles.CreateStyle(")
            .And.Contain("_editingSession.Styles.ModifyStyle(")
            .And.Contain("_editingSession.Styles.UpdateStyleToMatch(")
            .And.NotContain("StyleManager.");
    }
}
