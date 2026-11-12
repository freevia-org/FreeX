using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Free.Shared.AppServices;
using Free.Shared.Shell.Wpf;
using FreeW.App.Host.Backstage;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Options;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// The WPF side of File &gt; New templates: the gallery renderer draws what the shared planner says,
/// and FileCommands turns a tile click into a new untitled document.
/// </summary>
public sealed class BackstageTemplateGalleryTests : IDisposable
{
    private static readonly BackstageVisualKit Kit =
        new(Color.FromRgb(0x2B, 0x57, 0x9A), tileWidth: 150, tileHeight: 190);

    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "FreeW.BackstageTemplateGalleryTests-" + Guid.NewGuid().ToString("N"));

    public BackstageTemplateGalleryTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [StaFact]
    public void Renderer_DrawsATileWithMiniaturePerTemplateAndInvokesIt()
    {
        var invoked = new List<string>();
        var spec = FreeWTemplateGalleryPlanner.Build(
            () => invoked.Add("blank"),
            id => invoked.Add(id),
            path => invoked.Add(path),
            [new PersonalTemplateEntry(@"C:\Templates\Invoice.dotx", "Invoice")],
            @"C:\Templates");

        var pane = BackstageTemplateGalleryRenderer.Build(Kit, spec, 150, 190);

        var texts = Descendants<TextBlock>(pane).Select(text => text.Text).ToList();
        texts.Should().Contain(["New", "Featured", "Personal", "Templates folder: C:\\Templates"]);
        texts.Should().NotContain(text => text.Contains("not available", StringComparison.OrdinalIgnoreCase));

        var tiles = Descendants<Button>(pane).ToList();
        tiles.Select(AutomationProperties.GetName).Should().Equal(
            "Blank document", "Business letter", "Resume", "Report", "Meeting notes", "Memo", "Invoice");
        tiles.Select(AutomationProperties.GetAutomationId).Should().OnlyHaveUniqueItems();
        AutomationProperties.GetAutomationId(tiles[3]).Should().Be("BackstageTemplate_report");

        // Each content template shows a miniature of its first page; the blank page stays blank.
        Descendants<Canvas>(tiles[0]).Single().Children.Count.Should().Be(0);
        Descendants<Canvas>(tiles[3]).Single().Children.Count.Should().BeGreaterThan(3);
        Descendants<Canvas>(tiles[4]).Single().Children.OfType<System.Windows.Shapes.Line>()
            .Should().NotBeEmpty("the meeting-notes tile shows its details table");

        foreach (var tile in tiles)
            tile.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        invoked.Should().Equal(
            "blank", "business-letter", "resume", "report", "meeting-notes", "memo", @"C:\Templates\Invoice.dotx");
    }

    [StaFact]
    public void Renderer_ShowsEmptyPersonalStateInsteadOfTiles()
    {
        var spec = FreeWTemplateGalleryPlanner.Build(() => { }, _ => { }, _ => { }, [], _tempDir);

        var pane = BackstageTemplateGalleryRenderer.Build(Kit, spec, 150, 190);

        Descendants<TextBlock>(pane).Select(text => text.Text)
            .Should().Contain(text => text.StartsWith("No personal templates yet."));
        Descendants<WrapPanel>(pane).Should().ContainSingle("only the Featured row has tiles");
    }

    [StaFact]
    public void FileCommands_NewFromBuiltInTemplate_LoadsAnUntitledDocument()
    {
        var (file, editor) = NewHarness();

        file.NewFromBuiltInTemplate(BuiltInDocumentTemplates.ReportId).Should().BeTrue();

        editor.Model.PlainText.Should().Contain("Executive Summary").And.Contain("Contents");
        file.CurrentPath.Should().BeNull();
        file.IsDirty.Should().BeFalse();
        editor.CurrentFileName.Should().BeNull();

        file.NewFromBuiltInTemplate("no-such-template").Should().BeFalse();
        editor.Model.PlainText.Should().Contain("Executive Summary", "an unknown id must not replace the document");
    }

    [StaFact]
    public void FileCommands_PersonalTemplate_NewCreatesADocumentAndOpenEditsTheTemplate()
    {
        var (file, editor) = NewHarness();
        var templatePath = Path.Combine(_tempDir, "House Style.dotx");
        using (var stream = File.Create(templatePath))
            DocxFileAdapter.Dotx().Save(BuiltInDocumentTemplates.Create(BuiltInDocumentTemplates.MemoId)!, stream);

        file.NewFromTemplateFile(templatePath).Should().BeTrue();

        editor.Model.PlainText.Should().Contain("Memorandum");
        file.CurrentPath.Should().BeNull("a document created from a template is untitled");
        file.RecentEntries.Select(entry => entry.Path).Should().NotContain(templatePath);

        // Saving the new document writes a .docx next to the untouched template.
        var saved = Path.Combine(_tempDir, "Memo to team.docx");
        var templateBytes = File.ReadAllBytes(templatePath);
        file.SaveCopyToPath(saved).Should().BeTrue();
        File.ReadAllBytes(templatePath).Should().Equal(templateBytes);

        // File > Open (here: the recent-list route, which shares the Open semantics) edits the template.
        file.OpenRecentPath(templatePath).Should().BeTrue();
        file.CurrentPath.Should().Be(templatePath);
    }

    [StaFact]
    public void FileCommands_PersonalTemplatesFolder_FollowsOptions()
    {
        var options = new FreeWOptions();
        var (file, _) = NewHarness(options);

        file.PersonalTemplatesFolderPath.Should().Be(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            PersonalTemplatesFolder.DefaultFolderName));

        options.PersonalTemplatesFolder = _tempDir;
        file.PersonalTemplatesFolderPath.Should().Be(_tempDir);
    }

    private (FileCommands File, DocumentView Editor) NewHarness(FreeWOptions? options = null)
    {
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var file = new FileCommands(
            window,
            editor,
            onChanged: () => { },
            options: options,
            loadRecentFilesStore: () => RecentFilesStore.Load(Path.Combine(_tempDir, "recent.json")));
        return (file, editor);
    }

    private static IEnumerable<T> Descendants<T>(object root)
        where T : DependencyObject
    {
        if (root is not DependencyObject node)
            yield break;

        foreach (var child in LogicalTreeHelper.GetChildren(node))
        {
            if (child is T match)
                yield return match;
            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }
}
