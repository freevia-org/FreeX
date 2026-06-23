using System.IO;
using FluentAssertions;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Source-level dedup gate for the FreeW dialogs: the conversions to the shared
/// <c>Free.Shared.Shell.Wpf</c> helpers (<see cref="Free.Shared.Shell.DialogButtonRowFactory"/>,
/// <see cref="Free.Shared.Shell.DialogFocus"/>, <see cref="Free.Shared.Shell.DialogMessageHelper"/>)
/// must stay converted — no dialog should reintroduce a hand-rolled OK/Cancel button row, a raw
/// <c>MessageBox.Show</c>, or the <c>box.Focus(); box.SelectAll();</c> pattern that the shared helpers
/// replace. Mirrors FreeX's <c>RemainingDialogTests.SharedDialogChrome</c> source gate.
/// </summary>
public sealed class DialogSharedHelperDedupTests
{
    [Theory]
    [InlineData("ImageSizeDialog.cs")]
    [InlineData("PropertiesDialog.cs")]
    [InlineData("DateTimeDialog.cs")]
    [InlineData("StyleDialog.cs")]
    [InlineData("ZoomDialog.cs")]
    public void DialogsWithOkCancelRows_UseSharedButtonRowFactory(string fileName)
    {
        var source = ReadDialogSource(fileName);

        source.Should().Contain("DialogButtonRowFactory.Create(");
        // No hand-rolled accept button literal should remain in these converted dialogs.
        source.Should().NotContain("Content = \"OK\"");
        source.Should().NotContain("Content = \"Cancel\"");
    }

    [Theory]
    [InlineData("StatisticsDialog.cs")]
    [InlineData("AccessibilityReportDialog.cs")]
    public void InformationalDialogs_UseSharedOkOnlyButtonRow(string fileName)
    {
        var source = ReadDialogSource(fileName);

        source.Should().Contain("DialogButtonRowFactory.CreateOkOnly(");
        source.Should().NotContain("Content = \"Close\"");
    }

    [Theory]
    [InlineData("ImageSizeDialog.cs")]
    [InlineData("StyleDialog.cs")]
    public void ConvertedDialogs_RouteWarningsThroughDialogMessageHelper(string fileName)
    {
        var source = ReadDialogSource(fileName);

        source.Should().Contain("DialogMessageHelper.ShowWarning(");
        source.Should().NotContain("MessageBox.Show(");
    }

    [Fact]
    public void AutosaveCoordinator_RoutesRecoveryMessagesThroughDialogMessageHelper()
    {
        var source = ReadDialogSource("AutosaveCoordinator.cs");

        source.Should().Contain("DialogMessageHelper.AskYesNo(");
        source.Should().Contain("DialogMessageHelper.ShowInfo(");
        source.Should().Contain("DialogMessageHelper.ShowMessage(");
        source.Should().Contain("UserMessageButtons.OkCancel");
        source.Should().Contain("UserMessageIcon.Question");
        source.Should().Contain("DialogMessageHelper.ShowError(");
        source.Should().NotContain("MessageBox.Show(");
    }

    [Fact]
    public void ImageSizeDialog_UsesSharedFocusHelper()
    {
        var source = ReadDialogSource("ImageSizeDialog.cs");

        source.Should().Contain("DialogFocus.FocusAndSelect(");
        source.Should().NotContain("_widthBox.SelectAll();");
    }

    private static string ReadDialogSource(string fileName)
    {
        var path = Path.Combine(FindRepositoryRoot(), "freew", "FreeW.App.Host", fileName);
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
