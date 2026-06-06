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
        var formatPictureSource = DialogSourceTestSupport.ReadHostSources("FormatPictureDialog.cs");
        var namedRangeSource =
            DialogSourceTestSupport.ReadHostSources("NamedRangeDialog.xaml.cs") +
            DialogSourceTestSupport.ReadHostSources("NameDefinitionDialog.cs");
        var shapeGradientSource = DialogSourceTestSupport.ReadHostSources("ShapeGradientDialog.cs");

        foreach (var source in new[] { objectSource, formatPictureSource, namedRangeSource, shapeGradientSource })
        {
            source.Should().Contain("DialogButtonRowFactory.Create");
            source.Should().NotContain("InsertChartDialog.CreateButtonRow");
        }
    }
}
