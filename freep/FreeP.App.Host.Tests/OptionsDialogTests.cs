using System.IO;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

/// <summary>
/// R128: FreeP's Backstage "Options" pane had no "Edit options…" link on either shell — the resource
/// text was never authored (SisterBackstagePaneTextResources.BuildFreeP omitted OptionsEditText) and
/// neither host wired an Edit callback into BuildOptionsPaneSpec, so the shared composer's
/// `if (!string.IsNullOrWhiteSpace(spec.EditText) && spec.Edit is not null)` guard never rendered a link.
/// This suite proves the WPF Backstage pane now exposes that link and that the new OptionsDialog itself
/// parses/normalizes correctly. The Avalonia counterpart lives in
/// FreeP.App.Avalonia.Tests/OptionsDialogTests.cs (BOTH-SHELLS rule).
/// </summary>
public sealed class OptionsDialogTests
{
    [StaFact]
    public void R128_BackstageOptionsPane_ExposesEditOptionsLink()
    {
        var window = new MainWindow(new FreePOptions());
        window.ActivateBackstageEntryForTests("Options").Should().BeTrue();

        var content = window.CurrentBackstagePaneContentForTests;
        content.Should().NotBeNull();

        var buttons = FindButtons((DependencyObject)content!);
        buttons.Should().Contain(button => Equals(
            button.Content,
            FreePBackstagePaneTextCatalog.Descriptor.OptionsEditText!.FallbackText));
    }

    [Fact]
    public void BackstageOptionsPane_UsesCatalogLabelWithoutHostFallback()
    {
        var source = File.ReadAllText(TestWorkspaceFileLocator.Find(
            "freep", "FreeP.App.Host", "Backstage", "BackstageView.cs"));

        source.Should().Contain("Panes.BuildOptionsPane(PanePlans.BuildOptionsPane(");
        source.Should().NotContain("Edit options");
    }

    [StaFact]
    public void R128_BackstageOptionsPane_StillRendersReadOnlySummaryFields()
    {
        // No-regression sibling: the read-only field summary (RecentFilesCap / DefaultSaveFormat /
        // UiLanguage / data-folder rows) that BuildOptionsPaneSpec already rendered before this fix
        // must still be present alongside the new edit link, not replaced by it.
        var options = new FreePOptions { RecentFilesCap = 12 };
        var window = new MainWindow(options);
        window.ActivateBackstageEntryForTests("Options").Should().BeTrue();

        var content = window.CurrentBackstagePaneContentForTests;
        var textBlocks = FindTextBlocks((DependencyObject)content!);
        textBlocks.Should().Contain(tb => tb.Text != null && tb.Text.Contains("12"));
    }

    [StaFact]
    public void R128_OptionsDialog_Accept_NormalizesAndReturnsResult()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var dialog = new OptionsDialog(owner, new FreePOptions { RecentFilesCap = 5 });
            dialog.RecentFilesCapForTest.Text = "20";
            dialog.UiLanguageForTest.Text = "de-DE";

            dialog.AcceptForTest();

            dialog.Result.RecentFilesCap.Should().Be(20);
            dialog.Result.UiLanguage.Should().Be("de-DE");
            dialog.Result.DefaultSaveFormat.Should().Be(FreePOptions.FxpDefaultFormat);
        }
        finally
        {
            owner.Close();
        }
    }

    [StaFact]
    public void R128_OptionsDialog_Accept_RejectsOutOfRangeCapAndShowsStatus()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var dialog = new OptionsDialog(owner, new FreePOptions());
            dialog.RecentFilesCapForTest.Text = "not-a-number";

            dialog.AcceptForTest();

            dialog.StatusForTest.Visibility.Should().Be(Visibility.Visible);
        }
        finally
        {
            owner.Close();
        }
    }

    [Fact]
    public void R128_EditFlow_AppliesLiveAndPersists()
    {
        // Mirrors MainWindow.OpenOptions without opening a real modal: the dialog produces a normalized
        // result, the host copies it onto the live options instance (so the file session/Program see the new
        // cap/language immediately) and saves via the shared ApplicationOptionsStore.
        using var temporaryDirectory = new TestTemporaryDirectory("FreeP.OptionsDialogTests-");
        {
            var path = Path.Combine(temporaryDirectory.Path, "settings.json");
            var store = Free.Shared.AppServices.ApplicationOptionsStore<FreePOptions>.ForPath(path);
            var live = new FreePOptions { RecentFilesCap = FreePOptions.DefaultRecentFilesCap };
            var runtime = new FreePOptionsRuntimeSession(live);

            var edited = OptionsDialogPlanner.BuildResult(recentFilesCap: 3, format: null, uiLanguage: "uk-UA");
            var outcome = runtime.ApplyAndPersist(edited, _ => store.Save(live));
            outcome.Persisted.Should().BeTrue();

            live.RecentFilesCap.Should().Be(3);
            var reloaded = Free.Shared.AppServices.ApplicationOptionsStore<FreePOptions>.ForPath(path).Load();
            reloaded.RecentFilesCap.Should().Be(3);
            reloaded.UiLanguage.Should().Be("uk-UA");
        }
    }

    private static List<Button> FindButtons(DependencyObject root)
    {
        var results = new List<Button>();
        Walk(root, o =>
        {
            if (o is Button button)
                results.Add(button);
        });
        return results;
    }

    private static List<TextBlock> FindTextBlocks(DependencyObject root)
    {
        var results = new List<TextBlock>();
        Walk(root, o =>
        {
            if (o is TextBlock tb)
                results.Add(tb);
        });
        return results;
    }

    private static void Walk(DependencyObject root, Action<DependencyObject> visit)
    {
        visit(root);
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
            Walk(System.Windows.Media.VisualTreeHelper.GetChild(root, i), visit);

        // Some Backstage content is built as a logical tree (StackPanel.Children) before being attached
        // to the visual tree; also walk logical children for robustness.
        if (root is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is DependencyObject childDo)
                    Walk(childDo, visit);
            }
        }
    }
}
