using System.IO;
using System.Xml.Linq;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class CustomViewsDialogXamlTests
{
    [Fact]
    public void DialogList_ExposesAccessibleName()
    {
        var xaml = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.Host", "CustomViewsDialog.xaml"));

        xaml.Should().Contain("AutomationProperties.Name=\"Custom views\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"Shows saved workbook views");
    }

    [Fact]
    public void ShowButton_IsDefaultDialogAction()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "CustomViewsDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var showButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name")?.Value == "ShowButton");

        showButton.Attribute("IsDefault")?.Value.Should().Be("True");
    }

    private static string FindWorkspaceFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find {Path.Combine(parts)} from {AppContext.BaseDirectory}");
    }
}
