using System.Linq;
using Free.Shared.Ribbon;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Asserts that the <c>PagedEdit</c> mode is a SHIPPED OPT-IN mode that IS present in the production
/// ribbon (View ▸ Views ▸ Page Edit / <c>freew.paged-edit-view</c>), that the continuous editor
/// (PrintLayout) remains the startup default, and that the three continuous print-family view modes
/// (PrintLayout / WebLayout / Draft) still work.  Runs in both DEBUG and Release builds.
/// </summary>
public sealed class PagedEditFlagTests
{
    // ── enum surface ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DocumentViewMode_ContainsFourModes_IncludingPagedEdit()
    {
        // PagedEdit is now a shipped opt-in mode.  The enum must have exactly four members in all
        // builds (DEBUG and Release alike).
        var allValues = Enum.GetValues<DocumentViewMode>();

        allValues.Should().HaveCount(4,
            "PagedEdit is a shipped opt-in mode — four enum values expected in all builds");
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
            "PrintLayout must remain the default; PagedEdit is opt-in and must not change the startup default");
    }

    // ── ribbon: freew.paged-edit-view MUST appear in the View tab's Views group ──────────────────

    [Fact]
    public void ViewRibbon_ContainsPagedEditCommand_InViewsGroup()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var viewTab = definition.FindTab("view");

        viewTab.Should().NotBeNull("the View tab must exist in the ribbon");

        // Collect all command ids from every control on the View tab.
        var allCommandIds = viewTab!.Groups
            .SelectMany(g => g.Controls)
            .Select(c => c.CommandId.Value)
            .ToList();

        allCommandIds.Should().Contain(
            "freew.paged-edit-view",
            "PagedEdit is a shipped opt-in View mode and must appear on the production View ribbon");

        // Specifically it must live in the Views group.
        var viewsGroupIds = viewTab.Groups
            .First(g => g.Id == "views")
            .Controls
            .Select(c => c.CommandId.Value)
            .ToList();

        viewsGroupIds.Should().Contain(
            "freew.paged-edit-view",
            "freew.paged-edit-view must be in the Views group alongside Print Layout / Web Layout / Draft");
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
            view.ViewMode.Should().Be(mode,
                $"SetViewMode({mode}) must still work after the PagedEdit mode was promoted to production");
        }
    }
}
