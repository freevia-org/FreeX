using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.App.Presentation.Interactions;
using FreeX.App.Presentation.Shell;

namespace FreeX.App.Presentation.Tests.Interactions;

public sealed class InteractionSurfaceCatalogTests
{
    [Fact]
    public void Dialogs_PinTheKnown120LogicalWpfSurfaces()
    {
        InteractionSurfaceCatalog.Dialogs.Should().HaveCount(120);
        InteractionSurfaceCatalog.Dialogs.Select(row => row.Name)
            .Should().OnlyContain(name => !string.IsNullOrWhiteSpace(name))
            .And.OnlyHaveUniqueItems();
        InteractionSurfaceCatalog.Dialogs.Select(row => row.Id)
            .Should().OnlyContain(id => id.StartsWith("dialog.", StringComparison.Ordinal))
            .And.OnlyHaveUniqueItems();
        InteractionSurfaceCatalog.Dialogs.Select(row => row.Name)
            .Should().Contain(["MergeCellsContentWarningDialog", "CommentListWindow"]);
    }

    [Fact]
    public void Dialogs_ClassifyOnlyTheCurrentModelessToolAndFlyoutSurfacesAsModeless()
    {
        InteractionSurfaceCatalog.Dialogs
            .Where(row => row.Modality == InteractionSurfaceModality.Modeless)
            .Select(row => row.Name)
            .Should().BeEquivalentTo(
                "AutoFilterDialog",
                "CommentListWindow",
                "ErrorCheckingDialog",
                "FindReplaceDialog",
                "WatchWindowDialog");

        InteractionSurfaceCatalog.Dialogs
            .Where(row => row.Modality == InteractionSurfaceModality.Modal)
            .Should().HaveCount(115);
    }

    [Fact]
    public void DialogRows_DoNotOverclaimPortableDesktopImplementationParity()
    {
        InteractionSurfaceCatalog.Dialogs.Should().OnlyContain(row =>
            row.Kind == InteractionSurfaceKind.Dialog &&
            row.DialogFamily.HasValue &&
            !row.ContextFamily.HasValue &&
            !string.IsNullOrWhiteSpace(row.Owner) &&
            !string.IsNullOrWhiteSpace(row.Family) &&
            row.Prerequisites.Count > 0 &&
            !string.IsNullOrWhiteSpace(row.Source.CatalogOrPlanner) &&
            row.Platforms.Wpf.IsApplicable == true &&
            row.Platforms.Wpf.Implementation == InteractionImplementationCapability.ManagedSurface &&
            !row.Platforms.PortableDesktop.IsApplicable.HasValue &&
            row.Platforms.PortableDesktop.Implementation == InteractionImplementationCapability.Unverified &&
            row.Platforms.Wpf.NativeBoundary == InteractionNativeBoundary.None &&
            row.Platforms.PortableDesktop.NativeBoundary == InteractionNativeBoundary.None);

        InteractionSurfaceCatalog.Dialogs.Should().OnlyContain(row =>
            row.Expectations.Open == InteractionExpectation.Required &&
            row.Expectations.InitialFocus == InteractionExpectation.Required &&
            row.Expectations.TabTraversal == InteractionExpectation.Required &&
            row.Expectations.EnterSubmit == InteractionExpectation.WhenActionExists &&
            row.Expectations.EscapeCancel == InteractionExpectation.Required &&
            row.Expectations.FocusReturn == InteractionExpectation.Required);
    }

    [Fact]
    public void ContextMenus_RepresentEveryAuthoritativeFamilyExactlyOnce()
    {
        var expectedFamilies = Enum.GetValues<ContextMenuFamily>();

        InteractionSurfaceCatalog.ContextMenus.Should().HaveCount(expectedFamilies.Length);
        InteractionSurfaceCatalog.ContextMenus.Select(row => row.ContextFamily!.Value)
            .Should().BeEquivalentTo(expectedFamilies)
            .And.OnlyHaveUniqueItems();
        InteractionSurfaceCatalog.ContextMenus.Should().OnlyContain(row =>
            row.Kind == InteractionSurfaceKind.ContextMenu &&
            !row.DialogFamily.HasValue &&
            row.ContextFamily.HasValue &&
            row.Modality == InteractionSurfaceModality.Transient &&
            row.Prerequisites.Count > 0 &&
            row.Variants.Count > 0 &&
            row.Source.VariantSources.Count > 0);
    }

