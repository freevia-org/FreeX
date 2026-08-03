using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace FreeW.App.Host.Tests;

public sealed class BordersAndShadingDialogVisualParityTests
{
    [StaFact]
    public void Constructor_seeds_edge_checks_like_the_route_authority_capture()
    {
        var dialogType = Assembly.Load("FreeW.App.Host")
            .GetType("FreeW.App.Host.BordersAndShadingDialog", throwOnError: true)!;
        var constructor = dialogType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Window), typeof(FreeW.Core.Model.ParagraphFormatting), typeof(FreeW.Core.Model.PageBorder)],
            modifiers: null)!;
        var dialog = (Window)constructor.Invoke([null, FreeW.Core.Model.ParagraphFormatting.Default, null]);

        try
        {
            foreach (var fieldName in new[] { "_top", "_left", "_bottom", "_right" })
            {
                var check = (CheckBox)dialogType
                    .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(dialog)!;
                check.IsChecked.Should().BeTrue();
                check.IsEnabled.Should().BeTrue();
            }
        }
        finally
        {
            dialog.Close();
        }

        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew", "FreeW.App.Host", "BordersAndShadingDialog.cs"));
        source.Should().Contain("dialog.ApplyParagraphSetting();");
    }
}
