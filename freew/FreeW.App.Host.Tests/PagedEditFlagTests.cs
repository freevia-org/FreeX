using System.Linq;
using Free.Shared.Ribbon;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Asserts that the <c>PagedEdit</c> mode is DEV-ONLY and does NOT appear in the production ribbon,
/// and that the three production view modes (PrintLayout / WebLayout / Draft) are still the only ones
/// that the editor default and the View ribbon expose.  Runs in both DEBUG and Release builds.
/// </summary>
public sealed class PagedEditFlagTests
{
    // ── enum surface ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ProductionBuild_DocumentViewMode_ContainsExactlyThreeModes()
    {
        // In a Release build the #if DEBUG block is excluded and the enum has exactly three members.
        // In a Debug build the enum has four — but the fourth must NOT be reachable via the ribbon.
        var allValues = Enum.GetValues<DocumentViewMode>();

#if DEBUG
        allValues.Should().HaveCount(4, "DEBUG build exposes PagedEdit as the fourth enum value");
        allValues.Should().Contain(DocumentViewMode.PagedEdit,
            "PagedEdit is present in DEBUG builds");
#else
        allValues.Should().HaveCount(3,
            "Release build must have exactly three view-mode enum values (PagedEdit excluded)");
#endif
    }

    // ── default mode ──────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void Default_Editor_IsPrintLayout()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());

        view.ViewMode.Should().Be(DocumentViewMode.PrintLayout,
            "PrintLayout must remain the default; PagedEdit must not change it");
    }

    // ── ribbon: PagedEdit must not appear in the View tab's command ids ───────────────────────────

    [Fact]
    public void ViewRibbon_DoesNotContainPagedEditCommand()
    {
        var definition = FreeWRibbon.Build();
        var viewTab = definition.FindTab("view");

        viewTab.Should().NotBeNull("the View tab must exist in the ribbon");

        // Collect all command ids from every control on the View tab.
        var allCommandIds = viewTab!.Groups
            .SelectMany(g => g.Controls)
            .Select(c => c.CommandId.Value)
            .ToList();

        allCommandIds.Should().NotContain(
            id => id.Contains("paged-edit", StringComparison.OrdinalIgnoreCase),
            "PagedEdit is DEV-ONLY and must not appear on the production View ribbon");
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
                $"SetViewMode({mode}) must still work after the PagedEdit flag was added");
        }
    }
}
