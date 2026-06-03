using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;
using FreeX.App.Host;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

public sealed partial class FormatCellsDialogXamlTests
{
    private static FormatCellsDialog ShowDialogForTest(
        CellStyle current,
        FormatCellsDialogTab initialTab = FormatCellsDialogTab.Number)
    {
        var dialog = new FormatCellsDialog(current, initialTab);
        dialog.Show();
        PumpDispatcher();
        return dialog;
    }

    private static string ReadFormatCellsDialogSource()
    {
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatCellsDialog.xaml.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatCellsDialog.Number.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatCellsNumberControlPlanner.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatCellsNumberFormatPlanner.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatCellsDialog.Font.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatCellsDialog.Fill.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatCellsDialog.Border.cs")));
    }

    private static T GetControl<T>(FormatCellsDialog dialog, string name)
        where T : class
    {
        var field = typeof(FormatCellsDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

    private static string PreviewForFormat(string format)
    {
        var method = typeof(FormatCellsDialog).GetMethod("PreviewForFormat", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return method!.Invoke(null, [format]).Should().BeOfType<string>().Subject;
    }

    private static void ClickOkForTest(FormatCellsDialog dialog)
    {
        var method = typeof(FormatCellsDialog).GetMethod("OkButton_Click", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        try
        {
            method!.Invoke(dialog, [dialog, new System.Windows.RoutedEventArgs()]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException invalidOperation
            && invalidOperation.Message.Contains("DialogResult"))
        {
            // The handler creates ResultDiff before setting DialogResult. Direct invocation on a modeless
            // test window reaches that WPF-only modal postcondition after the behavior under test runs.
        }
    }

    private static void InvokeDialogHandler(FormatCellsDialog dialog, string methodName)
        => InvokeDialogHandler(dialog, methodName, dialog);

    private static void InvokeDialogHandler(FormatCellsDialog dialog, string methodName, object sender)
    {
        var method = typeof(FormatCellsDialog).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(dialog, [sender, new System.Windows.RoutedEventArgs()]);
    }
}
