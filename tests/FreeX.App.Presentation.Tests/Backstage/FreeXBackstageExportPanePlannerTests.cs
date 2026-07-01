using FluentAssertions;
using FreeX.App.Presentation.Backstage;

namespace FreeX.App.Presentation.Tests.Backstage;

public sealed class FreeXBackstageExportPanePlannerTests
{
    private enum ExternalScope
    {
        SelectedRange,
        ActiveSheet,
        VisibleWorkbook
    }

    private enum ExternalOutputKind
    {
        Pdf,
        Xps
    }

    [Fact]
    public void CreateRequest_MapsExternalExportEnumsIntoBackstageRequest()
    {
        var request = FreeXBackstageExportPanePlanner.CreateRequest<ExternalScope, ExternalOutputKind>(
            [
                new(ExternalScope.SelectedRange, IsAvailable: false, IsDefault: false),
                new(ExternalScope.ActiveSheet, IsAvailable: true, IsDefault: true),
                new(ExternalScope.VisibleWorkbook, IsAvailable: true, IsDefault: false),
            ],
            [ExternalOutputKind.Pdf, ExternalOutputKind.Xps],
            ExternalOutputKind.Xps,
            canExport: true);

        request.Scopes.Select(option => (option.Scope, option.IsAvailable, option.IsDefault))
            .Should().Equal(
                (FreeXBackstageExportScopeId.SelectedRange, false, false),
                (FreeXBackstageExportScopeId.ActiveSheet, true, true),
                (FreeXBackstageExportScopeId.VisibleWorkbook, true, false));
        request.OutputKinds.Select(option => (option.OutputKind, option.IsDefault))
            .Should().Equal(
                (FreeXBackstageExportOutputKindId.Pdf, false),
                (FreeXBackstageExportOutputKindId.Xps, true));

        FreeXBackstageExportPanePlanner.ToExternalScope<ExternalScope>(FreeXBackstageExportScopeId.VisibleWorkbook)
            .Should().Be(ExternalScope.VisibleWorkbook);
        FreeXBackstageExportPanePlanner.ToExternalOutputKind<ExternalOutputKind>(FreeXBackstageExportOutputKindId.Pdf)
            .Should().Be(ExternalOutputKind.Pdf);
    }

    [Fact]
    public void Build_ProducesExportSectionsAndOptions()
    {
        var plan = FreeXBackstageExportPanePlanner.Build(new FreeXBackstageExportPaneRequest(
            [
                new(FreeXBackstageExportScopeId.SelectedRange, IsAvailable: false, IsDefault: false),
                new(FreeXBackstageExportScopeId.ActiveSheet, IsAvailable: true, IsDefault: true),
                new(FreeXBackstageExportScopeId.VisibleWorkbook, IsAvailable: true, IsDefault: false),
            ],
            [
                new(FreeXBackstageExportOutputKindId.Pdf, IsDefault: true),
                new(FreeXBackstageExportOutputKindId.Xps, IsDefault: false),
            ],
            CanExport: true));

        plan.CanExport.Should().BeTrue();
        plan.ShowUnavailableNote.Should().BeFalse();
        plan.ScopeHeaderKey.Should().Be("Backstage_Export_ScopeHeader");
        plan.ScopeGroupAutomationId.Should().Be("BackstageExportScope");
        plan.FormatHeaderKey.Should().Be("Backstage_Export_FormatHeader");
        plan.FormatGroupAutomationId.Should().Be("BackstageExportFormat");

        plan.ScopeOptions.Select(option => (option.Scope, option.LabelKey, option.AutomationId, option.IsEnabled, option.IsDefault))
            .Should().Equal(
                (FreeXBackstageExportScopeId.SelectedRange, "Backstage_Export_ScopeSelectionUnavailable", "BackstageExportScope_SelectedRange", false, false),
                (FreeXBackstageExportScopeId.ActiveSheet, "Backstage_Export_ScopeActiveSheet", "BackstageExportScope_ActiveSheet", true, true),
                (FreeXBackstageExportScopeId.VisibleWorkbook, "Backstage_Export_ScopeWorkbook", "BackstageExportScope_VisibleWorkbook", true, false));

        plan.OutputKindOptions.Select(option => (option.OutputKind, option.LabelKey, option.AutomationId, option.IsDefault))
            .Should().Equal(
                (FreeXBackstageExportOutputKindId.Pdf, "Backstage_Export_FormatPdf", "BackstageExportFormat_Pdf", true),
                (FreeXBackstageExportOutputKindId.Xps, "Backstage_Export_FormatXps", "BackstageExportFormat_Xps", false));
    }

    [Fact]
    public void Build_UnavailablePlanShowsUnavailableNote()
    {
        var plan = FreeXBackstageExportPanePlanner.Build(new FreeXBackstageExportPaneRequest(
            [new(FreeXBackstageExportScopeId.ActiveSheet, IsAvailable: false, IsDefault: false)],
            [],
            CanExport: false));

        plan.CanExport.Should().BeFalse();
        plan.ShowUnavailableNote.Should().BeTrue();
        plan.UnavailableNoteKey.Should().Be("Backstage_Export_Unavailable");
        plan.UnavailableAutomationId.Should().Be("BackstageExportUnavailable");
        plan.ScopeOptions.Single().IsEnabled.Should().BeFalse();
        plan.OutputKindOptions.Should().BeEmpty();
    }
}
