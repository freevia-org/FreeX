using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ChartDialogTests
{
    [Fact]
    public void ChartDialogs_LabelEditableHelperControlsWithTargets()
    {
        var source = ReadChartDialogSource();
        var helperSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartDialogHelpers.cs"));

        foreach (var expected in new[]
        {
            "new Label { Content = label, Target = box",
            "new Label { Content = UiText.Get(\"ChartStyle_StyleLabel\"), Target = _styleGallery"
        })
            source.Should().Contain(expected);

        foreach (var expected in new[]
        {
            "new Label { Content = label, Target = comboBox",
            "new Label { Content = label, Target = textBox"
        })
            helperSource.Should().Contain(expected);

        source.Should().NotContain("stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 3, 0, 4) })");
        helperSource.Should().NotContain("stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 3, 0, 4) })");
    }

}