    [Fact]
    public void WorksheetFamily_ExposesPlannerOwnedTargetsAndAllEightStateAxes()
    {
        var worksheet = InteractionSurfaceCatalog.ContextMenus
            .Single(row => row.ContextFamily == ContextMenuFamily.WorksheetTargetsAndStateVariants);

        worksheet.Variants.Should().HaveCount(15);
        worksheet.Variants.Select(variant => variant.Name).Should().Contain(
            "Worksheet",
            "Picture",
            "Shape",
            "TextBox",
            "Chart",
            "RowSelection",
            "ColumnSelection",
            "HasThreadedComment",
            "IsThreadedCommentResolved",
            "HasNote",
            "HasHyperlink",
            "HasAutoFilterHeaderTarget",
            "HasDropdownTarget",
            "HasPivotTableTarget",
            "NoteIsShown");
    }

    [Fact]
    public void PlannerBackedContextVariants_AreDerivedFromTheirNeutralCatalogs()
    {
        var autoFilter = InteractionSurfaceCatalog.ContextMenus
            .Single(row => row.ContextFamily == ContextMenuFamily.AutoFilterCriteria);
        var expectedCriteriaCount = Enum.GetValues<AutoFilterMenuFilterKind>()
            .Sum(kind => AutoFilterMenuCatalog.GetCriteriaDescriptors(kind).Count);
        autoFilter.Variants.Should().HaveCount(expectedCriteriaCount);

        var nativeMenu = InteractionSurfaceCatalog.ContextMenus
            .Single(row => row.ContextFamily == ContextMenuFamily.NativeApplicationMenu);
        nativeMenu.Variants.Select(variant => variant.Name)
            .Should().Equal(NativeMenuCatalog.TopLevelMenus.Select(menu => menu.Id.ToString()));
    }

    [Fact]
    public void NativeApplicationMenu_DeclaresTheAvaloniaNativeBoundary()
    {
        var nativeMenu = InteractionSurfaceCatalog.ContextMenus
            .Single(row => row.ContextFamily == ContextMenuFamily.NativeApplicationMenu);

        nativeMenu.Platforms.Wpf.Should().Be(new InteractionPlatformCapability(
            IsApplicable: false,
            InteractionImplementationCapability.NotApplicable));
        nativeMenu.Platforms.PortableDesktop.Should().Be(new InteractionPlatformCapability(
            IsApplicable: true,
            InteractionImplementationCapability.NativeSurface,
            InteractionNativeBoundary.NativeApplicationMenu));
        InteractionSurfaceCatalog.ForPlatform(InteractionPlatform.Wpf).Should().NotContain(nativeMenu);
        InteractionSurfaceCatalog.ForPlatform(InteractionPlatform.PortableDesktop).Should().Contain(nativeMenu);
        InteractionSurfaceCatalog.GetPlatformId(InteractionPlatform.PortableDesktop).Should().Be("avalonia");
    }

    [Fact]
    public void MissingPortableContextFamilies_AreExplicitAndExcludedFromApplicableRows()
    {
        var missingFamilies = new[]
        {
            ContextMenuFamily.PivotField,
            ContextMenuFamily.PivotChart,
            ContextMenuFamily.WaterfallPoint
        };
        var missingRows = InteractionSurfaceCatalog.ContextMenus
            .Where(row => missingFamilies.Contains(row.ContextFamily!.Value))
            .ToArray();

        missingRows.Should().HaveCount(missingFamilies.Length);
        missingRows.Should().OnlyContain(row =>
            row.Platforms.PortableDesktop.IsApplicable == false &&
            row.Platforms.PortableDesktop.Implementation == InteractionImplementationCapability.Missing);
        InteractionSurfaceCatalog.ForPlatform(InteractionPlatform.PortableDesktop)
            .Should().NotContain(row => missingFamilies.Contains(row.ContextFamily ?? default));
    }

    [Fact]
    public void RecentFilesAndQuickAccessToolbar_AreManagedOnPortableDesktop()
    {
        var managedFamilies = new[]
        {
            ContextMenuFamily.RecentFiles,
            ContextMenuFamily.QuickAccessToolbar
        };

        InteractionSurfaceCatalog.ContextMenus
            .Where(row => managedFamilies.Contains(row.ContextFamily!.Value))
            .Should().OnlyContain(row =>
                row.Platforms.PortableDesktop.IsApplicable == true &&
                row.Platforms.PortableDesktop.Implementation == InteractionImplementationCapability.ManagedSurface);
    }

    [Fact]
    public void AllRowsAndVariants_HaveStableUniqueNonblankIds()
    {
        InteractionSurfaceCatalog.Rows.Select(row => row.Id)
            .Should().OnlyContain(id => !string.IsNullOrWhiteSpace(id))
            .And.OnlyHaveUniqueItems();

        var variants = InteractionSurfaceCatalog.ContextMenus.SelectMany(row => row.Variants).ToArray();
        variants.Select(variant => variant.Id)
            .Should().OnlyContain(id => !string.IsNullOrWhiteSpace(id))
            .And.OnlyHaveUniqueItems();
        variants.Select(variant => variant.Name)
            .Should().OnlyContain(name => !string.IsNullOrWhiteSpace(name));
    }
}
