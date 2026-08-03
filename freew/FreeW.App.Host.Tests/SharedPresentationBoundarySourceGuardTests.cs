using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class SharedPresentationBoundarySourceGuardTests
{
    [Fact]
    public void PasteSpecialHosts_ConsumeSharedCatalogWithoutReowningOptions()
    {
        var catalog = ReadSource("freew", "FreeW.App.Presentation", "Dialogs", "PasteSpecialOptionCatalog.cs");
        var wpf = ReadSource("freew", "FreeW.App.Host", "PasteSpecialDialog.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "PasteSpecialDialog.cs");

        catalog.Should().Contain("public enum PasteSpecialOption");
        catalog.Should().Contain("public static class PasteSpecialOptionCatalog");
        wpf.Should().Contain("PasteSpecialOptionCatalog.Options");
        avalonia.Should().Contain("PasteSpecialOptionCatalog.Options");
        wpf.Should().NotContain("internal enum PasteSpecialOption");
        avalonia.Should().NotContain("private static readonly OptionRow[] Options");
        avalonia.Should().NotContain("private sealed record OptionRow");
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
