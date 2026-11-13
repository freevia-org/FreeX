using System.Linq;
using Free.Shared.Ribbon;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// W12: the WPF Print Layout is edited on pages (Word's model), so the WPF ribbon no longer offers a
/// separate Page Edit view (<c>freew.paged-edit-view</c> stays a registered command, which selects Print
/// Layout, and the Avalonia ribbon keeps its opt-in toggle); Print Layout stays the default view mode and
/// the three print-family view modes (PrintLayout / WebLayout / Draft) still work.
/// </summary>
public sealed class PagedEditFlagTests
{
    // ── enum surface ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DocumentViewMode_ContainsFourModes_IncludingPagedEdit()
    {
        // PagedEdit stays an enum value (the Avalonia host's opt-in overlay uses it).
        var allValues = Enum.GetValues<DocumentViewMode>();

        allValues.Should().HaveCount(4,
            "PagedEdit is a shipped mode — four enum values expected in all builds");
        allValues.Should().Contain(DocumentViewMode.PagedEdit,
            "PagedEdit must be present in both Debug and Release builds");
    }

    // ── default mode ──────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void Default_Editor_IsPrintLayout()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());

        view.ViewMode.Should().Be(DocumentViewMode.PrintLayout,
            "Print Layout is the default view (the WPF host shows it on pages)");
    }

    // ── ribbon: Print Layout is the paged view; no separate Page Edit toggle on the WPF ribbon ─────

    [Fact]
    public void WpfViewRibbon_HasPrintLayout_AndNoSeparatePageEditToggle()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var viewTab = definition.FindTab("view");

        viewTab.Should().NotBeNull("the View tab must exist in the ribbon");

        var viewsGroupIds = viewTab!.Groups
            .First(g => g.Id == "views")
            .Controls
            .Select(c => c.CommandId.Value)
            .ToList();

        viewsGroupIds.Should().Contain(["freew.print-layout", "freew.web-layout", "freew.draft-view", "freew.outline-view"]);
        viewTab.Groups.SelectMany(g => g.Controls).Select(c => c.CommandId.Value).Should().NotContain(
            "freew.paged-edit-view",
            "the WPF Print Layout is the paged editor, so a second toggle for it would be redundant");
    }

    [Fact]
    public void AvaloniaViewRibbon_KeepsItsOptInPageEditToggle()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        definition.FindTab("view")!.Groups
            .First(g => g.Id == "views")
            .Controls
            .Select(c => c.CommandId.Value)
            .Should().Contain("freew.paged-edit-view");
    }

    // ── existing view-mode parity tests still pass (regression guard) ─────────────────────────────

    [StaFact]
    public void ExistingViewModes_SetViewMode_StillWork()
    {
        var doc = TextDocument.CreateEmpty();
        var view = new DocumentView();
        view.LoadModel(doc);

        foreach (var mode in new[]
                 {
                     DocumentViewMode.WebLayout,
                     DocumentViewMode.Draft,
                     DocumentViewMode.PrintLayout
                 })
        {
            view.SetViewMode(mode);
            view.ViewMode.Should().Be(mode, $"SetViewMode({mode}) must still work");
        }
    }
}
