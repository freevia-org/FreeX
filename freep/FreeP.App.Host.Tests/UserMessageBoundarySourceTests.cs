using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class UserMessageBoundarySourceTests
{
    private static readonly string[] ChartOptionsDialogFiles =
    [
        "Chart3DViewOptionsDialog.cs",
        "ChartAreaOptionsDialog.cs",
        "ChartAxisOptionsDialog.cs",
        "ChartBubbleOptionsDialog.cs",
        "ChartDataTableOptionsDialog.cs",
        "ChartDisplayOptionsDialog.cs",
        "ChartExSeriesLayoutDialog.cs",
        "ChartLayoutOptionsDialog.cs",
        "ChartPieOptionsDialog.cs",
        "ChartPointOptionsDialog.cs",
        "ChartPlotStyleOptionsDialog.cs",
        "ChartProtectionOptionsDialog.cs",
        "ChartSeriesOptionsDialog.cs",
        "ChartTextOptionsDialog.cs",
    ];

    private static readonly string[] DirectWarningDialogFiles =
    [
        "MotionPathEditorDialog.cs",
        "RotationOptionsDialog.cs",
        "ZoomObjectPropertiesDialog.cs",
    ];

    [Fact]
    public void Chart_options_host_owns_native_warning_realization()
    {
        var host = ReadWorkspaceSource("freep", "FreeP.App.Host", "ChartOptionsDialogHost.cs");
        host.Should().Contain("DialogMessageHelper.ShowWarning(this, result.ValidationMessage, Title)");
        host.Should().NotContain("MessageBox.Show(");

        foreach (var fileName in ChartOptionsDialogFiles)
        {
            var source = ReadWorkspaceSource("freep", "FreeP.App.Host", fileName);
            source.Should().Contain("ChartOptionsDialogHost<", fileName);
            source.Should().NotContain("DialogMessageHelper.ShowWarning(", fileName);
            source.Should().NotContain("MessageBox.Show(", fileName);
            source.Should().NotContain("MessageBoxButton", fileName);
            source.Should().NotContain("MessageBoxImage", fileName);
        }
    }

    [Fact]
    public void Direct_warning_dialogs_use_the_shared_owned_warning_realizer()
    {
        foreach (var fileName in DirectWarningDialogFiles)
        {
            var source = ReadWorkspaceSource("freep", "FreeP.App.Host", fileName);
            source.Should().Contain("DialogMessageHelper.ShowWarning(", fileName);
            source.Should().NotContain("MessageBox.Show(", fileName);
            source.Should().NotContain("MessageBoxButton", fileName);
            source.Should().NotContain("MessageBoxImage", fileName);
        }
    }

    [Fact]
    public void Portable_contract_has_no_toolkit_references()
    {
        foreach (var fileName in new[] { "IUserMessageService.cs", "UserMessageDialog.cs" })
        {
            var source = ReadWorkspaceSource("shared", "Free.Shared.AppServices", fileName);
            source.Should().NotContain("System.Windows", fileName);
            source.Should().NotContain("Avalonia", fileName);
        }
    }

    [Fact]
    public void Toolkit_services_delegate_to_the_existing_message_realizers()
    {
        var wpf = ReadWorkspaceSource(
            "shared",
            "Free.Shared.Shell.Wpf",
            "WpfUserMessageService.cs");
        wpf.Should().Contain("DialogMessageHelper.ShowMessage(");
        wpf.Should().NotContain("MessageBox.Show(");

        var avalonia = ReadWorkspaceSource(
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaUserMessageService.cs");
        avalonia.Should().Contain("AvaloniaUserMessageDialog.ShowMessageAsync(");
        avalonia.Should().NotContain("MessageBox.Show(");
    }

    private static string ReadWorkspaceSource(params string[] relativeParts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory(
            "FreeP.slnx");
        var parts = new string[relativeParts.Length + 1];
        parts[0] = root;
        relativeParts.CopyTo(parts, 1);
        return File.ReadAllText(Path.Combine(parts));
    }
}
