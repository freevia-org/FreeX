using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class SharedPresentationBoundarySourceGuardTests
{
    [Fact]
    public void DocumentPropertiesHosts_ConsumeSharedCatalogAndCommitPlan_AndApplyThroughEditorHistory()
    {
        var wpfDialog = ReadSource("freew", "FreeW.App.Host", "PropertiesDialog.cs");
        var avaloniaDialog = ReadSource("freew", "FreeW.App.Avalonia", "PropertiesDialog.cs");
        var wpfWindow = ReadSource("freew", "FreeW.App.Host", "MainWindow.cs");
        var avaloniaWindow = ReadSource("freew", "FreeW.App.Avalonia", "MainWindow.cs");

        foreach (var source in new[] { wpfDialog, avaloniaDialog })
        {
            source.Should().Contain("new DocumentPropertiesDialogSession(");
            source.Should().Contain("_session.Surface.Fields");
            source.Should().Contain("_session.PlanCommit(");
            source.Should().NotContain("DocumentPropertiesDialogValues.FromInput(");
            source.Should().NotContain("FormatDate(");
            source.Should().NotContain("\"Last saved by:\"");
            source.Should().NotContain("_properties.Title =");
            source.Should().NotContain("_properties.Author =");
        }

        wpfWindow.Should().Contain("_editor.ApplyDocumentProperties(result);");
        avaloniaWindow.Should().Contain("_editor.ApplyDocumentProperties(result);");
    }

    [Fact]
    public void PasteSpecialHosts_ConsumeSharedSessionWithoutReowningOptionsOrAcceptance()
    {
        var catalog = ReadSource("freew", "FreeW.App.Presentation", "Dialogs", "PasteSpecialOptionCatalog.cs");
        var session = ReadSource("freew", "FreeW.App.Presentation", "Dialogs", "PasteSpecialDialogSession.cs");
        var wpf = ReadSource("freew", "FreeW.App.Host", "PasteSpecialDialog.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "PasteSpecialDialog.cs");

        catalog.Should().Contain("public enum PasteSpecialOption");
        catalog.Should().Contain("public static class PasteSpecialOptionCatalog");
        session.Should().Contain("PasteSpecialOptionCatalog.Options");
        foreach (var host in new[] { wpf, avalonia })
        {
            host.Should().Contain("PasteSpecialDialogSession");
            host.Should().Contain("PlanAcceptance()");
            host.Should().NotContain("PasteSpecialOptionCatalog.Options");
        }
        wpf.Should().NotContain("internal enum PasteSpecialOption");
        avalonia.Should().NotContain("private static readonly OptionRow[] Options");
        avalonia.Should().NotContain("private sealed record OptionRow");
    }

    [Fact]
    public void SimpleDialogHosts_DelegateResultAndCommitSemanticsToPresentation()
    {
        var wpfDateTime = ReadSource("freew", "FreeW.App.Host", "DateTimeDialog.cs");
        var avaloniaDateTime = ReadSource("freew", "FreeW.App.Avalonia", "DateTimeDialog.cs");
        var wpfPassword = ReadSource("freew", "FreeW.App.Host", "PasswordPromptDialog.cs");
        var avaloniaPassword = ReadSource("freew", "FreeW.App.Avalonia", "PasswordPromptDialog.cs");
        var wpfCommands = ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avaloniaCellShading = ReadSource("freew", "FreeW.App.Avalonia", "CellShadingDialog.cs");

        foreach (var host in new[] { wpfDateTime, avaloniaDateTime })
        {
            host.Should().Contain("DateTimeDialogSession");
            host.Should().Contain("_session.PlanAcceptance()");
            host.Should().NotContain("DateTimeFormats.BuildFieldPicture");
            host.Should().NotContain("record DateTimeDialogResult");
        }

        foreach (var host in new[] { wpfPassword, avaloniaPassword })
        {
            host.Should().Contain("PasswordPromptDialogSession");
            host.Should().Contain("_session.PlanAcceptance()");
        }
        wpfPassword.Should().NotContain("_result = _passwordBox.Password");
        avaloniaPassword.Should().NotContain("Result = _passwordBox.Text");

        wpfCommands.Should().Contain("CellShadingDialogPlanner.PlanCommit(result)");
        avaloniaCellShading.Should().Contain("CellShadingDialogPlanner.PlanCommit(result)");
        wpfCommands.Should().NotContain("result is not { Accepted: true }");
        avaloniaCellShading.Should().NotContain("result is not { Accepted: true }");
    }

    [Fact]
    public void DocumentInspectorHosts_ConsumeSharedAnyContract()
    {
        var wpfDialog = ReadSource("freew", "FreeW.App.Host", "DocumentInspectorDialog.cs");
        var avaloniaDialog = ReadSource("freew", "FreeW.App.Avalonia", "SafetyDialogs.cs");
        var avaloniaWindow = ReadSource("freew", "FreeW.App.Avalonia", "MainWindow.cs");

        wpfDialog.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        wpfDialog.Should().Contain("return choice.Any ? choice : null;");
        wpfDialog.Should().NotContain("record InspectorRemovalChoice");
        avaloniaDialog.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        avaloniaDialog.Should().NotContain("record InspectorRemovalChoice");
        avaloniaWindow.Should().Contain("choice.Any");
        avaloniaWindow.Should().NotContain("choice.HasAnySelection");
    }

    [Fact]
    public void DialogActionRows_ConsumeSharedPresentationSemanticsAcrossBothHosts()
    {
        var wpfCrossReference = ReadSource("freew", "FreeW.App.Host", "CrossReferenceDialog.cs");
        var avaloniaCrossReference = ReadSource("freew", "FreeW.App.Avalonia", "ReferencesDialogs.cs");
        var wpfInspector = ReadSource("freew", "FreeW.App.Host", "DocumentInspectorDialog.cs");
        var avaloniaInspector = ReadSource("freew", "FreeW.App.Avalonia", "SafetyDialogs.cs");
        var wpfWatermark = ReadSource("freew", "FreeW.App.Host", "WatermarkOptionsDialog.cs");
        var avaloniaWatermark = ReadSource("freew", "FreeW.App.Avalonia", "DesignDialogs.cs");

        wpfCrossReference.Should().Contain("CrossReferenceDialogPlanner.ActionButtons");
        avaloniaCrossReference.Should().Contain("CrossReferenceDialogPlanner.ActionButtons");
        wpfInspector.Should().Contain("DocumentInspectorDialogPlanner.ActionButtons");
        avaloniaInspector.Should().Contain("DocumentInspectorDialogPlanner.ActionButtons");
        wpfWatermark.Should().Contain("WatermarkOptionsDialogPlanner.ActionButtons");
        avaloniaWatermark.Should().Contain("WatermarkOptionsDialogPlanner.ActionButtons");
        avaloniaWatermark.Should().Contain("okButton.IsDefault = okPlan.IsDefault");
        avaloniaWatermark.Should().Contain("cancelButton.IsCancel = cancelPlan.IsCancel");
    }

    [Fact]
    public void InsertChartHosts_ConsumeSharedActionPlan()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "InsertChartDialog.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "MediaDialogParity.cs");

        wpf.Should().Contain("InsertChartDialogPlanner.BuildActionButtons(UiText.Get)");
        avalonia.Should().Contain("InsertChartDialogPlanner.BuildActionButtons(UiText.Get)");
        wpf.Should().Contain("acceptContent: actionPlans[0].Label");
        wpf.Should().Contain("cancelContent: actionPlans[1].Label");
        avalonia.Should().Contain("isDefault: actionPlans[0].IsDefault");
        avalonia.Should().Contain("isCancel: actionPlans[1].IsCancel");
    }

    [Fact]
    public void Focus_routes_consume_shared_targets_and_keep_WPF_keyboard_hooks()
    {
        var wpfSources = new[]
        {
            ReadSource("freew", "FreeW.App.Host", "CompareDocumentsDialog.cs"),
            ReadSource("freew", "FreeW.App.Host", "PropertiesDialog.cs"),
            ReadSource("freew", "FreeW.App.Host", "TableFormulaDialog.cs"),
            ReadSource("freew", "FreeW.App.Host", "ZoomDialog.cs"),
        };
        var avaloniaSources = new[]
        {
            ReadSource("freew", "FreeW.App.Avalonia", "ReviewCompareCombineDialogs.cs"),
            ReadSource("freew", "FreeW.App.Avalonia", "PropertiesDialog.cs"),
            ReadSource("freew", "FreeW.App.Avalonia", "TableDialogs.cs"),
            ReadSource("freew", "FreeW.App.Avalonia", "ZoomDialog.cs"),
        };

        foreach (var source in wpfSources.Concat(avaloniaSources))
        {
            source.Should().Contain("FreeWDialogFocusPlanner");
            source.Should().Contain("FocusPlan.SelectAllOnFocus");
        }

        wpfSources.Should().OnlyContain(source => source.Contains("FocusAndSelect", StringComparison.Ordinal));
        avaloniaSources.Should().OnlyContain(source => source.Contains("FocusAndSelect", StringComparison.Ordinal));
    }

    [Fact]
    public void PlatformDocumentViews_ConsumeSharedReviewDisplayStateTransitions()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ReviewDisplayState _reviewDisplayState");
            source.Should().Contain("CurrentReviewDisplayState");
            source.Should().Contain("_reviewDisplayState.ToPolicy()");
            source.Should().Contain("WithDisplayMode(");
            source.Should().Contain("WithShowInsertionsAndDeletions(");
            source.Should().Contain("WithShowComments(");
            source.Should().Contain("WithShowFormatting(");
            source.Should().NotContain("new(DisplayForReview, ShowMarkupInsertionsAndDeletions");
        }
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx") }.Concat(parts).ToArray()));

}
