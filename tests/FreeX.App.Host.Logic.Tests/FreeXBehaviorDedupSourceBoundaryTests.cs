using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FreeXBehaviorDedupSourceBoundaryTests
{
    [Fact]
    public void FormControlCycling_UsesSharedInteractionServiceInBothShells()
    {
        var host = ReadHost("MainWindow.FormControls.cs");
        var avalonia = ReadAvalonia("MainWindow.FormControls.cs");

        host.Should().Contain("FormControlInteractionService.CreateCommand(");
        avalonia.Should().Contain("FormControlInteractionService.CreateCommand(");
        host.Should().NotContain("AdvanceDropDownSelection");
        avalonia.Should().NotContain("AdvanceAvaloniaDropDownSelection");
        host.Should().NotContain("EstimateListItemCount(");
        avalonia.Should().NotContain("EstimateAvaloniaListItemCount(");
    }

    [Fact]
    public void FormControlHostDispatch_UsesSharedPortableGesturePlan()
    {
        var host = ReadHost("MainWindow.FormControls.cs");
        var avalonia = ReadAvalonia("MainWindow.FormControls.cs");

        host.Should().Contain("new FormControlInteractionRequest(e.Control, e.Gesture, e.ListItemIndex)");
        avalonia.Should().Contain("new FormControlInteractionRequest(control, interaction.Gesture, interaction.ListItemIndex)");
        host.Should().NotContain("FormControlClickRegion");
        avalonia.Should().NotContain("FormControlClickKind");
        host.Should().Contain("FormControlInteractionService.CreateCommand(");
        avalonia.Should().Contain("FormControlInteractionService.CreateCommand(");
        host.Should().NotContain("CreateToggleCheckBoxCommand(");
        avalonia.Should().NotContain("CreateToggleCheckBoxCommand(");
    }

    [Fact]
    public void CalculationOptionsHosts_UseSharedInvariantParser()
    {
        var host = ReadHost("OptionsDialog.xaml.cs");
        var avalonia = ReadAvalonia("MainWindow.Options.cs");

        host.Should().Contain("CalculationOptionsInputParser.TryParseBounds");
        avalonia.Should().Contain("CalculationOptionsInputParser.TryParseBounds");
        host.Should().NotContain("private static bool TryParseMaxIterations");
        host.Should().NotContain("private static bool TryParseMaxChange");
        avalonia.Should().NotContain("private static bool TryParseMaxIterations");
        avalonia.Should().NotContain("private static bool TryParseMaxChange");
    }

    [Fact]
    public void LegacyProtectionHashing_IsOwnedByCoreAndCalledDirectlyByExcelWriters()
    {
        var helper = ReadCoreModel("ProtectionPasswordHelper.cs");
        var workbookWriter = ReadCoreIo("XlsxWorkbookMetadataWriter.cs");
        var sheetWriter = ReadCoreIo("XlsxWorksheetProtectionMetadataWriter.cs");
        var xmlHelper = ReadCoreIo("XlsxWorkbookMetadataXmlHelper.cs");

        helper.Should().Contain("public static string ToLegacyPasswordHash");
        helper.Should().Contain("public static bool IsLegacyPasswordHash");
        workbookWriter.Should().Contain("ProtectionPasswordHelper.ToLegacyPasswordHash");
        sheetWriter.Should().Contain("ProtectionPasswordHelper.ToLegacyPasswordHash");
        xmlHelper.Should().NotContain("ToLegacyPasswordHash");
    }

    [Fact]
    public void AllowEditRangePasswordMutation_IsCoreOwned()
    {
        var core = ReadCoreCommands("SheetProtectionCommands.cs");
        var host = ReadHost("MainWindow.ReviewCommands.cs");
        var avalonia = ReadAvalonia("MainWindow.AllowEditRange.cs");

        core.Should().Contain("public sealed class SetAllowEditRangePasswordCommand");
        host.Should().NotContain("private sealed class SetAllowEditRangePasswordCommand");
        avalonia.Should().NotContain("private sealed class SetAllowEditRangePasswordCommand");
    }

    [Fact]
    public void ClipboardHtmlWriting_IsSharedWhileHostsOwnRegistration()
    {
        var host = ReadHost("MainWindow.ClipboardCommands.cs");
        var avalonia = ReadAvalonia("MainWindow.ClipboardHtml.cs");

        host.Should().Contain("ClipboardHtmlSerializer.Serialize");
        avalonia.Should().Contain("ClipboardHtmlSerializer.Serialize");
        host.Should().NotContain("private static string BuildCellCss");
        avalonia.Should().NotContain("private static string BuildCellCss");
        host.Should().NotContain("private static string WrapAsCfHtml");
        avalonia.Should().NotContain("private static string WrapAsCfHtml");
    }

    [Fact]
    public void OutlineAndDiagnosticsPolicies_AreSharedAcrossShells()
    {
        var hostOutline = ReadHost("MainWindow.OutlineCommands.cs");
        var avaloniaOutline = ReadAvalonia("MainWindow.Outline.cs");
        var hostDiagnostics = ReadHost("AppDiagnostics.cs");
        var avaloniaDiagnostics = ReadAvalonia("AvaloniaAppDiagnostics.cs");

        hostOutline.Should().Contain("_session.UngroupSelectedOutline");
        avaloniaOutline.Should().Contain("_session.UngroupSelectedOutline");
        hostOutline.Should().NotContain("private static int GetUngroupedOutlineLevel");
        avaloniaOutline.Should().NotContain("private static int GetUngroupedOutlineLevel");
        hostDiagnostics.Should().Contain("LocalAppDiagnostics");
        avaloniaDiagnostics.Should().Contain("LocalAppDiagnostics");
        avaloniaDiagnostics.Should().NotContain("AppDiagnosticsFileStore");
        avaloniaDiagnostics.Should().NotContain("_local");
        avaloniaDiagnostics.Should().NotContain("new bool IsEnabled");
        avaloniaDiagnostics.Should().NotContain("new void RecordEvent");
        avaloniaDiagnostics.Should().NotContain("new string RecordCrash");
    }

    private static string ReadHost(string fileName) =>
        DialogSourceTestSupport.ReadHostSourceFile(fileName);

    private static string ReadAvalonia(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Avalonia", fileName);

    private static string ReadCoreCommands(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.Core.Commands", fileName);

    private static string ReadCoreModel(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.Core.Model", fileName);

    private static string ReadCoreIo(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.Core.IO", fileName);
}
