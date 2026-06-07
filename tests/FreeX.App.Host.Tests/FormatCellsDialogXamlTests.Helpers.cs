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
        return DialogSourceTestSupport.ReadHostSources(
            "FormatCellsDialog.xaml.cs",
            "FormatCellsDialog.Number.cs",
            "FormatCellsNumberControlPlanner.cs",
            "FormatCellsNumberFormatPlanner.cs",
            "FormatCellsDialog.Font.cs",
            "FormatCellsDialog.Fill.cs",
            "FormatCellsDialog.Border.cs");
    }

    private static T GetControl<T>(FormatCellsDialog dialog, string name)
        where T : class
        => DialogSourceTestSupport.GetPrivateField<T>(dialog, name);

    private static string PreviewForFormat(string format)
    {
        var method = typeof(FormatCellsDialog).GetMethod("PreviewForFormat", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return method!.Invoke(null, [format]).Should().BeOfType<string>().Subject;
    }

    private static void ClickOkForTest(FormatCellsDialog dialog)
        => DialogSourceTestSupport.InvokePrivateHandlerAllowingNonModalDialogResult(dialog, "OkButton_Click");

    private static void InvokeDialogHandler(FormatCellsDialog dialog, string methodName)
        => InvokeDialogHandler(dialog, methodName, dialog);

    private static void InvokeDialogHandler(FormatCellsDialog dialog, string methodName, object sender)
        => DialogSourceTestSupport.InvokePrivateHandler(dialog, methodName, sender);
}
