using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ObjectDialogTests
{
    [Fact]
    public void ObjectDialogs_LabelSharedInputHelpersWithTargets()
    {
        var source = ReadObjectDialogSources();

        source.Should().Contain("new Label { Content = label, Target = box");
        source.Should().NotContain("new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) }");
    }

    [Fact]
    public void ObjectDialogs_UseSharedButtonRowsOutsideChartDialogs()
    {
        var objectSource = ReadObjectDialogSources();
        var formatPictureSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatPictureDialog.cs"));
        var namedRangeSource =
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "NamedRangeDialog.xaml.cs")) +
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "NameDefinitionDialog.cs"));
        var shapeGradientSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ShapeGradientDialog.cs"));

        foreach (var source in new[] { objectSource, formatPictureSource, namedRangeSource, shapeGradientSource })
        {
            source.Should().Contain("DialogButtonRowFactory.Create");
            source.Should().NotContain("InsertChartDialog.CreateButtonRow");
        }
    }
}
